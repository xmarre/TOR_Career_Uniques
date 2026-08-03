using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
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

        private static readonly object RecoverySync = new object();
        private static readonly HashSet<string> RepairingIds =
            new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> UnrecoverableIds =
            new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> LoggedRetainedIds =
            new HashSet<string>(StringComparer.Ordinal);

        private static MethodInfo _hasAnyLootTraits;
        private static MethodInfo _isRuntimeDuplicatedItem;
        private static Type _encounterTypeForLootRoster;
        private static PropertyInfo _pendingLootRosterProperty;
        private static object _campaignSession;
        private static bool _installed;
        private static bool _loggedRepairFailure;
        private static bool _loggedOwnershipScanFailure;

        [ThreadStatic]
        private static int _cleanupDepth;
        [ThreadStatic]
        private static bool _cleanupScanFailed;
        [ThreadStatic]
        private static HashSet<ItemObject> _protectedCleanupReferences;
        [ThreadStatic]
        private static HashSet<ItemObject> _globalCleanupReferences;

        private sealed class ItemObjectReferenceComparer :
            IEqualityComparer<ItemObject>
        {
            internal static readonly ItemObjectReferenceComparer Instance =
                new ItemObjectReferenceComparer();

            public bool Equals(ItemObject left, ItemObject right)
            {
                return Object.ReferenceEquals(left, right);
            }

            public int GetHashCode(ItemObject item)
            {
                return item == null ? 0 : RuntimeHelpers.GetHashCode(item);
            }
        }

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
                    "guards. TOR's weekly cleanup keeps its original roster " +
                    "removal scope and can no longer unregister a live item " +
                    "reference.");
            }
            catch (Exception ex)
            {
                ModLog.Error("TOR runtime magic-item lifetime fix could not be " +
                    "installed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        internal static void ResetSession()
        {
            lock (RecoverySync)
            {
                ClearRecoveryStateLocked();
                _campaignSession = null;
            }
            _loggedRepairFailure = false;
            _loggedOwnershipScanFailure = false;
            _encounterTypeForLootRoster = null;
            _pendingLootRosterProperty = null;
        }

        private static void ClearRecoveryStateLocked()
        {
            RepairingIds.Clear();
            UnrecoverableIds.Clear();
            LoggedRetainedIds.Clear();
        }

        private static void EnsureRecoverySessionLocked()
        {
            object campaign = Campaign.Current;
            if (Object.ReferenceEquals(_campaignSession, campaign))
                return;
            ClearRecoveryStateLocked();
            _campaignSession = campaign;
        }

        private static void BeforeRemovedUnusedLootItems()
        {
            if (_cleanupDepth == 0)
            {
                _cleanupScanFailed = false;
                _protectedCleanupReferences = null;
                _globalCleanupReferences = null;
                lock (RecoverySync)
                {
                    EnsureRecoverySessionLocked();
                    UnrecoverableIds.Clear();
                }

                try
                {
                    BuildCleanupReferenceCaches();
                }
                catch (Exception ex)
                {
                    _cleanupScanFailed = true;
                    LogOwnershipScanFailure(ex);
                }
            }
            _cleanupDepth++;
        }

        private static Exception AfterRemovedUnusedLootItems(
            Exception __exception)
        {
            if (_cleanupDepth > 0)
                _cleanupDepth--;
            if (_cleanupDepth == 0)
            {
                _cleanupScanFailed = false;
                _protectedCleanupReferences = null;
                _globalCleanupReferences = null;
            }
            return __exception;
        }

        // During TOR's candidate query, hide items found in authoritative player
        // locations or any hero equipment. The reference set is built once for the
        // weekly pass, so candidate checks remain constant-time.
        private static void AfterHasAnyLootTraits(ItemObject __0,
            ref bool __result)
        {
            if (_cleanupDepth <= 0 || !__result || __0 == null)
                return;

            if (_cleanupScanFailed || _protectedCleanupReferences == null ||
                _protectedCleanupReferences.Contains(__0))
            {
                // Ownership uncertainty must never fall through to TOR's
                // destructive cleanup path. Skip this candidate and retain it.
                __result = false;
            }
        }

        // The pass-level global set reflects every reference at cleanup entry. TOR
        // may remove unprotected stacks during the pass; retaining their object
        // registration until the next weekly pass is harmless and avoids repeating
        // a full world traversal for every cleanup candidate. Items with no entry
        // reference are unregistered normally in the current pass.
        private static bool BeforeUnregisterObject(MBObjectBase __0)
        {
            if (_cleanupDepth <= 0)
                return true;

            ItemObject item = __0 as ItemObject;
            if (item == null)
                return true;
            if (_cleanupScanFailed || _globalCleanupReferences == null)
                return false;
            if (!_globalCleanupReferences.Contains(item))
                return true;

            string id = item.StringId ?? "<no-id>";
            bool shouldLog;
            lock (RecoverySync)
                shouldLog = LoggedRetainedIds.Add(id);
            if (shouldLog)
            {
                ModLog.Info("Prevented TOR's weekly cleanup from unregistering " +
                    "still-referenced magic item '" + id + "'.");
            }
            return false;
        }

        private static void BuildCleanupReferenceCaches()
        {
            HashSet<ItemObject> protectedItems = new HashSet<ItemObject>(
                ItemObjectReferenceComparer.Instance);
            HashSet<ItemObject> globalItems = new HashSet<ItemObject>(
                ItemObjectReferenceComparer.Instance);
            HashSet<Settlement> protectedSettlements =
                GetProtectedSettlements();
            Clan playerClan = Clan.PlayerClan;

            foreach (MobileParty party in MobileParty.All)
            {
                if (party == null)
                    continue;
                AddRosterItems(party.ItemRoster, globalItems);
                bool playerParty =
                    Object.ReferenceEquals(party, MobileParty.MainParty) ||
                    (playerClan != null &&
                        (Object.ReferenceEquals(party.ActualClan, playerClan) ||
                         (party.LeaderHero != null &&
                          Object.ReferenceEquals(party.LeaderHero.Clan,
                              playerClan))));
                if (playerParty)
                    AddRosterItems(party.ItemRoster, protectedItems);
            }

            foreach (Settlement settlement in Settlement.All)
            {
                if (settlement == null)
                    continue;
                AddRosterItems(settlement.ItemRoster, globalItems);
                AddRosterItems(settlement.Stash, globalItems);
                AddRosterItems(settlement.Party == null ? null :
                    settlement.Party.ItemRoster, globalItems);
                if (!protectedSettlements.Contains(settlement))
                    continue;
                AddRosterItems(settlement.ItemRoster, protectedItems);
                AddRosterItems(settlement.Stash, protectedItems);
                AddRosterItems(settlement.Party == null ? null :
                    settlement.Party.ItemRoster, protectedItems);
            }

            foreach (Hero hero in EnumerateReferencedHeroes())
            {
                if (hero == null)
                    continue;
                AddEquipmentItems(hero.BattleEquipment, globalItems);
                AddEquipmentItems(hero.CivilianEquipment, globalItems);
                AddEquipmentItems(hero.BattleEquipment, protectedItems);
                AddEquipmentItems(hero.CivilianEquipment, protectedItems);
            }

            ItemRoster pendingLoot = GetPendingPlayerLootRoster();
            AddRosterItems(pendingLoot, globalItems);
            AddRosterItems(pendingLoot, protectedItems);
            _protectedCleanupReferences = protectedItems;
            _globalCleanupReferences = globalItems;
        }

        private static void AddRosterItems(ItemRoster roster,
            HashSet<ItemObject> target)
        {
            if (roster == null || target == null)
                return;
            foreach (ItemRosterElement element in roster)
            {
                ItemObject item = element.EquipmentElement.Item;
                if (element.Amount > 0 && item != null)
                    target.Add(item);
            }
        }

        private static void AddEquipmentItems(Equipment equipment,
            HashSet<ItemObject> target)
        {
            if (equipment == null || target == null)
                return;
            for (int i = 0;
                i < (int)EquipmentIndex.NumEquipmentSetSlots; i++)
            {
                ItemObject item = equipment.GetEquipmentFromSlot(
                    (EquipmentIndex)i).Item;
                if (item != null)
                    target.Add(item);
            }
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

        // Called reflectively by ItemImageTextureProvider.OnCreateImageWithId.
        // That method is entered synchronously from TextureProvider.Tick before
        // Bannerlord starts thumbnail generation, so the item must be repaired in
        // this call. Shared retry/cache state is locked in case another caller
        // reaches the helper concurrently.
        internal static bool RecoverReferencedRuntimeMagicItem(string itemId)
        {
            if (String.IsNullOrEmpty(itemId))
                return false;
            MBObjectManager manager = MBObjectManager.Instance;
            if (manager == null)
                return false;
            if (manager.GetObject<ItemObject>(itemId) != null)
            {
                lock (RecoverySync)
                {
                    EnsureRecoverySessionLocked();
                    UnrecoverableIds.Remove(itemId);
                }
                return false;
            }

            lock (RecoverySync)
            {
                EnsureRecoverySessionLocked();
                if (UnrecoverableIds.Contains(itemId) ||
                    !RepairingIds.Add(itemId))
                    return false;
            }

            try
            {
                if (_hasAnyLootTraits == null ||
                    _isRuntimeDuplicatedItem == null)
                    return false;

                ItemObject item = FindReferencedItemById(itemId);
                if (item == null || item.IsCraftedByPlayer ||
                    !InvokeItemPredicate(_hasAnyLootTraits, item) ||
                    !InvokeItemPredicate(_isRuntimeDuplicatedItem, item))
                {
                    lock (RecoverySync)
                        UnrecoverableIds.Add(itemId);
                    return false;
                }

                manager.RegisterObject(item);
                bool repaired = Object.ReferenceEquals(
                    manager.GetObject<ItemObject>(itemId), item);
                if (repaired)
                {
                    lock (RecoverySync)
                        UnrecoverableIds.Remove(itemId);
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
                lock (RecoverySync)
                    RepairingIds.Remove(itemId);
            }
        }

        private static bool InvokeItemPredicate(MethodInfo predicate,
            ItemObject item)
        {
            object result = predicate.Invoke(null, new object[] { item });
            return result is bool && (bool)result;
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

        private static IEnumerable<Hero> EnumerateReferencedHeroes()
        {
            HashSet<Hero> visited = new HashSet<Hero>();
            foreach (Hero hero in Hero.AllAliveHeroes)
            {
                if (hero != null && visited.Add(hero))
                    yield return hero;
            }

            PropertyInfo property = typeof(Hero).GetProperty(
                "DeadOrDisabledHeroes", BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.Static);
            IEnumerable values = property == null ? null :
                property.GetValue(null, null) as IEnumerable;
            if (values == null)
                yield break;
            foreach (object value in values)
            {
                Hero hero = value as Hero;
                if (hero != null && visited.Add(hero))
                    yield return hero;
            }
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

            foreach (Hero hero in EnumerateReferencedHeroes())
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
            object encounter = PlayerEncounter.Current;
            if (encounter == null)
                return null;

            Type encounterType = encounter.GetType();
            if (encounterType != _encounterTypeForLootRoster)
            {
                _encounterTypeForLootRoster = encounterType;
                _pendingLootRosterProperty = encounterType.GetProperty(
                    "RosterToReceiveLootItems",
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance);
            }

            return _pendingLootRosterProperty == null ? null :
                _pendingLootRosterProperty.GetValue(encounter, null)
                    as ItemRoster;
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
