using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using MCM.Abstractions.Attributes;
using MCM.Abstractions.Attributes.v2;
using MCM.Abstractions.Base.Global;
using MCM.Common;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

[assembly: AssemblyVersion("1.7.37.0")]
[assembly: AssemblyFileVersion("1.7.37.0")]

namespace TORCareerUniques
{
    public sealed class SubModule : MBSubModuleBase
    {
        protected override void OnSubModuleLoad()
        {
            ModLog.Initialize();
            EncounterHeroDeathGuard.Initialize();
            CareerUniqueRuntime.Initialize();
            SetItemRuntime.Initialize();
            SetItemRuntime.InitializeCompanionSetSupport();
            InventorySetTooltipBridge.Initialize();
            RuntimePerformanceGate.Initialize();
            EncounterAffinityRuntime.Initialize();
            McmHotkeyBridge.Initialize();
        }

        public override void BeginGameStart(Game game)
        {
            base.BeginGameStart(game);
        }

        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {
            CampaignGameStarter starter = gameStarterObject as CampaignGameStarter;
            if (starter != null)
                starter.AddBehavior(new UniqueEncounterBehavior());
        }

        protected override void OnApplicationTick(float dt)
        {
            // The direct widget patch installer is a one-time, constant-time check.
            UIIconPassThrough.Tick();
            SetItemRuntime.TickPendingCompanionSetWork();
            AdminBridge.Tick(dt);
            McmHotkeyBridge.Tick();
        }

        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            RuntimePerformanceGate.ResetSession();
            EncounterAffinityRuntime.ResetSession();
            base.OnBeforeInitialModuleScreenSetAsRoot();
        }

