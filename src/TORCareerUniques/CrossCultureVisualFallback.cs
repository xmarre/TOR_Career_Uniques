using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace TORCareerUniques
{
    internal static partial class SetItemRuntime
    {
        private const string CrossCultureVisualFallbackHarmonyId =
            "torcareeruniques.visuals.cross-culture-role-fallback";

        private static readonly bool CrossCultureVisualFallbackInstalled =
            TryInstallCrossCultureVisualFallback();

        private static readonly Dictionary<string, string> InferredVisualCultureByCareer =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> VisualCultureInferenceAttempted =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static object _crossCultureVisualResolverSession;

        // An explicit type constructor guarantees the one-time installation attempt before
        // the resolver is used. Installation failure is logged and leaves the original
        // declared-culture resolver active; v1.7.30's transactional hero rollback remains
        // the hard safety boundary.
        static SetItemRuntime()
        {
        }

        private sealed class VisualCultureFallbackCandidate
        {
            internal string CultureKey;
            internal string CultureName;
            internal int Score;
        }

        private static bool TryInstallCrossCultureVisualFallback()
        {
            try
            {
                return InstallCrossCultureVisualFallback();
            }
            catch (Exception ex)
            {
                ModLog.Error("Cross-culture visual role fallback could not be installed; " +
                    "the original declared-culture resolver remains active. " +
                    ex.GetType().Name + ": " + ex.Message);
                return false;
            }
        }

        private static bool InstallCrossCultureVisualFallback()
        {
            Type harmonyType = FindCrossCultureHarmonyType(
                "HarmonyLib.Harmony", "0Harmony");
            Type harmonyMethodType = FindCrossCultureHarmonyType(
                "HarmonyLib.HarmonyMethod", "0Harmony");
            if (harmonyType == null || harmonyMethodType == null)
                throw new TypeLoadException(
                    "HarmonyLib is unavailable while installing cross-culture visual fallback.");

            MethodInfo original = typeof(SetItemRuntime).GetMethod(
                nameof(CharacterMatchesVisualCulture),
                BindingFlags.NonPublic | BindingFlags.Static,
                null,
                new[] { typeof(object), typeof(VisualProfile) },
                null);
            MethodInfo postfix = typeof(SetItemRuntime).GetMethod(
                nameof(ApplyCrossCultureVisualFallback),
                BindingFlags.NonPublic | BindingFlags.Static);
            if (original == null || postfix == null)
                throw new MissingMethodException(typeof(SetItemRuntime).FullName,
                    "CharacterMatchesVisualCulture cross-culture fallback target");

            object harmony = Activator.CreateInstance(harmonyType,
                new object[] { CrossCultureVisualFallbackHarmonyId });
            object harmonyPostfix = CreateCrossCultureHarmonyMethod(
                harmonyMethodType, postfix);
            ApplyCrossCultureHarmonyPatch(harmonyType, harmony, original,
                harmonyPostfix);
            return true;
        }

        private static void ApplyCrossCultureVisualFallback(
            object __0, VisualProfile __1, ref bool __result)
        {
            if (__result || __0 == null || __1 == null)
                return;

            string careerId = FindCareerForVisualProfile(__1);
            if (String.IsNullOrEmpty(careerId))
                return;

            string fallbackCulture = GetOrInferVisualFallbackCulture(
                careerId, __1);
            if (String.IsNullOrEmpty(fallbackCulture))
                return;

            string characterCulture = GetVisualCultureKey(__0);
            if (String.Equals(characterCulture, fallbackCulture,
                StringComparison.OrdinalIgnoreCase))
                __result = true;
        }

        private static string FindCareerForVisualProfile(VisualProfile profile)
        {
            foreach (KeyValuePair<string, VisualProfile> pair in VisualProfileByCareer)
                if (Object.ReferenceEquals(pair.Value, profile))
                    return pair.Key;
            return null;
        }

        private static string GetOrInferVisualFallbackCulture(
            string careerId, VisualProfile profile)
        {
            EnsureCrossCultureVisualResolverSession();

            string cached;
            if (InferredVisualCultureByCareer.TryGetValue(careerId, out cached))
                return cached;
            if (VisualCultureInferenceAttempted.Contains(careerId))
                return null;
            VisualCultureInferenceAttempted.Add(careerId);

            SetDefinition definition;
            if (!DefinitionByCareer.TryGetValue(careerId, out definition))
                return null;
            CareerItemDefinition relic = CareerUniqueRuntime.GetDefinitionForSet(careerId);
            if (relic == null)
                return null;

            IEnumerable characters = GetStaticProperty(
                TypeByName("TaleWorlds.CampaignSystem.CharacterObject"), "All") as IEnumerable;
            if (characters == null)
                return null;

            // Do not broaden a functioning culture. The fallback activates only when the
            // declared culture has no role-compatible character with any armour coverage.
            foreach (object character in characters)
            {
                if (character == null ||
                    !MatchesDeclaredVisualCulture(character, profile))
                    continue;
                if (CountCompatibleRelicItemsOnCharacter(character, relic) > 0 &&
                    CountArmorSlotCoverage(character) > 0)
                    return null;
            }

            VisualCultureFallbackCandidate best = null;
            foreach (object character in characters)
            {
                if (character == null ||
                    MatchesDeclaredVisualCulture(character, profile))
                    continue;

                int roleMatches = CountCompatibleRelicItemsOnCharacter(character, relic);
                int coverage = CountArmorSlotCoverage(character);
                if (roleMatches <= 0 || coverage <= 0)
                    continue;

                string cultureKey = GetVisualCultureKey(character);
                if (String.IsNullOrEmpty(cultureKey))
                    continue;

                string search = NormalizeSearch(
                    (Convert.ToString(GetProperty(character, "StringId")) ?? String.Empty) + " " +
                    (Convert.ToString(GetProperty(character, "Name")) ?? String.Empty));
                int primary = CountPhraseMatches(search, profile.PrimaryPhrases);
                int distinctiveSecondary = CountDistinctiveVisualFallbackMatches(
                    search, profile.SecondaryPhrases);
                int definitionTheme = Math.Max(0,
                    ScoreDefinitionThemeOnObject(definition, character, 280));
                if (primary <= 0 && distinctiveSecondary <= 0 && definitionTheme <= 0)
                    continue;

                int negative = CountPhraseMatches(search, profile.NegativePhrases);
                int tier = Math.Max(0, EnumNumber(GetProperty(character, "Tier")));
                int score = primary * 7000 + distinctiveSecondary * 3200 +
                    definitionTheme + roleMatches * 1600 + coverage * 450 + tier * 120 -
                    negative * 5000;

                object culture = GetProperty(character, "Culture");
                string cultureName = Convert.ToString(GetProperty(culture, "Name"));
                if (best == null || score > best.Score ||
                    (score == best.Score && String.CompareOrdinal(
                        cultureKey, best.CultureKey ?? String.Empty) < 0))
                {
                    best = new VisualCultureFallbackCandidate
                    {
                        CultureKey = cultureKey,
                        CultureName = cultureName,
                        Score = score
                    };
                }
            }

            if (best == null || String.IsNullOrEmpty(best.CultureKey))
                return null;

            InferredVisualCultureByCareer[careerId] = best.CultureKey;
            ModLog.Info("Declared visual culture for " + careerId +
                " has no usable role-compatible armour source; inferred adjacent culture " +
                (String.IsNullOrEmpty(best.CultureName) ? best.CultureKey : best.CultureName) +
                " [" + best.CultureKey + "] from career and relic-role evidence.");
            return best.CultureKey;
        }

        private static bool MatchesDeclaredVisualCulture(
            object character, VisualProfile profile)
        {
            if (character == null || profile == null)
                return false;
            object culture = GetProperty(character, "Culture");
            string cultureSearch = NormalizeSearch(
                (Convert.ToString(GetProperty(culture, "StringId")) ?? String.Empty) + " " +
                (Convert.ToString(GetProperty(culture, "Name")) ?? String.Empty));
            return CountPhraseMatches(cultureSearch, profile.CulturePhrases) > 0;
        }

        private static string GetVisualCultureKey(object character)
        {
            if (character == null)
                return null;
            object culture = GetProperty(character, "Culture");
            if (culture == null)
                return null;
            string id = Convert.ToString(GetProperty(culture, "StringId"));
            if (!String.IsNullOrWhiteSpace(id))
                return NormalizeSearch(id);
            string name = Convert.ToString(GetProperty(culture, "Name"));
            return String.IsNullOrWhiteSpace(name) ? null : NormalizeSearch(name);
        }

        private static int CountDistinctiveVisualFallbackMatches(
            string normalizedSearch, string[] phrases)
        {
            if (String.IsNullOrEmpty(normalizedSearch) || phrases == null)
                return 0;
            int matches = 0;
            for (int i = 0; i < phrases.Length; i++)
            {
                string phrase = NormalizeSearch(phrases[i] ?? String.Empty);
                if (phrase.Length < 4 || IsGenericVisualRolePhrase(phrase))
                    continue;
                if (normalizedSearch.Contains(phrase))
                    matches++;
            }
            return matches;
        }

        private static bool IsGenericVisualRolePhrase(string phrase)
        {
            switch (phrase)
            {
                case "wizard":
                case "mage":
                case "sorcerer":
                case "sorceress":
                case "caster":
                case "spellcaster":
                case "staff":
                case "priest":
                case "shaman":
                case "archer":
                case "ranger":
                case "scout":
                case "knight":
                case "lord":
                case "warrior":
                case "infantry":
                case "cavalry":
                case "spearman":
                case "swordsman":
                case "mercenary":
                case "captain":
                case "hunter":
                case "warden":
                    return true;
                default:
                    return false;
            }
        }

        private static void EnsureCrossCultureVisualResolverSession()
        {
            object session = GetStaticProperty(
                TypeByName("TaleWorlds.CampaignSystem.Campaign"), "Current");
            if (Object.ReferenceEquals(session, _crossCultureVisualResolverSession))
                return;
            _crossCultureVisualResolverSession = session;
            InferredVisualCultureByCareer.Clear();
            VisualCultureInferenceAttempted.Clear();
        }

        private static Type FindCrossCultureHarmonyType(
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

        private static object CreateCrossCultureHarmonyMethod(
            Type harmonyMethodType, MethodInfo patchMethod)
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

        private static void ApplyCrossCultureHarmonyPatch(
            Type harmonyType, object harmony, MethodInfo original, object postfix)
        {
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
                    if (String.Equals(name, "postfix",
                        StringComparison.OrdinalIgnoreCase))
                        args[p] = postfix;
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
                "Patch(MethodBase, ..., HarmonyMethod postfix)");
        }
    }
}
