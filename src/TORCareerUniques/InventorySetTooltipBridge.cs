using System;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;

namespace TORCareerUniques
{
    internal static class InventorySetTooltipBridge
    {
        private const string HarmonyId = "torcareeruniques.inventory.settooltips";

        private static bool _initialized;
        private static bool _tooltipPatched;
        private static object _harmony;
        private static float _retryElapsed;
        private static bool _loggedHarmonyWait;
        private static bool _loggedIncomplete;
        private static bool _loggedInstallFailure;
        private static bool _loggedFirstTooltipInjection;
        private static bool _loggedTooltipRuntimeFailure;
        private static bool _loggedConditionalTraitFilterMismatch;

        internal static void Initialize()
        {
            if (_initialized)
                return;
            _initialized = true;
            TryInstallPatches();
        }

        internal static void Tick(float elapsed)
        {
            UIIconPassThrough.Tick();
            if (_tooltipPatched)
                return;

            _retryElapsed += elapsed;
            if (_retryElapsed < 5.0f)
                return;
            _retryElapsed = 0.0f;
            TryInstallPatches();
        }

        private static void TryInstallPatches()
        {
            try
            {
                Type harmonyType = FindType("HarmonyLib.Harmony", "0Harmony");
                Type harmonyMethodType = FindType("HarmonyLib.HarmonyMethod", "0Harmony");
                if (harmonyType == null || harmonyMethodType == null)
                {
                    if (!_loggedHarmonyWait)
                    {
                        _loggedHarmonyWait = true;
                        ModLog.Error("Set-tooltip UI patch is waiting for HarmonyLib from 0Harmony.");
                    }
                    return;
                }

                if (_harmony == null)
                    _harmony = Activator.CreateInstance(harmonyType,
                        new object[] { HarmonyId });

                if (!_tooltipPatched)
                {
                    Type torItemMenuType = FindType(
                        "TOR_Core.Items.TorItemMenuVM", "TOR_Core");
                    MethodInfo original = FindMethod(torItemMenuType,
                        "SetItemExtra", 4);
                    MethodInfo postfix = typeof(InventorySetTooltipBridge).GetMethod(
                        "AfterSetItemExtra",
                        BindingFlags.Public | BindingFlags.Static);
                    if (original != null && postfix != null)
                    {
                        ApplyPatch(harmonyType, harmonyMethodType, original,
                            null, postfix);
                        _tooltipPatched = true;
                        _loggedIncomplete = false;
                        ModLog.Info("Installed compact ToR set-description and carrier-trait filter patch.");
                    }
                }

                if (!_tooltipPatched && !_loggedIncomplete)
                {
                    _loggedIncomplete = true;
                    ModLog.Error("Set-tooltip UI patch is incomplete: directText=no. Retrying.");
                }
            }
            catch (Exception ex)
            {
                if (!_loggedInstallFailure)
                {
                    _loggedInstallFailure = true;
                    ModLog.Error("Set-tooltip UI patch installation failed: " +
                        FormatException(ex));
                }
            }
        }

