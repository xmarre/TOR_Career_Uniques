using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using TaleWorlds.CampaignSystem;

namespace TORCareerUniques
{
    // Event-driven inventory/set maintenance and set-item merchant protection.
    // No application tick or equipment fingerprint polling is used.
    public static class RuntimePerformanceGate
    {
        private const string InventoryStateName =
            "TaleWorlds.CampaignSystem.GameState.InventoryState";
        private const float TooltipInstallElapsed = 5.0f;
        private const string MerchantHarmonyId =
            "torcareeruniques.setitems.nonmerchandise";

        private static Assembly _mainAssembly;
        private static MethodInfo _setTick;
        private static MethodInfo _tooltipTick;
        private static bool _loggedFailure;
        private static bool _loadedProtectionApplied;
        private static bool _creationPatchAttempted;
        private static bool _creationPatchInstalled;
        private static object _merchantHarmony;
        private static object _inventoryHarmony;
        private static bool _inventoryActivationPatchInstalled;
        private static bool _equipmentMutationPatchInstalled;
        private static FieldInfo _knownSetItemsField;
        private static FieldInfo _lastCraftedItemCountField;
        private static readonly HashSet<string> ProtectedItemIds =
            new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> CleanedSettlementIds =
            new HashSet<string>(StringComparer.Ordinal);

        private static PropertyInfo _mainHeroProperty;
        private static PropertyInfo _battleEquipmentProperty;
        private static PropertyInfo _gameCurrent;
        private static PropertyInfo _gameStateManager;
        private static PropertyInfo _activeState;
        public static void Initialize()
        {
            try
            {
                EnsureCreationPatchInstalled();
                EnsureInventoryEventPatchesInstalled();
            }
            catch (Exception ex)
            {
                LogFailureOnce("Event-driven runtime initialization failed: " +
                    ex.GetType().Name + ": " + ex.Message);
            }
        }

        public static void OnCampaignSessionLaunched()
        {
            EnsureCreationPatchInstalled();
            EnsureInventoryEventPatchesInstalled();
            TryProtectLoadedSetItemsOnce();
        }

        // Harmony postfix for InventoryState.InventoryLogic assignment, which occurs
        // once while a concrete inventory/shop state is being opened.
        public static void AfterInventoryStateActivated()
        {
            try
            {
                if (Campaign.Current == null)
                    return;
                RefreshSetRuntime();
                CleanCurrentSettlementMarketOnce();
                InvokeTooltipInstaller();
            }
            catch (Exception ex)
            {
                LogFailureOnce("Inventory-entry runtime refresh failed: " +
                    ex.GetType().Name + ": " + ex.Message);
            }
        }

