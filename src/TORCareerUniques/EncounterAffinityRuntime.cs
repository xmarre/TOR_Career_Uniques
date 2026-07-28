using System;
using System.Collections.Generic;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;

namespace TORCareerUniques
{
    // Party-specific non-aggression for independent legendary hosts. The patch is
    // a constant-time prefix on Bannerlord's existing attack eligibility model;
    // faction discovery is performed once when a campaign session is launched.
    internal static class EncounterAffinityRuntime
    {
        private const string PartyPrefix = "torcu_enc_";
        private const string HarmonyId =
            "torcareeruniques.encounters.affinity";
        private const float SuperiorTargetRatio = 1.60f;

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

        private static readonly Dictionary<string, string[]> AffinityTokens =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                { "GrailKnight", A("bretonnia", "bretonnian", "breton") },
                { "WarriorPriest", A("empire", "imperial", "reikland", "middenland") },
                { "BloodKnight", A("vampire", "sylvania", "mousillon") },
                { "Mercenary", A("border princes", "border_princes", "tilea", "estalia") },
                { "BlackGrailKnight", A("mousillon") },
                { "WarriorPriestUlric", A("empire", "imperial", "middenland") },
                { "Waywatcher", A("wood elf", "wood_elf", "asrai", "athel loren", "athel_loren") },
                { "Warden", A("eonir", "wood elf", "wood_elf", "asrai", "athel loren", "athel_loren") },
                { "KnightOldWorld", A("empire", "imperial", "reikland") },
                { "Slayer", A("dwarf", "dawi", "karaz ankor", "karaz_ankor") },
                { "OrcBoss", A("greenskin", "orc", "badlands") }
            };

        private static readonly Dictionary<string, HashSet<IFaction>>
            ProtectedFactionsByCareer =
                new Dictionary<string, HashSet<IFaction>>(
                    StringComparer.Ordinal);
        private static bool _patchInstalled;
        private static bool _patchFailureLogged;
        private static bool _diplomacyPatchInstalled;
        private static bool _diplomacyPatchFailureLogged;
        private static object _harmony;
        private static object _diplomacyHarmony;

        internal static void Initialize()
        {
            // Install only the party-AI patch before campaign loading. The synthetic
            // faction-diplomacy shim is installed later from OnCampaignSessionLaunched,
            // after saved encounter clans, leaders and ownership have been reconciled.
            EnsurePatchInstalled();
        }

        internal static void OnCampaignSessionLaunched()
        {
            EnsurePatchInstalled();
            EnsureDedicatedDiplomacyPatchInstalled();
            RebuildFactionCache();
        }

        internal static void ResetSession()
        {
            ProtectedFactionsByCareer.Clear();
        }

        // Harmony prefix. Returning false skips only the vanilla eligibility test
        // for the protected pair; it does not alter faction diplomacy or encounters.
        public static bool BeforeShouldConsiderAttacking(MobileParty party,
            MobileParty targetParty, ref bool __result)
        {
            try
            {
                if (party == null || targetParty == null)
                    return true;

                string attackerCareer = TryGetHostCareer(party);
                string targetCareer = TryGetHostCareer(targetParty);
                if (attackerCareer == null && targetCareer == null)
                    return true;

                // The player remains free to initiate an encounter even when their
                // kingdom happens to match the host's cultural affinity.
                if (party != MobileParty.MainParty &&
                    targetParty != MobileParty.MainParty)
                {
                    if ((attackerCareer != null && IsProtectedFaction(
                        attackerCareer, targetParty.MapFaction)) ||
                        (targetCareer != null && IsProtectedFaction(
                        targetCareer, party.MapFaction)))
                    {
                        __result = false;
                        return false;
                    }
                }

                // Legendary hosts keep attacking viable prey, while a deterministic
                // strength gate stops them selecting armies that overwhelmingly
                // outmatch them. The player can still attack the host normally.
                if (attackerCareer != null && party.Party != null &&
                    targetParty.Party != null &&
                    targetParty.Party.EstimatedStrength >
                        party.Party.EstimatedStrength * SuperiorTargetRatio)
                {
                    __result = false;
                    return false;
                }
            }
            catch
            {
                // Compatibility failure falls through to the native model.
            }
            return true;
        }

