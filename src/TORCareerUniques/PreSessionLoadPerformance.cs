using System;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.MountAndBlade;

namespace TORCareerUniques
{
    public sealed class PreSessionLoadPerformanceSubModule : MBSubModuleBase
    {
        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            PreSessionLoadPerformance.Initialize();
        }
    }

    internal static class PreSessionLoadPerformance
    {
        private const string HarmonyId =
            "torcareeruniques.pre-session-load-performance.1.7.37";

        private static Harmony _harmony;
        private static bool _installed;
        private static bool _loadActive;
        private static bool _sessionBoundaryReported;
        private static int _earlyTraitAttempts;
        private static bool _earlyTraitsReady;
        private static long _moduleLoadStarted;
        private static long _artisanSyncTicks;
        private static int _artisanSyncCalls;
        private static long _craftedItemTicks;
        private static int _craftedItemCalls;
        private static int _torcuCraftedItemCalls;
        private static long _banditCacheTicks;
        private static int _banditCacheCalls;
        private static int _dedicatedNavalBypasses;
        private static long _sessionCallbackStarted;
        private static MethodInfo _navalGetter;
        private static MethodInfo _careerTraitInjector;
        private static MethodInfo _setTraitInjector;

        private struct CraftedCallState
        {
            internal long Started;
            internal bool IsTorcu;
        }

        internal static void Initialize()
        {
            if (_installed)
                return;

            _installed = true;
            _loadActive = true;
            _moduleLoadStarted = Stopwatch.GetTimestamp();

            try
            {
                _harmony = new Harmony(HarmonyId);

                PatchOptional(
                    FindMethodByNameAndParameterCount(
                        AccessTools.TypeByName(
                            "TOR_Core.CampaignMechanics.Crafting.TORArtisanDistrictCampaignBehavior"),
                        "SyncData", 1),
                    nameof(BeforeArtisanSyncData),
                    nameof(AfterArtisanSyncData));

                PatchOptional(
                    FindMethodByNameAndParameterCount(
                        AccessTools.TypeByName(
                            "TOR_Core.Items.ExtendedItemObjectManager"),
                        "AddCraftedItem", 3),
                    nameof(BeforeAddCraftedItem),
                    nameof(AfterAddCraftedItem));

                PatchOptional(
                    FindMethodByNameAndParameterCount(
                        AccessTools.TypeByName(
                            "TaleWorlds.CampaignSystem.CampaignBehaviors.BanditSpawnCampaignBehavior"),
                        "CacheBanditCounts", 0),
                    nameof(BeforeBanditCache),
                    nameof(AfterBanditCache));

                _navalGetter = AccessTools.PropertyGetter(
                    typeof(Clan), "HasNavalNavigationCapability");
                PatchOptional(_navalGetter,
                    nameof(BeforeHasNavalNavigationCapability), null);

                MethodInfo sessionLaunch = AccessTools.Method(
                    typeof(UniqueEncounterBehavior), "OnSessionLaunched",
                    new[]
                    {
                        typeof(TaleWorlds.CampaignSystem.CampaignGameStarter)
                    });
                PatchOptional(sessionLaunch,
                    nameof(BeforeSessionLaunched),
                    nameof(AfterSessionLaunched));

                ModLog.AlwaysInfo(
                    "Installed pre-session load guard and phase profiler. " +
                    "TORCU traits will be registered before TOR restores crafted set items " +
                    "when the registry is available.");
            }
            catch (Exception ex)
            {
                ModLog.Error("Pre-session load guard installation failed: " +
                    ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void PatchOptional(MethodInfo original,
            string prefixName, string postfixName)
        {
            if (original == null)
                return;

            HarmonyMethod prefix = null;
            HarmonyMethod postfix = null;
            if (!String.IsNullOrEmpty(prefixName))
            {
                MethodInfo method = AccessTools.Method(
                    typeof(PreSessionLoadPerformance), prefixName);
                if (method != null)
                    prefix = new HarmonyMethod(method)
                    {
                        priority = Priority.First
                    };
            }
            if (!String.IsNullOrEmpty(postfixName))
            {
                MethodInfo method = AccessTools.Method(
                    typeof(PreSessionLoadPerformance), postfixName);
                if (method != null)
                    postfix = new HarmonyMethod(method)
                    {
                        priority = Priority.Last
                    };
            }

            _harmony.Patch(original, prefix: prefix, postfix: postfix);
        }

        private static MethodInfo FindMethodByNameAndParameterCount(
            Type type, string name, int parameterCount)
        {
            if (type == null)
                return null;

            MethodInfo[] methods = type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Static | BindingFlags.Instance);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (method.Name == name &&
                    method.GetParameters().Length == parameterCount)
                    return method;
            }
            return null;
        }

        private static void BeforeArtisanSyncData(ref long __state)
        {
            __state = 0;
            if (!_loadActive)
                return;

            EnsureEarlyTraits();
            __state = Stopwatch.GetTimestamp();
            _artisanSyncCalls++;
        }

        private static void AfterArtisanSyncData(long __state)
        {
            if (__state != 0)
                _artisanSyncTicks += Stopwatch.GetTimestamp() - __state;
        }

        private static void BeforeAddCraftedItem(object[] __args,
            ref CraftedCallState __state)
        {
            __state = default(CraftedCallState);
            if (!_loadActive)
                return;

            EnsureEarlyTraits();
            __state.Started = Stopwatch.GetTimestamp();
            __state.IsTorcu = ContainsTorcuTrait(__args);
            _craftedItemCalls++;
            if (__state.IsTorcu)
                _torcuCraftedItemCalls++;
        }

        private static void AfterAddCraftedItem(CraftedCallState __state)
        {
            if (__state.Started != 0)
                _craftedItemTicks += Stopwatch.GetTimestamp() -
                    __state.Started;
        }

        private static bool ContainsTorcuTrait(object[] arguments)
        {
            if (arguments == null || arguments.Length < 3)
                return false;

            IEnumerable traits = arguments[2] as IEnumerable;
            if (traits == null)
                return false;

            foreach (object value in traits)
            {
                string id = Convert.ToString(value);
                if (!String.IsNullOrEmpty(id) &&
                    id.StartsWith("torcu_", StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static void BeforeBanditCache(ref long __state)
        {
            __state = 0;
            if (!_loadActive)
                return;

            __state = Stopwatch.GetTimestamp();
            _banditCacheCalls++;
        }

        private static void AfterBanditCache(long __state)
        {
            if (__state != 0)
                _banditCacheTicks += Stopwatch.GetTimestamp() - __state;
        }

        private static bool BeforeHasNavalNavigationCapability(
            Clan __instance, ref bool __result)
        {
            if (!_loadActive || !IsDedicatedEncounterClan(__instance))
                return true;

            // Runtime-created TORCU clans intentionally have no naval role. Some
            // Bannerlord/TOR load paths query this getter before checking whether the
            // clan is a native bandit faction. Returning the correct non-naval value
            // prevents those paths from dereferencing an absent default party template.
            _dedicatedNavalBypasses++;
            __result = false;
            return false;
        }

        private static bool IsDedicatedEncounterClan(Clan clan)
        {
            return clan != null &&
                !String.IsNullOrEmpty(clan.StringId) &&
                clan.StringId.StartsWith("torcu_faction_",
                    StringComparison.Ordinal);
        }

        private static void EnsureEarlyTraits()
        {
            if (_earlyTraitsReady || _earlyTraitAttempts >= 4)
                return;

            _earlyTraitAttempts++;
            try
            {
                if (_careerTraitInjector == null)
                    _careerTraitInjector = AccessTools.Method(
                        typeof(CareerUniqueRuntime), "EnsureTraitsInjected");
                if (_setTraitInjector == null)
                    _setTraitInjector = AccessTools.Method(
                        typeof(SetItemRuntime), "EnsureTraitsInjected");

                bool relics = _careerTraitInjector != null &&
                    Convert.ToBoolean(_careerTraitInjector.Invoke(null, null));
                bool sets = _setTraitInjector != null &&
                    Convert.ToBoolean(_setTraitInjector.Invoke(null, null));
                _earlyTraitsReady = relics && sets;
                if (_earlyTraitsReady)
                {
                    ModLog.AlwaysInfo(
                        "Registered TORCU relic and set traits before TOR crafted-item restoration.");
                }
            }
            catch (Exception ex)
            {
                if (_earlyTraitAttempts >= 4)
                {
                    ModLog.Error(
                        "Early TORCU trait registration remained unavailable; " +
                        "normal session initialization will retry it. " +
                        ex.GetType().Name + ": " + ex.Message);
                }
            }
        }

        private static void BeforeSessionLaunched(
            UniqueEncounterBehavior __instance)
        {
            if (_sessionBoundaryReported)
                return;

            _sessionBoundaryReported = true;
            _sessionCallbackStarted = Stopwatch.GetTimestamp();

            ModLog.AlwaysInfo(
                "Reached TORCU session callback " +
                FormatMilliseconds(ElapsedTicks(_moduleLoadStarted)) +
                " ms after module load. Pre-session phases: TOR artisan SyncData=" +
                FormatMilliseconds(_artisanSyncTicks) + " ms/" +
                _artisanSyncCalls + " call(s), AddCraftedItem=" +
                FormatMilliseconds(_craftedItemTicks) + " ms/" +
                _craftedItemCalls + " call(s), TORCU crafted items=" +
                _torcuCraftedItemCalls + ", bandit cache=" +
                FormatMilliseconds(_banditCacheTicks) + " ms/" +
                _banditCacheCalls + " call(s), dedicated naval-template bypasses=" +
                _dedicatedNavalBypasses + ", early traits ready=" +
                (_earlyTraitsReady ? "yes" : "no") + ".");
        }

        private static void AfterSessionLaunched(
            UniqueEncounterBehavior __instance)
        {
            long started = _sessionCallbackStarted;
            _sessionCallbackStarted = 0;
            _loadActive = false;

            if (started != 0)
            {
                ModLog.AlwaysInfo(
                    "TORCU session callback completed in " +
                    FormatMilliseconds(ElapsedTicks(started)) +
                    " ms. The temporary pre-session naval guard is now inactive.");
            }

            // Remove the getter prefix after load so there is no Harmony overhead on
            // campaign-map naval capability queries. The existing narrow bandit
            // classifier guard remains installed by CompatibilityFixes.
            try
            {
                if (_harmony != null && _navalGetter != null)
                    _harmony.Unpatch(_navalGetter,
                        HarmonyPatchType.Prefix, HarmonyId);
            }
            catch (Exception ex)
            {
                ModLog.Error("Could not remove the temporary naval load guard: " +
                    ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static long ElapsedTicks(long started)
        {
            return started == 0 ? 0 :
                Stopwatch.GetTimestamp() - started;
        }

        private static string FormatMilliseconds(long ticks)
        {
            double milliseconds = ticks * 1000.0 /
                Stopwatch.Frequency;
            return milliseconds.ToString("0",
                System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