        protected override void OnSubModuleUnloaded()
        {
            RuntimePerformanceGate.ResetSession();
            EncounterAffinityRuntime.ResetSession();
            base.OnSubModuleUnloaded();
        }
    }

    // This class is the only settings/admin surface. Every setter writes through to
    // the mod-owned configuration so the MCM display and runtime values cannot diverge.
    public class ModSettings : AttributeGlobalSettings<ModSettings>
    {
        private static readonly Action RespawnMissingAction =
            AdminBridge.RespawnMissing;
        private static readonly Action RepairMissingRecoveredRelicsAction =
            RelicRewardIntegrity.RepairFromMcm;
        public override string Id { get { return "TORCareerUniques_v1_1"; } }
        public override string DisplayName { get { return "TOR Career Uniques"; } }
        public override string FolderName { get { return "TORCareerUniques"; } }
        public override string FormatType { get { return "json2"; } }
        public override int UIVersion { get { return 11; } }

        // Gauntlet reads visible DataSource properties repeatedly.  Keep those
        // reads in this in-memory view model; persistence belongs in setters and
        // must never place a monitor/file-system path on MCM's render path.
        private int _dropChancePercent = PersistentConfig.DropChancePercent;
        private bool _requireMatchingCareer = PersistentConfig.RequireMatchingCareer;
        private int _respawnDays = PersistentConfig.RespawnDays;
        private bool _loggingEnabled = PersistentConfig.LoggingEnabled;
        private bool _verboseLogging = PersistentConfig.VerboseLogging;

        [SettingPropertyInteger("Career set-piece drop chance", 0, 100, "0'%'", Order = 0, RequireRestart = false,
            HintText = "Chance that a valid encounter awards a career set piece. Before 5/5 it always selects an undiscovered piece; after 5/5 it grants a duplicate quality reroll. Set to 100% for testing.")]
        [SettingPropertyGroup("Acquisition", GroupOrder = 0)]
        public int DropChancePercent
        {
            get { return _dropChancePercent; }
            set
            {
                int clamped = Math.Max(0, Math.Min(100, value));
                if (_dropChancePercent == clamped) return;
                _dropChancePercent = clamped;
                PersistentConfig.DropChancePercent = clamped;
            }
        }

        [SettingPropertyBool("Require matching active career", Order = 1, RequireRestart = false,
            HintText = "A set-piece roll is only made when the player's current career matches the encounter's career.")]
        [SettingPropertyGroup("Acquisition", GroupOrder = 0)]
        public bool RequireMatchingCareer
        {
            get { return _requireMatchingCareer; }
            set
            {
                if (_requireMatchingCareer == value) return;
                _requireMatchingCareer = value;
                PersistentConfig.RequireMatchingCareer = value;
            }
        }

        [SettingPropertyInteger("Encounter respawn delay", 1, 60, "0 days", Order = 2, RequireRestart = false,
            HintText = "Campaign days before a defeated guardian site or roaming host returns.")]
        [SettingPropertyGroup("Acquisition", GroupOrder = 0)]
        public int RespawnDays
        {
            get { return _respawnDays; }
            set
            {
                int clamped = Math.Max(1, Math.Min(60, value));
                if (_respawnDays == clamped) return;
                _respawnDays = clamped;
                PersistentConfig.RespawnDays = clamped;
            }
        }

        private Dropdown<string> _heroRecruitmentMode =
            CreateRecruitmentModeDropdown();

        [SettingPropertyDropdown("Encounter hero recruitment", Order = 3, RequireRestart = false,
            HintText = "Disabled; Full Set Required; or Full Set + Final Victory Required. The final-victory mode requires the matching 5/5 set to be actively equipped when the original hero is defeated.")]
        [SettingPropertyGroup("Acquisition", GroupOrder = 0)]
        public Dropdown<string> HeroRecruitmentMode
        {
            get { return _heroRecruitmentMode; }
            set
            {
                _heroRecruitmentMode = value ?? CreateRecruitmentModeDropdown();
                PersistentConfig.HeroRecruitmentMode =
                    RecruitmentModeIndex(_heroRecruitmentMode.SelectedValue);
            }
        }

        private static Dropdown<string> CreateRecruitmentModeDropdown()
        {
            string[] values = new[]
            {
                "Disabled",
                "Full Set Required",
                "Full Set + Final Victory Required"
            };
            return new Dropdown<string>(values,
                Math.Max(0, Math.Min(2, PersistentConfig.HeroRecruitmentMode)));
        }

        private static int RecruitmentModeIndex(string value)
        {
            if (String.Equals(value, "Disabled", StringComparison.Ordinal)) return 0;
            if (String.Equals(value, "Full Set Required", StringComparison.Ordinal)) return 1;
            return 2;
        }

        [SettingPropertyBool("Enable logging", Order = 0, RequireRestart = false,
            HintText = "Writes encounter spawns, defeats, reward rolls and grants to TORCareerUniques.log.")]
        [SettingPropertyGroup("Diagnostics", GroupOrder = 1)]
        public bool LoggingEnabled
        {
            get { return _loggingEnabled; }
            set
            {
                if (_loggingEnabled == value) return;
                _loggingEnabled = value;
                PersistentConfig.LoggingEnabled = value;
            }
        }

        [SettingPropertyBool("Verbose logging", Order = 1, RequireRestart = false,
            HintText = "Also logs selected anchors, troop pools and home-zone corrections.")]
        [SettingPropertyGroup("Diagnostics", GroupOrder = 1)]
        public bool VerboseLogging
        {
            get { return _verboseLogging; }
            set
            {
                if (_verboseLogging == value) return;
                _verboseLogging = value;
                PersistentConfig.VerboseLogging = value;
            }
        }

        [SettingPropertyButton("Repair missing recovered relics", 19, false,
            "Scans every living character, hero equipment set, mobile-party inventory, settlement inventory and stash before restoring anything. Existing relics are never moved. It also removes the single duplicate that the previous faulty recovery build could place in the active main inventory while the original remained on another character.", Content = "Repair now")]
        [SettingPropertyGroup("Relic Encounters & Testing", GroupOrder = 2)]
        public Action RepairMissingRecoveredRelics
        {
            get { return RepairMissingRecoveredRelicsAction; }
            set { }
        }

        [SettingPropertyButton("Respawn missing encounters", 20, false,
            "Clears cooldowns and immediately recreates every currently missing encounter. Recovered set pieces remain recovered.", Content = "Respawn now")]
        [SettingPropertyGroup("Relic Encounters & Testing", GroupOrder = 2)]
        public Action RespawnMissingEncounters { get { return RespawnMissingAction; } set { } }

        // MCM's dropdown popup is not scrollable and is clipped by the fixed options
        // frame. Keep every selector to five entries or fewer so every career remains
        // reachable at common UI scales.
        private Dropdown<string> _adminBretonniaCareer = CreateAdminCareerDropdown(
            "GrailDamsel", "GrailKnight", "KnightOldWorld");
        private Dropdown<string> _adminEmpireCareer = CreateAdminCareerDropdown(
            "WarriorPriest", "Mercenary", "WitchHunter", "WarriorPriestUlric", "ImperialMagister");
        private Dropdown<string> _adminUndeadCareer = CreateAdminCareerDropdown(
            "MinorVampire", "BloodKnight", "Necromancer", "BlackGrailKnight", "Necrarch");
        private Dropdown<string> _adminElfCareer = CreateAdminCareerDropdown(
            "Waywatcher", "Spellsinger", "Warden", "GreyLord");
        private Dropdown<string> _adminDwarfGreenskinCareer = CreateAdminCareerDropdown(
            "Ironbreaker", "Slayer", "Runelord", "OrcBoss", "OrcShaman");

        // Preset comparison reads button values too. Static cached commands keep
        // those values referentially stable across the live and default settings
        // instances and dispatch to the actual MCM singleton only when clicked.
        private static readonly Action ViewBretonniaEncounterAction =
            delegate { ShowSelectedEncounter(Instance == null ? null : Instance._adminBretonniaCareer); };
        private static readonly Action GrantBretonniaSetAction =
            delegate { GrantSelectedFullTestSet(Instance == null ? null : Instance._adminBretonniaCareer); };
        private static readonly Action ViewEmpireEncounterAction =
            delegate { ShowSelectedEncounter(Instance == null ? null : Instance._adminEmpireCareer); };
        private static readonly Action GrantEmpireSetAction =
            delegate { GrantSelectedFullTestSet(Instance == null ? null : Instance._adminEmpireCareer); };
        private static readonly Action ViewUndeadEncounterAction =
            delegate { ShowSelectedEncounter(Instance == null ? null : Instance._adminUndeadCareer); };
        private static readonly Action GrantUndeadSetAction =
            delegate { GrantSelectedFullTestSet(Instance == null ? null : Instance._adminUndeadCareer); };
        private static readonly Action ViewElfEncounterAction =
            delegate { ShowSelectedEncounter(Instance == null ? null : Instance._adminElfCareer); };
        private static readonly Action GrantElfSetAction =
            delegate { GrantSelectedFullTestSet(Instance == null ? null : Instance._adminElfCareer); };
        private static readonly Action ViewDwarfGreenskinEncounterAction =
            delegate { ShowSelectedEncounter(Instance == null ? null : Instance._adminDwarfGreenskinCareer); };
        private static readonly Action GrantDwarfGreenskinSetAction =
            delegate { GrantSelectedFullTestSet(Instance == null ? null : Instance._adminDwarfGreenskinCareer); };

        [SettingPropertyDropdown("Bretonnia / Reiksguard career set", Order = 0, RequireRestart = false,
            HintText = "Select a career set, then view its encounter or grant an isolated test copy.")]
        [SettingPropertyGroup("Relic Encounters & Testing", GroupOrder = 2)]
        public Dropdown<string> AdminBretonniaCareer
        {
            get { return _adminBretonniaCareer; }
            set { _adminBretonniaCareer = value; }
        }

        [SettingPropertyButton("Selected Bretonnia / Reiksguard encounter", 1, false,
            "Shows the selected set's encounter, hero, status, collection progress and reliable settlement tracking target.", Content = "View / track")]
        [SettingPropertyGroup("Relic Encounters & Testing", GroupOrder = 2)]
        public Action ViewBretonniaEncounter
        {
            get { return ViewBretonniaEncounterAction; }
            set { }
        }

        [SettingPropertyButton("Grant selected Bretonnia / Reiksguard test set", 2, false,
            "Grants five [ADMIN COPY] items for the selected career without changing acquisition status.", Content = "Grant set")]
        [SettingPropertyGroup("Relic Encounters & Testing", GroupOrder = 2)]
        public Action GrantBretonniaSet
        {
            get { return GrantBretonniaSetAction; }
            set { }
        }

        [SettingPropertyDropdown("Empire career set", Order = 4, RequireRestart = false,
            HintText = "Select a career set, then view its encounter or grant an isolated test copy.")]
        [SettingPropertyGroup("Relic Encounters & Testing", GroupOrder = 2)]
        public Dropdown<string> AdminEmpireCareer
        {
            get { return _adminEmpireCareer; }
            set { _adminEmpireCareer = value; }
        }

        [SettingPropertyButton("Selected Empire encounter", 5, false,
            "Shows the selected set's encounter, hero, status, collection progress and reliable settlement tracking target.", Content = "View / track")]
        [SettingPropertyGroup("Relic Encounters & Testing", GroupOrder = 2)]
        public Action ViewEmpireEncounter
        {
            get { return ViewEmpireEncounterAction; }
            set { }
        }

        [SettingPropertyButton("Grant selected Empire test set", 6, false,
            "Grants five [ADMIN COPY] items for the selected career without changing acquisition status.", Content = "Grant set")]
        [SettingPropertyGroup("Relic Encounters & Testing", GroupOrder = 2)]
        public Action GrantEmpireSet
        {
            get { return GrantEmpireSetAction; }
            set { }
        }

        [SettingPropertyDropdown("Undead career set", Order = 8, RequireRestart = false,
            HintText = "Select a career set, then view its encounter or grant an isolated test copy.")]
        [SettingPropertyGroup("Relic Encounters & Testing", GroupOrder = 2)]
        public Dropdown<string> AdminUndeadCareer
        {
            get { return _adminUndeadCareer; }
            set { _adminUndeadCareer = value; }
        }

        [SettingPropertyButton("Selected Undead encounter", 9, false,
            "Shows the selected set's encounter, hero, status, collection progress and reliable settlement tracking target.", Content = "View / track")]
        [SettingPropertyGroup("Relic Encounters & Testing", GroupOrder = 2)]
        public Action ViewUndeadEncounter
        {
            get { return ViewUndeadEncounterAction; }
            set { }
        }

        [SettingPropertyButton("Grant selected Undead test set", 10, false,
            "Grants five [ADMIN COPY] items for the selected career without changing acquisition status.", Content = "Grant set")]
        [SettingPropertyGroup("Relic Encounters & Testing", GroupOrder = 2)]
        public Action GrantUndeadSet
        {
            get { return GrantUndeadSetAction; }
            set { }
        }

        [SettingPropertyDropdown("Elven career set", Order = 12, RequireRestart = false,
            HintText = "Select a career set, then view its encounter or grant an isolated test copy.")]
        [SettingPropertyGroup("Relic Encounters & Testing", GroupOrder = 2)]
        public Dropdown<string> AdminElfCareer
        {
            get { return _adminElfCareer; }
            set { _adminElfCareer = value; }
        }

        [SettingPropertyButton("Selected Elven encounter", 13, false,
            "Shows the selected set's encounter, hero, status, collection progress and reliable settlement tracking target.", Content = "View / track")]
        [SettingPropertyGroup("Relic Encounters & Testing", GroupOrder = 2)]
        public Action ViewElfEncounter
        {
            get { return ViewElfEncounterAction; }
            set { }
        }

        [SettingPropertyButton("Grant selected Elven test set", 14, false,
            "Grants five [ADMIN COPY] items for the selected career without changing acquisition status.", Content = "Grant set")]
        [SettingPropertyGroup("Relic Encounters & Testing", GroupOrder = 2)]
        public Action GrantElfSet
        {
            get { return GrantElfSetAction; }
            set { }
        }

        [SettingPropertyDropdown("Dwarf / Greenskin career set", Order = 16, RequireRestart = false,
            HintText = "Select a career set, then view its encounter or grant an isolated test copy.")]
        [SettingPropertyGroup("Relic Encounters & Testing", GroupOrder = 2)]
        public Dropdown<string> AdminDwarfGreenskinCareer
        {
            get { return _adminDwarfGreenskinCareer; }
            set { _adminDwarfGreenskinCareer = value; }
        }

        [SettingPropertyButton("Selected Dwarf / Greenskin encounter", 17, false,
            "Shows the selected set's encounter, hero, status, collection progress and reliable settlement tracking target.", Content = "View / track")]
        [SettingPropertyGroup("Relic Encounters & Testing", GroupOrder = 2)]
        public Action ViewDwarfGreenskinEncounter
        {
            get { return ViewDwarfGreenskinEncounterAction; }
            set { }
        }

        [SettingPropertyButton("Grant selected Dwarf / Greenskin test set", 18, false,
            "Grants five [ADMIN COPY] items for the selected career without changing acquisition status.", Content = "Grant set")]
        [SettingPropertyGroup("Relic Encounters & Testing", GroupOrder = 2)]
        public Action GrantDwarfGreenskinSet
        {
            get { return GrantDwarfGreenskinSetAction; }
            set { }
        }

        private static Dropdown<string> CreateAdminCareerDropdown(params string[] careerIds)
        {
            return new Dropdown<string>(
                new List<string>(SetItemRuntime.GetCareerChoiceLabelsFor(careerIds)), 0);
        }

        private static void GrantSelectedFullTestSet(Dropdown<string> dropdown)
        {
            string selection = dropdown == null ? null : dropdown.SelectedValue;
            AdminBridge.GrantSelectedTestSet(selection);
        }

        private static void ShowSelectedEncounter(Dropdown<string> dropdown)
        {
            string selection = dropdown == null ? null : dropdown.SelectedValue;
            AdminBridge.ShowSelectedEncounter(selection);
        }
    }

    internal static class PersistentConfig
    {
        private static readonly object Gate = new object();
        private static string _path;
        private static bool _initialized;
        private static bool _loading;
        private static int _dropChancePercent = 20;
        private static bool _requireMatchingCareer = true;
        private static int _respawnDays = 14;
        private static int _heroRecruitmentMode = 2;
        private static bool _loggingEnabled = true;
        private static bool _verboseLogging;

        internal static string PathOnDisk { get { EnsureInitialized(); return _path; } }

        internal static int DropChancePercent
        {
            get { EnsureInitialized(); lock (Gate) return _dropChancePercent; }
            set { EnsureInitialized(); SetInt(ref _dropChancePercent, value, 0, 100); }
        }

        internal static bool RequireMatchingCareer
        {
            get { EnsureInitialized(); lock (Gate) return _requireMatchingCareer; }
            set { EnsureInitialized(); SetBool(ref _requireMatchingCareer, value); }
        }

        internal static int RespawnDays
        {
            get { EnsureInitialized(); lock (Gate) return _respawnDays; }
            set { EnsureInitialized(); SetInt(ref _respawnDays, value, 1, 60); }
        }

        internal static int HeroRecruitmentMode
        {
            get { EnsureInitialized(); lock (Gate) return _heroRecruitmentMode; }
            set { EnsureInitialized(); SetInt(ref _heroRecruitmentMode, value, 0, 2); }
        }

        internal static bool LoggingEnabled
        {
            get { EnsureInitialized(); lock (Gate) return _loggingEnabled; }
            set { EnsureInitialized(); SetBool(ref _loggingEnabled, value); }
        }

        internal static bool VerboseLogging
        {
            get { EnsureInitialized(); lock (Gate) return _verboseLogging; }
            set { EnsureInitialized(); SetBool(ref _verboseLogging, value); }
        }

        internal static void Initialize(string moduleDirectory)
        {
            lock (Gate)
            {
                if (_initialized)
                    return;
                _path = System.IO.Path.Combine(moduleDirectory, "TORCareerUniques.settings.ini");
                _initialized = true;
                LoadLocked();
                SaveLocked();
            }
        }

        internal static string Describe()
        {
            EnsureInitialized();
            lock (Gate)
            {
                return "Career set-piece drop chance: " + _dropChancePercent + "%\n" +
                    "Require matching active career: " + YesNo(_requireMatchingCareer) + "\n" +
                    "Encounter respawn delay: " + _respawnDays + " campaign days\n" +
                    "Encounter hero recruitment: " +
                    RecruitmentModeName(_heroRecruitmentMode) + "\n" +
                    "Logging: " + OnOff(_loggingEnabled) + "\n" +
                    "Verbose logging: " + OnOff(_verboseLogging) + "\n\n" +
                    "Saved to:\n" + _path;
            }
        }

        private static void EnsureInitialized()
        {
            if (_initialized)
                return;
            Initialize(ModLog.ResolveModuleDirectory());
        }

        private static void SetInt(ref int field, int value, int min, int max)
        {
            lock (Gate)
            {
                int clamped = Math.Max(min, Math.Min(max, value));
                if (field == clamped)
                    return;
                field = clamped;
                if (!_loading)
                    SaveLocked();
            }
        }

        private static void SetBool(ref bool field, bool value)
        {
            lock (Gate)
            {
                if (field == value)
                    return;
                field = value;
                if (!_loading)
                    SaveLocked();
            }
        }

        private static void LoadLocked()
        {
            if (String.IsNullOrEmpty(_path) || !File.Exists(_path))
                return;

            _loading = true;
            try
            {
                string[] lines = File.ReadAllLines(_path);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith(";", StringComparison.Ordinal))
                        continue;
                    int equals = line.IndexOf('=');
                    if (equals <= 0)
                        continue;
                    string key = line.Substring(0, equals).Trim();
                    string value = line.Substring(equals + 1).Trim();
                    int parsedInt;
                    bool parsedBool;
                    if (String.Equals(key, "DropChancePercent", StringComparison.OrdinalIgnoreCase) && Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedInt))
                        _dropChancePercent = Math.Max(0, Math.Min(100, parsedInt));
                    else if (String.Equals(key, "RequireMatchingCareer", StringComparison.OrdinalIgnoreCase) && Boolean.TryParse(value, out parsedBool))
                        _requireMatchingCareer = parsedBool;
                    else if (String.Equals(key, "RespawnDays", StringComparison.OrdinalIgnoreCase) && Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedInt))
                        _respawnDays = Math.Max(1, Math.Min(60, parsedInt));
                    else if (String.Equals(key, "HeroRecruitmentMode", StringComparison.OrdinalIgnoreCase) && Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedInt))
                        _heroRecruitmentMode = Math.Max(0, Math.Min(2, parsedInt));
                    else if (String.Equals(key, "HeroCaptureChancePercent", StringComparison.OrdinalIgnoreCase))
                    {
                        // Retired v1.6.x key. Random captivity must never re-enter the
                        // encounter lifecycle; retaining the line is harmless.
                    }
                    else if (String.Equals(key, "LoggingEnabled", StringComparison.OrdinalIgnoreCase) && Boolean.TryParse(value, out parsedBool))
                        _loggingEnabled = parsedBool;
                    else if (String.Equals(key, "VerboseLogging", StringComparison.OrdinalIgnoreCase) && Boolean.TryParse(value, out parsedBool))
                        _verboseLogging = parsedBool;
                }
            }
            catch (Exception ex)
            {
                ModLog.Error("Settings load failed; defaults/current values retained: " + ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                _loading = false;
            }
        }

        private static void SaveLocked()
        {
            if (String.IsNullOrEmpty(_path))
                return;
            try
            {
                string directory = System.IO.Path.GetDirectoryName(_path);
                if (!String.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                string content =
                    "# TOR Career Uniques v1.7.16 settings\n" +
                    "DropChancePercent=" + _dropChancePercent.ToString(CultureInfo.InvariantCulture) + "\n" +
                    "RequireMatchingCareer=" + _requireMatchingCareer.ToString(CultureInfo.InvariantCulture) + "\n" +
                    "RespawnDays=" + _respawnDays.ToString(CultureInfo.InvariantCulture) + "\n" +
                    "HeroRecruitmentMode=" + _heroRecruitmentMode.ToString(CultureInfo.InvariantCulture) + "\n" +
                    "LoggingEnabled=" + _loggingEnabled.ToString(CultureInfo.InvariantCulture) + "\n" +
                    "VerboseLogging=" + _verboseLogging.ToString(CultureInfo.InvariantCulture) + "\n";
                string temporary = _path + ".tmp";
                File.WriteAllText(temporary, content);
                if (File.Exists(_path))
                {
                    string backup = _path + ".bak";
                    try
                    {
                        File.Replace(temporary, _path, backup, true);
                        if (File.Exists(backup))
                            File.Delete(backup);
                    }
                    catch
                    {
                        File.Copy(temporary, _path, true);
                        File.Delete(temporary);
                    }
                }
                else
                {
                    File.Move(temporary, _path);
                }
            }
            catch (Exception ex)
            {
                ModLog.Error("Settings save failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static string YesNo(bool value) { return value ? "yes" : "no"; }
        private static string OnOff(bool value) { return value ? "enabled" : "disabled"; }
        private static string RecruitmentModeName(int value)
        {
            if (value <= 0) return "disabled";
            if (value == 1) return "full set required";
            return "full set + final victory required";
        }
    }

    internal static class ModConfig
    {
        internal static int DropChancePercent { get { return PersistentConfig.DropChancePercent; } }
        internal static bool RequireMatchingCareer { get { return PersistentConfig.RequireMatchingCareer; } }
        internal static int RespawnDays { get { return PersistentConfig.RespawnDays; } }
        internal static int HeroRecruitmentMode { get { return PersistentConfig.HeroRecruitmentMode; } }
        internal static bool LoggingEnabled { get { return PersistentConfig.LoggingEnabled; } }
        internal static bool VerboseLogging { get { return PersistentConfig.VerboseLogging; } }
    }

    internal static class ModLog
    {
        private static string _moduleDirectory;
        private static readonly object Gate = new object();

        internal static void Initialize()
        {
            _moduleDirectory = ResolveModuleDirectory();
            PersistentConfig.Initialize(_moduleDirectory);
            Write("INFO", "TOR Career Uniques v1.7.37 loaded. Pre-session load guard, early trait registration, and campaign-map-gated encounter maintenance active.");
        }

        internal static void LogMcmStatus()
        {
            try
            {
                ModSettings settings = ModSettings.Instance;
                if (settings == null)
                    Write("WARN", "MCM did not discover settings id TORCareerUniques_v1_1. No settlement-menu fallback is registered.");
                else
                    Write("INFO", "MCM discovered settings id TORCareerUniques_v1_1.");
            }
            catch (Exception ex)
            {
                Write("WARN", "MCM settings lookup failed (" + ex.GetType().Name + "). No settlement-menu fallback is registered.");
            }
        }

        internal static void AlwaysInfo(string message)
        {
            Write("INFO", message);
        }

        internal static void Info(string message)
        {
            if (!ModConfig.LoggingEnabled)
                return;
            Write("INFO", message);
        }

        internal static void Verbose(string message)
        {
            if (ModConfig.LoggingEnabled && ModConfig.VerboseLogging)
                Write("TRACE", message);
        }

        internal static void Error(string message)
        {
            Write("ERROR", message);
        }

        private static void Write(string level, string message)
        {
            try
            {
                lock (Gate)
                {
                    if (String.IsNullOrEmpty(_moduleDirectory))
                        _moduleDirectory = ResolveModuleDirectory();
                    Directory.CreateDirectory(_moduleDirectory);
                    File.AppendAllText(System.IO.Path.Combine(_moduleDirectory, "TORCareerUniques.log"),
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " | " + level + " | " + message + Environment.NewLine);
                }
            }
            catch { }
        }

        internal static string ResolveModuleDirectory()
        {
            try
            {
                DirectoryInfo directory = new FileInfo(Assembly.GetExecutingAssembly().Location).Directory;
                if (directory != null && directory.Parent != null && directory.Parent.Parent != null)
                    return directory.Parent.Parent.FullName;
            }
            catch { }
            return AppDomain.CurrentDomain.BaseDirectory;
        }
    }

    internal static class AdminBridge
    {
        private static UniqueEncounterBehavior _behavior;
        private static bool _applicationTickRequested;

        internal static void Attach(UniqueEncounterBehavior behavior)
        {
            _behavior = behavior;
        }

        internal static void RequestApplicationTick()
        {
            _applicationTickRequested = true;
        }

        internal static void Tick(float dt)
        {
            if (!_applicationTickRequested)
                return;
            if (Campaign.Current == null)
            {
                _behavior = null;
                _applicationTickRequested = false;
                return;
            }

            // Deferred encounter UI must not retry or reconcile while an Options/MCM
            // screen owns the UI. Keep the request pending and resume when the screen
            // closes; the per-frame cost while Options is open is only the cached
            // TopScreen/type-name check above, with no resolver, inquiry or campaign work.
            if (McmHotkeyBridge.IsOptionsScreenActive())
                return;

            UniqueEncounterBehavior behavior = _behavior;
            if (behavior != null)
            {
                behavior.ProcessApplicationTick(dt);
                _applicationTickRequested = behavior.RequiresApplicationTick();
            }
            else
            {
                _applicationTickRequested = false;
            }
        }

        internal static void ShowSelectedEncounter(string selection)
        {
            UniqueEncounterBehavior behavior = _behavior;
            if (behavior == null)
            {
                InquiryHelper.ShowMessage("TOR Career Uniques", "No campaign is currently loaded.");
                return;
            }
            string careerId = SetItemRuntime.ResolveCareerChoice(selection);
            if (String.IsNullOrEmpty(careerId))
            {
                InquiryHelper.ShowMessage("TOR Career Uniques",
                    "The selected career set could not be resolved.");
                return;
            }
            behavior.ShowEncounter(careerId);
        }

        internal static void RespawnMissing()
        {
            UniqueEncounterBehavior behavior = _behavior;
            if (behavior == null)
            {
                InquiryHelper.ShowMessage("TOR Career Uniques", "No campaign is currently loaded.");
                return;
            }
            behavior.AdminRespawnMissing();
        }

        internal static bool HasDiscoveredSetPiece(string careerId, int pieceIndex)
        {
            UniqueEncounterBehavior behavior = _behavior;
            return behavior != null && behavior.HasDiscoveredSetPiece(careerId, pieceIndex);
        }

        internal static bool RecordDiscoveredSetPiece(string careerId, int pieceIndex)
        {
            UniqueEncounterBehavior behavior = _behavior;
            return behavior != null && behavior.RecordDiscoveredSetPiece(careerId, pieceIndex);
        }

        internal static int GetDiscoveredSetPieceCount(string careerId)
        {
            UniqueEncounterBehavior behavior = _behavior;
            return behavior == null ? -1 : behavior.GetDiscoveredSetPieceCount(careerId);
        }

        internal static void GrantSelectedTestSet(string selection)
        {
            if (_behavior == null)
            {
                ModLog.Error("Admin full-set grant rejected: no campaign behavior is attached.");
                InquiryHelper.ShowMessage("TOR Career Uniques", "No campaign is currently loaded.");
                return;
            }

            string careerId = SetItemRuntime.ResolveCareerChoice(selection);
            ModLog.Info("Admin full-set grant button invoked. Selection='" +
                (selection ?? "<null>") + "'; career='" + (careerId ?? "<unresolved>") + "'.");

            if (String.IsNullOrEmpty(careerId))
            {
                ModLog.Error("Admin full-set grant failed at selection resolution: '" +
                    (selection ?? "<null>") + "'.");
                InquiryHelper.ShowMessage("Admin Test Set Failed",
                    "The selected career could not be resolved. Reopen Mod Options, select a career, and try again. Check TORCareerUniques.log for details.");
                return;
            }

            string result;
            string error;
            if (SetItemRuntime.TryGrantAdminSet(careerId, out result, out error))
            {
                InquiryHelper.ShowMessage("Admin Test Set Granted", result + "\n\n" +
                    SetItemRuntime.DescribeSetProgress(careerId));
            }
            else
            {
                InquiryHelper.ShowMessage("Admin Test Set Failed",
                    error ?? "Unknown error. Check TORCareerUniques.log.");
            }
        }
    }
}
