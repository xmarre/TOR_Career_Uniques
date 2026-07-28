using System;
using System.Collections;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.InputSystem;

namespace TORCareerUniques
{
    // Ctrl+E belongs to the campaign map. Harmony routes input from that screen's
    // own frame callback; this class adds no global application polling.
    internal static class McmHotkeyBridge
    {
        private const string SettingsId = "TORCareerUniques_v1_1";
        private const string HarmonyId = "torcareeruniques.mcm.map-hotkey";
        private const string MapScreenName =
            "SandBox.View.Map.MapScreen";
        private static object _openedScreen;
        private static int _selectionFrames;
        private static int _selectionAttempts;
        private static object _harmony;
        private static bool _mapPatchInstalled;
        private static bool _assemblyLoadSubscribed;
        private static bool _loggedInstalled;
        private static bool _loggedInstallFailure;
        private static Type _screenManagerType;
        private static PropertyInfo _topScreenProperty;

        internal static void Initialize()
        {
            TryInstallPatches();
            if (_mapPatchInstalled)
                return;
            if (!_assemblyLoadSubscribed)
            {
                AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
                _assemblyLoadSubscribed = true;
            }
        }

        private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
        {
            TryInstallPatches();
            if (_mapPatchInstalled && _assemblyLoadSubscribed)
            {
                AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
                _assemblyLoadSubscribed = false;
            }
        }

        // Harmony postfix: executes only while the campaign map screen ticks.
        public static void AfterMapScreenFrame()
        {
            if (_openedScreen != null || Campaign.Current == null ||
                Input.IsOnScreenKeyboardActive)
                return;

            bool controlDown = Input.IsKeyDown(InputKey.LeftControl) ||
                Input.IsKeyDown(InputKey.RightControl);
            if (!controlDown || !Input.IsKeyPressed(InputKey.E))
                return;

            if (IsMcmScreenAlreadyOpen())
                return;

            OpenTorSettings();
        }

        // Called by the mod's existing application tick. This is dormant (one null
        // comparison) unless Ctrl+E itself opened MCM and a short page-selection
        // handshake is pending. No Harmony hook is installed on the MCM options screen.
        internal static void Tick()
        {
            if (_openedScreen != null)
                TrySelectTorSettingsPage();
        }

        private static void TryInstallPatches()
        {
            try
            {
                Type harmonyType = FindLoadedType("HarmonyLib.Harmony");
                Type harmonyMethodType = FindLoadedType("HarmonyLib.HarmonyMethod");
                if (harmonyType == null || harmonyMethodType == null)
                    return;
                if (_harmony == null)
                    _harmony = Activator.CreateInstance(harmonyType,
                        new object[] { HarmonyId });

                if (!_mapPatchInstalled)
                    _mapPatchInstalled = TryPatchFrame(harmonyType,
                        harmonyMethodType, MapScreenName,
                        "AfterMapScreenFrame");
                if (_mapPatchInstalled && !_loggedInstalled)
                {
                    _loggedInstalled = true;
                    ModLog.Info("Installed campaign-map Ctrl+E hook; MCM page selection " +
                        "uses a bounded requested handshake with no MCM frame patch.");
                }
            }
            catch (Exception ex)
            {
                if (!_loggedInstallFailure)
                {
                    _loggedInstallFailure = true;
                    ModLog.Error("Could not install event-driven Ctrl+E MCM " +
                        "hooks: " + FormatException(ex));
                }
            }
        }

