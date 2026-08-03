using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;

namespace TORCareerUniques
{
    /// <summary>
    /// Applies TOR's magic-item inventory brush from the actual ItemObject bound
    /// to the row. Bannerlord's row id appends ItemModifier.StringId to the item
    /// id, while TOR's stock string classifier handles that combined id
    /// incorrectly.
    /// </summary>
    internal static class TorMagicItemInventoryClassificationFix
    {
        private const string HarmonyId =
            "torcareeruniques.tor-magic-inventory-row.1.7.41";

        private static readonly object Sync = new object();
        private static readonly Dictionary<string, bool> ResultCache =
            new Dictionary<string, bool>(StringComparer.Ordinal);
        private static readonly HashSet<string> LoggedCorrections =
            new HashSet<string>(StringComparer.Ordinal);

        private static MethodInfo _getAdditionalPropertiesReadOnly;
        private static MethodInfo _updateEquipmentTypeState;
        private static FieldInfo _itemTraitsField;
        private static Type _nativeTupleType;
        private static MBObjectManager _cacheObjectManager;
        private static bool _installed;
        private static bool _loggedRuntimeFailure;

        internal static void Initialize()
        {
            if (_installed)
                return;

            try
            {
                Type managerType = AccessTools.TypeByName(
                    "TOR_Core.Items.ExtendedItemObjectManager");
                Type torTupleType = AccessTools.TypeByName(
                    "TOR_Core.Items.TorInventoryItemTupleWidget");
                _nativeTupleType = AccessTools.TypeByName(
                    "TaleWorlds.MountAndBlade.GauntletUI.Widgets.Inventory.InventoryItemTupleWidget");
                Type twoDimensionContextType = AccessTools.TypeByName(
                    "TaleWorlds.TwoDimension.TwoDimensionContext");
                Type drawContextType = AccessTools.TypeByName(
                    "TaleWorlds.TwoDimension.TwoDimensionDrawContext");

                _getAdditionalPropertiesReadOnly = managerType == null ? null :
                    AccessTools.Method(managerType,
                        "GetAdditionalPropertiesReadOnly",
                        new[] { typeof(string) });
                MethodInfo addCraftedItem = managerType == null ? null :
                    AccessTools.Method(managerType, "AddCraftedItem", new[]
                    {
                        typeof(string), typeof(string), typeof(List<string>)
                    });
                MethodInfo renderTarget = torTupleType == null ||
                    twoDimensionContextType == null || drawContextType == null
                    ? null
                    : AccessTools.Method(torTupleType, "OnRender", new[]
                    {
                        twoDimensionContextType, drawContextType
                    });
                _updateEquipmentTypeState = _nativeTupleType == null ? null :
                    AccessTools.Method(_nativeTupleType,
                        "UpdateEquipmentTypeState", Type.EmptyTypes);
                MethodInfo renderPrefix = AccessTools.Method(
                    typeof(TorMagicItemInventoryClassificationFix),
                    nameof(BeforeTorInventoryItemTupleRender));
                MethodInfo registrationPostfix = AccessTools.Method(
                    typeof(TorMagicItemInventoryClassificationFix),
                    nameof(AfterAddCraftedItem));

                if (_getAdditionalPropertiesReadOnly == null ||
                    addCraftedItem == null || renderTarget == null ||
                    _updateEquipmentTypeState == null || renderPrefix == null ||
                    registrationPostfix == null)
                {
                    throw new MissingMethodException(
                        "TOR inventory-row magic-item APIs were not found.");
                }

                Harmony harmony = new Harmony(HarmonyId);
                harmony.Patch(renderTarget,
                    prefix: new HarmonyMethod(renderPrefix)
                    {
                        priority = Priority.Last
                    });
                harmony.Patch(addCraftedItem,
                    postfix: new HarmonyMethod(registrationPostfix)
                    {
                        priority = Priority.Last
                    });

                _installed = true;
                ModLog.AlwaysInfo("Installed direct TOR magic-item inventory-row " +
                    "classification from the bound ItemObject id.");
            }
            catch (Exception ex)
            {
                ModLog.Error("TOR inventory-row magic-item fix could not be " +
                    "installed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        internal static void ResetSession()
        {
            lock (Sync)
            {
                ResultCache.Clear();
                LoggedCorrections.Clear();
                _cacheObjectManager = null;
                _itemTraitsField = null;
            }
            _loggedRuntimeFailure = false;
        }

        // Runs after the existing UIIconPassThrough prefix. Restore the native
        // brush again so ordering remains safe, then apply the magic brush from
        // the exact bound ItemObject. TOR's original renderer still runs.
        private static void BeforeTorInventoryItemTupleRender(object __instance)
        {
            if (__instance == null || _nativeTupleType == null ||
                _updateEquipmentTypeState == null ||
                !_nativeTupleType.IsInstanceOfType(__instance))
                return;

            try
            {
                _updateEquipmentTypeState.Invoke(__instance, null);

                string rowId = Convert.ToString(GetPropertyRecursive(__instance,
                    "ItemID"));
                string itemId;
                if (!IsMagicItemUiId(rowId, out itemId))
                    return;

                object mainContainer = GetPropertyRecursive(__instance,
                    "MainContainer");
                object magicBrush = GetFieldRecursive(__instance,
                    "_magicBrush");
                if (mainContainer == null || magicBrush == null)
                    return;

                object currentBrush = GetPropertyRecursive(mainContainer,
                    "Brush");
                object characterCantUseBrush = GetPropertyRecursive(__instance,
                    "CharacterCantUseBrush");
                if (IsBrushCloneRelated(currentBrush,
                    characterCantUseBrush))
                    return;

                if (!SetPropertyRecursive(mainContainer, "Brush", magicBrush))
                    return;

                bool shouldLog;
                lock (Sync)
                    shouldLog = LoggedCorrections.Add(rowId);
                if (shouldLog)
                {
                    ModLog.Info("Applied TOR magic inventory background for row '" +
                        rowId + "' using registered item '" + itemId + "'.");
                }
            }
            catch (Exception ex)
            {
                if (_loggedRuntimeFailure)
                    return;
                _loggedRuntimeFailure = true;
                ModLog.Error("TOR inventory-row magic-item reconciliation " +
                    "failed: " + ex.GetType().Name + ": " + ex.Message + ".");
            }
        }

        private static void AfterAddCraftedItem()
        {
            lock (Sync)
                ResultCache.Clear();
        }

        private static bool IsMagicItemUiId(string uiStringId,
            out string itemId)
        {
            itemId = null;
            if (String.IsNullOrEmpty(uiStringId) ||
                _getAdditionalPropertiesReadOnly == null)
                return false;

            MBObjectManager manager = MBObjectManager.Instance;
            if (manager == null)
                return false;

            lock (Sync)
            {
                if (!Object.ReferenceEquals(_cacheObjectManager, manager))
                {
                    ResultCache.Clear();
                    LoggedCorrections.Clear();
                    _cacheObjectManager = manager;
                }

                bool cached;
                if (ResultCache.TryGetValue(uiStringId, out cached))
                {
                    if (cached)
                        TryResolveRegisteredItemId(manager, uiStringId,
                            out itemId);
                    return cached;
                }
            }

            bool result = TryResolveRegisteredItemId(manager, uiStringId,
                out itemId) && HasRegisteredItemTraits(itemId);
            lock (Sync)
                ResultCache[uiStringId] = result;
            return result;
        }

        private static bool TryResolveRegisteredItemId(MBObjectManager manager,
            string uiStringId, out string itemId)
        {
            itemId = null;
            ItemObject exact = manager.GetObject<ItemObject>(uiStringId);
            if (exact != null)
            {
                itemId = exact.StringId;
                return !String.IsNullOrEmpty(itemId);
            }

            // The actual item id is the longest registered prefix. The suffix is
            // the EquipmentElement's modifier id. This avoids assuming that the
            // modifier is present in MBObjectManager's global modifier catalogue.
            for (int length = uiStringId.Length - 1; length > 0; length--)
            {
                ItemObject item = manager.GetObject<ItemObject>(
                    uiStringId.Substring(0, length));
                if (item == null)
                    continue;
                itemId = item.StringId;
                return !String.IsNullOrEmpty(itemId);
            }
            return false;
        }

        private static bool HasRegisteredItemTraits(string itemId)
        {
            object properties = _getAdditionalPropertiesReadOnly.Invoke(null,
                new object[] { itemId });
            if (properties == null)
                return false;

            if (_itemTraitsField == null ||
                _itemTraitsField.DeclaringType == null ||
                !_itemTraitsField.DeclaringType.IsInstanceOfType(properties))
            {
                _itemTraitsField = AccessTools.Field(properties.GetType(),
                    "ItemTraits");
            }

            object traits = _itemTraitsField == null ? null :
                _itemTraitsField.GetValue(properties);
            ICollection collection = traits as ICollection;
            if (collection != null)
                return collection.Count > 0;
            IEnumerable enumerable = traits as IEnumerable;
            if (enumerable == null)
                return false;

            IEnumerator enumerator = enumerable.GetEnumerator();
            try
            {
                return enumerator.MoveNext();
            }
            finally
            {
                IDisposable disposable = enumerator as IDisposable;
                if (disposable != null)
                    disposable.Dispose();
            }
        }

        private static bool IsBrushCloneRelated(object brush,
            object referenceBrush)
        {
            if (brush == null || referenceBrush == null)
                return false;
            if (Object.ReferenceEquals(brush, referenceBrush))
                return true;

            MethodInfo[] methods = brush.GetType().GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (method.Name != "IsCloneRelated")
                    continue;
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != 1 ||
                    !parameters[0].ParameterType.IsInstanceOfType(
                        referenceBrush))
                    continue;
                object result = method.Invoke(brush,
                    new object[] { referenceBrush });
                return result is bool && (bool)result;
            }
            return false;
        }

        private static bool SetPropertyRecursive(object instance, string name,
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
                if (property != null && property.CanWrite)
                {
                    property.SetValue(instance, value, null);
                    return true;
                }
                type = type.BaseType;
            }
            return false;
        }

        private static object GetFieldRecursive(object instance, string name)
        {
            if (instance == null)
                return null;
            Type type = instance.GetType();
            while (type != null)
            {
                FieldInfo field = type.GetField(name,
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (field != null)
                    return field.GetValue(instance);
                type = type.BaseType;
            }
            return null;
        }

        private static object GetPropertyRecursive(object instance, string name)
        {
            if (instance == null)
                return null;
            Type type = instance.GetType();
            while (type != null)
            {
                PropertyInfo property = type.GetProperty(name,
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (property != null && property.CanRead)
                    return property.GetValue(instance, null);
                type = type.BaseType;
            }
            return null;
        }
    }
}
