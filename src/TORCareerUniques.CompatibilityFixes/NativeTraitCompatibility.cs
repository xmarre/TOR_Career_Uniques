using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using TaleWorlds.MountAndBlade;

namespace TORCareerUniques.CompatibilityFixes
{
    /// <summary>
    /// Keeps a missing native TOR item trait scoped to the item/career that needs it.
    /// TORCU's trait-registration routines otherwise treat every native trait across all
    /// careers as one global readiness invariant, so one stale TOR data entry prevents
    /// every encounter and admin grant from initializing.
    /// </summary>
    public sealed class NativeTraitCompatibilitySubModule : MBSubModuleBase
    {
        private const string HarmonyId =
            "torcareeruniques.compatibilityfixes.native-trait-isolation.1";
        private static readonly object RegistrationLock = new object();
        private static readonly HashSet<string> LoggedMissingTraits =
            new HashSet<string>(StringComparer.Ordinal);
        private static bool _installed;

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            if (_installed)
                return;

            Harmony harmony = new Harmony(HarmonyId);
            PatchTraitRegistration(harmony, "TORCareerUniques.CareerUniqueRuntime");
            PatchTraitRegistration(harmony, "TORCareerUniques.SetItemRuntime");
            _installed = true;
        }

        private static void PatchTraitRegistration(Harmony harmony, string typeName)
        {
            Type runtimeType = AccessTools.TypeByName(typeName);
            if (runtimeType == null)
                throw new TypeLoadException(typeName + " was not found.");

            MethodInfo target = AccessTools.Method(
                runtimeType, "EnsureTraitsInjected", Type.EmptyTypes);
            if (target == null)
                throw new MissingMethodException(
                    runtimeType.FullName, "EnsureTraitsInjected()");

            harmony.Patch(
                target,
                prefix: new HarmonyMethod(
                    typeof(NativeTraitCompatibilitySubModule),
                    nameof(BeforeEnsureTraitsInjected)),
                postfix: new HarmonyMethod(
                    typeof(NativeTraitCompatibilitySubModule),
                    nameof(AfterEnsureTraitsInjected)),
                finalizer: new HarmonyMethod(
                    typeof(NativeTraitCompatibilitySubModule),
                    nameof(FinalizeEnsureTraitsInjected)));
        }

        private static void BeforeEnsureTraitsInjected(
            MethodBase __originalMethod,
            out TraitIsolationState __state)
        {
            __state = new TraitIsolationState();
            Monitor.Enter(RegistrationLock);
            __state.LockHeld = true;

            try
            {
                Type runtimeType = __originalMethod == null
                    ? null
                    : __originalMethod.DeclaringType;
                if (runtimeType == null)
                    return;

                HashSet<string> registryIds;
                string validationId;
                if (!TryReadLoadedTraitIds(out registryIds, out validationId))
                    return;

                if (String.Equals(
                    runtimeType.FullName,
                    "TORCareerUniques.CareerUniqueRuntime",
                    StringComparison.Ordinal))
                {
                    IsolateMissingCareerTraits(
                        runtimeType, registryIds, validationId, __state);
                }
                else if (String.Equals(
                    runtimeType.FullName,
                    "TORCareerUniques.SetItemRuntime",
                    StringComparison.Ordinal))
                {
                    IsolateMissingSetTraits(
                        runtimeType, registryIds, validationId, __state);
                }
            }
            catch (Exception ex)
            {
                Restore(__state);
                LogCompatibilityFailure(ex);
            }
        }

        private static void AfterEnsureTraitsInjected(TraitIsolationState __state)
        {
            Restore(__state);
        }

        private static Exception FinalizeEnsureTraitsInjected(
            Exception __exception,
            TraitIsolationState __state)
        {
            Restore(__state);
            return __exception;
        }

        private static void IsolateMissingCareerTraits(
            Type runtimeType,
            HashSet<string> registryIds,
            string validationId,
            TraitIsolationState state)
        {
            Array definitions = GetStaticArray(runtimeType, "Definitions");
            if (definitions == null || String.IsNullOrEmpty(validationId))
                return;

            for (int i = 0; i < definitions.Length; i++)
            {
                object definition = definitions.GetValue(i);
                Array traits = GetArrayMember(definition, "Traits");
                if (traits == null || traits.Length <= 3)
                    continue;

                string careerId = GetStringMember(definition, "CareerId");
                IsolateMissingNativeTrait(
                    traits.GetValue(3),
                    "career relic " + careerId,
                    registryIds,
                    validationId,
                    state);
            }
        }

