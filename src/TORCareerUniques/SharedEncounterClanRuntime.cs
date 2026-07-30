using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace TORCareerUniques
{
    public sealed class SharedEncounterClanSubModule : MBSubModuleBase
    {
        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            UniqueEncounterBehavior.InitializeSharedEncounterClanRuntime();
        }
    }

    internal sealed partial class UniqueEncounterBehavior
    {
        private const string SharedEncounterClanId =
            "torcu_faction_collective";
        private const string SharedEncounterClanHarmonyId =
            "torcareeruniques.shared-encounter-clan.1.7.38";
        private static bool _sharedEncounterClanRuntimeInstalled;

        internal static void InitializeSharedEncounterClanRuntime()
        {
            if (_sharedEncounterClanRuntimeInstalled)
                return;

            try
            {
                Harmony harmony = new Harmony(SharedEncounterClanHarmonyId);

                MethodInfo resolver = AccessTools.Method(
                    typeof(UniqueEncounterBehavior),
                    "ResolveOrCreateEncounterOwnerClan",
                    new[]
                    {
                        typeof(EncounterDefinition), typeof(Settlement),
                        typeof(Hero), typeof(CharacterObject), typeof(Clan)
                    });
                MethodInfo resolverPrefix = AccessTools.Method(
                    typeof(UniqueEncounterBehavior),
                    nameof(ReplaceEncounterOwnerClan));
                if (resolver == null || resolverPrefix == null)
                    throw new MissingMethodException(
                        "Encounter owner-clan resolver was not found.");
                harmony.Patch(resolver, prefix: new HarmonyMethod(resolverPrefix)
                {
                    priority = Priority.First
                });

                MethodInfo ensure = AccessTools.Method(
                    typeof(UniqueEncounterBehavior),
                    "EnsureEncounterHeroClan",
                    new[] { typeof(Hero), typeof(Clan) });
                MethodInfo ensurePrefix = AccessTools.Method(
                    typeof(UniqueEncounterBehavior),
                    nameof(ReplaceEncounterHeroClanAttachment));
                if (ensure == null || ensurePrefix == null)
                    throw new MissingMethodException(
                        "Encounter hero clan-attachment method was not found.");
                harmony.Patch(ensure, prefix: new HarmonyMethod(ensurePrefix)
                {
                    priority = Priority.First
                });

                MethodInfo session = AccessTools.Method(
                    typeof(UniqueEncounterBehavior), "OnSessionLaunched",
                    new[] { typeof(CampaignGameStarter) });
                MethodInfo sessionPostfix = AccessTools.Method(
                    typeof(UniqueEncounterBehavior),
                    nameof(AfterSessionLaunchedMigrateLegacyClans));
                if (session == null || sessionPostfix == null)
                    throw new MissingMethodException(
                        "Campaign session callback was not found.");
                harmony.Patch(session, postfix: new HarmonyMethod(sessionPostfix)
                {
                    priority = Priority.Last
                });

                _sharedEncounterClanRuntimeInstalled = true;
                ModLog.AlwaysInfo(
                    "Installed shared encounter-clan runtime. New and migrated " +
                    "encounter heroes use one serialized faction graph.");
            }
            catch (Exception ex)
            {
                ModLog.Error("Shared encounter-clan runtime could not be installed: " +
                    ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static bool ReplaceEncounterOwnerClan(
            EncounterDefinition __0, Settlement __1, Hero __2,
            CharacterObject __3, Clan __4, ref Clan __result)
        {
            __result = ResolveOrCreateSharedEncounterClan(
                __1, __2, __3, __4);
            return false;
        }

        private static Clan ResolveOrCreateSharedEncounterClan(
            Settlement anchor, Hero leader, CharacterObject template,
            Clan nativeBanditClan)
        {
            Clan sharedClan = FindSharedEncounterClan();
            bool created = sharedClan == null;
            if (created)
            {
                sharedClan = Clan.CreateClan(SharedEncounterClanId);
                if (sharedClan == null || !String.Equals(sharedClan.StringId,
                    SharedEncounterClanId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Could not create the shared encounter clan.");
                }
            }

            TextObject name = new TextObject(
                "Independent Legendary Hosts", null);
            sharedClan.ChangeClanName(name, name);

            if (sharedClan.Culture == null)
            {
                if (template != null && template.Culture != null)
                    sharedClan.Culture = template.Culture;
                else if (leader != null && leader.CharacterObject != null &&
                    leader.CharacterObject.Culture != null)
                    sharedClan.Culture = leader.CharacterObject.Culture;
                else if (nativeBanditClan != null &&
                    nativeBanditClan.Culture != null)
                    sharedClan.Culture = nativeBanditClan.Culture;
            }

            if (nativeBanditClan != null)
            {
                if (sharedClan.BasicTroop == null &&
                    nativeBanditClan.BasicTroop != null)
                    sharedClan.BasicTroop = nativeBanditClan.BasicTroop;
                if (created)
                {
                    sharedClan.Color = nativeBanditClan.Color;
                    sharedClan.Color2 = nativeBanditClan.Color2;
                    if (nativeBanditClan.Banner != null)
                        sharedClan.Banner = nativeBanditClan.Banner;
                }
            }

            ReflectionUtil.SetProperty(sharedClan, "IsMinorFaction", true);
            ReflectionUtil.SetProperty(sharedClan, "IsOutlaw", true);
            ReflectionUtil.SetProperty(sharedClan, "IsBanditFaction", false);
            ReflectionUtil.SetProperty(sharedClan, "IsNoble", false);
            if (anchor != null && sharedClan.InitialHomeSettlement == null)
                sharedClan.SetInitialHomeSettlement(anchor);

            return sharedClan;
        }

        private static bool ReplaceEncounterHeroClanAttachment(
            Hero __0, Clan __1)
        {
            if (!IsSharedEncounterClan(__1))
                return true;

            EnsureHeroAttachedToSharedClan(__0, __1);
            return false;
        }

        private static void EnsureHeroAttachedToSharedClan(
            Hero hero, Clan sharedClan)
        {
            if (hero == null)
                throw new ArgumentNullException("hero");
            if (sharedClan == null)
                throw new ArgumentNullException("sharedClan");

            MobileParty attachedParty = hero.PartyBelongedTo;
            bool partyIsActive = attachedParty != null && attachedParty.IsActive;
            if (partyIsActive &&
                !Object.ReferenceEquals(attachedParty.ActualClan, sharedClan))
            {
                if (attachedParty.MapEvent != null)
                {
                    throw new InvalidOperationException(
                        "Shared encounter-party owner repair is deferred while " +
                        attachedParty.StringId + " is participating in a map event.");
                }
                attachedParty.ActualClan = sharedClan;
            }

            // SetLeader establishes the clan leader before Hero.Clan dispatches
            // OnHeroChangedClan. This preserves the native conversation invariant
            // even for the first hero moved into a newly created shared clan.
            sharedClan.SetLeader(hero);

            if (!Object.ReferenceEquals(hero.Clan, sharedClan))
                throw new InvalidOperationException(
                    "Encounter hero did not retain shared clan membership.");
            if (sharedClan.Leader == null ||
                !Object.ReferenceEquals(sharedClan.Leader.Clan, sharedClan))
                throw new InvalidOperationException(
                    "The shared encounter clan has no valid conversation leader.");
            if (partyIsActive &&
                !Object.ReferenceEquals(attachedParty.ActualClan, sharedClan))
                throw new InvalidOperationException(
                    "Encounter party did not retain shared clan ownership.");
        }

        private static void AfterSessionLaunchedMigrateLegacyClans(
            UniqueEncounterBehavior __instance)
        {
            if (__instance == null || !__instance._sessionReady)
                return;

            try
            {
                __instance.MigrateLegacyEncounterClansToSharedClan();
            }
            catch (Exception ex)
            {
                ModLog.Error("Legacy encounter-clan migration failed: " +
                    FormatException(ex));
            }
        }

        private void MigrateLegacyEncounterClansToSharedClan()
        {
            List<Clan> legacyClans = new List<Clan>();
            foreach (Clan clan in Clan.All)
            {
                if (IsLegacyEncounterClan(clan))
                    legacyClans.Add(clan);
            }
            if (legacyClans.Count == 0)
                return;

            Clan seed = legacyClans[0];
            Clan sharedClan = ResolveOrCreateSharedEncounterClan(
                seed.InitialHomeSettlement, seed.Leader,
                seed.Leader == null ? null : seed.Leader.CharacterObject,
                seed);

            int movedHeroes = 0;
            int movedParties = 0;
            HashSet<Clan> legacySet = new HashSet<Clan>(legacyClans);

            for (int i = 0; i < legacyClans.Count; i++)
            {
                List<Hero> members = new List<Hero>();
                foreach (Hero hero in legacyClans[i].Heroes)
                    if (hero != null)
                        members.Add(hero);

                for (int h = 0; h < members.Count; h++)
                {
                    Hero hero = members[h];
                    if (Object.ReferenceEquals(hero.Clan, Clan.PlayerClan) ||
                        Object.ReferenceEquals(hero.CompanionOf,
                            Clan.PlayerClan))
                        continue;
                    if (!legacySet.Contains(hero.Clan))
                        continue;
                    sharedClan.SetLeader(hero);
                    movedHeroes++;
                }
            }

            List<MobileParty> parties = new List<MobileParty>();
            foreach (MobileParty party in MobileParty.All)
                if (party != null)
                    parties.Add(party);
            for (int i = 0; i < parties.Count; i++)
            {
                MobileParty party = parties[i];
                if (!legacySet.Contains(party.ActualClan))
                    continue;
                if (party.MapEvent != null)
                {
                    throw new InvalidOperationException(
                        "Cannot migrate legacy encounter party " +
                        party.StringId + " during an active map event.");
                }
                party.ActualClan = sharedClan;
                movedParties++;
            }

            EnsureSharedClanLeader(sharedClan);

            int removedClans = 0;
            for (int i = 0; i < legacyClans.Count; i++)
            {
                Clan legacyClan = legacyClans[i];
                if (legacyClan.Heroes.Count != 0)
                {
                    throw new InvalidOperationException(
                        legacyClan.StringId + " still owns " +
                        legacyClan.Heroes.Count + " hero(es).");
                }
                if (HasPartyOwnedByClan(legacyClan))
                {
                    throw new InvalidOperationException(
                        legacyClan.StringId +
                        " still owns an active mobile party.");
                }
                if (legacyClan.Settlements.Count != 0)
                {
                    throw new InvalidOperationException(
                        legacyClan.StringId +
                        " unexpectedly owns a settlement.");
                }

                DestroyClanAction.Apply(legacyClan);
                removedClans++;
            }

            ModLog.AlwaysInfo("Collapsed " + removedClans +
                " legacy TORCU encounter clans into " +
                SharedEncounterClanId + "; moved heroes=" + movedHeroes +
                ", parties=" + movedParties +
                ". Save the campaign once. The current load already paid for the " +
                "legacy faction graph, but subsequent loads of the migrated save " +
                "will contain only the shared encounter clan.");
        }

        private void EnsureSharedClanLeader(Clan sharedClan)
        {
            Hero leader = sharedClan.Leader;
            if (IsValidSharedClanLeader(leader, sharedClan))
                return;

            Hero candidate = FindSharedLeaderCandidate(
                _encounterHeroes, sharedClan);
            if (candidate == null)
                candidate = FindSharedLeaderCandidate(
                    _successorHeroes, sharedClan);
            if (candidate == null)
            {
                foreach (Hero hero in sharedClan.Heroes)
                {
                    if (IsValidSharedClanLeader(hero, sharedClan))
                    {
                        candidate = hero;
                        break;
                    }
                }
            }

            if (candidate != null)
                sharedClan.SetLeader(candidate);
            if (sharedClan.Heroes.Count > 0 &&
                !IsValidSharedClanLeader(sharedClan.Leader, sharedClan))
            {
                throw new InvalidOperationException(
                    "Could not establish a valid leader for the shared " +
                    "encounter clan.");
            }
        }

        private static Hero FindSharedLeaderCandidate(
            Dictionary<string, Hero> heroes, Clan sharedClan)
        {
            if (heroes == null)
                return null;
            foreach (KeyValuePair<string, Hero> entry in heroes)
                if (IsValidSharedClanLeader(entry.Value, sharedClan))
                    return entry.Value;
            return null;
        }

        private static bool IsValidSharedClanLeader(
            Hero hero, Clan sharedClan)
        {
            return hero != null && !hero.IsDead &&
                Object.ReferenceEquals(hero.Clan, sharedClan) &&
                !Object.ReferenceEquals(hero.CompanionOf, Clan.PlayerClan);
        }

        private static bool HasPartyOwnedByClan(Clan clan)
        {
            foreach (MobileParty party in MobileParty.All)
                if (party != null &&
                    Object.ReferenceEquals(party.ActualClan, clan))
                    return true;
            return false;
        }

        private static Clan FindSharedEncounterClan()
        {
            foreach (Clan clan in Clan.All)
                if (IsSharedEncounterClan(clan))
                    return clan;
            return null;
        }

        private static bool IsSharedEncounterClan(Clan clan)
        {
            return clan != null && String.Equals(clan.StringId,
                SharedEncounterClanId, StringComparison.Ordinal);
        }

        private static bool IsLegacyEncounterClan(Clan clan)
        {
            return clan != null && !String.IsNullOrEmpty(clan.StringId) &&
                clan.StringId.StartsWith("torcu_faction_",
                    StringComparison.Ordinal) &&
                !IsSharedEncounterClan(clan);
        }
    }
}
