using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;

namespace TORCareerUniques
{
    internal sealed partial class UniqueEncounterBehavior
    {
        private const int CurrentEncounterHeroSchemaVersion = 5;

        private Dictionary<string, Hero> _encounterHeroes =
            new Dictionary<string, Hero>(StringComparer.Ordinal);
        private Dictionary<string, string> _pendingHeroRecoveries =
            new Dictionary<string, string>(StringComparer.Ordinal);

        private void RegisterEncounterHeroEvents()
        {
            CampaignEvents.CanHeroDieEvent.AddNonSerializedListener(this, OnCanEncounterHeroDie);
            CampaignEvents.CanHeroBecomePrisonerEvent.AddNonSerializedListener(this, OnCanEncounterHeroBecomePrisoner);
            CampaignEvents.HeroPrisonerTaken.AddNonSerializedListener(this, OnEncounterHeroPrisonerTaken);
            CampaignEvents.HeroPrisonerReleased.AddNonSerializedListener(this, OnEncounterHeroPrisonerReleased);
            CampaignEvents.HeroWounded.AddNonSerializedListener(this, OnEncounterHeroWounded);
        }

        private void ReconcileEncounterHeroes()
        {
            EnsureState();
            _pendingHeroRecoveries.Clear();
            List<string> invalid = new List<string>();
            foreach (KeyValuePair<string, Hero> entry in _encounterHeroes)
            {
                if (!EncounterCatalog.ByCareer.ContainsKey(entry.Key) || entry.Value == null)
                    invalid.Add(entry.Key);
            }
            for (int i = 0; i < invalid.Count; i++)
                _encounterHeroes.Remove(invalid[i]);

            invalid.Clear();
            foreach (KeyValuePair<string, Hero> entry in _successorHeroes)
            {
                if (!EncounterCatalog.ByCareer.ContainsKey(entry.Key) ||
                    entry.Value == null)
                    invalid.Add(entry.Key);
            }
            for (int i = 0; i < invalid.Count; i++)
                _successorHeroes.Remove(invalid[i]);

            Dictionary<string, Hero> activeHeroes =
                GetActiveEncounterHeroSnapshot();
            EncounterHeroDeathGuard.ClearAndRegister(activeHeroes.Values);
            foreach (KeyValuePair<string, Hero> entry in activeHeroes)
            {
                string auditError;
                EncounterDefinition auditDefinition;
                Clan expectedClan = null;
                if (EncounterCatalog.ByCareer.TryGetValue(entry.Key,
                    out auditDefinition))
                {
                    Settlement auditAnchor = ResolveAnchor(auditDefinition);
                    Clan nativeBanditClan = ResolveBanditClan(auditDefinition);
                    expectedClan = ResolveOrCreateEncounterOwnerClan(
                        auditDefinition, auditAnchor, entry.Value,
                        entry.Value.CharacterObject, nativeBanditClan);
                }
                if (!AuditPersistentHero(entry.Key, entry.Value, expectedClan,
                    out auditError))
                {
                    ModLog.Error("Persistent encounter-hero audit failed for " +
                        entry.Key + ": " + auditError);
                    continue;
                }

                EncounterDefinition definition;
                if (!entry.Value.IsDead && !entry.Value.IsPrisoner &&
                    entry.Value.PartyBelongedToAsPrisoner == null &&
                    entry.Value.PartyBelongedTo == null &&
                    EncounterCatalog.ByCareer.TryGetValue(entry.Key, out definition))
                {
                    PlaceEncounterHeroBetweenEncounters(entry.Key, entry.Value,
                        ResolveAnchor(definition), false);
                }
            }
        }

        private bool AuditPersistentHero(string careerId, Hero hero,
            Clan expectedClan, out string error)
        {
            error = null;
            try
            {
                EncounterHeroProfile profile = GetProfileForLeader(careerId, hero);
                if (profile == null)
                    throw new InvalidOperationException("No encounter-hero profile exists.");
                if (hero == null)
                    throw new InvalidOperationException("Saved hero reference is null.");
                if (hero.IsDead)
                    throw new InvalidOperationException("The persistent hero is marked dead.");

                EnsureEncounterHeroClan(hero, expectedClan);
                hero.SetNewOccupation(Occupation.Special);
                hero.HiddenInEncyclopedia = false;
                hero.IsKnownToPlayer = true;
                hero.Level = Math.Max(hero.Level, profile.Level);
                if (hero.HitPoints <= 0)
                    hero.HitPoints = 1;
                if (!String.Equals(hero.Name == null ? null : hero.Name.ToString(),
                    profile.FullName, StringComparison.Ordinal))
                    hero.SetName(new TextObject(profile.FullName, null),
                        new TextObject(profile.FirstName, null));
                RaiseHeroSkills(hero, profile);
                EncounterDefinition definition;
                CharacterObject capabilityTemplate =
                    EncounterCatalog.ByCareer.TryGetValue(careerId, out definition) ?
                    ResolveEncounterHeroTemplate(definition, profile) : null;
                EnsureTorCareerAndAbilities(hero, profile, capabilityTemplate, false);
                int unsafeTraitsRemoved =
                    SetItemRuntime.RemoveMissionUnsafeEncounterHeroTraits(hero);
                if (unsafeTraitsRemoved > 0)
                    ModLog.Info("Removed " + unsafeTraitsRemoved +
                        " mission-unsafe post-lethal revive trait carrier(s) from " +
                        hero.Name + ".");
                if (!SetItemRuntime.HasCompleteEncounterHeroSet(hero, careerId))
                {
                    string equipmentSummary;
                    string equipmentError;
                    if (!SetItemRuntime.TryEquipEncounterHero(hero, careerId,
                        profile.PreferMounted, out equipmentSummary, out equipmentError))
                        throw new InvalidOperationException("Persistent full-set repair failed: " +
                            equipmentError);
                    ModLog.Info("Repaired persistent encounter-hero equipment for " +
                        hero.Name + ": " + equipmentSummary + ".");
                }
                string equipmentAudit;
                if (!SetItemRuntime.ValidateEncounterHeroEquipment(hero, careerId,
                    profile.PreferMounted, out equipmentAudit))
                    throw new InvalidOperationException("Equipment audit failed: " +
                        equipmentAudit);
                return true;
            }
            catch (Exception ex)
            {
                error = FormatException(ex);
                return false;
            }
        }

        private Hero GetOrCreateEncounterHero(EncounterDefinition definition,
            Settlement anchor, Clan partyClan)
        {
            EnsureState();
            Hero hero;
            Dictionary<string, Hero> heroMap = IsOriginalRecruited(
                definition.CareerId) ? _successorHeroes : _encounterHeroes;
            if (heroMap.TryGetValue(definition.CareerId, out hero) && hero != null)
            {
                EncounterHeroDeathGuard.Register(hero);
                Clan existingOwnerClan = ResolveOrCreateEncounterOwnerClan(definition,
                    anchor, hero, hero.CharacterObject, partyClan);
                string auditError;
                if (!AuditPersistentHero(definition.CareerId, hero, existingOwnerClan,
                    out auditError))
                    throw new InvalidOperationException("Existing persistent hero failed validation: " +
                        auditError);
                return hero;
            }

            EncounterHeroProfile profile = EncounterHeroProfiles.Get(
                definition.CareerId);
            if (IsOriginalRecruited(definition.CareerId))
            {
                SuccessorIdentity successorIdentity =
                    EncounterSuccessorProfiles.Get(definition.CareerId);
                profile = profile == null || successorIdentity == null
                    ? profile : successorIdentity.ApplyTo(profile);
            }
            if (profile == null)
                throw new InvalidOperationException("No encounter-hero profile exists for " +
                    definition.CareerId + ".");

            CharacterObject template = ResolveEncounterHeroTemplate(definition, profile);
            if (template == null)
                throw new InvalidOperationException("No lore-compatible character template exists for " +
                    profile.FullName + " / " + definition.CareerId + ".");

            // Native bandit clans are spawn templates only. Persistent encounter heroes
            // belong to TORCU-owned independent minor clans with a real non-null leader.
            // Native lord-conversation tags dereference ConversationHero.Clan.Leader, so
            // assigning a hero directly to a leaderless native bandit shell violates a
            // Bannerlord conversation invariant and can crash while advancing dialogue.
            Clan ownerClan = ResolveOrCreateEncounterOwnerClan(definition, anchor,
                null, template, partyClan);
            if (ownerClan == null)
                throw new InvalidOperationException("No dedicated independent encounter clan exists for " +
                    profile.FullName + ".");
            hero = HeroCreator.CreateSpecialHero(template, anchor, ownerClan, null,
                profile.Age);
            if (hero == null)
                throw new InvalidOperationException("HeroCreator.CreateSpecialHero returned null for " +
                    profile.FullName + ".");

            // Register immediately. A later ToR or crafted-item failure must never leave
            // an untracked, mortal orphan hero in the campaign save.
            heroMap[definition.CareerId] = hero;
            EncounterHeroDeathGuard.Register(hero);
            try
            {
                // Establish both sides of the ownership invariant immediately after
                // creation, before any later setup can dispatch campaign events:
                // Hero.Clan == ownerClan and ownerClan.Leader == hero.
                EnsureEncounterHeroClan(hero, ownerClan);
                hero.SetName(new TextObject(profile.FullName, null),
                    new TextObject(profile.FirstName, null));
                hero.SetNewOccupation(Occupation.Special);
                hero.HiddenInEncyclopedia = false;
                hero.IsKnownToPlayer = true;
                hero.Level = profile.Level;
                hero.BornSettlement = anchor;
                hero.StayingInSettlement = definition.Kind ==
                    EncounterKind.RoamingHost ? anchor : null;
                hero.UpdateLastKnownClosestSettlement(anchor);
                RaiseHeroSkills(hero, profile);
                EnsureTorCareerAndAbilities(hero, profile, template, true);

                string equipmentSummary;
                string equipmentError;
                if (!SetItemRuntime.TryEquipEncounterHero(hero, definition.CareerId,
                    profile.PreferMounted, out equipmentSummary, out equipmentError))
                    throw new InvalidOperationException("Full-set equipment setup failed: " + equipmentError);
                string equipmentAudit;
                if (!SetItemRuntime.ValidateEncounterHeroEquipment(hero,
                    definition.CareerId, profile.PreferMounted, out equipmentAudit))
                    throw new InvalidOperationException("Final equipment audit failed: " +
                        equipmentAudit);

                hero.HitPoints = Math.Max(1, hero.MaxHitPoints);
                PlaceEncounterHeroBetweenEncounters(definition.CareerId, hero,
                    anchor, false);
                ModLog.Info("Created persistent encounter hero " + profile.FullName +
                    " [" + hero.StringId + "] for " + definition.MapName +
                    "; template=" + template.StringId + ", level=" + hero.Level +
                    ", equipment=" + equipmentSummary + "; audit=" +
                    equipmentAudit + ".");
                return hero;
            }
            catch
            {
                try
                {
                    hero.SetNewOccupation(Occupation.Special);
                    hero.HitPoints = Math.Max(1, hero.HitPoints);
                    PlaceEncounterHeroBetweenEncounters(definition.CareerId,
                        hero, anchor, false);
                }
                catch { }
                throw;
            }
        }

