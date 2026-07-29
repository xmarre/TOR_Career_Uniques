using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace TORCareerUniques
{
    internal static partial class SetItemRuntime
    {
        private const string CompanionSetHarmonyId =
            "torcareeruniques.sets.player-clan-heroes";
        private const string InventoryStateTypeName =
            "TaleWorlds.CampaignSystem.GameState.InventoryState";

        private static readonly List<HeroSetSnapshot> PlayerHeroSetSnapshots =
            new List<HeroSetSnapshot>();
        private static readonly Dictionary<object, object> PlayerHeroByBattleEquipment =
            new Dictionary<object, object>(ReferenceObjectComparer.Instance);
        private static readonly Dictionary<object, HeroSetSnapshot> SetSnapshotByItemObject =
            new Dictionary<object, HeroSetSnapshot>(ReferenceObjectComparer.Instance);
        private static readonly Dictionary<string, HeroSetSnapshot> SetSnapshotByUniqueItemId =
            new Dictionary<string, HeroSetSnapshot>(StringComparer.Ordinal);
        private static readonly HashSet<string> AmbiguousEquippedSetItemIds =
            new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<object> DirtyPlayerBattleEquipment =
            new HashSet<object>(ReferenceObjectComparer.Instance);

        private static object _companionSetCampaignSession;
        private static object _companionSetSnapshotMainHero;
        private static HeroSetSnapshot _mainHeroSetSnapshot;
        private static bool _companionSetSnapshotDirty = true;
        private static bool _companionSetSnapshotAvailable;
        private static bool _forceFullCompanionSetSnapshot = true;
        private static bool _companionSetRefreshInProgress;
        private static bool _internalCompanionCarrierMutation;
        private static bool _companionSetSupportInstallAttempted;
        private static bool _companionCarrierIsolationPending;

        private sealed class HeroSetSnapshot
        {
            internal object Hero;
            internal object BattleEquipment;
            internal string HeroKey;
            internal Dictionary<string, EquippedSetState> StateByCareer;
        }

        private sealed class HeroDesiredTraits
        {
            internal HeroSetSnapshot Snapshot;
            internal readonly Dictionary<string, List<string>> Desired =
                new Dictionary<string, List<string>>(StringComparer.Ordinal);
            internal readonly Dictionary<string, object> TargetItems =
                new Dictionary<string, object>(StringComparer.Ordinal);
        }

        private sealed class TraitRollback
        {
            internal string ItemId;
            internal List<string> ConditionalTraits;
            internal bool HadAppliedKey;
            internal string AppliedKey;
        }

        private sealed class ReferenceObjectComparer : IEqualityComparer<object>
        {
            internal static readonly ReferenceObjectComparer Instance =
                new ReferenceObjectComparer();

            public new bool Equals(object x, object y)
            {
                return Object.ReferenceEquals(x, y);
            }

            public int GetHashCode(object obj)
            {
                return obj == null ? 0 :
                    System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
            }
        }

        internal static void InitializeCompanionSetSupport()
        {
            if (_companionSetSupportInstallAttempted)
                return;
            _companionSetSupportInstallAttempted = true;
            TryInstallCompanionSetSupport();
        }

        private static bool TryInstallCompanionSetSupport()
        {
            try
            {
                return InstallCompanionSetSupport();
            }
            catch (Exception ex)
            {
                ModLog.Error("Player-clan companion set support could not be installed; " +
                    "the controlled-hero set runtime remains active. " +
                    ex.GetType().Name + ": " + ex.Message);
                return false;
            }
        }

        private static bool InstallCompanionSetSupport()
        {
            Type harmonyType = FindCrossCultureHarmonyType(
                "HarmonyLib.Harmony", "0Harmony");
            Type harmonyMethodType = FindCrossCultureHarmonyType(
                "HarmonyLib.HarmonyMethod", "0Harmony");
            if (harmonyType == null || harmonyMethodType == null)
                throw new TypeLoadException(
                    "HarmonyLib is unavailable while installing companion set support.");

            object harmony = Activator.CreateInstance(harmonyType,
                new object[] { CompanionSetHarmonyId });

            MethodInfo refreshBonuses = FindCompanionSetMethod(
                typeof(SetItemRuntime), "RefreshEquippedBonuses", 1,
                BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo refreshPrefix = typeof(SetItemRuntime).GetMethod(
                nameof(ReplaceMainHeroOnlyBonusRefresh),
                BindingFlags.NonPublic | BindingFlags.Static);
            PatchCompanionSetMethod(harmonyType, harmonyMethodType, harmony,
                refreshBonuses, refreshPrefix, null);

            MethodInfo tooltip = FindCompanionSetMethod(
                typeof(SetItemRuntime), "TryBuildTooltipForItemViewModel", 4,
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
            MethodInfo tooltipPrefix = typeof(SetItemRuntime).GetMethod(
                nameof(BuildCompanionAwareTooltip),
                BindingFlags.NonPublic | BindingFlags.Static);
            PatchCompanionSetMethod(harmonyType, harmonyMethodType, harmony,
                tooltip, tooltipPrefix, null);

            MethodInfo equipmentRefresh = typeof(RuntimePerformanceGate).GetMethod(
                "AfterEquipmentMutation", BindingFlags.Public | BindingFlags.Static,
                null, new[] { typeof(object) }, null);
            MethodInfo equipmentPrefix = typeof(SetItemRuntime).GetMethod(
                nameof(BeforeRuntimeEquipmentMutation),
                BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo equipmentPostfix = typeof(SetItemRuntime).GetMethod(
                nameof(AfterRuntimeEquipmentMutation),
                BindingFlags.NonPublic | BindingFlags.Static);
            PatchCompanionSetMethod(harmonyType, harmonyMethodType, harmony,
                equipmentRefresh, equipmentPrefix, equipmentPostfix);

            MethodInfo inventoryActivated = typeof(RuntimePerformanceGate).GetMethod(
                "AfterInventoryStateActivated", BindingFlags.Public | BindingFlags.Static,
                null, Type.EmptyTypes, null);
            MethodInfo inventoryPrefix = typeof(SetItemRuntime).GetMethod(
                nameof(BeforeInventoryStateActivated),
                BindingFlags.NonPublic | BindingFlags.Static);
            PatchCompanionSetMethod(harmonyType, harmonyMethodType, harmony,
                inventoryActivated, inventoryPrefix, null);

            MethodInfo sessionLaunched = typeof(RuntimePerformanceGate).GetMethod(
                "OnCampaignSessionLaunched", BindingFlags.Public | BindingFlags.Static,
                null, Type.EmptyTypes, null);
            MethodInfo sessionPostfix = typeof(SetItemRuntime).GetMethod(
                nameof(AfterCampaignSessionLaunched),
                BindingFlags.NonPublic | BindingFlags.Static);
            PatchCompanionSetMethod(harmonyType, harmonyMethodType, harmony,
                sessionLaunched, null, sessionPostfix);

            MethodInfo resetSession = typeof(RuntimePerformanceGate).GetMethod(
                "ResetSession", BindingFlags.Public | BindingFlags.Static,
                null, Type.EmptyTypes, null);
            MethodInfo resetPostfix = typeof(SetItemRuntime).GetMethod(
                nameof(AfterRuntimeSessionReset),
                BindingFlags.NonPublic | BindingFlags.Static);
            PatchCompanionSetMethod(harmonyType, harmonyMethodType, harmony,
                resetSession, null, resetPostfix);

            MethodInfo rosterPostfix = typeof(SetItemRuntime).GetMethod(
                nameof(AfterPlayerClanRosterChanged),
                BindingFlags.NonPublic | BindingFlags.Static);
            PatchOptionalActionMethods(harmonyType, harmonyMethodType, harmony,
                "TaleWorlds.CampaignSystem.Actions.AddCompanionAction",
                "Apply", rosterPostfix);
            PatchOptionalActionMethods(harmonyType, harmonyMethodType, harmony,
                "TaleWorlds.CampaignSystem.Actions.RemoveCompanionAction",
                "Apply", rosterPostfix);

            ModLog.Info("Installed event-driven, per-hero set support for player-clan companions.");
            return true;
        }

        private static MethodInfo FindCompanionSetMethod(Type type, string name,
            int parameterCount, BindingFlags flags)
        {
            if (type == null)
                return null;
            MethodInfo[] methods = type.GetMethods(flags);
            for (int i = 0; i < methods.Length; i++)
                if (methods[i].Name == name &&
                    methods[i].GetParameters().Length == parameterCount)
                    return methods[i];
            return null;
        }

        private static void PatchCompanionSetMethod(Type harmonyType,
            Type harmonyMethodType, object harmony, MethodInfo original,
            MethodInfo prefixMethod, MethodInfo postfixMethod)
        {
            if (original == null)
                throw new MissingMethodException(
                    "Companion set Harmony target could not be resolved.");
            if (prefixMethod == null && postfixMethod == null)
                throw new MissingMethodException(
                    "Companion set Harmony patch method could not be resolved.");

            object prefix = prefixMethod == null ? null :
                CreateCrossCultureHarmonyMethod(harmonyMethodType, prefixMethod);
            object postfix = postfixMethod == null ? null :
                CreateCrossCultureHarmonyMethod(harmonyMethodType, postfixMethod);
            ApplyCompanionSetHarmonyPatch(harmonyType, harmony, original,
                prefix, postfix);
        }

        private static void PatchOptionalActionMethods(Type harmonyType,
            Type harmonyMethodType, object harmony, string typeName,
            string methodName, MethodInfo postfixMethod)
        {
            Type actionType = FindCrossCultureHarmonyType(typeName,
                "TaleWorlds.CampaignSystem");
            if (actionType == null || postfixMethod == null)
                return;

            MethodInfo[] methods = actionType.GetMethods(BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.Static);
            for (int i = 0; i < methods.Length; i++)
            {
                if (methods[i].Name != methodName ||
                    methods[i].ContainsGenericParameters)
                    continue;
                try
                {
                    object postfix = CreateCrossCultureHarmonyMethod(
                        harmonyMethodType, postfixMethod);
                    ApplyCompanionSetHarmonyPatch(harmonyType, harmony,
                        methods[i], null, postfix);
                }
                catch (Exception ex)
                {
                    ModLog.Verbose("Optional player-clan roster hook skipped for " +
                        typeName + "." + methodName + ": " +
                        ex.GetType().Name + ": " + ex.Message);
                }
            }
        }

        private static void ApplyCompanionSetHarmonyPatch(Type harmonyType,
            object harmony, MethodInfo original, object prefix, object postfix)
        {
            MethodInfo[] methods = harmonyType.GetMethods(
                BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo candidate = methods[i];
                if (candidate.Name != "Patch")
                    continue;
                ParameterInfo[] parameters = candidate.GetParameters();
                if (parameters.Length < 2 ||
                    !typeof(MethodBase).IsAssignableFrom(parameters[0].ParameterType))
                    continue;

                object[] args = new object[parameters.Length];
                args[0] = original;
                bool usable = true;
                for (int p = 1; p < parameters.Length; p++)
                {
                    string name = parameters[p].Name ?? String.Empty;
                    if (String.Equals(name, "prefix",
                        StringComparison.OrdinalIgnoreCase))
                        args[p] = prefix;
                    else if (String.Equals(name, "postfix",
                        StringComparison.OrdinalIgnoreCase))
                        args[p] = postfix;
                    else if (parameters[p].HasDefaultValue)
                        args[p] = parameters[p].DefaultValue;
                    else if (!parameters[p].ParameterType.IsValueType)
                        args[p] = null;
                    else
                    {
                        usable = false;
                        break;
                    }
                }
                if (!usable)
                    continue;
                candidate.Invoke(harmony, args);
                return;
            }
            throw new MissingMethodException(harmonyType.FullName,
                "Patch(MethodBase, HarmonyMethod prefix, HarmonyMethod postfix)");
        }

        private static bool ReplaceMainHeroOnlyBonusRefresh(
            Dictionary<string, EquippedSetState> __0)
        {
            try
            {
                RefreshAllPlayerHeroBonuses(__0);
                return false;
            }
            catch (Exception ex)
            {
                LogOnce("companion-set-refresh:" + ex.GetType().FullName + ":" +
                    ex.Message, "Companion set refresh failed; falling back to the " +
                    "controlled-hero runtime for this refresh. " + FormatException(ex));
                return true;
            }
        }

        private static void RefreshAllPlayerHeroBonuses(
            Dictionary<string, EquippedSetState> mainHeroState)
        {
            EnsureCompanionSetSession();
            object mainHero = GetMainHeroIfReady();
            if (!Object.ReferenceEquals(mainHero, _companionSetSnapshotMainHero))
            {
                _forceFullCompanionSetSnapshot = true;
                _companionSetSnapshotDirty = true;
            }

            if (!_companionSetSnapshotDirty)
                return;
            if (_companionSetRefreshInProgress)
                return;

            _companionSetRefreshInProgress = true;
            try
            {
                EnsureCurrentPlayerHeroSnapshots(mainHero, mainHeroState);
                List<HeroDesiredTraits> plans = BuildPlayerHeroDesiredTraits();
                Dictionary<string, List<HeroDesiredTraits>> equippedUsers =
                    BuildEquippedItemUsersById();
                bool hasSharedTargets = HasSharedDesiredTargets(plans,
                    equippedUsers);

                // Item traits are keyed globally by item id. Shared carriers therefore
                // require a hero-specific persistent copy, but replacing an EquipmentElement
                // while InventoryLogic still owns the old element can corrupt the next
                // unequip/drop operation. Defer only this rare isolation path until the
                // inventory state has closed; all ordinary companion refreshes stay immediate.
                if (hasSharedTargets && IsCompanionInventoryStateActive())
                {
                    _companionCarrierIsolationPending = true;
                    return;
                }
                _companionCarrierIsolationPending = false;

                if (hasSharedTargets &&
                    IsolateSharedDesiredTargets(plans, equippedUsers))
                {
                    _forceFullCompanionSetSnapshot = true;
                    EnsureCurrentPlayerHeroSnapshots(mainHero, null);
                    plans = BuildPlayerHeroDesiredTraits();
                    ThrowIfDesiredTargetsRemainShared(plans);
                }

                ApplyPlayerHeroBonusesTransactionally(plans);
                _companionSetSnapshotDirty = false;
                DirtyPlayerBattleEquipment.Clear();
            }
            finally
            {
                _companionSetRefreshInProgress = false;
            }
        }

        private static void EnsureCurrentPlayerHeroSnapshots(object mainHero,
            Dictionary<string, EquippedSetState> mainHeroState)
        {
            if (!_companionSetSnapshotAvailable ||
                _forceFullCompanionSetSnapshot ||
                !Object.ReferenceEquals(mainHero, _companionSetSnapshotMainHero))
            {
                RebuildPlayerHeroSetSnapshots(mainHero, mainHeroState);
                _forceFullCompanionSetSnapshot = false;
                DirtyPlayerBattleEquipment.Clear();
                return;
            }

            if (DirtyPlayerBattleEquipment.Count == 0)
                return;

            object[] dirty = new object[DirtyPlayerBattleEquipment.Count];
            DirtyPlayerBattleEquipment.CopyTo(dirty);
            DirtyPlayerBattleEquipment.Clear();

            for (int i = 0; i < dirty.Length; i++)
            {
                object equipment = dirty[i];

                // Invalidate the cached owner and snapshot first. If the hero died or
                // left the clan, the live lookup below must be allowed to return null so
                // orphaned desired traits cannot survive through a stale dictionary entry.
                PlayerHeroByBattleEquipment.Remove(equipment);
                for (int s = PlayerHeroSetSnapshots.Count - 1; s >= 0; s--)
                    if (Object.ReferenceEquals(
                        PlayerHeroSetSnapshots[s].BattleEquipment, equipment))
                        PlayerHeroSetSnapshots.RemoveAt(s);

                object hero = FindPlayerHeroByBattleEquipment(equipment);
                if (hero == null)
                    continue;

                bool isMainHero = Object.ReferenceEquals(hero, mainHero);
                Dictionary<string, EquippedSetState> supplied =
                    isMainHero ? mainHeroState : null;
                AddPlayerHeroSetSnapshot(hero, supplied, isMainHero);
            }

            RebuildSnapshotIndexes();
            _companionSetSnapshotMainHero = mainHero;
        }

        private static void RebuildPlayerHeroSetSnapshots(object mainHero,
            Dictionary<string, EquippedSetState> mainHeroState)
        {
            PlayerHeroSetSnapshots.Clear();
            _mainHeroSetSnapshot = null;

            HashSet<object> visited = new HashSet<object>(
                ReferenceObjectComparer.Instance);
            if (mainHero != null)
            {
                AddPlayerHeroSetSnapshot(mainHero, mainHeroState, true);
                visited.Add(mainHero);
            }

            object playerClan = GetStaticProperty(
                TypeByName("TaleWorlds.CampaignSystem.Clan"), "PlayerClan");
            IEnumerable heroes = playerClan == null ? null :
                GetProperty(playerClan, "Heroes") as IEnumerable;
            if (heroes != null)
            {
                foreach (object hero in heroes)
                {
                    if (hero == null || visited.Contains(hero) ||
                        ToBoolean(GetProperty(hero, "IsDead")))
                        continue;
                    visited.Add(hero);
                    AddPlayerHeroSetSnapshot(hero, null, false);
                }
            }

            RebuildSnapshotIndexes();
            _companionSetSnapshotMainHero = mainHero;
            _companionSetSnapshotAvailable = true;
        }

        private static void AddPlayerHeroSetSnapshot(object hero,
            Dictionary<string, EquippedSetState> suppliedState,
            bool isMainHero)
        {
            object equipment = GetProperty(hero, "BattleEquipment");
            if (equipment == null)
                return;

            HeroSetSnapshot snapshot = new HeroSetSnapshot
            {
                Hero = hero,
                BattleEquipment = equipment,
                HeroKey = GetHeroSnapshotKey(hero),
                StateByCareer = suppliedState ??
                    ScanHeroSetState(hero, isMainHero)
            };

            PlayerHeroSetSnapshots.Add(snapshot);
            if (isMainHero)
                _mainHeroSetSnapshot = snapshot;
        }

        private static Dictionary<string, EquippedSetState> ScanHeroSetState(
            object hero, bool includePersistentEncounterHeroCopies)
        {
            Dictionary<string, EquippedSetState> stateByCareer =
                new Dictionary<string, EquippedSetState>(
                    StringComparer.OrdinalIgnoreCase);
            object equipment = GetProperty(hero, "BattleEquipment");
            if (equipment == null)
                return stateByCareer;

            List<EquippedItemRef> equippedItems = new List<EquippedItemRef>();
            foreach (object element in EnumerateEquipmentElements(equipment))
            {
                object item = GetProperty(element, "Item");
                if (item == null)
                    continue;

                string itemId = Convert.ToString(GetProperty(item, "StringId"));
                if (String.IsNullOrEmpty(itemId))
                    continue;

                IList traits = GetItemTraits(itemId);
                if (!includePersistentEncounterHeroCopies &&
                    HasHeroSignature(traits))
                    continue;

                equippedItems.Add(new EquippedItemRef
                {
                    ItemId = itemId,
                    Item = item,
                    ItemTypeName = GetItemTypeName(item)
                });

                PieceSignature signature = FindPieceSignature(traits);
                if (signature == null)
                {
                    SetItemInstance known;
                    if (KnownSetItemsById.TryGetValue(itemId, out known) &&
                        known != null)
                        signature = known.Signature;
                }
                if (signature == null)
                    signature = FindPieceSignatureByName(item);
                if (signature == null)
                    continue;

                EquippedSetState setState;
                if (!stateByCareer.TryGetValue(
                    signature.Definition.CareerId, out setState))
                {
                    setState = new EquippedSetState(signature.Definition);
                    stateByCareer.Add(signature.Definition.CareerId, setState);
                }

                setState.PieceIndices.Add(signature.PieceIndex);
                setState.ItemIdsByPiece[signature.PieceIndex] = itemId;
                if (signature.PieceIndex == 0)
                {
                    setState.RelicItemId = itemId;
                    setState.RelicItem = item;
                }
                else if (String.IsNullOrEmpty(setState.CarrierItemId))
                {
                    setState.CarrierItemId = itemId;
                    setState.CarrierItem = item;
                }
            }

            foreach (EquippedSetState setState in stateByCareer.Values)
                setState.EquippedItems.AddRange(equippedItems);
            return stateByCareer;
        }

        private static void RebuildSnapshotIndexes()
        {
            PlayerHeroByBattleEquipment.Clear();
            SetSnapshotByItemObject.Clear();
            SetSnapshotByUniqueItemId.Clear();
            AmbiguousEquippedSetItemIds.Clear();

            for (int i = 0; i < PlayerHeroSetSnapshots.Count; i++)
            {
                HeroSetSnapshot snapshot = PlayerHeroSetSnapshots[i];
                if (snapshot == null || snapshot.BattleEquipment == null)
                    continue;
                PlayerHeroByBattleEquipment[snapshot.BattleEquipment] =
                    snapshot.Hero;
                IndexEquippedSetItems(snapshot);
            }
        }

        private static void IndexEquippedSetItems(HeroSetSnapshot snapshot)
        {
            if (snapshot == null || snapshot.BattleEquipment == null ||
                snapshot.StateByCareer == null ||
                snapshot.StateByCareer.Count == 0)
                return;

            foreach (object element in EnumerateEquipmentElements(
                snapshot.BattleEquipment))
            {
                object item = GetProperty(element, "Item");
                string itemId = Convert.ToString(GetProperty(item, "StringId"));
                if (item == null || String.IsNullOrEmpty(itemId))
                    continue;

                IList traits = GetItemTraits(itemId);
                if (HasHeroSignature(traits))
                    continue;
                PieceSignature signature = FindPieceSignature(traits);
                if (signature == null)
                {
                    SetItemInstance known;
                    if (KnownSetItemsById.TryGetValue(itemId, out known) &&
                        known != null)
                        signature = known.Signature;
                }
                if (signature == null)
                    signature = FindPieceSignatureByName(item);
                if (signature == null ||
                    !snapshot.StateByCareer.ContainsKey(
                        signature.Definition.CareerId))
                    continue;

                HeroSetSnapshot existing;
                if (AmbiguousEquippedSetItemIds.Contains(itemId))
                {
                    RemoveItemObjectOwnersForId(itemId);
                    continue;
                }
                if (SetSnapshotByUniqueItemId.TryGetValue(itemId,
                    out existing) &&
                    !Object.ReferenceEquals(existing, snapshot))
                {
                    SetSnapshotByUniqueItemId.Remove(itemId);
                    AmbiguousEquippedSetItemIds.Add(itemId);
                    RemoveItemObjectOwnersForId(itemId);
                }
                else
                {
                    SetSnapshotByItemObject[item] = snapshot;
                    SetSnapshotByUniqueItemId[itemId] = snapshot;
                }
            }
        }

        private static void RemoveItemObjectOwnersForId(string itemId)
        {
            List<object> remove = new List<object>();
            foreach (object item in SetSnapshotByItemObject.Keys)
                if (String.Equals(GetItemId(item), itemId,
                    StringComparison.Ordinal))
                    remove.Add(item);
            for (int i = 0; i < remove.Count; i++)
                SetSnapshotByItemObject.Remove(remove[i]);
        }

        private static string GetHeroSnapshotKey(object hero)
        {
            string id = Convert.ToString(GetProperty(hero, "StringId"));
            if (!String.IsNullOrWhiteSpace(id))
                return id;
            string name = Convert.ToString(GetProperty(hero, "Name"));
            return String.IsNullOrWhiteSpace(name) ? "unknown hero" : name;
        }

        private static List<HeroDesiredTraits> BuildPlayerHeroDesiredTraits()
        {
            List<HeroDesiredTraits> result =
                new List<HeroDesiredTraits>();
            for (int i = 0; i < PlayerHeroSetSnapshots.Count; i++)
            {
                HeroSetSnapshot snapshot = PlayerHeroSetSnapshots[i];
                HeroDesiredTraits plan = new HeroDesiredTraits
                {
                    Snapshot = snapshot
                };
                foreach (EquippedSetState state in
                    snapshot.StateByCareer.Values)
                    AddDesiredTraitsForHeroState(snapshot, state,
                        plan.Desired, plan.TargetItems);
                result.Add(plan);
            }
            return result;
        }

        private static void AddDesiredTraitsForHeroState(
            HeroSetSnapshot snapshot, EquippedSetState state,
            Dictionary<string, List<string>> desired,
            Dictionary<string, object> targetItems)
        {
            int count = state.PieceIndices.Count;

            foreach (int pieceIndex in state.PieceIndices)
            {
                if (pieceIndex <= 0 ||
                    pieceIndex > state.Definition.Pieces.Length)
                    continue;
                SetPieceDefinition equippedPiece =
                    state.Definition.Pieces[pieceIndex - 1];
                for (int e = 0; e < equippedPiece.Effects.Length; e++)
                {
                    TraitDefinition effect = equippedPiece.Effects[e];
                    BonusTargetKind targetKind = GetBonusTargetKind(effect);
                    if (targetKind == BonusTargetKind.Armor)
                        continue;
                    EquippedItemRef target = SelectBonusTarget(state,
                        targetKind);
                    if (target == null)
                    {
                        LogOnce("companion-piece-bonus-target:" +
                            snapshot.HeroKey + ":" +
                            state.Definition.CareerId + ":" + effect.Id,
                            "Equipped set-piece effect '" + effect.Name +
                            "' for " + state.Definition.CareerId + " on " +
                            snapshot.HeroKey + " has no equipped " +
                            DescribeBonusTarget(targetKind) + " target.");
                        continue;
                    }
                    AddDesiredTrait(desired, targetItems, target,
                        GetRoutedPieceTraitId(effect));
                }
            }

            if (count < 2)
                return;

            for (int t = 0; t < state.Definition.Tiers.Length; t++)
            {
                SetTierDefinition tier = state.Definition.Tiers[t];
                if (count < tier.RequiredPieces)
                    continue;
                for (int e = 0; e < tier.Effects.Length; e++)
                {
                    TraitDefinition effect = tier.Effects[e];
                    BonusTargetKind targetKind =
                        GetBonusTargetKind(effect);
                    EquippedItemRef target = SelectBonusTarget(state,
                        targetKind);
                    if (target == null)
                    {
                        LogOnce("companion-bonus-target:" +
                            snapshot.HeroKey + ":" +
                            state.Definition.CareerId + ":" + effect.Id +
                            ":" + count, "Active set bonus '" +
                            effect.Name + "' for " +
                            state.Definition.CareerId + " on " +
                            snapshot.HeroKey + " has no equipped " +
                            DescribeBonusTarget(targetKind) +
                            " target. The trait was not attached.");
                        continue;
                    }
                    AddDesiredTrait(desired, targetItems, target,
                        effect.Id);
                }
            }
        }

        private static bool HasSharedDesiredTargets(
            List<HeroDesiredTraits> plans,
            Dictionary<string, List<HeroDesiredTraits>> users)
        {
            for (int p = 0; p < plans.Count; p++)
            {
                foreach (string itemId in plans[p].Desired.Keys)
                {
                    List<HeroDesiredTraits> equippedUsers;
                    if (users.TryGetValue(itemId, out equippedUsers) &&
                        equippedUsers.Count > 1)
                        return true;
                }
            }
            return false;
        }

        private static bool IsolateSharedDesiredTargets(
            List<HeroDesiredTraits> plans,
            Dictionary<string, List<HeroDesiredTraits>> users)
        {
            bool changed = false;

            for (int p = 0; p < plans.Count; p++)
            {
                HeroDesiredTraits plan = plans[p];
                string[] ids = new string[plan.Desired.Count];
                plan.Desired.Keys.CopyTo(ids, 0);
                for (int i = 0; i < ids.Length; i++)
                {
                    List<HeroDesiredTraits> equippedUsers;
                    if (!users.TryGetValue(ids[i], out equippedUsers) ||
                        equippedUsers.Count <= 1)
                        continue;

                    object target;
                    if (!plan.TargetItems.TryGetValue(ids[i], out target) ||
                        target == null)
                        throw new InvalidOperationException(
                            "Shared companion set target " + ids[i] +
                            " has no item object.");

                    CloneSharedCarrierForHero(plan.Snapshot, target,
                        ids[i]);
                    changed = true;
                }
            }
            return changed;
        }

        private static Dictionary<string, List<HeroDesiredTraits>>
            BuildEquippedItemUsersById()
        {
            Dictionary<string, List<HeroDesiredTraits>> result =
                new Dictionary<string, List<HeroDesiredTraits>>(
                    StringComparer.Ordinal);
            Dictionary<HeroSetSnapshot, HeroDesiredTraits> planBySnapshot =
                new Dictionary<HeroSetSnapshot, HeroDesiredTraits>();
            for (int i = 0; i < PlayerHeroSetSnapshots.Count; i++)
                planBySnapshot[PlayerHeroSetSnapshots[i]] =
                    new HeroDesiredTraits
                    {
                        Snapshot = PlayerHeroSetSnapshots[i]
                    };

            foreach (KeyValuePair<HeroSetSnapshot, HeroDesiredTraits> pair
                in planBySnapshot)
            {
                HashSet<string> seen = new HashSet<string>(
                    StringComparer.Ordinal);
                foreach (object element in EnumerateEquipmentElements(
                    pair.Key.BattleEquipment))
                {
                    object item = GetProperty(element, "Item");
                    string itemId = GetItemId(item);
                    if (item == null || String.IsNullOrEmpty(itemId) ||
                        !seen.Add(itemId))
                        continue;

                    List<HeroDesiredTraits> users;
                    if (!result.TryGetValue(itemId, out users))
                    {
                        users = new List<HeroDesiredTraits>();
                        result.Add(itemId, users);
                    }
                    users.Add(pair.Value);
                }
            }
            return result;
        }

        private static void CloneSharedCarrierForHero(
            HeroSetSnapshot snapshot, object target, string itemId)
        {
            if (snapshot == null || snapshot.BattleEquipment == null)
                throw new InvalidOperationException(
                    "Shared companion carrier has no owning equipment.");

            string slot = FindEquipmentSlotForItem(
                snapshot.BattleEquipment, target, itemId);
            if (String.IsNullOrEmpty(slot))
                throw new InvalidOperationException(
                    "Shared companion carrier " + itemId +
                    " is not present in " + snapshot.HeroKey +
                    "'s battle equipment.");

            List<string> baseTraits = GetNonConditionalTraits(itemId);
            string name = Convert.ToString(GetProperty(target, "Name"));
            object clone = CreateRecordedHeroItem(target,
                String.IsNullOrEmpty(name) ? "Companion wargear" : name,
                baseTraits, null);

            _internalCompanionCarrierMutation = true;
            try
            {
                SetEquipmentItemPreservingModifier(
                    snapshot.BattleEquipment, slot, clone);
            }
            finally
            {
                _internalCompanionCarrierMutation = false;
            }

            DirtyPlayerBattleEquipment.Add(snapshot.BattleEquipment);
            _companionSetSnapshotDirty = true;
            ModLog.Info("Isolated shared set-bonus carrier " + itemId +
                " for " + snapshot.HeroKey + " as " + GetItemId(clone) +
                ".");
        }

        private static string FindEquipmentSlotForItem(object equipment,
            object target, string itemId)
        {
            string[] slots = { "Weapon0", "Weapon1", "Weapon2", "Weapon3",
                "Weapon4", "Head", "Body", "Leg", "Gloves", "Cape",
                "Horse", "HorseHarness" };
            for (int i = 0; i < slots.Length; i++)
            {
                object item = GetEquipmentItem(equipment, slots[i]);
                if (Object.ReferenceEquals(item, target))
                    return slots[i];
            }
            for (int i = 0; i < slots.Length; i++)
            {
                object item = GetEquipmentItem(equipment, slots[i]);
                if (String.Equals(GetItemId(item), itemId,
                    StringComparison.Ordinal))
                    return slots[i];
            }
            return null;
        }

        private static List<string> GetNonConditionalTraits(string itemId)
        {
            List<string> result = new List<string>();
            IList traits = GetItemTraits(itemId);
            if (traits == null)
                return result;
            for (int i = 0; i < traits.Count; i++)
            {
                string id = Convert.ToString(traits[i]);
                if (!String.IsNullOrEmpty(id) &&
                    !IsConditionalRuntimeTrait(id) &&
                    !result.Contains(id))
                    result.Add(id);
            }
            return result;
        }

        private static void SetEquipmentItemPreservingModifier(
            object equipment, string slotName, object item)
        {
            Type indexType = TypeByName("TaleWorlds.Core.EquipmentIndex");
            Type itemType = TypeByName("TaleWorlds.Core.ItemObject");
            Type elementType = TypeByName("TaleWorlds.Core.EquipmentElement");
            if (equipment == null || indexType == null ||
                itemType == null || elementType == null)
                throw new InvalidOperationException(
                    "Core equipment reflection types are unavailable.");

            object index = Enum.Parse(indexType, slotName, true);
            MethodInfo getter = equipment.GetType().GetMethod("get_Item",
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance, null, new[] { indexType }, null);
            object oldElement = getter == null ? null :
                getter.Invoke(equipment, new[] { index });
            object modifier = GetProperty(oldElement, "ItemModifier");

            object replacement = null;
            ConstructorInfo[] constructors = elementType.GetConstructors(
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance);
            for (int i = 0; i < constructors.Length; i++)
            {
                ParameterInfo[] parameters =
                    constructors[i].GetParameters();
                if (parameters.Length == 4 &&
                    parameters[0].ParameterType == itemType)
                {
                    replacement = constructors[i].Invoke(
                        new[] { item, modifier, null, (object)false });
                    break;
                }
                if (replacement == null && parameters.Length == 1 &&
                    parameters[0].ParameterType == itemType)
                    replacement = constructors[i].Invoke(
                        new[] { item });
            }
            if (replacement == null)
                throw new MissingMethodException(elementType.FullName,
                    ".ctor(ItemObject, ItemModifier, ItemObject, bool)");

            MethodInfo add = equipment.GetType().GetMethod(
                "AddEquipmentToSlotWithoutAgent",
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance, null,
                new[] { indexType, elementType }, null);
            if (add == null)
                throw new MissingMethodException(
                    equipment.GetType().FullName,
                    "AddEquipmentToSlotWithoutAgent");
            add.Invoke(equipment, new[] { index, replacement });
        }

        private static void ThrowIfDesiredTargetsRemainShared(
            List<HeroDesiredTraits> plans)
        {
            Dictionary<string, HeroSetSnapshot> owner =
                new Dictionary<string, HeroSetSnapshot>(
                    StringComparer.Ordinal);
            for (int p = 0; p < plans.Count; p++)
            {
                foreach (string itemId in plans[p].Desired.Keys)
                {
                    HeroSetSnapshot existing;
                    if (owner.TryGetValue(itemId, out existing) &&
                        !Object.ReferenceEquals(existing,
                            plans[p].Snapshot))
                        throw new InvalidOperationException(
                            "Set-bonus carrier " + itemId +
                            " remains shared between " +
                            existing.HeroKey + " and " +
                            plans[p].Snapshot.HeroKey + ".");
                    owner[itemId] = plans[p].Snapshot;
                }
            }
        }

        private static void ApplyPlayerHeroBonusesTransactionally(
            List<HeroDesiredTraits> plans)
        {
            Dictionary<string, List<string>> desired =
                new Dictionary<string, List<string>>(
                    StringComparer.Ordinal);
            Dictionary<string, object> targets =
                new Dictionary<string, object>(
                    StringComparer.Ordinal);

            for (int p = 0; p < plans.Count; p++)
            {
                foreach (KeyValuePair<string, List<string>> pair in
                    plans[p].Desired)
                {
                    if (desired.ContainsKey(pair.Key))
                        throw new InvalidOperationException(
                            "Set-bonus item id " + pair.Key +
                            " is still owned by more than one hero.");
                    desired.Add(pair.Key,
                        new List<string>(pair.Value));
                    object target;
                    if (!plans[p].TargetItems.TryGetValue(
                        pair.Key, out target) || target == null)
                        throw new InvalidOperationException(
                            "Set-bonus target " + pair.Key +
                            " disappeared before application.");
                    targets.Add(pair.Key, target);
                }
            }

            HashSet<string> allItemIds = new HashSet<string>(
                AppliedBonusKeyByItemId.Keys, StringComparer.Ordinal);
            foreach (string itemId in desired.Keys)
                allItemIds.Add(itemId);

            List<TraitRollback> rollback = new List<TraitRollback>();
            foreach (string itemId in allItemIds)
            {
                string previous;
                bool hadKey = AppliedBonusKeyByItemId.TryGetValue(
                    itemId, out previous);
                rollback.Add(new TraitRollback
                {
                    ItemId = itemId,
                    ConditionalTraits =
                        GetConditionalRuntimeTraits(itemId),
                    HadAppliedKey = hadKey,
                    AppliedKey = previous
                });
            }

            try
            {
                for (int i = 0; i < rollback.Count; i++)
                {
                    string itemId = rollback[i].ItemId;
                    List<string> bonusIds;
                    if (!desired.TryGetValue(itemId, out bonusIds))
                        bonusIds = new List<string>();
                    else
                        bonusIds.Sort(StringComparer.Ordinal);

                    ApplyRuntimeBonusTraits(itemId, bonusIds);
                    if (bonusIds.Count == 0)
                    {
                        AppliedBonusKeyByItemId.Remove(itemId);
                        continue;
                    }

                    object target;
                    if (!targets.TryGetValue(itemId, out target) ||
                        target == null)
                        throw new InvalidOperationException(
                            "Set-bonus target item disappeared before " +
                            "verification: " + itemId + ".");
                    VerifyResolvedBonusTraits(target, bonusIds);
                    AppliedBonusKeyByItemId[itemId] =
                        String.Join("|", bonusIds.ToArray());
                }
            }
            catch
            {
                RollBackPlayerHeroBonusTransaction(rollback);
                throw;
            }
        }

        private static List<string> GetConditionalRuntimeTraits(
            string itemId)
        {
            List<string> result = new List<string>();
            IList traits = GetItemTraits(itemId);
            if (traits == null)
                return result;
            for (int i = 0; i < traits.Count; i++)
            {
                string id = Convert.ToString(traits[i]);
                if (IsConditionalRuntimeTrait(id) &&
                    !result.Contains(id))
                    result.Add(id);
            }
            return result;
        }

        private static void RollBackPlayerHeroBonusTransaction(
            List<TraitRollback> rollback)
        {
            List<string> failures = new List<string>();
            for (int i = rollback.Count - 1; i >= 0; i--)
            {
                TraitRollback entry = rollback[i];
                try
                {
                    ApplyRuntimeBonusTraits(entry.ItemId,
                        entry.ConditionalTraits);
                    if (entry.HadAppliedKey)
                        AppliedBonusKeyByItemId[entry.ItemId] =
                            entry.AppliedKey;
                    else
                        AppliedBonusKeyByItemId.Remove(entry.ItemId);
                }
                catch (Exception ex)
                {
                    failures.Add(entry.ItemId + ": " +
                        ex.GetType().Name + ": " + ex.Message);
                }
            }
            if (failures.Count > 0)
                ModLog.Error("Companion set rollback had " +
                    failures.Count + " failure(s): " +
                    String.Join(" | ", failures.ToArray()));
        }

        private static bool BuildCompanionAwareTooltip(object __0,
            ref string __1, ref string __2,
            ref List<SetTooltipRow> __3, ref bool __result)
        {
            try
            {
                string itemId = GetItemIdFromViewModel(__0);
                if (String.IsNullOrWhiteSpace(itemId))
                    return true;

                object item = GetItemFromViewModel(__0);
                PieceSignature signature =
                    FindPieceSignatureForTooltip(item, itemId);
                if (signature == null || !EnsureTraitsInjected())
                    return true;

                EnsureCompanionSetSession();
                EnsureCurrentPlayerHeroSnapshots(
                    GetMainHeroIfReady(), null);

                HeroSetSnapshot owner =
                    FindSetItemOwnerSnapshot(item, itemId);
                if (owner == null)
                    owner = _mainHeroSetSnapshot;

                EquippedSetState equipped = null;
                if (owner != null && owner.StateByCareer != null)
                    owner.StateByCareer.TryGetValue(
                        signature.Definition.CareerId, out equipped);

                IList traits = GetItemTraits(itemId);
                SetItemInstance instance = new SetItemInstance
                {
                    Item = item,
                    SaveData = null,
                    Signature = signature,
                    IsAdmin = HasAdminSignature(traits) ||
                        (Convert.ToString(GetProperty(item, "Name")) ??
                            String.Empty).StartsWith("[ADMIN COPY]",
                                StringComparison.OrdinalIgnoreCase)
                };

                __1 = itemId;
                __2 = BuildSetDescription(instance, equipped);
                __3 = BuildSetTooltipRows(instance, equipped);
                __result = !String.IsNullOrWhiteSpace(__2);
                return false;
            }
            catch (Exception ex)
            {
                LogOnce("companion-set-tooltip:" +
                    ex.GetType().FullName + ":" + ex.Message,
                    "Companion-aware set tooltip failed; using the " +
                    "controlled-hero tooltip state. " +
                    FormatException(ex));
                return true;
            }
        }

        private static HeroSetSnapshot FindSetItemOwnerSnapshot(
            object item, string itemId)
        {
            HeroSetSnapshot snapshot;
            if (item != null &&
                SetSnapshotByItemObject.TryGetValue(item, out snapshot))
                return snapshot;
            if (!String.IsNullOrEmpty(itemId) &&
                !AmbiguousEquippedSetItemIds.Contains(itemId) &&
                SetSnapshotByUniqueItemId.TryGetValue(
                    itemId, out snapshot))
                return snapshot;
            return null;
        }

        private static void BeforeRuntimeEquipmentMutation(object __0)
        {
            // Equipment mutations occur throughout Bannerlord while parties, troops,
            // encounters and mission agents are initialized. Only a living player-clan
            // hero edited from the inventory screen can invalidate this cache.
            if (_internalCompanionCarrierMutation || __0 == null ||
                !IsCompanionInventoryStateActive())
                return;

            EnsureCompanionSetSession();
            if (FindPlayerHeroByBattleEquipment(__0) == null)
                return;

            // Preserve every unaffected hero snapshot. EnsureCurrentPlayerHeroSnapshots
            // replaces only the snapshot whose exact BattleEquipment object is dirty.
            DirtyPlayerBattleEquipment.Add(__0);
            _companionSetSnapshotDirty = true;
        }

        private static void AfterRuntimeEquipmentMutation(object __0)
        {
            try
            {
                if (_internalCompanionCarrierMutation ||
                    __0 == null || _companionSetRefreshInProgress ||
                    !IsCompanionInventoryStateActive())
                    return;

                object owner = FindPlayerHeroByBattleEquipment(__0);
                if (owner == null)
                    return;
                if (Object.ReferenceEquals(owner,
                    GetMainHeroIfReady()))
                    return;

                Tick();
            }
            catch (Exception ex)
            {
                LogOnce("companion-equipment-event:" +
                    ex.GetType().FullName + ":" + ex.Message,
                    "Companion equipment event refresh failed: " +
                    FormatException(ex));
            }
        }

        private static object FindPlayerHeroByBattleEquipment(
            object equipment)
        {
            object hero;
            if (PlayerHeroByBattleEquipment.TryGetValue(
                equipment, out hero))
                return hero;

            object mainHero = GetMainHeroIfReady();
            object mainEquipment = GetProperty(mainHero,
                "BattleEquipment");
            if (mainHero != null &&
                Object.ReferenceEquals(mainEquipment, equipment))
                return mainHero;

            object playerClan = GetStaticProperty(
                TypeByName("TaleWorlds.CampaignSystem.Clan"),
                "PlayerClan");
            IEnumerable heroes = playerClan == null ? null :
                GetProperty(playerClan, "Heroes") as IEnumerable;
            if (heroes == null)
                return null;

            foreach (object candidate in heroes)
            {
                if (candidate == null ||
                    ToBoolean(GetProperty(candidate, "IsDead")))
                    continue;
                object candidateEquipment = GetProperty(candidate,
                    "BattleEquipment");
                if (candidateEquipment != null)
                    PlayerHeroByBattleEquipment[candidateEquipment] =
                        candidate;
                if (Object.ReferenceEquals(candidateEquipment,
                    equipment))
                    return candidate;
            }
            return null;
        }

        private static void BeforeInventoryStateActivated()
        {
            EnsureCompanionSetSession();
            _forceFullCompanionSetSnapshot = true;
            _companionSetSnapshotDirty = true;
        }

        private static void AfterCampaignSessionLaunched()
        {
            ResetCompanionSetSnapshot();
            try
            {
                Tick();
            }
            catch
            {
                // Inventory entry remains the bounded retry point.
            }
        }

        private static void AfterRuntimeSessionReset()
        {
            ResetCompanionSetSnapshot();
        }

        private static void AfterPlayerClanRosterChanged()
        {
            try
            {
                EnsureCompanionSetSession();
                _forceFullCompanionSetSnapshot = true;
                _companionSetSnapshotDirty = true;
                if (IsCompanionRuntimeStateActive())
                    Tick();
            }
            catch (Exception ex)
            {
                LogOnce("companion-roster-event:" +
                    ex.GetType().FullName + ":" + ex.Message,
                    "Player-clan hero roster refresh failed: " +
                    FormatException(ex));
            }
        }

        private static void EnsureCompanionSetSession()
        {
            object session = GetStaticProperty(
                TypeByName("TaleWorlds.CampaignSystem.Campaign"),
                "Current");
            if (Object.ReferenceEquals(session,
                _companionSetCampaignSession))
                return;
            ResetCompanionSetSnapshot();
            _companionSetCampaignSession = session;
        }

        private static void ResetCompanionSetSnapshot()
        {
            PlayerHeroSetSnapshots.Clear();
            PlayerHeroByBattleEquipment.Clear();
            SetSnapshotByItemObject.Clear();
            SetSnapshotByUniqueItemId.Clear();
            AmbiguousEquippedSetItemIds.Clear();
            DirtyPlayerBattleEquipment.Clear();
            _companionSetCampaignSession = null;
            _companionSetSnapshotMainHero = null;
            _mainHeroSetSnapshot = null;
            _companionSetSnapshotDirty = true;
            _companionSetSnapshotAvailable = false;
            _forceFullCompanionSetSnapshot = true;
            _companionSetRefreshInProgress = false;
            _internalCompanionCarrierMutation = false;
            _companionCarrierIsolationPending = false;
        }

        internal static void TickPendingCompanionSetWork()
        {
            if (!_companionCarrierIsolationPending ||
                _companionSetRefreshInProgress ||
                IsCompanionInventoryStateActive() ||
                !IsCompanionRuntimeStateActive())
                return;

            _companionCarrierIsolationPending = false;
            Tick();
            if (_companionSetSnapshotDirty)
                _companionCarrierIsolationPending = true;
        }

        private static bool IsCompanionInventoryStateActive()
        {
            object state = GetActiveGameState();
            return state != null &&
                String.Equals(state.GetType().FullName,
                    InventoryStateTypeName,
                    StringComparison.Ordinal);
        }

        private static bool IsCompanionRuntimeStateActive()
        {
            object state = GetActiveGameState();
            if (state == null)
                return false;
            string name = state.GetType().FullName ?? String.Empty;
            return name.IndexOf("Loading",
                StringComparison.OrdinalIgnoreCase) < 0;
        }

        private static object GetActiveGameState()
        {
            Type gameType = TypeByName("TaleWorlds.Core.Game");
            object game = GetStaticProperty(gameType, "Current");
            object manager = GetProperty(game, "GameStateManager");
            return GetProperty(manager, "ActiveState");
        }
    }
}
