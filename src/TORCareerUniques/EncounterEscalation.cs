using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;

namespace TORCareerUniques
{
    internal sealed class EncounterStrengthProfile
    {
        public int CollectionPieces;
        public int VeteranTier;
        public int TargetTroops;
        public float TotalMultiplier;
        public float EliteShare;
        public float QualityBias;
    }

    internal sealed partial class UniqueEncounterBehavior
    {
        private const int CurrentEncounterStrengthSchemaVersion = 5;
        private static readonly float[] CollectionStrengthMultipliers =
            { 1.00f, 1.10f, 1.20f, 1.35f, 1.50f, 1.67f };
        private static readonly float[] CollectionEliteShares =
            { 0.20f, 0.22f, 0.27f, 0.31f, 0.36f, 0.40f };

        private Dictionary<string, int> _veteranClears =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private int _encounterStrengthSchemaVersion;
        private bool _suppressEscalationNotifications;
        private string _resolvingRewardCareerId;
        private string _deferredEscalationText;

        private EncounterStrengthProfile GetEncounterStrengthProfile(
            EncounterDefinition definition, int serial)
        {
            int pieces = Math.Max(0, Math.Min(5,
                GetDiscoveredSetPieceCount(definition.CareerId)));
            int veteranTier = GetVeteranTier(definition.CareerId);
            float collectionMultiplier =
                CollectionStrengthMultipliers[pieces];
            float veteranMultiplier = 1f + 0.05f * veteranTier;
            float totalMultiplier = collectionMultiplier * veteranMultiplier;
            Random random = new Random(StableHash(definition.CareerId +
                ":strength:" + serial));
            int baseTarget = definition.MinimumTroops + random.Next(
                definition.MaximumTroops - definition.MinimumTroops + 1);

            return new EncounterStrengthProfile
            {
                CollectionPieces = pieces,
                VeteranTier = veteranTier,
                TargetTroops = Math.Max(1, (int)Math.Round(baseTarget *
                    totalMultiplier)),
                TotalMultiplier = totalMultiplier,
                EliteShare = Math.Min(0.45f,
                    CollectionEliteShares[pieces] + 0.01f * veteranTier),
                QualityBias = 0.04f + 0.015f * pieces +
                    0.01f * veteranTier
            };
        }

        private int GetVeteranTier(string careerId)
        {
            int clears;
            return _veteranClears.TryGetValue(careerId, out clears)
                ? Math.Max(0, Math.Min(5, clears)) : 0;
        }

        private void AdvanceVeteranTierAfterPlayerVictory(
            EncounterDefinition definition)
        {
            if (definition == null ||
                GetDiscoveredSetPieceCount(definition.CareerId) < 5)
                return;

            int oldTier = GetVeteranTier(definition.CareerId);
            if (oldTier >= 5)
                return;
            int newTier = oldTier + 1;
            _veteranClears[definition.CareerId] = newTier;
            string text = GetVeteranEscalationText(definition.CareerId,
                newTier);
            if (!String.IsNullOrEmpty(text))
                CareerUniqueRuntime.Notify(text);
            ModLog.Info(definition.MapName +
                " advanced to veteran tier " + newTier + "/5 after a " +
                "post-completion player victory.");
        }