        private static bool TryPatchFrame(Type harmonyType,
            Type harmonyMethodType, string typeName, string postfixName)
        {
            Type screenType = FindLoadedType(typeName);
            if (screenType == null)
                return false;
            MethodInfo original = screenType.GetMethod("OnFrameTick",
                BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic, null, new[] { typeof(float) }, null);
            MethodInfo postfix = typeof(McmHotkeyBridge).GetMethod(postfixName,
                BindingFlags.Static | BindingFlags.Public);
            if (original == null || original.DeclaringType != screenType ||
                postfix == null)
                throw new MissingMethodException(typeName,
                    "declared OnFrameTick(float)");

            object harmonyMethod = CreateHarmonyMethod(harmonyMethodType,
                postfix);
            MethodInfo[] methods = harmonyType.GetMethods(
                BindingFlags.Instance | BindingFlags.Public);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo candidate = methods[i];
                if (candidate.Name != "Patch") continue;
                ParameterInfo[] parameters = candidate.GetParameters();
                if (parameters.Length < 3 ||
                    !typeof(MethodBase).IsAssignableFrom(
                        parameters[0].ParameterType))
                    continue;
                object[] args = new object[parameters.Length];
                args[0] = original;
                for (int p = 1; p < parameters.Length; p++)
                {
                    string name = parameters[p].Name ?? String.Empty;
                    if (String.Equals(name, "postfix",
                        StringComparison.OrdinalIgnoreCase))
                        args[p] = harmonyMethod;
                    else if (parameters[p].HasDefaultValue)
                        args[p] = parameters[p].DefaultValue;
                    else
                        args[p] = null;
                }
                candidate.Invoke(_harmony, args);
                return true;
            }
            throw new MissingMethodException(harmonyType.FullName,
                "Patch(MethodBase, ...)");
        }

        private static object CreateHarmonyMethod(Type harmonyMethodType,
            MethodInfo method)
        {
            ConstructorInfo constructor = harmonyMethodType.GetConstructor(
                new[] { typeof(MethodInfo) });
            if (constructor != null)
                return constructor.Invoke(new object[] { method });
            object result = Activator.CreateInstance(harmonyMethodType);
            FieldInfo field = harmonyMethodType.GetField("method",
                BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic);
            if (field != null)
            {
                field.SetValue(result, method);
                return result;
            }
            throw new MissingMemberException(harmonyMethodType.FullName,
                "method");
        }

        private static void OpenTorSettings()
        {
            try
            {
                Type serviceType = FindLoadedType(
                    "BUTR.DependencyInjection.GenericServiceProvider");
                Type screenInterface = FindLoadedType(
                    "MCM.UI.IMCMOptionsScreen");
                Type screenManager = FindLoadedType(
                    "TaleWorlds.ScreenSystem.ScreenManager");
                if (serviceType == null || screenInterface == null ||
                    screenManager == null)
                    throw new InvalidOperationException(
                        "MCM UI services are not available.");

                MethodInfo getService = null;
                MethodInfo[] serviceMethods = serviceType.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Static);
                for (int i = 0; i < serviceMethods.Length; i++)
                {
                    MethodInfo candidate = serviceMethods[i];
                    if (candidate.Name == "GetService" &&
                        candidate.IsGenericMethodDefinition &&
                        candidate.GetGenericArguments().Length == 1 &&
                        candidate.GetParameters().Length == 0)
                    {
                        getService = candidate;
                        break;
                    }
                }
                if (getService == null)
                    throw new MissingMethodException(serviceType.FullName,
                        "GetService<T>()");

                object screen = getService.MakeGenericMethod(screenInterface)
                    .Invoke(null, null);
                if (screen == null)
                    throw new InvalidOperationException(
                        "MCM did not create its Mod Options screen.");

                MethodInfo pushScreen = null;
                MethodInfo[] screenMethods = screenManager.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Static);
                for (int i = 0; i < screenMethods.Length; i++)
                {
                    MethodInfo candidate = screenMethods[i];
                    ParameterInfo[] parameters = candidate.GetParameters();
                    if (candidate.Name == "PushScreen" &&
                        parameters.Length == 1 &&
                        parameters[0].ParameterType.IsAssignableFrom(
                            screen.GetType()))
                    {
                        pushScreen = candidate;
                        break;
                    }
                }
                if (pushScreen == null)
                    throw new MissingMethodException(screenManager.FullName,
                        "PushScreen(ScreenBase)");

