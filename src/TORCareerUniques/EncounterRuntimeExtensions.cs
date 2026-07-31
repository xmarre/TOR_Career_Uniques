using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.MountAndBlade;

namespace TORCareerUniques
{
    public sealed class EncounterRuntimeExtensionsSubModule : MBSubModuleBase
    {
        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            EncounterAffinityCommandGuard.Initialize();
            EncounterBaseStrengthRuntime.Initialize();
        }
    }

    internal static class EncounterAffinityCommandGuard
    {
        private const string HarmonyId =
            "torcareeruniques.encounters.affinity-command-guard.1.7.40";

        private static readonly Dictionary<string, string> CareerByPartySlug =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "grailknight", "GrailKnight" },
                { "warriorpriest", "WarriorPriest" },
                { "bloodknight", "BloodKnight" },
                { "mercenary", "Mercenary" },
                { "blackgrailknight", "BlackGrailKnight" },
                { "warriorpriestulric", "WarriorPriestUlric" },
                { "waywatcher", "Waywatcher" },
                { "warden", "Warden" },
                { "knightoldworld", "KnightOldWorld" },
                { "slayer", "Slayer" },
                { "orcboss", "OrcBoss" }
            };

        private static Dictionary<string, HashSet<IFaction>>
            _protectedFactionsByCareer;
        private static bool _installed;

        internal static void Initialize()
        {
            if (_installed)
                return;

            try
            {
                FieldInfo cacheField = AccessTools.Field(
                    typeof(EncounterAffinityRuntime),
                    "ProtectedFactionsByCareer");
                _protectedFactionsByCareer = cacheField == null ? null :
                    cacheField.GetValue(null) as
                        Dictionary<string, HashSet<IFaction>>;
                if (_protectedFactionsByCareer == null)
                {
                    throw new MissingFieldException(
                        typeof(EncounterAffinityRuntime).FullName,
                        "ProtectedFactionsByCareer");
                }

                MethodInfo prefix = AccessTools.Method(
                    typeof(EncounterAffinityCommandGuard),
                    nameof(BeforeSetMoveEngageParty));
                if (prefix == null)
                {
                    throw new MissingMethodException(
                        typeof(EncounterAffinityCommandGuard).FullName,
                        nameof(BeforeSetMoveEngageParty));
                }

                Harmony harmony = new Harmony(HarmonyId);
                MethodInfo[] methods = typeof(MobileParty).GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance);
                int patched = 0;
                for (int i = 0; i < methods.Length; i++)
                {
                    MethodInfo method = methods[i];
                    if (!String.Equals(method.Name, "SetMoveEngageParty",
                        StringComparison.Ordinal))
                        continue;
                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length == 0 ||
                        parameters[0].ParameterType != typeof(MobileParty))
                        continue;

                    harmony.Patch(method, prefix: new HarmonyMethod(prefix)
                    {
                        priority = Priority.First
                    });
                    patched++;
                }

                if (patched == 0)
                {
                    throw new MissingMethodException(
                        typeof(MobileParty).FullName,
                        "SetMoveEngageParty(MobileParty, ...)");
                }

                _installed = true;
                ModLog.AlwaysInfo(
                    "Installed direct engage-command affinity guard on " +
                    patched + " MobileParty overload(s). Lore-protected hosts " +
                    "remain party-specific while retaining the single shared " +
                    "serialized encounter clan.");
            }
            catch (Exception ex)
            {
                ModLog.Error(
                    "Direct engage-command affinity guard could not be " +
                    "installed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        public static bool BeforeSetMoveEngageParty(
            MobileParty __instance, object[] __args)
        {
            try
            {
                MobileParty target = FindTargetParty(__args);
                if (!IsLoreProtectedPair(__instance, target))
                    return true;

                if (__instance != null && __instance.Ai != null)
                    __instance.Ai.RethinkAtNextHourlyTick = true;
                ClearMatchingStaleTarget(__instance, target);
                return false;
            }
            catch
            {
                // Compatibility failures retain native behavior.
                return true;
            }
        }

        private static MobileParty FindTargetParty(object[] arguments)
        {
            if (arguments == null)
                return null;
            for (int i = 0; i < arguments.Length; i++)
            {
                MobileParty party = arguments[i] as MobileParty;
                if (party != null)
                    return party;
            }
            return null;
        }

        private static bool IsLoreProtectedPair(
            MobileParty first, MobileParty second)
        {
            if (first == null || second == null ||
                first == MobileParty.MainParty ||
                second == MobileParty.MainParty)
                return false;

            string firstCareer = TryGetHostCareer(first);
            string secondCareer = TryGetHostCareer(second);
            return (firstCareer != null &&
                    IsProtectedFaction(firstCareer, second.MapFaction)) ||
                (secondCareer != null &&
                    IsProtectedFaction(secondCareer, first.MapFaction));
        }

        private static bool IsProtectedFaction(
            string careerId, IFaction faction)
        {
            HashSet<IFaction> factions;
            return faction != null &&
                _protectedFactionsByCareer != null &&
                _protectedFactionsByCareer.TryGetValue(
                    careerId, out factions) &&
                factions.Contains(faction);
        }

        private static string TryGetHostCareer(MobileParty party)
        {
            string id = party == null ? null : party.StringId;
            if (String.IsNullOrEmpty(id) ||
                !id.StartsWith("torcu_enc_", StringComparison.Ordinal))
                return null;

            string remainder = id.Substring("torcu_enc_".Length);
            int separator = remainder.LastIndexOf('_');
            string slug = separator > 0 ?
                remainder.Substring(0, separator) : remainder;
            string careerId;
            return CareerByPartySlug.TryGetValue(slug, out careerId) ?
                careerId : null;
        }

        private static void ClearMatchingStaleTarget(
            MobileParty party, MobileParty target)
        {
            if (party == null || target == null)
                return;

            object current = ReflectionUtil.GetProperty(
                party, "TargetParty");
            if (!Object.ReferenceEquals(current, target) && party.Ai != null)
            {
                object targetPartyBase = ReflectionUtil.GetProperty(
                    party.Ai, "AiBehaviorPartyBase");
                if (targetPartyBase == null)
                    return;
                object currentMobileParty = ReflectionUtil.GetProperty(
                    targetPartyBase, "MobileParty");
                if (!Object.ReferenceEquals(currentMobileParty, target))
                    return;
            }

            party.SetMoveModeHold();
        }
    }

    internal static class EncounterBaseStrengthRuntime
    {
        private const string HarmonyId =
            "torcareeruniques.encounters.base-strength.1.7.40";
        private static bool _installed;

        internal static void Initialize()
        {
            if (_installed)
                return;

            try
            {
                MethodInfo original = AccessTools.Method(
                    typeof(UniqueEncounterBehavior),
                    "GetEncounterStrengthProfile",
                    new[] { typeof(EncounterDefinition), typeof(int) });
                MethodInfo postfix = AccessTools.Method(
                    typeof(EncounterBaseStrengthRuntime),
                    nameof(AfterGetEncounterStrengthProfile));
                if (original == null || postfix == null)
                {
                    throw new MissingMethodException(
                        "Encounter strength profile method was not found.");
                }

                Harmony harmony = new Harmony(HarmonyId);
                harmony.Patch(original, postfix: new HarmonyMethod(postfix)
                {
                    priority = Priority.Last
                });
                _installed = true;
                ModLog.AlwaysInfo(
                    "Installed configurable host and guardian-location base " +
                    "strength multipliers. Collection and veteran escalation " +
                    "remain multiplicative.");
            }
            catch (Exception ex)
            {
                ModLog.Error(
                    "Encounter base-strength runtime could not be installed: " +
                    ex.GetType().Name + ": " + ex.Message);
            }
        }

        public static void AfterGetEncounterStrengthProfile(
            EncounterDefinition __0, ref EncounterStrengthProfile __result)
        {
            if (__0 == null || __result == null)
                return;

            int percent = __0.Kind == EncounterKind.RoamingHost ?
                EncounterStrengthConfig.RoamingHostPercent :
                EncounterStrengthConfig.GuardianLocationPercent;
            if (percent == 100)
                return;

            float multiplier = percent / 100f;
            __result.TargetTroops = Math.Max(1,
                (int)Math.Round(__result.TargetTroops * multiplier));
            __result.TotalMultiplier *= multiplier;
        }
    }
}