        private void MigrateEncounterStrengthAndAi()
        {
            bool rebuildSeededRosters = _encounterStrengthSchemaVersion < 2;
            bool correctAllLoreIdentity = _encounterStrengthSchemaVersion < 4;
            bool migrateDedicatedOwners = _encounterStrengthSchemaVersion < 5;
            bool migrate = _encounterStrengthSchemaVersion <
                CurrentEncounterStrengthSchemaVersion;
            int reinforced = 0;
            int rebuilt = 0;
            int identityCorrected = 0;
            int renamed = 0;
            int safetyUpdated = 0;
            bool loreMigrationFailed = false;
            for (int i = 0; i < EncounterCatalog.All.Length; i++)
            {
                EncounterDefinition definition = EncounterCatalog.All[i];
                if (definition.Kind != EncounterKind.RoamingHost)
                    continue;
                MobileParty party = FindActiveEncounter(definition.CareerId);
                if (party == null)
                    continue;

                party.Aggressiveness = 0.35f;
                if (party.Ai != null)
                    party.Ai.RethinkAtNextHourlyTick = true;
                safetyUpdated++;

                if (!migrate)
                    continue;
                try
                {
                    bool careerIdentityChanged = correctAllLoreIdentity &&
                        definition.CareerLed &&
                        !(String.Equals(definition.CareerId, "Warden",
                            StringComparison.Ordinal) &&
                            _encounterStrengthSchemaVersion >= 3);
                    bool rebuildThisRoster = rebuildSeededRosters ||
                        careerIdentityChanged;

                    if (migrateDedicatedOwners)
                    {
                        if (party.MapEvent != null)
                            throw new InvalidOperationException(
                                "Active host is still participating in a map event; dedicated-owner migration is deferred until a later safe load.");

                        Hero leader = party.LeaderHero;
                        Clan nativeBanditClan = ResolveBanditClan(definition);
                        if (nativeBanditClan == null || leader == null)
                            throw new InvalidOperationException(
                                "Dedicated-owner migration could not resolve the active leader and native bandit spawn template.");
                        Clan correctedClan = ResolveOrCreateEncounterOwnerClan(
                            definition, ResolveAnchor(definition), leader,
                            leader.CharacterObject, nativeBanditClan);
                        EnsureEncounterHeroClan(leader, correctedClan);
                        party.ActualClan = correctedClan;
                        if (!Object.ReferenceEquals(party.ActualClan,
                            correctedClan) ||
                            !Object.ReferenceEquals(correctedClan.Leader, leader))
                            throw new InvalidOperationException(
                                "Dedicated-owner migration did not retain owner/leader invariants.");
                        identityCorrected++;
                    }

                    if (careerIdentityChanged)
                    {
                        string currentName = party.Name == null ? null :
                            party.Name.ToString();
                        if (!String.Equals(currentName, definition.MapName,
                            StringComparison.Ordinal))
                        {
                            party.Party.SetCustomName(new TaleWorlds.Localization.TextObject(
                                definition.MapName, null));
                            renamed++;
                        }
                    }

                    if (!rebuildThisRoster)
                        continue;

                    int serial;
                    _spawnSerials.TryGetValue(definition.CareerId,
                        out serial);
                    List<TroopCandidate> pool = BuildTroopPool(definition);
                    if (pool.Count == 0)
                        throw new InvalidOperationException(
                            "No valid authored troops were available for migration.");
                    int before = party.MemberRoster.TotalManCount;
                    int removed = ClearNonHeroEncounterTroops(party);
                    PopulateParty(party, pool, definition, Math.Max(1, serial));
                    if (removed > 0)
                        rebuilt++;
                    if (party.MemberRoster.TotalManCount >
                        Math.Max(1, before - removed))
                        reinforced++;
                }
                catch (Exception ex)
                {
                    if (migrateDedicatedOwners || correctAllLoreIdentity)
                        loreMigrationFailed = true;
                    ModLog.Error("Legacy host migration failed for " +
                        definition.MapName + ": " + FormatException(ex));
                }
            }

            if (migrate && !loreMigrationFailed)
            {
                _encounterStrengthSchemaVersion =
                    CurrentEncounterStrengthSchemaVersion;
                ModLog.Info("Encounter strength/lore state upgraded to schema " +
                    CurrentEncounterStrengthSchemaVersion +
                    "; active hosts reinforced=" + reinforced +
                    ", rosters rebuilt=" + rebuilt +
                    ", dedicated owners corrected=" + identityCorrected +
                    ", renamed=" + renamed +
                    ", AI safety updated=" + safetyUpdated + ".");
            }
            else if (loreMigrationFailed)
            {
                ModLog.Error("Encounter strength/lore schema " +
                    CurrentEncounterStrengthSchemaVersion + " was not committed " +
                    "because at least one active host could not be corrected; " +
                    "the bounded migration will retry on the next campaign load.");
            }
        }

        private void QueueOrShowCollectionEscalation(string careerId,
            int progress)
        {
            if (_suppressEscalationNotifications || progress < 1 ||
                progress > 5)
                return;
            string text = GetCollectionEscalationText(careerId, progress);
            if (String.IsNullOrEmpty(text))
                return;
            if (String.Equals(_resolvingRewardCareerId, careerId,
                StringComparison.Ordinal))
                _deferredEscalationText = text;
            else
                CareerUniqueRuntime.Notify(text);
        }

