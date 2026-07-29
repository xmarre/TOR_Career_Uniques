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
        private static readonly bool CompanionSetSupportInstalled =
            TryInstallCompanionSetSupport();

        private static object _companionSetCampaignSession;
        private static object _companionSetSnapshotMainHero;
        private static HeroSetSnapshot _mainHeroSetSnapshot;
        private static bool _companionSetSnapshotDirty = true;
        private static bool _companionSetSnapshotAvailable;
        private static bool _companionSetRefreshInProgress;

        private sealed class HeroSetSnapshot
        {
            internal object Hero;
            internal object BattleEquipment;
            internal string HeroKey;
            internal Dictionary<string, EquippedSetState> StateByCareer;
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
                _companionSetSnapshotDirty = true;

            if (!_companionSetSnapshotDirty)
                return;
            if (_companionSetRefreshInProgress)
                return;

            _companionSetRefreshInProgress = true;
            try
            {
                if (!_companionSetSnapshotAvailable ||
                    !Object.ReferenceEquals(mainHero,
                        _companionSetSnapshotMainHero))
                    RebuildPlayerHeroSetSnapshots(mainHero, mainHeroState);

                Dictionary<string, List<string>> desired =
                    new Dictionary<string, List<string>>(StringComparer.Ordinal);
                Dictionary<string, object> targetItems =
                    new Dictionary<string, object>(StringComparer.Ordinal);

                for (int i = 0; i < PlayerHeroSetSnapshots.Count; i++)
                {
                    HeroSetSnapshot snapshot = PlayerHeroSetSnapshots[i];
                    foreach (EquippedSetState state in snapshot.StateByCareer.Values)
                        AddDesiredTraitsForHeroState(snapshot, state,
                            desired, targetItems);
                }

                ApplyAggregatedPlayerHeroBonuses(desired, targetItems);
                _companionSetSnapshotDirty = false;
            }
            finally
            {
                _companionSetRefreshInProgress = false;
            }
        }

        private static void RebuildPlayerHeroSetSnapshots(object mainHero,
            Dictionary<string, EquippedSetState> mainHeroState)
        {
            PlayerHeroSetSnapshots.Clear();
            PlayerHeroByBattleEquipment.Clear();
            SetSnapshotByItemObject.Clear();
            SetSnapshotByUniqueItemId.Clear();
            AmbiguousEquippedSetItemIds.Clear();
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

            Dictionary<string, EquippedSetState> state = suppliedState ??
                ScanHeroSetState(hero, isMainHero);
            HeroSetSnapshot snapshot = new HeroSetSnapshot
            {
                Hero = hero,
                BattleEquipment = equipment,
                HeroKey = GetHeroSnapshotKey(hero),
                StateByCareer = state
            };

            PlayerHeroByBattleEquipment[equipment] = hero;
            PlayerHeroSetSnapshots.Add(snapshot);
            if (isMainHero)
                _mainHeroSetSnapshot = snapshot;
            IndexEquippedSetItems(snapshot);
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

                equippedItems.Add(new EquippedItemRef
                {
                    ItemId = itemId,
                    Item = item,
                    ItemTypeName = GetItemTypeName(item)
                });

                IList traits = GetItemTraits(itemId);
                if (!includePersistentEncounterHeroCopies &&
                    HasHeroSignature(traits))
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

        private static void IndexEquippedSetItems(HeroSetSnapshot snapshot)
        {
            if (snapshot == null || snapshot.BattleEquipment == null ||
                snapshot.StateByCareer == null || snapshot.StateByCareer.Count == 0)
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

                SetSnapshotByItemObject[item] = snapshot;
                HeroSetSnapshot existing;
                if (AmbiguousEquippedSetItemIds.Contains(itemId))
                    continue;
                if (SetSnapshotByUniqueItemId.TryGetValue(itemId, out existing) &&
                    !Object.ReferenceEquals(existing, snapshot))
                {
                    SetSnapshotByUniqueItemId.Remove(itemId);
                    AmbiguousEquippedSetItemIds.Add(itemId);
                }
                else
                {
                    SetSnapshotByUniqueItemId[itemId] = snapshot;
                }
            }
        }

        private static string GetHeroSnapshotKey(object hero)
        {
            string id = Convert.ToString(GetProperty(hero, "StringId"));
            if (!String.IsNullOrWhiteSpace(id))
                return id;
            string name = Convert.ToString(GetProperty(hero, "Name"));
            return String.IsNullOrWhiteSpace(name) ? "unknown hero" : name;
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
                    EquippedItemRef target = SelectBonusTarget(state, targetKind);
                    if (target == null)
                    {
                        LogOnce("companion-piece-bonus-target:" + snapshot.HeroKey +
                            ":" + state.Definition.CareerId + ":" + effect.Id,
                            "Equipped set-piece effect '" + effect.Name + "' for " +
                            state.Definition.CareerId + " on " + snapshot.HeroKey +
                            " has no equipped " + DescribeBonusTarget(targetKind) +
                            " target.");
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
                    BonusTargetKind targetKind = GetBonusTargetKind(effect);
                    EquippedItemRef target = SelectBonusTarget(state, targetKind);
                    if (target == null)
                    {
                        LogOnce("companion-bonus-target:" + snapshot.HeroKey + ":" +
                            state.Definition.CareerId + ":" + effect.Id + ":" + count,
                            "Active set bonus '" + effect.Name + "' for " +
                            state.Definition.CareerId + " on " + snapshot.HeroKey +
                            " has no equipped " + DescribeBonusTarget(targetKind) +
                            " target. The trait was not attached.");
                        continue;
                    }
                    AddDesiredTrait(desired, targetItems, target, effect.Id);
                }
            }
        }

        private static void ApplyAggregatedPlayerHeroBonuses(
            Dictionary<string, List<string>> desired,
            Dictionary<string, object> targetItems)
        {
            HashSet<string> allItemIds = new HashSet<string>(
                AppliedBonusKeyByItemId.Keys, StringComparer.Ordinal);
            foreach (string itemId in desired.Keys)
                allItemIds.Add(itemId);

            foreach (string itemId in allItemIds)
            {
                List<string> bonusIds;
                if (!desired.TryGetValue(itemId, out bonusIds))
                    bonusIds = new List<string>();
                else
                    bonusIds.Sort(StringComparer.Ordinal);

                string key = String.Join("|", bonusIds.ToArray());
                string previous;
                if (AppliedBonusKeyByItemId.TryGetValue(itemId, out previous) &&
                    String.Equals(previous, key, StringComparison.Ordinal))
                    continue;

                ApplyRuntimeBonusTraits(itemId, bonusIds);
                if (bonusIds.Count == 0)
                {
                    AppliedBonusKeyByItemId.Remove(itemId);
                    ModLog.Info("Removed conditional set-bonus traits from " +
                        itemId + ".");
                    continue;
                }

                object target;
                if (!targetItems.TryGetValue(itemId, out target) || target == null)
                    throw new InvalidOperationException(
                        "Player-clan set-bonus target item disappeared before " +
                        "verification: " + itemId + ".");
                VerifyResolvedBonusTraits(target, bonusIds);
                AppliedBonusKeyByItemId[itemId] = key;
                ModLog.Info("Activated " + bonusIds.Count +
                    " cumulative set-bonus traits on " + itemId +
                    " from independently evaluated player-clan hero equipment.");
            }
        }

        private static bool BuildCompanionAwareTooltip(object __0,
            ref string __1, ref string __2, ref List<SetTooltipRow> __3,
            ref bool __result)
        {
            try
            {
                string itemId = GetItemIdFromViewModel(__0);
                if (String.IsNullOrWhiteSpace(itemId))
                    return true;

                object item = GetItemFromViewModel(__0);
                PieceSignature signature = FindPieceSignatureForTooltip(item, itemId);
                if (signature == null)
                    return true;
                if (!EnsureTraitsInjected())
                    return true;

                EnsureCompanionSetSession();
                if (!_companionSetSnapshotAvailable)
                    RebuildPlayerHeroSetSnapshots(GetMainHeroIfReady(), null);

                HeroSetSnapshot owner = FindSetItemOwnerSnapshot(item, itemId);
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
                        (Convert.ToString(GetProperty(item, "Name")) ?? String.Empty)
                            .StartsWith("[ADMIN COPY]",
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
                LogOnce("companion-set-tooltip:" + ex.GetType().FullName + ":" +
                    ex.Message, "Companion-aware set tooltip failed; using the " +
                    "controlled-hero tooltip state. " + FormatException(ex));
                return true;
            }
        }

        private static HeroSetSnapshot FindSetItemOwnerSnapshot(object item,
            string itemId)
        {
            HeroSetSnapshot snapshot;
            if (item != null && SetSnapshotByItemObject.TryGetValue(item,
                out snapshot))
                return snapshot;
            if (!String.IsNullOrEmpty(itemId) &&
                !AmbiguousEquippedSetItemIds.Contains(itemId) &&
                SetSnapshotByUniqueItemId.TryGetValue(itemId, out snapshot))
                return snapshot;
            return null;
        }

        private static void BeforeRuntimeEquipmentMutation(object __0)
        {
            EnsureCompanionSetSession();
            _companionSetSnapshotDirty = true;
            _companionSetSnapshotAvailable = false;
        }

        private static void AfterRuntimeEquipmentMutation(object __0)
        {
            try
            {
                if (__0 == null || _companionSetRefreshInProgress ||
                    !IsCompanionInventoryStateActive())
                    return;

                object owner = FindPlayerHeroByBattleEquipment(__0);
                if (owner == null)
                    return;
                object mainHero = GetMainHeroIfReady();
                if (Object.ReferenceEquals(owner, mainHero))
                    return;

                Tick();
            }
            catch (Exception ex)
            {
                LogOnce("companion-equipment-event:" + ex.GetType().FullName +
                    ":" + ex.Message, "Companion equipment event refresh failed: " +
                    FormatException(ex));
            }
        }

        private static object FindPlayerHeroByBattleEquipment(object equipment)
        {
            object hero;
            if (PlayerHeroByBattleEquipment.TryGetValue(equipment, out hero))
                return hero;

            object mainHero = GetMainHeroIfReady();
            object mainEquipment = GetProperty(mainHero, "BattleEquipment");
            if (mainHero != null && Object.ReferenceEquals(mainEquipment, equipment))
                return mainHero;

            object playerClan = GetStaticProperty(
                TypeByName("TaleWorlds.CampaignSystem.Clan"), "PlayerClan");
            IEnumerable heroes = playerClan == null ? null :
                GetProperty(playerClan, "Heroes") as IEnumerable;
            if (heroes == null)
                return null;

            foreach (object candidate in heroes)
            {
                if (candidate == null || ToBoolean(GetProperty(candidate, "IsDead")))
                    continue;
                object candidateEquipment = GetProperty(candidate, "BattleEquipment");
                if (candidateEquipment != null)
                    PlayerHeroByBattleEquipment[candidateEquipment] = candidate;
                if (Object.ReferenceEquals(candidateEquipment, equipment))
                    return candidate;
            }
            return null;
        }

        private static void BeforeInventoryStateActivated()
        {
            EnsureCompanionSetSession();
            _companionSetSnapshotDirty = true;
            _companionSetSnapshotAvailable = false;
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
                // The normal inventory-entry event remains the bounded retry point.
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
                _companionSetSnapshotDirty = true;
                _companionSetSnapshotAvailable = false;
                if (IsCompanionRuntimeStateActive())
                    Tick();
            }
            catch (Exception ex)
            {
                LogOnce("companion-roster-event:" + ex.GetType().FullName + ":" +
                    ex.Message, "Player-clan hero roster refresh failed: " +
                    FormatException(ex));
            }
        }

        private static void EnsureCompanionSetSession()
        {
            object session = GetStaticProperty(
                TypeByName("TaleWorlds.CampaignSystem.Campaign"), "Current");
            if (Object.ReferenceEquals(session, _companionSetCampaignSession))
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
            _companionSetCampaignSession = null;
            _companionSetSnapshotMainHero = null;
            _mainHeroSetSnapshot = null;
            _companionSetSnapshotDirty = true;
            _companionSetSnapshotAvailable = false;
            _companionSetRefreshInProgress = false;
        }

        private static bool IsCompanionInventoryStateActive()
        {
            object state = GetActiveGameState();
            return state != null && String.Equals(state.GetType().FullName,
                InventoryStateTypeName, StringComparison.Ordinal);
        }

        private static bool IsCompanionRuntimeStateActive()
        {
            object state = GetActiveGameState();
            if (state == null)
                return false;
            string name = state.GetType().FullName ?? String.Empty;
            return name.IndexOf("Loading", StringComparison.OrdinalIgnoreCase) < 0;
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
