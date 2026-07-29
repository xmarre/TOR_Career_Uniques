using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace TORCareerUniques
{
    public sealed class SaveLoadPerformanceSubModule : MBSubModuleBase
    {
        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            SaveLoadPerformanceRuntime.Initialize();
        }
    }

    internal static class SaveLoadPerformanceRuntime
    {
        private const string HarmonyId =
            "torcareeruniques.save-load-fast-path.1.7.35";
        private static bool _installed;

        internal static void Initialize()
        {
            if (_installed)
                return;

            try
            {
                Harmony harmony = new Harmony(HarmonyId);

                MethodInfo migration = AccessTools.Method(typeof(SetItemRuntime),
                    "MigrateKnownVisualsOnce");
                MethodInfo migrationPrefix = AccessTools.Method(typeof(SetItemRuntime),
                    "ReplaceKnownVisualMigrationWithFastAudit");
                if (migration == null || migrationPrefix == null)
                    throw new MissingMethodException(
                        "Set-item visual migration fast-path target was not found.");
                harmony.Patch(migration, prefix: new HarmonyMethod(migrationPrefix)
                {
                    priority = Priority.First
                });

                MethodInfo sessionLaunch = AccessTools.Method(
                    typeof(UniqueEncounterBehavior), "OnSessionLaunched",
                    new[] { typeof(CampaignGameStarter) });
                MethodInfo sessionPrefix = AccessTools.Method(
                    typeof(UniqueEncounterBehavior),
                    "BeginSaveLoadEncounterAuditBatch");
                MethodInfo sessionPostfix = AccessTools.Method(
                    typeof(UniqueEncounterBehavior),
                    "EndSaveLoadEncounterAuditBatch");
                if (sessionLaunch == null || sessionPrefix == null ||
                    sessionPostfix == null)
                    throw new MissingMethodException(
                        "Encounter session-load audit batch target was not found.");
                harmony.Patch(sessionLaunch,
                    prefix: new HarmonyMethod(sessionPrefix)
                    {
                        priority = Priority.First
                    },
                    postfix: new HarmonyMethod(sessionPostfix)
                    {
                        priority = Priority.Last
                    });

                MethodInfo audit = AccessTools.Method(
                    typeof(UniqueEncounterBehavior), "AuditPersistentHero",
                    new[]
                    {
                        typeof(string), typeof(Hero), typeof(Clan),
                        typeof(string).MakeByRefType()
                    });
                MethodInfo auditPrefix = AccessTools.Method(
                    typeof(UniqueEncounterBehavior),
                    "ReplacePersistentHeroAuditWithFastAudit");
                if (audit == null || auditPrefix == null)
                    throw new MissingMethodException(
                        "Persistent encounter-hero audit fast-path target was not found.");
                harmony.Patch(audit, prefix: new HarmonyMethod(auditPrefix)
                {
                    priority = Priority.First
                });

                _installed = true;
                ModLog.Info("Installed bounded save-load visual and encounter-hero fast paths.");
            }
            catch (Exception ex)
            {
                // Falling back to the existing audited resolver is safer than
                // preventing the module from loading on an unexpected API shape.
                ModLog.Error("Save-load fast paths could not be installed; " +
                    "the existing full validation path remains active. " +
                    ex.GetType().Name + ": " + ex.Message);
            }
        }
    }

    internal static partial class SetItemRuntime
    {
        private static MethodInfo _registeredItemLookup;
        private static object _registeredItemManager;

        private static bool ReplaceKnownVisualMigrationWithFastAudit()
        {
            MigrateKnownVisualsFastAudit();
            return false;
        }

        private static void MigrateKnownVisualsFastAudit()
        {
            EnsureVisualResolverSession();
            if (_visualMigrationPassCompleted)
                return;

            DiscoverSetItems();
            List<KeyValuePair<string, SetItemInstance>> invalid =
                new List<KeyValuePair<string, SetItemInstance>>();
            int structurallyValid = 0;

            foreach (KeyValuePair<string, SetItemInstance> pair in KnownSetItemsById)
            {
                if (pair.Value == null ||
                    VisualMigrationAttemptedItemIds.Contains(pair.Key))
                    continue;

                if (IsKnownVisualStructurallyValidFast(pair.Value))
                {
                    VisualMigrationAttemptedItemIds.Add(pair.Key);
                    structurallyValid++;
                }
                else
                {
                    invalid.Add(pair);
                }
            }

            // A normal current save ends here. No CharacterObject or ItemObject catalogue
            // is touched merely to rediscover the base visuals already copied into the
            // generated items stored by TOR.
            if (invalid.Count == 0)
            {
                _visualMigrationPassCompleted = true;
                ModLog.Verbose("Save-load visual fast audit retained all " +
                    structurallyValid +
                    " known set item(s); global visual catalogue resolution was skipped.");
                return;
            }

            // Preserve the v1.7.29+ repair in full for genuinely malformed legacy data.
            // Readiness is checked only after the structural pass proves repair is needed.
            if (!IsVisualResolverReady())
                return;

            _visualMigrationPassCompleted = true;
            for (int i = 0; i < invalid.Count; i++)
            {
                string itemId = invalid[i].Key;
                SetItemInstance known = invalid[i].Value;
                VisualMigrationAttemptedItemIds.Add(itemId);
                try
                {
                    EnsureCorrectVisual(itemId, known.Item, known.SaveData,
                        known.Signature);
                }
                catch (Exception ex)
                {
                    LogOnce("visual-migration-exception:" + itemId + ":" +
                        ex.GetType().FullName + ":" + ex.Message,
                        "One-shot visual migration failed for " + itemId + ": " +
                        FormatException(ex));
                }
            }

            ModLog.Info("Save-load visual fast audit retained " +
                structurallyValid + " valid set item(s) and sent " +
                invalid.Count +
                " structurally invalid legacy item(s) through the bounded resolver.");
        }

        private static bool IsKnownVisualStructurallyValidFast(
            SetItemInstance known)
        {
            if (known == null || known.Item == null || known.SaveData == null ||
                known.Signature == null || known.Signature.Definition == null)
                return false;

            string currentBaseId = Convert.ToString(
                GetProperty(known.SaveData, "OriginalItemStringId"));
            if (String.IsNullOrWhiteSpace(currentBaseId))
                return false;

            // Resolve the saved base through MBObjectManager's id index. This is a
            // constant-time lookup and, unlike checking the generated copy itself,
            // still detects the missing/wrong-slot Cowl base that v1.7.29 repaired.
            object savedBase = FindRegisteredItemByIdFast(currentBaseId);
            if (savedBase == null)
                return false;

            if (known.Signature.PieceIndex == 0)
            {
                CareerItemDefinition relic =
                    CareerUniqueRuntime.GetDefinitionForSet(
                        known.Signature.Definition.CareerId);
                return relic != null &&
                    CareerUniqueRuntime.IsBaseItemCompatible(relic, savedBase);
            }

            int armorIndex = known.Signature.PieceIndex - 1;
            if (armorIndex < 0 || armorIndex >=
                known.Signature.Definition.Pieces.Length)
                return false;
            SetPieceDefinition piece =
                known.Signature.Definition.Pieces[armorIndex];
            return IsExactSlotItem(savedBase, piece.Slot);
        }

        private static object FindRegisteredItemByIdFast(string itemId)
        {
            if (String.IsNullOrWhiteSpace(itemId))
                return null;

            if (_registeredItemLookup == null ||
                _registeredItemManager == null)
            {
                Type managerType = TypeByName(
                    "TaleWorlds.ObjectSystem.MBObjectManager");
                Type itemType = TypeByName("TaleWorlds.Core.ItemObject");
                object manager = GetStaticProperty(managerType, "Instance");
                if (managerType == null || itemType == null || manager == null)
                    return null;

                MethodInfo[] methods = managerType.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance);
                for (int i = 0; i < methods.Length; i++)
                {
                    MethodInfo method = methods[i];
                    ParameterInfo[] parameters = method.GetParameters();
                    if (method.Name != "GetObject" ||
                        !method.IsGenericMethodDefinition ||
                        parameters.Length != 1 ||
                        parameters[0].ParameterType != typeof(string))
                        continue;
                    _registeredItemLookup =
                        method.MakeGenericMethod(itemType);
                    _registeredItemManager = manager;
                    break;
                }
            }

            return _registeredItemLookup == null ? null :
                _registeredItemLookup.Invoke(_registeredItemManager,
                    new object[] { itemId });
        }
    }

    internal sealed partial class UniqueEncounterBehavior
    {
        private readonly Dictionary<string, Hero> _saveLoadAuditedHeroes =
            new Dictionary<string, Hero>(StringComparer.Ordinal);
        private bool _saveLoadAuditBatchActive;

        private static void BeginSaveLoadEncounterAuditBatch(
            UniqueEncounterBehavior __instance)
        {
            if (__instance == null)
                return;
            __instance._saveLoadAuditedHeroes.Clear();
            __instance._saveLoadAuditBatchActive = true;
        }

        private static void EndSaveLoadEncounterAuditBatch(
            UniqueEncounterBehavior __instance)
        {
            if (__instance == null)
                return;
            __instance._saveLoadAuditBatchActive = false;
            __instance._saveLoadAuditedHeroes.Clear();
        }

        private static bool ReplacePersistentHeroAuditWithFastAudit(
            UniqueEncounterBehavior __instance, string careerId, Hero hero,
            Clan expectedClan, ref string error, ref bool __result)
        {
            __result = __instance != null &&
                __instance.AuditPersistentHeroFast(careerId, hero,
                    expectedClan, out error);
            return false;
        }

        private bool AuditPersistentHeroFast(string careerId, Hero hero,
            Clan expectedClan, out string error)
        {
            error = null;
            Hero cached;
            if (_saveLoadAuditBatchActive &&
                _saveLoadAuditedHeroes.TryGetValue(careerId ?? String.Empty,
                    out cached) && Object.ReferenceEquals(cached, hero))
                return true;

            try
            {
                EncounterHeroProfile profile = GetProfileForLeader(careerId, hero);
                if (profile == null)
                    throw new InvalidOperationException(
                        "No encounter-hero profile exists.");
                if (hero == null)
                    throw new InvalidOperationException(
                        "Saved hero reference is null.");
                if (hero.IsDead)
                    throw new InvalidOperationException(
                        "The persistent hero is marked dead.");

                EnsureEncounterHeroClan(hero, expectedClan);
                hero.SetNewOccupation(Occupation.Special);
                hero.HiddenInEncyclopedia = false;
                hero.IsKnownToPlayer = true;
                hero.Level = Math.Max(hero.Level, profile.Level);
                if (hero.HitPoints <= 0)
                    hero.HitPoints = 1;
                if (!String.Equals(hero.Name == null ? null :
                    hero.Name.ToString(), profile.FullName,
                    StringComparison.Ordinal))
                {
                    hero.SetName(new TextObject(profile.FullName, null),
                        new TextObject(profile.FirstName, null));
                }
                RaiseHeroSkills(hero, profile);

                bool completeSet = SetItemRuntime.HasCompleteEncounterHeroSet(
                    hero, careerId);
                bool deepCareerRepairRequired =
                    _encounterHeroSchemaVersion <
                        CurrentEncounterHeroSchemaVersion ||
                    !completeSet ||
                    !HasExpectedTorCareerRecordFast(hero, profile);

                // The old audit rebuilt every hero's template capabilities, career
                // tiers, path choices and spell catalogue on every load. Current-schema
                // heroes with a verified career record and complete persistent set need
                // only the cheap structural audit above. Legacy or incomplete heroes
                // still execute the original full repair path unchanged.
                if (deepCareerRepairRequired)
                {
                    EncounterDefinition definition;
                    CharacterObject capabilityTemplate =
                        EncounterCatalog.ByCareer.TryGetValue(careerId,
                            out definition)
                        ? ResolveEncounterHeroTemplate(definition, profile)
                        : null;
                    EnsureTorCareerAndAbilities(hero, profile,
                        capabilityTemplate, false);
                }

                int unsafeTraitsRemoved =
                    SetItemRuntime.RemoveMissionUnsafeEncounterHeroTraits(hero);
                if (unsafeTraitsRemoved > 0)
                {
                    ModLog.Info("Removed " + unsafeTraitsRemoved +
                        " mission-unsafe post-lethal revive trait carrier(s) from " +
                        hero.Name + ".");
                }

                if (!completeSet)
                {
                    string equipmentSummary;
                    string equipmentError;
                    if (!SetItemRuntime.TryEquipEncounterHero(hero, careerId,
                        profile.PreferMounted, out equipmentSummary,
                        out equipmentError))
                    {
                        throw new InvalidOperationException(
                            "Persistent full-set repair failed: " +
                            equipmentError);
                    }
                    ModLog.Info("Repaired persistent encounter-hero equipment for " +
                        hero.Name + ": " + equipmentSummary + ".");
                }

                string equipmentAudit;
                if (!SetItemRuntime.ValidateEncounterHeroEquipment(hero,
                    careerId, profile.PreferMounted, out equipmentAudit))
                {
                    throw new InvalidOperationException(
                        "Equipment audit failed: " + equipmentAudit);
                }

                if (_saveLoadAuditBatchActive)
                    _saveLoadAuditedHeroes[careerId ?? String.Empty] = hero;
                return true;
            }
            catch (Exception ex)
            {
                error = FormatException(ex);
                return false;
            }
        }

        private static bool HasExpectedTorCareerRecordFast(Hero hero,
            EncounterHeroProfile profile)
        {
            if (hero == null || profile == null)
                return false;
            try
            {
                Type heroExtensions = ReflectionUtil.TypeByName(
                    "TOR_Core.Extensions.HeroExtensions");
                MethodInfo getInfo = FindMethod(heroExtensions,
                    "GetExtendedInfo", 1);
                object info = getInfo == null ? null : getInfo.Invoke(null,
                    new object[] { hero });
                if (info == null)
                    return false;

                string careerId = Convert.ToString(
                    GetMemberValue(info, "CareerID"));
                if (!String.Equals(careerId, profile.CareerId,
                    StringComparison.OrdinalIgnoreCase))
                    return false;

                MethodInfo hasAttribute = FindMethod(heroExtensions,
                    "HasAttribute", 2);
                if (hasAttribute == null)
                    return false;
                string[] tiers =
                    { "CareerTier1", "CareerTier2", "CareerTier3" };
                for (int i = 0; i < tiers.Length; i++)
                {
                    if (!Convert.ToBoolean(hasAttribute.Invoke(null,
                        new object[] { hero, tiers[i] })))
                        return false;
                }

                IList choices = GetMemberValue(info, "CareerChoices") as IList;
                return choices != null && choices.Count >= 2;
            }
            catch
            {
                return false;
            }
        }
    }
}