        private static string Milestone(int progress, string one, string two,
            string three, string four, string five)
        {
            switch (progress)
            {
                case 1: return one;
                case 2: return two;
                case 3: return three;
                case 4: return four;
                case 5: return five;
                default: return null;
            }
        }

        private static string GetCollectionEscalationText(string careerId,
            int progress)
        {
            switch (careerId)
            {
                case "GrailDamsel":
                    return Milestone(progress,
                        "Ysabeau feels the first relic leave the Blighted Chapel. Fallen pilgrims rise to bar your next desecration.",
                        "A second relic is freed, and Ysabeau binds darker vows into the chapel's dead defenders.",
                        "The Lady's ruined shrine groans beneath Ysabeau's wrath; blackened knights answer her summons.",
                        "With four relics taken, Ysabeau seals the chapel in profane mist and calls her strongest guardians.",
                        "The reliquary stands empty. Ysabeau swears a final blighted vow, and the chapel becomes a fortress of the damned.");
                case "GrailKnight":
                    return Milestone(progress,
                        "Sir Malrec learns that one Grail relic has escaped his procession. More unhallowed knights ride beneath his banner.",
                        "A second stolen relic stains Sir Malrec's honour; fresh retainers swear themselves to his black pilgrimage.",
                        "Sir Malrec proclaims you a blasphemer before the Lady, drawing hardened knights to the Procession.",
                        "Four relics are beyond his grasp. The Black Grail Procession gathers its most dreadful sworn lances.",
                        "Robbed of every relic, Sir Malrec begins a war-pilgrimage in your name; the whole Procession rides for vengeance.");
                case "MinorVampire":
                    return Milestone(progress,
                        "Vicomte Aleron scents the loss of his first blood-soaked heirloom. Ancient thralls stir within the sepulchre.",
                        "A second heirloom is taken, and Aleron awakens grave guards long denied the taste of battle.",
                        "Aleron's wounded pride becomes a blood debt; stronger dead crowd the Sepulchre's passages.",
                        "With four heirlooms stolen, the Vicomte opens the Red Duke's deepest crypts and calls their keepers forth.",
                        "The last heirloom is yours. Aleron invokes the Red Duke's name and musters every awakened horror for vengeance.");
                case "WarriorPriest":
                    return Milestone(progress,
                        "Voss learns that one recovered temple relic has passed from his custody. He treats the loss as a test and calls more hardened Sigmarites to the purge.",
                        "A second relic is taken. Voss tightens the cordon around the Purple Hand's routes and brings veteran zealots and soldiers into his procession.",
                        "Voss names you a dangerous claimant to consecrated arms; seasoned hunters and flagellants join his pursuit.",
                        "Four relics are beyond his guard. The purge abandons restraint and gathers its sternest veterans for a final reckoning.",
                        "The full testament has passed from Voss. He turns the entire Purple Hand Purge toward one last trial of your claim.");
                case "BloodKnight":
                    return Milestone(progress,
                        "Kastellan Varos marks the first stolen heirloom as a wound to his honour. Stronger Blood Dragons join his errantry.",
                        "A second heirloom is claimed, deepening Varos's blood debt and awakening veteran undead retainers.",
                        "Varos sends crimson challenges across the borderlands; proud killers flock to answer them.",
                        "With four heirlooms gone, the Kastellan calls in ancient oaths and mounts his deadliest household.",
                        "The last heirloom is lost. Varos swears to wash away the insult in your blood, and the Crimson Errantry rides in full strength.");
                case "Mercenary":
                    return Milestone(progress,
                        "Captain Ferrante discovers the first prize missing from his pay chest. He spends good coin on harder veterans.",
                        "A second trophy is taken, and Ferrante doubles the contracts for seasoned blades and crossbows.",
                        "The Black Company places your name beneath a ruinous bounty; ambitious companies join the hunt.",
                        "Four prizes are gone. Ferrante empties his war chest to hire the Border Princes' most ruthless professionals.",
                        "The pay chest has been stripped. Ferrante stakes the Company's fortune on one last, heavily reinforced reckoning.");
                case "WitchHunter":
                    return Milestone(progress,
                        "Krieger learns that one confiscated relic has left the Ashen Tribunal. He doubles the watch and calls in harder hunters.",
                        "A second relic is reclaimed, and Krieger seals the Tribunal behind veteran guards and flagellants.",
                        "Krieger marks you as a dangerous claimant to tainted evidence; seasoned witch-hunters answer his summons.",
                        "With four relics gone, the Tribunal burns lesser evidence and concentrates its fiercest wardens below.",
                        "The evidence vault is stripped bare. The Ashen Tribunal prepares every surviving hunter for one final judgement.");
                case "Necromancer":
                    return Milestone(progress,
                        "Mordechai feels the first grave-good leave his barrow. Restless bones claw upward around the violated tomb.",
                        "A second relic is disturbed, and Mordechai speaks deeper names over the awakened dead.",
                        "The barrow trembles under forbidden rites; wights from older chambers join Mordechai's host.",
                        "Four relics have crossed the threshold. Mordechai breaks the seals on the barrow's royal dead.",
                        "The last grave-good is stolen. Mordechai commands the entire Restless Host to rise and reclaim its inheritance.");
                case "BlackGrailKnight":
                    return Milestone(progress,
                        "Sir Severin learns that one Black Grail relic has escaped his reliquary. More damned retainers swear to the Guard.",
                        "A second relic is lost, and Severin calls ancient Mousillon riders back to the black procession.",
                        "The Keeper names your claim an unforgivable challenge; hardened Black Grail knights gather around the reliquary train.",
                        "With four relics taken, Severin opens old crypt-oaths and summons his strongest undead household.",
                        "The final relic is beyond him. The Black Grail Reliquary Guard rides in full strength for one last reckoning.");
                case "Necrarch":
                    return Milestone(progress,
                        "Azrad senses the first specimen removed from his Ossuary. Fresh dead wake beneath his pallid will.",
                        "A second relic is stolen, driving Azrad to graft stronger guardians from forbidden remains.",
                        "The Necrarch descends into older rites; crypt horrors and learned dead crowd his laboratories.",
                        "Four relics are gone. Azrad opens sealed vaults whose failures were never meant to walk.",
                        "The last relic leaves the Ossuary. Azrad begins his masterwork of retaliation and wakes every viable corpse.");
                case "WarriorPriestUlric":
                    return Milestone(progress,
                        "Hagen Wolfsbane finds the first shrine relic claimed from his keeping. More winter-hardened Ulricans answer his challenge.",
                        "A second relic is taken, and veteran hunters gather beneath Hagen's wolf-marked banner.",
                        "Hagen calls your name into the northern wind; hard White Wolf warriors join the pursuit.",
                        "Four relics are lost. The White Wolf Hunt gathers its sternest champions for the reckoning.",
                        "The last shrine relic is beyond Hagen. The whole White Wolf Hunt turns toward one final trial beneath Ulric's gaze.");
                case "ImperialMagister":
                    return Milestone(progress,
                        "Volker detects the first Collegiate instrument leaving its ward. He reinforces the Observatory with disciplined retainers.",
                        "A second relic is claimed, and Volker retunes the Observatory's wards around veteran guards and trained adepts.",
                        "Arcane alarms ring through forgotten College circles; experienced retainers answer Volker's summons.",
                        "With four instruments lost, Volker opens the sealed laboratories and concentrates every remaining defence.",
                        "The last Collegiate relic is yours. The Observatory aligns every surviving ward and defender for a final examination.");
                case "Waywatcher":
                    return Milestone(progress,
                        "Aelir sees the first Asrai heirloom leave his keeping. More seasoned beast-hunters take up the trail.",
                        "A second heirloom is taken, and veteran glade scouts reinforce Aelir's march patrols.",
                        "The Thorn-Eyed marks you as the hunt's true quarry; experienced Waywatchers close beneath the boughs.",
                        "Four heirlooms are gone. Aelir gathers every hunter still ranging the forest marches.",
                        "The last heirloom is beyond him. The Beast-Hunters commit their whole band to one final pursuit.");
                case "Spellsinger":
                    return Milestone(progress,
                        "Lethariel feels the first sacred focus leave the Defiled Waystone. More Asrai wardens gather to contain the wounded roots.",
                        "A second focus is freed, and Lethariel strengthens the living wards with veteran guardians.",
                        "The waystone cries through Athel Loren; forest spirits and hardened Asrai gather beneath Lethariel's song.",
                        "Four sacred heirlooms are gone. Lethariel draws the remaining guardians into a tighter ring around the poisoned roots.",
                        "The final focus is reclaimed. Every surviving warden of the Defiled Waystone gathers for one last defence.");
                case "Warden":
                    return Milestone(progress,
                        "Caerwyn learns that one heirloom of his condemned spear-line has passed into your hands. More hunted Asrai answer the Hunted's call.",
                        "A second heirloom is taken. Veterans who once sheltered Caerwyn from Orion's riders join the quarry-host.",
                        "The forest's judgment closes around the Hunted; Caerwyn gathers exiles and oath-marked wardens for a harder stand.",
                        "With four heirlooms gone, every scattered survivor of Caerwyn's condemned glade rallies beneath his spear.",
                        "The last heirloom is recovered. Caerwyn commits the whole quarry-host to one final stand before the Wild Hunt finds them.");
                case "GreyLord":
                    return Milestone(progress,
                        "Severin Veyl feels the first Grey College secret pass beyond his veil. Shadow-trained guards thicken below the vault.",
                        "A second relic escapes, and Veyl folds stronger agents into the passages between shadow and stone.",
                        "The Grey Magister names you inside a forbidden cipher; veteran operatives answer from across Altdorf.",
                        "Four secrets are gone. Veyl collapses lesser veils and concentrates his deadliest wardens beneath the College.",
                        "The final secret is yours. The vault is drawn fully into shadow, with every surviving agent waiting inside.");
                case "KnightOldWorld":
                    return Milestone(progress,
                        "Sir Eckhardt discovers the first knightly heirloom missing. More veteran companions ride to reinforce the Black Road Brotherhood.",
                        "A second heirloom is claimed, and Eckhardt summons seasoned knights from old obligations along the road.",
                        "The Brotherhood treats your claim as a trial of succession; hard lances gather beneath its worn standards.",
                        "Four heirlooms are gone. Eckhardt calls every remaining sworn companion to the Black Road.",
                        "The final heirloom is beyond him. The whole Brotherhood forms for one last charge to test your claim.");
                case "Ironbreaker":
                    return Milestone(progress,
                        "Durgan Ironmantle records the first reclaimed heirloom as a new entry in the underhold's grudge. The barricades thicken.",
                        "A second heirloom leaves the hold, and Durgan sets stronger guards beneath renewed rune-wards.",
                        "The grudge grows heavy enough to summon veteran clansmen to Ironmantle's side.",
                        "Four heirlooms are gone. Durgan seals the deep ways and mans them with the underhold's hardest shields.",
                        "The last heirloom is reclaimed. Ironmantle carves your name into stone and musters the full wrath of the underhold.");
                case "Slayer":
                    return Milestone(progress,
                        "Kragni Oathscar finds the first Slayer axe missing from the oath-host. More doom-seekers join the Troll King's hunt.",
                        "A second oath-axe is claimed, and veteran Slayers fall in beside Kragni for the harder road ahead.",
                        "Oathscar names you another trial upon the road to doom; scarred Slayers gather for the reckoning.",
                        "Four axes are gone. The Troll King's Hunters call every surviving oath-brother into one grim host.",
                        "The last oath-axe is yours. The entire Slayer host stakes its honour on one final battle before returning to the Troll King's trail.");
                case "Runelord":
                    return Milestone(progress,
                        "Baragor Embermark feels the first rune-heirloom cross the broken seal. Ancient wards flare around the vault.",
                        "A second heirloom is taken, and Embermark awakens stronger oath-bound guardians.",
                        "The desecration enters the clan's reckoning; veteran Dawi answer Baragor's rune-lit summons.",
                        "Four heirlooms are gone. Embermark kindles the master wards and seals the inner vault with steel.",
                        "The final heirloom is reclaimed. Baragor strikes your name upon the anvil and rouses every rune-guard still standing.");
                case "OrcBoss":
                    return Milestone(progress,
                        "Morglug hears that one trophy was nicked from his Waaagh! More boyz flock to the boss promising a proper fight.",
                        "A second trophy is gone, and Morglug's rage draws bigger, meaner ladz to his banner.",
                        "Morglug bellows your name across the Badlands; black orcs and boar boyz answer the challenge.",
                        "Four trophies are missing. The Rival Waaagh! swells as every brute wants a share of the scrap.",
                        "The last trophy is yours. Morglug calls the biggest Waaagh! he can muster and promises your skull to the loudest boy.");
                case "OrcShaman":
                    return Milestone(progress,
                        "Nazgob feels the first moon-charm vanish and starts shouting at Mork. More goblins crowd the Hollow.",
                        "A second charm is nicked; green lightning dances over stronger boyz drawn by Nazgob's fury.",
                        "The Moon-Eater declares you cursed meat, and shamans, squigs and hard ladz gather for the omen.",
                        "Four charms are gone. Nazgob gulps sacred mushrooms and calls a much nastier mob into the Hollow.",
                        "The last moon-charm is yours. Gork and Mork are roaring in Nazgob's skull, and the whole Hollow boils up to kill you.");
                default:
                    return null;
            }
        }

