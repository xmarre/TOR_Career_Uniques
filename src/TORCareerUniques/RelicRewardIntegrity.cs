using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace TORCareerUniques
{
    public sealed class RelicRewardIntegritySubModule : MBSubModuleBase
    {
        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            RelicRewardIntegrity.Initialize();
        }

        protected override void OnApplicationTick(float dt)
        {
            base.OnApplicationTick(dt);
            RelicRewardIntegrity.Tick(dt);
        }

        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            RelicRewardIntegrity.ResetSession();
            base.OnBeforeInitialModuleScreenSetAsRoot();
        }
    }

    internal static class RelicRewardIntegrity
    {
        private const string HarmonyId =
            "torcareeruniques.rewards.inventory-integrity.1.7.41.v2";

        private static readonly List<PendingGrantAudit> PendingAudits =
            new List<PendingGrantAudit>();

        private static bool _installed;
        private static bool _restoring;
        private static bool _screenDeferralLogged;
        private static object _campaignSession;

        private sealed class InsertState
        {
            internal bool Verifiable;
            internal int Before;
        }

        private sealed class PendingGrantAudit
        {
            internal ItemObject Item;
            internal ItemModifier Modifier;
            internal int ExpectedGlobalCount;
            internal string CareerId;
            internal float Delay;
            internal int Attempts;
        }

        private sealed class RelicOccurrence
        {
            internal ItemObject Item;
            internal ItemModifier Modifier;
            internal ItemRoster Roster;
            internal EquipmentElement Element;
            internal int Amount;
            internal string Location;
            internal bool IsMainRoster;
            internal bool IsEquipment;
        }

        internal static void Initialize()
        {
            if (_installed)
                return;

            try
            {
                Harmony harmony = new Harmony(HarmonyId);
                MethodInfo insertTarget = AccessTools.Method(
                    typeof(CareerUniqueRuntime), "AddToRoster",
                    new[]
                    {
                        typeof(object), typeof(object), typeof(object),
                        typeof(int), typeof(string).MakeByRefType()
                    });
                MethodInfo insertPrefix = AccessTools.Method(
                    typeof(RelicRewardIntegrity), nameof(BeforeAddToRoster));
                MethodInfo insertPostfix = AccessTools.Method(
                    typeof(RelicRewardIntegrity), nameof(AfterAddToRoster));
                MethodInfo applicationTickTarget = AccessTools.Method(
                    typeof(UniqueEncounterBehavior), "ProcessApplicationTick",
                    new[] { typeof(float) });
                MethodInfo applicationTickPrefix = AccessTools.Method(
                    typeof(RelicRewardIntegrity),
                    nameof(BeforeEncounterApplicationTick));
                if (insertTarget == null || insertPrefix == null ||
                    insertPostfix == null || applicationTickTarget == null ||
                    applicationTickPrefix == null)
                {
                    throw new MissingMethodException(
                        "The TORCU reward delivery path could not be resolved.");
                }

                harmony.Patch(insertTarget,
                    prefix: new HarmonyMethod(insertPrefix)
                    {
                        priority = Priority.First
                    },
                    postfix: new HarmonyMethod(insertPostfix)
                    {
                        priority = Priority.Last
                    });
                harmony.Patch(applicationTickTarget,
                    prefix: new HarmonyMethod(applicationTickPrefix)
                    {
                        priority = Priority.First
                    });
                _installed = true;
                ModLog.AlwaysInfo(
                    "Installed reward inventory integrity verification, global " +
                    "post-loot ownership auditing, and inventory-screen reward " +
                    "deferral. Existing relics are never moved between owners.");
            }
            catch (Exception ex)
            {
                ModLog.Error("Reward inventory integrity verification could not be " +
                    "installed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        internal static void ResetSession()
        {
            PendingAudits.Clear();
            _restoring = false;
            _screenDeferralLogged = false;
            _campaignSession = null;
        }

        internal static void Tick(float dt)
        {
            object campaign = Campaign.Current;
            if (!Object.ReferenceEquals(_campaignSession, campaign))
            {
                PendingAudits.Clear();
                _restoring = false;
                _screenDeferralLogged = false;
                _campaignSession = campaign;
            }
            if (campaign == null || PendingAudits.Count == 0 ||
                IsInventoryTransactionScreenActive() ||
                IsPlayerStillInMapEvent())
                return;

            for (int i = PendingAudits.Count - 1; i >= 0; i--)
            {
                PendingGrantAudit audit = PendingAudits[i];
                audit.Delay -= Math.Max(0f, dt);
                if (audit.Delay > 0f)
                    continue;

                int globalCount;
                string locations;
                string scanError;
                if (!TryCountExactStackGlobally(audit.Item, audit.Modifier,
                    out globalCount, out locations, out scanError))
                {
                    audit.Attempts++;
                    if (audit.Attempts >= 3)
                    {
                        ModLog.Error("Post-loot ownership audit for '" +
                            DescribeItem(audit.Item, audit.Modifier) +
                            "' was abandoned after three bounded scan failures: " +
                            scanError + ". No item was moved or duplicated.");
                        PendingAudits.RemoveAt(i);
                    }
                    else
                    {
                        audit.Delay = 1f;
                    }
                    continue;
                }

                if (globalCount >= audit.ExpectedGlobalCount)
                {
                    ModLog.Info("Post-loot ownership audit retained '" +
                        DescribeItem(audit.Item, audit.Modifier) + "' at " +
                        (String.IsNullOrEmpty(locations) ?
                            "an owned inventory or equipment slot" : locations) +
                        ". No item was moved or duplicated.");
                    PendingAudits.RemoveAt(i);
                    continue;
                }

                int missing = audit.ExpectedGlobalCount - globalCount;
                string restoreError;
                if (RestoreExactAuditedStack(audit.Item, audit.Modifier,
                    missing, out restoreError))
                {
                    ModLog.AlwaysInfo("Post-loot ownership audit restored " +
                        missing + " missing stack(s) of '" +
                        DescribeItem(audit.Item, audit.Modifier) +
                        "' after the transaction boundary deleted them. " +
                        "No existing owner was modified.");
                    PendingAudits.RemoveAt(i);
                }
                else
                {
                    audit.Attempts++;
                    if (audit.Attempts >= 3)
                    {
                        ModLog.Error("Post-loot ownership audit could not restore '" +
                            DescribeItem(audit.Item, audit.Modifier) +
                            "' after three bounded attempts: " + restoreError +
                            ". Use the recovery button in the existing TOR Career " +
                            "Uniques MCM page and retain the log.");
                        PendingAudits.RemoveAt(i);
                    }
                    else
                    {
                        audit.Delay = 1f;
                    }
                }
            }
        }

        private static bool BeforeEncounterApplicationTick(object __instance)
        {
            if (!HasPendingRewardUiWork(__instance) ||
                !IsInventoryTransactionScreenActive())
            {
                _screenDeferralLogged = false;
                return true;
            }

            if (!_screenDeferralLogged)
            {
                _screenDeferralLogged = true;
                ModLog.Info("Deferred relic aftermath UI until the active " +
                    "inventory/loot transaction screen closes. This prevents its " +
                    "older roster snapshot from discarding a newly granted relic.");
            }
            return false;
        }

        private static bool HasPendingRewardUiWork(object behavior)
        {
            ICollection pending = GetField(behavior, "_pendingRewards") as
                ICollection;
            if (pending != null && pending.Count > 0)
                return true;
            string result = Convert.ToString(GetField(behavior,
                "_pendingResultText"));
            return !String.IsNullOrEmpty(result);
        }

        private static void BeforeAddToRoster(object roster, object item,
            object modifier, int count, out InsertState __state)
        {
            __state = new InsertState();
            if (count <= 0)
                return;

            bool supported;
            __state.Before = CountExactStack(roster, item, modifier,
                out supported);
            __state.Verifiable = supported;
        }

        private static void AfterAddToRoster(object roster, object item,
            object modifier, int count, ref string error, ref bool __result,
            InsertState __state)
        {
            if (!__result || count <= 0 || __state == null ||
                !__state.Verifiable)
                return;

            bool supported;
            int after = CountExactStack(roster, item, modifier, out supported);
            if (!supported || after < __state.Before + count)
            {
                string name = Convert.ToString(GetProperty(item, "Name"));
                string id = Convert.ToString(GetProperty(item, "StringId"));
                error = "Inventory insertion did not retain the granted item " +
                    "stack for '" +
                    (String.IsNullOrWhiteSpace(name) ? id : name) +
                    "' (item=" + (id ?? "<no-id>") + ", before=" +
                    __state.Before + ", after=" + after + ", requested=" +
                    count + ").";
                __result = false;

                if (__state.Before == 0 && after == 0 && count == 1)
                    RollBackUnownedRuntimeDuplicate(item);

                ModLog.Error(error +
                    " Recovery/discovery state was not advanced.");
                return;
            }

            if (_restoring || !IsMainPartyRoster(roster))
                return;

            ItemObject relic = item as ItemObject;
            ItemModifier relicModifier = modifier as ItemModifier;
            string careerId;
            if (relic != null &&
                (modifier == null || relicModifier != null) &&
                TryGetRealRelicCareer(relic, out careerId))
            {
                QueuePostLootAudit(relic, relicModifier, count, careerId);
            }
        }

        private static void QueuePostLootAudit(ItemObject item,
            ItemModifier modifier, int addedCount, string careerId)
        {
            int globalBefore;
            string ignoredLocations;
            string ignoredError;
            if (!TryCountExactStackGlobally(item, modifier, out globalBefore,
                out ignoredLocations, out ignoredError))
            {
                globalBefore = CountExactStack(
                    MobileParty.MainParty == null ? null :
                        MobileParty.MainParty.ItemRoster,
                    item, modifier, out _);
            }

            int expected = Math.Max(addedCount, globalBefore);
            for (int i = 0; i < PendingAudits.Count; i++)
            {
                PendingGrantAudit existing = PendingAudits[i];
                if (Object.ReferenceEquals(existing.Item, item) &&
                    Object.ReferenceEquals(existing.Modifier, modifier))
                {
                    existing.ExpectedGlobalCount = Math.Max(
                        existing.ExpectedGlobalCount, expected);
                    existing.Delay = 1f;
                    existing.Attempts = 0;
                    return;
                }
            }

            PendingAudits.Add(new PendingGrantAudit
            {
                Item = item,
                Modifier = modifier,
                ExpectedGlobalCount = expected,
                CareerId = careerId,
                Delay = 1f,
                Attempts = 0
            });
        }

        private static bool RestoreExactAuditedStack(ItemObject item,
            ItemModifier modifier, int count, out string error)
        {
            error = null;
            if (item == null || count <= 0 || MobileParty.MainParty == null ||
                MobileParty.MainParty.ItemRoster == null)
            {
                error = "The exact audited item or main inventory is unavailable.";
                return false;
            }

            bool added;
            _restoring = true;
            try
            {
                added = CareerUniqueRuntime.AddToRoster(
                    MobileParty.MainParty.ItemRoster, item, modifier, count,
                    out error);
            }
            finally
            {
                _restoring = false;
            }
            return added;
        }

        private static bool TryFindExactStack(ItemRoster roster,
            ItemObject item, ItemModifier modifier,
            out EquipmentElement found)
        {
            found = default(EquipmentElement);
            if (roster == null || item == null)
                return false;
            foreach (ItemRosterElement element in roster)
            {
                EquipmentElement equipment = element.EquipmentElement;
                if (element.Amount > 0 &&
                    Object.ReferenceEquals(equipment.Item, item) &&
                    Object.ReferenceEquals(equipment.ItemModifier, modifier))
                {
                    found = equipment;
                    return true;
                }
            }
            return false;
        }

        private static int CountExactStack(object rosterObject,
            object itemObject, object modifierObject, out bool supported)
        {
            supported = false;
            ItemRoster roster = rosterObject as ItemRoster;
            ItemObject item = itemObject as ItemObject;
            ItemModifier modifier = modifierObject as ItemModifier;
            if (roster == null || item == null ||
                (modifierObject != null && modifier == null))
                return 0;

            int count = 0;
            foreach (ItemRosterElement element in roster)
            {
                EquipmentElement equipment = element.EquipmentElement;
                if (Object.ReferenceEquals(equipment.Item, item) &&
                    Object.ReferenceEquals(equipment.ItemModifier, modifier))
                    count += Math.Max(0, element.Amount);
            }
            supported = true;
            return count;
        }

        private static bool IsMainPartyRoster(object roster)
        {
            return MobileParty.MainParty != null &&
                Object.ReferenceEquals(roster,
                    MobileParty.MainParty.ItemRoster);
        }

        private static bool IsInventoryTransactionScreenActive()
        {
            try
            {
                Type manager = AccessTools.TypeByName(
                    "TaleWorlds.ScreenSystem.ScreenManager");
                PropertyInfo top = manager == null ? null :
                    AccessTools.Property(manager, "TopScreen");
                object screen = top == null ? null :
                    top.GetValue(null, null);
                string name = screen == null ? String.Empty :
                    screen.GetType().FullName ?? String.Empty;
                return name.IndexOf("InventoryScreen",
                           StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("LootScreen",
                        StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("TradeScreen",
                        StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsPlayerStillInMapEvent()
        {
            try
            {
                return MobileParty.MainParty != null &&
                    MobileParty.MainParty.MapEvent != null;
            }
            catch
            {
                return true;
            }
        }

        private static void RollBackUnownedRuntimeDuplicate(object itemObject)
        {
            ItemObject item = itemObject as ItemObject;
            if (item == null)
                return;

            try
            {
                object artisan = CareerUniqueRuntime.GetArtisanBehavior();
                IDictionary crafted = artisan == null ? null :
                    AccessTools.Field(artisan.GetType(), "_customCraftedItems")
                        ?.GetValue(artisan) as IDictionary;
                if (crafted == null || !crafted.Contains(item))
                    return;

                crafted.Remove(item);
                Type managerType = AccessTools.TypeByName(
                    "TOR_Core.Items.ExtendedItemObjectManager");
                string itemId = item.StringId;

                IDictionary infoMap = managerType == null ? null :
                    AccessTools.Field(managerType, "_itemToInfoMap")
                        ?.GetValue(null) as IDictionary;
                if (infoMap != null && !String.IsNullOrEmpty(itemId))
                    infoMap.Remove(itemId);

                object duplicatedIds = managerType == null ? null :
                    AccessTools.Field(managerType,
                        "_runtimeDuplicatedItemIds")?.GetValue(null);
                MethodInfo remove = duplicatedIds == null ? null :
                    duplicatedIds.GetType().GetMethod("Remove",
                        BindingFlags.Public | BindingFlags.Instance, null,
                        new[] { typeof(string) }, null);
                if (remove != null && !String.IsNullOrEmpty(itemId))
                    remove.Invoke(duplicatedIds, new object[] { itemId });

                ModLog.Info("Rolled back failed unowned runtime item " +
                    "registration for " + (itemId ?? "<no-id>") + ".");
            }
            catch (Exception ex)
            {
                ModLog.Error("Failed reward registration rollback was " +
                    "incomplete: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        internal static void RepairFromMcm()
        {
            string result = RepairMissingRecoveredRelics();
            InquiryHelper.ShowMessage("Relic Reward Recovery", result);
        }

        private static string RepairMissingRecoveredRelics()
        {
            if (Campaign.Current == null || MobileParty.MainParty == null ||
                MobileParty.MainParty.ItemRoster == null)
            {
                return "No campaign with a player inventory is currently " +
                    "loaded.";
            }

            List<string> repaired = new List<string>();
            List<string> preserved = new List<string>();
            List<string> cleaned = new List<string>();
            List<string> failed = new List<string>();
            string[] careers = SetItemRuntime.GetCareerIds();
            for (int i = 0; i < careers.Length; i++)
            {
                string careerId = careers[i];
                if (!AdminBridge.HasDiscoveredSetPiece(careerId, 0))
                    continue;

                List<RelicOccurrence> occurrences;
                string scanError;
                if (!TryScanRelicOccurrences(careerId, out occurrences,
                    out scanError))
                {
                    failed.Add(careerId + ": " + scanError);
                    continue;
                }

                string cleanedName;
                if (TryRemovePreviousRecoveryDuplicate(careerId, occurrences,
                    out cleanedName))
                {
                    cleaned.Add(cleanedName);
                    if (!TryScanRelicOccurrences(careerId, out occurrences,
                        out scanError))
                    {
                        failed.Add(careerId + ": post-cleanup scan failed: " +
                            scanError);
                        continue;
                    }
                }

                if (occurrences.Count > 0)
                {
                    preserved.Add(CareerUniqueRuntime.GetItemName(careerId) +
                        " already exists at " +
                        FormatOccurrenceLocations(occurrences));
                    continue;
                }

                string restoredName;
                string source;
                string restoreError;
                if (TryRestoreMissingRelic(careerId, out restoredName,
                    out source, out restoreError))
                {
                    repaired.Add(restoredName + " (" + source + ")");
                }
                else
                {
                    failed.Add(careerId + ": " + restoreError);
                }
            }

            SetItemRuntime.Tick();
            List<string> sections = new List<string>();
            if (cleaned.Count > 0)
            {
                sections.Add("Removed duplicate(s) created by the previous faulty " +
                    "recovery build: " + String.Join(", ", cleaned.ToArray()) +
                    ". The original copies were left on their existing characters.");
            }
            if (repaired.Count > 0)
            {
                sections.Add("Restored genuinely missing recovered relic(s): " +
                    String.Join(", ", repaired.ToArray()) + ".");
            }
            if (preserved.Count > 0)
            {
                sections.Add("Existing relics were not moved or duplicated: " +
                    String.Join("; ", preserved.ToArray()) + ".");
            }
            if (failed.Count > 0)
            {
                sections.Add("Could not safely repair: " +
                    String.Join("; ", failed.ToArray()) + ".");
            }
            if (sections.Count == 0)
                sections.Add("No recovered relic required repair.");

            string result = String.Join("\n\n", sections.ToArray());
            ModLog.AlwaysInfo(result.Replace("\n", " "));
            return result;
        }

        private static bool TryRemovePreviousRecoveryDuplicate(
            string careerId, List<RelicOccurrence> occurrences,
            out string removedName)
        {
            removedName = null;
            if (occurrences == null)
                return false;

            RelicOccurrence main = null;
            int mainAmount = 0;
            bool exactObjectExistsOutsideMain = false;
            for (int i = 0; i < occurrences.Count; i++)
            {
                RelicOccurrence occurrence = occurrences[i];
                if (occurrence.IsMainRoster)
                {
                    mainAmount += Math.Max(0, occurrence.Amount);
                    if (main == null)
                        main = occurrence;
                }
            }
            if (main == null || mainAmount != 1 || main.Amount != 1 ||
                main.Roster == null)
                return false;

            for (int i = 0; i < occurrences.Count; i++)
            {
                RelicOccurrence occurrence = occurrences[i];
                if (!occurrence.IsMainRoster &&
                    Object.ReferenceEquals(occurrence.Item, main.Item))
                {
                    exactObjectExistsOutsideMain = true;
                    break;
                }
            }
            if (!exactObjectExistsOutsideMain)
                return false;

            try
            {
                main.Roster.AddToCounts(main.Element, -1);
                removedName = DescribeItem(main.Item, main.Modifier);
                ModLog.AlwaysInfo("Removed erroneous prior recovery duplicate '" +
                    removedName + "' from the active main inventory. The original " +
                    "runtime item at " + FirstExternalLocation(occurrences,
                        main.Item) + " was left untouched.");
                return true;
            }
            catch (Exception ex)
            {
                ModLog.Error("Could not remove prior recovery duplicate for " +
                    careerId + ": " + ex.GetType().Name + ": " + ex.Message);
                return false;
            }
        }

        private static string FirstExternalLocation(
            List<RelicOccurrence> occurrences, ItemObject item)
        {
            for (int i = 0; i < occurrences.Count; i++)
            {
                if (!occurrences[i].IsMainRoster &&
                    Object.ReferenceEquals(occurrences[i].Item, item))
                    return occurrences[i].Location;
            }
            return "another character or storage owner";
        }

        private static bool TryRestoreMissingRelic(string careerId,
            out string restoredName, out string source, out string error)
        {
            restoredName = CareerUniqueRuntime.GetItemName(careerId);
            source = null;
            error = null;

            ItemObject saved = FindSavedRelicItem(careerId);
            if (saved != null)
            {
                ItemModifier modifier =
                    CareerUniqueRuntime.RollLootModifier(saved) as ItemModifier;
                bool added;
                _restoring = true;
                try
                {
                    added = CareerUniqueRuntime.AddToRoster(
                        MobileParty.MainParty.ItemRoster, saved, modifier, 1,
                        out error);
                }
                finally
                {
                    _restoring = false;
                }
                if (!added)
                    return false;
                restoredName = DescribeItem(saved, modifier);
                source = "restored from TOR's saved relic record";
            }
            else
            {
                bool granted;
                _restoring = true;
                try
                {
                    granted =
                        CareerUniqueRuntime.TryGrantCareerItemWithLootModifier(
                            careerId, out restoredName, out error);
                }
                finally
                {
                    _restoring = false;
                }
                if (!granted)
                    return false;
                source = "recreated because the old runtime record was missing";
            }

            List<RelicOccurrence> verification;
            string scanError;
            if (!TryScanRelicOccurrences(careerId, out verification,
                out scanError))
            {
                error = "the relic was restored, but post-restore ownership " +
                    "verification failed: " + scanError +
                    ". Do not repeat the repair before the log is checked.";
                return false;
            }
            for (int i = 0; i < verification.Count; i++)
            {
                if (verification[i].IsMainRoster)
                    return true;
            }
            error = "the relic was restored, but it was not retained in the " +
                "active main inventory";
            return false;
        }

        private static ItemObject FindSavedRelicItem(string careerId)
        {
            object artisan = CareerUniqueRuntime.GetArtisanBehavior();
            IDictionary crafted = artisan == null ? null :
                AccessTools.Field(artisan.GetType(), "_customCraftedItems")
                    ?.GetValue(artisan) as IDictionary;
            if (crafted == null)
                return null;

            foreach (DictionaryEntry entry in crafted)
            {
                ItemObject item = entry.Key as ItemObject;
                string foundCareer;
                if (item != null &&
                    TryGetRealRelicCareer(item, entry.Value, out foundCareer) &&
                    String.Equals(foundCareer, careerId,
                        StringComparison.OrdinalIgnoreCase))
                    return item;
            }
            return null;
        }

        private static bool TryScanRelicOccurrences(string careerId,
            out List<RelicOccurrence> occurrences, out string error)
        {
            occurrences = new List<RelicOccurrence>();
            error = null;
            if (String.IsNullOrEmpty(careerId))
            {
                error = "career id is empty";
                return false;
            }

            try
            {
                HashSet<ItemRoster> visitedRosters = new HashSet<ItemRoster>();
                ItemRoster mainRoster = MobileParty.MainParty == null ? null :
                    MobileParty.MainParty.ItemRoster;
                AddRosterOccurrences(mainRoster, "active main inventory",
                    true, careerId, occurrences, visitedRosters);

                foreach (MobileParty party in MobileParty.All)
                {
                    if (party == null || party.ItemRoster == null)
                        continue;
                    string partyName = party.Name == null ? party.StringId :
                        party.Name.ToString();
                    AddRosterOccurrences(party.ItemRoster,
                        "party " + partyName + " (" +
                        (party.StringId ?? "<no-id>") + ")",
                        Object.ReferenceEquals(party.ItemRoster, mainRoster),
                        careerId, occurrences, visitedRosters);
                }

                foreach (Settlement settlement in Settlement.All)
                {
                    if (settlement == null)
                        continue;
                    string settlementName = settlement.Name == null ?
                        settlement.StringId : settlement.Name.ToString();
                    AddRosterOccurrences(settlement.ItemRoster,
                        "settlement inventory at " + settlementName,
                        false, careerId, occurrences, visitedRosters);
                    AddRosterOccurrences(settlement.Stash,
                        "stash at " + settlementName,
                        false, careerId, occurrences, visitedRosters);
                    AddRosterOccurrences(settlement.Party == null ? null :
                        settlement.Party.ItemRoster,
                        "settlement party inventory at " + settlementName,
                        false, careerId, occurrences, visitedRosters);
                }

                IEnumerable heroes;
                if (!TryGetAllReferencedHeroes(out heroes))
                {
                    error = "Bannerlord's complete hero registry could not " +
                        "be read; repair was aborted to avoid duplicating a relic on " +
                        "another playable character.";
                    return false;
                }

                HashSet<Hero> visitedHeroes = new HashSet<Hero>();
                if (Hero.MainHero != null)
                    visitedHeroes.Add(Hero.MainHero);
                foreach (object value in heroes)
                {
                    Hero hero = value as Hero;
                    if (hero != null)
                        visitedHeroes.Add(hero);
                }
                foreach (Hero hero in visitedHeroes)
                {
                    string heroName = hero.Name == null ? hero.StringId :
                        hero.Name.ToString();
                    if (!AddEquipmentOccurrences(hero.BattleEquipment,
                        heroName + " battle equipment", careerId, occurrences) ||
                        !AddEquipmentOccurrences(hero.CivilianEquipment,
                        heroName + " civilian equipment", careerId, occurrences))
                    {
                        error = "equipment for " + heroName +
                            " could not be scanned; repair was aborted to avoid a " +
                            "duplicate.";
                        return false;
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                ModLog.Error("World-wide relic ownership scan failed for " +
                    careerId + ": " + error);
                return false;
            }
        }

        private static void AddRosterOccurrences(ItemRoster roster,
            string location, bool isMainRoster, string careerId,
            List<RelicOccurrence> occurrences,
            HashSet<ItemRoster> visitedRosters)
        {
            if (roster == null || !visitedRosters.Add(roster))
                return;
            foreach (ItemRosterElement rosterElement in roster)
            {
                if (rosterElement.Amount <= 0)
                    continue;
                EquipmentElement element = rosterElement.EquipmentElement;
                ItemObject item = element.Item;
                string foundCareer;
                if (item == null ||
                    !TryGetRelicCareerFromAnyItem(item, out foundCareer) ||
                    !String.Equals(foundCareer, careerId,
                        StringComparison.OrdinalIgnoreCase))
                    continue;
                occurrences.Add(new RelicOccurrence
                {
                    Item = item,
                    Modifier = element.ItemModifier,
                    Roster = roster,
                    Element = element,
                    Amount = rosterElement.Amount,
                    Location = location,
                    IsMainRoster = isMainRoster,
                    IsEquipment = false
                });
            }
        }

        private static bool AddEquipmentOccurrences(object equipment,
            string location, string careerId,
            List<RelicOccurrence> occurrences)
        {
            if (equipment == null)
                return true;
            IEnumerable elements;
            if (!TryEnumerateEquipmentElements(equipment, out elements))
                return false;
            foreach (object value in elements)
            {
                ItemObject item = GetProperty(value, "Item") as ItemObject;
                string foundCareer;
                if (item == null ||
                    !TryGetRelicCareerFromAnyItem(item, out foundCareer) ||
                    !String.Equals(foundCareer, careerId,
                        StringComparison.OrdinalIgnoreCase))
                    continue;
                occurrences.Add(new RelicOccurrence
                {
                    Item = item,
                    Modifier = GetProperty(value, "ItemModifier") as ItemModifier,
                    Roster = null,
                    Element = default(EquipmentElement),
                    Amount = 1,
                    Location = location,
                    IsMainRoster = false,
                    IsEquipment = true
                });
            }
            return true;
        }

        private static bool TryGetRelicCareerFromAnyItem(ItemObject item,
            out string careerId)
        {
            careerId = null;
            if (item == null)
                return false;
            string itemName = item.Name == null ? String.Empty :
                item.Name.ToString();
            if (itemName.StartsWith("[ADMIN COPY]",
                StringComparison.OrdinalIgnoreCase))
                return false;

            try
            {
                MethodInfo getTraits = AccessTools.Method(
                    typeof(SetItemRuntime), "GetItemTraits",
                    new[] { typeof(string) });
                IList traits = getTraits == null ? null :
                    getTraits.Invoke(null, new object[] { item.StringId }) as IList;
                if (traits == null)
                    return TryGetRealRelicCareer(item, out careerId);
                if (ContainsPrefix(traits, "torcu_admin_") ||
                    ContainsPrefix(traits, "torcu_hero_"))
                    return false;

                MethodInfo findSignature = AccessTools.Method(
                    typeof(SetItemRuntime), "FindPieceSignature",
                    new[] { typeof(IList) });
                object signature = findSignature == null ? null :
                    findSignature.Invoke(null, new object[] { traits });
                if (signature == null)
                    return false;
                object rawIndex = GetField(signature, "PieceIndex") ??
                    GetProperty(signature, "PieceIndex");
                if (rawIndex == null || Convert.ToInt32(rawIndex) != 0)
                    return false;
                object definition = GetField(signature, "Definition") ??
                    GetProperty(signature, "Definition");
                if (definition == null)
                    return false;
                careerId = Convert.ToString(
                    GetField(definition, "CareerId") ??
                    GetProperty(definition, "CareerId"));
                return !String.IsNullOrEmpty(careerId);
            }
            catch (Exception ex)
            {
                ModLog.Error("Could not inspect relic signature for '" +
                    (String.IsNullOrEmpty(itemName) ? item.StringId : itemName) +
                    "': " + ex.GetType().Name + ": " + ex.Message);
                return false;
            }
        }

        private static bool TryGetRealRelicCareer(ItemObject item,
            out string careerId)
        {
            careerId = null;
            object artisan = CareerUniqueRuntime.GetArtisanBehavior();
            IDictionary crafted = artisan == null ? null :
                AccessTools.Field(artisan.GetType(), "_customCraftedItems")
                    ?.GetValue(artisan) as IDictionary;
            if (crafted == null || item == null || !crafted.Contains(item))
                return false;
            return TryGetRealRelicCareer(item, crafted[item], out careerId);
        }

        private static bool TryGetRealRelicCareer(ItemObject item,
            object saveData, out string careerId)
        {
            careerId = null;
            IList traits = GetProperty(saveData, "ItemTraits") as IList ??
                GetField(saveData, "ItemTraits") as IList;
            string itemName = item == null || item.Name == null ?
                String.Empty : item.Name.ToString();
            if (item == null || traits == null ||
                ContainsPrefix(traits, "torcu_admin_") ||
                ContainsPrefix(traits, "torcu_hero_") ||
                itemName.StartsWith("[ADMIN COPY]",
                    StringComparison.OrdinalIgnoreCase))
                return false;

            MethodInfo findSignature = AccessTools.Method(
                typeof(SetItemRuntime), "FindPieceSignature",
                new[] { typeof(IList) });
            if (findSignature == null)
                return false;
            try
            {
                object signature = findSignature.Invoke(null,
                    new object[] { traits });
                if (signature == null)
                    return false;
                object rawIndex = GetField(signature, "PieceIndex") ??
                    GetProperty(signature, "PieceIndex");
                if (rawIndex == null || Convert.ToInt32(rawIndex) != 0)
                    return false;
                object definition = GetField(signature, "Definition") ??
                    GetProperty(signature, "Definition");
                if (definition == null)
                    return false;
                careerId = Convert.ToString(
                    GetField(definition, "CareerId") ??
                    GetProperty(definition, "CareerId"));
                return !String.IsNullOrEmpty(careerId);
            }
            catch (Exception ex)
            {
                ModLog.Error("Skipped malformed saved relic signature for '" +
                    (String.IsNullOrEmpty(itemName) ? item.StringId : itemName) +
                    "': " + ex.GetType().Name + ": " + ex.Message);
                return false;
            }
        }

        private static bool TryGetAllReferencedHeroes(out IEnumerable heroes)
        {
            heroes = null;
            try
            {
                ArrayList result = new ArrayList();
                HashSet<Hero> visited = new HashSet<Hero>();
                string[] registryNames =
                {
                    "AllAliveHeroes",
                    "DeadOrDisabledHeroes"
                };
                for (int i = 0; i < registryNames.Length; i++)
                {
                    PropertyInfo property = typeof(Hero).GetProperty(
                        registryNames[i], BindingFlags.Public |
                        BindingFlags.NonPublic | BindingFlags.Static);
                    if (property == null)
                    {
                        if (i == 0)
                            return false;
                        continue;
                    }

                    IEnumerable values = property.GetValue(null, null) as
                        IEnumerable;
                    if (values == null)
                        return false;
                    foreach (object value in values)
                    {
                        Hero hero = value as Hero;
                        if (hero != null && visited.Add(hero))
                            result.Add(hero);
                    }
                }
                heroes = result;
                return true;
            }
            catch (Exception ex)
            {
                ModLog.Error("Complete hero registry scan failed: " +
                    ex.GetType().Name + ": " + ex.Message);
                return false;
            }
        }

        private static bool TryEnumerateEquipmentElements(object equipment,
            out IEnumerable elements)
        {
            elements = null;
            try
            {
                MethodInfo enumerate = AccessTools.Method(
                    typeof(SetItemRuntime), "EnumerateEquipmentElements",
                    new[] { typeof(object) });
                if (enumerate == null)
                    return false;
                elements = enumerate.Invoke(null,
                    new object[] { equipment }) as IEnumerable;
                return elements != null;
            }
            catch (Exception ex)
            {
                ModLog.Error("Equipment enumeration failed during relic scan: " +
                    ex.GetType().Name + ": " + ex.Message);
                return false;
            }
        }

        private static bool TryCountExactStackGlobally(ItemObject item,
            ItemModifier modifier, out int count, out string locations,
            out string error)
        {
            count = 0;
            locations = null;
            error = null;
            if (item == null)
            {
                error = "item is null";
                return false;
            }

            try
            {
                List<string> foundAt = new List<string>();
                HashSet<string> uniqueLocations = new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);
                HashSet<ItemRoster> visitedRosters = new HashSet<ItemRoster>();
                ItemRoster mainRoster = MobileParty.MainParty == null ? null :
                    MobileParty.MainParty.ItemRoster;
                CountExactInRoster(mainRoster, "active main inventory", item,
                    modifier, ref count, foundAt, uniqueLocations,
                    visitedRosters);

                foreach (MobileParty party in MobileParty.All)
                {
                    if (party == null || party.ItemRoster == null)
                        continue;
                    string partyName = party.Name == null ? party.StringId :
                        party.Name.ToString();
                    CountExactInRoster(party.ItemRoster,
                        "party " + partyName + " (" +
                        (party.StringId ?? "<no-id>") + ")", item, modifier,
                        ref count, foundAt, uniqueLocations, visitedRosters);
                }

                foreach (Settlement settlement in Settlement.All)
                {
                    if (settlement == null)
                        continue;
                    string settlementName = settlement.Name == null ?
                        settlement.StringId : settlement.Name.ToString();
                    CountExactInRoster(settlement.ItemRoster,
                        "settlement inventory at " + settlementName, item,
                        modifier, ref count, foundAt, uniqueLocations,
                        visitedRosters);
                    CountExactInRoster(settlement.Stash,
                        "stash at " + settlementName, item, modifier,
                        ref count, foundAt, uniqueLocations, visitedRosters);
                    CountExactInRoster(settlement.Party == null ? null :
                        settlement.Party.ItemRoster,
                        "settlement party inventory at " + settlementName,
                        item, modifier, ref count, foundAt, uniqueLocations,
                        visitedRosters);
                }

                IEnumerable heroes;
                if (!TryGetAllReferencedHeroes(out heroes))
                {
                    error = "the complete hero registry was unavailable";
                    return false;
                }
                HashSet<Hero> visitedHeroes = new HashSet<Hero>();
                if (Hero.MainHero != null)
                    visitedHeroes.Add(Hero.MainHero);
                foreach (object value in heroes)
                {
                    Hero hero = value as Hero;
                    if (hero != null)
                        visitedHeroes.Add(hero);
                }
                foreach (Hero hero in visitedHeroes)
                {
                    string heroName = hero.Name == null ? hero.StringId :
                        hero.Name.ToString();
                    if (!CountExactInEquipment(hero.BattleEquipment,
                        heroName + " battle equipment", item, modifier,
                        ref count, foundAt, uniqueLocations) ||
                        !CountExactInEquipment(hero.CivilianEquipment,
                            heroName + " civilian equipment", item, modifier,
                            ref count, foundAt, uniqueLocations))
                    {
                        error = "equipment for " + heroName +
                            " could not be scanned";
                        return false;
                    }
                }

                locations = String.Join(", ", foundAt.ToArray());
                return true;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                ModLog.Error("Exact global relic stack scan failed: " + error);
                return false;
            }
        }

        private static void CountExactInRoster(ItemRoster roster,
            string location, ItemObject item, ItemModifier modifier,
            ref int count, List<string> locations,
            HashSet<string> uniqueLocations,
            HashSet<ItemRoster> visitedRosters)
        {
            if (roster == null || !visitedRosters.Add(roster))
                return;
            int local = 0;
            foreach (ItemRosterElement element in roster)
            {
                EquipmentElement equipment = element.EquipmentElement;
                if (element.Amount > 0 &&
                    Object.ReferenceEquals(equipment.Item, item) &&
                    Object.ReferenceEquals(equipment.ItemModifier, modifier))
                    local += element.Amount;
            }
            if (local <= 0)
                return;
            count += local;
            if (uniqueLocations.Add(location))
                locations.Add(location);
        }

        private static bool CountExactInEquipment(object equipment,
            string location, ItemObject item, ItemModifier modifier,
            ref int count, List<string> locations,
            HashSet<string> uniqueLocations)
        {
            if (equipment == null)
                return true;
            IEnumerable elements;
            if (!TryEnumerateEquipmentElements(equipment, out elements))
                return false;
            int local = 0;
            foreach (object element in elements)
            {
                if (Object.ReferenceEquals(GetProperty(element, "Item"), item) &&
                    Object.ReferenceEquals(
                        GetProperty(element, "ItemModifier"), modifier))
                    local++;
            }
            if (local > 0)
            {
                count += local;
                if (uniqueLocations.Add(location))
                    locations.Add(location);
            }
            return true;
        }

        private static string FormatOccurrenceLocations(
            List<RelicOccurrence> occurrences)
        {
            HashSet<string> unique = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            List<string> locations = new List<string>();
            for (int i = 0; i < occurrences.Count; i++)
            {
                string location = occurrences[i].Location ?? "unknown location";
                if (unique.Add(location))
                    locations.Add(location);
                if (locations.Count >= 6)
                    break;
            }
            return String.Join(", ", locations.ToArray());
        }

        private static string DescribeItem(ItemObject item,
            ItemModifier modifier)
        {
            return CareerUniqueRuntime.FormatModifiedItemName(
                item == null || item.Name == null ?
                    (item == null ? "<missing item>" : item.StringId) :
                    item.Name.ToString(), modifier);
        }

        private static bool ContainsPrefix(IList values, string prefix)
        {
            if (values == null || String.IsNullOrEmpty(prefix))
                return false;
            for (int i = 0; i < values.Count; i++)
            {
                string value = Convert.ToString(values[i]);
                if (!String.IsNullOrEmpty(value) &&
                    value.StartsWith(prefix, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static object GetProperty(object instance, string name)
        {
            if (instance == null)
                return null;
            PropertyInfo property = instance.GetType().GetProperty(name,
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance | BindingFlags.Static);
            return property == null ? null :
                property.GetValue(instance, null);
        }

        private static object GetField(object instance, string name)
        {
            if (instance == null)
                return null;
            FieldInfo field = instance.GetType().GetField(name,
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance | BindingFlags.Static);
            return field == null ? null : field.GetValue(instance);
        }
    }
}
