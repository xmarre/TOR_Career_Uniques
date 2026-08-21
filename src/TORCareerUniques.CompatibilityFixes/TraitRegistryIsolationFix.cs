using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace TORCareerUniques.CompatibilityFixes
{
    /// <summary>
    /// Isolates TOR native-trait availability failures to the TORCU item that actually
    /// requires the trait. TOR/WiTM may legitimately expose a registry in which one
    /// authored native enchantment is absent while the rest of the registry is usable.
    /// A missing native trait must therefore never poison initialization for every
    /// unrelated career.
    /// </summary>
    public sealed class TraitRegistryIsolationSubModule : MBSubModuleBase
    {
        private const string HarmonyId = "torcareeruniques.traitregistryisolation";
        private const string TorcuTraitPrefix = "torcu_";
        private static readonly object LogSync = new object();
        private static readonly HashSet<string> LoggedMessages =
            new HashSet<string>(StringComparer.Ordinal);
        private static bool _installed;

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            if (_installed)
                return;

            var harmony = new Harmony(HarmonyId);
            InstallRegistryIsolation(harmony);
            InstallTorcuFactoryValidation(harmony);
            _installed = true;
        }

        private static void InstallRegistryIsolation(Harmony harmony)
        {
            Type careerRuntime = AccessTools.TypeByName(
                "TORCareerUniques.CareerUniqueRuntime");
            Type setRuntime = AccessTools.TypeByName(
                "TORCareerUniques.SetItemRuntime");
            if (careerRuntime == null || setRuntime == null)
                throw new TypeLoadException(
                    "TOR Career Uniques trait runtimes were not found.");

            MethodInfo careerEnsure = AccessTools.Method(
                careerRuntime, "EnsureTraitsInjected");
            MethodInfo setEnsure = AccessTools.Method(
                setRuntime, "EnsureTraitsInjected");
            if (careerEnsure == null || setEnsure == null)
                throw new MissingMethodException(
                    "TOR Career Uniques trait initialization methods were not found.");

            MethodInfo careerPrefix = AccessTools.Method(
                typeof(TraitRegistryIsolationSubModule),
                nameof(BeforeCareerEnsureTraitsInjected));
            MethodInfo setPrefix = AccessTools.Method(
                typeof(TraitRegistryIsolationSubModule),
                nameof(BeforeSetEnsureTraitsInjected));
            if (careerPrefix == null || setPrefix == null)
                throw new MissingMethodException(
                    "Trait-registry isolation prefixes were not found.");

            harmony.Patch(careerEnsure,
                prefix: new HarmonyMethod(careerPrefix));
            harmony.Patch(setEnsure,
                prefix: new HarmonyMethod(setPrefix));
        }

        private static void InstallTorcuFactoryValidation(Harmony harmony)
        {
            Type helperType = AccessTools.TypeByName(
                "TOR_Core.CampaignMechanics.Crafting.EnchantmentHelper");
            if (helperType == null)
                throw new TypeLoadException("TOR EnchantmentHelper was not found.");

            MethodInfo prefix = AccessTools.Method(
                typeof(TraitRegistryIsolationSubModule),
                nameof(BeforeCreateEnchantedItem));
            if (prefix == null)
                throw new MissingMethodException(
                    typeof(TraitRegistryIsolationSubModule).FullName,
                    nameof(BeforeCreateEnchantedItem));

            int patched = 0;
            foreach (MethodInfo method in AccessTools.GetDeclaredMethods(helperType))
            {
                if (!String.Equals(method.Name, "CreateEnchantedItem",
                    StringComparison.Ordinal) || method.GetParameters().Length != 5)
                    continue;
                harmony.Patch(method, prefix: new HarmonyMethod(prefix));
                patched++;
            }

            if (patched == 0)
                throw new MissingMethodException(
                    helperType.FullName, "CreateEnchantedItem(..., 5 parameters)");
        }

        public static bool BeforeCareerEnsureTraitsInjected(ref bool __result)
        {
            try
            {
                __result = EnsureCareerTraitsInjected();
                return false;
            }
            catch (Exception ex)
            {
                LogOnce("career-injection-fallback",
                    "Career trait-registry isolation failed; falling back to the core " +
                    "initializer. " + FormatException(ex));
                return true;
            }
        }

        public static bool BeforeSetEnsureTraitsInjected(ref bool __result)
        {
            try
            {
                __result = EnsureSetTraitsInjected();
                return false;
            }
            catch (Exception ex)
            {
                LogOnce("set-injection-fallback",
                    "Set trait-registry isolation failed; falling back to the core " +
                    "initializer. " + FormatException(ex));
                return true;
            }
        }

        public static bool BeforeCreateEnchantedItem(
            object[] __args, ref ItemObject __result)
        {
            if (__args == null || __args.Length < 2)
                return true;

            IEnumerable requested = __args[1] as IEnumerable;
            if (requested == null)
                return true;

            List<string> requestedIds = new List<string>();
            bool isTorcuItem = false;
            foreach (object value in requested)
            {
                string id = Convert.ToString(value);
                if (String.IsNullOrEmpty(id))
                    continue;
                requestedIds.Add(id);
                if (id.StartsWith(TorcuTraitPrefix,
                    StringComparison.OrdinalIgnoreCase))
                    isTorcuItem = true;
            }
            if (!isTorcuItem)
                return true;

            object manager;
            Type traitType;
            IList registered;
            HashSet<string> existing;
            if (!TryGetTraitRegistry(out manager, out traitType,
                out registered, out existing))
            {
                __result = null;
                LogOnce("factory-registry-not-ready",
                    "TORCU item creation was blocked because TOR's item-trait registry " +
                    "is not ready.");
                return false;
            }

            for (int i = 0; i < requestedIds.Count; i++)
            {
                string id = requestedIds[i];
                if (existing.Contains(id))
                    continue;

                __result = null;
                LogOnce("factory-missing:" + id,
                    "TORCU item creation was blocked because required TOR trait '" +
                    id + "' is absent from the live registry. Only items requiring " +
                    "that trait are unavailable.");
                return false;
            }

            return true;
        }

        private static bool EnsureCareerTraitsInjected()
        {
            Type runtimeType = AccessTools.TypeByName(
                "TORCareerUniques.CareerUniqueRuntime");
            if (runtimeType == null)
                return false;

            object manager;
            Type traitType;
            IList registered;
            HashSet<string> existing;
            if (!TryGetTraitRegistry(out manager, out traitType,
                out registered, out existing))
                return false;

            FieldInfo cachedManager = RequireField(runtimeType,
                "_traitsInjectedManager");
            if (Object.ReferenceEquals(manager, cachedManager.GetValue(null)))
                return true;

            Array definitions = RequireArray(
                RequireField(runtimeType, "Definitions").GetValue(null),
                "CareerUniqueRuntime.Definitions");
            MethodInfo createTrait = AccessTools.Method(runtimeType, "CreateTrait");
            if (createTrait == null)
                throw new MissingMethodException(runtimeType.FullName, "CreateTrait");

            int added = 0;
            for (int d = 0; d < definitions.Length; d++)
            {
                object definition = definitions.GetValue(d);
                string careerId = ReadString(definition, "CareerId");
                string validItemType = ReadString(definition, "ValidItemType");
                Array specs = RequireArray(ReadMember(definition, "Traits"),
                    careerId + ".Traits");

                for (int t = 0; t < specs.Length; t++)
                {
                    object spec = specs.GetValue(t);
                    string id = ReadString(spec, "Id");
                    if (t == 3)
                    {
                        if (!existing.Contains(id))
                            LogMissingNative(id, "career relic " + careerId);
                        continue;
                    }

                    if (existing.Contains(id))
                        continue;

                    object trait = createTrait.Invoke(null,
                        new[] { traitType, spec, (object)validItemType });
                    if (trait == null)
                        throw new InvalidOperationException(
                            "CreateTrait returned null for " + id + ".");
                    registered.Add(trait);
                    existing.Add(id);
                    added++;
                }
            }

            if (added > 0)
                LogInfo("Injected " + added +
                    " unique-item traits while isolating unavailable native TOR traits.");

            bool complete = definitions.Length == 0;
            if (!complete)
            {
                object first = definitions.GetValue(0);
                string signature = ReadString(first, "SignatureTraitId");
                if (String.IsNullOrEmpty(signature))
                {
                    Array firstTraits = RequireArray(ReadMember(first, "Traits"),
                        "first career definition traits");
                    signature = firstTraits.Length == 0 ? null :
                        ReadString(firstTraits.GetValue(0), "Id");
                }
                complete = !String.IsNullOrEmpty(signature) &&
                    existing.Contains(signature);
            }

            if (complete)
                cachedManager.SetValue(null, manager);
            return complete;
        }

        private static bool EnsureSetTraitsInjected()
        {
            Type runtimeType = AccessTools.TypeByName(
                "TORCareerUniques.SetItemRuntime");
            Type careerRuntime = AccessTools.TypeByName(
                "TORCareerUniques.CareerUniqueRuntime");
            if (runtimeType == null || careerRuntime == null)
                return false;

            object manager;
            Type traitType;
            IList registered;
            HashSet<string> existing;
            if (!TryGetTraitRegistry(out manager, out traitType,
                out registered, out existing))
                return false;

            FieldInfo cachedManager = RequireField(runtimeType,
                "_traitsInjectedManager");
            if (Object.ReferenceEquals(manager, cachedManager.GetValue(null)))
                return true;

            Array definitions = RequireArray(
                RequireField(runtimeType, "Definitions").GetValue(null),
                "SetItemRuntime.Definitions");
            MethodInfo getRelic = RequireMethod(careerRuntime,
                "GetDefinitionForSet");
            MethodInfo cloneTrait = RequireMethod(runtimeType, "CloneTrait");
            MethodInfo getAdminSignature = RequireMethod(runtimeType,
                "GetAdminSignature");
            MethodInfo getHeroSignature = RequireMethod(runtimeType,
                "GetHeroSignature");
            MethodInfo injectTrait = RequireMethod(runtimeType, "InjectTrait");
            MethodInfo getBonusTargetKind = RequireMethod(runtimeType,
                "GetBonusTargetKind");
            MethodInfo getRoutedPieceTraitId = RequireMethod(runtimeType,
                "GetRoutedPieceTraitId");
            MethodInfo getBonusValidItemType = RequireMethod(runtimeType,
                "GetBonusValidItemType");

            int added = 0;
            for (int d = 0; d < definitions.Length; d++)
            {
                object definition = definitions.GetValue(d);
                string careerId = ReadString(definition, "CareerId");
                object relic = getRelic.Invoke(null, new object[] { careerId });
                if (relic == null)
                    continue;

                Array relicTraits = RequireArray(ReadMember(relic, "Traits"),
                    careerId + " relic traits");
                if (relicTraits.Length == 0)
                    continue;
                object relicSignature = relicTraits.GetValue(0);
                string relicValidItemType = ReadString(relic, "ValidItemType");

                object adminRelicAlias = cloneTrait.Invoke(null, new[]
                {
                    relicSignature,
                    getAdminSignature.Invoke(null,
                        new object[] { definition, 0 })
                });
                if (InvokeInject(injectTrait, registered, existing, traitType,
                    adminRelicAlias, relicValidItemType))
                    added++;

                object heroRelicAlias = cloneTrait.Invoke(null, new[]
                {
                    relicSignature,
                    getHeroSignature.Invoke(null,
                        new object[] { definition, 0 })
                });
                if (InvokeInject(injectTrait, registered, existing, traitType,
                    heroRelicAlias, relicValidItemType))
                    added++;

                Array pieces = RequireArray(ReadMember(definition, "Pieces"),
                    careerId + ".Pieces");
                for (int p = 0; p < pieces.Length; p++)
                {
                    object piece = pieces.GetValue(p);
                    Array effects = RequireArray(ReadMember(piece, "Effects"),
                        careerId + " set piece " + (p + 1) + " effects");

                    for (int e = 0; e < effects.Length; e++)
                    {
                        object effect = effects.GetValue(e);
                        string id = ReadString(effect, "Id");
                        if (e == 1)
                        {
                            if (!existing.Contains(id))
                                LogMissingNative(id, careerId +
                                    " set piece " + (p + 1));
                            continue;
                        }
                        if (InvokeInject(injectTrait, registered, existing,
                            traitType, effect, "Armor"))
                            added++;
                    }

                    if (effects.Length > 0)
                    {
                        object primary = effects.GetValue(0);
                        object adminAlias = cloneTrait.Invoke(null, new[]
                        {
                            primary,
                            getAdminSignature.Invoke(null,
                                new object[] { definition, p + 1 })
                        });
                        if (InvokeInject(injectTrait, registered, existing,
                            traitType, adminAlias, "Armor"))
                            added++;

                        object heroAlias = cloneTrait.Invoke(null, new[]
                        {
                            primary,
                            getHeroSignature.Invoke(null,
                                new object[] { definition, p + 1 })
                        });
                        if (InvokeInject(injectTrait, registered, existing,
                            traitType, heroAlias, "Armor"))
                            added++;
                    }

                    for (int e = 0; e < effects.Length; e++)
                    {
                        object effect = effects.GetValue(e);
                        object targetKind = getBonusTargetKind.Invoke(null,
                            new[] { effect });
                        if (String.Equals(Convert.ToString(targetKind), "Armor",
                            StringComparison.Ordinal))
                            continue;

                        object routed = cloneTrait.Invoke(null, new[]
                        {
                            effect,
                            getRoutedPieceTraitId.Invoke(null,
                                new[] { effect })
                        });
                        string validType = Convert.ToString(
                            getBonusValidItemType.Invoke(null,
                                new[] { effect }));
                        if (InvokeInject(injectTrait, registered, existing,
                            traitType, routed, validType))
                            added++;
                    }
                }

                Array tiers = RequireArray(ReadMember(definition, "Tiers"),
                    careerId + ".Tiers");
                for (int t = 0; t < tiers.Length; t++)
                {
                    Array effects = RequireArray(
                        ReadMember(tiers.GetValue(t), "Effects"),
                        careerId + " tier " + t + " effects");
                    for (int e = 0; e < effects.Length; e++)
                    {
                        object effect = effects.GetValue(e);
                        string validType = Convert.ToString(
                            getBonusValidItemType.Invoke(null,
                                new[] { effect }));
                        if (InvokeInject(injectTrait, registered, existing,
                            traitType, effect, validType))
                            added++;
                    }
                }
            }

            if (added > 0)
                LogInfo("Injected " + added +
                    " career-set/admin traits while isolating unavailable native TOR traits.");

            bool complete = definitions.Length == 0;
            if (!complete)
            {
                object firstDefinition = definitions.GetValue(0);
                Array pieces = RequireArray(
                    ReadMember(firstDefinition, "Pieces"),
                    "first set definition pieces");
                if (pieces.Length > 0)
                {
                    Array effects = RequireArray(
                        ReadMember(pieces.GetValue(0), "Effects"),
                        "first set-piece effects");
                    string signature = effects.Length == 0 ? null :
                        ReadString(effects.GetValue(0), "Id");
                    complete = !String.IsNullOrEmpty(signature) &&
                        existing.Contains(signature);
                }
            }

            if (complete)
                cachedManager.SetValue(null, manager);
            return complete;
        }

        private static bool TryGetTraitRegistry(out object manager,
            out Type traitType, out IList registered,
            out HashSet<string> existing)
        {
            manager = null;
            traitType = AccessTools.TypeByName("TOR_Core.Items.ItemTrait");
            registered = null;
            existing = null;

            Type managerType = AccessTools.TypeByName(
                "TOR_Core.Items.ItemTraitManager");
            if (managerType == null || traitType == null)
                return false;

            PropertyInfo instance = AccessTools.Property(managerType, "Instance");
            manager = instance == null ? null : instance.GetValue(null, null);
            MethodInfo getTraits = AccessTools.Method(managerType, "GetItemTraits");
            registered = manager == null || getTraits == null ? null :
                getTraits.Invoke(manager, null) as IList;
            if (registered == null || registered.Count == 0)
                return false;

            existing = new HashSet<string>(StringComparer.Ordinal);
            foreach (object trait in registered)
            {
                string id = ReadString(trait, "ItemTraitStringId");
                if (!String.IsNullOrEmpty(id))
                    existing.Add(id);
            }
            return true;
        }

        private static bool InvokeInject(MethodInfo injectTrait,
            IList registered, HashSet<string> existing, Type traitType,
            object spec, string validItemType)
        {
            return Convert.ToBoolean(injectTrait.Invoke(null, new object[]
            {
                registered, existing, traitType, spec, validItemType
            }));
        }

        private static FieldInfo RequireField(Type type, string name)
        {
            FieldInfo field = AccessTools.Field(type, name);
            if (field == null)
                throw new MissingFieldException(type.FullName, name);
            return field;
        }

        private static MethodInfo RequireMethod(Type type, string name)
        {
            MethodInfo method = AccessTools.Method(type, name);
            if (method == null)
                throw new MissingMethodException(type.FullName, name);
            return method;
        }

        private static Array RequireArray(object value, string name)
        {
            Array array = value as Array;
            if (array == null)
                throw new InvalidOperationException(name + " is unavailable.");
            return array;
        }

        private static object ReadMember(object instance, string name)
        {
            if (instance == null)
                return null;
            Type type = instance.GetType();
            FieldInfo field = AccessTools.Field(type, name);
            if (field != null)
                return field.GetValue(instance);
            PropertyInfo property = AccessTools.Property(type, name);
            return property == null ? null : property.GetValue(instance, null);
        }

        private static string ReadString(object instance, string name)
        {
            return Convert.ToString(ReadMember(instance, name));
        }

        private static void LogMissingNative(string traitId, string context)
        {
            LogOnce("missing-native:" + traitId + ":" + context,
                "Required native TOR trait is missing: " + traitId +
                " (" + context + "). Only items that require this native trait " +
                "will be unavailable.");
        }

        private static void LogOnce(string key, string message)
        {
            lock (LogSync)
            {
                if (!LoggedMessages.Add(key))
                    return;
            }
            Log("Error", message);
        }

        private static void LogInfo(string message)
        {
            Log("Info", message);
        }

        private static void Log(string level, string message)
        {
            try
            {
                Type logType = AccessTools.TypeByName("TORCareerUniques.ModLog");
                MethodInfo method = logType == null ? null :
                    AccessTools.Method(logType, level,
                        new[] { typeof(string) });
                if (method != null)
                {
                    method.Invoke(null, new object[] { message });
                    return;
                }
            }
            catch
            {
            }
            Console.WriteLine("[TORCareerUniques] " + message);
        }

        private static string FormatException(Exception ex)
        {
            if (ex == null)
                return "Unknown error.";
            TargetInvocationException invocation = ex as TargetInvocationException;
            Exception inner = invocation == null ? ex :
                (invocation.InnerException ?? invocation);
            return inner.GetType().Name + ": " + inner.Message;
        }
    }
}
