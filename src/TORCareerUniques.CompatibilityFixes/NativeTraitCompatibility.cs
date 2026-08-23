using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using TaleWorlds.MountAndBlade;

namespace TORCareerUniques.CompatibilityFixes
{
    /// <summary>
    /// Keeps TORCU's native-trait references compatible with TOR releases that retain
    /// a trait's display identity while changing its StringId. It also prevents one
    /// unresolved native trait from blocking registry initialization for every career.
    /// </summary>
    public sealed class NativeTraitCompatibilitySubModule : MBSubModuleBase
    {
        private const string HarmonyId =
            "torcareeruniques.compatibilityfixes.native-traits.1";
        private static readonly HashSet<string> Logged =
            new HashSet<string>(StringComparer.Ordinal);
        private static bool _installed;

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            if (_installed)
                return;

            Harmony harmony = new Harmony(HarmonyId);
            InstallEnsureTraitsPatch(harmony,
                "TORCareerUniques.CareerUniqueRuntime");
            InstallEnsureTraitsPatch(harmony,
                "TORCareerUniques.SetItemRuntime");
            _installed = true;
        }

        private static void InstallEnsureTraitsPatch(
            Harmony harmony,
            string runtimeTypeName)
        {
            Type runtimeType = AccessTools.TypeByName(runtimeTypeName);
            if (runtimeType == null)
                throw new TypeLoadException(runtimeTypeName + " was not found.");

            MethodInfo target = AccessTools.Method(
                runtimeType, "EnsureTraitsInjected", Type.EmptyTypes);
            if (target == null)
                throw new MissingMethodException(
                    runtimeType.FullName, "EnsureTraitsInjected()");

            MethodInfo prefix = AccessTools.Method(
                typeof(NativeTraitCompatibilitySubModule),
                nameof(BeforeEnsureTraitsInjected));
            MethodInfo postfix = AccessTools.Method(
                typeof(NativeTraitCompatibilitySubModule),
                nameof(AfterEnsureTraitsInjected));
            MethodInfo finalizer = AccessTools.Method(
                typeof(NativeTraitCompatibilitySubModule),
                nameof(FinalizeEnsureTraitsInjected));

            harmony.Patch(
                target,
                prefix: new HarmonyMethod(prefix),
                postfix: new HarmonyMethod(postfix),
                finalizer: new HarmonyMethod(finalizer));
        }

        private static void BeforeEnsureTraitsInjected(
            MethodBase __originalMethod,
            out NativeTraitPatchState __state)
        {
            __state = new NativeTraitPatchState();
            try
            {
                Type runtimeType = __originalMethod == null
                    ? null
                    : __originalMethod.DeclaringType;
                if (runtimeType == null)
                    return;

                IList registry;
                HashSet<string> ids;
                Dictionary<string, List<NativeTraitCandidate>> byName;
                string validationId;
                if (!TryReadNativeRegistry(
                    out registry, out ids, out byName, out validationId))
                    return;

                if (String.Equals(
                    runtimeType.FullName,
                    "TORCareerUniques.CareerUniqueRuntime",
                    StringComparison.Ordinal))
                {
                    PrepareCareerNativeTraits(
                        runtimeType, ids, byName, validationId, __state);
                }
                else if (String.Equals(
                    runtimeType.FullName,
                    "TORCareerUniques.SetItemRuntime",
                    StringComparison.Ordinal))
                {
                    PrepareSetNativeTraits(
                        runtimeType, ids, byName, validationId, __state);
                }
            }
            catch (Exception ex)
            {
                RestoreTemporaryIds(__state);
                LogOnce(
                    "native-trait-compat-prefix:" +
                    (ex.GetType().FullName ?? ex.GetType().Name) + ":" +
                    ex.Message,
                    "Native TOR trait compatibility preparation failed: " +
                    FormatException(ex),
                    true);
            }
        }

        private static void AfterEnsureTraitsInjected(
            NativeTraitPatchState __state)
        {
            RestoreTemporaryIds(__state);
        }

        private static Exception FinalizeEnsureTraitsInjected(
            Exception __exception,
            NativeTraitPatchState __state)
        {
            RestoreTemporaryIds(__state);
            return __exception;
        }