        // v1.7.15 correctly removed IsBanditFaction from runtime-created clans to
        // keep them out of Bannerlord's native hideout/spawn registries. That also
        // removed the faction manager's implicit bandit-vs-normal hostility. Emulate
        // only the two public relation predicates used by encounters/conversations;
        // do not put TORCU clans back into Clan.BanditFactions.
        public static bool BeforeIsAtWarAgainstFaction(IFaction __0,
            IFaction __1, ref bool __result)
        {
            if (!IsDedicatedVersusNormalFaction(__0, __1))
                return true;
            __result = true;
            return false;
        }

        public static bool BeforeIsNeutralWithFaction(IFaction __0,
            IFaction __1, ref bool __result)
        {
            if (!IsDedicatedVersusNormalFaction(__0, __1))
                return true;
            __result = false;
            return false;
        }

        private static bool IsDedicatedVersusNormalFaction(IFaction first,
            IFaction second)
        {
            bool firstDedicated = IsDedicatedEncounterFaction(first);
            bool secondDedicated = IsDedicatedEncounterFaction(second);
            if (firstDedicated == secondDedicated)
                return false;
            IFaction other = firstDedicated ? second : first;
            if (other == null || ReflectionUtil.ToBool(ReflectionUtil.GetProperty(
                other, "IsEliminated")))
                return false;

            // FactionManager.IsAtWarAgainstFaction returns false for eliminated
            // factions before consulting diplomacy. Never bypass that invariant: an
            // eliminated kingdom may no longer have a leader, and native daily barter
            // logic can otherwise pass that null leader into the diplomacy model.
            // Real bandit factions already use Bannerlord's native hostility rules.
            return !other.IsBanditFaction;
        }

        private static bool IsProtectedFaction(string careerId,
            IFaction faction)
        {
            HashSet<IFaction> factions;
            return faction != null && ProtectedFactionsByCareer.TryGetValue(
                careerId, out factions) && factions.Contains(faction);
        }

        private static string TryGetHostCareer(MobileParty party)
        {
            string id = party == null ? null : party.StringId;
            if (String.IsNullOrEmpty(id) || !id.StartsWith(PartyPrefix,
                StringComparison.Ordinal))
                return null;
            string remainder = id.Substring(PartyPrefix.Length);
            int separator = remainder.LastIndexOf('_');
            string slug = separator > 0 ? remainder.Substring(0, separator) :
                remainder;
            string careerId;
            return CareerByPartySlug.TryGetValue(slug, out careerId) ?
                careerId : null;
        }

        private static void RebuildFactionCache()
        {
            ProtectedFactionsByCareer.Clear();
            foreach (KeyValuePair<string, string[]> mapping in AffinityTokens)
                ProtectedFactionsByCareer[mapping.Key] =
                    new HashSet<IFaction>();

            foreach (Kingdom kingdom in Kingdom.All)
                AddFactionToMatchingMappings(kingdom);
            foreach (Clan clan in Clan.All)
            {
                if (clan == null || clan.Kingdom != null ||
                    IsDedicatedEncounterClan(clan) ||
                    ReflectionUtil.ToBool(ReflectionUtil.GetProperty(clan,
                        "IsBanditFaction")))
                    continue;
                AddFactionToMatchingMappings(clan);
            }

            foreach (KeyValuePair<string, HashSet<IFaction>> entry in
                ProtectedFactionsByCareer)
                ModLog.Verbose("Affinity cache " + entry.Key + ": " +
                    DescribeFactions(entry.Value) + ".");
        }

        private static bool IsDedicatedEncounterClan(Clan clan)
        {
            return IsDedicatedEncounterFaction(clan);
        }

        private static bool IsDedicatedEncounterFaction(IFaction faction)
        {
            Clan clan = faction as Clan;
            return clan != null && !String.IsNullOrEmpty(clan.StringId) &&
                clan.StringId.StartsWith("torcu_faction_",
                    StringComparison.Ordinal);
        }