        // Harmony postfix for TOR_Core.Items.TorItemMenuVM.SetItemExtra.
        // __0 is the original SPItemVM argument.
        public static void AfterSetItemExtra(object __instance, object __0)
        {
            try
            {
                int hiddenRuntimeTraits = RemoveHiddenRuntimeTraitViewModels(
                    __instance, __0);

                string itemId;
                string description;
                if (!SetItemRuntime.TryBuildTooltipForItemViewModel(__0,
                    out itemId, out description))
                {
                    if (hiddenRuntimeTraits > 0)
                    {
                        ModLog.Verbose("Suppressed " + hiddenRuntimeTraits +
                            " conditional set traits from the tooltip of a non-set carrier item.");
                    }
                    return;
                }

                SetProperty(__instance, "ItemDescription", description);
                SetProperty(__instance, "HasDescription", true);
                SetProperty(__instance, "IsMagicItem", true);
                if (!_loggedFirstTooltipInjection)
                {
                    _loggedFirstTooltipInjection = true;
                    ModLog.Info("Verified compact live set tooltip for " + itemId +
                        ": one description block, duplicateRows=0, hiddenCarrierEffects=" +
                        hiddenRuntimeTraits + ".");
                }
                ModLog.Verbose("Injected one compact set-description block into " +
                    "TorItemMenuVM for " + itemId + "; suppressed " +
                    hiddenRuntimeTraits + " conditional carrier traits.");
            }
            catch (Exception ex)
            {
                if (!_loggedTooltipRuntimeFailure)
                {
                    _loggedTooltipRuntimeFailure = true;
                    ModLog.Error("Direct set-tooltip injection failed: " +
                        FormatException(ex));
                }
            }
        }

        private static int RemoveHiddenRuntimeTraitViewModels(object itemMenu,
            object itemViewModel)
        {
            List<string> ids = SetItemRuntime.GetTooltipTraitIdsForItemViewModel(
                itemViewModel);
            if (ids == null || ids.Count == 0)
                return 0;

            List<int> hiddenIndexes = new List<int>();
            for (int i = 0; i < ids.Count; i++)
            {
                if (SetItemRuntime.IsHiddenTooltipTraitId(ids[i]))
                    hiddenIndexes.Add(i);
            }
            if (hiddenIndexes.Count == 0)
                return 0;

            object list = GetProperty(itemMenu, "ItemTraitList");
            if (list == null)
                throw new MissingMemberException(itemMenu.GetType().FullName,
                    "ItemTraitList");

            int count = Convert.ToInt32(GetProperty(list, "Count"));
            if (count != ids.Count)
            {
                if (!_loggedConditionalTraitFilterMismatch)
                {
                    _loggedConditionalTraitFilterMismatch = true;
                    ModLog.Error("Conditional set-trait tooltip filtering skipped because " +
                        "ToR produced " + count + " trait VMs for " + ids.Count +
                        " item-trait ids. No unrelated tooltip entry was removed.");
                }
                return 0;
            }

            MethodInfo removeAt = list.GetType().GetMethod("RemoveAt",
                BindingFlags.Public | BindingFlags.Instance,
                null, new[] { typeof(int) }, null);
            if (removeAt == null)
                throw new MissingMethodException(list.GetType().FullName,
                    "RemoveAt(Int32)");

            for (int i = hiddenIndexes.Count - 1; i >= 0; i--)
                removeAt.Invoke(list, new object[] { hiddenIndexes[i] });
            return hiddenIndexes.Count;
        }

        private static int InjectNativeSetTraitViewModels(object itemMenu,
            object itemViewModel)
        {
            List<string> ids = SetItemRuntime.GetSetDisplayTraitIdsForItemViewModel(
                itemViewModel);
            if (ids == null || ids.Count == 0)
                return 0;

            object list = GetProperty(itemMenu, "ItemTraitList");
            if (list == null)
                throw new MissingMemberException(itemMenu.GetType().FullName,
                    "ItemTraitList");

            MethodInfo add = null;
            MethodInfo[] methods = list.GetType().GetMethods(
                BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < methods.Length; i++)
            {
                if (methods[i].Name == "Add" &&
                    methods[i].GetParameters().Length == 1)
                {
                    add = methods[i];
                    break;
                }
            }
            if (add == null)
                throw new MissingMethodException(list.GetType().FullName, "Add");

            Type managerType = FindType("TOR_Core.Items.ItemTraitManager", "TOR_Core");
            object manager = managerType == null ? null :
                managerType.GetProperty("Instance",
                    BindingFlags.Public | BindingFlags.Static).GetValue(null, null);
            MethodInfo getById = managerType == null ? null :
                managerType.GetMethod("GetItemTraitByStringId",
                    BindingFlags.Public | BindingFlags.Instance);
            if (manager == null || getById == null)
                throw new MissingMethodException("TOR_Core.Items.ItemTraitManager",
                    "GetItemTraitByStringId");

