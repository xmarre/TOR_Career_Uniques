using System;
using System.Reflection;

[assembly: AssemblyVersion("1.7.41.0")]
[assembly: AssemblyFileVersion("1.7.41.0")]

namespace TORCareerUniques
{
    // InventoryScreenWidget owns the large item tooltip independently of SPItemVM.
    // The armor-value widget beside equipped head/body/glove/boot slots causes
    // ItemWidgetHoverEnd(null) while the cursor crosses toward the tooltip.
    // Retain only across that captured small widget branch and while the mouse is
    // inside the live tooltip rectangle. Native clearing resumes everywhere else.
    public static class UIIconPassThrough
    {
        private const string HarmonyId =
            "torcareeruniques.inventory.equippedarmortooltip";
        private const float MaxBridgeWidth = 220.0f;
        private const float MaxBridgeHeight = 220.0f;
        private const float MaxHorizontalGap = 140.0f;
        private const float MaxVerticalGap = 90.0f;
        private const float TooltipMargin = 8.0f;

        private static object _harmony;
        private static bool _widgetPatchInstalled;
        private static bool _magicBackgroundPatchInstalled;
        private static bool _itemImagePatchInstalled;
        private static bool _initialInstallAttempted;
        private static bool _assemblyLoadSubscribed;
        private static bool _installing;
        private static bool _loggedInstalled;
        private static bool _loggedFirstBridge;
        private static bool _loggedFirstClose;
        private static bool _loggedInstallFailure;
        private static bool _loggedRuntimeFailure;
        private static bool _loggedMagicBackgroundInstalled;
        private static bool _loggedMagicBackgroundFailure;
        private static bool _loggedItemImageInstalled;
        private static bool _loggedItemImageFailure;

        // Per-screen bridge state. These are object references on purpose: this
        // helper has no compile-time dependency on Gauntlet or CampaignSystem.
        private static object _retainedScreen;
        private static object _retainedItemWidget;
        private static object _capturedBridgeRoot;
        private static bool _enteredTooltip;
        private static Type _hoveredFieldOwnerType;
        private static FieldInfo _hoveredItemField;
        private static object _lastUnprotectedItemWidget;
        private static MethodInfo _updateEquipmentTypeState;
        private static MethodInfo _recoverRuntimeMagicItem;

        private static MethodInfo _logInfo;
        private static MethodInfo _logVerbose;
        private static MethodInfo _logError;

        public static void Tick()
        {
            if (AreAllPatchesInstalled())
                return;

            if (!_initialInstallAttempted)
            {
                _initialInstallAttempted = true;
                TryInstallWidgetPatch();
            }

            if (!AreAllPatchesInstalled())
                EnsureAssemblyLoadSubscription();
        }

        public static bool TryMakeHoveredGapPassThroughForValidation(
            out string widgetPath)
        {
            TryInstallWidgetPatch();
            widgetPath = _widgetPatchInstalled
                ? "InventoryScreenWidget.ItemWidgetHoverEnd"
                : String.Empty;
            return _widgetPatchInstalled;
        }