        private static void IsolateMissingSetTraits(
            Type runtimeType,
            HashSet<string> registryIds,
            string validationId,
            TraitIsolationState state)
        {
            Array definitions = GetStaticArray(runtimeType, "Definitions");
            if (definitions == null || String.IsNullOrEmpty(validationId))
                return;

            for (int d = 0; d < definitions.Length; d++)
            {
                object definition = definitions.GetValue(d);
                string careerId = GetStringMember(definition, "CareerId");
                Array pieces = GetArrayMember(definition, "Pieces");
                if (pieces == null)
                    continue;

                for (int p = 0; p < pieces.Length; p++)
                {
                    Array effects = GetArrayMember(
                        pieces.GetValue(p), "Effects");
                    if (effects == null || effects.Length <= 1)
                        continue;

                    IsolateMissingNativeTrait(
                        effects.GetValue(1),
                        careerId + " set piece " + (p + 1),
                        registryIds,
                        validationId,
                        state);
                }
            }
        }

        private static void IsolateMissingNativeTrait(
            object spec,
            string context,
            HashSet<string> registryIds,
            string validationId,
            TraitIsolationState state)
        {
            if (spec == null)
                return;

            string configuredId = GetStringMember(spec, "Id");
            if (String.IsNullOrWhiteSpace(configuredId) ||
                registryIds.Contains(configuredId))
                return;

            // The original EnsureTraitsInjected methods only test membership for
            // native-trait slots, then immediately continue. Give that membership
            // check an existing registry id for the duration of this synchronous
            // call and restore the real configured id before any item can use it.
            // This permits TORCU-owned traits to finish registering without ever
            // attaching a substitute native effect to an item.
            TemporaryId temporary =
                new TemporaryId(spec, configuredId, validationId);
            if (!SetStringMember(spec, "Id", validationId))
                return;

            state.TemporaryIds.Add(temporary);
            LogMissingTraitOnce(configuredId, context);
        }

        private static bool TryReadLoadedTraitIds(
            out HashSet<string> ids,
            out string validationId)
        {
            ids = new HashSet<string>(StringComparer.Ordinal);
            validationId = null;

            Type managerType = AccessTools.TypeByName(
                "TOR_Core.Items.ItemTraitManager");
            if (managerType == null)
                return false;

            object manager = GetStaticMember(managerType, "Instance");
            if (manager == null)
                return false;

            MethodInfo getTraits = AccessTools.Method(
                managerType, "GetItemTraits", Type.EmptyTypes);
            IList traits = getTraits == null
                ? null
                : getTraits.Invoke(manager, null) as IList;
            if (traits == null || traits.Count == 0)
                return false;

            for (int i = 0; i < traits.Count; i++)
            {
                string id = GetStringMember(
                    traits[i], "ItemTraitStringId");
                if (String.IsNullOrWhiteSpace(id))
                    continue;

                ids.Add(id);
                if (validationId == null &&
                    !id.StartsWith("torcu_", StringComparison.Ordinal))
                    validationId = id;
            }

            return validationId != null;
        }

        private static void Restore(TraitIsolationState state)
        {
            if (state == null || state.Restored)
                return;

            state.Restored = true;
            try
            {
                for (int i = state.TemporaryIds.Count - 1; i >= 0; i--)
                {
                    TemporaryId entry = state.TemporaryIds[i];
                    string currentId = GetStringMember(entry.Spec, "Id");
                    if (String.Equals(
                        currentId,
                        entry.ValidationId,
                        StringComparison.Ordinal))
                    {
                        SetStringMember(
                            entry.Spec, "Id", entry.OriginalId);
                    }
                }
                state.TemporaryIds.Clear();
            }
            finally
            {
                if (state.LockHeld)
                {
                    state.LockHeld = false;
                    Monitor.Exit(RegistrationLock);
                }
            }
        }

