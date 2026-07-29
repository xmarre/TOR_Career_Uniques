using System;
using System.Reflection;

namespace TORCareerUniques
{
    internal static partial class SetItemRuntime
    {
        private const string CompanionMutationGuardHarmonyId =
            "torcareeruniques.sets.companion-mutation-guard";

        private static readonly bool CompanionMutationGuardInstalled =
            TryInstallCompanionMutationGuard();

        private static bool TryInstallCompanionMutationGuard()
        {
            try
            {
                Type harmonyType = FindCrossCultureHarmonyType(
                    "HarmonyLib.Harmony", "0Harmony");
                Type harmonyMethodType = FindCrossCultureHarmonyType(
                    "HarmonyLib.HarmonyMethod", "0Harmony");
                if (harmonyType == null || harmonyMethodType == null)
                    throw new TypeLoadException(
                        "HarmonyLib is unavailable while installing the companion mutation guard.");

                MethodInfo original = typeof(SetItemRuntime).GetMethod(
                    "BeforeRuntimeEquipmentMutation",
                    BindingFlags.NonPublic | BindingFlags.Static,
                    null, new[] { typeof(object) }, null);
                MethodInfo prefix = typeof(SetItemRuntime).GetMethod(
                    nameof(GuardCompanionMutationInvalidation),
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (original == null || prefix == null)
                    throw new MissingMethodException(typeof(SetItemRuntime).FullName,
                        "BeforeRuntimeEquipmentMutation guard target");

                object harmony = Activator.CreateInstance(harmonyType,
                    new object[] { CompanionMutationGuardHarmonyId });
                PatchCompanionSetMethod(harmonyType, harmonyMethodType, harmony,
                    original, prefix, null);
                return true;
            }
            catch (Exception ex)
            {
                ModLog.Error("Companion equipment mutation guard could not be installed. " +
                    ex.GetType().Name + ": " + ex.Message);
                return false;
            }
        }

        // The underlying Equipment method is used throughout Bannerlord while parties,
        // troops, encounters, and mission agents are being initialized. Only player-clan
        // battle equipment changed from the active inventory screen can affect this cache.
        // Rejecting every other call prevents unrelated equipment churn from growing the
        // dirty set or scheduling a later clan-wide refresh.
        private static bool GuardCompanionMutationInvalidation(object __0)
        {
            try
            {
                if (_internalCompanionCarrierMutation || __0 == null ||
                    !IsCompanionInventoryStateActive())
                    return false;
                return FindPlayerHeroByBattleEquipment(__0) != null;
            }
            catch (Exception ex)
            {
                LogOnce("companion-mutation-guard:" + ex.GetType().FullName + ":" +
                    ex.Message,
                    "Companion equipment mutation guard failed; the mutation will be " +
                    "reconciled on the next bounded inventory/session refresh. " +
                    FormatException(ex));
                return false;
            }
        }
    }
}