        // Harmony postfix for Equipment.AddEquipmentToSlotWithoutAgent(...).
        public static void AfterEquipmentMutation(object __instance)
        {
            try
            {
                if (Campaign.Current == null || __instance == null ||
                    !IsInventoryStateActive())
                    return;
                ResolveHeroAccessors();
                object hero = _mainHeroProperty == null ? null :
                    _mainHeroProperty.GetValue(null, null);
                object equipment = hero == null || _battleEquipmentProperty == null
                    ? null : _battleEquipmentProperty.GetValue(hero, null);
                if (Object.ReferenceEquals(__instance, equipment))
                    RefreshSetRuntime();
            }
            catch (Exception ex)
            {
                LogFailureOnce("Equipment-event runtime refresh failed: " +
                    ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void RefreshSetRuntime()
        {
            ResolveMainMethods();
            if (_setTick != null)
            {
                _setTick.Invoke(null, null);
                ProtectKnownSetItems();
            }
        }

        public static void ResetSession()
        {
            try
            {
                ResolveMainAssembly();
                if (_mainAssembly != null)
                {
                    Type admin = _mainAssembly.GetType(
                        "TORCareerUniques.AdminBridge", false);
                    SetStaticField(admin, "_behavior", null);
                    SetStaticField(admin, "_applicationTickRequested", false);

                    Type setRuntime = _mainAssembly.GetType(
                        "TORCareerUniques.SetItemRuntime", false);
                    ClearStaticCollection(setRuntime, "BaseTraitsByItemId");
                    ClearStaticCollection(setRuntime, "AppliedBonusKeyByItemId");
                    ClearStaticCollection(setRuntime, "VisualSourceByCareer");
                    ClearStaticCollection(setRuntime, "VisualItemByCareerSlot");
                    ClearStaticCollection(setRuntime, "VisualOutfitSignatureOwner");
                    ClearStaticCollection(setRuntime, "VisualArchetypeItemIdsByCareer");
                    ClearStaticCollection(setRuntime, "VisualCultureItemIdsByCareer");
                    ClearStaticCollection(setRuntime, "VisualEquipmentPairCountsByCareer");
                    ClearStaticCollection(setRuntime, "VisualOutfitResolutionAttempted");
                    ClearStaticCollection(setRuntime, "KnownSetItemsById");
                    ClearStaticCollection(setRuntime, "DescriptionKeyByItemId");
                    ClearStaticCollection(setRuntime, "DisplayStateKeyByCareer");
                    ClearStaticCollection(setRuntime, "MigratedVisualBaseByItemId");
                    ClearStaticCollection(setRuntime, "VisualMigrationAttemptedItemIds");
                    SetStaticField(setRuntime, "_lastMainHero", null);
                    SetStaticField(setRuntime, "_lastCraftedItemCount", -1);
                    SetStaticField(setRuntime, "_visualResolverSession", null);
                    SetStaticField(setRuntime, "_visualAuditAttempted", false);
                    SetStaticField(setRuntime, "_visualAuditRetryDelay", 0);
                    SetStaticField(setRuntime, "_lastVisualAuditFailureKey", null);
                    SetStaticField(setRuntime, "_visualMigrationPassCompleted", false);
                    SetStaticField(setRuntime, "_busy", false);

                    Type careerRuntime = _mainAssembly.GetType(
                        "TORCareerUniques.CareerUniqueRuntime", false);
                    ClearStaticCollection(careerRuntime, "ResolvedBaseItemByCareer");
                    SetStaticField(careerRuntime, "_baseItemCacheSession", null);
                }
            }
            catch
            {
                // Teardown must never be blocked by cleanup diagnostics.
            }
            finally
            {
                _loadedProtectionApplied = false;
                ProtectedItemIds.Clear();
                CleanedSettlementIds.Clear();
            }
        }

        private static void InvokeTooltipInstaller()
        {
            if (_tooltipTick != null)
                _tooltipTick.Invoke(null, new object[] { TooltipInstallElapsed });
        }

        private static bool IsInventoryStateActive()
        {
            if (_gameCurrent == null || _gameStateManager == null ||
                _activeState == null)
                ResolveGameStateAccessors();
            if (_gameCurrent == null || _gameStateManager == null ||
                _activeState == null)
                return false;

            object game = _gameCurrent.GetValue(null, null);
            object manager = game == null ? null :
                _gameStateManager.GetValue(game, null);
            object state = manager == null ? null :
                _activeState.GetValue(manager, null);
            return state != null && String.Equals(state.GetType().FullName,
                InventoryStateName, StringComparison.Ordinal);
        }


        private static void ResolveHeroAccessors()
        {
            if (_mainHeroProperty != null && _battleEquipmentProperty != null)
                return;

            Type heroType = FindLoadedType("TaleWorlds.CampaignSystem.Hero");
            if (heroType == null)
                return;

            _mainHeroProperty = heroType.GetProperty("MainHero",
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Static);
            _battleEquipmentProperty = heroType.GetProperty("BattleEquipment",
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance);
        }

        private static void ResolveGameStateAccessors()
        {
            Type gameType = FindLoadedType("TaleWorlds.Core.Game");
            if (gameType == null)
                return;
            _gameCurrent = gameType.GetProperty("Current",
                BindingFlags.Public | BindingFlags.Static);
            _gameStateManager = gameType.GetProperty("GameStateManager",
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance);
            Type managerType = _gameStateManager == null ? null :
                _gameStateManager.PropertyType;
            _activeState = managerType == null ? null : managerType.GetProperty(
                "ActiveState", BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance);
        }

        private static void ResolveMainMethods()
        {
            ResolveMainAssembly();
            if (_mainAssembly == null)
                return;
            if (_setTick == null)
            {
                Type type = _mainAssembly.GetType(
                    "TORCareerUniques.SetItemRuntime", false);
                _setTick = type == null ? null : type.GetMethod("Tick",
                    BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.NonPublic);
            }
            if (_tooltipTick == null)
            {
                Type type = _mainAssembly.GetType(
                    "TORCareerUniques.InventorySetTooltipBridge", false);
                _tooltipTick = type == null ? null : type.GetMethod("Tick",
                    BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.NonPublic);
            }
        }

        private static void ResolveMainAssembly()
        {
            if (_mainAssembly != null)
                return;
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                if (String.Equals(assemblies[i].GetName().Name,
                    "TORCareerUniques", StringComparison.Ordinal))
                {
                    _mainAssembly = assemblies[i];
                    return;
                }
            }
        }

        private static Type FindLoadedType(string fullName)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type type = assemblies[i].GetType(fullName, false);
                if (type != null)
                    return type;
            }
            return null;
        }