        private static void LogCompatibilityFailure(Exception ex)
        {
            try
            {
                Type logType = AccessTools.TypeByName("TORCareerUniques.ModLog");
                MethodInfo logError = AccessTools.Method(
                    logType, "Error", new[] { typeof(string) });
                if (logError != null)
                {
                    logError.Invoke(null, new object[]
                    {
                        "Native-trait isolation guard failed; TORCU will use its " +
                        "original registry-readiness behavior for this call: " +
                        ex.GetType().FullName + ": " + ex.Message
                    });
                }
            }
            catch
            {
            }
        }

        private static void LogMissingTraitOnce(
            string traitId,
            string context)
        {
            lock (LoggedMissingTraits)
            {
                if (!LoggedMissingTraits.Add(traitId))
                    return;
            }

            string message =
                "Required native TOR trait is absent from the loaded registry: " +
                traitId + " (" + context + "). TORCU will keep this failure " +
                "local to items that require that trait instead of disabling all " +
                "career encounters. Current TOR/WiTM data should contain TORCU's " +
                "mapped native traits; verify that TOR_Core ModuleData matches the " +
                "installed TOR version.";

            try
            {
                Type logType = AccessTools.TypeByName("TORCareerUniques.ModLog");
                MethodInfo logError = AccessTools.Method(
                    logType, "Error", new[] { typeof(string) });
                if (logError != null)
                    logError.Invoke(null, new object[] { message });
            }
            catch
            {
                // Diagnostics must not affect registry initialization.
            }
        }

        private static Array GetStaticArray(Type type, string name)
        {
            FieldInfo field = AccessTools.Field(type, name);
            return field == null ? null : field.GetValue(null) as Array;
        }

        private static Array GetArrayMember(object instance, string name)
        {
            return GetMember(instance, name) as Array;
        }

        private static object GetStaticMember(Type type, string name)
        {
            if (type == null)
                return null;

            PropertyInfo property = AccessTools.Property(type, name);
            MethodInfo getter = property == null
                ? null
                : property.GetGetMethod(true);
            if (getter != null)
                return property.GetValue(null, null);

            FieldInfo field = AccessTools.Field(type, name);
            return field == null ? null : field.GetValue(null);
        }

        private static object GetMember(object instance, string name)
        {
            if (instance == null)
                return null;

            Type type = instance.GetType();
            PropertyInfo property = AccessTools.Property(type, name);
            MethodInfo getter = property == null
                ? null
                : property.GetGetMethod(true);
            if (getter != null)
                return property.GetValue(instance, null);

            FieldInfo field = AccessTools.Field(type, name);
            return field == null ? null : field.GetValue(instance);
        }

        private static string GetStringMember(object instance, string name)
        {
            object value = GetMember(instance, name);
            return value == null ? String.Empty : Convert.ToString(value);
        }

        private static bool SetStringMember(
            object instance,
            string name,
            string value)
        {
            if (instance == null)
                return false;

            Type type = instance.GetType();
            FieldInfo field = AccessTools.Field(type, name);
            if (field != null && field.FieldType == typeof(string))
            {
                field.SetValue(instance, value);
                return true;
            }

            PropertyInfo property = AccessTools.Property(type, name);
            MethodInfo setter = property == null
                ? null
                : property.GetSetMethod(true);
            if (setter != null && property.PropertyType == typeof(string))
            {
                setter.Invoke(instance, new object[] { value });
                return true;
            }

            return false;
        }

        private sealed class TraitIsolationState
        {
            private readonly List<TemporaryId> _temporaryIds =
                new List<TemporaryId>();
            private bool _lockHeld;
            private bool _restored;

            internal List<TemporaryId> TemporaryIds { get { return _temporaryIds; } }
            internal bool LockHeld
            {
                get { return _lockHeld; }
                set { _lockHeld = value; }
            }
            internal bool Restored
            {
                get { return _restored; }
                set { _restored = value; }
            }
        }

        private sealed class TemporaryId
        {
            internal TemporaryId(
                object spec,
                string originalId,
                string validationId)
            {
                Spec = spec;
                OriginalId = originalId;
                ValidationId = validationId;
            }

            internal object Spec { get; private set; }
            internal string OriginalId { get; private set; }
            internal string ValidationId { get; private set; }
        }
    }
}