        private static void AddFactionToMatchingMappings(IFaction faction)
        {
            if (faction == null)
                return;
            string descriptor = Normalize(ReflectionUtil.SearchText(faction) +
                " " + ReflectionUtil.SearchText(
                    ReflectionUtil.GetProperty(faction, "Culture")));
            foreach (KeyValuePair<string, string[]> mapping in AffinityTokens)
            {
                for (int i = 0; i < mapping.Value.Length; i++)
                {
                    if (!ContainsPhrase(descriptor, mapping.Value[i]))
                        continue;
                    ProtectedFactionsByCareer[mapping.Key].Add(faction);
                    break;
                }
            }
        }

        private static bool ContainsPhrase(string normalizedDescriptor,
            string phrase)
        {
            string normalizedPhrase = Normalize(phrase).Trim();
            return normalizedPhrase.Length > 0 && normalizedDescriptor.Contains(
                " " + normalizedPhrase + " ");
        }

        private static string Normalize(string value)
        {
            if (String.IsNullOrEmpty(value))
                return " ";
            char[] source = value.ToLowerInvariant().ToCharArray();
            char[] result = new char[source.Length];
            int length = 0;
            bool separating = true;
            for (int i = 0; i < source.Length; i++)
            {
                if (Char.IsLetterOrDigit(source[i]))
                {
                    result[length++] = source[i];
                    separating = false;
                }
                else if (!separating)
                {
                    result[length++] = ' ';
                    separating = true;
                }
            }
            if (length > 0 && result[length - 1] == ' ')
                length--;
            return " " + new string(result, 0, length) + " ";
        }

        private static string DescribeFactions(HashSet<IFaction> factions)
        {
            List<string> names = new List<string>();
            foreach (IFaction faction in factions)
                names.Add(Convert.ToString(ReflectionUtil.GetProperty(faction,
                    "StringId")) ?? faction.ToString());
            names.Sort(StringComparer.Ordinal);
            return names.Count == 0 ? "no runtime match" :
                String.Join(", ", names.ToArray());
        }

        private static void EnsurePatchInstalled()
        {
            if (_patchInstalled)
                return;
            try
            {
                Type harmonyType = FindLoadedType("HarmonyLib.Harmony");
                Type harmonyMethodType = FindLoadedType(
                    "HarmonyLib.HarmonyMethod");
                if (harmonyType == null || harmonyMethodType == null)
                    return;

                MethodInfo original = typeof(DefaultMobilePartyAIModel).GetMethod(
                    "ShouldConsiderAttacking", BindingFlags.Public |
                    BindingFlags.Instance, null, new[] { typeof(MobileParty),
                        typeof(MobileParty) }, null);
                MethodInfo prefix = typeof(EncounterAffinityRuntime).GetMethod(
                    "BeforeShouldConsiderAttacking", BindingFlags.Public |
                    BindingFlags.Static);
                if (original == null || prefix == null)
                    throw new MissingMethodException(
                        typeof(DefaultMobilePartyAIModel).FullName,
                        "ShouldConsiderAttacking(MobileParty, MobileParty)");

                _harmony = Activator.CreateInstance(harmonyType,
                    new object[] { HarmonyId });
                ApplyHarmonyPrefix(_harmony, harmonyType, harmonyMethodType,
                    original, prefix);
                _patchInstalled = true;
                ModLog.Info("Installed roaming-host affinity and superior-army " +
                    "attack safety on the native campaign AI model.");
            }
            catch (Exception ex)
            {
                if (!_patchFailureLogged)
                {
                    _patchFailureLogged = true;
                    ModLog.Error("Roaming-host AI safety patch failed: " +
                        ex.GetType().Name + ": " + ex.Message);
                }
            }
        }

