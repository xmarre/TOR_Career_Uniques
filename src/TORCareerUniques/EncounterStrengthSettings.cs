using System;
using System.Globalization;
using System.IO;
using MCM.Abstractions.Attributes;
using MCM.Abstractions.Attributes.v2;
using MCM.Abstractions.Base.Global;

namespace TORCareerUniques
{
    public sealed class EncounterStrengthSettings :
        AttributeGlobalSettings<EncounterStrengthSettings>
    {
        public override string Id
        {
            get { return "TORCareerUniques_EncounterStrength_v1"; }
        }

        public override string DisplayName
        {
            get { return "TOR Career Uniques - Encounter Strength"; }
        }

        public override string FolderName
        {
            get { return "TORCareerUniques"; }
        }

        public override string FormatType
        {
            get { return "json2"; }
        }

        public override int UIVersion
        {
            get { return 1; }
        }

        private int _roamingHostPercent =
            EncounterStrengthConfig.RoamingHostPercent;
        private int _guardianLocationPercent =
            EncounterStrengthConfig.GuardianLocationPercent;

        [SettingPropertyInteger(
            "Roaming host base strength", 25, 300, "0'%'",
            Order = 0, RequireRestart = false,
            HintText = "Scales the authored 100-125 troop base for newly " +
                "spawned roaming hosts. Collection progress and veteran tiers " +
                "remain multiplicative. Existing active hosts use the new " +
                "value when they next respawn or are rebuilt.")]
        [SettingPropertyGroup("Base Strength", GroupOrder = 0)]
        public int RoamingHostPercent
        {
            get { return _roamingHostPercent; }
            set
            {
                int clamped = Math.Max(25, Math.Min(300, value));
                if (_roamingHostPercent == clamped)
                    return;
                _roamingHostPercent = clamped;
                EncounterStrengthConfig.RoamingHostPercent = clamped;
            }
        }

        [SettingPropertyInteger(
            "Guardian location base strength", 25, 300, "0'%'",
            Order = 1, RequireRestart = false,
            HintText = "Scales the authored 110-135 troop base whenever a " +
                "guardian location materializes its defenders. Collection " +
                "progress and veteran tiers remain multiplicative.")]
        [SettingPropertyGroup("Base Strength", GroupOrder = 0)]
        public int GuardianLocationPercent
        {
            get { return _guardianLocationPercent; }
            set
            {
                int clamped = Math.Max(25, Math.Min(300, value));
                if (_guardianLocationPercent == clamped)
                    return;
                _guardianLocationPercent = clamped;
                EncounterStrengthConfig.GuardianLocationPercent = clamped;
            }
        }
    }

    internal static class EncounterStrengthConfig
    {
        private static readonly object Gate = new object();
        private static bool _initialized;
        private static bool _loading;
        private static string _path;
        private static int _roamingHostPercent = 100;
        private static int _guardianLocationPercent = 100;

        internal static int RoamingHostPercent
        {
            get
            {
                EnsureInitialized();
                lock (Gate)
                    return _roamingHostPercent;
            }
            set
            {
                EnsureInitialized();
                SetPercent(ref _roamingHostPercent, value);
            }
        }

        internal static int GuardianLocationPercent
        {
            get
            {
                EnsureInitialized();
                lock (Gate)
                    return _guardianLocationPercent;
            }
            set
            {
                EnsureInitialized();
                SetPercent(ref _guardianLocationPercent, value);
            }
        }

        private static void EnsureInitialized()
        {
            if (_initialized)
                return;

            lock (Gate)
            {
                if (_initialized)
                    return;
                _path = Path.Combine(ModLog.ResolveModuleDirectory(),
                    "TORCareerUniques.strength.ini");
                _initialized = true;
                LoadLocked();
                SaveLocked();
            }
        }

        private static void SetPercent(ref int field, int value)
        {
            lock (Gate)
            {
                int clamped = Math.Max(25, Math.Min(300, value));
                if (field == clamped)
                    return;
                field = clamped;
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
                    if (line.Length == 0 ||
                        line.StartsWith("#", StringComparison.Ordinal) ||
                        line.StartsWith(";", StringComparison.Ordinal))
                        continue;

                    int equals = line.IndexOf('=');
                    if (equals <= 0)
                        continue;

                    string key = line.Substring(0, equals).Trim();
                    string value = line.Substring(equals + 1).Trim();
                    int parsed;
                    if (!Int32.TryParse(value, NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out parsed))
                        continue;

                    if (String.Equals(key, "RoamingHostPercent",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        _roamingHostPercent =
                            Math.Max(25, Math.Min(300, parsed));
                    }
                    else if (String.Equals(key, "GuardianLocationPercent",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        _guardianLocationPercent =
                            Math.Max(25, Math.Min(300, parsed));
                    }
                }
            }
            catch (Exception ex)
            {
                ModLog.Error(
                    "Encounter strength settings load failed; current values " +
                    "retained: " + ex.GetType().Name + ": " + ex.Message);
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
                string directory = Path.GetDirectoryName(_path);
                if (!String.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                string content =
                    "# TOR Career Uniques encounter strength settings\n" +
                    "RoamingHostPercent=" +
                    _roamingHostPercent.ToString(
                        CultureInfo.InvariantCulture) + "\n" +
                    "GuardianLocationPercent=" +
                    _guardianLocationPercent.ToString(
                        CultureInfo.InvariantCulture) + "\n";
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
                ModLog.Error(
                    "Encounter strength settings save failed: " +
                    ex.GetType().Name + ": " + ex.Message);
            }
        }
    }
}
