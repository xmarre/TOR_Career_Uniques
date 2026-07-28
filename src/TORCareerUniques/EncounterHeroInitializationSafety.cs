using System;
using System.Collections.Generic;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;

namespace TORCareerUniques
{
    internal sealed partial class UniqueEncounterBehavior
    {
        private const string EncounterHeroInitializationSafetyHarmonyId =
            "torcareeruniques.encounterheroes.initialization-safety";

        private static readonly bool EncounterHeroInitializationSafetyInstalled =
            InstallEncounterHeroInitializationSafety();

        // An explicit type constructor removes beforefieldinit semantics. The rollback
        // patch is therefore installed before the first behavior instance can execute
        // GetOrCreateEncounterHero instead of depending on an otherwise unreferenced
        // static-field initializer being scheduled eagerly by the runtime.
        static UniqueEncounterBehavior()
        {
            if (!EncounterHeroInitializationSafetyInstalled)
                throw new InvalidOperationException(
                    "Encounter-hero initialization safety was not installed.");
        }

        private sealed class EncounterHeroCreationState
        {
            internal string CareerId;
            internal Hero EncounterHeroBefore;
            internal Hero SuccessorHeroBefore;
        }

        private static bool InstallEncounterHeroInitializationSafety()
        {
            Type harmonyType = FindInitializationSafetyType(
                "HarmonyLib.Harmony", "0Harmony");
            Type harmonyMethodType = FindInitializationSafetyType(
                "HarmonyLib.HarmonyMethod", "0Harmony");
            if (harmonyType == null || harmonyMethodType == null)
                throw new TypeLoadException(
                    "HarmonyLib is unavailable while installing encounter-hero initialization safety.");

            MethodInfo original = typeof(UniqueEncounterBehavior).GetMethod(
                "GetOrCreateEncounterHero",
                BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                new[] { typeof(EncounterDefinition), typeof(Settlement), typeof(Clan) },
                null);
            MethodInfo prefix = typeof(UniqueEncounterBehavior).GetMethod(
                "CaptureEncounterHeroCreationState",
                BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo finalizer = typeof(UniqueEncounterBehavior).GetMethod(
                "RollbackFailedEncounterHeroCreation",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (original == null || prefix == null || finalizer == null)
                throw new MissingMethodException(
                    typeof(UniqueEncounterBehavior).FullName,
                    "GetOrCreateEncounterHero initialization-safety patch target");

            object harmony = Activator.CreateInstance(harmonyType,
                new object[] { EncounterHeroInitializationSafetyHarmonyId });
            ApplyInitializationSafetyPatch(harmonyType, harmonyMethodType,
                harmony, original, prefix, finalizer);
            return true;
        }

        private static void CaptureEncounterHeroCreationState(
            UniqueEncounterBehavior __instance, EncounterDefinition __0,
            out EncounterHeroCreationState __state)
        {
            __state = new EncounterHeroCreationState();
            if (__instance == null || __0 == null ||
                String.IsNullOrEmpty(__0.CareerId))
                return;

            __state.CareerId = __0.CareerId;
            Hero existing;
            if (__instance._encounterHeroes != null &&
                __instance._encounterHeroes.TryGetValue(__0.CareerId, out existing))
                __state.EncounterHeroBefore = existing;
            if (__instance._successorHeroes != null &&
                __instance._successorHeroes.TryGetValue(__0.CareerId, out existing))
                __state.SuccessorHeroBefore = existing;
        }

        private static Exception RollbackFailedEncounterHeroCreation(
            UniqueEncounterBehavior __instance, EncounterHeroCreationState __state,
            Exception __exception)
        {
            if (__exception == null || __instance == null || __state == null ||
                String.IsNullOrEmpty(__state.CareerId))
                return __exception;

            try
            {
                List<Hero> failedHeroes = new List<Hero>();
                RestoreHeroMapAfterFailedCreation(__instance._encounterHeroes,
                    __state.CareerId, __state.EncounterHeroBefore, failedHeroes);
                RestoreHeroMapAfterFailedCreation(__instance._successorHeroes,
                    __state.CareerId, __state.SuccessorHeroBefore, failedHeroes);
                if (__instance._pendingHeroRecoveries != null)
                    __instance._pendingHeroRecoveries.Remove(__state.CareerId);

                for (int i = 0; i < failedHeroes.Count; i++)
                    RemoveFailedEncounterHero(__state.CareerId, failedHeroes[i]);

                if (failedHeroes.Count > 0)
                    ModLog.Error("Rolled back " + failedHeroes.Count +
                        " partially initialized encounter hero(s) for " +
                        __state.CareerId + " after " +
                        __exception.GetType().Name + ": " + __exception.Message);
            }
            catch (Exception rollbackException)
            {
                ModLog.Error("Encounter-hero initialization rollback failed for " +
                    __state.CareerId + ": " + rollbackException.GetType().Name +
                    ": " + rollbackException.Message);
            }
            return __exception;
        }

        private static void RestoreHeroMapAfterFailedCreation(
            Dictionary<string, Hero> heroMap, string careerId, Hero previousHero,
            List<Hero> failedHeroes)
        {
            if (heroMap == null)
                return;

            Hero currentHero;
            if (!heroMap.TryGetValue(careerId, out currentHero) ||
                currentHero == null || Object.ReferenceEquals(currentHero, previousHero))
                return;

            if (previousHero == null)
                heroMap.Remove(careerId);
            else
                heroMap[careerId] = previousHero;

            for (int i = 0; i < failedHeroes.Count; i++)
                if (Object.ReferenceEquals(failedHeroes[i], currentHero))
                    return;
            failedHeroes.Add(currentHero);
        }

        private static void RemoveFailedEncounterHero(string careerId, Hero hero)
        {
            if (hero == null)
                return;

            EncounterHeroDeathGuard.Unregister(hero);
            Exception removalFailure = null;
            try
            {
                KillCharacterAction.ApplyByRemove(hero, false, true);
            }
            catch (Exception ex)
            {
                removalFailure = ex;
            }

            if (!hero.IsDead)
            {
                try
                {
                    DisableHeroAction.Apply(hero);
                }
                catch (Exception disableException)
                {
                    string removalText = removalFailure == null ? "none" :
                        removalFailure.GetType().Name + ": " + removalFailure.Message;
                    throw new InvalidOperationException(
                        "Native removal and disable both failed for " + careerId +
                        " / " + hero.StringId + ". Remove failure: " + removalText +
                        "; disable failure: " + disableException.GetType().Name +
                        ": " + disableException.Message, disableException);
                }
            }
        }

        private static Type FindInitializationSafetyType(
            string fullName, string assemblyName)
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

        private static void ApplyInitializationSafetyPatch(
            Type harmonyType, Type harmonyMethodType, object harmony,
            MethodInfo original, MethodInfo prefixMethod,
            MethodInfo finalizerMethod)
        {
            object prefix = CreateInitializationSafetyHarmonyMethod(
                harmonyMethodType, prefixMethod);
            object finalizer = CreateInitializationSafetyHarmonyMethod(
                harmonyMethodType, finalizerMethod);
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
                    if (String.Equals(name, "prefix",
                        StringComparison.OrdinalIgnoreCase))
                        args[p] = prefix;
                    else if (String.Equals(name, "finalizer",
                        StringComparison.OrdinalIgnoreCase))
                        args[p] = finalizer;
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
                candidate.Invoke(harmony, args);
                return;
            }

            throw new MissingMethodException(harmonyType.FullName,
                "Patch(MethodBase, HarmonyMethod, ..., HarmonyMethod finalizer)");
        }

        private static object CreateInitializationSafetyHarmonyMethod(
            Type harmonyMethodType, MethodInfo patchMethod)
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

            throw new MissingMemberException(harmonyMethodType.FullName, "method");
        }
    }
}
