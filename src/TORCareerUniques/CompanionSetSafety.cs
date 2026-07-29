using System;
using System.Collections.Generic;
using System.Reflection;

namespace TORCareerUniques
{
    internal static partial class SetItemRuntime
    {
        private const string CompanionMixedGearSafetyHarmonyId =
            "torcareeruniques.sets.companion-mixed-gear-safety";

        private static readonly bool CompanionMixedGearSafetyInstalled =
            TryInstallCompanionMixedGearSafety();

        private static bool TryInstallCompanionMixedGearSafety()
        {
            try
            {
                Type harmonyType = FindCrossCultureHarmonyType(
                    "HarmonyLib.Harmony", "0Harmony");
                Type harmonyMethodType = FindCrossCultureHarmonyType(
                    "HarmonyLib.HarmonyMethod", "0Harmony");
                if (harmonyType == null || harmonyMethodType == null)
                    throw new TypeLoadException(
                        "HarmonyLib is unavailable while installing companion mixed-gear safety.");

                MethodInfo original = FindCompanionSetMethod(
                    typeof(SetItemRuntime), "ScanHeroSetState", 2,
                    BindingFlags.NonPublic | BindingFlags.Static);
                MethodInfo postfix = typeof(SetItemRuntime).GetMethod(
                    nameof(RemovePersistentHeroCarriersFromCompanionState),
                    BindingFlags.NonPublic | BindingFlags.Static);
                object harmony = Activator.CreateInstance(harmonyType,
                    new object[] { CompanionMixedGearSafetyHarmonyId });
                PatchCompanionSetMethod(harmonyType, harmonyMethodType, harmony,
                    original, null, postfix);
                return true;
            }
            catch (Exception ex)
            {
                ModLog.Error("Companion mixed-gear safety could not be installed. " +
                    ex.GetType().Name + ": " + ex.Message);
                return false;
            }
        }

        private static void RemovePersistentHeroCarriersFromCompanionState(
            object __0, bool __1,
            Dictionary<string, EquippedSetState> __result)
        {
            if (__1 || __result == null || __result.Count == 0)
                return;

            foreach (EquippedSetState state in __result.Values)
            {
                for (int i = state.EquippedItems.Count - 1; i >= 0; i--)
                {
                    EquippedItemRef equipped = state.EquippedItems[i];
                    if (equipped == null || String.IsNullOrEmpty(equipped.ItemId))
                        continue;
                    if (HasHeroSignature(GetItemTraits(equipped.ItemId)))
                        state.EquippedItems.RemoveAt(i);
                }
            }
        }
    }
}