        // Harmony prefix for private
        // InventoryScreenWidget.ItemWidgetHoverEnd(InventoryItemButtonWidget).
        // __0 is null for the clear call emitted by InventoryScreenWidget.OnUpdate.
        public static bool BeforeInventoryScreenItemHoverEnd(object __instance,
            object __0)
        {
            try
            {
                if (__instance == null || __0 != null)
                {
                    ResetBridgeState();
                    return true;
                }

                object current = GetCurrentHoveredItemWidget(__instance);
                if (current == null)
                {
                    if (_retainedScreen != null)
                        ResetBridgeState();
                    _lastUnprotectedItemWidget = null;
                    return true;
                }

                // OnUpdate can emit the same null-hover clear every frame. Once a
                // widget is known not to be a protected armour slot, its repeated
                // clears become a reference comparison with no property traversal.
                if (Object.ReferenceEquals(current, _lastUnprotectedItemWidget))
                    return true;

                int equipmentIndex;
                if (!TryGetProtectedArmorIndex(current, out equipmentIndex))
                {
                    ResetBridgeState();
                    _lastUnprotectedItemWidget = current;
                    return true;
                }
                _lastUnprotectedItemWidget = null;

                object inventory = GetActiveInventoryViewModel();
                if (inventory != null)
                {
                    string slotProperty = GetSlotProperty(equipmentIndex);
                    object item = String.IsNullOrEmpty(slotProperty)
                        ? null
                        : GetPropertyRecursive(inventory, slotProperty);
                    if (item == null || IsEmptyItemViewModel(item))
                    {
                        ResetBridgeState();
                        return true;
                    }
                }

                object eventManager = GetPropertyRecursive(__instance,
                    "EventManager");
                object hoveredView = GetPropertyRecursive(eventManager,
                    "HoveredView");
                object tooltipRoot = GetPropertyOrField(__instance,
                    "InventoryTooltip");
                object targetTooltip = FindChildById(tooltipRoot,
                    "TargetItemTooltip");

                // A different equipped item started a new hover cycle.
                if (!Object.ReferenceEquals(_retainedScreen, __instance) ||
                    !Object.ReferenceEquals(_retainedItemWidget, current))
                {
                    ResetBridgeState();
                    _retainedScreen = __instance;
                    _retainedItemWidget = current;

                    if (IsInsideTooltip(hoveredView, eventManager, tooltipRoot,
                        targetTooltip))
                    {
                        _enteredTooltip = true;
                        LogFirstBridge(equipmentIndex,
                            "tooltip pane was reached directly");
                        return false;
                    }

                    _capturedBridgeRoot = CaptureSmallAdjacentBranch(
                        hoveredView, current, tooltipRoot, __instance);
                    if (_capturedBridgeRoot == null)
                    {
                        ResetBridgeState();
                        return true;
                    }

                    LogFirstBridge(equipmentIndex,
                        "captured armor-value branch " +
                        DescribeWidget(_capturedBridgeRoot));
                    return false;
                }

                // The tooltip can be event-transparent in some ToR layouts, so
                // use both ancestry and its live rectangle.
                if (IsInsideTooltip(hoveredView, eventManager, tooltipRoot,
                    targetTooltip))
                {
                    _enteredTooltip = true;
                    return false;
                }

                // Keep the tooltip only while crossing or returning across the
                // exact small widget branch captured by the first clear event.
                if (_capturedBridgeRoot != null &&
                    IsSameOrDescendant(hoveredView, _capturedBridgeRoot))
                {
                    return false;
                }

                // The cursor is now outside both the bridge and the tooltip.
                // Restore Bannerlord's native close immediately.
                string slotName = GetSlotName(equipmentIndex);
                bool hadEnteredTooltip = _enteredTooltip;
                ResetBridgeState();
                if (!_loggedFirstClose)
                {
                    _loggedFirstClose = true;
                    LogInfo("Verified equipped armor tooltip native close after " +
                        "leaving " + slotName + " tooltip; enteredTooltip=" +
                        (hadEnteredTooltip ? "yes" : "no") + ".");
                }
                return true;
            }
            catch (Exception ex)
            {
                ResetBridgeState();
                if (!_loggedRuntimeFailure)
                {
                    _loggedRuntimeFailure = true;
                    LogError("Equipped armor tooltip bridge failed at runtime: " +
                        FormatException(ex));
                }
                return true;
            }
        }


        // Harmony prefix for TOR's TorInventoryItemTupleWidget.OnRender.
        // Bannerlord's private updater derives the default, cannot-use, and
        // equipment-mode brush from the item currently bound to this recycled
        // tuple. TOR's override only assigns the magic brush and never restores
        // the native brush when the tuple is rebound to a non-magic item.
        public static void BeforeTorInventoryItemTupleRender(object __instance)
        {
            if (__instance == null || _updateEquipmentTypeState == null)
                return;

            try
            {
                _updateEquipmentTypeState.Invoke(__instance, null);
            }
            catch (Exception ex)
            {
                if (!_loggedMagicBackgroundFailure)
                {
                    _loggedMagicBackgroundFailure = true;
                    LogError("TOR magic-item background restoration failed at " +
                        "runtime: " + FormatException(ex));
                }
            }
        }

