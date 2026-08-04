using System;
using System.Collections.Generic;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;

namespace TORCareerUniques
{
    internal static class EncounterHeroDeathGuard
    {
        private const string HarmonyId = "torcareeruniques.encounterheroes.lifecycleguard";
        private static readonly object Gate = new object();
        private static readonly HashSet<Hero> ProtectedHeroes =
            new HashSet<Hero>(ReferenceComparer.Instance);
        private static readonly HashSet<Hero> ReportedCaptureBlocks =
            new HashSet<Hero>(ReferenceComparer.Instance);
        private static bool _initialized;
        private static bool _loggedHarmonyWait;
        private static object _harmony;

        internal static void Initialize()
        {
            lock (Gate)
            {
                if (_initialized)
                    return;

                Type harmonyType = FindType("HarmonyLib.Harmony", "0Harmony");
                Type harmonyMethodType = FindType("HarmonyLib.HarmonyMethod", "0Harmony");
                if (harmonyType == null || harmonyMethodType == null)
                {
                    if (!_loggedHarmonyWait)
                    {
                        _loggedHarmonyWait = true;
                        ModLog.Error("Encounter-hero lifecycle guard is waiting for HarmonyLib from 0Harmony.");
                    }
                    return;
                }

                MethodInfo killOriginal = FindKillInternal();
                MethodInfo killPrefix = typeof(EncounterHeroDeathGuard).GetMethod(
                    "PreventForcedDeath", BindingFlags.NonPublic | BindingFlags.Static);
                if (killOriginal == null || killPrefix == null)
                    throw new MissingMethodException(typeof(KillCharacterAction).FullName,
                        "ApplyInternal(Hero, Hero, KillCharacterActionDetail, bool, bool)");

                MethodInfo captureOriginal = FindTakePrisonerInternal();
                MethodInfo capturePrefix = typeof(EncounterHeroDeathGuard).GetMethod(
                    "PreventForcedCaptivity", BindingFlags.NonPublic | BindingFlags.Static);
                if (captureOriginal == null || capturePrefix == null)
                    throw new MissingMethodException(typeof(TakePrisonerAction).FullName,
                        "ApplyInternal(PartyBase, Hero, bool)");

                _harmony = Activator.CreateInstance(harmonyType,
                    new object[] { HarmonyId });
                ApplyPatch(harmonyType, harmonyMethodType, killOriginal, killPrefix);
                ApplyPatch(harmonyType, harmonyMethodType, captureOriginal,
                    capturePrefix);
                _initialized = true;
                ModLog.Info("Installed encounter-hero forced-death and final-action captivity guards.");
            }
        }

        internal static void Register(Hero hero)
        {
            if (!_initialized)
                Initialize();
            if (hero == null)
                return;
            lock (Gate)
                ProtectedHeroes.Add(hero);
        }

        internal static void Unregister(Hero hero)
        {
            if (hero == null)
                return;
            lock (Gate)
            {
                ProtectedHeroes.Remove(hero);
                ReportedCaptureBlocks.Remove(hero);
            }
        }

        internal static void ClearAndRegister(IEnumerable<Hero> heroes)
        {
            if (!_initialized)
                Initialize();

            List<Hero> snapshot = new List<Hero>();
            lock (Gate)
            {
                ProtectedHeroes.Clear();
                ReportedCaptureBlocks.Clear();
                if (heroes != null)
                {
                    foreach (Hero hero in heroes)
                    {
                        if (hero == null || !ProtectedHeroes.Add(hero))
                            continue;
                        snapshot.Add(hero);
                    }
                }
            }

            // Existing saves can already contain a captive encounter leader from
            // TakePrisonerAction paths that never consult CanHeroBecomePrisonerEvent.
            // Release only heroes registered by the owning encounter behavior. The
            // normal HeroPrisonerReleased listener then performs the established
            // recovery/cooldown transition and removes the stale prisoner-roster row.
            for (int i = 0; i < snapshot.Count; i++)
                ReleaseInvalidExistingCaptivity(snapshot[i]);
        }

        private static bool PreventForcedDeath(Hero __0)
        {
            Hero victim = __0;
            if (victim == null || !IsProtected(victim))
                return true;

            try
            {
                if (victim.HitPoints <= 0)
                    victim.HitPoints = 1;
                ModLog.Info("Blocked KillCharacterAction for persistent encounter hero " +
                    victim.Name + ". The hero remains unconscious/alive.");
            }
            catch (Exception ex)
            {
                ModLog.Error("Forced-death guard health repair failed for " +
                    victim.Name + ": " + ex.GetType().Name + ": " + ex.Message);
            }
            return false;
        }