            Type vmType = add.GetParameters()[0].ParameterType;
            ConstructorInfo constructor = null;
            ParameterInfo constructorParameter = null;
            ConstructorInfo[] constructors = vmType.GetConstructors(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            for (int i = 0; i < constructors.Length; i++)
            {
                ParameterInfo[] parameters = constructors[i].GetParameters();
                if (parameters.Length == 1)
                {
                    constructor = constructors[i];
                    constructorParameter = parameters[0];
                    if (parameters[0].ParameterType == typeof(string))
                        break;
                }
            }
            if (constructor == null || constructorParameter == null)
                throw new MissingMethodException(vmType.FullName, ".ctor(string/ItemTrait)");

            int added = 0;
            for (int i = 0; i < ids.Count; i++)
            {
                object trait = getById.Invoke(manager, new object[] { ids[i] });
                if (trait == null)
                    throw new InvalidOperationException("Set-summary trait '" + ids[i] +
                        "' is absent from ToR's trait registry.");

                object argument;
                if (constructorParameter.ParameterType == typeof(string))
                    argument = ids[i];
                else if (constructorParameter.ParameterType.IsInstanceOfType(trait))
                    argument = trait;
                else
                    throw new InvalidOperationException(vmType.FullName +
                        " has an unsupported one-argument constructor type: " +
                        constructorParameter.ParameterType.FullName + ".");

                object vm = constructor.Invoke(new object[] { argument });
                add.Invoke(list, new object[] { vm });
                added++;
            }
            return added;
        }

        private static void InjectSetPropertyRows(object itemMenu,
            IList<SetTooltipRow> rows)
        {
            object target = GetProperty(itemMenu, "TargetItemProperties");
            if (target == null)
                throw new MissingMemberException(itemMenu.GetType().FullName,
                    "TargetItemProperties");

            MethodInfo add = null;
            MethodInfo[] methods = target.GetType().GetMethods(
                BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < methods.Length; i++)
            {
                if (methods[i].Name == "Add" &&
                    methods[i].GetParameters().Length == 1)
                {
                    add = methods[i];
                    break;
                }
            }
            if (add == null)
                throw new MissingMethodException(target.GetType().FullName, "Add");

            Type rowType = add.GetParameters()[0].ParameterType;
            for (int i = 0; i < rows.Count; i++)
            {
                object row = CreateTooltipPropertyRow(rowType, rows[i]);
                add.Invoke(target, new object[] { row });
            }
        }