        // Harmony prefix for Bannerlord's item-image provider. The provider
        // resolves a thumbnail request by item StringId through MBObjectManager.
        // TOR's old weekly cleanup can leave a live roster/equipment reference
        // after removing that index. Repair the exact referenced runtime item
        // before the provider performs its lookup. Healthy ids return immediately.
        public static void BeforeItemImageTextureCreation(string __0)
        {
            if (String.IsNullOrEmpty(__0))
                return;

            try
            {
                if (_recoverRuntimeMagicItem == null)
                {
                    Type fixType = FindType(
                        "TORCareerUniques.TorMagicItemLifecycleFix",
                        "TORCareerUniques");
                    _recoverRuntimeMagicItem = fixType == null ? null :
                        fixType.GetMethod(
                            "RecoverReferencedRuntimeMagicItem",
                            BindingFlags.Public | BindingFlags.NonPublic |
                            BindingFlags.Static, null,
                            new[] { typeof(string) }, null);
                }

                if (_recoverRuntimeMagicItem != null)
                    _recoverRuntimeMagicItem.Invoke(null,
                        new object[] { __0 });
            }
            catch (Exception ex)
            {
                if (!_loggedItemImageFailure)
                {
                    _loggedItemImageFailure = true;
                    LogError("TOR runtime magic-item thumbnail repair failed at " +
                        "runtime: " + FormatException(ex));
                }
            }
        }

        private static void LogFirstBridge(int equipmentIndex, string detail)
        {
            if (!_loggedFirstBridge)
            {
                _loggedFirstBridge = true;
                LogInfo("Verified bounded equipped " +
                    GetSlotName(equipmentIndex) + " tooltip bridge: " + detail +
                    ". Native close remains enabled outside the bridge and tooltip.");
            }
            else
            {
                LogVerbose("Retained equipped " + GetSlotName(equipmentIndex) +
                    " tooltip across " + detail + ".");
            }
        }

        private static object CaptureSmallAdjacentBranch(object hoveredView,
            object itemWidget, object tooltipRoot, object screen)
        {
            if (hoveredView == null || itemWidget == null)
                return null;
            if (IsSameOrDescendant(hoveredView, itemWidget) ||
                IsSameOrDescendant(hoveredView, tooltipRoot))
                return null;

            object candidate = hoveredView;
            object parent = GetPropertyRecursive(candidate, "ParentWidget");
            while (parent != null &&
                !Object.ReferenceEquals(parent, screen) &&
                !Object.ReferenceEquals(parent, tooltipRoot) &&
                !IsSameOrDescendant(parent, itemWidget))
            {
                float width;
                float height;
                if (!TryGetWidgetSize(parent, out width, out height) ||
                    width <= 0.0f || height <= 0.0f ||
                    width > MaxBridgeWidth || height > MaxBridgeHeight)
                    break;

                candidate = parent;
                parent = GetPropertyRecursive(candidate, "ParentWidget");
            }

            float candidateWidth;
            float candidateHeight;
            if (!TryGetWidgetSize(candidate, out candidateWidth,
                out candidateHeight) || candidateWidth <= 0.0f ||
                candidateHeight <= 0.0f ||
                candidateWidth > MaxBridgeWidth ||
                candidateHeight > MaxBridgeHeight)
                return null;

            object itemRegion = GetPropertyRecursive(itemWidget,
                "ParentWidget") ?? itemWidget;
            if (!AreWidgetsAdjacent(itemRegion, candidate))
                return null;

            return candidate;
        }

        private static bool AreWidgetsAdjacent(object first, object second)
        {
            float ax;
            float ay;
            float aw;
            float ah;
            float bx;
            float by;
            float bw;
            float bh;
            if (!TryGetWidgetRect(first, out ax, out ay, out aw, out ah) ||
                !TryGetWidgetRect(second, out bx, out by, out bw, out bh))
                return false;

            float horizontalGap = Math.Max(0.0f,
                Math.Max(ax - (bx + bw), bx - (ax + aw)));
            float verticalGap = Math.Max(0.0f,
                Math.Max(ay - (by + bh), by - (ay + ah)));
            return horizontalGap <= MaxHorizontalGap &&
                verticalGap <= MaxVerticalGap;
        }