        private static void PrepareCareerNativeTraits(
            Type runtimeType,
            HashSet<string> ids,
            Dictionary<string, List<NativeTraitCandidate>> byName,
            string validationId,
            NativeTraitPatchState state)
        {
            Array definitions = GetStaticArray(runtimeType, "Definitions");
            if (definitions == null)
                return;

            for (int d = 0; d < definitions.Length; d++)
            {
                object definition = definitions.GetValue(d);
                if (definition == null)
                    continue;

                Array traits = GetArrayMember(definition, "Traits");
                if (traits == null || traits.Length <= 3)
                    continue;

                string careerId = GetStringMember(definition, "CareerId");
                PrepareNativeSpec(
                    traits.GetValue(3),
                    "career relic " + careerId,
                    ids,
                    byName,
                    validationId,
                    state);
            }
        }

        private static void PrepareSetNativeTraits(
            Type runtimeType,
            HashSet<string> ids,
            Dictionary<string, List<NativeTraitCandidate>> byName,
            string validationId,
            NativeTraitPatchState state)
        {
            Array definitions = GetStaticArray(runtimeType, "Definitions");
            if (definitions == null)
                return;

            for (int d = 0; d < definitions.Length; d++)
            {
                object definition = definitions.GetValue(d);
                if (definition == null)
                    continue;

                string careerId = GetStringMember(definition, "CareerId");
                Array pieces = GetArrayMember(definition, "Pieces");
                if (pieces == null)
                    continue;

                for (int p = 0; p < pieces.Length; p++)
                {
                    object piece = pieces.GetValue(p);
                    Array effects = GetArrayMember(piece, "Effects");
                    if (effects == null || effects.Length <= 1)
                        continue;

                    PrepareNativeSpec(
                        effects.GetValue(1),
                        careerId + " set piece " + (p + 1),
                        ids,
                        byName,
                        validationId,
                        state);
                }
            }
        }

        private static void PrepareNativeSpec(
            object spec,
            string context,
            HashSet<string> ids,
            Dictionary<string, List<NativeTraitCandidate>> byName,
            string validationId,
            NativeTraitPatchState state)
        {
            if (spec == null)
                return;

            string configuredId = GetStringMember(spec, "Id");
            if (String.IsNullOrWhiteSpace(configuredId) ||
                ids.Contains(configuredId))
                return;

            string name = GetStringMember(spec, "Name");
            string description = GetStringMember(spec, "Description");
            string resolvedId = ResolveCurrentNativeId(
                name, description, byName);

            if (!String.IsNullOrEmpty(resolvedId))
            {
                if (!SetStringMember(spec, "Id", resolvedId))
                    return;

                ids.Add(resolvedId);
                LogOnce(
                    "native-trait-rebind:" + configuredId + "->" + resolvedId,
                    "Rebound stale native TOR trait id '" + configuredId +
                    "' to '" + resolvedId + "' by loaded trait identity (" +
                    context + ", " + name + ").",
                    false);
                return;
            }

            // EnsureTraitsInjected currently treats every native reference across all
            // 22 careers as one global readiness invariant. If TOR genuinely removed a
            // trait, that makes one unrelated item disable every encounter and admin
            // grant. Substitute an existing registry id only while the readiness scan
            // runs, then restore the unresolved id immediately. The placeholder is
            // never inserted into TOR's registry and can never be attached to an item.
            if (String.IsNullOrEmpty(validationId))
                return;

            state.TemporaryIds.Add(
                new TemporaryId(spec, configuredId, validationId));
            if (!SetStringMember(spec, "Id", validationId))
            {
                state.TemporaryIds.RemoveAt(state.TemporaryIds.Count - 1);
                return;
            }

            LogOnce(
                "native-trait-isolated:" + configuredId,
                "Native TOR trait '" + configuredId + "' (" + name +
                ") is not present in the loaded registry. Registry initialization " +
                "will continue so unrelated careers remain usable; the affected " +
                "item keeps its original unresolved trait id and will fail locally " +
                "instead of disabling all TORCU encounters (" + context + ").",
                true);
        }

