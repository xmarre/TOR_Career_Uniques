using System;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.MountAndBlade;

namespace TORCareerUniques
{
    public sealed class DeferredEncounterInitializationSubModule : MBSubModuleBase
    {
        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            UniqueEncounterBehavior.InitializeDeferredEncounterInitialization();
        }
    }

    internal sealed partial class UniqueEncounterBehavior
    {
        private const string DeferredInitializationHarmonyId =
            "torcareeruniques.deferred-encounter-initialization.1.7.36";

        private static bool _deferredInitializationInstalled;
        private static Type _deferredScreenManagerType;
        private static PropertyInfo _deferredTopScreenProperty;
        private long _deferredSessionLoadStart;
        private bool _deferredLoadAnnouncementWritten;

        private struct DeferredInitializationTiming
        {
            internal long Started;
            internal int CursorBefore;
        }

        internal static void InitializeDeferredEncounterInitialization()
        {
            if (_deferredInitializationInstalled)
                return;

            try
            {
                Harmony harmony = new Harmony(DeferredInitializationHarmonyId);

                MethodInfo sessionLaunch = AccessTools.Method(
                    typeof(UniqueEncounterBehavior), "OnSessionLaunched",
                    new[] { typeof(TaleWorlds.CampaignSystem.CampaignGameStarter) });
                MethodInfo beforeSession = AccessTools.Method(
                    typeof(UniqueEncounterBehavior),
                    nameof(BeforeTimedSessionLoad));
                MethodInfo afterSession = AccessTools.Method(
                    typeof(UniqueEncounterBehavior),
                    nameof(AfterTimedSessionLoad));
                if (sessionLaunch == null || beforeSession == null ||
                    afterSession == null)
                {
                    throw new MissingMethodException(
                        "Session-load timing target was not found.");
                }
                harmony.Patch(sessionLaunch,
                    prefix: new HarmonyMethod(beforeSession)
                    {
                        priority = Priority.First
                    },
                    postfix: new HarmonyMethod(afterSession)
                    {
                        priority = Priority.Last
                    });

                MethodInfo synchronous = AccessTools.Method(
                    typeof(UniqueEncounterBehavior),
                    "CompleteInitializationWithoutPolling");
                MethodInfo defer = AccessTools.Method(
                    typeof(UniqueEncounterBehavior),
                    nameof(SkipSynchronousAllEncounterInitialization));
                if (synchronous == null || defer == null)
                {
                    throw new MissingMethodException(
                        "Synchronous encounter-initialization target was not found.");
                }
                harmony.Patch(synchronous,
                    prefix: new HarmonyMethod(defer)
                    {
                        priority = Priority.First
                    });

                MethodInfo incremental = AccessTools.Method(
                    typeof(UniqueEncounterBehavior),
                    "ProcessIncrementalInitialization",
                    new[] { typeof(float) });
                MethodInfo beforeIncremental = AccessTools.Method(
                    typeof(UniqueEncounterBehavior),
                    nameof(BeforeDeferredInitializationStep));
                MethodInfo afterIncremental = AccessTools.Method(
                    typeof(UniqueEncounterBehavior),
                    nameof(AfterDeferredInitializationStep));
                if (incremental == null || beforeIncremental == null ||
                    afterIncremental == null)
                {
                    throw new MissingMethodException(
                        "Incremental encounter-initialization target was not found.");
                }
                harmony.Patch(incremental,
                    prefix: new HarmonyMethod(beforeIncremental)
                    {
                        priority = Priority.First
                    },
                    postfix: new HarmonyMethod(afterIncremental)
                    {
                        priority = Priority.Last
                    });

                _deferredInitializationInstalled = true;
                ModLog.Info("Installed campaign-map-gated encounter initialization.");
            }
            catch (Exception ex)
            {
                ModLog.Error("Deferred encounter initialization could not be installed; " +
                    "the existing synchronous safety path remains active. " +
                    ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void BeforeTimedSessionLoad(
            UniqueEncounterBehavior __instance)
        {
            if (__instance == null)
                return;
            __instance._deferredSessionLoadStart = Stopwatch.GetTimestamp();
            __instance._deferredLoadAnnouncementWritten = false;
        }

        private static void AfterTimedSessionLoad(
            UniqueEncounterBehavior __instance)
        {
            if (__instance == null || __instance._deferredSessionLoadStart == 0)
                return;

            double elapsed = ElapsedMilliseconds(
                __instance._deferredSessionLoadStart);
            __instance._deferredSessionLoadStart = 0;
            ModLog.Info("TORCU synchronous session-load work completed in " +
                elapsed.ToString("0", System.Globalization.CultureInfo.InvariantCulture) +
                " ms. Encounter party/site maintenance remains queued until the " +
                "campaign map is active.");
        }

        private static bool SkipSynchronousAllEncounterInitialization(
            UniqueEncounterBehavior __instance)
        {
            if (__instance != null &&
                !__instance._deferredLoadAnnouncementWritten)
            {
                __instance._deferredLoadAnnouncementWritten = true;
                ModLog.Info("Deferred all-22-encounter maintenance out of the " +
                    "save-load critical path. Persistent encounter-hero repair and " +
                    "equipment validation remain synchronous.");
            }
            return false;
        }

        private static bool BeforeDeferredInitializationStep(
            UniqueEncounterBehavior __instance,
            ref DeferredInitializationTiming __state)
        {
            __state = default(DeferredInitializationTiming);
            if (__instance == null || !IsCampaignMapScreenActiveForInitialization())
                return false;

            __state.Started = Stopwatch.GetTimestamp();
            __state.CursorBefore = __instance._initializationCursor;
            return true;
        }

        private static void AfterDeferredInitializationStep(
            UniqueEncounterBehavior __instance,
            DeferredInitializationTiming __state)
        {
            if (__instance == null || __state.Started == 0)
                return;

            double elapsed = ElapsedMilliseconds(__state.Started);
            if (elapsed < 100.0)
                return;

            string target = "troop catalogue/setup";
            int index = __state.CursorBefore;
            if (index >= 0 && index < EncounterCatalog.All.Length)
            {
                EncounterDefinition definition = EncounterCatalog.All[index];
                if (definition != null &&
                    !String.IsNullOrEmpty(definition.MapName))
                {
                    target = definition.MapName;
                }
            }

            ModLog.Info("Deferred encounter initialization for " + target +
                " took " + elapsed.ToString("0",
                    System.Globalization.CultureInfo.InvariantCulture) + " ms.");
        }

        private static bool IsCampaignMapScreenActiveForInitialization()
        {
            try
            {
                if (_deferredScreenManagerType == null)
                {
                    Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
                    for (int i = 0; i < assemblies.Length; i++)
                    {
                        _deferredScreenManagerType = assemblies[i].GetType(
                            "TaleWorlds.ScreenSystem.ScreenManager", false);
                        if (_deferredScreenManagerType != null)
                            break;
                    }
                }
                if (_deferredScreenManagerType == null)
                    return false;

                if (_deferredTopScreenProperty == null)
                {
                    _deferredTopScreenProperty =
                        _deferredScreenManagerType.GetProperty("TopScreen",
                            BindingFlags.Static | BindingFlags.Public |
                            BindingFlags.NonPublic);
                }
                object topScreen = _deferredTopScreenProperty == null ? null :
                    _deferredTopScreenProperty.GetValue(null, null);
                string name = topScreen == null ? String.Empty :
                    topScreen.GetType().FullName ?? String.Empty;
                return String.Equals(name, "SandBox.View.Map.MapScreen",
                    StringComparison.Ordinal) ||
                    name.EndsWith(".MapScreen", StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        private static double ElapsedMilliseconds(long started)
        {
            return (Stopwatch.GetTimestamp() - started) * 1000.0 /
                Stopwatch.Frequency;
        }
    }
}