        private static bool IsInsideTooltip(object hoveredView,
            object eventManager, object tooltipRoot, object targetTooltip)
        {
            if (IsSameOrDescendant(hoveredView, targetTooltip) ||
                IsSameOrDescendant(hoveredView, tooltipRoot))
                return true;

            object mouse = GetPropertyRecursive(eventManager,
                "MousePositionInReferenceResolution");
            float mouseX;
            float mouseY;
            if (!TryGetVector(mouse, out mouseX, out mouseY))
                return false;

            return IsPointInsideWidget(mouseX, mouseY, targetTooltip,
                TooltipMargin) ||
                IsPointInsideWidget(mouseX, mouseY, tooltipRoot,
                    TooltipMargin);
        }

        private static bool IsPointInsideWidget(float x, float y,
            object widget, float margin)
        {
            float left;
            float top;
            float width;
            float height;
            if (!TryGetWidgetRect(widget, out left, out top, out width,
                out height) || width <= 0.0f || height <= 0.0f)
                return false;

            return x >= left - margin && x <= left + width + margin &&
                y >= top - margin && y <= top + height + margin;
        }

        private static bool TryGetWidgetRect(object widget, out float x,
            out float y, out float width, out float height)
        {
            x = 0.0f;
            y = 0.0f;
            width = 0.0f;
            height = 0.0f;
            if (widget == null)
                return false;

            object position = GetPropertyRecursive(widget, "GlobalPosition");
            if (!TryGetVector(position, out x, out y))
                return false;
            return TryGetWidgetSize(widget, out width, out height);
        }

        private static bool TryGetWidgetSize(object widget, out float width,
            out float height)
        {
            width = 0.0f;
            height = 0.0f;
            if (widget == null)
                return false;

            object size = GetPropertyRecursive(widget, "Size") ??
                GetPropertyRecursive(widget, "MeasuredSize");
            return TryGetVector(size, out width, out height);
        }

        private static bool TryGetVector(object vector, out float x,
            out float y)
        {
            x = 0.0f;
            y = 0.0f;
            if (vector == null)
                return false;

            object xValue = GetPropertyOrField(vector, "X");
            object yValue = GetPropertyOrField(vector, "Y");
            if (xValue == null || yValue == null)
                return false;
            try
            {
                x = Convert.ToSingle(xValue);
                y = Convert.ToSingle(yValue);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsSameOrDescendant(object widget,
            object ancestor)
        {
            if (widget == null || ancestor == null)
                return false;

            object current = widget;
            int guard = 0;
            while (current != null && guard++ < 128)
            {
                if (Object.ReferenceEquals(current, ancestor))
                    return true;
                current = GetPropertyRecursive(current, "ParentWidget");
            }
            return false;
        }

        private static object FindChildById(object root, string id)
        {
            if (root == null || String.IsNullOrEmpty(id))
                return null;
            string ownId = Convert.ToString(GetPropertyRecursive(root, "Id"));
            if (String.Equals(ownId, id, StringComparison.Ordinal))
                return root;

            MethodInfo[] methods = root.GetType().GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (method.Name != "FindChild")
                    continue;
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 1 &&
                    parameters[0].ParameterType == typeof(string))
                {
                    try
                    {
                        object found = method.Invoke(root,
                            new object[] { id });
                        if (found != null)
                            return found;
                    }
                    catch
                    {
                    }
                }
            }
            return null;
        }

        private static string DescribeWidget(object widget)
        {
            if (widget == null)
                return "<none>";
            string type = widget.GetType().Name;
            string id = Convert.ToString(GetPropertyRecursive(widget, "Id"));
            float width;
            float height;
            if (!TryGetWidgetSize(widget, out width, out height))
                return type + (String.IsNullOrEmpty(id) ? "" : "#" + id);
            return type + (String.IsNullOrEmpty(id) ? "" : "#" + id) +
                "[" + Math.Round(width) + "x" + Math.Round(height) + "]";
        }

        private static void ResetBridgeState()
        {
            _retainedScreen = null;
            _retainedItemWidget = null;
            _capturedBridgeRoot = null;
            _enteredTooltip = false;
        }

        private static object GetCurrentHoveredItemWidget(object screen)
        {
            if (screen == null)
                return null;

