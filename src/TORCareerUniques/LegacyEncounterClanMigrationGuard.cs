using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace TORCareerUniques
{
    public sealed class LegacyEncounterClanMigrationGuardSubModule :
        MBSubModuleBase
    {
        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            UniqueEncounterBehavior.InitializeLegacyEncounterClanMigrationGuard();
        }

        protected override void OnGameStart(Game game,
            IGameStarter gameStarterObject)
        {
            base.OnGameStart(game, gameStarterObject);
            CampaignGameStarter starter =
                gameStarterObject as CampaignGameStarter;
            if (starter != null)
            {
                starter.AddBehavior(
                    new LegacyEncounterClanMigrationMarkerBehavior());
            }
        }
    }

    internal sealed class LegacyEncounterClanMigrationMarkerBehavior :
        CampaignBehaviorBase
    {
        private const int CurrentMigrationVersion = 1;
        private static LegacyEncounterClanMigrationMarkerBehavior _current;
        private int _migrationVersion;

        internal static bool IsComplete
        {
            get
            {
                return _current != null &&
                    _current._migrationVersion >= CurrentMigrationVersion;
            }
        }

        internal static void MarkComplete()
        {
            if (_current != null)
                _current._migrationVersion = CurrentMigrationVersion;
        }

        public override void RegisterEvents()
        {
            _current = this;
        }

        public override void SyncData(IDataStore dataStore)
        {
            _current = this;
            dataStore.SyncData("torcu_shared_clan_migration_version",
                ref _migrationVersion);
        }
    }

    internal sealed partial class UniqueEncounterBehavior
    {
        private const string LegacyClanMigrationGuardHarmonyId =
            "torcareeruniques.legacy-clan-migration-guard.1.7.39";
        private static bool _legacyClanMigrationGuardInstalled;

        internal static void InitializeLegacyEncounterClanMigrationGuard()
        {
            if (_legacyClanMigrationGuardInstalled)
                return;

            try
            {
                MethodInfo migration = AccessTools.Method(
                    typeof(UniqueEncounterBehavior),
                    "MigrateLegacyEncounterClansToSharedClan");
                MethodInfo prefix = AccessTools.Method(
                    typeof(UniqueEncounterBehavior),
                    nameof(BeforeLegacyEncounterClanMigration));
                MethodInfo postfix = AccessTools.Method(
                    typeof(UniqueEncounterBehavior),
                    nameof(AfterLegacyEncounterClanMigration));
                if (migration == null || prefix == null || postfix == null)
                {
                    throw new MissingMethodException(
                        "Shared encounter-clan migration method was not found.");
                }

                Harmony harmony = new Harmony(
                    LegacyClanMigrationGuardHarmonyId);
                harmony.Patch(migration,
                    prefix: new HarmonyMethod(prefix)
                    {
                        priority = Priority.First
                    },
                    postfix: new HarmonyMethod(postfix)
                    {
                        priority = Priority.Last
                    });

                _legacyClanMigrationGuardInstalled = true;
                ModLog.AlwaysInfo(
                    "Installed persisted legacy encounter-clan migration guard.");
            }
            catch (Exception ex)
            {
                ModLog.Error(
                    "Legacy encounter-clan migration guard could not be installed: " +
                    ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static bool BeforeLegacyEncounterClanMigration()
        {
            if (LegacyEncounterClanMigrationMarkerBehavior.IsComplete)
                return false;

            List<Clan> legacyClans = new List<Clan>();
            foreach (Clan clan in Clan.All)
            {
                if (IsLegacyEncounterClan(clan))
                    legacyClans.Add(clan);
            }

            if (legacyClans.Count == 0)
            {
                LegacyEncounterClanMigrationMarkerBehavior.MarkComplete();
                return false;
            }

            if (!LegacyClansOwnPersistentState(legacyClans))
            {
                LegacyEncounterClanMigrationMarkerBehavior.MarkComplete();
                ModLog.AlwaysInfo(
                    "Legacy TORCU encounter clans were already retired; " +
                    "recorded migration completion without repeating native " +
                    "clan-destruction notifications.");
                return false;
            }

            return true;
        }

        private static void AfterLegacyEncounterClanMigration()
        {
            LegacyEncounterClanMigrationMarkerBehavior.MarkComplete();
        }

        private static bool LegacyClansOwnPersistentState(
            List<Clan> legacyClans)
        {
            HashSet<Clan> legacySet = new HashSet<Clan>(legacyClans);
            for (int i = 0; i < legacyClans.Count; i++)
            {
                Clan clan = legacyClans[i];
                if (clan.Heroes.Count != 0 || clan.Settlements.Count != 0)
                    return true;
            }

            foreach (MobileParty party in MobileParty.All)
            {
                if (party != null && legacySet.Contains(party.ActualClan))
                    return true;
            }

            return false;
        }
    }
}
