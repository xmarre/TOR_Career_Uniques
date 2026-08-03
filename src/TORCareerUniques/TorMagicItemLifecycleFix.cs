using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Workshops;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;

namespace TORCareerUniques
{
    /// <summary>
    /// Keeps TOR runtime magic items registered while any live game object still
    /// references them. TOR's weekly cleanup has an incomplete ownership scan:
    /// it can miss weapon slots, civilian equipment, inactive shared characters,
    /// player-clan party inventories, stashes, and pending loot. Unregistering a
    /// missed item leaves those references alive while removing the StringId from
    /// MBObjectManager, so its cached icon disappears permanently after eviction.
    /// </summary>
    internal static class TorMagicItemLifecycleFix
    {
        private const string HarmonyId =
            "torcareeruniques.tor-runtime-magic-item-lifetime.1.7.41";

        private static MethodInfo _hasAnyLootTraits;
        private static MethodInfo _isRuntimeDuplicatedItem;
        private static bool _installed;
        private static bool _loggedRepairFailure;
        private static bool _loggedOwnershipScanFailure;
        private static readonly HashSet<string> RepairingIds =
            new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> LoggedRetainedIds =
            new HashSet<string>(StringComparer.Ordinal);

        [ThreadStatic]
        private static int _cleanupDepth;