        // TakePrisonerAction.ApplyInternal is the authoritative mutation boundary.
        // Bannerlord settlement capture and several direct action paths reach it
        // without raising CanHeroBecomePrisonerEvent, so the campaign-event veto is
        // insufficient on its own.
        private static bool PreventForcedCaptivity(PartyBase __0, Hero __1)
        {
            Hero prisoner = __1;
            if (prisoner == null || !IsProtected(prisoner))
                return true;

            bool shouldLog;
            lock (Gate)
                shouldLog = ReportedCaptureBlocks.Add(prisoner);
            if (shouldLog)
            {
                string captor = __0 == null || __0.Name == null
                    ? "an unknown party"
                    : __0.Name.ToString();
                ModLog.Info("Blocked TakePrisonerAction for persistent encounter hero " +
                    prisoner.Name + " by " + captor +
                    ". Encounter leaders use the normal defeat/recovery lifecycle.");
            }
            return false;
        }

        private static void ReleaseInvalidExistingCaptivity(Hero hero)
        {
            if (hero == null ||
                (!hero.IsPrisoner && hero.PartyBelongedToAsPrisoner == null))
                return;

            try
            {
                string captor = hero.PartyBelongedToAsPrisoner == null ||
                    hero.PartyBelongedToAsPrisoner.Name == null
                    ? "an unknown party"
                    : hero.PartyBelongedToAsPrisoner.Name.ToString();
                EndCaptivityAction.ApplyByEscape(hero, null, false);
                if (hero.IsPrisoner || hero.PartyBelongedToAsPrisoner != null)
                    throw new InvalidOperationException(
                        "EndCaptivityAction did not clear the prisoner state.");
                ModLog.Info("Released stale captive encounter hero " + hero.Name +
                    " from " + captor +
                    "; normal encounter recovery and respawn scheduling resumed.");
            }
            catch (Exception ex)
            {
                ModLog.Error("Could not release stale captive encounter hero " +
                    hero.Name + ": " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static bool IsProtected(Hero hero)
        {
            lock (Gate)
                return ProtectedHeroes.Contains(hero);
        }

        private static MethodInfo FindKillInternal()
        {
            MethodInfo[] methods = typeof(KillCharacterAction).GetMethods(
                BindingFlags.NonPublic | BindingFlags.Static);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                ParameterInfo[] parameters = method.GetParameters();
                if (method.Name != "ApplyInternal" || parameters.Length != 5)
                    continue;
                if (parameters[0].ParameterType == typeof(Hero) &&
                    parameters[1].ParameterType == typeof(Hero) &&
                    parameters[3].ParameterType == typeof(bool) &&
                    parameters[4].ParameterType == typeof(bool))
                    return method;
            }
            return null;
        }

        private static MethodInfo FindTakePrisonerInternal()
        {
            MethodInfo[] methods = typeof(TakePrisonerAction).GetMethods(
                BindingFlags.NonPublic | BindingFlags.Static);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                ParameterInfo[] parameters = method.GetParameters();
                if (method.Name != "ApplyInternal" || parameters.Length != 3)
                    continue;
                if (parameters[0].ParameterType == typeof(PartyBase) &&
                    parameters[1].ParameterType == typeof(Hero) &&
                    parameters[2].ParameterType == typeof(bool))
                    return method;
            }
            return null;
        }

        private static Type FindType(string fullName, string assemblyName)
        {
            Type result = Type.GetType(fullName + ", " + assemblyName, false);
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

        private static void ApplyPatch(Type harmonyType, Type harmonyMethodType,
            MethodInfo original, MethodInfo prefixMethod)
        {
            object prefix = CreateHarmonyMethod(harmonyMethodType, prefixMethod);
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
                    if (String.Equals(name, "prefix", StringComparison.OrdinalIgnoreCase))
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

        private sealed class ReferenceComparer : IEqualityComparer<Hero>
        {
            internal static readonly ReferenceComparer Instance = new ReferenceComparer();
            public bool Equals(Hero x, Hero y) { return Object.ReferenceEquals(x, y); }
            public int GetHashCode(Hero obj)
            {
                return obj == null ? 0 :
                    System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
            }
        }
    }
}