                pushScreen.Invoke(null, new[] { screen });
                _openedScreen = screen;
                _selectionFrames = 0;
                _selectionAttempts = 0;
                ModLog.Info("Ctrl+E opened MCM Mod Options; selecting TOR " +
                    "Career Uniques settings page.");
            }
            catch (Exception ex)
            {
                ModLog.Error("Ctrl+E could not open TOR Career Uniques MCM: " +
                    FormatException(ex));
            }
        }

        private static void TrySelectTorSettingsPage()
        {
            if (_openedScreen == null)
                return;
            if (++_selectionFrames < 6)
                return;
            _selectionFrames = 0;
            if (++_selectionAttempts > 120)
            {
                ModLog.Error("MCM opened from Ctrl+E, but the TOR Career " +
                    "Uniques page did not become available within 12 seconds.");
                _openedScreen = null;
                return;
            }

            try
            {
                FieldInfo dataSourceField = _openedScreen.GetType().GetField(
                    "_dataSource", BindingFlags.Instance |
                    BindingFlags.NonPublic);
                object dataSource = dataSourceField == null ? null :
                    dataSourceField.GetValue(_openedScreen);
                if (dataSource == null)
                    return;

                PropertyInfo listProperty = dataSource.GetType().GetProperty(
                    "ModSettingsList", BindingFlags.Instance |
                    BindingFlags.Public | BindingFlags.NonPublic);
                IEnumerable entries = listProperty == null ? null :
                    listProperty.GetValue(dataSource, null) as IEnumerable;
                if (entries == null)
                    return;

                foreach (object entry in entries)
                {
                    if (entry == null)
                        continue;
                    PropertyInfo idProperty = entry.GetType().GetProperty("Id",
                        BindingFlags.Instance | BindingFlags.Public |
                        BindingFlags.NonPublic);
                    string id = idProperty == null ? null : Convert.ToString(
                        idProperty.GetValue(entry, null));
                    if (!String.Equals(id, SettingsId,
                        StringComparison.Ordinal))
                        continue;

                    MethodInfo select = dataSource.GetType().GetMethod(
                        "ExecuteSelect", BindingFlags.Instance |
                        BindingFlags.Public | BindingFlags.NonPublic,
                        null, new[] { entry.GetType() }, null);
                    if (select == null)
                        throw new MissingMethodException(
                            dataSource.GetType().FullName, "ExecuteSelect");
                    select.Invoke(dataSource, new[] { entry });
                    ModLog.Info("Ctrl+E selected TOR Career Uniques in MCM.");
                    _openedScreen = null;
                    return;
                }
            }
            catch (Exception ex)
            {
                ModLog.Error("MCM opened from Ctrl+E, but selecting TOR Career " +
                    "Uniques failed: " + FormatException(ex));
                _openedScreen = null;
            }
        }

        internal static bool IsOptionsScreenActive()
        {
            try
            {
                if (_screenManagerType == null)
                    _screenManagerType = FindLoadedType(
                        "TaleWorlds.ScreenSystem.ScreenManager");
                if (_screenManagerType == null)
                    return false;
                if (_topScreenProperty == null)
                    _topScreenProperty = _screenManagerType.GetProperty("TopScreen",
                        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                object current = _topScreenProperty == null ? null :
                    _topScreenProperty.GetValue(null, null);
                string name = current == null || current.GetType() == null ? String.Empty :
                    current.GetType().FullName ?? String.Empty;
                return name.IndexOf("Options", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
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

        private static bool IsMcmScreenAlreadyOpen()
        {
            try
            {
                Type screenManager = FindLoadedType(
                    "TaleWorlds.ScreenSystem.ScreenManager");
                PropertyInfo topScreen = screenManager == null ? null :
                    screenManager.GetProperty("TopScreen", BindingFlags.Static |
                        BindingFlags.Public | BindingFlags.NonPublic);
                object current = topScreen == null ? null :
                    topScreen.GetValue(null, null);
                return current != null && current.GetType().FullName != null &&
                    current.GetType().FullName.IndexOf("ModOptions",
                        StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }

        private static string FormatException(Exception exception)
        {
            Exception current = exception;
            while (current is TargetInvocationException &&
                current.InnerException != null)
                current = current.InnerException;
            return current.GetType().FullName + ": " + current.Message;
        }
    }
}
