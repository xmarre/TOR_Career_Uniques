using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace TORCareerUniques
{
    public sealed class RelicRewardIntegritySubModule : MBSubModuleBase
    {
        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            RelicRewardIntegrity.Initialize();
        }
    }

    internal static class RelicRewardIntegrity
    {
        private const string HarmonyId =
            "torcareeruniques.rewards.inventory-integrity.1.7.41";
        private static bool _installed;

        private sealed class InsertState
        {
            internal bool Verifiable;
            internal int Before;
        }

        internal static void Initialize()
        {
            if (_installed)
                return;

            try
            {
                MethodInfo target = AccessTools.Method(
                    typeof(CareerUniqueRuntime), "AddToRoster",
                    new[]
                    {
                        typeof(object), typeof(object), typeof(object),
                        typeof(int), typeof(string).MakeByRefType()
                    });
                MethodInfo prefix = AccessTools.Method(
                    typeof(RelicRewardIntegrity), nameof(BeforeAddToRoster));
                MethodInfo postfix = AccessTools.Method(
                    typeof(RelicRewardIntegrity), nameof(AfterAddToRoster));
                if (target == null || prefix == null || postfix == null)
                    throw new MissingMethodException(
                        "The TORCU inventory insertion path could not be resolved.");

                new Harmony(HarmonyId).Patch(target,
                    prefix: new HarmonyMethod(prefix) { priority = Priority.First },
                    postfix: new HarmonyMethod(postfix) { priority = Priority.Last });
                _installed = true;
                ModLog.AlwaysInfo(
                    "Installed reward inventory integrity verification. A relic grant " +
                    "cannot advance recovery state unless its exact item/modifier stack " +
                    "is present in the player inventory after insertion.");
            }
            catch (Exception ex)
            {
                ModLog.Error("Reward inventory integrity verification could not be " +
                    "installed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void BeforeAddToRoster(object roster, object item,
            object modifier, int count, out InsertState __state)
        {
            __state = new InsertState();
            if (count <= 0)
                return;

            bool supported;
            __state.Before = CountExactStack(roster, item, modifier,
                out supported);
            __state.Verifiable = supported;
        }

        private static void AfterAddToRoster(object roster, object item,
            object modifier, int count, ref string error, ref bool __result,
            InsertState __state)
        {
            if (!__result || count <= 0 || __state == null ||
                !__state.Verifiable)
                return;

            bool supported;
            int after = CountExactStack(roster, item, modifier, out supported);
            if (!supported || after >= __state.Before + count)
                return;

            string name = Convert.ToString(GetProperty(item, "Name"));
            string id = Convert.ToString(GetProperty(item, "StringId"));
            error = "Inventory insertion did not retain the granted item stack for '" +
                (String.IsNullOrWhiteSpace(name) ? id : name) + "' (item=" +
                (id ?? "<no-id>") + ", before=" + __state.Before +
                ", after=" + after + ", requested=" + count + ").";
            __result = false;

            if (__state.Before == 0 && after == 0 && count == 1)
                RollBackUnownedRuntimeDuplicate(item);

            ModLog.Error(error + " Recovery/discovery state was not advanced.");
        }

        private static int CountExactStack(object rosterObject, object itemObject,
            object modifierObject, out bool supported)
        {
            supported = false;
            ItemRoster roster = rosterObject as ItemRoster;
            ItemObject item = itemObject as ItemObject;
            ItemModifier modifier = modifierObject as ItemModifier;
            if (roster == null || item == null ||
                (modifierObject != null && modifier == null))
                return 0;

            int count = 0;
            foreach (ItemRosterElement element in roster)
            {
                EquipmentElement equipment = element.EquipmentElement;
                if (Object.ReferenceEquals(equipment.Item, item) &&
                    Object.ReferenceEquals(equipment.ItemModifier, modifier))
                    count += element.Amount;
            }
            supported = true;
            return count;
        }

        private static void RollBackUnownedRuntimeDuplicate(object itemObject)
        {
            ItemObject item = itemObject as ItemObject;
            if (item == null)
                return;

            try
            {
                object artisan = CareerUniqueRuntime.GetArtisanBehavior();
                IDictionary crafted = artisan == null ? null :
                    AccessTools.Field(artisan.GetType(), "_customCraftedItems")
                        ?.GetValue(artisan) as IDictionary;
                if (crafted == null || !crafted.Contains(item))
                    return;

                crafted.Remove(item);
                Type managerType = AccessTools.TypeByName(
                    "TOR_Core.Items.ExtendedItemObjectManager");
                string itemId = item.StringId;

                IDictionary infoMap = managerType == null ? null :
                    AccessTools.Field(managerType, "_itemToInfoMap")
                        ?.GetValue(null) as IDictionary;
                if (infoMap != null && !String.IsNullOrEmpty(itemId))
                    infoMap.Remove(itemId);

                object duplicatedIds = managerType == null ? null :
                    AccessTools.Field(managerType, "_runtimeDuplicatedItemIds")
                        ?.GetValue(null);
                MethodInfo remove = duplicatedIds == null ? null :
                    duplicatedIds.GetType().GetMethod("Remove",
                        BindingFlags.Public | BindingFlags.Instance, null,
                        new[] { typeof(string) }, null);
                if (remove != null && !String.IsNullOrEmpty(itemId))
                    remove.Invoke(duplicatedIds, new object[] { itemId });

                ModLog.Info("Rolled back failed unowned runtime item registration for " +
                    (itemId ?? "<no-id>") + ".");
            }
            catch (Exception ex)
            {
                ModLog.Error("Failed reward registration rollback was incomplete: " +
                    ex.GetType().Name + ": " + ex.Message);
            }
        }

        [CommandLineFunctionality.CommandLineArgumentFunction(
            "repair_orphaned_relic_rewards", "torcu")]
        public static string RepairOrphanedRelicRewards(List<string> arguments)
        {
            if (Campaign.Current == null || MobileParty.MainParty == null ||
                MobileParty.MainParty.ItemRoster == null)
                return "No campaign with a player inventory is currently loaded.";

            object artisan = CareerUniqueRuntime.GetArtisanBehavior();
            IDictionary crafted = artisan == null ? null :
                AccessTools.Field(artisan.GetType(), "_customCraftedItems")
                    ?.GetValue(artisan) as IDictionary;
            if (crafted == null)
                return "TOR's crafted-item save dictionary is unavailable.";

            MethodInfo findSignature = AccessTools.Method(
                typeof(SetItemRuntime), "FindPieceSignature",
                new[] { typeof(IList) });
            if (findSignature == null)
                return "TORCU's set-piece signature resolver is unavailable.";

            Dictionary<string, ItemObject> orphanByCareer =
                new Dictionary<string, ItemObject>(StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry entry in crafted)
            {
                ItemObject item = entry.Key as ItemObject;
                IList traits = GetProperty(entry.Value, "ItemTraits") as IList ??
                    GetField(entry.Value, "ItemTraits") as IList;
                if (item == null || traits == null ||
                    ContainsPrefix(traits, "torcu_admin_") ||
                    ContainsPrefix(traits, "torcu_hero_"))
                    continue;

                object signature = findSignature.Invoke(null,
                    new object[] { traits });
                if (signature == null)
                    continue;

                int pieceIndex = Convert.ToInt32(
                    GetField(signature, "PieceIndex") ??
                    GetProperty(signature, "PieceIndex"));
                if (pieceIndex != 0)
                    continue;

                object definition = GetField(signature, "Definition") ??
                    GetProperty(signature, "Definition");
                string careerId = Convert.ToString(
                    GetField(definition, "CareerId") ??
                    GetProperty(definition, "CareerId"));
                if (String.IsNullOrEmpty(careerId) ||
                    !AdminBridge.HasDiscoveredSetPiece(careerId, 0) ||
                    orphanByCareer.ContainsKey(careerId))
                    continue;

                if (!IsOwnedByPlayer(item))
                    orphanByCareer.Add(careerId, item);
            }

            if (orphanByCareer.Count == 0)
                return "No recovered-but-unowned relic reward was found to repair.";

            List<string> repaired = new List<string>();
            List<string> failed = new List<string>();
            foreach (KeyValuePair<string, ItemObject> pair in orphanByCareer)
            {
                ItemObject item = pair.Value;
                object modifier = CareerUniqueRuntime.RollLootModifier(item);
                string error;
                if (CareerUniqueRuntime.AddToRoster(
                    MobileParty.MainParty.ItemRoster, item, modifier, 1,
                    out error))
                {
                    repaired.Add(CareerUniqueRuntime.FormatModifiedItemName(
                        item.Name == null ? item.StringId : item.Name.ToString(),
                        modifier));
                }
                else
                {
                    failed.Add(pair.Key + ": " +
                        (error ?? "inventory insertion failed"));
                }
            }

            SetItemRuntime.Tick();
            string result = repaired.Count == 0 ?
                "No orphaned relic reward could be restored." :
                "Restored orphaned relic reward(s): " +
                String.Join(", ", repaired.ToArray()) + ".";
            if (failed.Count > 0)
                result += " Failed: " + String.Join("; ", failed.ToArray()) + ".";
            ModLog.AlwaysInfo(result);
            return result;
        }

        private static bool IsOwnedByPlayer(ItemObject item)
        {
            if (item == null)
                return false;
            if (RosterContains(MobileParty.MainParty == null ? null :
                MobileParty.MainParty.ItemRoster, item))
                return true;

            HashSet<Hero> heroes = new HashSet<Hero>();
            if (Hero.MainHero != null)
                heroes.Add(Hero.MainHero);
            Clan clan = Clan.PlayerClan;
            AddHeroes(heroes, clan, "Heroes");
            AddHeroes(heroes, clan, "Companions");
            AddHeroes(heroes, clan, "Lords");
            foreach (Hero hero in heroes)
            {
                if (EquipmentContains(hero == null ? null : hero.BattleEquipment,
                    item) ||
                    EquipmentContains(hero == null ? null : hero.CivilianEquipment,
                        item))
                    return true;
            }

            if (clan != null)
            {
                foreach (Settlement settlement in Settlement.All)
                {
                    if (settlement != null && settlement.OwnerClan == clan &&
                        RosterContains(settlement.ItemRoster, item))
                        return true;
                }
            }
            return false;
        }

        private static void AddHeroes(HashSet<Hero> target, object owner,
            string propertyName)
        {
            IEnumerable values = GetProperty(owner, propertyName) as IEnumerable;
            if (values == null)
                return;
            foreach (object value in values)
            {
                Hero hero = value as Hero;
                if (hero != null)
                    target.Add(hero);
            }
        }

        private static bool RosterContains(ItemRoster roster, ItemObject item)
        {
            if (roster == null || item == null)
                return false;
            foreach (ItemRosterElement element in roster)
                if (element.Amount > 0 &&
                    Object.ReferenceEquals(element.EquipmentElement.Item, item))
                    return true;
            return false;
        }

        private static bool EquipmentContains(object equipment, ItemObject item)
        {
            if (equipment == null || item == null)
                return false;
            try
            {
                MethodInfo enumerate = AccessTools.Method(
                    typeof(SetItemRuntime), "EnumerateEquipmentElements",
                    new[] { typeof(object) });
                IEnumerable elements = enumerate == null ? null :
                    enumerate.Invoke(null, new[] { equipment }) as IEnumerable;
                if (elements == null)
                    return false;
                foreach (object element in elements)
                    if (Object.ReferenceEquals(GetProperty(element, "Item"), item))
                        return true;
            }
            catch
            {
            }
            return false;
        }

        private static bool ContainsPrefix(IList values, string prefix)
        {
            if (values == null || String.IsNullOrEmpty(prefix))
                return false;
            for (int i = 0; i < values.Count; i++)
            {
                string value = Convert.ToString(values[i]);
                if (!String.IsNullOrEmpty(value) &&
                    value.StartsWith(prefix, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static object GetProperty(object instance, string name)
        {
            if (instance == null)
                return null;
            PropertyInfo property = instance.GetType().GetProperty(name,
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance | BindingFlags.Static);
            return property == null ? null : property.GetValue(instance, null);
        }

        private static object GetField(object instance, string name)
        {
            if (instance == null)
                return null;
            FieldInfo field = instance.GetType().GetField(name,
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance | BindingFlags.Static);
            return field == null ? null : field.GetValue(instance);
        }
    }
}