        private static void ClearStaticCollection(Type type, string fieldName)
        {
            if (type == null)
                return;
            FieldInfo field = type.GetField(fieldName,
                BindingFlags.Static | BindingFlags.Public |
                BindingFlags.NonPublic);
            object value = field == null ? null : field.GetValue(null);
            if (value == null)
                return;
            MethodInfo clear = value.GetType().GetMethod("Clear",
                BindingFlags.Instance | BindingFlags.Public,
                null, Type.EmptyTypes, null);
            if (clear != null)
                clear.Invoke(value, null);
        }

        private static void SetStaticField(Type type, string fieldName,
            object value)
        {
            if (type == null)
                return;
            FieldInfo field = type.GetField(fieldName,
                BindingFlags.Static | BindingFlags.Public |
                BindingFlags.NonPublic);
            if (field != null)
                field.SetValue(null, value);
        }

        private static void TryProtectLoadedSetItemsOnce()
        {
            if (_loadedProtectionApplied)
                return;

            ResolveSetRuntimeMembers();
            if (_lastCraftedItemCountField == null)
                return;

            object countValue = _lastCraftedItemCountField.GetValue(null);
            if (countValue == null || Convert.ToInt32(countValue) < 0)
                return;

            // Set the guard before doing any reflective work. A broken runtime shape
            // must not turn this into a recurring campaign-map retry loop.
            _loadedProtectionApplied = true;
            try
            {
                int protectedCount = ProtectKnownSetItems();
                if (protectedCount > 0)
                    LogInfo("Marked " + protectedCount +
                        " loaded career-set item definitions as non-merchandise.");
            }
            catch (Exception ex)
            {
                LogFailureOnce("Loaded set-item merchant protection failed: " +
                    ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static int ProtectKnownSetItems()
        {
            ResolveSetRuntimeMembers();
            object dictionary = _knownSetItemsField == null ? null :
                _knownSetItemsField.GetValue(null);
            IEnumerable entries = dictionary as IEnumerable;
            if (entries == null)
                return 0;

            int changed = 0;
            foreach (object entry in entries)
            {
                object key = GetMemberValueByName(entry, "Key");
                object value = GetMemberValueByName(entry, "Value");
                object item = GetMemberValueByName(value, "Item");
                string itemId = Convert.ToString(key);
                if (String.IsNullOrWhiteSpace(itemId) && item != null)
                    itemId = Convert.ToString(GetMemberValueByName(item,
                        "StringId"));
                if (String.IsNullOrWhiteSpace(itemId) || item == null)
                    continue;

                ProtectedItemIds.Add(itemId);
                if (ProtectItemFromMerchants(item))
                    changed++;
            }
            return changed;
        }

        private static void ResolveSetRuntimeMembers()
        {
            ResolveMainAssembly();
            if (_mainAssembly == null)
                return;
            Type setRuntime = _mainAssembly.GetType(
                "TORCareerUniques.SetItemRuntime", false);
            if (setRuntime == null)
                return;

            if (_knownSetItemsField == null)
                _knownSetItemsField = setRuntime.GetField("KnownSetItemsById",
                    BindingFlags.Static | BindingFlags.NonPublic |
                    BindingFlags.Public);
            if (_lastCraftedItemCountField == null)
                _lastCraftedItemCountField = setRuntime.GetField(
                    "_lastCraftedItemCount", BindingFlags.Static |
                    BindingFlags.NonPublic | BindingFlags.Public);
        }

        private static bool ProtectItemFromMerchants(object item)
        {
            if (item == null)
                return false;

            try
            {
                object currentFlags = GetMemberValueByName(item, "ItemFlags");
                if (currentFlags != null && currentFlags.GetType().IsEnum)
                {
                    Type flagsType = currentFlags.GetType();
                    string flagName = null;
                    string[] names = Enum.GetNames(flagsType);
                    for (int i = 0; i < names.Length; i++)
                    {
                        string candidate = names[i];
                        if (candidate.IndexOf("merch",
                            StringComparison.OrdinalIgnoreCase) >= 0 &&
                            candidate.IndexOf("not",
                            StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            flagName = candidate;
                            break;
                        }
                    }
                    if (!String.IsNullOrEmpty(flagName))
                    {
                        object nonMerchandise = Enum.Parse(flagsType,
                            flagName, true);
                        ulong combined = Convert.ToUInt64(currentFlags) |
                            Convert.ToUInt64(nonMerchandise);
                        object newFlags = Enum.ToObject(flagsType, combined);
                        TrySetMemberValue(item, "ItemFlags", newFlags);
                    }
                }

                object verified = GetMemberValueByName(item, "NotMerchandise");
                if (verified != null && Convert.ToBoolean(verified))
                    return true;

                // Compatibility fallback for builds exposing a writable/private
                // NotMerchandise property rather than an ItemFlags enum member.
                if (TrySetMemberValue(item, "NotMerchandise", true))
                {
                    verified = GetMemberValueByName(item, "NotMerchandise");
                    return verified != null && Convert.ToBoolean(verified);
                }
            }
            catch
            {
                return false;
            }
            return false;
        }

        private static void EnsureInventoryEventPatchesInstalled()
        {
            if (_inventoryActivationPatchInstalled &&
                _equipmentMutationPatchInstalled)
                return;

            Type harmonyType = FindLoadedType("HarmonyLib.Harmony");
            Type harmonyMethodType = FindLoadedType("HarmonyLib.HarmonyMethod");
            if (harmonyType == null || harmonyMethodType == null)
                return;

            if (_inventoryHarmony == null)
                _inventoryHarmony = Activator.CreateInstance(harmonyType,
                    new object[] { "torcareeruniques.inventory.events" });

            if (!_inventoryActivationPatchInstalled)
            {
                Type stateType = FindLoadedType(InventoryStateName);
                MethodInfo original = stateType == null ? null :
                    stateType.GetMethod("set_InventoryLogic", BindingFlags.Public |
                        BindingFlags.NonPublic | BindingFlags.Instance,
                        null, new[] { stateType.GetProperty("InventoryLogic",
                            BindingFlags.Public | BindingFlags.NonPublic |
                            BindingFlags.Instance).PropertyType }, null);
                MethodInfo postfix = typeof(RuntimePerformanceGate).GetMethod(
                    "AfterInventoryStateActivated", BindingFlags.Public |
                    BindingFlags.Static);
                if (original != null && postfix != null)
                {
                    ApplyHarmonyPatch(harmonyType, _inventoryHarmony, original,
                        CreateHarmonyMethod(harmonyMethodType, postfix));
                    _inventoryActivationPatchInstalled = true;
                }
            }

            if (!_equipmentMutationPatchInstalled)
            {
                Type equipmentType = FindLoadedType("TaleWorlds.Core.Equipment");
                MethodInfo postfix = typeof(RuntimePerformanceGate).GetMethod(
                    "AfterEquipmentMutation", BindingFlags.Public |
                    BindingFlags.Static);
                MethodInfo mutation = null;
                MethodInfo[] methods = equipmentType == null ?
                    new MethodInfo[0] : equipmentType.GetMethods(
                        BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.Instance);
                for (int i = 0; i < methods.Length; i++)
                {
                    if (methods[i].Name == "AddEquipmentToSlotWithoutAgent" &&
                        methods[i].GetParameters().Length == 2)
                    {
                        mutation = methods[i];
                        break;
                    }
                }
                if (mutation != null && postfix != null)
                {
                    ApplyHarmonyPatch(harmonyType, _inventoryHarmony, mutation,
                        CreateHarmonyMethod(harmonyMethodType, postfix));
                    _equipmentMutationPatchInstalled = true;
                }
            }

            if (_inventoryActivationPatchInstalled &&
                _equipmentMutationPatchInstalled)
                LogInfo("Installed event-driven inventory entry and equipment-change refresh hooks.");
        }

        private static void EnsureCreationPatchInstalled()
        {
            if (_creationPatchInstalled || _creationPatchAttempted)
                return;

            try
            {
                Type harmonyType = FindLoadedType("HarmonyLib.Harmony");
                Type harmonyMethodType = FindLoadedType("HarmonyLib.HarmonyMethod");
                Type helperType = FindLoadedType(
                    "TOR_Core.CampaignMechanics.Crafting.EnchantmentHelper");
                if (harmonyType == null || harmonyMethodType == null ||
                    helperType == null)
                {
                    LogFailureOnce("Set-item merchant protection patch could not " +
                        "resolve Harmony or ToR's enchantment helper.");
                    return;
                }

                _creationPatchAttempted = true;

                MethodInfo original = FindStaticMethodByCount(helperType,
                    "CreateEnchantedItem", 5);
                MethodInfo postfix = typeof(RuntimePerformanceGate).GetMethod(
                    "AfterCreateEnchantedItem",
                    BindingFlags.Public | BindingFlags.Static);
                if (original == null || postfix == null)
                    throw new MissingMethodException(helperType.FullName,
                        "CreateEnchantedItem(...)");

                _merchantHarmony = Activator.CreateInstance(harmonyType,
                    new object[] { MerchantHarmonyId });
                object harmonyPostfix = CreateHarmonyMethod(harmonyMethodType,
                    postfix);
                ApplyHarmonyPatch(harmonyType, _merchantHarmony, original,
                    harmonyPostfix);
                _creationPatchInstalled = true;
                LogInfo("Installed event-driven non-merchandise protection for " +
                    "new career-set items.");
            }
            catch (Exception ex)
            {
                LogFailureOnce("Set-item merchant protection patch failed: " +
                    ex.GetType().Name + ": " + ex.Message);
            }
        }

        // Harmony postfix. __1 is CreateEnchantedItem's trait-list argument.
        public static void AfterCreateEnchantedItem(object __result, object __1)
        {
            try
            {
                if (__result == null || !ContainsCareerUniqueTrait(__1 as IEnumerable))
                    return;

                string itemId = Convert.ToString(GetMemberValueByName(__result,
                    "StringId"));
                if (!String.IsNullOrWhiteSpace(itemId))
                    ProtectedItemIds.Add(itemId);
                ProtectItemFromMerchants(__result);
            }
            catch
            {
                // A compatibility hook must never break ToR item creation.
            }
        }

        private static bool ContainsCareerUniqueTrait(IEnumerable traits)
        {
            if (traits == null)
                return false;
            foreach (object raw in traits)
            {
                string id = Convert.ToString(raw);
                if (!String.IsNullOrEmpty(id) && id.StartsWith("torcu_",
                    StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static void CleanCurrentSettlementMarketOnce()
        {
            try
            {
                Type settlementType = FindLoadedType(
                    "TaleWorlds.CampaignSystem.Settlements.Settlement");
                PropertyInfo currentProperty = settlementType == null ? null :
                    settlementType.GetProperty("CurrentSettlement",
                        BindingFlags.Static | BindingFlags.Public |
                        BindingFlags.NonPublic);
                object settlement = currentProperty == null ? null :
                    currentProperty.GetValue(null, null);
                if (settlement == null)
                    return;

                string settlementId = Convert.ToString(GetMemberValueByName(
                    settlement, "StringId"));
                if (String.IsNullOrWhiteSpace(settlementId) ||
                    CleanedSettlementIds.Contains(settlementId))
                    return;

                // Exactly one bounded cleanup per visited settlement/session.
                CleanedSettlementIds.Add(settlementId);
                object roster = GetMemberValueByName(settlement, "ItemRoster");
                IEnumerable enumerable = roster as IEnumerable;
                if (enumerable == null || ProtectedItemIds.Count == 0)
                    return;

                List<object> snapshot = new List<object>();
                foreach (object element in enumerable)
                    snapshot.Add(element);

                int removedStacks = 0;
                for (int i = 0; i < snapshot.Count; i++)
                {
                    object rosterElement = snapshot[i];
                    object equipmentElement = GetMemberValueByName(rosterElement,
                        "EquipmentElement");
                    object item = GetMemberValueByName(equipmentElement, "Item");
                    string itemId = Convert.ToString(GetMemberValueByName(item,
                        "StringId"));
                    if (String.IsNullOrWhiteSpace(itemId) ||
                        !ProtectedItemIds.Contains(itemId))
                        continue;

                    object amountValue = GetMemberValueByName(rosterElement,
                        "Amount");
                    int amount = amountValue == null ? 0 :
                        Convert.ToInt32(amountValue);
                    if (amount <= 0 || equipmentElement == null)
                        continue;

                    MethodInfo addToCounts = FindAddToCountsForElement(
                        roster.GetType(), equipmentElement.GetType());
                    if (addToCounts == null)
                        throw new MissingMethodException(roster.GetType().FullName,
                            "AddToCounts(EquipmentElement, int)");
                    addToCounts.Invoke(roster, new object[] {
                        equipmentElement, -amount });
                    removedStacks++;
                }

                if (removedStacks > 0)
                    LogInfo("Removed " + removedStacks +
                        " leaked career-set merchandise stack(s) from " +
                        settlementId + ".");
            }
            catch (Exception ex)
            {
                LogFailureOnce("Current-settlement set-item cleanup failed: " +
                    ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static MethodInfo FindAddToCountsForElement(Type rosterType,
            Type elementType)
        {
            if (rosterType == null || elementType == null)
                return null;
            MethodInfo[] methods = rosterType.GetMethods(BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.Instance);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (method.Name != "AddToCounts")
                    continue;
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 2 &&
                    parameters[0].ParameterType == elementType &&
                    parameters[1].ParameterType == typeof(int))
                    return method;
            }
            return null;
        }

        private static MethodInfo FindStaticMethodByCount(Type type,
            string name, int parameterCount)
        {
            if (type == null)
                return null;
            MethodInfo[] methods = type.GetMethods(BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.Static);
            for (int i = 0; i < methods.Length; i++)
                if (methods[i].Name == name &&
                    methods[i].GetParameters().Length == parameterCount)
                    return methods[i];
            return null;
        }

        private static object CreateHarmonyMethod(Type harmonyMethodType,
            MethodInfo patchMethod)
        {
            ConstructorInfo constructor = harmonyMethodType.GetConstructor(
                new[] { typeof(MethodInfo) });
            if (constructor != null)
                return constructor.Invoke(new object[] { patchMethod });

            object result = Activator.CreateInstance(harmonyMethodType);
            FieldInfo field = harmonyMethodType.GetField("method",
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance);
            if (field != null)
            {
                field.SetValue(result, patchMethod);
                return result;
            }

            PropertyInfo property = harmonyMethodType.GetProperty("method",
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance);
            if (property != null)
            {
                property.SetValue(result, patchMethod, null);
                return result;
            }
            throw new MissingMemberException(harmonyMethodType.FullName,
                "method");
        }

        private static void ApplyHarmonyPatch(Type harmonyType,
            object harmony, MethodInfo original, object postfix)
        {
            MethodInfo[] methods = harmonyType.GetMethods(BindingFlags.Public |
                BindingFlags.Instance);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo candidate = methods[i];
                if (candidate.Name != "Patch")
                    continue;
                ParameterInfo[] parameters = candidate.GetParameters();
                if (parameters.Length < 3 ||
                    !typeof(MethodBase).IsAssignableFrom(
                        parameters[0].ParameterType))
                    continue;

                object[] args = new object[parameters.Length];
                args[0] = original;
                bool usable = true;
                for (int p = 1; p < parameters.Length; p++)
                {
                    string name = parameters[p].Name ?? String.Empty;
                    if (String.Equals(name, "postfix",
                        StringComparison.OrdinalIgnoreCase))
                        args[p] = postfix;
                    else if (String.Equals(name, "prefix",
                        StringComparison.OrdinalIgnoreCase))
                        args[p] = null;
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
                "Patch(MethodBase, ...)");
        }

        private static object GetMemberValueByName(object instance, string name)
        {
            if (instance == null)
                return null;
            Type type = instance.GetType();
            while (type != null)
            {
                PropertyInfo property = type.GetProperty(name,
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (property != null)
                    return property.GetValue(instance, null);
                FieldInfo field = type.GetField(name,
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (field != null)
                    return field.GetValue(instance);
                type = type.BaseType;
            }
            return null;
        }

        private static bool TrySetMemberValue(object instance, string name,
            object value)
        {
            if (instance == null)
                return false;
            Type type = instance.GetType();
            while (type != null)
            {
                PropertyInfo property = type.GetProperty(name,
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (property != null && property.GetSetMethod(true) != null)
                {
                    property.SetValue(instance, value, null);
                    return true;
                }
                FieldInfo field = type.GetField(name,
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (field != null)
                {
                    field.SetValue(instance, value);
                    return true;
                }
                type = type.BaseType;
            }
            return false;
        }

        private static void LogInfo(string message)
        {
            try
            {
                ResolveMainAssembly();
                Type log = _mainAssembly == null ? null :
                    _mainAssembly.GetType("TORCareerUniques.ModLog", false);
                MethodInfo info = log == null ? null : log.GetMethod("Info",
                    BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.NonPublic);
                if (info != null)
                    info.Invoke(null, new object[] { message });
            }
            catch { }
        }

        private static void LogFailureOnce(string message)
        {
            if (_loggedFailure)
                return;
            _loggedFailure = true;
            try
            {
                ResolveMainAssembly();
                Type log = _mainAssembly == null ? null :
                    _mainAssembly.GetType("TORCareerUniques.ModLog", false);
                MethodInfo error = log == null ? null : log.GetMethod("Error",
                    BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.NonPublic);
                if (error != null)
                    error.Invoke(null, new object[] { message });
            }
            catch { }
        }
    }
}
