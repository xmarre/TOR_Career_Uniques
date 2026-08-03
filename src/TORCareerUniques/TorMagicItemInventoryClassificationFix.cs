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
    /// Corrects TOR's inventory-row magic-item check for EquipmentElements that
    /// carry an ItemModifier. Bannerlord supplies Item.StringId followed by the
    /// modifier StringId, while TOR compares the modifier against the end of the
    /// item-map key and therefore rejects a valid modified magic item.
    /// </summary>
    internal static class TorMagicItemInventoryClassificationFix
    {
        private const string HarmonyId =
            "torcareeruniques.tor-magic-item-inventory-classification.1.7.41";

        private static readonly object Sync = new object();
        private static readonly Dictionary<string, bool> ResultCache =
            new Dictionary<string, bool>(StringComparer.Ordinal);
        private static readonly HashSet<string> LoggedCorrections =
            new HashSet<string>(StringComparer.Ordinal);

        private static MethodInfo _getAdditionalPropertiesReadOnly;
        private static FieldInfo _itemTraitsField;
        private static string[] _itemModifierIds;
        private static object _modifierCacheObjectManager;
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
                MethodInfo hasMagicItemId = managerType == null ? null :
                    AccessTools.Method(managerType, "HasMagicItemId",
                        new[] { typeof(string) });
                MethodInfo addCraftedItem = managerType == null ? null :
                    AccessTools.Method(managerType, "AddCraftedItem", new[]
                    {
                        typeof(string), typeof(string), typeof(List<string>)
                    });
                _getAdditionalPropertiesReadOnly = managerType == null ? null :
                    AccessTools.Method(managerType,
                        "GetAdditionalPropertiesReadOnly",
                        new[] { typeof(string) });
                MethodInfo classifierPostfix = AccessTools.Method(
                    typeof(TorMagicItemInventoryClassificationFix),
                    nameof(AfterHasMagicItemId));
                MethodInfo registrationPostfix = AccessTools.Method(
                    typeof(TorMagicItemInventoryClassificationFix),
                    nameof(AfterAddCraftedItem));

                if (hasMagicItemId == null || addCraftedItem == null ||
                    _getAdditionalPropertiesReadOnly == null ||
                    classifierPostfix == null || registrationPostfix == null)
                {
                    throw new MissingMethodException(
                        "TOR magic-item inventory classification APIs were not found.");
                }

                Harmony harmony = new Harmony(HarmonyId);
                harmony.Patch(hasMagicItemId,
                    postfix: new HarmonyMethod(classifierPostfix)
                    {
                        priority = Priority.Last
                    });
                harmony.Patch(addCraftedItem,
                    postfix: new HarmonyMethod(registrationPostfix)
                    {
                        priority = Priority.Last
                    });
                _installed = true;
                ModLog.AlwaysInfo("Installed modifier-aware TOR inventory " +
                    "magic-item classification.");
            }
            catch (Exception ex)
            {
                ModLog.Error("TOR inventory magic-item classification fix " +
                    "could not be installed: " + ex.GetType().Name + ": " +
                    ex.Message);
            }
        }

        internal static void ResetSession()
        {
            lock (Sync)
            {
                ResultCache.Clear();
                LoggedCorrections.Clear();
                _itemModifierIds = null;
                _modifierCacheObjectManager = null;
                _itemTraitsField = null;
            }
            _loggedRuntimeFailure = false;
        }

        // Harmony postfix for ExtendedItemObjectManager.HasMagicItemId(string).
        // Preserve every true result from TOR and repair only its modified-id false
        // negatives.
        private static void AfterHasMagicItemId(string __0, ref bool __result)
        {
            if (__result || String.IsNullOrEmpty(__0))
                return;

            try
            {
                bool corrected;
                bool cached;
                lock (Sync)
                    cached = ResultCache.TryGetValue(__0, out corrected);

                string itemId = null;
                string modifierId = null;
                if (!cached)
                {
                    bool resolved = TryResolveModifiedMagicItemId(__0,
                        out itemId, out modifierId);
                    lock (Sync)
                    {
                        if (!ResultCache.TryGetValue(__0, out corrected))
                        {
                            corrected = resolved;
                            ResultCache[__0] = corrected;
                        }
                    }
                }

                if (!corrected)
                    return;

                __result = true;
                bool shouldLog;
                lock (Sync)
                    shouldLog = LoggedCorrections.Add(__0);
                if (shouldLog)
                {
                    ModLog.Info("Corrected TOR inventory magic-item background " +
                        "for '" + __0 + "'" +
                        (String.IsNullOrEmpty(itemId) ? String.Empty :
                            " (item='" + itemId + "', modifier='" +
                            modifierId + "')") + ".");
                }
            }
            catch (Exception ex)
            {
                if (_loggedRuntimeFailure)
                    return;
                _loggedRuntimeFailure = true;
                ModLog.Error("TOR modifier-aware inventory magic-item check " +
                    "failed: " + ex.GetType().Name + ": " + ex.Message +
                    ". TOR's original result was retained.");
            }
        }

        private static void AfterAddCraftedItem()
        {
            lock (Sync)
                ResultCache.Clear();
        }

        private static bool TryResolveModifiedMagicItemId(string uiStringId,
            out string itemId, out string modifierId)
        {
            itemId = null;
            modifierId = null;
            string[] modifierIds = GetItemModifierIds();
            for (int i = 0; i < modifierIds.Length; i++)
            {
                string candidateModifier = modifierIds[i];
                if (uiStringId.Length <= candidateModifier.Length ||
                    !uiStringId.EndsWith(candidateModifier,
                        StringComparison.Ordinal))
                    continue;

                string candidateItem = uiStringId.Substring(0,
                    uiStringId.Length - candidateModifier.Length);
                if (!HasRegisteredItemTraits(candidateItem))
                    continue;

                itemId = candidateItem;
                modifierId = candidateModifier;
                return true;
            }
            return false;
        }

        private static string[] GetItemModifierIds()
        {
            MBObjectManager manager = MBObjectManager.Instance;
            if (manager == null)
                return Array.Empty<string>();

            lock (Sync)
            {
                if (Object.ReferenceEquals(_modifierCacheObjectManager,
                    manager) && _itemModifierIds != null)
                    return _itemModifierIds;
            }

            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (ItemModifier modifier in
                manager.GetObjectTypeList<ItemModifier>())
            {
                if (modifier != null &&
                    !String.IsNullOrEmpty(modifier.StringId))
                    ids.Add(modifier.StringId);
            }

            string[] result = new string[ids.Count];
            ids.CopyTo(result);
            Array.Sort(result, delegate(string left, string right)
            {
                int length = right.Length.CompareTo(left.Length);
                return length != 0 ? length :
                    String.CompareOrdinal(left, right);
            });

            lock (Sync)
            {
                if (!Object.ReferenceEquals(_modifierCacheObjectManager,
                    manager) || _itemModifierIds == null)
                {
                    _modifierCacheObjectManager = manager;
                    _itemModifierIds = result;
                }
                return _itemModifierIds;
            }
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
    }
}