            Type screenType = screen.GetType();
            if (screenType != _hoveredFieldOwnerType)
            {
                _hoveredFieldOwnerType = screenType;
                _hoveredItemField = null;
                Type cursor = screenType;
                while (cursor != null && _hoveredItemField == null)
                {
                    _hoveredItemField = cursor.GetField(
                        "_currentHoveredItemWidget",
                        BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.Instance | BindingFlags.DeclaredOnly);
                    cursor = cursor.BaseType;
                }
            }
            return _hoveredItemField == null ? null :
                _hoveredItemField.GetValue(screen);
        }

        private static bool AreAllPatchesInstalled()
        {
            return _widgetPatchInstalled &&
                _magicBackgroundPatchInstalled &&
                _itemImagePatchInstalled;
        }

        private static void EnsureAssemblyLoadSubscription()
        {
            if (_assemblyLoadSubscribed || AreAllPatchesInstalled())
                return;

            AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoaded;
            _assemblyLoadSubscribed = true;
        }

        private static void RemoveAssemblyLoadSubscription()
        {
            if (!_assemblyLoadSubscribed)
                return;

            AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoaded;
            _assemblyLoadSubscribed = false;
        }

        private static void OnAssemblyLoaded(object sender, AssemblyLoadEventArgs args)
        {
            if (AreAllPatchesInstalled())
            {
                RemoveAssemblyLoadSubscription();
                return;
            }

            Assembly assembly = args == null ? null : args.LoadedAssembly;
            string name = assembly == null ? String.Empty :
                assembly.GetName().Name ?? String.Empty;
            if (!String.Equals(name, "0Harmony", StringComparison.Ordinal) &&
                !String.Equals(name,
                    "TaleWorlds.MountAndBlade.GauntletUI.Widgets",
                    StringComparison.Ordinal) &&
                !String.Equals(name, "TOR_Core", StringComparison.Ordinal) &&
                !String.Equals(name,
                    "TaleWorlds.MountAndBlade.GauntletUI",
                    StringComparison.Ordinal) &&
                !String.Equals(name, "TORCareerUniques",
                    StringComparison.Ordinal))
                return;

            TryInstallWidgetPatch();
        }

