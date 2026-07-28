using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace TORCareerUniques
{
    internal enum EncounterKind
    {
        GuardianSite,
        RoamingHost
    }

    internal sealed class EncounterDefinition
    {
        public string CareerId;
        public string MapName;
        public EncounterKind Kind;
        public string[] RegionTokens;
        public string[] EnemyTokens;
        public bool CareerLed;
        // Encounter adversaries, authored followers, and the technical independent
        // owner are separate concepts. Every definition must author its combatant
        // and owner themes explicitly so a career hero can never inherit the race
        // or faction of the enemies that encounter was originally built to fight.
        public string[] TroopTokens;
        public string[] TroopAvoidTokens;
        public bool RequireThemedTroops;
        public string[] FactionTokens;
        public string[] FactionAvoidTokens;
        public string[] LootTokens;
        public int MinimumTroops;
        public int MaximumTroops;
        public float HomeRadius;
        public string SearchText;
    }

    internal static class EncounterCatalog
    {
        internal static readonly EncounterDefinition[] All = Build();
        internal static readonly Dictionary<string, EncounterDefinition> ByCareer = BuildMap();

        private static Dictionary<string, EncounterDefinition> BuildMap()
        {
            Dictionary<string, EncounterDefinition> result = new Dictionary<string, EncounterDefinition>(StringComparer.Ordinal);
            for (int i = 0; i < All.Length; i++)
                result.Add(All[i].CareerId, All[i]);
            return result;
        }

        private static EncounterDefinition[] Build()
        {
            string[] empireFollowers = A("empire", "imperial", "reikland",
                "middenland", "sigmar", "flagellant", "free_company",
                "greatsword", "halberdier", "handgunner");
            string[] empireAvoid = A("chaos", "cultist", "mutant", "daemon",
                "beastman", "ungor", "gor", "undead", "vampire", "skaven",
                "orc", "goblin", "norsca", "dark_elf", "druchii", "high_elf",
                "asur", "wood_elf", "asrai");
            string[] asraiFollowers = A("tor_we_", "wood_elf", "asrai",
                "eternal_guard", "glade_guard", "wildwood", "waywatcher",
                "wardancer", "deepwood", "warden");
            string[] asraiAvoid = A("dark_elf", "druchii", "high_elf", "asur",
                "beastman", "ungor", "gor", "chaos", "orc", "goblin",
                "skaven", "undead", "vampire", "troll");
            string[] dawiAvoid = A("chaos_dwarf", "chorf", "orc", "goblin",
                "greenskin", "skaven", "troll", "ogre", "undead", "vampire",
                "chaos");
            string[] genericIndependent = A("deserter", "outlaw", "brigand",
                "bandit", "looter");
            string[] forestIndependent = A("forest_bandit", "forest bandit",
                "bandit", "outlaw");
            string[] mountainIndependent = A("mountain_bandit", "mountain bandit",
                "bandit", "outlaw", "deserter");

            return new[]
            {
                EnemyLedSite("GrailDamsel", "The Blighted Grail Chapel",
                    A("breton", "mousillon", "couronne", "brionne"),
                    A("undead", "skeleton", "zombie", "mousillon", "black_grail", "vampire"),
                    A("breton", "grail", "holy", "damsel", "staff", "robe"),
                    "The ruined chapel is quiet. Search its desecrated reliquary for anything the servants of the Lady failed to recover."),

                EnemyLedHost("GrailKnight", "The Black Grail Procession",
                    A("breton", "mousillon", "couronne"),
                    A("black_grail", "undead", "vampire", "wight", "skeleton", "knight"),
                    A("breton", "grail", "lance", "knight", "plate"),
                    "A procession of corrupted knights circles the old roads, carrying trophies taken from fallen Grail pilgrims."),

                EnemyLedSite("MinorVampire", "The Sepulchre of the Red Duke",
                    A("mousillon", "breton", "sylvania", "vampire"),
                    A("undead", "vampire", "skeleton", "zombie", "wight", "crypt"),
                    A("vampire", "undead", "blood", "sword", "night"),
                    "The sealed sepulchre contains the effects of lesser blood-drinkers who once served a greater lord."),

                CareerLedHost("WarriorPriest", "The Purple Hand Purge",
                    A("empire", "reikland", "altdorf", "middenland"),
                    A("cultist", "chaos", "mutant", "heretic", "marauder", "nurgle", "tzeentch"),
                    A("sigmar", "empire", "hammer", "holy", "priest", "plate"),
                    "Lector Voss leads a hard-line Sigmarite purge along Imperial roads, carrying temple relics recovered from cells of the Purple Hand.",
                    empireFollowers, empireAvoid, genericIndependent, empireAvoid),

                EnemyLedHost("BloodKnight", "The Crimson Errantry",
                    A("sylvan", "vampire", "mousillon", "empire"),
                    A("blood_knight", "vampire", "undead", "wight", "black_knight", "grave_guard"),
                    A("vampire", "blood", "sword", "knight", "plate"),
                    "An arrogant company of Blood Dragons rides an endless challenge through the borderlands."),

                EnemyLedHost("Mercenary", "The Border Princes' Black Company",
                    A("border", "tilea", "estalia", "empire", "southern"),
                    A("mercenary", "brigand", "bandit", "outlaw", "pirate", "free_company"),
                    A("mercenary", "border", "tilea", "sword", "crossbow", "mail"),
                    "A renegade company patrols the trade roads with a pay chest full of trophies and unpaid contracts."),

                CareerLedSite("WitchHunter", "The Ashen Tribunal",
                    A("empire", "reikland", "middenland", "sylvania"),
                    A("witch", "cultist", "chaos", "mutant", "daemon", "heretic", "necromancer"),
                    A("witch_hunter", "silver", "pistol", "sword", "empire", "holy"),
                    "Krieger has turned a burned-out coven into an interrogation cell, guarding confiscated charms, execution tools and evidence under a hard Witch Hunter retinue.",
                    A("witch_hunter", "witchhunter", "inquisitor", "empire", "imperial",
                        "flagellant", "free_company", "sigmar"), empireAvoid,
                    genericIndependent, empireAvoid),

                EnemyLedSite("Necromancer", "The Barrow of the Restless Host",
                    A("sylvan", "vampire", "empire", "breton", "barrow"),
                    A("skeleton", "zombie", "undead", "wight", "grave_guard", "crypt"),
                    A("necromancer", "undead", "death", "staff", "bone", "magic"),
                    "The oldest burial chamber remains intact beneath the broken barrow. Its grave goods have not rested quietly."),

                CareerLedHost("BlackGrailKnight", "The Black Grail Reliquary Guard",
                    A("breton", "couronne", "mousillon", "brionne"),
                    A("grail", "breton", "knight", "pilgrim", "men_at_arms", "paladin"),
                    A("black_grail", "breton", "lance", "knight", "dark", "plate"),
                    "Sir Severin's black reliquary train hunts every rumour of the Black Grail, guarded by Mousillon's damned chivalry and undead retainers.",
                    A("black_grail", "mousillon", "undead", "vampire", "wight",
                        "black_knight", "grave_guard", "skeleton"),
                    A("paladin", "couronne", "brionne"),
                    A("black_grail", "mousillon", "undead", "vampire"), null),

                EnemyLedSite("Necrarch", "The Necrarch Ossuary",
                    A("sylvan", "vampire", "empire", "mountain"),
                    A("necrarch", "vampire", "undead", "skeleton", "zombie", "crypt", "bat"),
                    A("necrarch", "vampire", "bone", "staff", "magic", "death"),
                    "The ossuary's laboratories contain warped experiments, crumbling grimoires and the possessions of dead apprentices."),

                CareerLedHost("WarriorPriestUlric", "The White Wolf Hunt",
                    A("middenland", "empire", "nordland", "kislev", "ulric"),
                    A("beastman", "gor", "ungor", "chaos", "marauder", "wolf"),
                    A("ulric", "wolf", "winter", "hammer", "axe", "empire"),
                    "Hagen Wolfsbane leads an Ulrican hunt along the winter roads, bearing shrine relics recovered from beasts and raiders slain in the White Wolf's name.",
                    A("ulric", "wolf_priest", "white_wolf", "teutogen", "middenland",
                        "empire", "imperial", "flagellant"), empireAvoid,
                    genericIndependent, empireAvoid),

                CareerLedSite("ImperialMagister", "The Ruined Collegiate Observatory",
                    A("altdorf", "reikland", "empire", "college"),
                    A("cultist", "rogue_mage", "chaos", "mutant", "daemon", "sorcerer"),
                    A("empire", "magister", "college", "staff", "magic", "robe"),
                    "Volker's isolated Collegiate retinue holds the ruined observatory and its sealed instruments against cultists, rogue sorcerers and other trespassers.",
                    A("empire", "imperial", "college", "magister", "reikland",
                        "free_company", "greatsword", "halberdier"), empireAvoid,
                    genericIndependent, empireAvoid),

                CareerLedHost("Waywatcher", "The Beast-Hunters of Athel Loren",
                    A("athel", "loren", "wood_elf", "breton", "forest"),
                    A("beastman", "gor", "ungor", "chaos", "orc", "goblin"),
                    A("wood_elf", "asrai", "bow", "waywatcher", "forest", "arrow"),
                    "Aelir's Asrai hunters stalk the forest marches for Beastmen and other trespassers, carrying heirlooms of fallen scouts back beneath the boughs.",
                    asraiFollowers, asraiAvoid, forestIndependent, asraiAvoid),

                CareerLedSite("Spellsinger", "The Defiled Waystone",
                    A("athel", "loren", "wood_elf", "forest", "breton"),
                    A("beastman", "gor", "ungor", "chaos", "daemon", "orc"),
                    A("wood_elf", "asrai", "staff", "spellsinger", "forest", "magic"),
                    "Lethariel and her Asrai wardens contain a wounded waystone whose roots still hide generations of spellweaver offerings and dangerous lingering corruption.",
                    A("tor_we_", "wood_elf", "asrai", "glade_guard", "eternal_guard",
                        "waywatcher", "wardancer", "dryad", "treekin", "forest_spirit",
                        "wildwood"), asraiAvoid, forestIndependent, asraiAvoid),

                CareerLedHost("Warden", "The Wild Hunt's Quarry",
                    A("athel", "loren", "wood_elf", "forest", "breton"),
                    A("wild_hunt", "orion", "wood_elf", "asrai", "forest_spirit"),
                    A("wood_elf", "asrai", "spear", "warden", "forest", "shield"),
                    "An Asrai spear-line was condemned after its warden held a threatened glade instead of answering Orion's horn. The survivors now keep their old discipline while living as quarry of the Wild Hunt.",
                    asraiFollowers, asraiAvoid, forestIndependent, asraiAvoid),

                CareerLedSite("GreyLord", "The Vault Beneath the Grey College",
                    A("altdorf", "reikland", "empire", "grey", "college"),
                    A("cultist", "chaos", "daemon", "mutant", "sorcerer", "assassin"),
                    A("grey", "shadow", "empire", "staff", "magic", "robe"),
                    "Veyl's shadow agents have sealed a breached Grey College vault, holding its surviving secrets against cultists, sorcerers and thieves probing from below.",
                    A("empire", "imperial", "grey_lord", "grey_wizard", "reikland",
                        "free_company", "greatsword", "handgunner"), empireAvoid,
                    genericIndependent, empireAvoid),

                CareerLedHost("KnightOldWorld", "The Black Road Brotherhood",
                    A("empire", "breton", "border", "old_world"),
                    A("chaos", "marauder", "norsca", "reaver", "brigand", "knight"),
                    A("knight", "old_world", "runeblade", "sword", "plate", "empire"),
                    "Sir Eckhardt leads an independent brotherhood of veteran knights along the Black Road, preserving heirlooms recovered from broken orders and fallen comrades.",
                    A("reiksguard", "knight", "empire", "imperial", "greatsword",
                        "halberdier", "handgunner", "free_company", "mercenary"), empireAvoid,
                    genericIndependent, empireAvoid),

                CareerLedSite("Ironbreaker", "The Goblin-Delved Underhold",
                    A("dwarf", "karaz", "karak", "mountain"),
                    A("goblin", "night_goblin", "orc", "greenskin", "skaven", "troll"),
                    A("dwarf", "dawi", "gromril", "shield", "axe", "heavy"),
                    "Greenskins broke into the abandoned underhold, but Durgan Ironmantle and a surviving Dawi garrison still hold the inner gromril vault behind renewed barricades.",
                    A("dwarf", "dawi", "ironbreaker", "hammerer", "longbeard",
                        "quarreller", "thunderer", "gromril"), dawiAvoid,
                    mountainIndependent, dawiAvoid),

                CareerLedHost("Slayer", "The Troll King's Hunters",
                    A("dwarf", "karaz", "karak", "mountain", "troll"),
                    A("troll", "goblin", "orc", "greenskin", "ogre"),
                    A("dwarf", "dawi", "slayer", "axe", "rune", "troll"),
                    "Kragni Oathscar leads a wandering Slayer oath-host that hunts the Troll King's brood through the high roads, carrying the axes of comrades whose doom remains unfinished.",
                    A("dwarf", "dawi", "slayer", "troll_slayer", "giant_slayer",
                        "hammerer", "longbeard"), dawiAvoid,
                    mountainIndependent, dawiAvoid),

                CareerLedSite("Runelord", "The Desecrated Rune Vault",
                    A("dwarf", "karaz", "karak", "mountain"),
                    A("goblin", "orc", "greenskin", "skaven", "troll"),
                    A("dwarf", "dawi", "rune", "hammer", "anvil", "gromril"),
                    "The outer seals were broken by desecrators, but Baragor Embermark and oath-bound Dawi rune-guards still hold the inner vault and its ancestral work.",
                    A("dwarf", "dawi", "runesmith", "runelord", "ironbreaker",
                        "hammerer", "longbeard", "quarreller", "thunderer", "rune"), dawiAvoid,
                    mountainIndependent, dawiAvoid),

                EnemyLedHost("OrcBoss", "Grubnash's Rival Waaagh!",
                    A("badlands", "orc", "greenskin", "dwarf", "border"),
                    A("orc", "goblin", "greenskin", "black_orc", "boar", "troll"),
                    A("orc", "greenskin", "choppa", "axe", "boss", "heavy"),
                    "A rival boss roams the Badlands with a loud, well-armed Waaagh! and the trophies of every challenger it has crushed."),

                EnemyLedSite("OrcShaman", "The Moon-Idol Hollow",
                    A("badlands", "orc", "greenskin", "dwarf", "forest"),
                    A("goblin", "night_goblin", "orc", "greenskin", "shaman", "squig"),
                    A("orc", "goblin", "shaman", "staff", "moon", "magic"),
                    "The hollow is crowded with moon idols, mushroom smoke and offerings stolen by generations of greenskin shamans.")
            };
        }

        private static EncounterDefinition EnemyLedSite(string careerId, string name,
            string[] region, string[] combatants, string[] loot, string search)
        {
            return E(careerId, name, EncounterKind.GuardianSite, region,
                combatants, false, combatants, null, combatants, null, loot,
                110, 135, 0f, search);
        }

        private static EncounterDefinition EnemyLedHost(string careerId, string name,
            string[] region, string[] combatants, string[] loot, string search)
        {
            return E(careerId, name, EncounterKind.RoamingHost, region,
                combatants, false, combatants, null, combatants, null, loot,
                100, 125, 14f, search);
        }

        private static EncounterDefinition CareerLedSite(string careerId, string name,
            string[] region, string[] adversaries, string[] loot, string search,
            string[] troopTokens, string[] troopAvoidTokens,
            string[] factionTokens, string[] factionAvoidTokens)
        {
            return E(careerId, name, EncounterKind.GuardianSite, region,
                adversaries, true, troopTokens, troopAvoidTokens, factionTokens,
                factionAvoidTokens, loot, 110, 135, 0f, search);
        }

        private static EncounterDefinition CareerLedHost(string careerId, string name,
            string[] region, string[] adversaries, string[] loot, string search,
            string[] troopTokens, string[] troopAvoidTokens,
            string[] factionTokens, string[] factionAvoidTokens)
        {
            return E(careerId, name, EncounterKind.RoamingHost, region,
                adversaries, true, troopTokens, troopAvoidTokens, factionTokens,
                factionAvoidTokens, loot, 100, 125, 14f, search);
        }

        private static EncounterDefinition E(string careerId, string name,
            EncounterKind kind, string[] region, string[] adversaries,
            bool careerLed, string[] troopTokens, string[] troopAvoidTokens,
            string[] factionTokens, string[] factionAvoidTokens,
            string[] loot, int minimum, int maximum, float radius, string search)
        {
            if (troopTokens == null || troopTokens.Length == 0)
                throw new InvalidOperationException("Encounter " + careerId +
                    " has no authored combatant theme.");
            if (factionTokens == null || factionTokens.Length == 0)
                throw new InvalidOperationException("Encounter " + careerId +
                    " has no authored independent-owner theme.");
            return new EncounterDefinition
            {
                CareerId = careerId,
                MapName = name,
                Kind = kind,
                RegionTokens = region,
                EnemyTokens = adversaries,
                CareerLed = careerLed,
                TroopTokens = troopTokens,
                TroopAvoidTokens = troopAvoidTokens,
                RequireThemedTroops = true,
                FactionTokens = factionTokens,
                FactionAvoidTokens = factionAvoidTokens,
                LootTokens = loot,
                MinimumTroops = minimum,
                MaximumTroops = maximum,
                HomeRadius = radius,
                SearchText = search
            };
        }

        private static string[] A(params string[] values) { return values; }
    }

    internal sealed partial class UniqueEncounterBehavior : CampaignBehaviorBase
    {
        private const string PartyPrefix = "torcu_enc_";
        private const string SitePrefix = "torcu_site_";
        private const string SiteMenuId = "torcu_guardian_site";
        private List<string> _claimedCareerIds = new List<string>();
        private Dictionary<string, double> _respawnAtDay = new Dictionary<string, double>(StringComparer.Ordinal);
        private Dictionary<string, string> _anchorSettlementIds = new Dictionary<string, string>(StringComparer.Ordinal);
        private Dictionary<string, string> _siteSettlementIds = new Dictionary<string, string>(StringComparer.Ordinal);
        private Dictionary<string, int> _attempts = new Dictionary<string, int>(StringComparer.Ordinal);
        private Dictionary<string, int> _spawnSerials = new Dictionary<string, int>(StringComparer.Ordinal);
        private List<string> _pendingRewards = new List<string>();
        private List<string> _discoveredSetPieces = new List<string>();
        private int _encounterHeroSchemaVersion;
        private int _navigationMigrationVersion;
        private int _anchorSelectionSchemaVersion;
        private int _discoveryMigrationVersion;
        private float _uiDelay;
        private bool _inquiryOpen;
        private bool _sessionReady;
        private List<TroopCandidate> _troopCatalog;
        private int _initializationCursor;
        private float _initializationDelay;
        private bool _initializationComplete;
        private HashSet<string> _releasedHostPartyIds = new HashSet<string>(StringComparer.Ordinal);
        private HashSet<string> _intentionalDestroyPartyIds = new HashSet<string>(StringComparer.Ordinal);
        private HashSet<string> _resolvedGuardianSiteIds = new HashSet<string>(StringComparer.Ordinal);
        private HashSet<string> _rejectedSpawnAnchorIds =
            new HashSet<string>(StringComparer.Ordinal);
        private List<MobileParty> _deferredEncounterPartyCleanup =
            new List<MobileParty>();
        private Dictionary<string, List<MobileParty>> _activeEncountersByCareer =
            new Dictionary<string, List<MobileParty>>(StringComparer.Ordinal);
        private string _returnMenuId;
        private string _pendingResultTitle;
        private string _pendingResultText;
        private float _resultUiDelay;

        internal UniqueEncounterBehavior()
        {
            AdminBridge.Attach(this);
        }

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.MapEventEnded.AddNonSerializedListener(this, OnMapEventEnded);
            CampaignEvents.MobilePartyDestroyed.AddNonSerializedListener(this, OnMobilePartyDestroyed);
            CampaignEvents.OnCollectLootsItemsEvent.AddNonSerializedListener(this, OnCollectLootItems);
            CampaignEvents.PlayerInventoryExchangeEvent.AddNonSerializedListener(this, OnPlayerInventoryExchange);
            RegisterEncounterHeroEvents();
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("torcu_claimed_careers", ref _claimedCareerIds);
            dataStore.SyncData("torcu_respawn_days", ref _respawnAtDay);
            dataStore.SyncData("torcu_anchor_settlements", ref _anchorSettlementIds);
            dataStore.SyncData("torcu_native_site_settlements", ref _siteSettlementIds);
            dataStore.SyncData("torcu_attempts", ref _attempts);
            dataStore.SyncData("torcu_spawn_serials", ref _spawnSerials);
            dataStore.SyncData("torcu_pending_rewards", ref _pendingRewards);
            dataStore.SyncData("torcu_discovered_set_pieces", ref _discoveredSetPieces);
            dataStore.SyncData("torcu_encounter_heroes", ref _encounterHeroes);
            dataStore.SyncData("torcu_successor_heroes", ref _successorHeroes);
            dataStore.SyncData("torcu_mastery_proven",
                ref _masteryProvenCareerIds);
            dataStore.SyncData("torcu_mastery_victories",
                ref _masteryVictoryCareerIds);
            dataStore.SyncData("torcu_recruited_originals",
                ref _recruitedOriginalCareerIds);
            dataStore.SyncData("torcu_pending_recognition",
                ref _pendingRecognitionCareerIds);
            dataStore.SyncData("torcu_set_mastery_schema",
                ref _setMasterySchemaVersion);
            dataStore.SyncData("torcu_encounter_hero_schema",
                ref _encounterHeroSchemaVersion);
            dataStore.SyncData("torcu_navigation_migration",
                ref _navigationMigrationVersion);
            dataStore.SyncData("torcu_anchor_selection_schema",
                ref _anchorSelectionSchemaVersion);
            dataStore.SyncData("torcu_discovery_migration",
                ref _discoveryMigrationVersion);
            dataStore.SyncData("torcu_veteran_clears", ref _veteranClears);
            dataStore.SyncData("torcu_encounter_strength_schema",
                ref _encounterStrengthSchemaVersion);
            EnsureState();
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            try
            {
                EnsureState();
                // v1.7.14 created dedicated encounter clans with
                // IsBanditFaction=true. Clear that unsafe classification before
                // any campaign-map hourly clan tick can observe loaded clans.
                NormalizeDedicatedEncounterClanClassification();
                _suppressEscalationNotifications = true;
                AddSiteMenus(starter);
                _sessionReady = true;
                _initializationCursor = 0;
                _initializationDelay = 2.0f;
                _initializationComplete = false;
                _troopCatalog = null;
                _releasedHostPartyIds.Clear();
                _intentionalDestroyPartyIds.Clear();
                _resolvedGuardianSiteIds.Clear();
                _rejectedSpawnAnchorIds.Clear();
                _deferredEncounterPartyCleanup.Clear();
                ReconcileActiveEncounterIndexOnce();
                _returnMenuId = null;
                _pendingResultTitle = null;
                _pendingResultText = null;
                _resultUiDelay = 0f;
                MigrateRoamingHostAnchors();
                MigrateV143NavigationState();
                MigrateSetMasteryState();
                ReconcileEncounterHeroes();
                RepairV140EncounterHeroState();
                MigrateGuardianSiteMappings();
                ResolveAllGuardianSites();
                MigrateLegacyGuardianParties();
                CompleteInitializationWithoutPolling();
                CareerUniqueRuntime.Tick();
                SetItemRuntime.Tick();
                SetItemRuntime.IndexKnownSetItemsOnce();
                // Existing generated items are visually reconciled once at campaign
                // session launch. The generic runtime tick never performs visual
                // catalogue/archetype resolution, so inventory/MCM activity cannot
                // retrigger this work.
                SetItemRuntime.MigrateKnownVisualsOnce();
                RuntimePerformanceGate.OnCampaignSessionLaunched();
                if (_discoveryMigrationVersion < 1 &&
                    SetItemRuntime.MigrateLegacyDiscoveryClaims())
                    _discoveryMigrationVersion = 1;
                // Schema 4 replaces one bespoke intrinsic property on every set item
                // with a real TOR enchantment/blessing/rune. Rebuild only player-owned
                // legacy copies once, preserving modifiers and admin-copy isolation.
                if (_discoveryMigrationVersion >= 1 &&
                    _discoveryMigrationVersion < 4 &&
                    SetItemRuntime.MigratePlayerOwnedItemsAndDiscovery(true))
                    _discoveryMigrationVersion = 4;
                ReconcileClaims();
                MigrateEncounterStrengthAndAi();
                EncounterAffinityRuntime.OnCampaignSessionLaunched();
                _suppressEscalationNotifications = false;
                if (RequiresApplicationTick())
                    AdminBridge.RequestApplicationTick();
                ModLog.LogMcmStatus();
                ModLog.Info("Campaign session launched. Encounter initialization completed without campaign-map polling for " + EncounterCatalog.All.Length + " definitions.");
            }
            catch (Exception ex)
            {
                _suppressEscalationNotifications = false;
                _sessionReady = false;
                ModLog.Error("Session launch initialization failed: " + FormatException(ex));
            }
        }

        private void OnHourlyTick()
        {
            if (!_sessionReady)
                return;
            try
            {
                ProcessEncounterHeroRecoveries();
            }
            catch (Exception ex)
            {
                ModLog.Error("Hourly encounter-recovery processing failed: " + FormatException(ex));
            }
        }

        private void OnDailyTick()
        {
            if (!_sessionReady || !_initializationComplete)
                return;
            try
            {
                ScheduleUntrackedMissingEncounters();
                EnsureMissingRoamingHosts();
            }
            catch (Exception ex)
            {
                ModLog.Error("Daily missing-host maintenance failed: " + FormatException(ex));
            }
        }

        private void OnMapEventEnded(MapEvent mapEvent)
        {
            try
            {
                if (mapEvent == null)
                    return;

                MapEventSide losingSide = null;
                if (mapEvent.WinningSide == BattleSideEnum.Attacker)
                    losingSide = mapEvent.DefenderSide;
                else if (mapEvent.WinningSide == BattleSideEnum.Defender)
                    losingSide = mapEvent.AttackerSide;
                if (losingSide == null)
                {
                    CloseGuardianEncounters(mapEvent.AttackerSide);
                    CloseGuardianEncounters(mapEvent.DefenderSide);
                    return;
                }

                bool playerVictory = mapEvent.IsPlayerMapEvent && mapEvent.WinningSide == mapEvent.PlayerSide;
                List<MobileParty> defeatedEncounters = FindEncounterParties(losingSide);
                for (int i = 0; i < defeatedEncounters.Count; i++)
                {
                    MobileParty party = defeatedEncounters[i];
                    string careerId = CareerFromPartyId(party.StringId);
                    EncounterDefinition definition;
                    if (!EncounterCatalog.ByCareer.TryGetValue(careerId, out definition))
                        continue;

                    // The party is disposable; its persistent named hero survives the
                    // defeat independently and recovers during the normal cooldown.
                    QueueEncounterHeroRecovery(careerId,
                        "defeat of " + definition.MapName);

                    // Defeat timing is independent of who won. AI armies, autoresolve,
                    // the player, and other campaign systems all create the same cooldown.
                    ScheduleRespawn(careerId);

                    if (playerVictory)
                    {
                        EvaluateSetMasteryVictory(definition, party);
                        AdvanceVeteranTierAfterPlayerVictory(definition);
                        QueueReward(careerId);
                        if (definition.Kind == EncounterKind.GuardianSite)
                        {
                            CareerUniqueRuntime.Notify("The defenders of " + definition.MapName + " are broken. Return to the site and search it before they regroup.");
                            ModLog.Info("Player cleared " + definition.MapName + "; contextual site search is now available and respawn is scheduled.");
                        }
                        else
                        {
                            ModLog.Info("Player defeated " + definition.MapName + "; explicit aftermath search queued and respawn scheduled.");
                        }
                    }
                    else
                    {
                        ModLog.Info(definition.MapName + " was defeated by campaign AI; no player reward was queued and the normal respawn cooldown was scheduled.");
                    }

                    if (party.IsActive)
                        QueueDeferredEncounterPartyCleanup(party);
                }

                MapEventSide winningSide = mapEvent.WinningSide == BattleSideEnum.Attacker
                    ? mapEvent.AttackerSide : mapEvent.DefenderSide;
                CloseGuardianEncounters(winningSide);
            }
            catch (Exception ex)
            {
                ModLog.Error("MapEventEnded handling failed: " + FormatException(ex));
            }
        }

        private void CloseGuardianEncounters(MapEventSide side)
        {
            List<MobileParty> parties = FindEncounterParties(side);
            for (int i = 0; i < parties.Count; i++)
            {
                MobileParty party = parties[i];
                string careerId = CareerFromPartyId(party.StringId);
                EncounterDefinition definition;
                if (!EncounterCatalog.ByCareer.TryGetValue(careerId,
                    out definition) ||
                    definition.Kind != EncounterKind.GuardianSite)
                    continue;

                // A guardian party is materialized only for the immediate player
                // encounter. It must never survive any map-event closure, including
                // victory, defeat, retreat or an event with no declared winning side.
                if (party.IsActive)
                    QueueDeferredEncounterPartyCleanup(party);
                _respawnAtDay.Remove(careerId);
                if (!PrepareEncounterHeroForRecovery(careerId,
                    ResolveAnchor(definition), "closure of " + definition.MapName))
                    QueueEncounterHeroRecovery(careerId,
                        "closure of " + definition.MapName);
                ModLog.Info("Closed surviving guardian encounter " +
                    definition.MapName +
                    " without leaving an AI-reachable campaign party.");
            }
        }

        private void QueueDeferredEncounterPartyCleanup(MobileParty party)
        {
            if (party == null || !party.IsActive)
                return;
            if (!_deferredEncounterPartyCleanup.Contains(party))
                _deferredEncounterPartyCleanup.Add(party);
            AdminBridge.RequestApplicationTick();
        }

        private bool ProcessDeferredEncounterPartyCleanup()
        {
            if (_deferredEncounterPartyCleanup == null ||
                _deferredEncounterPartyCleanup.Count == 0)
                return false;

            bool completedAny = false;
            for (int i = _deferredEncounterPartyCleanup.Count - 1; i >= 0; i--)
            {
                MobileParty party = _deferredEncounterPartyCleanup[i];
                if (party == null || !party.IsActive)
                {
                    _deferredEncounterPartyCleanup.RemoveAt(i);
                    completedAny = true;
                    continue;
                }

                // MapEventEnded is a notification boundary, not a lifetime boundary.
                // Other listeners and native post-battle code can still hold the party.
                // Never destroy it from inside that callback; wait until Bannerlord has
                // fully detached it from the MapEvent, then dispose it on the next
                // requested application tick.
                if (party.MapEvent != null)
                    continue;

                try
                {
                    DestroyPartyWithoutCooldown(party);
                    if (!party.IsActive)
                    {
                        _deferredEncounterPartyCleanup.RemoveAt(i);
                        completedAny = true;
                    }
                    else
                        ModLog.Error("Deferred encounter-party cleanup left " +
                            party.StringId + " active; cleanup remains queued.");
                }
                catch (Exception ex)
                {
                    ModLog.Error("Deferred encounter-party cleanup failed for " +
                        party.StringId + ": " + FormatException(ex));
                }
            }
            return completedAny;
        }

        private void OnCollectLootItems(PartyBase receivingParty, ItemRoster lootRoster)
        {
            try
            {
                if (!_sessionReady || receivingParty == null || lootRoster == null ||
                    receivingParty != PartyBase.MainParty)
                    return;

                int normalized = SetItemRuntime.NormalizeEncounterHeroLootRoster(lootRoster);
                if (normalized > 0)
                {
                    ModLog.Info("Normalized " + normalized +
                        " encounter-hero set item stack(s) in the player battle-loot roster while preserving their equipment modifiers.");
                }
            }
            catch (Exception ex)
            {
                ModLog.Error("Battle-loot set-item normalization failed: " + FormatException(ex));
            }
        }

        private void OnPlayerInventoryExchange(
            List<ValueTuple<ItemRosterElement, int>> boughtItems,
            List<ValueTuple<ItemRosterElement, int>> soldItems,
            bool isTrading)
        {
            try
            {
                if (!_sessionReady)
                    return;

                // Inventory exchange is event-driven and runs only when the player
                // confirms an inventory/loot screen. It is the authoritative point
                // at which a loot-pool item becomes player-owned. Bannerlord stores
                // the accepted count in ItemRosterElement.Amount, not tuple Item2.
                SetItemRuntime.ProcessPlayerInventoryAcquisitions(boughtItems);
                SetItemRuntime.Tick();
                ReconcileClaims();
            }
            catch (Exception ex)
            {
                ModLog.Error("Player inventory set-discovery reconciliation failed: " +
                    FormatException(ex));
            }
        }

        private void OnMobilePartyDestroyed(MobileParty party, PartyBase destroyerParty)
        {
            try
            {
                if (party == null || String.IsNullOrEmpty(party.StringId) || !party.StringId.StartsWith(PartyPrefix, StringComparison.Ordinal))
                    return;

                UnregisterActiveEncounter(party);

                if (_intentionalDestroyPartyIds.Remove(party.StringId))
                {
                    ModLog.Verbose("Intentional TORCU cleanup ignored for cooldown tracking: " + party.StringId + ".");
                    return;
                }

                string careerId = CareerFromPartyId(party.StringId);
                EncounterDefinition definition;
                if (!EncounterCatalog.ByCareer.TryGetValue(careerId, out definition))
                    return;

                QueueEncounterHeroRecovery(careerId,
                    "destruction of " + definition.MapName);
                ScheduleRespawn(careerId);
                string victor = destroyerParty == PartyBase.MainParty ? "the player" :
                    (destroyerParty == null || destroyerParty.Name == null ? "campaign AI" : destroyerParty.Name.ToString());
                ModLog.Info(definition.MapName + " party was destroyed by " + victor + "; respawn cooldown recorded until day " +
                    _respawnAtDay[careerId].ToString("0.00") + ".");
            }
            catch (Exception ex)
            {
                ModLog.Error("MobilePartyDestroyed handling failed: " + FormatException(ex));
            }
        }

        internal bool RequiresApplicationTick()
        {
            if (!_sessionReady || _inquiryOpen)
                return false;
            if (!_initializationComplete)
                return true;
            if (_deferredEncounterPartyCleanup != null &&
                _deferredEncounterPartyCleanup.Count > 0)
                return true;
            if (!String.IsNullOrEmpty(_pendingResultText))
                return true;
            if (HasPendingRecognition())
                return true;
            return FindPendingRoamingRewardIndex() >= 0;
        }

        internal void ProcessApplicationTick(float dt)
        {
            if (!_sessionReady)
                return;

            // The application tick exists only for concrete deferred UI/party work.
            // Do not rescan pending hero recoveries every rendered frame while another
            // inquiry (including MCM) prevents the pending UI from being shown.  Recovery
            // is already processed hourly; run it here only when deferred party teardown
            // actually completed and may have detached a leader this frame.
            if (ProcessDeferredEncounterPartyCleanup())
                ProcessEncounterHeroRecoveries();
            ProcessIncrementalInitialization(dt);

            if (ProcessPendingRecognition())
                return;

            if (ProcessPendingResult(dt))
                return;

            if (_pendingRewards == null || _pendingRewards.Count == 0 || _inquiryOpen)
                return;

            _uiDelay -= dt;
            if (_uiDelay > 0f || IsPlayerStillInMapEvent())
                return;

            // Guardian-site rewards are deliberately not opened automatically.  The
            // cleared site remains searchable until the player returns and selects
            // the contextual search option.  Only roaming-host aftermaths are
            // presented here because their party object is gone after the battle.
            int rewardIndex = FindPendingRoamingRewardIndex();
            if (rewardIndex < 0)
                return;

            string careerId = _pendingRewards[rewardIndex];
            EncounterDefinition definition;
            if (!EncounterCatalog.ByCareer.TryGetValue(careerId, out definition))
            {
                _pendingRewards.RemoveAt(rewardIndex);
                return;
            }

            _inquiryOpen = true;
            bool shown = InquiryHelper.ShowChoice(
                definition.MapName,
                GetHostSearchPrompt(definition),
                GetHostSearchAction(definition),
                "Leave the dead",
                delegate
                {
                    _inquiryOpen = false;
                    RemovePendingReward(careerId);
                    ResolveReward(definition);
                },
                delegate
                {
                    _inquiryOpen = false;
                    RemovePendingReward(careerId);
                    ModLog.Info("Player left the spoils of " + definition.MapName + " unsearched.");
                });

            if (!shown)
            {
                // Never grant a relic or consolation loot because a UI call failed.
                // Retain the pending aftermath and retry later.
                _inquiryOpen = false;
                _uiDelay = 5f;
                ModLog.Error("Roaming-host search inquiry could not be displayed; reward remains pending and nothing was granted.");
            }
        }

        private int FindPendingRoamingRewardIndex()
        {
            for (int i = 0; i < _pendingRewards.Count; i++)
            {
                EncounterDefinition definition;
                if (EncounterCatalog.ByCareer.TryGetValue(_pendingRewards[i], out definition) && definition.Kind == EncounterKind.RoamingHost)
                    return i;
            }
            return -1;
        }

        private bool ProcessPendingResult(float dt)
        {
            if (String.IsNullOrEmpty(_pendingResultText))
                return false;

            if (_inquiryOpen)
                return true;

            _resultUiDelay -= dt;
            if (_resultUiDelay > 0f || IsPlayerStillInMapEvent())
                return true;

            string title = String.IsNullOrEmpty(_pendingResultTitle) ? "TOR Career Uniques" : _pendingResultTitle;
            string text = _pendingResultText;
            _pendingResultTitle = null;
            _pendingResultText = null;
            _resultUiDelay = 0f;

            _inquiryOpen = true;
            bool shown = InquiryHelper.ShowMessage(title, text, delegate
            {
                _inquiryOpen = false;
            });
            if (!shown)
            {
                _inquiryOpen = false;
                CareerUniqueRuntime.Notify(text);
                ModLog.Error("Reward-result inquiry could not be displayed; campaign-feed notification fallback was used.");
            }
            return true;
        }

        private void ProcessIncrementalInitialization(float dt)
        {
            if (_initializationComplete)
                return;

            _initializationDelay -= dt;
            if (_initializationDelay > 0f)
                return;
            _initializationDelay = 0.25f;

            try
            {
                if (_troopCatalog == null)
                {
                    _troopCatalog = BuildTroopCatalog();
                    ModLog.Info("Safe troop metadata catalogue built once for this session. Candidates: " + _troopCatalog.Count + ".");
                }

                if (_initializationCursor >= EncounterCatalog.All.Length)
                {
                    _initializationComplete = true;
                    ModLog.Info("Incremental encounter initialization completed.");
                    return;
                }

                EncounterDefinition definition = EncounterCatalog.All[_initializationCursor++];
                try
                {
                    EnsureEncounter(definition, false);
                }
                catch (Exception ex)
                {
                    ModLog.Error("Initialization of " + definition.MapName + " failed and was skipped: " + FormatException(ex));
                }

                if (_initializationCursor >= EncounterCatalog.All.Length)
                {
                    _initializationComplete = true;
                    ModLog.Info("Incremental encounter initialization completed.");
                }
            }
            catch (Exception ex)
            {
                _initializationComplete = true;
                ModLog.Error("Incremental encounter initialization was disabled after an unrecoverable error: " + FormatException(ex));
            }
        }

        private void CompleteInitializationWithoutPolling()
        {
            int remaining = 64;
            while (!_initializationComplete && --remaining >= 0)
            {
                _initializationDelay = 0f;
                ProcessIncrementalInitialization(1f);
            }
        }

        internal void ShowEncounter(string careerId)
        {
            EnsureState();
            for (int i = 0; i < EncounterCatalog.All.Length; i++)
            {
                EncounterDefinition definition = EncounterCatalog.All[i];
                if (String.Equals(definition.CareerId, careerId,
                    StringComparison.Ordinal))
                {
                    ShowEncounterDetails(definition);
                    return;
                }
            }
            InquiryHelper.ShowMessage("TOR Career Uniques",
                "No relic encounter is defined for " + careerId + ".");
        }

        internal void AdminRespawnMissing()
        {
            EnsureState();
            ProcessEncounterHeroRecoveries();
            int cleared = 0;
            int guardianCooldownsCleared = 0;
            int spawned = 0;
            int alreadyActive = 0;
            List<string> failures = new List<string>();
            for (int i = 0; i < EncounterCatalog.All.Length; i++)
            {
                EncounterDefinition definition = EncounterCatalog.All[i];
                if (definition.Kind == EncounterKind.GuardianSite)
                {
                    if (_respawnAtDay.Remove(definition.CareerId))
                    {
                        cleared++;
                        guardianCooldownsCleared++;
                    }
                    continue;
                }
                if (FindActiveEncounter(definition.CareerId) != null)
                {
                    alreadyActive++;
                    continue;
                }
                if (_respawnAtDay.Remove(definition.CareerId))
                    cleared++;

                string recoveryError;
                if (!PrepareEncounterHeroForImmediateRespawn(definition,
                    out recoveryError))
                {
                    failures.Add(definition.MapName + ": " + recoveryError);
                    _respawnAtDay[definition.CareerId] = CampaignTime.Now.ToDays;
                    continue;
                }

                MobileParty party = SpawnEncounterParty(definition, false, null);
                if (party != null && party.IsActive)
                    spawned++;
                else
                {
                    failures.Add(definition.MapName + ": party creation failed; " +
                        "see TORCareerUniques.log");
                    // Keep an expired tombstone so normal maintenance retries instead
                    // of interpreting the missing party as a fresh defeat.
                    _respawnAtDay[definition.CareerId] = CampaignTime.Now.ToDays;
                }
            }
            ModLog.Info("Admin respawn requested. Cooldowns cleared=" + cleared +
                ", roaming hosts spawned=" + spawned + ", already active=" +
                alreadyActive + ", failures=" + failures.Count + ".");
            string result = "Guardian-site cooldowns cleared: " +
                guardianCooldownsCleared +
                ".\nRoaming hosts recreated: " + spawned +
                ".\nAlready active: " + alreadyActive + ".";
            if (failures.Count > 0)
                result += "\n\nCould not recreate:\n- " +
                    String.Join("\n- ", failures.ToArray());
            result += "\n\nClaimed relic state was not changed.";
            InquiryHelper.ShowMessage("TOR Career Uniques", result);
        }

        private static string GetSiteSearchAction(EncounterDefinition definition)
        {
            switch (definition.CareerId)
            {
                case "GrailDamsel": return "Search the desecrated reliquary";
                case "MinorVampire": return "Break the seals of the burial vault";
                case "WitchHunter": return "Search the Tribunal evidence vaults";
                case "Necromancer": return "Open the oldest burial chamber";
                case "Necrarch": return "Search the ossuary laboratories";
                case "ImperialMagister": return "Examine the sealed Collegiate cabinets";
                case "Spellsinger": return "Follow the waystone roots to the hidden offerings";
                case "GreyLord": return "Search the breached Grey College vault";
                case "Ironbreaker": return "Clear the barricades and inspect the gromril cache";
                case "Runelord": return "Examine the desecrated rune repositories";
                case "OrcShaman": return "Ransack the moon-idol's offering pit";
                default: return "Search the cleared site";
            }
        }

        private static string GetSiteSearchPrompt(EncounterDefinition definition)
        {
            switch (definition.CareerId)
            {
                case "GrailDamsel": return "The dead lie still around the chapel. The reliquary door hangs open, its silver wards blackened but not entirely broken.";
                case "MinorVampire": return "The sepulchre is silent. Dust drifts through the opened crypt while old blood-seals glimmer beneath the stone lid.";
                case "WitchHunter": return "The Tribunal defenders are broken. Confiscated charms, coded ledgers and a hunter's locked evidence chest remain below the ash.";
                case "Necromancer": return "The restless host has fallen. Beneath the barrow, an older chamber remains sealed behind grave-stone and bone.";
                case "Necrarch": return "The ossuary's guardians are destroyed. Distillation tables, bone cabinets and forbidden notes remain in the inner laboratory.";
                case "ImperialMagister": return "The Observatory defenders are broken. Several Collegiate cabinets survived the collapse beneath a warded brass seal.";
                case "Spellsinger": return "The waystone guardians are broken and the corruption briefly recedes. Its roots now reveal offerings hidden by earlier spellweavers.";
                case "GreyLord": return "The vault defenders are gone. Shadowed alcoves and a breached Grey College strongbox remain beyond the false wall.";
                case "Ironbreaker": return "The underhold defenders are broken. Behind the barricades lies a Dawi cache of tools, armour and unclaimed gromril work.";
                case "Runelord": return "The vault is quiet. Broken tablets and soot-covered rune repositories await careful inspection.";
                case "OrcShaman": return "The rival shamans are scattered. Teef, fetishes and stolen weapons fill the offering pit beneath the moon-idol.";
                default: return definition.SearchText;
            }
        }

        private static string GetHostSearchAction(EncounterDefinition definition)
        {
            switch (definition.CareerId)
            {
                case "GrailKnight": return "Search the corrupted reliquary wagons";
                case "WarriorPriest": return "Search the purge's recovered temple chests";
                case "BloodKnight": return "Search the crimson knights' trophies";
                case "Mercenary": return "Open the Black Company's pay chest";
                case "BlackGrailKnight": return "Search the Black Grail reliquary train";
                case "WarriorPriestUlric": return "Search the White Wolf Hunt's shrine reliquaries";
                case "Waywatcher": return "Search the beast-hunters' recovered heirlooms";
                case "Warden": return "Search the quarry camp";
                case "KnightOldWorld": return "Search the Brotherhood's heirloom chests";
                case "Slayer": return "Search the oath-host's relic packs";
                case "OrcBoss": return "Ransack the rival warboss's loot pile";
                default: return "Search the defeated host";
            }
        }

        private static string GetHostSearchPrompt(EncounterDefinition definition)
        {
            return "The fighting is over, but the defeated host's baggage, trophies and guarded chests remain among the dead. Searching them may uncover the relic you came for, though stopping now gives survivors time to scatter with whatever they can carry.";
        }

        private void ResolveReward(EncounterDefinition definition)
        {
            EnsureState();
            int attempts;
            _attempts.TryGetValue(definition.CareerId, out attempts);
            attempts++;
            _attempts[definition.CareerId] = attempts;

            string activeCareer = CareerUniqueRuntime.GetCurrentCareerId();
            if (ModConfig.RequireMatchingCareer && !String.Equals(activeCareer, definition.CareerId, StringComparison.Ordinal))
            {
                string mismatchLoot = GrantConsolationLoot(definition, attempts);
                ReportReward(definition, "This relic does not answer to your current career. You recovered themed loot" + FormatLoot(mismatchLoot) + ".");
                ModLog.Info(definition.MapName + " defeated with career " + (activeCareer ?? "none") +
                    "; required " + definition.CareerId + ". No set-piece roll. Attempt " + attempts + ".");
                return;
            }

            int chance = ModConfig.DropChancePercent;
            int roll = new Random(StableHash(definition.CareerId + ":" + attempts + ":" + CampaignTime.Now.ToDays)).Next(1, 101);
            ModLog.Info("Set-piece roll at " + definition.MapName + ": " + roll + " <= " + chance +
                " for " + definition.CareerId + " (attempt " + attempts + ").");

            if (roll <= chance)
            {
                string itemName;
                bool advancedDiscovery;
                string error;
                int pieceSeed = StableHash(definition.CareerId + ":piece:" + attempts + ":" + CampaignTime.Now.ToDays);
                _resolvingRewardCareerId = definition.CareerId;
                _deferredEscalationText = null;
                bool granted;
                try
                {
                    granted = SetItemRuntime.TryGrantRandomRewardPiece(
                        definition.CareerId, pieceSeed, out itemName,
                        out advancedDiscovery, out error);
                }
                finally
                {
                    _resolvingRewardCareerId = null;
                }
                if (granted)
                {
                    int recovered = SetItemRuntime.GetRecoveredCount(definition.CareerId);
                    if (recovered >= 5)
                        MarkClaimed(definition.CareerId);
                    string resultText = advancedDiscovery
                        ? "Career set piece found: " + itemName + ". It has been added to your inventory. Set progress: " + recovered + "/5."
                        : "Duplicate career set piece found: " + itemName + ". It has been added to your inventory as a new quality roll. Set progress remains " + recovered + "/5.";
                    if (advancedDiscovery &&
                        !String.IsNullOrEmpty(_deferredEscalationText))
                        resultText += "\n\n" + _deferredEscalationText;
                    _deferredEscalationText = null;
                    ReportReward(definition, resultText);
                    ModLog.Info((advancedDiscovery ? "Set piece discovered: " : "Set-piece quality reroll granted: ") +
                        itemName + " from " + definition.MapName + " (" + recovered + "/5).");
                    return;
                }

                _deferredEscalationText = null;
                string errorLoot = GrantConsolationLoot(definition, attempts);
                ReportReward(definition, "The relic could not be created. You recovered themed loot" + FormatLoot(errorLoot) + ". Check TORCareerUniques.log.");
                ModLog.Error("Successful roll could not grant " + definition.CareerId + ": " + error);
                return;
            }

            string loot = GrantConsolationLoot(definition, attempts);
            ReportReward(definition, "No career set piece was found. You recovered themed loot" + FormatLoot(loot) + ".");
        }

        private void ReportReward(EncounterDefinition definition, string text)
        {
            CareerUniqueRuntime.Notify(text);
            _pendingResultTitle = definition.Kind == EncounterKind.GuardianSite
                ? "Treasure Search - " + definition.MapName
                : "Relic Encounter - " + definition.MapName;
            _pendingResultText = text;
            _resultUiDelay = 0.25f;
            AdminBridge.RequestApplicationTick();
            ModLog.Info("Reward result queued for display: " + definition.MapName + ".");
        }

        private string GrantConsolationLoot(EncounterDefinition definition, int attempts)
        {
            int seed = StableHash(definition.CareerId + ":loot:" + attempts + ":" + CampaignTime.Now.ToDays);
            Random random = new Random(seed);
            int count = random.Next(100) < 70 ? 1 : 2;
            string loot = CareerUniqueRuntime.GrantThemedLoot(definition.LootTokens, count, seed);
            ModLog.Info("Themed consolation loot at " + definition.MapName + ": " + (String.IsNullOrEmpty(loot) ? "none available" : loot) + ".");
            return loot;
        }

        private static string FormatLoot(string loot)
        {
            return String.IsNullOrEmpty(loot) ? "" : ": " + loot;
        }

        private void QueueReward(string careerId)
        {
            if (!_pendingRewards.Contains(careerId))
                _pendingRewards.Add(careerId);
            _uiDelay = 2.5f;
            AdminBridge.RequestApplicationTick();
        }

        private void RemovePendingReward(string careerId)
        {
            int index = _pendingRewards.IndexOf(careerId);
            if (index >= 0)
                _pendingRewards.RemoveAt(index);
        }

        private void ScheduleRespawn(string careerId)
        {
            _respawnAtDay[careerId] = CampaignTime.Now.ToDays + ModConfig.RespawnDays;
        }

        private void ScheduleUntrackedMissingEncounters()
        {
            double now = CampaignTime.Now.ToDays;
            for (int i = 0; i < EncounterCatalog.All.Length; i++)
            {
                EncounterDefinition definition = EncounterCatalog.All[i];
                if (definition.Kind == EncounterKind.GuardianSite)
                    continue;
                if (FindActiveEncounter(definition.CareerId) != null || _respawnAtDay.ContainsKey(definition.CareerId))
                    continue;
                int serial;
                _spawnSerials.TryGetValue(definition.CareerId, out serial);
                if (serial > 0)
                {
                    _respawnAtDay[definition.CareerId] = now + ModConfig.RespawnDays;
                    ModLog.Info(definition.MapName + " is missing outside a player victory; respawn scheduled.");
                }
            }
        }

        private void EnsureMissingRoamingHosts()
        {
            for (int i = 0; i < EncounterCatalog.All.Length; i++)
            {
                EncounterDefinition definition = EncounterCatalog.All[i];
                if (definition.Kind != EncounterKind.RoamingHost)
                    continue;
                if (FindActiveEncounter(definition.CareerId) != null)
                    continue;
                EnsureEncounter(definition, false);
            }
        }

        private void EnsureAllEncounters(bool adminOverride)
        {
            for (int i = 0; i < EncounterCatalog.All.Length; i++)
            {
                EncounterDefinition definition = EncounterCatalog.All[i];
                try
                {
                    EnsureEncounter(definition, adminOverride);
                }
                catch (Exception ex)
                {
                    ModLog.Error("Encounter maintenance skipped " + definition.MapName + ": " + FormatException(ex));
                }
            }
        }

        private void EnsureEncounter(EncounterDefinition definition, bool adminOverride)
        {
            if (definition.Kind == EncounterKind.GuardianSite)
            {
                EnsureGuardianSite(definition);
                return;
            }

            List<MobileParty> active = FindAllActiveEncounters(definition.CareerId);
            if (active.Count > 0)
            {
                Settlement activeAnchor = ResolveAnchor(definition);
                Clan activeClan = ResolveBanditClan(definition);
                string attachError;
                if (!TryAttachEncounterHero(definition, active[0], activeAnchor, activeClan, out attachError))
                {
                    ModLog.Error("Active encounter " + definition.MapName +
                        " has no valid persistent leader: " + attachError +
                        ". The disposable party will be removed without starting a new cooldown.");
                    if (active[0].MapEvent == null && active[0].IsActive)
                        DestroyPartyWithoutCooldown(active[0]);
                    return;
                }

                ReleaseHostToCampaignAi(active[0]);
                for (int i = 1; i < active.Count; i++)
                {
                    if (active[i].MapEvent == null && active[i].IsActive)
                    {
                        ModLog.Error("Duplicate encounter party detected for " + definition.CareerId + "; destroying " + active[i].StringId + ".");
                        DestroyPartyWithoutCooldown(active[i]);
                    }
                }
                return;
            }

            double now = CampaignTime.Now.ToDays;
            double respawnDay;
            if (!adminOverride && _respawnAtDay.TryGetValue(definition.CareerId, out respawnDay))
            {
                if (now < respawnDay)
                    return;
            }
            else if (!adminOverride)
            {
                int priorSerial;
                _spawnSerials.TryGetValue(definition.CareerId, out priorSerial);
                if (priorSerial > 0)
                {
                    // A previously spawned host that is absent is treated as defeated,
                    // never as permission to respawn immediately. This is the final
                    // safety net for AI autoresolve and third-party removals whose
                    // event ordering may hide the destroyed party from MapEventEnded.
                    _respawnAtDay[definition.CareerId] = now + ModConfig.RespawnDays;
                    ModLog.Info(definition.MapName + " is missing without a recorded destruction event; cooldown tombstone created before any respawn.");
                    return;
                }
            }

            string heroUnavailableReason;
            if (!IsEncounterHeroAvailable(definition.CareerId, out heroUnavailableReason))
            {
                ModLog.Info("Spawn suspended for " + definition.MapName + ": " +
                    heroUnavailableReason);
                return;
            }

            string recoveryError;
            if (!PrepareEncounterHeroForImmediateRespawn(definition,
                out recoveryError))
            {
                ModLog.Error("Respawn preparation failed for " +
                    definition.MapName + ": " + recoveryError + ".");
                // Preserve immediate eligibility so the next bounded daily
                // maintenance pass retries instead of starting a new cooldown.
                _respawnAtDay[definition.CareerId] = now;
                return;
            }

            SpawnEncounterParty(definition, false, null);
        }

        private void EnsureGuardianSite(EncounterDefinition definition)
        {
            Settlement site = FindGuardianSite(definition);
            if (site == null)
            {
                if (_resolvedGuardianSiteIds.Add(definition.CareerId + ":missing"))
                    ModLog.Error("No safe native campaign location could be assigned to guardian site " + definition.MapName + ".");
                return;
            }

            try
            {
                // Guardian encounters are attached to locations already authored in
                // the ToR campaign map. We never add settlements, change navigation
                // faces, clone map entities, or insert anything into Settlement.All.
                site.IsVisible = true;
                site.IsInspected = true;
                if (site.Party != null && MobileParty.MainParty != null)
                    site.Party.UpdateVisibilityAndInspected(MobileParty.MainParty.Position, 0f);

                Settlement anchor = ResolveAnchor(definition);
                Clan banditClan = ResolveBanditClan(definition);
                if (anchor == null || banditClan == null)
                    throw new InvalidOperationException("Guardian-site hero anchor/faction could not be resolved.");
                GetOrCreateEncounterHero(definition, anchor, banditClan);

                if (_resolvedGuardianSiteIds.Add(definition.CareerId))
                    ModLog.Info("Guardian site " + definition.MapName + " is hidden at native location " + site.Name + " (" + site.StringId + ").");
            }
            catch (Exception ex)
            {
                ModLog.Error("Activating guardian site " + definition.MapName + " failed: " + FormatException(ex));
            }
        }

        private MobileParty SpawnEncounterParty(EncounterDefinition definition, bool guardianDefenders, Settlement site)
        {
            // Creation starts from a native bandit party to preserve the
            // established encounter faction and spawn setup. The attachment
            // path replaces the native component with a saveable BanditPartyComponent
            // subclass that can retain a Hero leader without losing bandit AI.
            Settlement anchor = ResolveAnchor(definition);
            if (anchor == null)
            {
                ModLog.Error("No settlement anchor could be resolved for " + definition.MapName + ".");
                return null;
            }
            if (guardianDefenders && site == null)
            {
                ModLog.Error("Guardian-site defenders cannot spawn because the site settlement is missing for " + definition.MapName + ".");
                return null;
            }

            string heroUnavailableReason;
            if (!IsEncounterHeroAvailable(definition.CareerId, out heroUnavailableReason))
            {
                ModLog.Info("Spawn suspended for " + definition.MapName + ": " +
                    heroUnavailableReason);
                return null;
            }

            Clan banditClan = ResolveBanditClan(definition);
            if (banditClan == null)
            {
                ModLog.Error("No bandit faction could be resolved for " + definition.MapName + "; regular kingdom clans are deliberately not used.");
                return null;
            }

            int serial;
            _spawnSerials.TryGetValue(definition.CareerId, out serial);
            serial++;
            List<TroopCandidate> troopPool = BuildTroopPool(definition);
            if (troopPool.Count == 0)
            {
                ModLog.Error("No themed or fallback combat troops could be resolved for " + definition.MapName + ".");
                return null;
            }

            string partyId = PartyPrefix + Slug(definition.CareerId) + "_" + serial;
            MobileParty party = null;
            try
            {
                CampaignVec2 home;
                if (guardianDefenders)
                    home = site.GatePosition;
                else if (!TryResolveHostSpawnPosition(definition, ref anchor,
                    out home))
                    throw new InvalidOperationException(
                        "No culture-appropriate anchor with a reachable safe " +
                        "spawn position could be resolved.");

                party = BanditPartyComponent.CreateLooterParty(partyId, banditClan, anchor, true, null, home);
                party.Party.SetCustomName(new TextObject(guardianDefenders ? definition.MapName + " Defenders" : definition.MapName, null));
                party.Aggressiveness = guardianDefenders ? 0f : 0.35f;
                int seededTroops = party.MemberRoster.TotalManCount;
                party.MemberRoster.Clear();
                party.PrisonRoster.Clear();
                if (seededTroops > 0)
                    ModLog.Verbose("Removed " + seededTroops +
                        " native clan-template troops before authoring " +
                        definition.MapName + "'s themed roster.");
                PopulateParty(party, troopPool, definition, serial);
                string attachError;
                if (!TryAttachEncounterHero(definition, party, anchor, banditClan, out attachError))
                    throw new InvalidOperationException("Persistent encounter hero could not lead the party: " + attachError);
                if (guardianDefenders)
                {
                    party.SetMoveModeHold();
                    party.Ai.SetDoNotMakeNewDecisions(true);
                }
                else
                {
                    ReleaseHostToCampaignAi(party);
                    _respawnAtDay.Remove(definition.CareerId);
                }
                _spawnSerials[definition.CareerId] = serial;
                RegisterActiveEncounter(party);
                ModLog.Info("Spawned " + (guardianDefenders ? "defenders for " : String.Empty) + definition.MapName + " as " + party.StringId +
                    " near " + anchor.Name + " with " + party.MemberRoster.TotalManCount + " troops.");
                ModLog.Verbose("Anchor " + anchor.StringId + "; home " + home.X.ToString("0.0") + "," + home.Y.ToString("0.0") +
                    "; bandit clan " + banditClan.StringId + ".");
                return party;
            }
            catch (Exception ex)
            {
                ModLog.Error("Spawning " + definition.MapName + " failed: " + FormatException(ex));
                if (party != null && party.IsActive)
                    DestroyPartyWithoutCooldown(party);
                return null;
            }
        }

        private bool TryResolveHostSpawnPosition(EncounterDefinition definition,
            ref Settlement anchor, out CampaignVec2 home)
        {
            home = default(CampaignVec2);
            int attempts = 0;
            while (anchor != null && attempts++ < 12)
            {
                try
                {
                    home = ComputeHomePosition(definition, anchor);
                    return true;
                }
                catch (Exception ex)
                {
                    // Anchor failover is expected in coastal and mountainous
                    // regions. Only report an error if every bounded candidate
                    // fails and the encounter itself cannot be spawned.
                    ModLog.Verbose("Rejected roaming-host anchor " +
                        anchor.StringId + " for " + definition.MapName +
                        " because no safe reachable spawn position was found: " +
                        FormatException(ex));
                    _rejectedSpawnAnchorIds.Add(GetRejectedAnchorKey(
                        definition.CareerId, anchor.StringId));
                    _anchorSettlementIds.Remove(definition.CareerId);
                    anchor = ResolveAnchor(definition);
                }
            }
            return false;
        }

        private void ReleaseHostToCampaignAi(MobileParty party)
        {
            if (party == null || party.Ai == null || String.IsNullOrEmpty(party.StringId))
                return;
            if (_releasedHostPartyIds.Contains(party.StringId))
                return;

            // Clear the persisted tiny patrol order exactly once, then hand the party
            // back to Bannerlord's normal bandit campaign AI. Reapplying Hold every
            // maintenance tick would itself suppress normal movement decisions.
            party.SetMoveModeHold();
            party.Ai.SetDoNotMakeNewDecisions(false);
            party.Ai.RethinkAtNextHourlyTick = true;
            _releasedHostPartyIds.Add(party.StringId);
            ModLog.Info("Released roaming host " + party.StringId + " to standard campaign AI.");
        }

        private void DestroyPartyWithoutCooldown(MobileParty party)
        {
            if (party == null || !party.IsActive)
                return;
            string partyId = String.IsNullOrEmpty(party.StringId) ? null :
                party.StringId;
            if (partyId != null)
                _intentionalDestroyPartyIds.Add(partyId);
            try
            {
                DestroyPartyAction.Apply(null, party);
            }
            finally
            {
                // MobilePartyDestroyed is synchronous in 1.3.15 and normally consumes
                // this token. The finally block prevents a stale suppression entry if
                // another mod aborts or changes the destruction path.
                if (partyId != null)
                    _intentionalDestroyPartyIds.Remove(partyId);
            }
        }

        private void RepairV140EncounterHeroState()
        {
            if (_encounterHeroSchemaVersion >=
                CurrentEncounterHeroSchemaVersion)
                return;

            bool repairLegacyV140 = _encounterHeroSchemaVersion < 4;
            int clearedCooldowns = 0;
            int repairedGuardians = 0;
            if (repairLegacyV140 && _encounterHeroes != null &&
                _encounterHeroes.Count > 0)
            {
                for (int i = 0; i < EncounterCatalog.All.Length; i++)
                {
                    EncounterDefinition definition = EncounterCatalog.All[i];
                    if (IsOriginalRecruited(definition.CareerId))
                        continue;
                    Hero hero;
                    if (!_encounterHeroes.TryGetValue(definition.CareerId,
                        out hero) || hero == null || hero.IsDead)
                        continue;

                    if (definition.Kind == EncounterKind.GuardianSite)
                    {
                        bool wasCaptive = hero.IsPrisoner ||
                            hero.PartyBelongedToAsPrisoner != null;
                        if (wasCaptive)
                            EndCaptivityAction.ApplyByEscape(hero, null, false);
                        if (hero.PartyBelongedTo == null)
                            PlaceEncounterHeroBetweenEncounters(definition.CareerId,
                                hero, ResolveAnchor(definition), false);
                        _respawnAtDay.Remove(definition.CareerId);
                        if (wasCaptive ||
                            hero.HeroState == Hero.CharacterStates.Disabled)
                            repairedGuardians++;
                        continue;
                    }

                    if (hero.IsPrisoner ||
                        hero.PartyBelongedToAsPrisoner != null)
                        continue;

                    // v1.4.0 destroyed pre-existing roaming parties after
                    // assigning a leader to BanditPartyComponent, whose
                    // Leader property is permanently null. Those heroes
                    // remained at full health. Genuine defeated heroes are
                    // explicitly placed at 1 HP, preserving their cooldown.
                    if (hero.PartyBelongedTo == null &&
                        hero.HitPoints > 1 &&
                        FindActiveEncounter(definition.CareerId) == null &&
                        _respawnAtDay.Remove(definition.CareerId))
                    {
                        clearedCooldowns++;
                        ModLog.Info(
                            "Cleared v1.4.0 false cooldown tombstone for " +
                            definition.MapName + ".");
                    }
                }
            }

            // Schema 5 replaces borrowed native bandit faction ownership with one
            // deterministic TORCU-owned independent minor clan per encounter. Every
            // active encounter clan has a real leader, satisfying native conversation
            // code that dereferences ConversationHero.Clan.Leader.
            int dedicatedOwnersRepaired = 0;
            bool dedicatedOwnerMigrationFailed = false;
            for (int i = 0; i < EncounterCatalog.All.Length; i++)
            {
                EncounterDefinition definition = EncounterCatalog.All[i];
                Hero hero;
                if (!TryGetActiveEncounterHero(definition.CareerId, out hero) ||
                    hero == null || hero.IsDead)
                    continue;
                try
                {
                    Settlement anchor = ResolveAnchor(definition);
                    Clan nativeBanditClan = ResolveBanditClan(definition);
                    if (nativeBanditClan == null)
                        throw new InvalidOperationException(
                            "No native bandit spawn template could be resolved.");
                    Clan ownerClan = ResolveOrCreateEncounterOwnerClan(
                        definition, anchor, hero, hero.CharacterObject,
                        nativeBanditClan);
                    EnsureEncounterHeroClan(hero, ownerClan);
                    MobileParty party = hero.PartyBelongedTo;
                    if (party != null && party.IsActive)
                    {
                        if (party.MapEvent != null &&
                            !Object.ReferenceEquals(party.ActualClan, ownerClan))
                            throw new InvalidOperationException(
                                "Dedicated owner migration is deferred while the encounter is in a map event.");
                        party.ActualClan = ownerClan;
                    }
                    if (!Object.ReferenceEquals(ownerClan.Leader, hero))
                        throw new InvalidOperationException(
                            "Dedicated encounter clan has no valid leader after migration.");
                    dedicatedOwnersRepaired++;
                }
                catch (Exception ex)
                {
                    dedicatedOwnerMigrationFailed = true;
                    ModLog.Error("Dedicated encounter-clan migration failed for " +
                        definition.MapName + ": " + FormatException(ex));
                }
            }

            if (repairLegacyV140)
                _encounterHeroSchemaVersion = 4;

            if (!dedicatedOwnerMigrationFailed)
            {
                _encounterHeroSchemaVersion =
                    CurrentEncounterHeroSchemaVersion;
                ModLog.Info("Encounter-hero save state upgraded to schema " +
                    CurrentEncounterHeroSchemaVersion +
                    "; false cooldowns cleared=" + clearedCooldowns +
                    "; guardian heroes moved off-map=" + repairedGuardians +
                    "; dedicated owners repaired=" + dedicatedOwnersRepaired + ".");
            }
            else
            {
                ModLog.Error("Encounter-hero schema " +
                    CurrentEncounterHeroSchemaVersion + " was not committed " +
                    "because at least one dedicated owner migration was unsafe; " +
                    "it will retry on a later campaign load.");
            }
        }

        private void MigrateGuardianSiteMappings()
        {
            int removed = 0;
            for (int i = 0; i < EncounterCatalog.All.Length; i++)
            {
                EncounterDefinition definition = EncounterCatalog.All[i];
                if (definition.Kind != EncounterKind.GuardianSite)
                    continue;

                // v1.1.9-v1.1.14 stored synthetic site IDs in the general
                // encounter-anchor dictionary. Guardian defender parties need a real
                // fief as their BanditPartyComponent home, so discard that legacy value.
                string legacyAnchorId;
                if (_anchorSettlementIds.TryGetValue(definition.CareerId, out legacyAnchorId) &&
                    (String.IsNullOrEmpty(legacyAnchorId) || legacyAnchorId.StartsWith(SitePrefix, StringComparison.Ordinal) || !IsValidAnchorSettlement(FindSettlementById(legacyAnchorId))))
                {
                    _anchorSettlementIds.Remove(definition.CareerId);
                    removed++;
                }

                string savedId;
                if (!_siteSettlementIds.TryGetValue(definition.CareerId, out savedId))
                    continue;

                Settlement saved = FindSettlementById(savedId);
                if (String.IsNullOrEmpty(savedId) || savedId.StartsWith(SitePrefix, StringComparison.Ordinal) || !IsSafeNativeSiteCandidate(saved))
                {
                    _siteSettlementIds.Remove(definition.CareerId);
                    removed++;
                }
            }
            if (removed > 0)
                ModLog.Info("Removed " + removed + " unsafe synthetic guardian-site mappings. They will be reassigned to existing native ToR locations.");
        }

        private void ResolveAllGuardianSites()
        {
            for (int i = 0; i < EncounterCatalog.All.Length; i++)
            {
                EncounterDefinition definition = EncounterCatalog.All[i];
                if (definition.Kind == EncounterKind.GuardianSite)
                    FindGuardianSite(definition);
            }
        }

        private void MigrateLegacyGuardianParties()
        {
            for (int i = 0; i < EncounterCatalog.All.Length; i++)
            {
                EncounterDefinition definition = EncounterCatalog.All[i];
                if (definition.Kind == EncounterKind.GuardianSite)
                    DestroyLegacyGuardianParties(definition);
            }
        }

        private void DestroyLegacyGuardianParties(EncounterDefinition definition)
        {
            List<MobileParty> legacy = FindAllActiveEncounters(definition.CareerId);
            for (int i = 0; i < legacy.Count; i++)
            {
                MobileParty party = legacy[i];
                if (party.MapEvent == null && party.IsActive)
                {
                    ModLog.Info("Removing legacy mobile guardian-site party " + party.StringId + " for " + definition.MapName + ".");
                    DestroyPartyWithoutCooldown(party);
                }
            }
            Hero hero;
            if (FindActiveEncounter(definition.CareerId) == null &&
                TryGetActiveEncounterHero(definition.CareerId, out hero) &&
                hero != null && !hero.IsDead && !hero.IsPrisoner &&
                hero.PartyBelongedToAsPrisoner == null &&
                hero.PartyBelongedTo == null)
            {
                PlaceEncounterHeroBetweenEncounters(definition.CareerId, hero,
                    ResolveAnchor(definition), false);
            }
        }

        private Settlement FindGuardianSite(EncounterDefinition definition)
        {
            if (definition == null || definition.Kind != EncounterKind.GuardianSite)
                return null;

            string savedId;
            if (_siteSettlementIds.TryGetValue(definition.CareerId, out savedId))
            {
                Settlement saved = FindSettlementById(savedId);
                if (IsSafeNativeSiteCandidate(saved))
                    return saved;
                _siteSettlementIds.Remove(definition.CareerId);
            }

            HashSet<string> used = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < EncounterCatalog.All.Length; i++)
            {
                EncounterDefinition other = EncounterCatalog.All[i];
                if (other.Kind != EncounterKind.GuardianSite || String.Equals(other.CareerId, definition.CareerId, StringComparison.Ordinal))
                    continue;
                string otherId;
                if (_siteSettlementIds.TryGetValue(other.CareerId, out otherId) && !String.IsNullOrEmpty(otherId))
                    used.Add(otherId);
            }

            Settlement best = null;
            int bestScore = Int32.MinValue;
            foreach (Settlement settlement in Settlement.All)
            {
                if (!IsSafeNativeSiteCandidate(settlement) || used.Contains(settlement.StringId))
                    continue;

                string text = ReflectionUtil.SearchText(settlement) + " " + settlement.SettlementComponent.GetType().Name.ToLowerInvariant();
                int score = TokenScore(text, definition.RegionTokens, 12);
                score += SiteTypeScore(definition, settlement.SettlementComponent.GetType().Name);
                if (settlement.IsTown || settlement.IsCastle || settlement.IsVillage)
                    score -= 80;
                if (score > bestScore || (score == bestScore && best != null && String.CompareOrdinal(settlement.StringId, best.StringId) < 0))
                {
                    best = settlement;
                    bestScore = score;
                }
            }

            if (best != null)
            {
                _siteSettlementIds[definition.CareerId] = best.StringId;
                ModLog.Info("Assigned " + definition.MapName + " to existing native location " + best.Name + " (" + best.StringId + ", score " + bestScore + ").");
            }
            return best;
        }

        private static int SiteTypeScore(EncounterDefinition definition, string componentTypeName)
        {
            string type = (componentTypeName ?? String.Empty).ToLowerInvariant();
            int score = IsNativeTorLocationType(type) ? 100 : 0;
            string career = definition == null ? String.Empty : definition.CareerId;

            if (career == "GrailDamsel")
            {
                if (type.Contains("shrine")) score += 220;
                if (type.Contains("cursed")) score += 150;
            }
            else if (career == "MinorVampire" || career == "Necromancer" || career == "Necrarch")
            {
                if (type.Contains("cursed")) score += 240;
                if (type.Contains("chaosportal")) score += 150;
                if (type.Contains("trollcave")) score += 80;
            }
            else if (career == "WitchHunter")
            {
                if (type.Contains("cursed")) score += 220;
                if (type.Contains("shrine")) score += 170;
            }
            else if (career == "ImperialMagister" || career == "GreyLord")
            {
                if (type.Contains("shrine")) score += 200;
                if (type.Contains("cursed")) score += 120;
            }
            else if (career == "Spellsinger")
            {
                if (type.Contains("worldroots") || type.Contains("oakofages")) score += 280;
                if (type.Contains("shrine")) score += 80;
            }
            else if (career == "Ironbreaker" || career == "Runelord")
            {
                if (type.Contains("trollcave")) score += 220;
                if (type.Contains("cursed")) score += 160;
                if (type.Contains("chaosportal")) score += 80;
            }
            else if (career == "OrcShaman")
            {
                if (type.Contains("herdstone")) score += 280;
                if (type.Contains("trollcave")) score += 220;
                if (type.Contains("chaosportal")) score += 150;
            }
            return score;
        }

        private static bool IsSafeNativeSiteCandidate(Settlement settlement)
        {
            if (settlement == null || settlement.SettlementComponent == null || settlement.Party == null || String.IsNullOrEmpty(settlement.StringId))
                return false;
            if (settlement.StringId.StartsWith(SitePrefix, StringComparison.Ordinal))
                return false;

            string type = settlement.SettlementComponent.GetType().Name.ToLowerInvariant();
            return IsNativeTorLocationType(type) || settlement.IsTown || settlement.IsCastle || settlement.IsVillage;
        }

        private static bool IsNativeTorLocationType(string type)
        {
            if (String.IsNullOrEmpty(type))
                return false;
            return type.Contains("shrine") || type.Contains("worldroots") || type.Contains("oakofages") ||
                type.Contains("chaosportal") || type.Contains("herdstone") || type.Contains("slavercamp") ||
                type.Contains("cursedsite") || type.Contains("trollcave");
        }

        private static Settlement FindSettlementById(string id)
        {
            if (String.IsNullOrEmpty(id))
                return null;
            foreach (Settlement settlement in Settlement.All)
                if (settlement != null && String.Equals(settlement.StringId, id, StringComparison.Ordinal))
                    return settlement;
            return null;
        }

        private bool TryGetGuardianDefinition(Settlement settlement, out EncounterDefinition definition)
        {
            definition = null;
            if (settlement == null || String.IsNullOrEmpty(settlement.StringId))
                return false;

            for (int i = 0; i < EncounterCatalog.All.Length; i++)
            {
                EncounterDefinition candidate = EncounterCatalog.All[i];
                if (candidate.Kind != EncounterKind.GuardianSite)
                    continue;
                Settlement assigned = FindGuardianSite(candidate);
                if (assigned != null && String.Equals(assigned.StringId, settlement.StringId, StringComparison.Ordinal))
                {
                    definition = candidate;
                    return true;
                }
            }
            return false;
        }

        private void AddSiteMenus(CampaignGameStarter starter)
        {
            starter.AddGameMenu(SiteMenuId, "{=!}A guarded relic site", SiteMenuInit, GameMenu.MenuOverlayType.None, GameMenu.MenuFlags.None, null);
            starter.AddGameMenuOption(SiteMenuId, "torcu_search_site", "{TORCU_SITE_SEARCH_ACTION}", SiteSearchCondition, SiteSearchConsequence, false, -1, false, null);
            starter.AddGameMenuOption(SiteMenuId, "torcu_assault_site", "{=!}Assault the defenders", SiteAssaultCondition, SiteAssaultConsequence, false, -1, false, null);
            starter.AddGameMenuOption(SiteMenuId, "torcu_leave_site", "{=!}Return to the location", delegate(MenuCallbackArgs args)
            {
                args.optionLeaveType = GameMenuOption.LeaveType.Submenu;
                return true;
            }, LeaveGuardianSite, true, -1, false, null);

            string[] nativeMenus =
            {
                "shrine_menu", "oak_of_ages_menu", "worldroots_menu", "raidingsite_menu", "cursedsite_menu", "trollcave_menu",
                "town", "castle", "village"
            };
            for (int i = 0; i < nativeMenus.Length; i++)
            {
                try
                {
                    starter.AddGameMenuOption(nativeMenus[i], "torcu_enter_guardian_site", "{TORCU_SITE_ENTRY_ACTION}", SiteEntryCondition,
                        EnterGuardianSite, true, 0, false, null);
                }
                catch (Exception ex)
                {
                    ModLog.Verbose("Guardian-site entry option was not added to " + nativeMenus[i] + ": " + ex.Message);
                }
            }
        }

        private bool SiteEntryCondition(MenuCallbackArgs args)
        {
            EncounterDefinition definition;
            if (!TryGetGuardianDefinition(Settlement.CurrentSettlement, out definition))
                return false;
            MBTextManager.SetTextVariable("TORCU_SITE_ENTRY_ACTION", "Investigate " + definition.MapName);
            args.optionLeaveType = GameMenuOption.LeaveType.Submenu;
            args.Tooltip = new TextObject("A concealed trail from this established location leads to a guarded relic site.", null);
            return true;
        }

        private void EnterGuardianSite(MenuCallbackArgs args)
        {
            try
            {
                _returnMenuId = args == null || args.MenuContext == null || args.MenuContext.GameMenu == null
                    ? null : args.MenuContext.GameMenu.StringId;
                GameMenu.SwitchToMenu(SiteMenuId);
            }
            catch (Exception ex)
            {
                ModLog.Error("Opening guardian-site submenu failed: " + FormatException(ex));
            }
        }

        private void LeaveGuardianSite(MenuCallbackArgs args)
        {
            if (!String.IsNullOrEmpty(_returnMenuId))
            {
                string returnMenu = _returnMenuId;
                _returnMenuId = null;
                GameMenu.SwitchToMenu(returnMenu);
            }
            else
            {
                PlayerEncounter.Finish(true);
            }
        }

        private void SiteMenuInit(MenuCallbackArgs args)
        {
            EncounterDefinition definition;
            if (!TryGetGuardianDefinition(Settlement.CurrentSettlement, out definition))
                return;
            args.MenuTitle = new TextObject(definition.MapName, null);
            double respawnDay;
            string unavailableReason;
            if (_pendingRewards.Contains(definition.CareerId))
            {
                args.Text = new TextObject(GetSiteSearchPrompt(definition), null);
            }
            else if (!IsEncounterHeroAvailable(definition.CareerId, out unavailableReason))
            {
                args.Text = new TextObject(definition.SearchText + " The site is dormant: " +
                    unavailableReason + " No replacement defenders will be created.", null);
            }
            else if (_respawnAtDay.TryGetValue(definition.CareerId, out respawnDay) && CampaignTime.Now.ToDays < respawnDay)
            {
                int days = Math.Max(1, (int)Math.Ceiling(respawnDay - CampaignTime.Now.ToDays));
                args.Text = new TextObject("The site remains fixed on the map. Its defenders are gone and no untouched cache remains; fresh guardians are expected in " + days + " campaign days.", null);
            }
            else
            {
                args.Text = new TextObject(definition.SearchText + " The defenders must be defeated before the site can be searched.", null);
            }
        }

        private bool SiteSearchCondition(MenuCallbackArgs args)
        {
            EncounterDefinition definition;
            if (!TryGetGuardianDefinition(Settlement.CurrentSettlement, out definition) || !_pendingRewards.Contains(definition.CareerId))
                return false;
            MBTextManager.SetTextVariable("TORCU_SITE_SEARCH_ACTION", GetSiteSearchAction(definition));
            args.optionLeaveType = GameMenuOption.LeaveType.Continue;
            return true;
        }

        private void SiteSearchConsequence(MenuCallbackArgs args)
        {
            EncounterDefinition definition;
            if (!TryGetGuardianDefinition(Settlement.CurrentSettlement, out definition) || !_pendingRewards.Contains(definition.CareerId))
                return;
            RemovePendingReward(definition.CareerId);
            ModLog.Info("Player deliberately searched " + definition.MapName + ".");
            ResolveReward(definition);
        }

        private bool SiteAssaultCondition(MenuCallbackArgs args)
        {
            args.optionLeaveType = GameMenuOption.LeaveType.HostileAction;
            EncounterDefinition definition;
            if (!TryGetGuardianDefinition(Settlement.CurrentSettlement, out definition))
                return false;
            if (_pendingRewards.Contains(definition.CareerId))
            {
                args.IsEnabled = false;
                args.Tooltip = new TextObject("The defenders are already dead. Search the site before leaving its remains to be reclaimed.", null);
                return true;
            }
            string unavailableReason;
            if (!IsEncounterHeroAvailable(definition.CareerId, out unavailableReason))
            {
                args.IsEnabled = false;
                args.Tooltip = new TextObject(unavailableReason, null);
                return true;
            }
            double respawnDay;
            if (_respawnAtDay.TryGetValue(definition.CareerId, out respawnDay) && CampaignTime.Now.ToDays < respawnDay)
            {
                int days = Math.Max(1, (int)Math.Ceiling(respawnDay - CampaignTime.Now.ToDays));
                args.IsEnabled = false;
                args.Tooltip = new TextObject("The defenders return in " + days + " campaign days.", null);
            }
            return true;
        }

        private void SiteAssaultConsequence(MenuCallbackArgs args)
        {
            EncounterDefinition definition;
            Settlement site = Settlement.CurrentSettlement;
            if (!TryGetGuardianDefinition(site, out definition))
                return;

            MobileParty defenders = FindActiveEncounter(definition.CareerId);
            string unavailableReason;
            if (defenders == null && !IsEncounterHeroAvailable(definition.CareerId,
                out unavailableReason))
            {
                ShowTransientMessage(definition.MapName + " is suspended: " +
                    unavailableReason);
                return;
            }
            if (defenders == null)
                defenders = SpawnEncounterParty(definition, true, site);
            if (defenders == null)
            {
                ShowTransientMessage("The defenders for " + definition.MapName +
                    " could not be created. Check TORCareerUniques.log.");
                return;
            }

            try
            {
                PlayerEncounter.Finish(true);
                EncounterManager.StartPartyEncounter(PartyBase.MainParty, defenders.Party);
                ModLog.Info("Player assaulted guardian site " + definition.MapName + ".");
            }
            catch (Exception ex)
            {
                ModLog.Error("Starting guardian-site battle failed: " + FormatException(ex));
                if (defenders.IsActive)
                    DestroyPartyWithoutCooldown(defenders);
                _respawnAtDay.Remove(definition.CareerId);
                if (!PrepareEncounterHeroForRecovery(definition.CareerId,
                    ResolveAnchor(definition), "failed opening of " +
                    definition.MapName))
                    QueueEncounterHeroRecovery(definition.CareerId,
                        "failed opening of " + definition.MapName);
                ShowTransientMessage("The assault could not be started. No guardian " +
                    "party was left on the campaign map.");
            }
        }

        private void PopulateParty(MobileParty party, List<TroopCandidate> pool, EncounterDefinition definition, int serial)
        {
            Type characterType = ReflectionUtil.TypeByName("TaleWorlds.CampaignSystem.CharacterObject");
            MethodInfo add = null;
            foreach (MethodInfo method in party.MemberRoster.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                ParameterInfo[] parameters = method.GetParameters();
                if (method.Name == "AddToCounts" && parameters.Length == 7 && parameters[0].ParameterType == characterType)
                {
                    add = method;
                    break;
                }
            }
            if (add == null)
                throw new MissingMethodException("TroopRoster", "AddToCounts(CharacterObject, int, ...)");

            EncounterStrengthProfile profile = GetEncounterStrengthProfile(
                definition, serial);
            Random random = new Random(StableHash(definition.CareerId +
                ":troops:" + serial));
            int target = profile.TargetTroops;
            Dictionary<object, int> counts = new Dictionary<object, int>();
            int eliteThreshold = GetEliteThreshold(pool);
            int existingElites = CountExistingEliteTroops(party,
                eliteThreshold);
            int leaderAllowance = party.LeaderHero == null ? 1 : 0;
            int unitsToAdd = Math.Max(0, target -
                party.MemberRoster.TotalManCount - leaderAllowance);
            int desiredElites = Math.Max(0, (int)Math.Round(
                Math.Max(1, target - 1) * profile.EliteShare));
            int elitesToAdd = Math.Min(unitsToAdd,
                Math.Max(0, desiredElites - existingElites));

            for (int i = 0; i < unitsToAdd; i++)
            {
                bool requireElite = i < elitesToAdd;
                TroopCandidate candidate = WeightedTroopChoice(pool, random,
                    requireElite, eliteThreshold, profile.QualityBias);
                int current;
                counts.TryGetValue(candidate.Character, out current);
                counts[candidate.Character] = current + 1;
            }

            foreach (KeyValuePair<object, int> entry in counts)
                add.Invoke(party.MemberRoster, new object[] { entry.Key, entry.Value, false, 0, 0, true, -1 });
        }

        private static TroopCandidate WeightedTroopChoice(
            List<TroopCandidate> pool, Random random, bool requireElite,
            int eliteThreshold, float qualityBias)
        {
            int limit = Math.Min(48, pool.Count);
            int total = 0;
            for (int i = 0; i < limit; i++)
            {
                if ((pool[i].Level >= eliteThreshold) != requireElite)
                    continue;
                total += GetTroopWeight(pool[i], qualityBias);
            }
            if (total <= 0 && requireElite)
                return WeightedTroopChoice(pool, random, false,
                    Int32.MaxValue, qualityBias);
            if (total <= 0)
                return pool[random.Next(limit)];

            int roll = random.Next(total);
            for (int i = 0; i < limit; i++)
            {
                if ((pool[i].Level >= eliteThreshold) != requireElite)
                    continue;
                roll -= GetTroopWeight(pool[i], qualityBias);
                if (roll < 0)
                    return pool[i];
            }
            return pool[limit - 1];
        }

        private static int GetTroopWeight(TroopCandidate candidate,
            float qualityBias)
        {
            float themeWeight = 1f + Math.Max(0, candidate.Score) / 30f;
            float qualityWeight = 1f + Math.Max(0, candidate.Level) *
                qualityBias;
            return Math.Max(1, (int)Math.Round(themeWeight *
                qualityWeight * 10f));
        }

        private static int GetEliteThreshold(List<TroopCandidate> pool)
        {
            int maximum = 1;
            for (int i = 0; i < pool.Count; i++)
                maximum = Math.Max(maximum, pool[i].Level);
            return maximum >= 30 ? 30 : Math.Max(1, maximum - 4);
        }

        private static int CountExistingEliteTroops(MobileParty party,
            int eliteThreshold)
        {
            int result = 0;
            IEnumerable roster = party == null ? null :
                party.MemberRoster as IEnumerable;
            if (roster == null)
                return 0;
            foreach (object element in roster)
            {
                object character = ReflectionUtil.GetProperty(element,
                    "Character");
                if (character == null || ReflectionUtil.ToBool(
                    ReflectionUtil.GetProperty(character, "IsHero")))
                    continue;
                int level = ReflectionUtil.ToInt(
                    ReflectionUtil.GetProperty(character, "Level"));
                int number = ReflectionUtil.ToInt(
                    ReflectionUtil.GetProperty(element, "Number"));
                if (level >= eliteThreshold)
                    result += Math.Max(0, number);
            }
            return result;
        }

        private static int ClearNonHeroEncounterTroops(MobileParty party)
        {
            if (party == null || party.MemberRoster == null)
                return 0;
            int before = party.MemberRoster.TotalManCount;
            party.MemberRoster.RemoveIf(delegate(TroopRosterElement element)
            {
                return element.Character != null && !ReflectionUtil.ToBool(
                    ReflectionUtil.GetProperty(element.Character, "IsHero"));
            });
            return Math.Max(0, before - party.MemberRoster.TotalManCount);
        }

        private List<TroopCandidate> BuildTroopPool(EncounterDefinition definition)
        {
            if (_troopCatalog == null)
                _troopCatalog = BuildTroopCatalog();

            List<TroopCandidate> themed = new List<TroopCandidate>();
            List<TroopCandidate> fallback = new List<TroopCandidate>();
            for (int i = 0; i < _troopCatalog.Count; i++)
            {
                TroopCandidate source = _troopCatalog[i];
                if (definition.TroopAvoidTokens != null &&
                    TokenScore(source.Text, definition.TroopAvoidTokens, 1) > 0)
                    continue;

                int score = TokenScore(source.Text, definition.TroopTokens, 30);
                // Enemy-led definitions intentionally retain the old regional
                // tie-breaker. Career-led follower themes are authoritative and
                // must not be diluted by anchor geography.
                if (!definition.CareerLed)
                    score += TokenScore(source.Text, definition.RegionTokens, 7);
                TroopCandidate candidate = new TroopCandidate
                {
                    Character = source.Character,
                    Level = source.Level,
                    Score = score,
                    Text = source.Text
                };
                if (score > 0)
                    themed.Add(candidate);
                else if (ContainsAny(source.Text, "looter", "bandit", "brigand", "outlaw", "raider", "marauder", "goblin", "orc"))
                    fallback.Add(candidate);
            }

            List<TroopCandidate> result = definition.RequireThemedTroops
                ? themed : (themed.Count >= 4 ? themed : fallback);
            result.Sort(delegate(TroopCandidate a, TroopCandidate b)
            {
                int score = b.Score.CompareTo(a.Score);
                if (score != 0) return score;
                return b.Level.CompareTo(a.Level);
            });
            ModLog.Verbose(definition.MapName + " troop candidates: themed=" + themed.Count + ", fallback=" + fallback.Count + ".");
            return result;
        }

        private static List<TroopCandidate> BuildTroopCatalog()
        {
            List<TroopCandidate> result = new List<TroopCandidate>();
            Type characterType = ReflectionUtil.TypeByName("TaleWorlds.CampaignSystem.CharacterObject");
            IEnumerable all = ReflectionUtil.GetStaticProperty(characterType, "All") as IEnumerable;
            if (all == null)
                return result;

            int skipped = 0;
            try
            {
                foreach (object character in all)
                {
                    try
                    {
                        if (character == null ||
                            ReflectionUtil.ToBool(ReflectionUtil.GetProperty(character, "IsHero")) ||
                            ReflectionUtil.ToBool(ReflectionUtil.GetProperty(character, "IsChild")))
                            continue;

                        int level = ReflectionUtil.ToInt(ReflectionUtil.GetProperty(character, "Level"));
                        if (level < 5 || level > 45)
                            continue;

                        string text = ReflectionUtil.SearchText(character);
                        if (ContainsAny(text, "template", "dummy", "civilian", "villager", "merchant", "caravan_master", "prisoner"))
                            continue;

                        result.Add(new TroopCandidate { Character = character, Level = level, Score = 0, Text = text });
                    }
                    catch (Exception ex)
                    {
                        skipped++;
                        ModLog.Verbose("Skipped unsafe troop metadata entry: " + ex.GetType().Name + ": " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                ModLog.Error("Troop catalogue enumeration ended early: " + FormatException(ex));
            }

            ModLog.Info("Troop metadata catalogue scan completed. Usable=" + result.Count + ", skipped=" + skipped + ".");
            return result;
        }

        private Settlement ResolveAnchor(EncounterDefinition definition)
        {
            string savedId;
            if (_anchorSettlementIds.TryGetValue(definition.CareerId, out savedId))
            {
                foreach (Settlement settlement in Settlement.All)
                {
                    if (String.Equals(settlement.StringId, savedId, StringComparison.Ordinal) &&
                        IsValidAnchorSettlement(settlement) &&
                        !IsRejectedSpawnAnchor(definition.CareerId,
                            settlement.StringId))
                        return settlement;
                }
                _anchorSettlementIds.Remove(definition.CareerId);
                ModLog.Info("Discarded invalid legacy anchor " + savedId + " for " + definition.MapName + ".");
            }

            Dictionary<string, int> anchorUse = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> pair in _anchorSettlementIds)
            {
                int used;
                anchorUse.TryGetValue(pair.Value, out used);
                anchorUse[pair.Value] = used + 1;
            }

            List<SettlementCandidate> candidates = new List<SettlementCandidate>();
            string[] anchorTokens = GetAnchorTokens(definition);
            bool foundThematicCandidate = false;
            foreach (Settlement settlement in Settlement.All)
            {
                if (!IsValidAnchorSettlement(settlement))
                    continue;
                if (IsRejectedSpawnAnchor(definition.CareerId,
                    settlement.StringId))
                    continue;
                string text = ReflectionUtil.SearchText(settlement);
                int themeScore = TokenScore(text, anchorTokens, 40);
                if (themeScore > 0)
                    foundThematicCandidate = true;
                int score = themeScore;
                if (ReflectionUtil.ToBool(ReflectionUtil.GetProperty(settlement, "IsTown"))) score += 8;
                if (ReflectionUtil.ToBool(ReflectionUtil.GetProperty(settlement, "IsCastle"))) score += 5;
                int used;
                if (anchorUse.TryGetValue(settlement.StringId, out used))
                    score -= used * 12;
                candidates.Add(new SettlementCandidate { Settlement = settlement, Score = score });
            }
            if (candidates.Count == 0)
                return null;

            if (foundThematicCandidate)
                candidates.RemoveAll(delegate(SettlementCandidate candidate)
                {
                    return TokenScore(ReflectionUtil.SearchText(
                        candidate.Settlement), anchorTokens, 40) <= 0;
                });
            else
                ModLog.Error("No culture-appropriate anchor metadata matched " +
                    definition.MapName + "; using the safest generic fief fallback.");

            candidates.Sort(delegate(SettlementCandidate a, SettlementCandidate b)
            {
                int score = b.Score.CompareTo(a.Score);
                return score != 0 ? score : String.CompareOrdinal(a.Settlement.StringId, b.Settlement.StringId);
            });
            int bestScore = candidates[0].Score;
            int pool = 1;
            while (pool < candidates.Count &&
                candidates[pool].Score == bestScore)
                pool++;
            int index = Math.Abs(StableHash(definition.CareerId)) % pool;
            Settlement selected = candidates[index].Settlement;
            _anchorSettlementIds[definition.CareerId] = selected.StringId;
            ModLog.Verbose("Selected real fief anchor " + selected.StringId + " for " + definition.MapName + " from " + pool + " candidates.");
            return selected;
        }

        private bool IsRejectedSpawnAnchor(string careerId, string settlementId)
        {
            return _rejectedSpawnAnchorIds.Contains(GetRejectedAnchorKey(
                careerId, settlementId));
        }

        private static string GetRejectedAnchorKey(string careerId,
            string settlementId)
        {
            return (careerId ?? String.Empty) + "|" +
                (settlementId ?? String.Empty);
        }

        private static string[] GetAnchorTokens(EncounterDefinition definition)
        {
            if (definition == null)
                return new string[0];
            switch (definition.CareerId)
            {
                case "GrailKnight":
                    return AnchorTokens("breton", "mousillon", "couronne", "brionne");
                case "WarriorPriest":
                    return AnchorTokens("empire", "imperial", "reikland", "altdorf",
                        "middenland");
                case "BloodKnight":
                    return AnchorTokens("sylvan", "vampire", "mousillon");
                case "Mercenary":
                    return AnchorTokens("border", "tilea", "estalia", "southern");
                case "BlackGrailKnight":
                    return AnchorTokens("mousillon", "breton", "couronne", "brionne");
                case "WarriorPriestUlric":
                    return AnchorTokens("middenland", "nordland", "ulric", "empire");
                case "Waywatcher":
                    return AnchorTokens("wood_elf", "wood elf", "asrai", "athel",
                        "loren");
                case "Warden":
                    return AnchorTokens("eonir", "wood_elf", "wood elf", "asrai",
                        "athel", "loren");
                case "KnightOldWorld":
                    return AnchorTokens("empire", "imperial", "reikland", "altdorf",
                        "nuln", "middenland");
                case "Slayer":
                    return AnchorTokens("dwarf", "dawi", "karaz", "karak");
                case "OrcBoss":
                    return AnchorTokens("badlands", "orc", "greenskin");
                default:
                    return definition.RegionTokens;
            }
        }

        private static string[] AnchorTokens(params string[] values)
        {
            return values;
        }

        private void MigrateRoamingHostAnchors()
        {
            const int currentSchema = 1;
            if (_anchorSelectionSchemaVersion >= currentSchema)
                return;

            int cleared = 0;
            for (int i = 0; i < EncounterCatalog.All.Length; i++)
            {
                EncounterDefinition definition = EncounterCatalog.All[i];
                if (definition.Kind != EncounterKind.RoamingHost)
                    continue;
                if (_anchorSettlementIds.Remove(definition.CareerId))
                    cleared++;
            }
            _anchorSelectionSchemaVersion = currentSchema;
            ModLog.Info("Roaming-host anchor selection upgraded to schema " +
                currentSchema + "; stale broad-token anchors cleared=" +
                cleared + ".");
        }

        private static bool IsValidAnchorSettlement(Settlement settlement)
        {
            if (settlement == null || String.IsNullOrEmpty(settlement.StringId) || settlement.StringId.StartsWith(SitePrefix, StringComparison.Ordinal))
                return false;
            bool isTown = ReflectionUtil.ToBool(ReflectionUtil.GetProperty(settlement, "IsTown"));
            bool isCastle = ReflectionUtil.ToBool(ReflectionUtil.GetProperty(settlement, "IsCastle"));
            bool isVillage = ReflectionUtil.ToBool(ReflectionUtil.GetProperty(settlement, "IsVillage"));
            return isTown || isCastle || isVillage;
        }

        private static Settlement FindNearestTrackableSettlement(
            CampaignVec2 position)
        {
            Settlement nearest = null;
            float nearestDistanceSquared = Single.MaxValue;
            foreach (Settlement settlement in Settlement.All)
            {
                if (!IsValidAnchorSettlement(settlement))
                    continue;
                float distanceSquared = position.DistanceSquared(
                    settlement.GatePosition);
                if (distanceSquared >= nearestDistanceSquared)
                    continue;
                nearest = settlement;
                nearestDistanceSquared = distanceSquared;
            }
            return nearest;
        }

        private static Clan ResolveBanditClan(EncounterDefinition definition)
        {
            Clan best = null;
            int bestScore = Int32.MinValue;
            foreach (Clan clan in Clan.All)
            {
                if (IsDedicatedEncounterOwnerClan(clan))
                    continue;
                string text = ReflectionUtil.SearchText(clan);
                bool isBandit = ReflectionUtil.ToBool(ReflectionUtil.GetProperty(clan, "IsBanditFaction")) ||
                    ContainsAny(text, "bandit", "looter", "outlaw", "deserter", "troll_clan");
                if (!isBandit)
                    continue;
                if (definition.FactionAvoidTokens != null &&
                    TokenScore(text, definition.FactionAvoidTokens, 1) > 0)
                    continue;
                int themeScore = TokenScore(text, definition.FactionTokens, 15);
                int genericScore = TokenScore(text,
                    AnchorTokens("looter", "bandit", "outlaw", "deserter",
                        "brigand", "raider"), 10);
                int monsterPenalty = ContainsAny(text, "troll", "goblin",
                    "orc", "vampire", "undead", "beast", "chaos",
                    "skaven") ? 400 : 0;
                int score = themeScore > 0 ? 10000 + themeScore :
                    genericScore - monsterPenalty;
                if (score > bestScore || (score == bestScore && best != null &&
                    String.CompareOrdinal(clan.StringId, best.StringId) < 0))
                {
                    bestScore = score;
                    best = clan;
                }
            }
            if (best != null)
                ModLog.Verbose("Resolved independent bandit clan " +
                    best.StringId + " for " + definition.MapName +
                    " with score " + bestScore + ".");
            return best;
        }

        private CampaignVec2 ComputeHomePosition(EncounterDefinition definition, Settlement anchor)
        {
            return HostNavigationSafety.ComputeSafeHomePosition(
                definition.CareerId, anchor);
        }

        private void MigrateV143NavigationState()
        {
            if (_navigationMigrationVersion >= 2)
                return;

            _navigationMigrationVersion = 2;
            int repaired = 0;
            for (int i = 0; i < EncounterCatalog.All.Length; i++)
            {
                EncounterDefinition definition = EncounterCatalog.All[i];
                if (definition.Kind != EncounterKind.RoamingHost)
                    continue;

                MobileParty party = FindActiveEncounter(definition.CareerId);
                if (party == null)
                    continue;

                Settlement anchor = ResolveAnchor(definition);
                if (!HostNavigationSafety.RepairLegacyParty(
                    party, definition.CareerId, anchor))
                    continue;

                repaired++;
            }

            if (repaired > 0)
            {
                ModLog.Info("Relocated " + repaired +
                    " roaming hosts embedded in settlement models during one-time navigation migration v2.");
            }
        }

        private void ReconcileClaims()
        {
            for (int i = 0; i < EncounterCatalog.All.Length; i++)
            {
                string careerId = EncounterCatalog.All[i].CareerId;
                if (SetItemRuntime.IsSetComplete(careerId))
                    MarkClaimed(careerId);
                else
                    _claimedCareerIds.Remove(careerId);
            }
        }

        private void MarkClaimed(string careerId)
        {
            if (!_claimedCareerIds.Contains(careerId))
                _claimedCareerIds.Add(careerId);
        }

        internal bool HasDiscoveredSetPiece(string careerId, int pieceIndex)
        {
            EnsureState();
            return _discoveredSetPieces.Contains(GetDiscoveryKey(careerId, pieceIndex));
        }

        internal bool RecordDiscoveredSetPiece(string careerId, int pieceIndex)
        {
            EnsureState();
            string key = GetDiscoveryKey(careerId, pieceIndex);
            if (_discoveredSetPieces.Contains(key))
                return false;
            _discoveredSetPieces.Add(key);
            ModLog.Info("Set discovery recorded: " + careerId + " piece " + pieceIndex + ".");
            QueueOrShowCollectionEscalation(careerId,
                GetDiscoveredSetPieceCount(careerId));
            return true;
        }

        internal int GetDiscoveredSetPieceCount(string careerId)
        {
            EnsureState();
            int count = 0;
            for (int i = 0; i < 5; i++)
                if (_discoveredSetPieces.Contains(GetDiscoveryKey(careerId, i)))
                    count++;
            return count;
        }

        private static string GetDiscoveryKey(string careerId, int pieceIndex)
        {
            return (careerId ?? String.Empty) + ":" + pieceIndex;
        }

        private MobileParty FindActiveEncounter(string careerId)
        {
            List<MobileParty> parties;
            if (String.IsNullOrEmpty(careerId) ||
                !_activeEncountersByCareer.TryGetValue(careerId, out parties))
                return null;

            for (int i = parties.Count - 1; i >= 0; i--)
            {
                MobileParty party = parties[i];
                if (party == null || !party.IsActive)
                {
                    parties.RemoveAt(i);
                    continue;
                }
            }
            if (parties.Count == 0)
            {
                _activeEncountersByCareer.Remove(careerId);
                return null;
            }
            return parties[0];
        }

        private List<MobileParty> FindAllActiveEncounters(string careerId)
        {
            List<MobileParty> result = new List<MobileParty>();
            List<MobileParty> indexed;
            if (String.IsNullOrEmpty(careerId) ||
                !_activeEncountersByCareer.TryGetValue(careerId, out indexed))
                return result;

            for (int i = indexed.Count - 1; i >= 0; i--)
            {
                MobileParty party = indexed[i];
                if (party == null || !party.IsActive)
                    indexed.RemoveAt(i);
            }
            if (indexed.Count == 0)
                _activeEncountersByCareer.Remove(careerId);
            else
                result.AddRange(indexed);
            return result;
        }

        private void ReconcileActiveEncounterIndexOnce()
        {
            _activeEncountersByCareer.Clear();
            int indexed = 0;
            foreach (MobileParty party in MobileParty.All)
            {
                if (party == null || !party.IsActive ||
                    String.IsNullOrEmpty(party.StringId) ||
                    !party.StringId.StartsWith(PartyPrefix,
                        StringComparison.Ordinal))
                    continue;
                RegisterActiveEncounter(party);
                indexed++;
            }
            ModLog.Info("One-time active encounter index reconciliation found " +
                indexed + " existing TORCU parties.");
        }

        private void RegisterActiveEncounter(MobileParty party)
        {
            if (party == null || String.IsNullOrEmpty(party.StringId))
                return;
            string careerId = CareerFromPartyId(party.StringId);
            if (String.IsNullOrEmpty(careerId))
                return;

            List<MobileParty> parties;
            if (!_activeEncountersByCareer.TryGetValue(careerId, out parties))
            {
                parties = new List<MobileParty>();
                _activeEncountersByCareer.Add(careerId, parties);
            }
            if (!parties.Contains(party))
                parties.Add(party);
        }

        private void UnregisterActiveEncounter(MobileParty party)
        {
            if (party == null || String.IsNullOrEmpty(party.StringId))
                return;
            string careerId = CareerFromPartyId(party.StringId);
            List<MobileParty> parties;
            if (String.IsNullOrEmpty(careerId) ||
                !_activeEncountersByCareer.TryGetValue(careerId, out parties))
                return;
            parties.Remove(party);
            if (parties.Count == 0)
                _activeEncountersByCareer.Remove(careerId);
        }

        private static List<MobileParty> FindEncounterParties(MapEventSide side)
        {
            List<MobileParty> result = new List<MobileParty>();
            if (side == null)
                return result;
            foreach (MapEventParty eventParty in side.Parties)
            {
                MobileParty party = eventParty.Party == null ? null : eventParty.Party.MobileParty;
                if (party != null && party.StringId != null && party.StringId.StartsWith(PartyPrefix, StringComparison.Ordinal))
                    result.Add(party);
            }
            return result;
        }

        private static bool IsGuardianEncounterMapEvent(MapEvent mapEvent)
        {
            if (mapEvent == null)
                return false;
            List<MobileParty> attackerParties =
                FindEncounterParties(mapEvent.AttackerSide);
            List<MobileParty> defenderParties =
                FindEncounterParties(mapEvent.DefenderSide);
            for (int pass = 0; pass < 2; pass++)
            {
                List<MobileParty> parties = pass == 0
                    ? attackerParties : defenderParties;
                for (int i = 0; i < parties.Count; i++)
                {
                    string careerId = CareerFromPartyId(parties[i].StringId);
                    EncounterDefinition definition;
                    if (EncounterCatalog.ByCareer.TryGetValue(careerId,
                        out definition) &&
                        definition.Kind == EncounterKind.GuardianSite)
                        return true;
                }
            }
            return false;
        }

        private static string CareerFromPartyId(string partyId)
        {
            if (String.IsNullOrEmpty(partyId) || !partyId.StartsWith(PartyPrefix, StringComparison.Ordinal))
                return null;
            string remainder = partyId.Substring(PartyPrefix.Length);
            int serialSeparator = remainder.LastIndexOf('_');
            string slug = serialSeparator > 0 ? remainder.Substring(0, serialSeparator) : remainder;
            for (int i = 0; i < EncounterCatalog.All.Length; i++)
                if (String.Equals(Slug(EncounterCatalog.All[i].CareerId), slug, StringComparison.Ordinal))
                    return EncounterCatalog.All[i].CareerId;
            return null;
        }

        private static bool IsPlayerStillInMapEvent()
        {
            try
            {
                object mainParty = ReflectionUtil.GetStaticProperty(ReflectionUtil.TypeByName("TaleWorlds.CampaignSystem.Party.MobileParty"), "MainParty");
                return ReflectionUtil.GetProperty(mainParty, "MapEvent") != null;
            }
            catch { return true; }
        }

        private void ShowEncounterDetails(EncounterDefinition definition)
        {
            EnsureState();
            if (definition == null)
                return;
            UnlockFullSetOnlyMasteryOnDemand(definition.CareerId);

            MobileParty active = FindActiveEncounter(definition.CareerId);
            Settlement site = definition.Kind == EncounterKind.GuardianSite ? FindGuardianSite(definition) : null;
            Settlement homeAnchor = definition.Kind == EncounterKind.GuardianSite
                ? site : ResolveAnchor(definition);
            Settlement trackingAnchor = definition.Kind == EncounterKind.GuardianSite
                ? site : active == null
                    ? homeAnchor
                    : FindNearestTrackableSettlement(active.Position) ?? homeAnchor;
            // Bannerlord accepts MobileParty through the tracker interface but does
            // not render a useful persistent marker for these roaming bandit hosts.
            // Tracking is deliberately settlement-only: nearest current location
            // while active, saved home anchor as the bounded fallback.
            TaleWorlds.CampaignSystem.ITrackableCampaignObject trackingTarget =
                (TaleWorlds.CampaignSystem.ITrackableCampaignObject)trackingAnchor;
            string status;
            string heroAvailabilityReason;
            double respawn;
            if (!IsEncounterHeroAvailable(definition.CareerId, out heroAvailabilityReason))
                status = "SUSPENDED — " + heroAvailabilityReason;
            else if (_respawnAtDay.TryGetValue(definition.CareerId, out respawn) && CampaignTime.Now.ToDays < respawn)
                status = "RESPAWNING: " + Math.Max(1, (int)Math.Ceiling(respawn - CampaignTime.Now.ToDays)) + " campaign days";
            else if (definition.Kind == EncounterKind.GuardianSite)
                status = site == null ? "LOCATION MISSING" : "LOCATION ACTIVE";
            else
                status = active == null ? "HOST MISSING" : "HOST ACTIVE";

            int attempts;
            _attempts.TryGetValue(definition.CareerId, out attempts);
            int discovered = GetDiscoveredSetPieceCount(definition.CareerId);
            int serial;
            _spawnSerials.TryGetValue(definition.CareerId, out serial);
            int profileSerial = active == null ? Math.Max(1, serial + 1) :
                Math.Max(1, serial);
            EncounterStrengthProfile strength = GetEncounterStrengthProfile(
                definition, profileSerial);
            bool complete = discovered >= 5;
            string type = definition.Kind == EncounterKind.GuardianSite ? "Guardian site" : "Roaming host";
            string homeName = homeAnchor == null ? "unresolved" :
                homeAnchor.Name.ToString();
            string locationName = trackingAnchor == null ? "unresolved" :
                trackingAnchor.Name.ToString();
            string trackingName = locationName;
            string locationId = trackingAnchor == null ? String.Empty : trackingAnchor.StringId;
            string locationLine = definition.Kind == EncounterKind.GuardianSite
                ? "Map location: " + locationName
                : "Host home zone: around " + homeName;
            string liveHostLine = definition.Kind == EncounterKind.RoamingHost
                ? "\nLive host: " + (active == null ? "not currently present" : active.Name.ToString())
                : String.Empty;
            string text =
                "SET\n" + SetItemRuntime.GetSetName(definition.CareerId) + "\n\n" +
                "ENCOUNTER\n" + definition.MapName + "\n" +
                "Type: " + type + "\n" +
                "Status: " + status + "\n" +
                "Hero: " + GetEncounterHeroOverview(definition.CareerId) + "\n" +
                "Mastery: " + GetMasteryOverview(definition.CareerId) + "\n" +
                locationLine + liveHostLine + "\n\n" +
                "COLLECTION\n" + discovered + "/5 pieces discovered" +
                    (complete ? " — COMPLETE" : String.Empty) + "\n" +
                "Reward attempts: " + attempts + "\n" +
                "Veteran clears: " + strength.VeteranTier + "/5\n" +
                "Encounter strength: " + strength.TargetTroops +
                    " troops, " + (int)Math.Round(strength.EliteShare * 100f) +
                    "% elite target, x" +
                    strength.TotalMultiplier.ToString("0.00") + " size\n" +
                "\nTRACKING\n" +
                (definition.Kind == EncounterKind.GuardianSite
                    ? "Tracks the real map location containing this hidden encounter."
                    : active != null
                        ? "Tracks the native settlement currently closest to the roaming host."
                        : "Tracks the fixed settlement anchoring this missing host's home region.") +
                (definition.Kind == EncounterKind.RoamingHost && active != null
                    ? "\nCurrent nearest location: " + locationName : String.Empty) +
                (String.IsNullOrEmpty(locationId) ? String.Empty : "\nMap ID: " + locationId);

            bool parleyAvailable = IsRecruitmentEligibilityProven(definition.CareerId) &&
                !IsOriginalRecruited(definition.CareerId) &&
                SetItemRuntime.GetEquippedRealSetPieceCount(definition.CareerId) == 5 &&
                ModConfig.HeroRecruitmentMode > 0;
            InquiryHelper.ShowChoice(
                SetItemRuntime.GetSetName(definition.CareerId),
                text,
                parleyAvailable ? "Parley with original hero" :
                    (trackingTarget == null ? "Tracking unavailable" : "Track " + trackingName),
                "Close",
                delegate
                {
                    if (parleyAvailable)
                        ShowRecognitionParley(definition.CareerId);
                    else
                        TrackEncounterLocation(definition, trackingTarget,
                            trackingName);
                },
                delegate { });
        }

        private void TrackEncounterLocation(EncounterDefinition definition,
            TaleWorlds.CampaignSystem.ITrackableCampaignObject target,
            string targetName)
        {
            if (definition == null || target == null)
            {
                ShowTransientMessage("No native map location is available for this encounter.");
                return;
            }

            bool tracked = TryTrackOnMap(target);
            ModLog.Info("Map tracking requested for " + definition.MapName +
                " at " + targetName + "; verified=" + tracked + ".");
            ShowTransientMessage(tracked
                ? targetName + " is now tracked for " + definition.MapName + "."
                : "Bannerlord could not track " + targetName + ".");
        }

        private static bool TryTrackOnMap(
            TaleWorlds.CampaignSystem.ITrackableCampaignObject target)
        {
            if (target == null || Campaign.Current == null ||
                Campaign.Current.VisualTrackerManager == null)
                return false;
            try
            {
                VisualTrackerManager manager = Campaign.Current.VisualTrackerManager;
                if (!manager.CheckTracked(target))
                    manager.RegisterObject(target);
                return manager.CheckTracked(target);
            }
            catch (Exception ex)
            {
                ModLog.Error("Map tracking failed: " + FormatException(ex));
                return false;
            }
        }

        private static void ShowTransientMessage(string text)
        {
            try
            {
                Type messageType = ReflectionUtil.TypeByName(
                    "TaleWorlds.Library.InformationMessage");
                Type managerType = ReflectionUtil.TypeByName(
                    "TaleWorlds.Library.InformationManager") ??
                    ReflectionUtil.TypeByName("TaleWorlds.Core.InformationManager") ??
                    ReflectionUtil.TypeByName("TaleWorlds.Core.MBInformationManager");
                if (messageType == null || managerType == null)
                    return;
                object message = Activator.CreateInstance(messageType,
                    new object[] { text });
                MethodInfo[] methods = managerType.GetMethods(
                    BindingFlags.Public | BindingFlags.Static);
                for (int i = 0; i < methods.Length; i++)
                {
                    ParameterInfo[] parameters = methods[i].GetParameters();
                    if (methods[i].Name == "DisplayMessage" &&
                        parameters.Length == 1 &&
                        parameters[0].ParameterType.IsInstanceOfType(message))
                    {
                        methods[i].Invoke(null, new object[] { message });
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                ModLog.Error("Map tracking notification failed: " +
                    FormatException(ex));
            }
        }

        private void EnsureState()
        {
            if (_claimedCareerIds == null) _claimedCareerIds = new List<string>();
            if (_respawnAtDay == null) _respawnAtDay = new Dictionary<string, double>(StringComparer.Ordinal);
            if (_anchorSettlementIds == null) _anchorSettlementIds = new Dictionary<string, string>(StringComparer.Ordinal);
            if (_siteSettlementIds == null) _siteSettlementIds = new Dictionary<string, string>(StringComparer.Ordinal);
            if (_attempts == null) _attempts = new Dictionary<string, int>(StringComparer.Ordinal);
            if (_spawnSerials == null) _spawnSerials = new Dictionary<string, int>(StringComparer.Ordinal);
            if (_pendingRewards == null) _pendingRewards = new List<string>();
            if (_discoveredSetPieces == null) _discoveredSetPieces = new List<string>();
            if (_veteranClears == null) _veteranClears = new Dictionary<string, int>(StringComparer.Ordinal);
            if (_encounterHeroes == null) _encounterHeroes = new Dictionary<string, Hero>(StringComparer.Ordinal);
            if (_successorHeroes == null) _successorHeroes = new Dictionary<string, Hero>(StringComparer.Ordinal);
            if (_masteryProvenCareerIds == null) _masteryProvenCareerIds = new List<string>();
            if (_masteryVictoryCareerIds == null) _masteryVictoryCareerIds = new List<string>();
            if (_recruitedOriginalCareerIds == null) _recruitedOriginalCareerIds = new List<string>();
            if (_pendingRecognitionCareerIds == null) _pendingRecognitionCareerIds = new List<string>();
            if (_pendingHeroRecoveries == null) _pendingHeroRecoveries = new Dictionary<string, string>(StringComparer.Ordinal);
            if (_rejectedSpawnAnchorIds == null) _rejectedSpawnAnchorIds = new HashSet<string>(StringComparer.Ordinal);
            if (_releasedHostPartyIds == null) _releasedHostPartyIds = new HashSet<string>(StringComparer.Ordinal);
            if (_intentionalDestroyPartyIds == null) _intentionalDestroyPartyIds = new HashSet<string>(StringComparer.Ordinal);
            if (_resolvedGuardianSiteIds == null) _resolvedGuardianSiteIds = new HashSet<string>(StringComparer.Ordinal);
            if (_activeEncountersByCareer == null) _activeEncountersByCareer = new Dictionary<string, List<MobileParty>>(StringComparer.Ordinal);
        }

        private static float Distance(CampaignVec2 a, CampaignVec2 b)
        {
            Vec2 av = a.ToVec2();
            Vec2 bv = b.ToVec2();
            float x = av.x - bv.x;
            float y = av.y - bv.y;
            return (float)Math.Sqrt(x * x + y * y);
        }

        private static int TokenScore(string text, string[] tokens, int points)
        {
            if (String.IsNullOrEmpty(text) || tokens == null)
                return 0;
            int score = 0;
            for (int i = 0; i < tokens.Length; i++)
                if (!String.IsNullOrEmpty(tokens[i]) && text.Contains(tokens[i].ToLowerInvariant()))
                    score += points;
            return score;
        }

        private static bool ContainsAny(string text, params string[] values)
        {
            if (String.IsNullOrEmpty(text))
                return false;
            for (int i = 0; i < values.Length; i++)
                if (text.Contains(values[i])) return true;
            return false;
        }

        private static string Slug(string value)
        {
            return (value ?? String.Empty).ToLowerInvariant();
        }

        private static int StableHash(object value)
        {
            string text = Convert.ToString(value) ?? String.Empty;
            unchecked
            {
                int hash = 17;
                for (int i = 0; i < text.Length; i++)
                    hash = hash * 31 + text[i];
                return hash == Int32.MinValue ? Int32.MaxValue : Math.Abs(hash);
            }
        }

        private static string FormatException(Exception ex)
        {
            TargetInvocationException tie = ex as TargetInvocationException;
            if (tie != null && tie.InnerException != null)
                ex = tie.InnerException;
            return ex.GetType().FullName + ": " + ex.Message + Environment.NewLine + ex.StackTrace;
        }

        private sealed class TroopCandidate
        {
            public object Character;
            public int Level;
            public int Score;
            public string Text;
        }

        private sealed class SettlementCandidate
        {
            public Settlement Settlement;
            public int Score;
        }
    }

    internal static class InquiryHelper
    {
        internal static bool ShowMessage(string title, string text)
        {
            return ShowMessage(title, text, null);
        }

        internal static bool ShowMessage(string title, string text, Action closed)
        {
            return ShowChoice(title, text, "Continue", String.Empty, closed ?? delegate { }, null, false);
        }

        internal static bool ShowChoice(string title, string text, string affirmativeText, string negativeText,
            Action affirmative, Action negative)
        {
            return ShowChoice(title, text, affirmativeText, negativeText, affirmative, negative, true);
        }

        private static bool ShowChoice(string title, string text, string affirmativeText, string negativeText,
            Action affirmative, Action negative, bool showNegative)
        {
            try
            {
                Type inquiryType = ReflectionUtil.TypeByName("TaleWorlds.Library.InquiryData") ??
                    ReflectionUtil.TypeByName("TaleWorlds.Core.InquiryData");
                Type informationManager = ReflectionUtil.TypeByName("TaleWorlds.Library.InformationManager") ??
                    ReflectionUtil.TypeByName("TaleWorlds.Core.InformationManager") ??
                    ReflectionUtil.TypeByName("TaleWorlds.Core.MBInformationManager");
                if (inquiryType == null || informationManager == null)
                    return false;

                object inquiry = CreateInquiry(inquiryType, title, text, affirmativeText, negativeText,
                    affirmative ?? delegate { }, negative ?? delegate { }, showNegative);
                if (inquiry == null)
                    return false;

                MethodInfo selected = null;
                foreach (MethodInfo method in informationManager.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    if (method.Name == "ShowInquiry" && parameters.Length >= 1 && parameters[0].ParameterType == inquiryType)
                    {
                        selected = method;
                        break;
                    }
                }
                if (selected == null)
                    return false;

                object[] args = BuildDefaultArguments(selected.GetParameters());
                args[0] = inquiry;
                selected.Invoke(null, args);
                return true;
            }
            catch (Exception ex)
            {
                ModLog.Error("Inquiry display failed: " + ex.GetType().FullName + ": " + ex.Message);
                return false;
            }
        }

        private static object CreateInquiry(Type inquiryType, string title, string text, string affirmativeText,
            string negativeText, Action affirmative, Action negative, bool showNegative)
        {
            foreach (ConstructorInfo constructor in inquiryType.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
            {
                ParameterInfo[] parameters = constructor.GetParameters();
                if (parameters.Length < 8 || parameters[0].ParameterType != typeof(string) || parameters[1].ParameterType != typeof(string))
                    continue;

                object[] args = BuildDefaultArguments(parameters);
                int stringIndex = 0;
                int boolIndex = 0;
                int actionIndex = 0;
                for (int i = 0; i < parameters.Length; i++)
                {
                    Type type = parameters[i].ParameterType;
                    if (type == typeof(string))
                    {
                        if (stringIndex == 0) args[i] = title;
                        else if (stringIndex == 1) args[i] = text;
                        else if (stringIndex == 2) args[i] = affirmativeText;
                        else if (stringIndex == 3) args[i] = negativeText;
                        stringIndex++;
                    }
                    else if (type == typeof(bool))
                    {
                        args[i] = boolIndex == 0 || (boolIndex == 1 && showNegative);
                        boolIndex++;
                    }
                    else if (type == typeof(Action))
                    {
                        args[i] = actionIndex == 0 ? affirmative : (actionIndex == 1 ? negative : null);
                        actionIndex++;
                    }
                }
                if (stringIndex >= 4 && boolIndex >= 2 && actionIndex >= 2)
                    return constructor.Invoke(args);
            }
            return null;
        }

        private static object[] BuildDefaultArguments(ParameterInfo[] parameters)
        {
            object[] result = new object[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].HasDefaultValue)
                    result[i] = parameters[i].DefaultValue;
                else if (parameters[i].ParameterType.IsValueType)
                    result[i] = Activator.CreateInstance(parameters[i].ParameterType);
                else
                    result[i] = null;
            }
            return result;
        }
    }

    internal static class ReflectionUtil
    {
        internal static Type TypeByName(string fullName)
        {
            Type direct = Type.GetType(fullName, false);
            if (direct != null) return direct;
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type found = assemblies[i].GetType(fullName, false);
                if (found != null) return found;
            }
            return null;
        }

        internal static object GetStaticProperty(Type type, string name)
        {
            if (type == null) return null;
            try
            {
                PropertyInfo property = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                return property == null ? null : property.GetValue(null, null);
            }
            catch (Exception ex)
            {
                ModLog.Verbose("Static property read skipped: " + type.FullName + "." + name + " (" + ex.GetType().Name + ").");
                return null;
            }
        }

        internal static object GetProperty(object instance, string name)
        {
            if (instance == null) return null;

            Type type = instance.GetType();
            while (type != null)
            {
                try
                {
                    PropertyInfo property = type.GetProperty(
                        name,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                    if (property != null)
                        return property.GetValue(instance, null);
                }
                catch (Exception ex)
                {
                    ModLog.Verbose("Property read skipped: " + type.FullName + "." + name + " (" + ex.GetType().Name + ").");
                    return null;
                }
                type = type.BaseType;
            }
            return null;
        }

        internal static void SetProperty(object instance, string name, object value)
        {
            if (instance == null)
                throw new ArgumentNullException("instance");
            Type type = instance.GetType();
            while (type != null)
            {
                PropertyInfo property = type.GetProperty(name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (property != null)
                {
                    MethodInfo setter = property.GetSetMethod(true);
                    if (setter == null)
                        throw new MissingMethodException(type.FullName, "set_" + name);
                    setter.Invoke(instance, new object[] { value });
                    return;
                }
                type = type.BaseType;
            }
            throw new MissingMemberException(instance.GetType().FullName, name);
        }

        internal static bool ToBool(object value)
        {
            try { return value != null && Convert.ToBoolean(value); }
            catch { return false; }
        }

        internal static int ToInt(object value)
        {
            try { return value == null ? 0 : Convert.ToInt32(value); }
            catch { return 0; }
        }

        internal static string SearchText(object value)
        {
            if (value == null) return String.Empty;
            List<string> values = new List<string>();
            Add(values, GetProperty(value, "StringId"));
            Add(values, GetProperty(value, "Name"));

            object culture = GetProperty(value, "Culture");
            Add(values, GetProperty(culture, "StringId"));
            Add(values, GetProperty(culture, "Name"));

            Add(values, GetProperty(value, "Occupation"));

            object mapFaction = GetProperty(value, "MapFaction");
            Add(values, GetProperty(mapFaction, "StringId"));
            Add(values, GetProperty(mapFaction, "Name"));

            object ownerClan = GetProperty(value, "OwnerClan");
            Add(values, GetProperty(ownerClan, "StringId"));
            Add(values, GetProperty(ownerClan, "Name"));

            return String.Join(" ", values.ToArray()).ToLowerInvariant();
        }

        internal static CampaignVec2 GetAccessiblePoint(CampaignVec2 candidate, float radius)
        {
            try
            {
                object campaign = GetStaticProperty(TypeByName("TaleWorlds.CampaignSystem.Campaign"), "Current");
                object mapScene = GetProperty(campaign, "MapSceneWrapper");
                if (mapScene == null) return candidate;
                foreach (MethodInfo method in mapScene.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    if (method.Name == "GetAccessiblePointNearPosition" && parameters.Length == 2)
                    {
                        object result = method.Invoke(mapScene, new object[] { candidate, radius });
                        if (result is CampaignVec2)
                            return (CampaignVec2)result;
                    }
                }
            }
            catch (Exception ex)
            {
                ModLog.Verbose("Accessible-point lookup failed; using deterministic raw position: " + ex.Message);
            }
            return candidate;
        }

        private static void Add(List<string> values, object value)
        {
            if (value == null) return;
            try
            {
                string text = Convert.ToString(value);
                if (!String.IsNullOrWhiteSpace(text))
                    values.Add(text);
            }
            catch (Exception)
            {
                // Metadata scanning must never abort campaign loading because a game object has an unsafe ToString().
            }
        }
    }
}