        private static string ResolveCurrentNativeId(
            string configuredName,
            string configuredDescription,
            Dictionary<string, List<NativeTraitCandidate>> byName)
        {
            string nameKey = NormalizeIdentity(configuredName);
            if (String.IsNullOrEmpty(nameKey))
                return null;

            List<NativeTraitCandidate> candidates;
            if (!byName.TryGetValue(nameKey, out candidates) ||
                candidates == null || candidates.Count == 0)
                return null;

            if (candidates.Count == 1)
                return candidates[0].Id;

            string descriptionKey = NormalizeIdentity(configuredDescription);
            if (String.IsNullOrEmpty(descriptionKey))
                return null;

            NativeTraitCandidate match = null;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (!String.Equals(
                    candidates[i].DescriptionKey,
                    descriptionKey,
                    StringComparison.Ordinal))
                    continue;
                if (match != null)
                    return null;
                match = candidates[i];
            }
            return match == null ? null : match.Id;
        }

        private static bool TryReadNativeRegistry(
            out IList registry,
            out HashSet<string> ids,
            out Dictionary<string, List<NativeTraitCandidate>> byName,
            out string validationId)
        {
            registry = null;
            ids = new HashSet<string>(StringComparer.Ordinal);
            byName = new Dictionary<string, List<NativeTraitCandidate>>(
                StringComparer.Ordinal);
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
            if (getTraits == null)
                return false;

            registry = getTraits.Invoke(manager, null) as IList;
            if (registry == null || registry.Count == 0)
                return false;

            for (int i = 0; i < registry.Count; i++)
            {
                object trait = registry[i];
                if (trait == null)
                    continue;

                string id = GetStringMember(trait, "ItemTraitStringId");
                if (String.IsNullOrWhiteSpace(id))
                    continue;

                ids.Add(id);
                bool torcuTrait = id.StartsWith(
                    "torcu_", StringComparison.Ordinal);
                if (!torcuTrait && validationId == null)
                    validationId = id;
                if (torcuTrait)
                    continue;

                string nameKey = NormalizeIdentity(
                    GetStringMember(trait, "ItemTraitName"));
                if (String.IsNullOrEmpty(nameKey))
                    continue;

                List<NativeTraitCandidate> candidates;
                if (!byName.TryGetValue(nameKey, out candidates))
                {
                    candidates = new List<NativeTraitCandidate>();
                    byName.Add(nameKey, candidates);
                }
                candidates.Add(new NativeTraitCandidate
                {
                    Id = id,
                    DescriptionKey = NormalizeIdentity(
                        GetStringMember(trait, "ItemTraitDescription"))
                });
            }

            return true;
        }

        private static void RestoreTemporaryIds(NativeTraitPatchState state)
        {
            if (state == null || state.Restored)
                return;

            state.Restored = true;
            for (int i = state.TemporaryIds.Count - 1; i >= 0; i--)
            {
                TemporaryId entry = state.TemporaryIds[i];
                string current = GetStringMember(entry.Spec, "Id");
                if (String.Equals(
                    current, entry.ValidationId, StringComparison.Ordinal))
                    SetStringMember(entry.Spec, "Id", entry.OriginalId);
            }
            state.TemporaryIds.Clear();
        }

        private static Array GetStaticArray(Type type, string name)
        {
            FieldInfo field = AccessTools.Field(type, name);
            return field == null ? null : field.GetValue(null) as Array;
        }

        private static Array GetArrayMember(object instance, string name)
        {
            object value = GetMember(instance, name);
            return value as Array;
        }

        private static object GetStaticMember(Type type, string name)
        {
            if (type == null)
                return null;

            PropertyInfo property = AccessTools.Property(type, name);
            if (property != null && property.GetGetMethod(true) != null)
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
            if (property != null && property.GetGetMethod(true) != null)
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
            object instance, string name, string value)
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

        private static string NormalizeIdentity(string value)
        {
            if (String.IsNullOrWhiteSpace(value))
                return String.Empty;

            StringBuilder result = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (Char.IsLetterOrDigit(c))
                    result.Append(Char.ToLowerInvariant(c));
            }
            return result.ToString();
        }

        private static void LogOnce(
            string key, string message, bool error)
        {
            if (!Logged.Add(key))
                return;

            try
            {
                Type logType = AccessTools.TypeByName("TORCareerUniques.ModLog");
                MethodInfo method = AccessTools.Method(
                    logType, error ? "Error" : "Info",
                    new[] { typeof(string) });
                if (method != null)
                    method.Invoke(null, new object[] { message });
            }
            catch
            {
                // Compatibility logging must never affect game state.
            }
        }

        private static string FormatException(Exception ex)
        {
            TargetInvocationException invocation = ex as TargetInvocationException;
            if (invocation != null && invocation.InnerException != null)
                ex = invocation.InnerException;
            return ex.GetType().FullName + ": " + ex.Message;
        }

        private sealed class NativeTraitPatchState
        {
            internal readonly List<TemporaryId> TemporaryIds =
                new List<TemporaryId>();
            internal bool Restored;
        }

        private sealed class TemporaryId
        {
            internal readonly object Spec;
            internal readonly string OriginalId;
            internal readonly string ValidationId;

            internal TemporaryId(
                object spec, string originalId, string validationId)
            {
                Spec = spec;
                OriginalId = originalId;
                ValidationId = validationId;
            }
        }

        private sealed class NativeTraitCandidate
        {
            internal string Id;
            internal string DescriptionKey;
        }
    }
}