        private static Clan ResolveOrCreateEncounterOwnerClan(
            EncounterDefinition definition, Settlement anchor, Hero leader,
            CharacterObject template, Clan nativeBanditClan)
        {
            if (definition == null)
                throw new ArgumentNullException("definition");

            string clanId = "torcu_faction_" + Slug(definition.CareerId);
            Clan clan = null;
            foreach (Clan candidate in Clan.All)
            {
                if (candidate != null && String.Equals(candidate.StringId,
                    clanId, StringComparison.Ordinal))
                {
                    clan = candidate;
                    break;
                }
            }

            bool created = false;
            if (clan == null)
            {
                clan = Clan.CreateClan(clanId);
                created = true;
                if (clan == null || !String.Equals(clan.StringId, clanId,
                    StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        "Could not create deterministic encounter clan " + clanId + ".");
            }

            string displayName = GetEncounterFactionDisplayName(definition);
            TextObject name = new TextObject(displayName, null);
            clan.ChangeClanName(name, name);

            if (template != null && template.Culture != null)
                clan.Culture = template.Culture;
            else if (leader != null && leader.CharacterObject != null &&
                leader.CharacterObject.Culture != null)
                clan.Culture = leader.CharacterObject.Culture;
            else if (nativeBanditClan != null && nativeBanditClan.Culture != null)
                clan.Culture = nativeBanditClan.Culture;

            // Preserve native bandit creation data and harmless visual metadata while
            // keeping the custom clan independent from every kingdom. The native clan
            // remains only a spawn template; it is never exposed as the encounter
            // hero's actual faction after attachment/migration.
            if (nativeBanditClan != null)
            {
                if (nativeBanditClan.BasicTroop != null)
                    clan.BasicTroop = nativeBanditClan.BasicTroop;
                clan.Color = nativeBanditClan.Color;
                clan.Color2 = nativeBanditClan.Color2;
                if (nativeBanditClan.Banner != null)
                    clan.Banner = nativeBanditClan.Banner;
            }

            ReflectionUtil.SetProperty(clan, "IsMinorFaction", true);
            ReflectionUtil.SetProperty(clan, "IsOutlaw", true);
            // Do NOT classify runtime-created encounter clans as native bandit
            // factions. Bannerlord's BanditSpawnCampaignBehavior builds private
            // hideout dictionaries only for the native bandit factions that exist
            // during campaign initialization. Marking a later TORCU clan as
            // IsBanditFaction makes HourlyTickClan feed its culture into those
            // dictionaries and can throw KeyNotFoundException. Encounter parties
            // retain native bandit movement/attack semantics through their
            // EncounterBanditPartyComponent; the clan-level bandit flag is neither
            // required nor safe here.
            ReflectionUtil.SetProperty(clan, "IsBanditFaction", false);
            ReflectionUtil.SetProperty(clan, "IsNoble", false);
            if (anchor != null && (created || clan.InitialHomeSettlement == null))
                clan.SetInitialHomeSettlement(anchor);

            return clan;
        }

        private static bool IsDedicatedEncounterOwnerClan(Clan clan)
        {
            return clan != null && !String.IsNullOrEmpty(clan.StringId) &&
                clan.StringId.StartsWith("torcu_faction_",
                    StringComparison.Ordinal);
        }

        private static int NormalizeDedicatedEncounterClanClassification()
        {
            int repaired = 0;
            foreach (Clan clan in Clan.All)
            {
                if (!IsDedicatedEncounterOwnerClan(clan))
                    continue;
                if (!ReflectionUtil.ToBool(ReflectionUtil.GetProperty(clan,
                    "IsBanditFaction")))
                    continue;

                ReflectionUtil.SetProperty(clan, "IsBanditFaction", false);
                repaired++;
            }

            if (repaired > 0)
                ModLog.Info("Cleared unsafe native-bandit classification from " +
                    repaired + " dedicated encounter clan(s).");
            return repaired;
        }

        private static string GetEncounterFactionDisplayName(
            EncounterDefinition definition)
        {
            if (definition == null)
                return "Independent Encounter Host";
            switch (definition.CareerId)
            {
                case "GrailDamsel": return "The Blighted Grail Custodians";
                case "GrailKnight": return "The Black Grail Procession";
                case "MinorVampire": return "The Red Duke's Sepulchral Court";
                case "WarriorPriest": return "The Purple Hand Purge";
                case "BloodKnight": return "The Crimson Errantry";
                case "Mercenary": return "The Border Princes' Black Company";
                case "WitchHunter": return "The Ashen Tribunal";
                case "Necromancer": return "The Restless Host";
                case "BlackGrailKnight": return "The Black Grail Reliquary Guard";
                case "Necrarch": return "The Necrarch Ossuary";
                case "WarriorPriestUlric": return "The White Wolf Hunt";
                case "ImperialMagister": return "Volker's Collegiate Retinue";
                case "Waywatcher": return "Beast-Hunters of Athel Loren";
                case "Spellsinger": return "Wardens of the Defiled Waystone";
                case "Warden": return "The Hunted of Athel Loren";
                case "GreyLord": return "Veyl's Grey College Agents";
                case "KnightOldWorld": return "The Black Road Brotherhood";
                case "Ironbreaker": return "The Underhold Survivors";
                case "Slayer": return "The Troll King's Hunters";
                case "Runelord": return "Embermark's Rune-Guard";
                case "OrcBoss": return "Morglug's Waaagh!";
                case "OrcShaman": return "The Moon-Idol Shamans";
                default: return definition.MapName ?? "Independent Encounter Host";
            }
        }

        private static void EnsureEncounterHeroClan(Hero hero, Clan expectedClan)
        {
            if (hero == null)
                throw new ArgumentNullException("hero");
            if (expectedClan == null)
                throw new InvalidOperationException(
                    "The encounter's dedicated independent clan is unavailable.");

            MobileParty attachedParty = hero.PartyBelongedTo;
            bool partyIsActive = attachedParty != null && attachedParty.IsActive;
            bool needsClanChange = !Object.ReferenceEquals(hero.Clan, expectedClan);
            bool needsLeaderRepair = !Object.ReferenceEquals(expectedClan.Leader, hero);
            bool needsPartyOwnerRepair = partyIsActive &&
                !Object.ReferenceEquals(attachedParty.ActualClan, expectedClan);

            if (partyIsActive && needsPartyOwnerRepair)
            {
                if (attachedParty.MapEvent != null)
                    throw new InvalidOperationException(
                        "Encounter-party owner repair is deferred while " +
                        attachedParty.StringId + " is participating in a map event.");

                // Repair stale party ownership even when Hero.Clan and Clan.Leader
                // were already migrated. v1.7.14/15 only entered this owner-sync path
                // when one of those two hero/clan values also needed changing, so a
                // pre-existing party could keep displaying its borrowed native clan.
                attachedParty.ActualClan = expectedClan;
                ModLog.Info("Rebound encounter party " + attachedParty.StringId +
                    " from its borrowed native clan to " +
                    expectedClan.StringId + ".");
            }

            if (needsClanChange || needsLeaderRepair)
            {
                // Clan.SetLeader assigns the leader field before Hero.Clan dispatches
                // OnHeroChangedClan. Any active party owner was synchronized first so
                // synchronous listeners see consistent ownership and leadership.
                Clan previous = hero.Clan;
                expectedClan.SetLeader(hero);
                ModLog.Info("Assigned encounter hero " + hero.Name +
                    " as leader of dedicated clan " + expectedClan.StringId +
                    (previous == null ? "." :
                    " (replacing " + previous.StringId + ")."));
            }

            if (!Object.ReferenceEquals(hero.Clan, expectedClan))
                throw new InvalidOperationException(
                    "Hero clan assignment did not retain " +
                    expectedClan.StringId + " for " + hero.Name + ".");
            if (!Object.ReferenceEquals(expectedClan.Leader, hero))
                throw new InvalidOperationException(
                    "Encounter clan leader assignment did not retain " + hero.Name +
                    " for " + expectedClan.StringId + ".");
            if (partyIsActive &&
                !Object.ReferenceEquals(attachedParty.ActualClan, expectedClan))
                throw new InvalidOperationException(
                    "Encounter party ownership did not retain " +
                    expectedClan.StringId + " for " + attachedParty.StringId + ".");
        }

        private CharacterObject ResolveEncounterHeroTemplate(EncounterDefinition definition,
            EncounterHeroProfile profile)
        {
            CharacterObject strictBest = null;
            CharacterObject fallbackBest = null;
            int strictBestScore = Int32.MinValue;
            int fallbackBestScore = Int32.MinValue;
            foreach (CharacterObject candidate in CharacterObject.All)
            {
                if (candidate == null || candidate.IsChildTemplate ||
                    candidate.FirstBattleEquipment == null)
                    continue;
                if (candidate.HeroObject != null && !candidate.IsTemplate)
                    continue;
                if (profile.RequireMounted && !candidate.IsMounted)
                    continue;

                string text = ReflectionUtil.SearchText(candidate);
                int requiredMatches = CountTokenMatches(text,
                    profile.RequiredTemplateTokens);
                int profileMatches = CountTokenMatches(text,
                    profile.TemplateTokens);
                int regionMatches = CountTokenMatches(text, definition.RegionTokens);
                int lootMatches = CountTokenMatches(text, definition.LootTokens);
                int negativeMatches = CountTokenMatches(text,
                    profile.NegativeTemplateTokens);

                int score = requiredMatches * 5000 + profileMatches * 900;
                score += regionMatches * 350;
                score += lootMatches * 120;
                score += Math.Max(0, candidate.Level) * 35;
                score += Math.Max(0, candidate.Tier) * 260;
                if (candidate.IsTemplate)
                    score += 500;
                if (candidate.Occupation == Occupation.Wanderer)
                    score += 900;
                if (candidate.IsMounted == profile.PreferMounted)
                    score += 650;
                score -= negativeMatches * 1500;

                bool strict = profile.RequiredTemplateTokens == null ||
                    profile.RequiredTemplateTokens.Length == 0 ||
                    requiredMatches > 0;
                if (strict && score > strictBestScore)
                {
                    strictBest = candidate;
                    strictBestScore = score;
                }

                // A naming change in ToR must not erase an encounter. The fallback
                // still requires positive career/culture/region evidence and rejects
                // candidates dominated by explicit negative archetype tokens.
                bool loreFallback = (profileMatches > 0 || regionMatches > 0) &&
                    negativeMatches == 0;
                if (loreFallback && score > fallbackBestScore)
                {
                    fallbackBest = candidate;
                    fallbackBestScore = score;
                }
            }

            CharacterObject best = strictBest ?? fallbackBest;
            int bestScore = strictBest != null ? strictBestScore : fallbackBestScore;
            if (best != null)
            {
                if (strictBest == null)
                    ModLog.Error("No exact template-token match existed for " +
                        definition.CareerId + "; using lore-filtered fallback " +
                        best.StringId + ".");
                ModLog.Info("Resolved hero template for " + definition.CareerId +
                    ": " + best.Name + " [" + best.StringId + ", level " +
                    best.Level + ", tier " + best.Tier + ", mounted=" +
                    best.IsMounted + ", score " + bestScore + "].");
            }
            return best;
        }

        private static int CountTokenMatches(string text, string[] tokens)
        {
            if (String.IsNullOrEmpty(text) || tokens == null)
                return 0;
            int result = 0;
            for (int i = 0; i < tokens.Length; i++)
                if (!String.IsNullOrEmpty(tokens[i]) &&
                    text.IndexOf(tokens[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    result++;
            return result;
        }

        private static void RaiseHeroSkills(Hero hero, EncounterHeroProfile profile)
        {
            Type defaultSkills = ReflectionUtil.TypeByName("TaleWorlds.Core.DefaultSkills");
            if (defaultSkills == null)
                return;
            MethodInfo setSkill = hero.GetType().GetMethod("SetSkillValue",
                BindingFlags.Public | BindingFlags.Instance);
            MethodInfo getSkill = hero.GetType().GetMethod("GetSkillValue",
                BindingFlags.Public | BindingFlags.Instance);
            if (setSkill == null)
                return;

            PropertyInfo[] properties = defaultSkills.GetProperties(
                BindingFlags.Public | BindingFlags.Static);
            for (int i = 0; i < properties.Length; i++)
            {
                object skill = properties[i].GetValue(null, null);
                if (skill == null)
                    continue;
                string id = Convert.ToString(ReflectionUtil.GetProperty(skill, "StringId")) ??
                    properties[i].Name;
                int value = 150;
                if (ContainsToken(id, profile.PrimarySkillTokens))
                    value = 280;
                else if (ContainsToken(id, profile.SecondarySkillTokens))
                    value = 230;
                else if (String.Equals(id, profile.PreferMounted ? "Riding" : "Athletics",
                    StringComparison.OrdinalIgnoreCase))
                    value = 240;
                if (getSkill != null)
                {
                    int current = Convert.ToInt32(getSkill.Invoke(hero,
                        new object[] { skill }));
                    value = Math.Max(value, current);
                }
                setSkill.Invoke(hero, new object[] { skill, value });
            }
        }

        private static bool ContainsToken(string text, string[] tokens)
        {
            if (String.IsNullOrEmpty(text) || tokens == null)
                return false;
            for (int i = 0; i < tokens.Length; i++)
                if (!String.IsNullOrEmpty(tokens[i]) &&
                    text.IndexOf(tokens[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        private static void EnsureTorCareerAndAbilities(Hero hero,
            EncounterHeroProfile profile, CharacterObject capabilityTemplate,
            bool forceRebuild)
        {
            Type heroExtensions = ReflectionUtil.TypeByName("TOR_Core.Extensions.HeroExtensions");
            Type careersType = ReflectionUtil.TypeByName("TOR_Core.CharacterDevelopment.TORCareers");
            if (heroExtensions == null || careersType == null)
                throw new InvalidOperationException("TOR career APIs are not loaded.");

            object career = ResolveTorCareer(careersType, profile.CareerId);
            if (career == null)
                throw new InvalidOperationException("TOR career object '" + profile.CareerId + "' is unavailable.");

            // TOR's public AddCareer method invokes InitialCareerSetup implementations
            // that assume Hero.MainHero and can mutate the player's religion/resources.
            // Encounter heroes therefore receive the exact same persistent career record
            // directly on their own HeroExtendedInfo, then use TOR's hero-safe choice and
            // ability APIs for the remainder of initialization.
            object info = EnsureTorExtendedInfo(hero, heroExtensions);
            AssignTorCareerRecord(hero, info, career, profile.CareerId,
                heroExtensions, forceRebuild);

            hero.Level = Math.Max(hero.Level, profile.Level);
            // Predicates for later career groups can depend on template attributes,
            // lores, or abilities. Populate those target-hero capabilities before
            // resolving the branch, then select the complete eligible path without
            // invoking TOR's MainHero-only tier-unlock implementation.
            CopyTemplateTorCapabilities(hero, capabilityTemplate ??
                hero.CharacterObject, heroExtensions);
            if (capabilityTemplate != null &&
                !Object.ReferenceEquals(capabilityTemplate, hero.CharacterObject))
                CopyTemplateTorCapabilities(hero, hero.CharacterObject, heroExtensions);
            AddProfileAbilities(hero, profile, heroExtensions);
            ApplyTargetSafeCareerTierBenefits(hero, profile, heroExtensions);
            FillValidCareerPath(hero, career, profile);
            VerifyTorCareer(hero, profile, career, heroExtensions);
        }

        private static object EnsureTorExtendedInfo(Hero hero, Type heroExtensions)
        {
            MethodInfo getInfo = FindMethod(heroExtensions, "GetExtendedInfo", 1);
            object info = getInfo == null ? null : getInfo.Invoke(null,
                new object[] { hero });
            if (info != null)
                return info;

            Type managerType = ReflectionUtil.TypeByName(
                "TOR_Core.Extensions.ExtendedInfoSystem.ExtendedInfoManager");
            object manager = managerType == null ? null :
                ReflectionUtil.GetStaticProperty(managerType, "Instance");
            if (manager == null)
                throw new InvalidOperationException("TOR ExtendedInfoManager is unavailable.");

            MethodInfo getHeroInfo = managerType.GetMethod("GetHeroInfoFor",
                BindingFlags.Public | BindingFlags.Instance, null,
                new[] { typeof(string) }, null);
            info = getHeroInfo == null ? null : getHeroInfo.Invoke(manager,
                new object[] { hero.StringId });
            if (info == null)
            {
                MethodInfo onHeroCreated = managerType.GetMethod("OnHeroCreated",
                    BindingFlags.NonPublic | BindingFlags.Instance, null,
                    new[] { typeof(Hero), typeof(bool) }, null);
                if (onHeroCreated == null)
                    throw new MissingMethodException(managerType.FullName,
                        "OnHeroCreated(Hero, bool)");
                onHeroCreated.Invoke(manager, new object[] { hero, false });
                info = getHeroInfo == null ? null : getHeroInfo.Invoke(manager,
                    new object[] { hero.StringId });
            }
            if (info == null && getInfo != null)
                info = getInfo.Invoke(null, new object[] { hero });
            if (info == null)
                throw new InvalidOperationException("TOR did not create isolated extended info for " +
                    hero.Name + ".");
            return info;
        }

        private static void AssignTorCareerRecord(Hero hero, object info,
            object career, string careerId, Type heroExtensions, bool forceRebuild)
        {
            string currentId = Convert.ToString(GetMemberValue(info, "CareerID"));
            IList choices = GetMemberValue(info, "CareerChoices") as IList;
            if (choices == null)
            {
                choices = new List<string>();
                SetMemberValue(info, "CareerChoices", choices);
            }

            bool careerChanged = forceRebuild || !String.Equals(currentId, careerId,
                StringComparison.OrdinalIgnoreCase);
            if (careerChanged)
            {
                choices.Clear();
                MethodInfo removeAttribute = FindMethod(heroExtensions,
                    "RemoveAttribute", 2);
                if (removeAttribute != null)
                {
                    string[] tierAttributes = { "CareerTier1", "CareerTier2", "CareerTier3" };
                    for (int i = 0; i < tierAttributes.Length; i++)
                        removeAttribute.Invoke(null, new object[] { hero, tierAttributes[i] });
                }
                SetMemberValue(info, "CareerID", careerId);
            }

            object root = ReflectionUtil.GetProperty(career, "RootNode");
            string rootId = Convert.ToString(ReflectionUtil.GetProperty(root, "StringId"));
            if (String.IsNullOrEmpty(rootId))
                throw new InvalidOperationException("TOR career " + careerId +
                    " has no root choice id.");
            AddUniqueString(choices, rootId);

            MethodInfo addAttribute = FindMethod(heroExtensions, "AddAttribute", 2);
            if (addAttribute == null)
                throw new MissingMethodException(heroExtensions.FullName,
                    "AddAttribute(Hero, string)");
            string[] fullCareerTiers = { "CareerTier1", "CareerTier2", "CareerTier3" };
            for (int i = 0; i < fullCareerTiers.Length; i++)
                addAttribute.Invoke(null, new object[] { hero, fullCareerTiers[i] });

            string abilityId = Convert.ToString(
                ReflectionUtil.GetProperty(career, "AbilityTemplateID"));
            MethodInfo addAbility = FindMethod(heroExtensions, "AddAbility", 2);
            if (!String.IsNullOrEmpty(abilityId) && addAbility != null)
                addAbility.Invoke(null, new object[] { hero, abilityId });

            string assignedId = Convert.ToString(GetMemberValue(info, "CareerID"));
            if (!String.Equals(assignedId, careerId,
                StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("TOR career record did not retain " +
                    careerId + " for " + hero.Name + ".");
        }

        private static object GetMemberValue(object instance, string name)
        {
            if (instance == null)
                return null;
            Type type = instance.GetType();
            FieldInfo field = type.GetField(name, BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null)
                return field.GetValue(instance);
            PropertyInfo property = type.GetProperty(name, BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.Instance);
            return property == null ? null : property.GetValue(instance, null);
        }

        private static void SetMemberValue(object instance, string name, object value)
        {
            if (instance == null)
                throw new ArgumentNullException("instance");
            Type type = instance.GetType();
            FieldInfo field = type.GetField(name, BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null)
            {
                field.SetValue(instance, value);
                return;
            }
            PropertyInfo property = type.GetProperty(name, BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (property != null && property.CanWrite)
            {
                property.SetValue(instance, value, null);
                return;
            }
            throw new MissingMemberException(type.FullName, name);
        }

        private static void AddUniqueString(IList values, string value)
        {
            if (values == null || String.IsNullOrEmpty(value))
                return;
            for (int i = 0; i < values.Count; i++)
                if (String.Equals(Convert.ToString(values[i]), value,
                    StringComparison.Ordinal))
                    return;
            values.Add(value);
        }

        private static object ResolveTorCareer(Type careersType, string careerId)
        {
            PropertyInfo property = careersType.GetProperty(careerId,
                BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase);
            if (property != null)
                return property.GetValue(null, null);
            object all = ReflectionUtil.GetStaticProperty(careersType, "All");
            IEnumerable values = all as IEnumerable;
            if (values != null)
                foreach (object career in values)
                    if (String.Equals(Convert.ToString(
                        ReflectionUtil.GetProperty(career, "StringId")), careerId,
                        StringComparison.OrdinalIgnoreCase))
                        return career;
            return null;
        }

        private static void FillValidCareerPath(Hero hero, object career,
            EncounterHeroProfile profile)
        {
            Type heroExtensions = ReflectionUtil.TypeByName("TOR_Core.Extensions.HeroExtensions");
            MethodInfo tryAdd = FindMethod(heroExtensions, "TryAddCareerChoice", 2);
            MethodInfo hasChoice = FindMethod(heroExtensions, "HasCareerChoice", 2,
                delegate(ParameterInfo[] p) { return p[1].ParameterType != typeof(string); });
            if (tryAdd == null || hasChoice == null)
                throw new MissingMethodException(heroExtensions.FullName,
                    "TryAddCareerChoice/HasCareerChoice");

            IList groups = ReflectionUtil.GetProperty(career, "ChoiceGroups") as IList;
            if (groups == null)
                return;
            List<object> ordered = new List<object>();
            for (int i = 0; i < groups.Count; i++)
                if (groups[i] != null)
                    ordered.Add(groups[i]);
            ordered.Sort(delegate(object a, object b)
            {
                return ReflectionUtil.ToInt(ReflectionUtil.GetProperty(a, "Tier")).CompareTo(
                    ReflectionUtil.ToInt(ReflectionUtil.GetProperty(b, "Tier")));
            });

            int passes = 0;
            bool progress = true;
            while (progress && passes++ < 16)
            {
                progress = false;
                for (int g = 0; g < ordered.Count; g++)
                {
                    object group = ordered[g];
                    string eligibilityText;
                    if (!IsCareerGroupEligibleForHero(group, hero, out eligibilityText))
                        continue;
                    IList choices = ReflectionUtil.GetProperty(group, "Choices") as IList;
                    if (choices == null || choices.Count == 0)
                        continue;

                    bool already = false;
                    for (int c = 0; c < choices.Count; c++)
                        if (Convert.ToBoolean(hasChoice.Invoke(null,
                            new object[] { hero, choices[c] })))
                        {
                            already = true;
                            break;
                        }
                    if (already)
                        continue;

                    object selected = SelectCareerChoice(choices, profile);
                    if (selected != null && Convert.ToBoolean(tryAdd.Invoke(null,
                        new object[] { hero, selected })))
                    {
                        progress = true;
                        ModLog.Verbose("Encounter hero " + hero.Name +
                            " selected career choice " +
                            Convert.ToString(ReflectionUtil.GetProperty(selected, "StringId")) +
                            " (tier " + ReflectionUtil.ToInt(
                                ReflectionUtil.GetProperty(group, "Tier")) + ").");
                    }
                }
            }

            List<string> unresolved = new List<string>();
            for (int g = 0; g < ordered.Count; g++)
            {
                object group = ordered[g];
                string eligibilityText;
                if (!IsCareerGroupEligibleForHero(group, hero, out eligibilityText))
                    continue;
                IList choices = ReflectionUtil.GetProperty(group, "Choices") as IList;
                bool selected = false;
                if (choices != null)
                    for (int c = 0; c < choices.Count; c++)
                        if (Convert.ToBoolean(hasChoice.Invoke(null,
                            new object[] { hero, choices[c] })))
                        {
                            selected = true;
                            break;
                        }
                if (!selected)
                    unresolved.Add(DescribeCareerGroup(group) +
                        (String.IsNullOrEmpty(eligibilityText) ? String.Empty :
                            " [" + eligibilityText + "]"));
            }
            if (unresolved.Count > 0)
                throw new InvalidOperationException("Career path has unresolved eligible groups: " +
                    String.Join(", ", unresolved.ToArray()) + ".");
        }

        private static bool IsCareerGroupEligibleForHero(object group, Hero hero,
            out string text)
        {
            text = null;
            if (group == null || hero == null)
                return false;

            int tier = ReflectionUtil.ToInt(
                ReflectionUtil.GetProperty(group, "Tier"));
            if (tier < 1 || tier > 3)
            {
                text = "unsupported career tier " + tier;
                return false;
            }

            // TOR 1.16's career-group delegates are progression gates only:
            // constant-true, clan-tier, or explicit career-unlock attributes.
            // Encounter heroes are initialized as completed tier-3 careers, so
            // invoking those player progression gates is unnecessary and unsafe.
            // Every authored group through tier 3 is available; branch-token
            // scoring still selects one choice within each group.
            text = "encounter hero completed career tier " + tier;
            return true;
        }

        private static string DescribeCareerGroup(object group)
        {
            string id = Convert.ToString(ReflectionUtil.GetProperty(group, "StringId"));
            return String.IsNullOrEmpty(id) ?
                "tier " + ReflectionUtil.ToInt(ReflectionUtil.GetProperty(group, "Tier")) : id;
        }

        private static object SelectCareerChoice(IList choices,
            EncounterHeroProfile profile)
        {
            object best = null;
            int bestScore = Int32.MinValue;
            for (int i = 0; i < choices.Count; i++)
            {
                object choice = choices[i];
                string text = (Convert.ToString(ReflectionUtil.GetProperty(choice, "StringId")) +
                    " " + Convert.ToString(choice)).ToLowerInvariant();
                int score = CountTokenMatches(text, profile.BranchTokens) * 1000;
                string type = Convert.ToString(ReflectionUtil.GetProperty(choice, "Type"));
                if (String.Equals(type, "Keystone", StringComparison.OrdinalIgnoreCase))
                    score += 300;
                score -= i;
                if (score > bestScore)
                {
                    best = choice;
                    bestScore = score;
                }
            }
            return best;
        }

        private static void CopyTemplateTorCapabilities(Hero hero,
            CharacterObject template, Type heroExtensions)
        {
            Type characterExtensions = ReflectionUtil.TypeByName(
                "TOR_Core.Extensions.CharacterObjectExtensions");
            if (characterExtensions == null || template == null)
                return;
            MethodInfo getAbilities = FindMethod(characterExtensions, "GetAbilities", 1);
            MethodInfo getAttributes = FindMethod(characterExtensions, "GetAttributes", 1);
            MethodInfo addAbility = FindMethod(heroExtensions, "AddAbility", 2);
            MethodInfo addAttribute = FindMethod(heroExtensions, "AddAttribute", 2);
            CopyStringValues(getAbilities, template, addAbility, hero);
            CopyStringValues(getAttributes, template, addAttribute, hero);
        }

        private static void CopyStringValues(MethodInfo getter, object source,
            MethodInfo adder, Hero hero)
        {
            if (getter == null || adder == null)
                return;
            IEnumerable values = getter.Invoke(null, new object[] { source }) as IEnumerable;
            if (values == null)
                return;
            foreach (object value in values)
            {
                string id = Convert.ToString(value);
                if (!String.IsNullOrEmpty(id))
                    adder.Invoke(null, new object[] { hero, id });
            }
        }

        private static void AddProfileAbilities(Hero hero,
            EncounterHeroProfile profile, Type heroExtensions)
        {
            if (!profile.IsCaster)
                return;
            Type abilityFactory = ReflectionUtil.TypeByName("TOR_Core.AbilitySystem.AbilityFactory");
            MethodInfo getAll = abilityFactory == null ? null :
                abilityFactory.GetMethod("GetAllTemplates", BindingFlags.Public | BindingFlags.Static);
            MethodInfo addAbility = FindMethod(heroExtensions, "AddAbility", 2);
            MethodInfo addLore = FindMethod(heroExtensions, "AddKnownLore", 2);
            MethodInfo setCasting = FindMethod(heroExtensions, "SetSpellCastingLevel", 2);
            if (getAll == null || addAbility == null)
                return;

            List<AbilityCandidate> candidates = new List<AbilityCandidate>();
            IEnumerable templates = getAll.Invoke(null, null) as IEnumerable;
            if (templates != null)
                foreach (object template in templates)
                {
                    if (template == null || !ReflectionUtil.ToBool(
                        ReflectionUtil.GetProperty(template, "IsSpell")))
                        continue;
                    string id = Convert.ToString(ReflectionUtil.GetProperty(template, "StringID"));
                    if (String.IsNullOrEmpty(id))
                        id = Convert.ToString(ReflectionUtil.GetProperty(template, "StringId"));
                    string lore = Convert.ToString(ReflectionUtil.GetProperty(template,
                        "BelongsToLoreID")) ?? String.Empty;
                    string text = (id + " " + lore + " " +
                        Convert.ToString(ReflectionUtil.GetProperty(template, "Name"))).ToLowerInvariant();
                    int tokenMatches = CountTokenMatches(text, profile.AbilityTokens);
                    if (tokenMatches <= 0)
                        continue;
                    candidates.Add(new AbilityCandidate
                    {
                        Id = id,
                        LoreId = lore,
                        Tier = ReflectionUtil.ToInt(ReflectionUtil.GetProperty(template, "SpellTier")),
                        Score = tokenMatches * 1000 +
                            ReflectionUtil.ToInt(ReflectionUtil.GetProperty(template, "SpellTier")) * 100
                    });
                }
            candidates.Sort(delegate(AbilityCandidate a, AbilityCandidate b)
            {
                int score = b.Score.CompareTo(a.Score);
                return score != 0 ? score : String.Compare(a.Id, b.Id,
                    StringComparison.OrdinalIgnoreCase);
            });

            object info = null;
            MethodInfo getInfo = FindMethod(heroExtensions, "GetExtendedInfo", 1);
            if (getInfo != null)
                info = getInfo.Invoke(null, new object[] { hero });
            MethodInfo select = info == null ? null : info.GetType().GetMethod(
                "AddSelectedAbility", BindingFlags.Public | BindingFlags.Instance);

            HashSet<string> seenLores = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int granted = 0;
            for (int i = 0; i < candidates.Count && granted < profile.MaxSelectedSpells; i++)
            {
                AbilityCandidate candidate = candidates[i];
                addAbility.Invoke(null, new object[] { hero, candidate.Id });
                if (!String.IsNullOrEmpty(candidate.LoreId) && seenLores.Add(candidate.LoreId) &&
                    addLore != null)
                    addLore.Invoke(null, new object[] { hero, candidate.LoreId });
                if (select != null)
                    select.Invoke(info, new object[] { candidate.Id });
                granted++;
            }

            if (setCasting != null)
            {
                Type enumType = setCasting.GetParameters()[1].ParameterType;
                object master = Enum.ToObject(enumType, 4);
                setCasting.Invoke(null, new object[] { hero, master });
            }
            ModLog.Info("Configured caster loadout for " + hero.Name + ": " +
                granted + " selected matching spells, master spellcasting level.");
        }

        private static void ApplyTargetSafeCareerTierBenefits(Hero hero,
            EncounterHeroProfile profile, Type heroExtensions)
        {
            MethodInfo addAttribute = FindMethod(heroExtensions, "AddAttribute", 2);
            MethodInfo addLore = FindMethod(heroExtensions, "AddKnownLore", 2);
            MethodInfo hasLore = FindMethod(heroExtensions, "HasKnownLore", 2);

            // Only three encounter careers override TOR's generic tier-benefit hooks
            // with meaningful effects in TOR WiTM 1.16. Reproduce those effects on
            // the encounter hero itself instead of calling the hooks, which target
            // Hero.MainHero internally.
            if (String.Equals(profile.CareerId, "Runelord",
                StringComparison.OrdinalIgnoreCase))
            {
                if (addAttribute == null)
                    throw new MissingMethodException(heroExtensions.FullName,
                        "AddAttribute(Hero, string)");
                addAttribute.Invoke(null, new object[] { hero, "Spellcaster" });
            }
            else if (String.Equals(profile.CareerId, "GreyLord",
                StringComparison.OrdinalIgnoreCase))
            {
                if (addLore == null)
                    throw new MissingMethodException(heroExtensions.FullName,
                        "AddKnownLore(Hero, string)");
                addLore.Invoke(null, new object[] { hero, "DarkMagic" });
            }
            else if (String.Equals(profile.CareerId, "GrailDamsel",
                StringComparison.OrdinalIgnoreCase))
            {
                if (addLore == null || addAttribute == null)
                    throw new MissingMethodException(heroExtensions.FullName,
                        "AddKnownLore/AddAttribute");
                bool knowsLife = hasLore != null && Convert.ToBoolean(
                    hasLore.Invoke(null, new object[] { hero, "LoreOfLife" }));
                bool knowsBeasts = hasLore != null && Convert.ToBoolean(
                    hasLore.Invoke(null, new object[] { hero, "LoreOfBeasts" }));
                if (knowsLife || !knowsBeasts)
                    addLore.Invoke(null, new object[] { hero, "LoreOfBeasts" });
                if (knowsBeasts || !knowsLife)
                    addLore.Invoke(null, new object[] { hero, "LoreOfLife" });
                addLore.Invoke(null, new object[] { hero, "LoreOfHeavens" });
                addAttribute.Invoke(null,
                    new object[] { hero, "SecondLoreForDamselCompanions" });
            }
        }

        private static void VerifyTorCareer(Hero hero,
            EncounterHeroProfile profile, object career, Type heroExtensions)
        {
            MethodInfo getCareer = FindMethod(heroExtensions, "GetCareer", 1);
            object resolvedCareer = getCareer == null ? null :
                getCareer.Invoke(null, new object[] { hero });
            string id = Convert.ToString(ReflectionUtil.GetProperty(resolvedCareer, "StringId"));
            if (!String.Equals(id, profile.CareerId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Career verification resolved '" + id +
                    "' instead of '" + profile.CareerId + "'.");

            MethodInfo hasAttribute = FindMethod(heroExtensions, "HasAttribute", 2);
            if (hasAttribute == null)
                throw new MissingMethodException(heroExtensions.FullName,
                    "HasAttribute(Hero, string)");
            string[] tierAttributes = { "CareerTier1", "CareerTier2", "CareerTier3" };
            for (int i = 0; i < tierAttributes.Length; i++)
                if (!Convert.ToBoolean(hasAttribute.Invoke(null,
                    new object[] { hero, tierAttributes[i] })))
                    throw new InvalidOperationException("Career verification is missing " +
                        tierAttributes[i] + ".");

            MethodInfo getChoices = FindMethod(heroExtensions, "GetAllCareerChoices", 1);
            MethodInfo hasChoice = FindMethod(heroExtensions, "HasCareerChoice", 2,
                delegate(ParameterInfo[] p) { return p[1].ParameterType != typeof(string); });
            IList choices = getChoices == null ? null :
                getChoices.Invoke(null, new object[] { hero }) as IList;
            if (choices == null || choices.Count < 2 || hasChoice == null)
                throw new InvalidOperationException("Career verification found no leveled path choices.");

            HashSet<string> saved = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < choices.Count; i++)
                saved.Add(Convert.ToString(choices[i]));
            bool careerHasKeystone = false;
            bool selectedKeystone = false;
            int eligibleGroups = 0;
            int selectedEligibleGroups = 0;
            List<string> missingGroups = new List<string>();
            IList groups = ReflectionUtil.GetProperty(career, "ChoiceGroups") as IList;
            if (groups != null)
                for (int g = 0; g < groups.Count; g++)
                {
                    object group = groups[g];
                    IList groupChoices = ReflectionUtil.GetProperty(group, "Choices") as IList;
                    bool groupSelected = false;
                    if (groupChoices != null)
                        for (int c = 0; c < groupChoices.Count; c++)
                        {
                            object choice = groupChoices[c];
                            if (Convert.ToBoolean(hasChoice.Invoke(null,
                                new object[] { hero, choice })))
                                groupSelected = true;
                            if (!String.Equals(Convert.ToString(
                                ReflectionUtil.GetProperty(choice, "Type")), "Keystone",
                                StringComparison.OrdinalIgnoreCase))
                                continue;
                            careerHasKeystone = true;
                            string choiceId = Convert.ToString(
                                ReflectionUtil.GetProperty(choice, "StringId"));
                            if (saved.Contains(choiceId))
                                selectedKeystone = true;
                        }

                    string eligibilityText;
                    if (IsCareerGroupEligibleForHero(group, hero, out eligibilityText))
                    {
                        eligibleGroups++;
                        if (groupSelected)
                            selectedEligibleGroups++;
                        else
                            missingGroups.Add(DescribeCareerGroup(group));
                    }
                }
            if (missingGroups.Count > 0)
                throw new InvalidOperationException("Career verification found eligible groups " +
                    "without a saved choice: " + String.Join(", ", missingGroups.ToArray()) + ".");
            if (careerHasKeystone && !selectedKeystone)
                throw new InvalidOperationException("Career verification found no selected keystone.");

            ModLog.Info("Verified " + hero.Name + " career " + profile.CareerId +
                " at tier 3 with " + choices.Count + " saved root/path choices across " +
                selectedEligibleGroups + "/" + eligibleGroups + " eligible groups" +
                (careerHasKeystone ? " including a keystone" : String.Empty) + ".");
        }

        private static MethodInfo FindMethod(Type type, string name, int parameterCount)
        {
            return FindMethod(type, name, parameterCount, null);
        }

        private static MethodInfo FindMethod(Type type, string name, int parameterCount,
            Predicate<ParameterInfo[]> predicate)
        {
            if (type == null)
                return null;
            MethodInfo[] methods = type.GetMethods(BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
            for (int i = 0; i < methods.Length; i++)
            {
                ParameterInfo[] parameters = methods[i].GetParameters();
                if (methods[i].Name == name && parameters.Length == parameterCount &&
                    (predicate == null || predicate(parameters)))
                    return methods[i];
            }
            return null;
        }

        private static void EnsureLeaderCapableEncounterParty(
            EncounterDefinition definition, MobileParty party, Settlement anchor,
            Hero hero, Clan partyClan)
        {
            if (party == null)
                throw new ArgumentNullException("party");
            if (hero == null)
                throw new ArgumentNullException("hero");

            EnsureEncounterHeroClan(hero, partyClan);

            if (!(party.PartyComponent is EncounterBanditPartyComponent))
            {
                EncounterBanditPartyComponent.Convert(party, anchor);
                ModLog.Info("Converted encounter party " + party.StringId +
                    " to leader-capable bandit component while preserving native bandit AI.");
            }

            party.ActualClan = hero.Clan;
            if (!Object.ReferenceEquals(party.ActualClan, hero.Clan))
                throw new InvalidOperationException(
                    "Encounter party clan did not retain " +
                    hero.Clan.StringId + ".");
            if (!party.IsBandit)
                throw new InvalidOperationException(
                    "Leader-capable encounter party lost Bannerlord's bandit flag.");
        }

        private bool TryAttachEncounterHero(EncounterDefinition definition,
            MobileParty party, Settlement anchor, Clan partyClan, out string error)
        {
            error = null;
            try
            {
                Hero hero = GetOrCreateEncounterHero(definition, anchor, partyClan);
                if (hero.IsDead)
                {
                    error = "Persistent hero " + hero.Name + " is marked dead; spawn blocked to protect save integrity.";
                    return false;
                }
                if (hero.IsPrisoner || hero.PartyBelongedToAsPrisoner != null)
                {
                    error = hero.Name + " is currently a prisoner and cannot lead the respawned encounter.";
                    return false;
                }

                EnsureLeaderCapableEncounterParty(definition, party, anchor, hero,
                    hero.Clan);

                if (hero.PartyBelongedTo == party)
                {
                    if (party.LeaderHero != hero)
                        party.ChangePartyLeader(hero);
                    if (party.LeaderHero != hero)
                    {
                        error = "Existing party membership did not retain " + hero.Name +
                            " as leader.";
                        return false;
                    }
                    return true;
                }

                MobileParty previousParty = hero.PartyBelongedTo;
                if (previousParty != null)
                {
                    string previousCareer = String.IsNullOrEmpty(previousParty.StringId) ?
                        null : CareerFromPartyId(previousParty.StringId);
                    bool disposableSameEncounter = !String.IsNullOrEmpty(previousCareer) &&
                        String.Equals(previousCareer, definition.CareerId,
                            StringComparison.Ordinal);
                    if (previousParty.IsActive && !disposableSameEncounter)
                    {
                        error = hero.Name + " unexpectedly belongs to active party " +
                            previousParty.Name + "; refusing to destroy or mutate an unrelated party.";
                        return false;
                    }
                    bool expectPartyDestruction = previousParty.IsActive &&
                        disposableSameEncounter && previousParty.LeaderHero == hero &&
                        !String.IsNullOrEmpty(previousParty.StringId);
                    string intentionallyDestroyedId = expectPartyDestruction ?
                        previousParty.StringId : null;
                    if (expectPartyDestruction)
                        _intentionalDestroyPartyIds.Add(intentionallyDestroyedId);
                    try
                    {
                        MakeHeroFugitiveAction.Apply(hero, false);
                    }
                    finally
                    {
                        // Native destruction events are synchronous. Remove any token
                        // still present after the action so a failed or changed engine path
                        // cannot suppress an unrelated future party with the same id.
                        if (!String.IsNullOrEmpty(intentionallyDestroyedId))
                            _intentionalDestroyPartyIds.Remove(intentionallyDestroyedId);
                    }
                    if (hero.PartyBelongedTo != null)
                    {
                        error = "Could not detach " + hero.Name + " from previous party " +
                            previousParty.Name + ".";
                        return false;
                    }
                }
                hero.StayingInSettlement = null;
                hero.ChangeState(Hero.CharacterStates.Active);
                hero.HitPoints = Math.Max(1, hero.MaxHitPoints);
                AddHeroToPartyAction.Apply(hero, party, false);
                party.ChangePartyLeader(hero);
                if (party.LeaderHero != hero)
                {
                    error = "MobileParty.ChangePartyLeader did not retain " + hero.Name + ".";
                    return false;
                }
                ModLog.Info("Attached persistent hero " + hero.Name + " to " +
                    party.StringId + " as party leader at full health.");
                return true;
            }
            catch (Exception ex)
            {
                error = FormatException(ex);
                return false;
            }
        }

        private bool IsEncounterHeroAvailable(string careerId, out string reason)
        {
            reason = null;
            Hero hero;
            if (!TryGetActiveEncounterHero(careerId, out hero) || hero == null)
                return true;
            if (hero.IsDead)
            {
                reason = hero.Name + " is unexpectedly dead.";
                return false;
            }
            if (hero.IsPrisoner || hero.PartyBelongedToAsPrisoner != null)
            {
                PartyBase prisonParty = hero.PartyBelongedToAsPrisoner;
                string captor = prisonParty == null || prisonParty.Name == null
                    ? null : prisonParty.Name.ToString();
                reason = hero.Name + " is captive" +
                    (String.IsNullOrEmpty(captor) ? String.Empty : " in " + captor) +
                    ". The encounter remains suspended until release.";
                return false;
            }
            return true;
        }

        private void QueueEncounterHeroRecovery(string careerId, string reason)
        {
            if (String.IsNullOrEmpty(careerId))
                return;
            _pendingHeroRecoveries[careerId] = reason ?? "encounter defeat";
        }

        private void ProcessEncounterHeroRecoveries()
        {
            if (_pendingHeroRecoveries == null || _pendingHeroRecoveries.Count == 0)
                return;

            List<string> completed = new List<string>();
            foreach (KeyValuePair<string, string> entry in _pendingHeroRecoveries)
            {
                Hero hero;
                if (!TryGetActiveEncounterHero(entry.Key, out hero) || hero == null)
                {
                    completed.Add(entry.Key);
                    continue;
                }
                if (hero.IsPrisoner || hero.PartyBelongedToAsPrisoner != null)
                {
                    // Keep the deferred recovery pending. Some release-event paths
                    // fire before all prisoner fields are cleared; retaining the entry
                    // lets the next application tick finish the transition safely.
                    continue;
                }

                MobileParty currentParty = hero.PartyBelongedTo;
                if (currentParty != null && currentParty.IsActive)
                    continue;

                EncounterDefinition definition;
                if (!EncounterCatalog.ByCareer.TryGetValue(entry.Key, out definition))
                {
                    completed.Add(entry.Key);
                    continue;
                }
                if (PrepareEncounterHeroForRecovery(entry.Key,
                    ResolveAnchor(definition), entry.Value))
                    completed.Add(entry.Key);
            }

            for (int i = 0; i < completed.Count; i++)
                _pendingHeroRecoveries.Remove(completed[i]);
        }

        private bool PrepareEncounterHeroForRecovery(string careerId,
            Settlement anchor, string reason)
        {
            Hero hero;
            if (!TryGetActiveEncounterHero(careerId, out hero) || hero == null)
                return true;
            try
            {
                if (hero.IsPrisoner || hero.PartyBelongedToAsPrisoner != null)
                    return false;
                if (hero.PartyBelongedTo != null && hero.PartyBelongedTo.IsActive)
                    return false;
                if (hero.IsDead)
                {
                    ModLog.Error("Recovery transition refused because persistent hero " +
                        hero.Name + " is marked dead.");
                    return false;
                }

                PlaceEncounterHeroBetweenEncounters(careerId, hero, anchor, true);
                ModLog.Info("Encounter hero " + hero.Name +
                    " survived " + reason + " unconscious and is recovering" +
                    (anchor == null ? String.Empty : " at " + anchor.Name) + ".");
                return true;
            }
            catch (Exception ex)
            {
                ModLog.Error("Recovery transition failed for " + hero.Name +
                    ": " + FormatException(ex));
                return false;
            }
        }

        private bool PrepareEncounterHeroForImmediateRespawn(
            EncounterDefinition definition, out string error)
        {
            error = null;
            if (definition == null)
            {
                error = "encounter definition is missing";
                return false;
            }

            Hero hero;
            if (!TryGetActiveEncounterHero(definition.CareerId, out hero) ||
                hero == null)
                return true;
            if (hero.IsDead)
            {
                error = hero.Name + " is unexpectedly dead";
                return false;
            }
            if (hero.IsPrisoner || hero.PartyBelongedToAsPrisoner != null)
            {
                error = hero.Name + " is still captive";
                return false;
            }

            try
            {
                MobileParty previousParty = hero.PartyBelongedTo;
                if (previousParty != null)
                {
                    string previousCareer = String.IsNullOrEmpty(
                        previousParty.StringId) ? null :
                        CareerFromPartyId(previousParty.StringId);
                    bool sameEncounter = String.Equals(previousCareer,
                        definition.CareerId, StringComparison.Ordinal);
                    if (previousParty.IsActive && !sameEncounter)
                    {
                        error = hero.Name + " belongs to unrelated active party " +
                            previousParty.Name;
                        return false;
                    }
                    if (previousParty.IsActive && previousParty.MapEvent != null)
                    {
                        error = "the previous battle is still resolving";
                        return false;
                    }

                    string intentionalId = previousParty.IsActive &&
                        sameEncounter ? previousParty.StringId : null;
                    if (!String.IsNullOrEmpty(intentionalId))
                        _intentionalDestroyPartyIds.Add(intentionalId);
                    try
                    {
                        MakeHeroFugitiveAction.Apply(hero, false);
                    }
                    finally
                    {
                        if (!String.IsNullOrEmpty(intentionalId))
                            _intentionalDestroyPartyIds.Remove(intentionalId);
                    }
                    if (hero.PartyBelongedTo != null)
                    {
                        error = "could not detach " + hero.Name +
                            " from the defeated party";
                        return false;
                    }
                }

                PlaceEncounterHeroBetweenEncounters(definition.CareerId, hero,
                    ResolveAnchor(definition), false);
                _pendingHeroRecoveries.Remove(definition.CareerId);
                ModLog.Info("Prepared " + hero.Name +
                    " synchronously for an administrative encounter respawn.");
                return true;
            }
            catch (Exception ex)
            {
                error = FormatException(ex);
                return false;
            }
        }

        private static void PlaceEncounterHeroBetweenEncounters(string careerId,
            Hero hero, Settlement anchor, bool wounded)
        {
            if (hero == null)
                return;
            if (wounded)
                hero.HitPoints = 1;

            EncounterDefinition definition;
            bool guardian = EncounterCatalog.ByCareer.TryGetValue(careerId,
                out definition) && definition.Kind == EncounterKind.GuardianSite;
            if (guardian)
            {
                // Guardian heroes are encounter payload, not campaign-map actors.
                // Fugitive + StayingInSettlement exposes them to native captivity,
                // settlement and AI systems even without a MobileParty.
                hero.StayingInSettlement = null;
                hero.ChangeState(Hero.CharacterStates.Disabled);
            }
            else
            {
                hero.ChangeState(Hero.CharacterStates.Fugitive);
                hero.StayingInSettlement = anchor;
            }
            if (anchor != null)
                hero.UpdateLastKnownClosestSettlement(anchor);
        }

        private void OnCanEncounterHeroDie(Hero hero,
            KillCharacterAction.KillCharacterActionDetail detail, ref bool result)
        {
            if (!IsEncounterHero(hero))
                return;
            result = false;
            if (hero.HitPoints <= 0)
                hero.HitPoints = 1;
            ModLog.Verbose("Prevented death eligibility for encounter hero " +
                hero.Name + " (detail=" + detail + ").");
        }

        private void OnCanEncounterHeroBecomePrisoner(Hero hero, ref bool result)
        {
            if (!result)
                return;
            if (hero == Hero.MainHero && MobileParty.MainParty != null &&
                IsGuardianEncounterMapEvent(MobileParty.MainParty.MapEvent))
            {
                // Guardian defenders are virtualized immediately after the battle.
                // They cannot remain as the main hero's captor on the campaign map.
                result = false;
                ModLog.Info("Prevented player captivity by a temporary guardian-site " +
                    "party; the site encounter will close after defeat.");
                return;
            }
            if (!IsEncounterHero(hero))
                return;
            string careerId;
            EncounterDefinition definition;
            if (TryGetEncounterHeroCareer(hero, out careerId) &&
                EncounterCatalog.ByCareer.TryGetValue(careerId, out definition) &&
                definition.Kind == EncounterKind.GuardianSite)
            {
                result = false;
                ModLog.Info("Prevented illegal capture of guardian-site hero " +
                    hero.Name + ". Guardian encounters are player-only.");
                return;
            }
            result = false;
            ModLog.Info("Prevented capture of active encounter leader " +
                hero.Name + "; v1.7.2 set-mastery recruitment replaces random captivity.");
        }

        private void OnEncounterHeroPrisonerTaken(PartyBase capturer, Hero prisoner)
        {
            string careerId;
            if (!TryGetEncounterHeroCareer(prisoner, out careerId))
                return;
            EncounterDefinition definition = EncounterCatalog.ByCareer[careerId];
            if (definition.Kind == EncounterKind.GuardianSite)
            {
                ModLog.Error("Guardian-site hero " + prisoner.Name +
                    " entered captivity through a path that bypassed eligibility; " +
                    "releasing and returning the hero to dormant storage immediately.");
                EndCaptivityAction.ApplyByEscape(prisoner, null, false);
                return;
            }
            ModLog.Info("Persistent encounter hero " + prisoner.Name +
                " was captured by " +
                (capturer == null || capturer.Name == null ? "an unknown party" : capturer.Name.ToString()) +
                ". " + definition.MapName +
                " will not respawn until the hero is released.");
        }

        private void OnEncounterHeroPrisonerReleased(Hero prisoner, PartyBase party,
            IFaction capturerFaction, EndCaptivityDetail detail, bool showNotification)
        {
            string careerId;
            if (!TryGetEncounterHeroCareer(prisoner, out careerId))
                return;
            EncounterDefinition definition = EncounterCatalog.ByCareer[careerId];
            Settlement anchor = ResolveAnchor(definition);
            string recoveryReason = "release from captivity (" + detail + ")";
            if (!PrepareEncounterHeroForRecovery(careerId, anchor, recoveryReason))
                QueueEncounterHeroRecovery(careerId, recoveryReason);
            if (definition.Kind == EncounterKind.GuardianSite)
            {
                _respawnAtDay.Remove(careerId);
                ModLog.Info("Guardian-site hero " + prisoner.Name +
                    " returned to dormant off-map storage after invalid captivity.");
                return;
            }
            double now = CampaignTime.Now.ToDays;
            double existing;
            if (!_respawnAtDay.TryGetValue(careerId, out existing) || existing < now)
                _respawnAtDay[careerId] = now + ModConfig.RespawnDays;
            ModLog.Info("Released encounter hero " + prisoner.Name +
                " returned to recovery; respawn eligibility day " +
                _respawnAtDay[careerId].ToString("0.00") + ".");
        }

        private void OnEncounterHeroWounded(Hero woundedHero)
        {
            if (IsEncounterHero(woundedHero) && woundedHero.HitPoints <= 0)
                woundedHero.HitPoints = 1;
        }

        private bool IsEncounterHero(Hero hero)
        {
            if (hero == null)
                return false;
            for (int i = 0; i < EncounterCatalog.All.Length; i++)
            {
                Hero candidate;
                if (TryGetActiveEncounterHero(EncounterCatalog.All[i].CareerId,
                    out candidate) && Object.ReferenceEquals(candidate, hero))
                    return true;
            }
            return false;
        }

        private bool TryGetEncounterHeroCareer(Hero hero, out string careerId)
        {
            for (int i = 0; i < EncounterCatalog.All.Length; i++)
            {
                Hero candidate;
                string candidateCareer = EncounterCatalog.All[i].CareerId;
                if (TryGetActiveEncounterHero(candidateCareer, out candidate) &&
                    Object.ReferenceEquals(candidate, hero))
                {
                    careerId = candidateCareer;
                    return true;
                }
            }
            careerId = null;
            return false;
        }

        private string GetEncounterHeroOverview(string careerId)
        {
            Hero hero;
            if (!TryGetActiveEncounterHero(careerId, out hero) || hero == null)
                return "not yet created";
            string state = hero.IsPrisoner ? "CAPTIVE" :
                (hero.PartyBelongedTo != null ? "ACTIVE — " + hero.PartyBelongedTo.Name :
                hero.HeroState.ToString().ToUpperInvariant());
            string role = IsOriginalRecruited(careerId) ? "successor" : "original";
            return hero.Name + " [" + role + "] (level " + hero.Level + ", " + state +
                ", health " + hero.HitPoints + "/" + hero.MaxHitPoints + ")";
        }

        private sealed class AbilityCandidate
        {
            public string Id;
            public string LoreId;
            public int Tier;
            public int Score;
        }
    }

    internal sealed class EncounterHeroProfile
    {
        public string CareerId;
        public string FullName;
        public string FirstName;
        public int Level;
        public int Age;
        public bool PreferMounted;
        public bool RequireMounted;
        public bool IsCaster;
        public int MaxSelectedSpells;
        public string[] RequiredTemplateTokens;
        public string[] TemplateTokens;
        public string[] NegativeTemplateTokens;
        public string[] BranchTokens;
        public string[] AbilityTokens;
        public string[] PrimarySkillTokens;
        public string[] SecondarySkillTokens;
    }

    internal static class EncounterHeroProfiles
    {
        private static readonly Dictionary<string, EncounterHeroProfile> ByCareer = Build();

        internal static EncounterHeroProfile Get(string careerId)
        {
            EncounterHeroProfile profile;
            return ByCareer.TryGetValue(careerId ?? String.Empty, out profile) ?
                profile : null;
        }

        private static Dictionary<string, EncounterHeroProfile> Build()
        {
            Dictionary<string, EncounterHeroProfile> r =
                new Dictionary<string, EncounterHeroProfile>(StringComparer.Ordinal);
            Add(r, P("GrailDamsel", "Ysabeau the Blighted", "Ysabeau", 42, 37,
                false, false, true, A("damsel", "prophetess", "breton"),
                A("grail", "damsel", "prophetess", "breton"), A("orc", "dwarf", "vampire"),
                A("lady", "grail", "healing", "ward"), A("life", "heavens", "light", "lady"),
                A("onehanded", "polearm", "medicine"), A("athletics", "riding", "leadership")));
            Add(r, P("GrailKnight", "Sir Malrec the Unhallowed", "Malrec", 46, 41,
                true, true, false, A("breton", "knight"),
                A("grail", "paladin", "knight", "breton"), A("black_orc", "dwarf", "elf"),
                A("lance", "valor", "grail", "charge"), null,
                A("polearm", "onehanded", "riding"), A("leadership", "athletics")));
            Add(r, P("MinorVampire", "Vicomte Aleron the Blooded", "Aleron", 43, 96,
                false, false, false, A("vampire"), A("minor_vampire", "vampire", "blood"),
                A("dwarf", "orc", "elf"), A("blood", "night", "duelist", "hunger"), null,
                A("onehanded", "athletics", "roguery"), A("leadership", "riding")));
            Add(r, P("WarriorPriest", "Lector Konrad Voss", "Konrad", 44, 45,
                false, false, true, A("warrior_priest", "sigmar", "empire"), A("warrior_priest", "sigmar", "lector", "priest"),
                A("ulric", "orc", "vampire"), A("sigmar", "hammer", "faith", "fury"),
                A("sigmar", "prayer", "holy"), A("twohanded", "onehanded", "athletics"),
                A("leadership", "medicine")));
            Add(r, P("BloodKnight", "Kastellan Varos the Red", "Varos", 48, 132,
                true, true, false, A("vampire"), A("blood_knight", "blood_dragon", "vampire_knight"),
                A("necrarch", "dwarf", "orc"), A("blood", "dragon", "duel", "charge"), null,
                A("onehanded", "polearm", "riding"), A("athletics", "leadership")));
            Add(r, P("Mercenary", "Captain Luccio Ferrante", "Luccio", 41, 39,
                false, false, false, A("mercenary", "tilea", "border"), A("mercenary", "captain", "tilea", "border"),
                A("undead", "orc", "dwarf"), A("paymaster", "duelist", "crossbow", "captain"), null,
                A("crossbow", "onehanded", "tactics"), A("leadership", "roguery", "athletics")));
            Add(r, P("WitchHunter", "Inquisitor Matthias Krieger", "Matthias", 43, 44,
                false, false, false, A("witch_hunter", "witchhunter", "inquisitor", "empire"), A("witch_hunter", "witchhunter", "inquisitor"),
                A("undead", "orc", "dwarf"), A("silver", "pistol", "judgement", "hunter"), null,
                A("crossbow", "onehanded", "athletics"), A("scouting", "roguery", "leadership")));
            Add(r, P("Necromancer", "Mordechai the Restless", "Mordechai", 45, 78,
                false, false, true, A("necromancer", "master_necromancer", "death"), A("necromancer", "master_necromancer", "death"),
                A("orc", "dwarf", "elf"), A("death", "undead", "summon", "corpse"),
                A("necromancy", "death", "undead"), A("athletics", "medicine", "tactics"),
                A("onehanded", "leadership")));
            Add(r, P("BlackGrailKnight", "Sir Severin, Keeper of the Black Grail", "Severin", 47, 63,
                true, true, false, A("breton", "knight"), A("black_grail", "mousillon", "knight"),
                A("dwarf", "orc", "elf"), A("black", "grail", "terror", "lance"), null,
                A("polearm", "onehanded", "riding"), A("leadership", "athletics")));
            Add(r, P("Necrarch", "Azrad the Pallid", "Azrad", 47, 211,
                false, false, true, A("vampire"), A("necrarch", "vampire", "sorcerer"),
                A("blood_knight", "dwarf", "orc"), A("necrarch", "experiment", "death", "magic"),
                A("necromancy", "death", "vampire"), A("athletics", "medicine", "tactics"),
                A("onehanded", "leadership")));
            Add(r, P("WarriorPriestUlric", "Hagen Wolfsbane", "Hagen", 45, 46,
                false, false, true, A("ulric", "wolf_priest", "warrior_priest"), A("ulric", "wolf_priest", "warrior_priest"),
                A("sigmar", "undead", "orc"), A("ulric", "wolf", "winter", "fury"),
                A("ulric", "winter", "prayer"), A("twohanded", "onehanded", "athletics"),
                A("leadership", "medicine")));
            Add(r, P("ImperialMagister", "Magister Erasmus Volker", "Erasmus", 44, 52,
                false, false, true, A("imperial_magister", "magister", "wizard", "college", "empire"), A("imperial_magister", "magister", "wizard", "college"),
                A("undead", "orc", "dwarf"), A("celestial", "college", "control", "arcane"),
                A("heavens", "celestial", "light", "empire"), A("athletics", "tactics", "medicine"),
                A("onehanded", "leadership")));
            Add(r, P("Waywatcher", "Aelir the Thorn-Eyed", "Aelir", 45, 167,
                false, false, false, A("wood_elf", "asrai", "elf"), A("waywatcher", "wood_elf", "asrai"),
                A("dark_elf", "orc", "dwarf"), A("bow", "ambush", "thorn", "hunter"), null,
                A("bow", "athletics", "scouting"), A("onehanded", "roguery")));
            Add(r, P("Spellsinger", "Lethariel of the Withered Bough", "Lethariel", 45, 193,
                false, false, true, A("wood_elf", "asrai", "elf"), A("spellsinger", "spellweaver", "wood_elf"),
                A("dark_elf", "orc", "dwarf"), A("forest", "healing", "beast", "song"),
                A("life", "beasts", "forest", "athel"), A("athletics", "medicine", "bow"),
                A("scouting", "leadership")));
            Add(r, P("Warden", "Caerwyn the Hunted", "Caerwyn", 44, 154,
                false, false, false, A("wood_elf", "asrai", "elf"), A("warden", "eternal_guard", "wood_elf"),
                A("dark_elf", "orc", "dwarf"), A("spear", "shield", "guard", "forest"), null,
                A("polearm", "onehanded", "athletics"), A("bow", "leadership", "scouting")));
            Add(r, P("GreyLord", "Magister Severin Veyl", "Severin", 46, 58,
                false, false, true, A("grey_lord", "grey_wizard", "shadow", "wizard"), A("grey_lord", "grey_wizard", "shadow", "wizard"),
                A("undead", "orc", "dwarf"), A("shadow", "deception", "grey", "stealth"),
                A("shadow", "grey", "illusion"), A("athletics", "roguery", "tactics"),
                A("onehanded", "scouting")));
            Add(r, P("KnightOldWorld", "Sir Eckhardt of the Black Road", "Eckhardt", 46, 42,
                true, true, false, A("reiksguard", "knight", "empire"), A("reiksguard", "knight", "old_world", "empire"),
                A("undead", "orc", "dwarf"), A("runeblade", "order", "charge", "sword"), null,
                A("onehanded", "polearm", "riding"), A("leadership", "athletics")));
            Add(r, P("Ironbreaker", "Durgan Ironmantle", "Durgan", 47, 119,
                false, false, false, A("dwarf", "dawi"), A("ironbreaker", "dwarf", "dawi", "gromril"),
                A("chaos_dwarf", "orc", "elf"), A("shield", "gromril", "hold", "stone"), null,
                A("onehanded", "athletics", "engineering"), A("crossbow", "leadership")));
            Add(r, P("Slayer", "Kragni Oathscar", "Kragni", 48, 104,
                false, false, false, A("dwarf", "dawi"), A("slayer", "troll_slayer", "dwarf"),
                A("chaos_dwarf", "orc", "elf"), A("troll", "oath", "axe", "death"), null,
                A("twohanded", "athletics", "throwing"), A("onehanded", "leadership")));
            Add(r, P("Runelord", "Baragor Embermark", "Baragor", 47, 176,
                false, false, true, A("dwarf", "dawi"), A("runelord", "runesmith", "dwarf"),
                A("chaos_dwarf", "orc", "elf"), A("rune", "anvil", "ward", "master"),
                A("rune", "runesmith", "runelord"), A("engineering", "athletics", "onehanded"),
                A("crossbow", "leadership")));
            Add(r, P("OrcBoss", "Morglug Ironjaw", "Morglug", 49, 51,
                true, false, false, A("orc", "greenskin"), A("orc_boss", "warboss", "black_orc", "orc"),
                A("goblin", "human", "dwarf"), A("waaagh", "boss", "brutal", "charge"), null,
                A("twohanded", "onehanded", "athletics"), A("riding", "leadership", "throwing")));
            Add(r, P("OrcShaman", "Nazgob Moon-Eater", "Nazgob", 46, 64,
                false, false, true, A("orc", "greenskin"), A("orc_shaman", "shaman", "orc"),
                A("goblin", "human", "dwarf"), A("waaagh", "moon", "curse", "shaman"),
                A("waaagh", "shaman", "greenskin", "moon"), A("athletics", "tactics", "medicine"),
                A("onehanded", "leadership")));
            return r;
        }

        private static EncounterHeroProfile P(string careerId, string fullName,
            string firstName, int level, int age, bool preferMounted,
            bool requireMounted, bool caster, string[] required, string[] templates,
            string[] negative, string[] branch, string[] abilities,
            string[] primarySkills, string[] secondarySkills)
        {
            return new EncounterHeroProfile
            {
                CareerId = careerId,
                FullName = fullName,
                FirstName = firstName,
                Level = level,
                Age = age,
                PreferMounted = preferMounted,
                RequireMounted = requireMounted,
                IsCaster = caster,
                MaxSelectedSpells = caster ? 8 : 0,
                RequiredTemplateTokens = required,
                TemplateTokens = templates,
                NegativeTemplateTokens = negative,
                BranchTokens = branch,
                AbilityTokens = abilities,
                PrimarySkillTokens = primarySkills,
                SecondarySkillTokens = secondarySkills
            };
        }

        private static void Add(Dictionary<string, EncounterHeroProfile> map,
            EncounterHeroProfile profile)
        {
            map.Add(profile.CareerId, profile);
        }

        private static string[] A(params string[] values) { return values; }
    }
}