        private static object CreateTooltipPropertyRow(Type rowType,
            SetTooltipRow row)
        {
            ConstructorInfo[] constructors = rowType.GetConstructors(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            for (int i = 0; i < constructors.Length; i++)
            {
                ParameterInfo[] parameters = constructors[i].GetParameters();
                if (parameters.Length == 5 &&
                    parameters[0].ParameterType == typeof(string) &&
                    parameters[1].ParameterType == typeof(string) &&
                    parameters[2].ParameterType == typeof(int) &&
                    parameters[3].ParameterType == typeof(bool) &&
                    !parameters[4].ParameterType.IsValueType)
                {
                    object constructed = constructors[i].Invoke(new object[] {
                        row.Definition, row.Value, 20, false, null
                    });
                    TrySetProperty(constructed, "PropertyModifier", 1);
                    return constructed;
                }
            }

            object result = Activator.CreateInstance(rowType);
            SetProperty(result, "DefinitionLabel", row.Definition);
            SetProperty(result, "ValueLabel", row.Value);
            SetProperty(result, "TextHeight", 20);
            SetProperty(result, "OnlyShowWhenExtended", false);
            TrySetProperty(result, "PropertyModifier", 1);
            return result;
        }

        private static void ApplyPatch(Type harmonyType, Type harmonyMethodType,
            MethodInfo original, MethodInfo prefixMethod, MethodInfo postfixMethod)
        {
            object prefix = prefixMethod == null ? null :
                CreateHarmonyMethod(harmonyMethodType, prefixMethod);
            object postfix = postfixMethod == null ? null :
                CreateHarmonyMethod(harmonyMethodType, postfixMethod);

            MethodInfo[] methods = harmonyType.GetMethods(
                BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo candidate = methods[i];
                if (candidate.Name != "Patch")
                    continue;

                ParameterInfo[] parameters = candidate.GetParameters();
                if (parameters.Length < 3 ||
                    !typeof(MethodBase).IsAssignableFrom(parameters[0].ParameterType))
                    continue;

                object[] args = new object[parameters.Length];
                args[0] = original;
                bool usable = true;
                for (int p = 1; p < parameters.Length; p++)
                {
                    string name = parameters[p].Name ?? String.Empty;
                    if (String.Equals(name, "prefix", StringComparison.OrdinalIgnoreCase))
                        args[p] = prefix;
                    else if (String.Equals(name, "postfix", StringComparison.OrdinalIgnoreCase))
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

                candidate.Invoke(_harmony, args);
                return;
            }

            throw new MissingMethodException(harmonyType.FullName,
                "Patch(MethodBase, HarmonyMethod, HarmonyMethod, ...)");
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
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null)
            {
                field.SetValue(result, patchMethod);
                return result;
            }

            PropertyInfo property = harmonyMethodType.GetProperty("method",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (property != null && property.CanWrite)
            {
                property.SetValue(result, patchMethod, null);
                return result;
            }

            throw new MissingMemberException(harmonyMethodType.FullName, "method");
        }

        private static MethodInfo FindMethod(Type type, string name, int parameterCount)
        {
            if (type == null)
                return null;
            MethodInfo[] methods = type.GetMethods(BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.Instance);
            for (int i = 0; i < methods.Length; i++)
            {
                if (methods[i].Name == name &&
                    methods[i].GetParameters().Length == parameterCount)
                    return methods[i];
            }
            return null;
        }

        private static Type FindType(string fullName, string assemblyName)
        {
            Type type = Type.GetType(fullName + ", " + assemblyName, false);
            if (type != null)
                return type;

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                type = assemblies[i].GetType(fullName, false);
                if (type != null)
                    return type;
            }
            return null;
        }

        private static object GetProperty(object instance, string name)
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
                type = type.BaseType;
            }
            return null;
        }

        private static void SetProperty(object instance, string name, object value)
        {
            if (instance == null)
                throw new ArgumentNullException("instance");

            Type type = instance.GetType();
            while (type != null)
            {
                PropertyInfo property = type.GetProperty(name,
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (property != null)
                {
                    property.SetValue(instance, value, null);
                    return;
                }
                type = type.BaseType;
            }
            throw new MissingMemberException(instance.GetType().FullName, name);
        }

        private static bool TrySetProperty(object instance, string name, object value)
        {
            if (instance == null)
                return false;
            Type type = instance.GetType();
            while (type != null)
            {
                PropertyInfo property = type.GetProperty(name,
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (property != null && property.CanWrite)
                {
                    if (value != null && !property.PropertyType.IsInstanceOfType(value))
                        value = Convert.ChangeType(value, property.PropertyType);
                    property.SetValue(instance, value, null);
                    return true;
                }
                type = type.BaseType;
            }
            return false;
        }

        private static string FormatException(Exception ex)
        {
            TargetInvocationException invocation = ex as TargetInvocationException;
            if (invocation != null && invocation.InnerException != null)
                ex = invocation.InnerException;
            return ex.GetType().FullName + ": " + ex.Message +
                Environment.NewLine + ex.StackTrace;
        }
    }
}