        private static void TryInstallWidgetPatch()
        {
            if (AreAllPatchesInstalled() || _installing)
                return;

            _installing = true;
            try
            {
                Type harmonyType = FindType("HarmonyLib.Harmony", "0Harmony");
                Type harmonyMethodType = FindType("HarmonyLib.HarmonyMethod",
                    "0Harmony");
                if (harmonyType == null || harmonyMethodType == null)
                    return;

                if (_harmony == null)
                    _harmony = Activator.CreateInstance(harmonyType,
                        new object[] { HarmonyId });

                if (!_widgetPatchInstalled)
                {
                    Type screenType = FindType(
                        "TaleWorlds.MountAndBlade.GauntletUI.Widgets.Inventory.InventoryScreenWidget",
                        "TaleWorlds.MountAndBlade.GauntletUI.Widgets");
                    MethodInfo original = FindMethod(screenType,
                        "ItemWidgetHoverEnd", 1);
                    MethodInfo prefix = typeof(UIIconPassThrough).GetMethod(
                        "BeforeInventoryScreenItemHoverEnd",
                        BindingFlags.Public | BindingFlags.Static);
                    if (original != null && prefix != null)
                    {
                        ApplyPatch(harmonyType, harmonyMethodType, original,
                            prefix);
                        _widgetPatchInstalled = true;
                        if (!_loggedInstalled)
                        {
                            _loggedInstalled = true;
                            LogInfo("Installed bounded InventoryScreenWidget " +
                                "armor-tooltip bridge with native close " +
                                "restoration.");
                        }
                    }
                }

                if (!_magicBackgroundPatchInstalled)
                {
                    Type torTupleType = FindType(
                        "TOR_Core.Items.TorInventoryItemTupleWidget",
                        "TOR_Core");
                    Type nativeTupleType = FindType(
                        "TaleWorlds.MountAndBlade.GauntletUI.Widgets.Inventory.InventoryItemTupleWidget",
                        "TaleWorlds.MountAndBlade.GauntletUI.Widgets");
                    MethodInfo original = FindMethod(torTupleType,
                        "OnRender", 2);
                    _updateEquipmentTypeState = FindMethod(nativeTupleType,
                        "UpdateEquipmentTypeState", 0);
                    MethodInfo prefix = typeof(UIIconPassThrough).GetMethod(
                        "BeforeTorInventoryItemTupleRender",
                        BindingFlags.Public | BindingFlags.Static);
                    if (original != null &&
                        _updateEquipmentTypeState != null && prefix != null)
                    {
                        ApplyPatch(harmonyType, harmonyMethodType, original,
                            prefix);
                        _magicBackgroundPatchInstalled = true;
                        if (!_loggedMagicBackgroundInstalled)
                        {
                            _loggedMagicBackgroundInstalled = true;
                            LogInfo("Installed TOR inventory tuple brush " +
                                "reconciliation. Recycled non-magic rows now " +
                                "restore Bannerlord's current native brush before " +
                                "TOR evaluates the magic background.");
                        }
                    }
                }

                if (!_itemImagePatchInstalled)
                {
                    Type providerType = FindType(
                        "TaleWorlds.MountAndBlade.GauntletUI.TextureProviders.ImageIdentifiers.ItemImageTextureProvider",
                        "TaleWorlds.MountAndBlade.GauntletUI");
                    MethodInfo original = FindMethod(providerType,
                        "OnCreateImageWithId", 2);
                    MethodInfo prefix = typeof(UIIconPassThrough).GetMethod(
                        "BeforeItemImageTextureCreation",
                        BindingFlags.Public | BindingFlags.Static);
                    Type fixType = FindType(
                        "TORCareerUniques.TorMagicItemLifecycleFix",
                        "TORCareerUniques");
                    _recoverRuntimeMagicItem = fixType == null ? null :
                        fixType.GetMethod(
                            "RecoverReferencedRuntimeMagicItem",
                            BindingFlags.Public | BindingFlags.NonPublic |
                            BindingFlags.Static, null,
                            new[] { typeof(string) }, null);
                    if (original != null && prefix != null &&
                        _recoverRuntimeMagicItem != null)
                    {
                        ApplyPatch(harmonyType, harmonyMethodType, original,
                            prefix);
                        _itemImagePatchInstalled = true;
                        if (!_loggedItemImageInstalled)
                        {
                            _loggedItemImageInstalled = true;
                            LogInfo("Installed TOR runtime magic-item thumbnail " +
                                "index repair before Bannerlord item-image cache " +
                                "miss resolution.");
                        }
                    }
                }

                if (AreAllPatchesInstalled())
                    RemoveAssemblyLoadSubscription();
            }
            catch (Exception ex)
            {
                if (!_loggedInstallFailure)
                {
                    _loggedInstallFailure = true;
                    LogError("Inventory widget patch installation failed: " +
                        FormatException(ex));
                }
            }
            finally
            {
                _installing = false;
            }
        }

        private static bool TryGetProtectedArmorIndex(object widget,
            out int equipmentIndex)
        {
            equipmentIndex = -1;
            if (widget == null)
                return false;

            string typeName = widget.GetType().FullName ??
                widget.GetType().Name ?? String.Empty;
            if (typeName.IndexOf("InventoryEquippedItemSlotWidget",
                StringComparison.Ordinal) < 0)
                return false;

            object value = GetPropertyRecursive(widget,
                "TargetEquipmentIndex");
            if (value == null)
                return false;
            try
            {
                equipmentIndex = Convert.ToInt32(value);
            }
            catch
            {
                equipmentIndex = -1;
                return false;
            }

            return equipmentIndex == 5 || equipmentIndex == 6 ||
                equipmentIndex == 8 || equipmentIndex == 9;
        }

        private static string GetSlotProperty(int equipmentIndex)
        {
            switch (equipmentIndex)
            {
                case 5: return "CharacterHelmSlot";
                case 6: return "CharacterTorsoSlot";
                case 8: return "CharacterGloveSlot";
                case 9: return "CharacterBootSlot";
                default: return null;
            }
        }

        private static string GetSlotName(int equipmentIndex)
        {
            switch (equipmentIndex)
            {
                case 5: return "helmet";
                case 6: return "body armor";
                case 8: return "gloves";
                case 9: return "boots";
                default: return "armor";
            }
        }

