using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MCM.Abstractions.Attributes;
using MCM.Abstractions.Attributes.v2;
using MCM.Abstractions.Base.Global;
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

    public sealed class RelicRewardRecoverySettings :
        AttributeGlobalSettings<RelicRewardRecoverySettings>
    {
        private static readonly Action RepairAction =
            RelicRewardIntegrity.RepairFromMcm;

        public override string Id
        {
            get { return "TORCareerUniques_RelicRecovery_v1"; }
        }

        public override string DisplayName
        {
            get { return "TOR Career Uniques - Recovery"; }
        }

        public override string FolderName
        {
            get { return "TORCareerUniques"; }
        }

        public override string FormatType
        {
            get { return "json2"; }
        }

        public override int UIVersion
        {
            get { return 1; }
        }

        [SettingPropertyButton("Repair missing recovered relics", 0, false,
            "Checks recovered career weapons that are absent from every player-owned inventory and equipment slot. If the exact runtime item was transferred to another party after a reinforced battle, it is reclaimed with its modifier; otherwise the saved relic is restored with a fresh quality roll.",
            Content = "Repair now")]
        [SettingPropertyGroup("Relic reward recovery", GroupOrder = 0)]
        public Action RepairMissingRecoveredRelics
        {
            get { return RepairAction; }
            set { }
        }
    }

    internal static class RelicRewardIntegrity
    {
        private const string HarmonyId =
            "torcareeruniques.rewards.inventory-integrity.1.7.41";
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
            internal int ExpectedCount;
            internal string CareerId;
            internal float Delay;
            internal int Attempts;
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
                    "Installed reward inventory integrity verification, post-loot " +
                    "ownership auditing, and inventory-screen reward deferral.");
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

                ItemRoster mainRoster = MobileParty.MainParty == null ? null :
                    MobileParty.MainParty.ItemRoster;
                bool supported;
                int current = CountExactStack(mainRoster, audit.Item,
                    audit.Modifier, out supported);
                if (supported && current >= audit.ExpectedCount)
                {
                    ModLog.Info("Post-loot ownership audit retained '" +
                        DescribeItem(audit.Item, audit.Modifier) + "'.");
                    PendingAudits.RemoveAt(i);
                    continue;
                }

                audit.Attempts++;
                string source;
                bool restored = RestoreAuditedGrant(audit, out source);
                if (restored)
                {
                    ModLog.AlwaysInfo("Post-loot ownership audit restored '" +
                        DescribeItem(audit.Item, audit.Modifier) + "'" +
                        (String.IsNullOrEmpty(source) ? String.Empty :
                            " after reclaiming it from " + source) +
                        ". Recovery state remains valid.");
                    PendingAudits.RemoveAt(i);
                }
                else if (audit.Attempts >= 3)
                {
                    ModLog.Error("Post-loot ownership audit could not restore '" +
                        DescribeItem(audit.Item, audit.Modifier) +
                        "' after three bounded attempts. Use the MCM recovery " +
                        "button and retain the log.");
                    PendingAudits.RemoveAt(i);
                }
                else
                {
                    audit.Delay = 1f;
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
                QueuePostLootAudit(relic, relicModifier, after, careerId);
            }
        }

        private static void QueuePostLootAudit(ItemObject item,
            ItemModifier modifier, int expectedCount, string careerId)
        {
            for (int i = 0; i < PendingAudits.Count; i++)
            {
                PendingGrantAudit existing = PendingAudits[i];
                if (Object.ReferenceEquals(existing.Item, item) &&
                    Object.ReferenceEquals(existing.Modifier, modifier))
                {
                    existing.ExpectedCount = Math.Max(existing.ExpectedCount,
                        expectedCount);
                    existing.Delay = 1f;
                    existing.Attempts = 0;
                    return;
                }
            }

            PendingAudits.Add(new PendingGrantAudit
            {
                Item = item,
                Modifier = modifier,
                ExpectedCount = expectedCount,
                CareerId = careerId,
                Delay = 1f,
                Attempts = 0
            });
        }

        private static bool RestoreAuditedGrant(PendingGrantAudit audit,
            out string source)
        {
            source = null;
            if (audit == null || audit.Item == null ||
                MobileParty.MainParty == null ||
                MobileParty.MainParty.ItemRoster == null)
                return false;

            ItemRoster sourceRoster;
            EquipmentElement sourceElement;
            if (TryDetachExactStackFromExternalParty(audit.Item,
                audit.Modifier, out sourceRoster, out sourceElement,
                out source))
            {
                string error;
                bool added;
                _restoring = true;
                try
                {
                    added = CareerUniqueRuntime.AddToRoster(
                        MobileParty.MainParty.ItemRoster, audit.Item,
                        audit.Modifier, 1, out error);
                }
                finally
                {
                    _restoring = false;
                }
                if (!added)
                {
                    sourceRoster.AddToCounts(sourceElement, 1);
                    ModLog.Error("Could not reclaim post-battle relic from " +
                        source + ": " +
                        (error ?? "inventory insertion failed") + ".");
                    source = null;
                    return false;
                }
            }
            else
            {
                string error;
                bool added;
                _restoring = true;
                try
                {
                    added = CareerUniqueRuntime.AddToRoster(
                        MobileParty.MainParty.ItemRoster, audit.Item,
                        audit.Modifier, 1, out error);
                }
                finally
                {
                    _restoring = false;
                }
                if (!added)
                {
                    ModLog.Error("Could not restore post-battle relic: " +
                        (error ?? "inventory insertion failed") + ".");
                    return false;
                }
            }

            bool supported;
            int current = CountExactStack(
                MobileParty.MainParty.ItemRoster, audit.Item,
                audit.Modifier, out supported);
            return supported && current >= audit.ExpectedCount;
        }

        private static bool TryDetachExactStackFromExternalParty(
            ItemObject item, ItemModifier modifier, out ItemRoster sourceRoster,
            out EquipmentElement sourceElement, out string sourceName)
        {
            sourceRoster = null;
            sourceElement = default(EquipmentElement);
            sourceName = null;
            if (item == null)
                return false;

            foreach (MobileParty party in MobileParty.All)
            {
                if (party == null || !party.IsActive ||
                    Object.ReferenceEquals(party, MobileParty.MainParty) ||
                    party.ItemRoster == null)
                    continue;

                EquipmentElement found;
                if (!TryFindExactStack(party.ItemRoster, item, modifier,
                    out found))
                    continue;

                party.ItemRoster.AddToCounts(found, -1);
                sourceRoster = party.ItemRoster;
                sourceElement = found;
                sourceName = (party.Name == null ? party.StringId :
                    party.Name.ToString()) + " (" +
                    (party.StringId ?? "<no-id>") + ")";
                return true;
            }
            return false;
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
                    count += element.Amount;
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
            string result = RepairOrphanedRelicRewards(
                new List<string>());
            InquiryHelper.ShowMessage("Relic Reward Recovery", result);
        }

        [CommandLineFunctionality.CommandLineArgumentFunction(
            "repair_orphaned_relic_rewards", "torcu")]
        public static string RepairOrphanedRelicRewards(
            List<string> arguments)
        {
            if (Campaign.Current == null || MobileParty.MainParty == null ||
                MobileParty.MainParty.ItemRoster == null)
            {
                return "No campaign with a player inventory is currently " +
                    "loaded.";
            }

            Dictionary<string, ItemObject> orphanByCareer;
            string scanError;
            if (!TryFindOrphanedRelics(out orphanByCareer, out scanError))
                return scanError;
            if (orphanByCareer.Count == 0)
            {
                return "No recovered-but-unowned relic reward was found to " +
                    "repair.";
            }

            List<string> repaired = new List<string>();
            List<string> failed = new List<string>();
            foreach (KeyValuePair<string, ItemObject> pair in orphanByCareer)
            {
                ItemObject item = pair.Value;
                ItemModifier modifier;
                ItemRoster sourceRoster;
                EquipmentElement sourceElement;
                string source;
                bool reclaimed = TryDetachAnyStackFromExternalParty(item,
                    out modifier, out sourceRoster, out sourceElement,
                    out source);
                if (!reclaimed)
                    modifier = CareerUniqueRuntime.RollLootModifier(item);

                string error;
                bool added;
                _restoring = true;
                try
                {
                    added = CareerUniqueRuntime.AddToRoster(
                        MobileParty.MainParty.ItemRoster, item, modifier, 1,
                        out error);
                }
                finally
                {
                    _restoring = false;
                }

                if (added)
                {
                    string name = CareerUniqueRuntime.FormatModifiedItemName(
                        item.Name == null ? item.StringId :
                            item.Name.ToString(), modifier);
                    repaired.Add(name +
                        (reclaimed ? " (reclaimed from " + source + ")" :
                            " (restored from TOR's saved relic record)"));
                    ModLog.AlwaysInfo("Recovered missing real relic '" +
                        name + "' for " + pair.Key +
                        (reclaimed ? " from " + source :
                            " from TOR's crafted-item save record") + ".");
                }
                else
                {
                    if (reclaimed)
                        sourceRoster.AddToCounts(sourceElement, 1);
                    failed.Add(pair.Key + ": " +
                        (error ?? "inventory insertion failed"));
                }
            }

            SetItemRuntime.Tick();
            string result = repaired.Count == 0 ?
                "No orphaned relic reward could be restored." :
                "Restored missing recovered relic reward(s): " +
                String.Join(", ", repaired.ToArray()) + ".";
            if (failed.Count > 0)
                result += " Failed: " +
                    String.Join("; ", failed.ToArray()) + ".";
            ModLog.AlwaysInfo(result);
            return result;
        }

        private static bool TryFindOrphanedRelics(
            out Dictionary<string, ItemObject> orphanByCareer,
            out string error)
        {
            orphanByCareer =
                new Dictionary<string, ItemObject>(
                    StringComparer.OrdinalIgnoreCase);
            error = null;

            object artisan = CareerUniqueRuntime.GetArtisanBehavior();
            IDictionary crafted = artisan == null ? null :
                AccessTools.Field(artisan.GetType(), "_customCraftedItems")
                    ?.GetValue(artisan) as IDictionary;
            if (crafted == null)
            {
                error = "TOR's crafted-item save dictionary is unavailable.";
                return false;
            }

            foreach (DictionaryEntry entry in crafted)
            {
                ItemObject item = entry.Key as ItemObject;
                if (item == null)
                    continue;

                string careerId;
                if (!TryGetRealRelicCareer(item, entry.Value, out careerId) ||
                    !AdminBridge.HasDiscoveredSetPiece(careerId, 0) ||
                    orphanByCareer.ContainsKey(careerId))
                    continue;

                if (!IsOwnedByPlayer(item))
                    orphanByCareer.Add(careerId, item);
            }
            return true;
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
            return TryGetRealRelicCareer(item, crafted[item],
                out careerId);
        }

        private static bool TryGetRealRelicCareer(ItemObject item,
            object saveData, out string careerId)
        {
            careerId = null;
            IList traits = GetProperty(saveData, "ItemTraits") as IList ??
                GetField(saveData, "ItemTraits") as IList;
            if (item == null || traits == null ||
                ContainsPrefix(traits, "torcu_admin_") ||
                ContainsPrefix(traits, "torcu_hero_"))
                return false;

            MethodInfo findSignature = AccessTools.Method(
                typeof(SetItemRuntime), "FindPieceSignature",
                new[] { typeof(IList) });
            if (findSignature == null)
                return false;

            object signature = findSignature.Invoke(null,
                new object[] { traits });
            if (signature == null)
                return false;

            int pieceIndex = Convert.ToInt32(
                GetField(signature, "PieceIndex") ??
                GetProperty(signature, "PieceIndex"));
            if (pieceIndex != 0)
                return false;

            object definition = GetField(signature, "Definition") ??
                GetProperty(signature, "Definition");
            careerId = Convert.ToString(
                GetField(definition, "CareerId") ??
                GetProperty(definition, "CareerId"));
            return !String.IsNullOrEmpty(careerId);
        }

        private static bool IsOwnedByPlayer(ItemObject item)
        {
            if (item == null)
                return false;
            if (RosterContains(MobileParty.MainParty == null ? null :
                MobileParty.MainParty.ItemRoster, item))
                return true;

            Clan clan = Clan.PlayerClan;
            if (clan != null)
            {
                foreach (MobileParty party in MobileParty.All)
                {
                    if (party == null || party.ItemRoster == null)
                        continue;
                    bool playerClanParty =
                        Object.ReferenceEquals(party.ActualClan, clan) ||
                        (party.LeaderHero != null &&
                         Object.ReferenceEquals(party.LeaderHero.Clan, clan));
                    if (playerClanParty &&
                        RosterContains(party.ItemRoster, item))
                        return true;
                }
            }

            HashSet<Hero> heroes = new HashSet<Hero>();
            if (Hero.MainHero != null)
                heroes.Add(Hero.MainHero);
            AddHeroes(heroes, clan, "Heroes");
            AddHeroes(heroes, clan, "Companions");
            AddHeroes(heroes, clan, "Lords");
            foreach (Hero hero in heroes)
            {
                if (EquipmentContains(hero == null ? null :
                    hero.BattleEquipment, item) ||
                    EquipmentContains(hero == null ? null :
                        hero.CivilianEquipment, item))
                    return true;
            }

            if (clan != null)
            {
                foreach (Settlement settlement in Settlement.All)
                {
                    if (settlement != null &&
                        settlement.OwnerClan == clan &&
                        RosterContains(settlement.ItemRoster, item))
                        return true;
                }
            }
            return false;
        }

        private static bool TryDetachAnyStackFromExternalParty(
            ItemObject item, out ItemModifier modifier,
            out ItemRoster sourceRoster,
            out EquipmentElement sourceElement, out string sourceName)
        {
            modifier = null;
            sourceRoster = null;
            sourceElement = default(EquipmentElement);
            sourceName = null;
            if (item == null)
                return false;

            Clan playerClan = Clan.PlayerClan;
            foreach (MobileParty party in MobileParty.All)
            {
                if (party == null || !party.IsActive ||
                    Object.ReferenceEquals(party, MobileParty.MainParty) ||
                    party.ItemRoster == null)
                    continue;
                bool playerOwned =
                    playerClan != null &&
                    (Object.ReferenceEquals(party.ActualClan, playerClan) ||
                     (party.LeaderHero != null &&
                      Object.ReferenceEquals(party.LeaderHero.Clan,
                          playerClan)));
                if (playerOwned)
                    continue;

                EquipmentElement found;
                if (!TryFindAnyStack(party.ItemRoster, item, out found))
                    continue;

                party.ItemRoster.AddToCounts(found, -1);
                modifier = found.ItemModifier;
                sourceRoster = party.ItemRoster;
                sourceElement = found;
                sourceName = (party.Name == null ? party.StringId :
                    party.Name.ToString()) + " (" +
                    (party.StringId ?? "<no-id>") + ")";
                return true;
            }
            return false;
        }

        private static bool TryFindAnyStack(ItemRoster roster,
            ItemObject item, out EquipmentElement found)
        {
            found = default(EquipmentElement);
            if (roster == null || item == null)
                return false;
            foreach (ItemRosterElement element in roster)
            {
                if (element.Amount > 0 &&
                    Object.ReferenceEquals(
                        element.EquipmentElement.Item, item))
                {
                    found = element.EquipmentElement;
                    return true;
                }
            }
            return false;
        }

        private static void AddHeroes(HashSet<Hero> target, object owner,
            string propertyName)
        {
            IEnumerable values = GetProperty(owner, propertyName) as
                IEnumerable;
            if (values == null)
                return;
            foreach (object value in values)
            {
                Hero hero = value as Hero;
                if (hero != null)
                    target.Add(hero);
            }
        }

        private static bool RosterContains(ItemRoster roster,
            ItemObject item)
        {
            if (roster == null || item == null)
                return false;
            foreach (ItemRosterElement element in roster)
            {
                if (element.Amount > 0 &&
                    Object.ReferenceEquals(
                        element.EquipmentElement.Item, item))
                    return true;
            }
            return false;
        }

        private static bool EquipmentContains(object equipment,
            ItemObject item)
        {
            if (equipment == null || item == null)
                return false;
            try
            {
                MethodInfo enumerate = AccessTools.Method(
                    typeof(SetItemRuntime), "EnumerateEquipmentElements",
                    new[] { typeof(object) });
                IEnumerable elements = enumerate == null ? null :
                    enumerate.Invoke(null, new[] { equipment }) as
                        IEnumerable;
                if (elements == null)
                    return false;
                foreach (object element in elements)
                {
                    if (Object.ReferenceEquals(
                        GetProperty(element, "Item"), item))
                        return true;
                }
            }
            catch
            {
            }
            return false;
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