        private static string GetVeteranEscalationText(string careerId,
            int tier)
        {
            string text;
            switch (careerId)
            {
                case "GrailDamsel": text = "The Blighted Chapel gathers still darker pilgrims and hardens its profane wards."; break;
                case "GrailKnight": text = "The Black Grail Procession returns with fresh black vows and harder lances."; break;
                case "MinorVampire": text = "The Sepulchre answers defeat by waking another rank of ancient retainers."; break;
                case "WarriorPriest": text = "The Purple Hand Purge turns defeat into a sterner sermon and gathers fresh Sigmarite veterans."; break;
                case "BloodKnight": text = "The Crimson Errantry answers defeat with a harsher challenge and worthier killers."; break;
                case "Mercenary": text = "The Black Company raises the bounty and hires another company of veterans."; break;
                case "WitchHunter": text = "The Ashen Tribunal answers defeat by calling in harder hunters and tightening every ward."; break;
                case "Necromancer": text = "The Restless Host learns from the slaughter and raises older, stronger dead."; break;
                case "BlackGrailKnight": text = "The Black Grail Reliquary Guard binds fresh retainers beneath unforgiving dark vows."; break;
                case "Necrarch": text = "The Necrarch Ossuary refines its failed defence into a stronger generation of horrors."; break;
                case "WarriorPriestUlric": text = "The White Wolf Hunt draws another pack of winter-hardened Ulrican warriors."; break;
                case "ImperialMagister": text = "The Ruined Observatory recalibrates its wards and gathers stronger Collegiate defenders."; break;
                case "Waywatcher": text = "The Beast-Hunters study the failed pursuit and gather deadlier Asrai stalkers."; break;
                case "Spellsinger": text = "The Defiled Waystone draws stronger Asrai and forest guardians into its wounded circle."; break;
                case "Warden": text = "Another band of oath-marked Asrai reaches the quarry-host before the Wild Hunt can claim them and joins the spear-line."; break;
                case "GreyLord": text = "The Grey College vault closes one failed shadow-path and opens a deadlier one."; break;
                case "KnightOldWorld": text = "The Black Road Brotherhood answers defeat by calling more veteran knights to its worn standards."; break;
                case "Ironbreaker": text = "The underhold adds the defeat to its grudge and reinforces every gate with harder Dawi."; break;
                case "Slayer": text = "The Troll King's Hunters answer survival with more doom-seekers sworn to the next reckoning."; break;
                case "Runelord": text = "The Rune Vault recuts its broken wards and summons sterner Dawi rune-guards."; break;
                case "OrcBoss": text = "The Rival Waaagh! takes survival as proof of a bigger scrap coming, and more boyz join in."; break;
                case "OrcShaman": text = "The Moon-Idol Hollow reads defeat as a louder omen and gathers an even nastier mob."; break;
                default: return null;
            }
            return text + " Its veteran strength has risen (" + tier +
                " of 5).";
        }
    }
}