        private static bool IsEmptyItemViewModel(object itemViewModel)
        {
            string id = Convert.ToString(GetPropertyRecursive(itemViewModel,
                "StringId"));
            return String.IsNullOrEmpty(id);
        }

        private static object GetActiveInventoryViewModel()
        {
            Type spItemVmType = FindType(
                "TaleWorlds.CampaignSystem.ViewModelCollection.Inventory.SPItemVM",
                "TaleWorlds.CampaignSystem.ViewModelCollection");
            Type inventoryType = FindType(
                "TaleWorlds.CampaignSystem.ViewModelCollection.Inventory.SPInventoryVM",
                "TaleWorlds.CampaignSystem.ViewModelCollection");
            if (spItemVmType == null || inventoryType == null)
                return null;

            FieldInfo onFocus = spItemVmType.GetField("OnFocus",
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Static);
            Delegate callback = onFocus == null
                ? null
                : onFocus.GetValue(null) as Delegate;
            object target = callback == null ? null : callback.Target;
            return target != null && inventoryType.IsInstanceOfType(target)
                ? target
                : null;
        }

        private static MethodInfo FindMethod(Type type, string name,
            int parameterCount)
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

        private static void ApplyPatch(Type harmonyType,
            Type harmonyMethodType, MethodInfo original, MethodInfo prefixMethod)
        {
            object prefix = CreateHarmonyMethod(harmonyMethodType,
                prefixMethod);
            MethodInfo[] methods = harmonyType.GetMethods(
                BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo candidate = methods[i];
                if (candidate.Name != "Patch")
                    continue;

                ParameterInfo[] parameters = candidate.GetParameters();
                if (parameters.Length < 2 ||
                    !typeof(MethodBase).IsAssignableFrom(
                        parameters[0].ParameterType))
                    continue;

                object[] args = new object[parameters.Length];
                args[0] = original;
                bool usable = true;
                for (int p = 1; p < parameters.Length; p++)
                {
                    string parameterName = parameters[p].Name ?? String.Empty;
                    if (String.Equals(parameterName, "prefix",
                        StringComparison.OrdinalIgnoreCase))
                        args[p] = prefix;
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
                "Patch(MethodBase, HarmonyMethod, ...)");
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
            if (property != null && property.CanWrite)
            {
                property.SetValue(result, patchMethod, null);
                return result;
            }

            throw new MissingMemberException(harmonyMethodType.FullName,
                "method");
        }

        private static object GetPropertyOrField(object instance, string name)
        {
            object value = GetPropertyRecursive(instance, name);
            return value ?? GetFieldRecursive(instance, name);
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

        private static Type FindType(string fullName, string assemblyName)
        {
            Type result = Type.GetType(fullName + ", " + assemblyName,
                false);
            if (result != null)
                return result;
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                result = assemblies[i].GetType(fullName, false);
                if (result != null)
                    return result;
            }
            return null;
        }

        private static string FormatException(Exception exception)
        {
            TargetInvocationException invocation = exception as
                TargetInvocationException;
            if (invocation != null && invocation.InnerException != null)
                exception = invocation.InnerException;
            return exception.GetType().Name + ": " + exception.Message;
        }

        private static void LogInfo(string message)
        {
            InvokeLog(ref _logInfo, "Info", message);
        }

        private static void LogVerbose(string message)
        {
            InvokeLog(ref _logVerbose, "Verbose", message);
        }

        private static void LogError(string message)
        {
            InvokeLog(ref _logError, "Error", message);
        }

        private static void InvokeLog(ref MethodInfo cache, string method,
            string message)
        {
            try
            {
                if (cache == null)
                {
                    Type log = FindType("TORCareerUniques.ModLog",
                        "TORCareerUniques");
                    if (log != null)
                    {
                        cache = log.GetMethod(method,
                            BindingFlags.Public | BindingFlags.NonPublic |
                            BindingFlags.Static, null,
                            new[] { typeof(string) }, null);
                    }
                }
                if (cache != null)
                    cache.Invoke(null, new object[] { message });
            }
            catch
            {
            }
        }
    }
}