        private static void EnsureDedicatedDiplomacyPatchInstalled()
        {
            if (_diplomacyPatchInstalled)
                return;
            try
            {
                Type harmonyType = FindLoadedType("HarmonyLib.Harmony");
                Type harmonyMethodType = FindLoadedType(
                    "HarmonyLib.HarmonyMethod");
                if (harmonyType == null || harmonyMethodType == null)
                    return;

                MethodInfo isAtWar = typeof(FactionManager).GetMethod(
                    "IsAtWarAgainstFaction", BindingFlags.Public |
                    BindingFlags.Static, null, new[] { typeof(IFaction),
                        typeof(IFaction) }, null);
                MethodInfo isNeutral = typeof(FactionManager).GetMethod(
                    "IsNeutralWithFaction", BindingFlags.Public |
                    BindingFlags.Static, null, new[] { typeof(IFaction),
                        typeof(IFaction) }, null);
                MethodInfo warPrefix = typeof(EncounterAffinityRuntime).GetMethod(
                    "BeforeIsAtWarAgainstFaction", BindingFlags.Public |
                    BindingFlags.Static);
                MethodInfo neutralPrefix = typeof(EncounterAffinityRuntime).GetMethod(
                    "BeforeIsNeutralWithFaction", BindingFlags.Public |
                    BindingFlags.Static);
                if (isAtWar == null || isNeutral == null || warPrefix == null ||
                    neutralPrefix == null)
                    throw new MissingMethodException(typeof(FactionManager).FullName,
                        "IsAtWarAgainstFaction/IsNeutralWithFaction");

                _diplomacyHarmony = Activator.CreateInstance(harmonyType,
                    new object[] { HarmonyId + ".diplomacy" });
                ApplyHarmonyPrefix(_diplomacyHarmony, harmonyType,
                    harmonyMethodType, isAtWar, warPrefix);
                ApplyHarmonyPrefix(_diplomacyHarmony, harmonyType,
                    harmonyMethodType, isNeutral, neutralPrefix);
                _diplomacyPatchInstalled = true;
                ModLog.Info("Installed dedicated encounter-clan hostile diplomacy " +
                    "shim without native bandit-faction registration.");
            }
            catch (Exception ex)
            {
                if (!_diplomacyPatchFailureLogged)
                {
                    _diplomacyPatchFailureLogged = true;
                    ModLog.Error("Dedicated encounter diplomacy patch failed: " +
                        ex.GetType().Name + ": " + ex.Message);
                }
            }
        }

        private static void ApplyHarmonyPrefix(object harmonyInstance,
            Type harmonyType, Type harmonyMethodType, MethodInfo original,
            MethodInfo prefix)
        {
            object harmonyPrefix = CreateHarmonyMethod(harmonyMethodType,
                prefix);
            MethodInfo[] methods = harmonyType.GetMethods(BindingFlags.Public |
                BindingFlags.Instance);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo candidate = methods[i];
                if (candidate.Name != "Patch")
                    continue;
                ParameterInfo[] parameters = candidate.GetParameters();
                if (parameters.Length < 2 ||
                    !typeof(MethodBase).IsAssignableFrom(
                        parameters[0].ParameterType))
                    continue;
                object[] arguments = new object[parameters.Length];
                arguments[0] = original;
                bool usable = true;
                for (int p = 1; p < parameters.Length; p++)
                {
                    string name = parameters[p].Name ?? String.Empty;
                    if (String.Equals(name, "prefix",
                        StringComparison.OrdinalIgnoreCase))
                        arguments[p] = harmonyPrefix;
                    else if (parameters[p].HasDefaultValue)
                        arguments[p] = parameters[p].DefaultValue;
                    else if (!parameters[p].ParameterType.IsValueType)
                        arguments[p] = null;
                    else
                    {
                        usable = false;
                        break;
                    }
                }
                if (!usable)
                    continue;
                candidate.Invoke(harmonyInstance, arguments);
                return;
            }
            throw new MissingMethodException(harmonyType.FullName,
                "Patch(MethodBase, ...)");
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
            if (property != null)
            {
                property.SetValue(result, patchMethod, null);
                return result;
            }
            throw new MissingMemberException(harmonyMethodType.FullName,
                "method");
        }

        private static Type FindLoadedType(string fullName)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type found = assemblies[i].GetType(fullName, false);
                if (found != null)
                    return found;
            }
            return null;
        }

        private static string[] A(params string[] values)
        {
            return values;
        }
    }
}
