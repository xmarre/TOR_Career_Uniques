using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.MountAndBlade;

namespace TORCareerUniques
{
    public sealed class CompanionSetLiveReconcileSubModule : MBSubModuleBase
    {
        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            SetItemRuntime.InitializeCompanionLiveReconcileSupport();
        }
    }

    internal static partial class SetItemRuntime
    {
        private const string CompanionLiveReconcileHarmonyId =
            "torcareeruniques.sets.companion-live-reconcile.1.7.34";
        private static bool _companionLiveReconcileInstalled;

        internal static void InitializeCompanionLiveReconcileSupport()
        {
            if (_companionLiveReconcileInstalled)
                return;

            try
            {
                Harmony harmony = new Harmony(CompanionLiveReconcileHarmonyId);

                MethodInfo enumerate = AccessTools.Method(typeof(SetItemRuntime),
                    "EnumeratePlayerClanHeroes", new[] { typeof(object) });
                MethodInfo enumeratePrefix = AccessTools.Method(typeof(SetItemRuntime),
                    nameof(ReplacePlayerClanHeroEnumeration));
                if (enumerate == null || enumeratePrefix == null)
                    throw new MissingMethodException(
                        "Player-clan hero enumeration patch target was not found.");
                HarmonyMethod enumerationPatch = new HarmonyMethod(enumeratePrefix)
                {
                    priority = Priority.First,
                    before = new[] { CompanionSetHarmonyId }
                };
                harmony.Patch(enumerate, prefix: enumerationPatch);

                MethodInfo tooltip = FindCompanionSetMethod(typeof(SetItemRuntime),
                    "TryBuildTooltipForItemViewModel", 4,
                    BindingFlags.NonPublic | BindingFlags.Public |
                    BindingFlags.Static);
                MethodInfo tooltipPrefix = AccessTools.Method(typeof(SetItemRuntime),
                    nameof(BeforeCompanionAwareTooltipLiveReconcile));
                if (tooltip == null || tooltipPrefix == null)
                    throw new MissingMethodException(
                        "Companion set tooltip reconciliation target was not found.");
                HarmonyMethod tooltipPatch = new HarmonyMethod(tooltipPrefix)
                {
                    priority = Priority.First,
                    before = new[] { CompanionSetHarmonyId }
                };
                harmony.Patch(tooltip, prefix: tooltipPatch);

                MethodInfo equipmentMutation = AccessTools.Method(
                    typeof(RuntimePerformanceGate), "AfterEquipmentMutation",
                    new[] { typeof(object) });
                MethodInfo equipmentPostfix = AccessTools.Method(
                    typeof(SetItemRuntime),
                    nameof(AfterAnyInventoryEquipmentMutation));
                if (equipmentMutation == null || equipmentPostfix == null)
                    throw new MissingMethodException(
                        "Inventory equipment reconciliation target was not found.");
                harmony.Patch(equipmentMutation, postfix:
                    new HarmonyMethod(equipmentPostfix)
                    {
                        priority = Priority.Last
                    });

                _companionLiveReconcileInstalled = true;
                ModLog.Info("Installed authoritative main-party companion set reconciliation.");
            }
            catch (Exception ex)
            {
                ModLog.Error("Companion live set reconciliation could not be installed. " +
                    ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static bool ReplacePlayerClanHeroEnumeration(object __0,
            ref IEnumerable __result)
        {
            __result = EnumeratePlayerClanHeroesAuthoritatively(__0);
            return false;
        }

        private static IEnumerable EnumeratePlayerClanHeroesAuthoritatively(
            object playerClan)
        {
            HashSet<object> yielded = new HashSet<object>(
                ReferenceObjectComparer.Instance);

            if (playerClan != null)
            {
                string[] collectionNames = { "Heroes", "Companions", "Lords" };
                for (int i = 0; i < collectionNames.Length; i++)
                {
                    IEnumerable heroes = GetProperty(playerClan,
                        collectionNames[i]) as IEnumerable;
                    if (heroes == null)
                        continue;
                    foreach (object hero in heroes)
                        if (hero != null && yielded.Add(hero))
                            yield return hero;
                }
            }

            // The party roster is the authoritative source for heroes selectable
            // on the inventory screen, including custom companions whose clan
            // collection membership has not been synchronized yet.
            object mainParty = GetStaticProperty(TypeByName(
                "TaleWorlds.CampaignSystem.Party.MobileParty"), "MainParty");
            IEnumerable members = GetProperty(mainParty, "MemberRoster") as IEnumerable;
            if (members != null)
            {
                foreach (object member in members)
                {
                    object character = GetProperty(member, "Character");
                    object hero = GetProperty(character, "HeroObject") ??
                        GetProperty(character, "Hero");
                    if (hero != null && yielded.Add(hero))
                        yield return hero;
                }
            }

            // Bounded compatibility fallback for versions/mods whose clan and
            // party collections expose different hero wrappers. This iterator is
            // consumed only at existing session/inventory/roster refresh points.
            if (playerClan != null)
            {
                IEnumerable alive = GetStaticProperty(TypeByName(
                    "TaleWorlds.CampaignSystem.Hero"),
                    "AllAliveHeroes") as IEnumerable;
                if (alive != null)
                {
                    foreach (object hero in alive)
                    {
                        if (hero == null || yielded.Contains(hero) ||
                            !Object.ReferenceEquals(
                                GetProperty(hero, "Clan"), playerClan))
                            continue;
                        yielded.Add(hero);
                        yield return hero;
                    }
                }
            }
        }

        private static void AfterAnyInventoryEquipmentMutation(object __0)
        {
            try
            {
                if (_internalCompanionCarrierMutation ||
                    _companionSetRefreshInProgress || __0 == null ||
                    !IsCompanionInventoryStateActive())
                    return;

                // Bannerlord can report the inventory's working Equipment object,
                // which is not reference-equal to Hero.BattleEquipment. Force one
                // bounded rebuild rather than dropping that mutation as unrelated.
                _forceFullCompanionSetSnapshot = true;
                _companionSetSnapshotDirty = true;
                Tick();
            }
            catch (Exception ex)
            {
                LogOnce("companion-live-equipment-event:" +
                    ex.GetType().FullName + ":" + ex.Message,
                    "Live companion equipment reconciliation failed: " +
                    FormatException(ex));
            }
        }

        private static void BeforeCompanionAwareTooltipLiveReconcile(object __0)
        {
            try
            {
                string itemId = GetItemIdFromViewModel(__0);
                if (String.IsNullOrWhiteSpace(itemId))
                    return;

                object item = GetItemFromViewModel(__0);
                PieceSignature signature =
                    FindPieceSignatureForTooltip(item, itemId);
                if (signature == null)
                    return;

                EnsureCompanionSetSession();
                EnsureCurrentPlayerHeroSnapshots(GetMainHeroIfReady(), null);

                HeroSetSnapshot cached =
                    FindSetItemOwnerSnapshot(item, itemId);
                if (SnapshotContainsSetPieceForLiveReconcile(cached, signature))
                    return;

                object liveOwner = FindLiveSetItemOwnerAuthoritatively(
                    item, itemId, signature);
                if (liveOwner == null)
                    return;

                _forceFullCompanionSetSnapshot = true;
                _companionSetSnapshotDirty = true;
                RefreshAllPlayerHeroBonuses(null);

                LogOnce("companion-live-owner:" +
                    GetHeroSnapshotKey(liveOwner) + ":" + itemId,
                    "Reconciled live companion set equipment for " +
                    GetHeroSnapshotKey(liveOwner) +
                    " while building the set tooltip.");
            }
            catch (Exception ex)
            {
                LogOnce("companion-live-tooltip:" +
                    ex.GetType().FullName + ":" + ex.Message,
                    "Live companion tooltip reconciliation failed: " +
                    FormatException(ex));
            }
        }

        private static bool SnapshotContainsSetPieceForLiveReconcile(
            HeroSetSnapshot snapshot, PieceSignature signature)
        {
            if (snapshot == null || snapshot.StateByCareer == null ||
                signature == null || signature.Definition == null)
                return false;

            EquippedSetState state;
            return snapshot.StateByCareer.TryGetValue(
                signature.Definition.CareerId, out state) &&
                state != null &&
                state.PieceIndices.Contains(signature.PieceIndex);
        }

        private static object FindLiveSetItemOwnerAuthoritatively(
            object item, string itemId, PieceSignature signature)
        {
            object mainHero = GetMainHeroIfReady();
            object playerClan = GetStaticProperty(
                TypeByName("TaleWorlds.CampaignSystem.Clan"), "PlayerClan");
            HashSet<object> visited = new HashSet<object>(
                ReferenceObjectComparer.Instance);
            object owner = null;

            List<object> candidates = new List<object>();
            if (mainHero != null)
                candidates.Add(mainHero);
            foreach (object hero in
                EnumeratePlayerClanHeroesAuthoritatively(playerClan))
                candidates.Add(hero);

            for (int h = 0; h < candidates.Count; h++)
            {
                object hero = candidates[h];
                if (hero == null || !visited.Add(hero) ||
                    ToBoolean(GetProperty(hero, "IsDead")))
                    continue;

                object equipment = GetProperty(hero, "BattleEquipment");
                foreach (object element in EnumerateEquipmentElements(equipment))
                {
                    object equippedItem = GetProperty(element, "Item");
                    if (equippedItem == null)
                        continue;
                    string equippedId = Convert.ToString(
                        GetProperty(equippedItem, "StringId"));
                    if (!Object.ReferenceEquals(equippedItem, item) &&
                        !String.Equals(equippedId, itemId,
                            StringComparison.Ordinal))
                        continue;

                    PieceSignature equippedSignature =
                        FindPieceSignatureForTooltip(equippedItem, equippedId);
                    if (equippedSignature == null || signature == null ||
                        signature.Definition == null ||
                        !String.Equals(
                            equippedSignature.Definition.CareerId,
                            signature.Definition.CareerId,
                            StringComparison.OrdinalIgnoreCase) ||
                        equippedSignature.PieceIndex != signature.PieceIndex)
                        continue;

                    if (owner != null &&
                        !Object.ReferenceEquals(owner, hero))
                        return null;
                    owner = hero;
                }
            }
            return owner;
        }
    }
}
