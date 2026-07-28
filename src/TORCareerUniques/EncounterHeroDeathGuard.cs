using System;
using System.Collections.Generic;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;

namespace TORCareerUniques
{
    internal static class EncounterHeroDeathGuard
    {
        private const string HarmonyId = "torcareeruniques.encounterheroes.deathguard";
        private static readonly object Gate = new object();
        private static readonly HashSet<Hero> ProtectedHeroes =
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
                        ModLog.Error("Encounter-hero forced-death guard is waiting for HarmonyLib from 0Harmony.");
                    }
                    return;
                }

                MethodInfo original = FindKillInternal();
                MethodInfo prefix = typeof(EncounterHeroDeathGuard).GetMethod(
                    "PreventForcedDeath", BindingFlags.NonPublic | BindingFlags.Static);
                if (original == null || prefix == null)
                    throw new MissingMethodException(typeof(KillCharacterAction).FullName,
                        "ApplyInternal(Hero, Hero, KillCharacterActionDetail, bool, bool)");

                _harmony = Activator.CreateInstance(harmonyType,
                    new object[] { HarmonyId });
                ApplyPatch(harmonyType, harmonyMethodType, original, prefix);
                _initialized = true;
                ModLog.Info("Installed encounter-hero forced-death guard on KillCharacterAction.ApplyInternal.");
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
                ProtectedHeroes.Remove(hero);
        }

        internal static void ClearAndRegister(IEnumerable<Hero> heroes)
        {
            if (!_initialized)
                Initialize();
            lock (Gate)
            {
                ProtectedHeroes.Clear();
                if (heroes == null)
                    return;
                foreach (Hero hero in heroes)
                    if (hero != null)
                        ProtectedHeroes.Add(hero);
            }
        }

        private static bool PreventForcedDeath(Hero __0)
        {
            Hero victim = __0;
            if (victim == null)
                return true;

            bool protectedHero;
            lock (Gate)
                protectedHero = ProtectedHeroes.Contains(victim);
            if (!protectedHero)
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
                return obj == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
            }
        }
    }
}