        internal static void Initialize()
        {
            if (_installed)
                return;

            try
            {
                Type behaviorType = AccessTools.TypeByName(
                    "TOR_Core.CampaignMechanics.Crafting.LootCampaignBehavior");
                Type itemExtensionsType = AccessTools.TypeByName(
                    "TOR_Core.Extensions.ItemObjectExtensions");
                Type extendedManagerType = AccessTools.TypeByName(
                    "TOR_Core.Items.ExtendedItemObjectManager");

                MethodInfo cleanupTarget = behaviorType == null ? null :
                    AccessTools.Method(behaviorType, "RemovedUnusedLootItems",
                        Type.EmptyTypes);
                _hasAnyLootTraits = itemExtensionsType == null ? null :
                    AccessTools.Method(itemExtensionsType, "HasAnyLootTraits",
                        new[] { typeof(ItemObject) });
                _isRuntimeDuplicatedItem = extendedManagerType == null ? null :
                    AccessTools.Method(extendedManagerType,
                        "IsRuntimeDuplicatedItem",
                        new[] { typeof(ItemObject) });
                MethodInfo unregisterTarget = AccessTools.Method(
                    typeof(MBObjectManager), "UnregisterObject",
                    new[] { typeof(MBObjectBase) });

                MethodInfo cleanupPrefix = AccessTools.Method(
                    typeof(TorMagicItemLifecycleFix),
                    nameof(BeforeRemovedUnusedLootItems));
                MethodInfo cleanupFinalizer = AccessTools.Method(
                    typeof(TorMagicItemLifecycleFix),
                    nameof(AfterRemovedUnusedLootItems));
                MethodInfo traitsPostfix = AccessTools.Method(
                    typeof(TorMagicItemLifecycleFix),
                    nameof(AfterHasAnyLootTraits));
                MethodInfo unregisterPrefix = AccessTools.Method(
                    typeof(TorMagicItemLifecycleFix),
                    nameof(BeforeUnregisterObject));

                if (cleanupTarget == null || _hasAnyLootTraits == null ||
                    _isRuntimeDuplicatedItem == null ||
                    unregisterTarget == null || cleanupPrefix == null ||
                    cleanupFinalizer == null || traitsPostfix == null ||
                    unregisterPrefix == null)
                {
                    throw new MissingMethodException(
                        "TOR runtime magical-loot cleanup could not be resolved.");
                }

                Harmony harmony = new Harmony(HarmonyId);
                harmony.Patch(cleanupTarget,
                    prefix: new HarmonyMethod(cleanupPrefix)
                    {
                        priority = Priority.First
                    },
                    finalizer: new HarmonyMethod(cleanupFinalizer)
                    {
                        priority = Priority.Last
                    });
                harmony.Patch(_hasAnyLootTraits,
                    postfix: new HarmonyMethod(traitsPostfix)
                    {
                        priority = Priority.Last
                    });
                harmony.Patch(unregisterTarget,
                    prefix: new HarmonyMethod(unregisterPrefix)
                    {
                        priority = Priority.First
                    });

                _installed = true;
                ModLog.AlwaysInfo(
                    "Installed reference-safe TOR runtime magic-item lifetime " +
                    "guards. TOR's weekly cleanup keeps its original removal " +
                    "scope and can no longer unregister a live item reference.");
            }
            catch (Exception ex)
            {
                ModLog.Error("TOR runtime magic-item lifetime fix could not be " +
                    "installed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void BeforeRemovedUnusedLootItems()
        {
            _cleanupDepth++;
        }

        private static Exception AfterRemovedUnusedLootItems(
            Exception __exception)
        {
            if (_cleanupDepth > 0)
                _cleanupDepth--;
            return __exception;
        }

        // During TOR's candidate query, hide items found in authoritative player
        // locations or any living hero equipment. The original cleanup therefore
        // never mutates those items. Unprotected candidates retain TOR's behavior.
        private static void AfterHasAnyLootTraits(ItemObject __0,
            ref bool __result)
        {
            if (_cleanupDepth <= 0 || !__result || __0 == null)
                return;

            try
            {
                if (HasProtectedReference(__0))
                    __result = false;
            }
            catch (Exception ex)
            {
                // Ownership uncertainty must never fall through to TOR's
                // destructive cleanup path. Skip this candidate and retain it.
                __result = false;
                LogOwnershipScanFailure(ex);
            }
        }

        // TOR removes foreign settlement/lord-party stacks before unregistering.
        // Re-scan every live reference at that final lifetime boundary. A remaining
        // roster/equipment reference makes unregistration invalid regardless of
        // ownership, because the ItemObject is still reachable by the game.
        private static bool BeforeUnregisterObject(MBObjectBase __0)
        {
            if (_cleanupDepth <= 0)
                return true;

            ItemObject item = __0 as ItemObject;
            if (item == null)
                return true;

            try
            {
                if (!HasAnyGlobalReference(item))
                    return true;
            }
            catch (Exception ex)
            {
                // A failed final reference proof cannot authorize object
                // destruction. Retain the item and let the next weekly pass retry.
                LogOwnershipScanFailure(ex);
                return false;
            }

            string id = item.StringId ?? "<no-id>";
            if (LoggedRetainedIds.Add(id))
            {
                ModLog.Info("Prevented TOR's weekly cleanup from unregistering " +
                    "still-referenced magic item '" + id + "'.");
            }
            return false;
        }

        private static void LogOwnershipScanFailure(Exception ex)
        {
            if (_loggedOwnershipScanFailure)
                return;

            _loggedOwnershipScanFailure = true;
            ModLog.Error("TOR runtime magic-item ownership scan failed: " +
                ex.GetType().Name + ": " + ex.Message +
                ". The affected item was retained.");
        }

        // Called reflectively by the item-image provider prefix. Healthy ids are
        // constant-time. An already-damaged id is located only in live rosters and
        // equipment, validated as TOR runtime magical loot, and re-registered
        // before Bannerlord performs its thumbnail lookup.
        internal static bool RecoverReferencedRuntimeMagicItem(string itemId)
        {
            if (String.IsNullOrEmpty(itemId) ||
                !RepairingIds.Add(itemId))
                return false;

            try
            {
                if (_hasAnyLootTraits == null ||
                    _isRuntimeDuplicatedItem == null)
                    return false;

                MBObjectManager manager = MBObjectManager.Instance;
                if (manager == null ||
                    manager.GetObject<ItemObject>(itemId) != null)
                    return false;

                ItemObject item = FindReferencedItemById(itemId);
                if (item == null || item.IsCraftedByPlayer ||
                    !InvokeItemPredicate(_hasAnyLootTraits, item) ||
                    !InvokeItemPredicate(_isRuntimeDuplicatedItem, item))
                    return false;

                manager.RegisterObject(item);
                bool repaired = Object.ReferenceEquals(
                    manager.GetObject<ItemObject>(itemId), item);
                if (repaired)
                {
                    ModLog.AlwaysInfo("Re-registered referenced TOR runtime " +
                        "magic item '" + itemId +
                        "' after an earlier unsafe cleanup removed its object " +
                        "manager index. Thumbnail loading can resume.");
                }
                return repaired;
            }
            catch (Exception ex)
            {
                if (!_loggedRepairFailure)
                {
                    _loggedRepairFailure = true;
                    ModLog.Error("Could not repair an unregistered referenced " +
                        "TOR magic item: " + ex.GetType().Name + ": " +
                        ex.Message);
                }
                return false;
            }
            finally
            {
                RepairingIds.Remove(itemId);
            }
        }

        private static bool InvokeItemPredicate(MethodInfo predicate,
            ItemObject item)
        {
            object result = predicate.Invoke(null, new object[] { item });
            return result is bool && (bool)result;
        }

        private static bool HasProtectedReference(ItemObject item)
        {
            if (item == null)
                return false;

            Clan playerClan = Clan.PlayerClan;
            foreach (MobileParty party in MobileParty.All)
            {
                if (party == null || party.ItemRoster == null)
                    continue;

                bool playerParty =
                    Object.ReferenceEquals(party, MobileParty.MainParty) ||
                    (playerClan != null &&
                        (Object.ReferenceEquals(party.ActualClan, playerClan) ||
                         (party.LeaderHero != null &&
                          Object.ReferenceEquals(party.LeaderHero.Clan,
                              playerClan))));
                if (playerParty && RosterContains(party.ItemRoster, item))
                    return true;
            }

            HashSet<Settlement> protectedSettlements =
                GetProtectedSettlements();
            foreach (Settlement settlement in protectedSettlements)
            {
                if (settlement != null &&
                    (RosterContains(settlement.ItemRoster, item) ||
                     RosterContains(settlement.Stash, item) ||
                     RosterContains(settlement.Party == null ? null :
                         settlement.Party.ItemRoster, item)))
                    return true;
            }

            // TOR's original scan uses CharacterObject armour only. The actual
            // live state is both Equipment sets for every living Hero, including
            // weapon slots and inactive multi-character protagonists.
            foreach (Hero hero in Hero.AllAliveHeroes)
            {
                if (hero != null &&
                    (EquipmentContains(hero.BattleEquipment, item) ||
                     EquipmentContains(hero.CivilianEquipment, item)))
                    return true;
            }

            return RosterContains(GetPendingPlayerLootRoster(), item);
        }

        private static HashSet<Settlement> GetProtectedSettlements()
        {
            HashSet<Settlement> result = new HashSet<Settlement>();
            Hero mainHero = Hero.MainHero;
            Clan playerClan = Clan.PlayerClan;
            Kingdom playerKingdom = playerClan == null ? null :
                playerClan.Kingdom;

            foreach (Settlement settlement in Settlement.All)
            {
                if (settlement == null)
                    continue;
                if (playerClan != null &&
                    Object.ReferenceEquals(settlement.OwnerClan, playerClan))
                {
                    result.Add(settlement);
                    continue;
                }
                if (mainHero != null && mainHero.IsKingdomLeader &&
                    playerKingdom != null &&
                    Object.ReferenceEquals(settlement.MapFaction,
                        playerKingdom))
                {
                    result.Add(settlement);
                }
            }

            if (mainHero != null)
            {
                foreach (Workshop workshop in mainHero.OwnedWorkshops)
                {
                    if (workshop != null && workshop.Settlement != null)
                        result.Add(workshop.Settlement);
                }
            }
            return result;
        }

        private static bool HasAnyGlobalReference(ItemObject item)
        {
            foreach (MobileParty party in MobileParty.All)
            {
                if (party != null && RosterContains(party.ItemRoster, item))
                    return true;
            }

            foreach (Settlement settlement in Settlement.All)
            {
                if (settlement != null &&
                    (RosterContains(settlement.ItemRoster, item) ||
                     RosterContains(settlement.Stash, item) ||
                     RosterContains(settlement.Party == null ? null :
                         settlement.Party.ItemRoster, item)))
                    return true;
            }

            foreach (Hero hero in Hero.AllAliveHeroes)
            {
                if (hero != null &&
                    (EquipmentContains(hero.BattleEquipment, item) ||
                     EquipmentContains(hero.CivilianEquipment, item)))
                    return true;
            }

            return RosterContains(GetPendingPlayerLootRoster(), item);
        }

        private static ItemObject FindReferencedItemById(string itemId)
        {
            foreach (MobileParty party in MobileParty.All)
            {
                ItemObject item = FindInRoster(
                    party == null ? null : party.ItemRoster, itemId);
                if (item != null)
                    return item;
            }

            foreach (Settlement settlement in Settlement.All)
            {
                if (settlement == null)
                    continue;
                ItemObject item = FindInRoster(settlement.ItemRoster, itemId) ??
                    FindInRoster(settlement.Stash, itemId) ??
                    FindInRoster(settlement.Party == null ? null :
                        settlement.Party.ItemRoster, itemId);
                if (item != null)
                    return item;
            }

            foreach (Hero hero in Hero.AllAliveHeroes)
            {
                if (hero == null)
                    continue;
                ItemObject item = FindInEquipment(hero.BattleEquipment,
                    itemId) ?? FindInEquipment(hero.CivilianEquipment, itemId);
                if (item != null)
                    return item;
            }

            return FindInRoster(GetPendingPlayerLootRoster(), itemId);
        }

        private static ItemRoster GetPendingPlayerLootRoster()
        {
            try
            {
                object encounter = PlayerEncounter.Current;
                if (encounter == null)
                    return null;
                PropertyInfo property = encounter.GetType().GetProperty(
                    "RosterToReceiveLootItems",
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance);
                return property == null ? null :
                    property.GetValue(encounter, null) as ItemRoster;
            }
            catch
            {
                return null;
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
                    Object.ReferenceEquals(element.EquipmentElement.Item,
                        item))
                    return true;
            }
            return false;
        }

        private static ItemObject FindInRoster(ItemRoster roster,
            string itemId)
        {
            if (roster == null)
                return null;
            foreach (ItemRosterElement element in roster)
            {
                ItemObject item = element.EquipmentElement.Item;
                if (element.Amount > 0 && item != null &&
                    String.Equals(item.StringId, itemId,
                        StringComparison.Ordinal))
                    return item;
            }
            return null;
        }

        private static bool EquipmentContains(Equipment equipment,
            ItemObject item)
        {
            if (equipment == null || item == null)
                return false;
            for (int i = 0;
                i < (int)EquipmentIndex.NumEquipmentSetSlots; i++)
            {
                EquipmentElement element = equipment.GetEquipmentFromSlot(
                    (EquipmentIndex)i);
                if (Object.ReferenceEquals(element.Item, item))
                    return true;
            }
            return false;
        }

        private static ItemObject FindInEquipment(Equipment equipment,
            string itemId)
        {
            if (equipment == null)
                return null;
            for (int i = 0;
                i < (int)EquipmentIndex.NumEquipmentSetSlots; i++)
            {
                ItemObject item = equipment.GetEquipmentFromSlot(
                    (EquipmentIndex)i).Item;
                if (item != null && String.Equals(item.StringId, itemId,
                    StringComparison.Ordinal))
                    return item;
            }
            return null;
        }
    }
}
