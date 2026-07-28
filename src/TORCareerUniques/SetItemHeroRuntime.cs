using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using TaleWorlds.CampaignSystem;

namespace TORCareerUniques
{
    internal static partial class SetItemRuntime
    {
        private const string HeroPrefix = "torcu_hero_";
        private const string MissionUnsafeReviveTrait =
            "bret_blessing_lady_ward";

        // TOR's ReviveScript runs after Agent.Health has already reached zero and
        // only writes health back to the agent. That is usable for the main-player
        // flow TOR authored it for, but a normal AI hero can remain in Bannerlord's
        // killed visual/controller state while reporting positive health. Keep the
        // canonical trait on player loot and set pieces; only persistent encounter
        // heroes receive the mission-safe payload.
        private static bool IsMissionSafeEncounterHeroTrait(string traitId)
        {
            return !String.Equals(traitId, MissionUnsafeReviveTrait,
                StringComparison.Ordinal);
        }

        internal static bool HasCompleteEncounterHeroSet(Hero hero, string careerId)
        {
            if (hero == null || String.IsNullOrEmpty(careerId))
                return false;
            SetDefinition definition;
            if (!DefinitionByCareer.TryGetValue(careerId, out definition))
                return false;

            object equipment = hero.BattleEquipment;
            if (equipment == null)
                return false;

            HashSet<string> allTraits = new HashSet<string>(StringComparer.Ordinal);
            string[] weaponSlots = { "Weapon0", "Weapon1", "Weapon2", "Weapon3" };
            bool relicMarkerFound = false;
            for (int i = 0; i < weaponSlots.Length; i++)
            {
                IList traits = GetTraitsForEquipmentSlot(equipment, weaponSlots[i]);
                AddTraitIds(allTraits, traits);
                if (ContainsTrait(traits, GetHeroSignature(definition, 0)))
                    relicMarkerFound = true;
            }
            if (!relicMarkerFound)
                return false;

            if (definition.Pieces == null || definition.Pieces.Length != 4)
                return false;
            for (int p = 0; p < definition.Pieces.Length; p++)
            {
                string slot = GetExpectedEquipmentIndexName(definition.Pieces[p].Slot);
                IList traits = GetTraitsForEquipmentSlot(equipment, slot);
                AddTraitIds(allTraits, traits);
                if (!ContainsTrait(traits, GetHeroSignature(definition, p + 1)))
                    return false;
            }

            // Include any separately duplicated supporting weapon/shield carriers.
            string[] remainingSlots = { "Head", "Body", "Leg", "Gloves", "Cape",
                "Horse", "HorseHarness" };
            for (int i = 0; i < remainingSlots.Length; i++)
                AddTraitIds(allTraits, GetTraitsForEquipmentSlot(equipment,
                    remainingSlots[i]));

            CareerItemDefinition relic = CareerUniqueRuntime.GetDefinitionForSet(careerId);
            if (relic == null)
                return false;
            for (int i = 1; i < relic.Traits.Length; i++)
                if (IsMissionSafeEncounterHeroTrait(relic.Traits[i].Id) &&
                    !allTraits.Contains(relic.Traits[i].Id))
                    return false;

            for (int p = 0; p < definition.Pieces.Length; p++)
            {
                SetPieceDefinition piece = definition.Pieces[p];
                for (int e = 1; e < piece.Effects.Length; e++)
                    if (IsMissionSafeEncounterHeroTrait(piece.Effects[e].Id) &&
                        !allTraits.Contains(piece.Effects[e].Id))
                        return false;
                for (int e = 0; e < piece.Effects.Length; e++)
                    if (GetBonusTargetKind(piece.Effects[e]) != BonusTargetKind.Armor &&
                        !allTraits.Contains(GetRoutedPieceTraitId(piece.Effects[e])))
                        return false;
            }

            for (int t = 0; t < definition.Tiers.Length; t++)
                for (int e = 0; e < definition.Tiers[t].Effects.Length; e++)
                    if (IsMissionSafeEncounterHeroTrait(
                        definition.Tiers[t].Effects[e].Id) &&
                        !allTraits.Contains(definition.Tiers[t].Effects[e].Id))
                        return false;
            return true;
        }

        internal static int RemoveMissionUnsafeEncounterHeroTraits(Hero hero)
        {
            if (hero == null || hero.BattleEquipment == null)
                return 0;

            int repaired = 0;
            string[] slots = { "Weapon0", "Weapon1", "Weapon2", "Weapon3",
                "Head", "Body", "Leg", "Gloves", "Cape", "Horse",
                "HorseHarness" };
            for (int i = 0; i < slots.Length; i++)
            {
                object item = GetEquipmentItem(hero.BattleEquipment, slots[i]);
                if (item == null)
                    continue;
                IList traits = GetItemTraits(GetItemId(item));
                if (!ContainsTrait(traits, MissionUnsafeReviveTrait))
                    continue;
                RemoveFixedTrait(item, MissionUnsafeReviveTrait);
                repaired++;
            }
            return repaired;
        }

        private static IList GetTraitsForEquipmentSlot(object equipment,
            string slotName)
        {
            object item = GetEquipmentItem(equipment, slotName);
            return item == null ? null : GetItemTraits(GetItemId(item));
        }

        private static void AddTraitIds(HashSet<string> target, IList traits)
        {
            if (target == null || traits == null)
                return;
            for (int i = 0; i < traits.Count; i++)
            {
                string id = Convert.ToString(traits[i]);
                if (!String.IsNullOrEmpty(id))
                    target.Add(id);
            }
        }

        private static bool ContainsTrait(IList traits, string expected)
        {
            if (traits == null || String.IsNullOrEmpty(expected))
                return false;
            for (int i = 0; i < traits.Count; i++)
                if (String.Equals(Convert.ToString(traits[i]), expected,
                    StringComparison.Ordinal))
                    return true;
            return false;
        }

        internal static bool TryEquipEncounterHero(Hero hero, string careerId,
            bool preferMounted, out string summary, out string error)
        {
            summary = null;
            error = null;
            if (hero == null)
            {
                error = "Encounter hero is null.";
                return false;
            }

            SetDefinition definition;
            if (!DefinitionByCareer.TryGetValue(careerId ?? String.Empty, out definition))
            {
                error = "Unknown career id '" + careerId + "'.";
                return false;
            }

            if (HasCompleteEncounterHeroSet(hero, careerId))
            {
                summary = "existing persistent full set retained";
                return true;
            }

            if (!EnsureReady(out error))
                return false;

            object equipment = hero.BattleEquipment;
            if (equipment == null)
            {
                error = "The encounter hero has no battle equipment object.";
                return false;
            }

            try
            {
                Dictionary<string, object> createdBySlot =
                    new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                Dictionary<string, bool> createdItemIds =
                    new Dictionary<string, bool>(StringComparer.Ordinal);

                CareerItemDefinition relic = CareerUniqueRuntime.GetDefinitionForSet(careerId);
                if (relic == null)
                    throw new InvalidOperationException("Career relic definition is unavailable for " + careerId + ".");
                object relicBase = CareerUniqueRuntime.FindBaseItem(relic);
                if (relicBase == null)
                    throw new InvalidOperationException("No lore-compatible relic base item was resolved for " + careerId + ".");

                List<string> relicTraits = new List<string>();
                relicTraits.Add(GetHeroSignature(definition, 0));
                for (int i = 1; i < relic.Traits.Length; i++)
                    if (IsMissionSafeEncounterHeroTrait(relic.Traits[i].Id))
                        relicTraits.Add(relic.Traits[i].Id);

                object relicItem = CreateRecordedHeroItem(relicBase, relic.ItemName,
                    relicTraits, null);
                string relicSlot = FindBestWeaponSlot(equipment, relicItem, null);
                if (String.IsNullOrEmpty(relicSlot))
                    throw new InvalidOperationException("No compatible weapon slot was available for " + relic.ItemName + ".");
                SetEquipmentItem(equipment, relicSlot, relicItem);
                createdBySlot[relicSlot] = relicItem;
                createdItemIds[GetItemId(relicItem)] = true;

                for (int p = 0; p < definition.Pieces.Length; p++)
                {
                    SetPieceDefinition piece = definition.Pieces[p];
                    object baseItem = FindArmorBaseItem(definition, piece);
                    if (baseItem == null)
                        throw new InvalidOperationException("No exact-slot visual base item was resolved for " + piece.ItemName + ".");

                    List<string> traits = new List<string>();
                    traits.Add(GetHeroSignature(definition, p + 1));
                    for (int e = 1; e < piece.Effects.Length; e++)
                        if (IsMissionSafeEncounterHeroTrait(piece.Effects[e].Id))
                            traits.Add(piece.Effects[e].Id);

                    object created = CreateRecordedHeroItem(baseItem, piece.ItemName,
                        traits, piece.Slot);
                    string slot = GetExpectedEquipmentIndexName(piece.Slot);
                    SetEquipmentItem(equipment, slot, created);
                    createdBySlot[slot] = created;
                    createdItemIds[GetItemId(created)] = true;
                }

                Dictionary<string, List<string>> routedBySlot =
                    BuildEncounterHeroRoutedTraits(definition, equipment, relicSlot);

                foreach (KeyValuePair<string, List<string>> entry in routedBySlot)
                {
                    object currentItem = GetEquipmentItem(equipment, entry.Key);
                    if (currentItem == null)
                        throw new InvalidOperationException("Set-bonus carrier slot " + entry.Key + " became empty.");

                    string currentId = GetItemId(currentItem);
                    if (createdItemIds.ContainsKey(currentId))
                    {
                        AppendFixedTraits(currentItem, entry.Value);
                        VerifyFixedTraits(currentId, entry.Value);
                    }
                    else
                    {
                        string currentName = Convert.ToString(GetProperty(currentItem, "Name"));
                        object carrier = CreateRecordedHeroItem(currentItem,
                            String.IsNullOrEmpty(currentName) ? "Heroic wargear" : currentName,
                            entry.Value, null);
                        SetEquipmentItem(equipment, entry.Key, carrier);
                        createdBySlot[entry.Key] = carrier;
                        createdItemIds[GetItemId(carrier)] = true;
                        VerifyFixedTraits(GetItemId(carrier), entry.Value);
                    }
                }

                HashSet<string> protectedSlots = new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);
                foreach (string slot in createdBySlot.Keys)
                    protectedSlots.Add(slot);
                foreach (string slot in routedBySlot.Keys)
                    protectedSlots.Add(slot);

                string supplementalSummary = UpgradeEncounterHeroSupplementalEquipment(
                    definition, equipment, protectedSlots, preferMounted);

                if (!HasCompleteEncounterHeroSet(hero, careerId))
                    throw new InvalidOperationException("Post-equip audit did not find the complete five-piece hero set and its persistent intrinsic/tier bonus payload.");

                if (RemoveMissionUnsafeEncounterHeroTraits(hero) != 0)
                    throw new InvalidOperationException("Mission-unsafe revive trait remained on newly generated encounter-hero equipment.");

                summary = "full five-piece " + definition.SetName +
                    " equipped; " + CountRoutedTraits(routedBySlot) +
                    " intrinsic/set-bonus traits permanently routed across " +
                    routedBySlot.Count + " compatible equipped carriers; " +
                    supplementalSummary;
                ModLog.Info("Equipped encounter hero " + hero.Name + " with " + summary + ".");
                return true;
            }
            catch (Exception ex)
            {
                error = FormatException(ex);
                ModLog.Error("Encounter-hero equipment setup failed for " + careerId +
                    " / " + hero.Name + ": " + error);
                return false;
            }
        }

        internal static bool ValidateEncounterHeroEquipment(Hero hero,
            string careerId, bool expectMounted, out string summary)
        {
            summary = null;
            if (hero == null)
            {
                summary = "hero reference is null";
                return false;
            }
            SetDefinition definition;
            if (!DefinitionByCareer.TryGetValue(careerId ?? String.Empty,
                out definition))
            {
                summary = "unknown career id '" + careerId + "'";
                return false;
            }
            object equipment = hero.BattleEquipment;
            if (equipment == null)
            {
                summary = "battle equipment is null";
                return false;
            }
            if (!HasCompleteEncounterHeroSet(hero, careerId))
            {
                summary = "five-piece signatures or persistent intrinsic/tier traits are incomplete";
                return false;
            }
            if (ContainsMissionUnsafeEncounterHeroTrait(equipment))
            {
                summary = "mission-unsafe post-lethal revive trait is still equipped";
                return false;
            }

            string[] slots = { "Weapon0", "Weapon1", "Weapon2", "Weapon3",
                "Head", "Body", "Leg", "Gloves", "Cape", "Horse",
                "HorseHarness" };
            int occupied = 0;
            int weapons = 0;
            for (int i = 0; i < slots.Length; i++)
            {
                object item = GetEquipmentItem(equipment, slots[i]);
                if (item == null)
                    continue;
                occupied++;
                if (i < 4)
                    weapons++;
                if (!ItemFitsSlot(item, slots[i]))
                {
                    summary = "item " + GetItemId(item) + " does not fit " + slots[i];
                    return false;
                }
            }

            bool relicFound = false;
            for (int i = 0; i < 4; i++)
                if (ContainsTrait(GetTraitsForEquipmentSlot(equipment,
                    "Weapon" + i), GetHeroSignature(definition, 0)))
                    relicFound = true;
            if (!relicFound)
            {
                summary = "career relic signature is not present in a weapon slot";
                return false;
            }

            for (int p = 0; p < definition.Pieces.Length; p++)
            {
                SetPieceDefinition piece = definition.Pieces[p];
                string slot = GetExpectedEquipmentIndexName(piece.Slot);
                object item = GetEquipmentItem(equipment, slot);
                if (item == null || !IsExactSlotItem(item, piece.Slot))
                {
                    summary = piece.ItemName + " is missing from exact slot " + slot;
                    return false;
                }
                if (!ContainsTrait(GetTraitsForEquipmentSlot(equipment, slot),
                    GetHeroSignature(definition, p + 1)))
                {
                    summary = piece.ItemName + " is missing its persistent hero signature";
                    return false;
                }
            }

            object horse = GetEquipmentItem(equipment, "Horse");
            object harness = GetEquipmentItem(equipment, "HorseHarness");
            if (expectMounted && (horse == null || harness == null))
            {
                summary = "mounted profile lacks " +
                    (horse == null ? "a mount" : "a horse harness");
                return false;
            }

            summary = occupied + " valid occupied slots, " + weapons +
                " weapon slots, complete persistent set payload" +
                (expectMounted ? ", mount and harness verified" : String.Empty);
            return true;
        }

        private static string UpgradeEncounterHeroSupplementalEquipment(
            SetDefinition definition, object equipment,
            HashSet<string> protectedSlots, bool preferMounted)
        {
            List<object> items = GetHeroEquipmentCatalog();
            if (items.Count == 0)
                throw new InvalidOperationException("The item catalog is empty while upgrading encounter-hero wargear.");

            int upgraded = 0;
            int filled = 0;
            string[] slots = { "Weapon0", "Weapon1", "Weapon2", "Weapon3",
                "Head", "Body", "Leg", "Gloves", "Cape", "Horse",
                "HorseHarness" };
            for (int i = 0; i < slots.Length; i++)
            {
                string slot = slots[i];
                if (protectedSlots.Contains(slot))
                    continue;

                object current = GetEquipmentItem(equipment, slot);
                string requiredType = current == null ?
                    GetFillableSupplementalType(slot, preferMounted) :
                    GetItemTypeName(current);
                if (String.IsNullOrEmpty(requiredType))
                    continue;

                object best = FindBestSupplementalItem(definition, items, slot,
                    requiredType, current);
                if (best == null || Object.ReferenceEquals(best, current) ||
                    String.Equals(GetItemId(best), GetItemId(current),
                        StringComparison.Ordinal))
                    continue;

                SetEquipmentItem(equipment, slot, best);
                if (current == null)
                    filled++;
                else
                    upgraded++;
                ModLog.Info("Encounter hero " + definition.CareerId + " " +
                    (current == null ? "filled" : "upgraded") + " " + slot +
                    " with lore-filtered high-tier item " + GetItemId(best) + ".");
            }

            if (preferMounted)
            {
                if (GetEquipmentItem(equipment, "Horse") == null)
                    throw new InvalidOperationException("No lore-compatible mount could be assigned to mounted profile " +
                        definition.CareerId + ".");
                if (GetEquipmentItem(equipment, "HorseHarness") == null)
                    throw new InvalidOperationException("No lore-compatible horse harness could be assigned to mounted profile " +
                        definition.CareerId + ".");
            }
            return upgraded + " supplemental slots upgraded and " + filled +
                " empty lore-valid slots filled" +
                (preferMounted ? "; mounted loadout completed" : String.Empty);
        }

        private static string GetFillableSupplementalType(string slot,
            bool preferMounted)
        {
            switch (slot)
            {
                case "Head": return "HeadArmor";
                case "Body": return "BodyArmor";
                case "Leg": return "LegArmor";
                case "Gloves": return "HandArmor";
                case "Cape": return "Cape";
                case "Horse": return preferMounted ? "Horse" : null;
                case "HorseHarness": return preferMounted ? "HorseHarness" : null;
                default: return null; // Never invent weapons/ammunition for an empty slot.
            }
        }

        private static List<object> GetHeroEquipmentCatalog()
        {
            List<object> result = new List<object>();
            Type managerType = TypeByName("TaleWorlds.ObjectSystem.MBObjectManager");
            Type itemType = TypeByName("TaleWorlds.Core.ItemObject");
            object manager = GetStaticProperty(managerType, "Instance");
            if (manager == null || managerType == null || itemType == null)
                return result;
            MethodInfo generic = null;
            MethodInfo[] methods = managerType.GetMethods(
                BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < methods.Length; i++)
                if (methods[i].Name == "GetObjectTypeList" &&
                    methods[i].IsGenericMethodDefinition &&
                    methods[i].GetParameters().Length == 0)
                {
                    generic = methods[i];
                    break;
                }
            IEnumerable values = generic == null ? null :
                generic.MakeGenericMethod(itemType).Invoke(manager, null) as IEnumerable;
            if (values != null)
                foreach (object item in values)
                    if (item != null)
                        result.Add(item);
            return result;
        }

        private static object FindBestSupplementalItem(SetDefinition definition,
            List<object> items, string slot, string requiredType, object current)
        {
            VisualProfile profile;
            VisualProfileByCareer.TryGetValue(definition.CareerId, out profile);
            Type duplicateManager = TypeByName(
                "TOR_Core.Items.ExtendedItemObjectManager");
            MethodInfo isDuplicate = FindStaticMethod(duplicateManager,
                "IsRuntimeDuplicatedItem", 1);

            object best = current;
            int bestScore = current == null ? Int32.MinValue :
                ScoreSupplementalItem(definition, profile, current, true);
            for (int i = 0; i < items.Count; i++)
            {
                object candidate = items[i];
                string id = GetItemId(candidate);
                if (String.IsNullOrEmpty(id) ||
                    id.StartsWith(HeroPrefix, StringComparison.OrdinalIgnoreCase) ||
                    !String.Equals(GetItemTypeName(candidate), requiredType,
                        StringComparison.OrdinalIgnoreCase) ||
                    !ItemFitsSlot(candidate, slot))
                    continue;
                if (isDuplicate != null && ToBoolean(isDuplicate.Invoke(null,
                    new object[] { candidate })))
                    continue;

                int score = ScoreSupplementalItem(definition, profile,
                    candidate, false);
                if (score == Int32.MinValue || score <= bestScore)
                    continue;
                best = candidate;
                bestScore = score;
            }
            return best;
        }

        private static int ScoreSupplementalItem(SetDefinition definition,
            VisualProfile profile, object item, bool allowWeakLore)
        {
            if (item == null)
                return Int32.MinValue;
            string text = NormalizeSearch(GetItemId(item) + " " +
                Convert.ToString(GetProperty(item, "Name")) + " " +
                Convert.ToString(GetProperty(GetProperty(item, "Culture"), "StringId")) + " " +
                Convert.ToString(GetProperty(GetProperty(item, "Culture"), "Name")));
            int lore = TokenMatchScore(text, definition.FactionTokens, 1600);
            if (profile != null)
            {
                lore += CountPhraseMatches(text, profile.CulturePhrases) * 9000;
                lore += CountPhraseMatches(text, profile.PrimaryPhrases) * 3200;
                lore += CountPhraseMatches(text, profile.SecondaryPhrases) * 900;
                lore -= CountPhraseMatches(text, profile.NegativePhrases) * 7000;
            }
            if (!allowWeakLore && lore <= 0)
                return Int32.MinValue;

            int tier = EnumNumber(GetProperty(item, "Tier"));
            int value;
            try { value = Convert.ToInt32(GetProperty(item, "Value")); }
            catch { value = 0; }
            int score = lore + Math.Max(0, tier) * 3000 +
                Math.Min(Math.Max(value, 0), 250000) / 40;
            if (ToBoolean(GetProperty(item, "NotMerchandise")))
                score += 250; // Elite NPC-only wargear is valid for these heroes.
            return score;
        }

        private static Dictionary<string, List<string>> BuildEncounterHeroRoutedTraits(
            SetDefinition definition, object equipment, string relicSlot)
        {
            Dictionary<string, List<string>> result =
                new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            for (int p = 0; p < definition.Pieces.Length; p++)
            {
                SetPieceDefinition piece = definition.Pieces[p];
                for (int e = 0; e < piece.Effects.Length; e++)
                {
                    TraitDefinition effect = piece.Effects[e];
                    BonusTargetKind targetKind = GetBonusTargetKind(effect);
                    if (targetKind == BonusTargetKind.Armor)
                        continue;
                    string targetSlot = EnsureHeroBonusTarget(definition, equipment,
                        targetKind, relicSlot);
                    AddTraitBySlot(result, targetSlot, GetRoutedPieceTraitId(effect));
                }
            }

            for (int t = 0; t < definition.Tiers.Length; t++)
            {
                SetTierDefinition tier = definition.Tiers[t];
                for (int e = 0; e < tier.Effects.Length; e++)
                {
                    TraitDefinition effect = tier.Effects[e];
                    if (!IsMissionSafeEncounterHeroTrait(effect.Id))
                        continue;
                    string targetSlot = EnsureHeroBonusTarget(definition, equipment,
                        GetBonusTargetKind(effect), relicSlot);
                    AddTraitBySlot(result, targetSlot, effect.Id);
                }
            }
            return result;
        }

        private static bool ContainsMissionUnsafeEncounterHeroTrait(object equipment)
        {
            string[] slots = { "Weapon0", "Weapon1", "Weapon2", "Weapon3",
                "Head", "Body", "Leg", "Gloves", "Cape", "Horse",
                "HorseHarness" };
            for (int i = 0; i < slots.Length; i++)
            {
                object item = GetEquipmentItem(equipment, slots[i]);
                if (item != null && ContainsTrait(GetItemTraits(GetItemId(item)),
                    MissionUnsafeReviveTrait))
                    return true;
            }
            return false;
        }

        private static string EnsureHeroBonusTarget(SetDefinition definition,
            object equipment, BonusTargetKind kind, string relicSlot)
        {
            List<HeroEquipmentSlot> slots = ReadEquipmentSlots(equipment);
            HeroEquipmentSlot target = SelectHeroBonusTarget(slots, kind, relicSlot);
            if (target != null)
                return target.SlotName;

            object carrierBase = FindHeroCarrierBaseItem(definition, kind);
            if (carrierBase == null)
                throw new InvalidOperationException("No lore-compatible " +
                    DescribeBonusTarget(kind) + " item exists to carry a full-set effect for " +
                    definition.CareerId + ".");

            string slot = FindBestWeaponSlot(equipment, carrierBase, relicSlot);
            if (String.IsNullOrEmpty(slot))
                throw new InvalidOperationException("No free/replaceable weapon slot exists for the required " +
                    DescribeBonusTarget(kind) + " carrier on " + definition.CareerId + ".");
            SetEquipmentItem(equipment, slot, carrierBase);
            ModLog.Info("Added lore-filtered " + DescribeBonusTarget(kind) +
                " carrier " + GetItemId(carrierBase) + " to " + definition.CareerId +
                " encounter hero slot " + slot + ".");
            return slot;
        }

        private static HeroEquipmentSlot SelectHeroBonusTarget(
            List<HeroEquipmentSlot> slots, BonusTargetKind kind, string relicSlot)
        {
            if (kind == BonusTargetKind.Armor)
            {
                for (int i = 0; i < slots.Count; i++)
                    if (String.Equals(slots[i].SlotName, "Body", StringComparison.OrdinalIgnoreCase) &&
                        slots[i].Item != null)
                        return slots[i];
            }

            if (!String.IsNullOrEmpty(relicSlot))
            {
                for (int i = 0; i < slots.Count; i++)
                    if (String.Equals(slots[i].SlotName, relicSlot, StringComparison.OrdinalIgnoreCase) &&
                        IsCompatibleBonusTarget(ToEquippedRef(slots[i]), kind))
                        return slots[i];
            }

            for (int i = 0; i < slots.Count; i++)
                if (IsCompatibleBonusTarget(ToEquippedRef(slots[i]), kind))
                    return slots[i];
            return null;
        }

        private static EquippedItemRef ToEquippedRef(HeroEquipmentSlot slot)
        {
            if (slot == null || slot.Item == null)
                return null;
            return new EquippedItemRef
            {
                Item = slot.Item,
                ItemId = GetItemId(slot.Item),
                ItemTypeName = GetItemTypeName(slot.Item)
            };
        }

        private static object FindHeroCarrierBaseItem(SetDefinition definition,
            BonusTargetKind kind)
        {
            Type managerType = TypeByName("TaleWorlds.ObjectSystem.MBObjectManager");
            Type itemType = TypeByName("TaleWorlds.Core.ItemObject");
            object manager = GetStaticProperty(managerType, "Instance");
            if (manager == null || itemType == null)
                return null;

            MethodInfo generic = null;
            foreach (MethodInfo method in managerType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                if (method.Name == "GetObjectTypeList" && method.IsGenericMethodDefinition && method.GetParameters().Length == 0)
                    generic = method;
            IEnumerable items = generic == null ? null :
                generic.MakeGenericMethod(itemType).Invoke(manager, null) as IEnumerable;
            if (items == null)
                return null;

            VisualProfile profile;
            VisualProfileByCareer.TryGetValue(definition.CareerId, out profile);
            Type extendedManagerType = TypeByName("TOR_Core.Items.ExtendedItemObjectManager");
            MethodInfo isDuplicate = FindStaticMethod(extendedManagerType,
                "IsRuntimeDuplicatedItem", 1);

            object best = null;
            int bestScore = Int32.MinValue;
            foreach (object item in items)
            {
                if (item == null || ToBoolean(GetProperty(item, "NotMerchandise")))
                    continue;
                if (isDuplicate != null && ToBoolean(isDuplicate.Invoke(null, new object[] { item })))
                    continue;

                EquippedItemRef candidate = new EquippedItemRef
                {
                    Item = item,
                    ItemId = GetItemId(item),
                    ItemTypeName = GetItemTypeName(item)
                };
                if (!IsCompatibleBonusTarget(candidate, kind))
                    continue;

                string text = NormalizeSearch(candidate.ItemId + " " +
                    Convert.ToString(GetProperty(item, "Name")) + " " +
                    Convert.ToString(GetProperty(GetProperty(item, "Culture"), "StringId")) + " " +
                    Convert.ToString(GetProperty(GetProperty(item, "Culture"), "Name")));
                int score = 0;
                if (profile != null)
                {
                    score += CountPhraseMatches(text, profile.CulturePhrases) * 1200;
                    score += CountPhraseMatches(text, profile.PrimaryPhrases) * 450;
                    score += CountPhraseMatches(text, profile.SecondaryPhrases) * 150;
                    score -= CountPhraseMatches(text, profile.NegativePhrases) * 500;
                }
                score += TokenMatchScore(text, definition.FactionTokens, 220);
                int value = 0;
                try { value = Convert.ToInt32(GetProperty(item, "Value")); }
                catch { value = 0; }
                score += Math.Min(Math.Max(value, 0), 200000) / 25;
                if (score > bestScore)
                {
                    best = item;
                    bestScore = score;
                }
            }
            return best;
        }

        private static int TokenMatchScore(string text, string[] tokens, int points)
        {
            if (String.IsNullOrEmpty(text) || tokens == null)
                return 0;
            int result = 0;
            for (int i = 0; i < tokens.Length; i++)
            {
                string token = NormalizeSearch(tokens[i]).Trim();
                if (token.Length > 0 && text.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                    result += points;
            }
            return result;
        }

        private static object CreateRecordedHeroItem(object baseItem, string itemName,
            List<string> traitIds, SetSlot? expectedSlot)
        {
            Type helperType = TypeByName("TOR_Core.CampaignMechanics.Crafting.EnchantmentHelper");
            MethodInfo create = FindStaticMethod(helperType, "CreateEnchantedItem", 5);
            if (create == null)
                throw new MissingMethodException("TOR_Core.CampaignMechanics.Crafting.EnchantmentHelper",
                    "CreateEnchantedItem");
            IList reflected = new List<string>(traitIds);
            object newItem = create.Invoke(null, new object[]
                { baseItem, reflected, itemName, false, null });
            if (newItem == null)
                throw new InvalidOperationException("ToR returned null while creating hero item " + itemName + ".");
            if (expectedSlot.HasValue && !IsExactSlotItem(newItem, expectedSlot.Value))
                throw new InvalidOperationException("ToR created hero item '" + itemName +
                    "' in wrong slot " + Convert.ToString(GetItemTypeValue(newItem)) + ".");
            EnsureCraftedItemRecorded(baseItem, newItem, itemName, reflected);
            return newItem;
        }

        private static void AppendFixedTraits(object item, List<string> additions)
        {
            string itemId = GetItemId(item);
            if (item == null || String.IsNullOrEmpty(itemId) ||
                additions == null || additions.Count == 0)
                return;

            Type managerType = TypeByName("TOR_Core.Items.ExtendedItemObjectManager");
            FieldInfo mapField = managerType == null ? null : managerType.GetField(
                "_itemToInfoMap", BindingFlags.NonPublic | BindingFlags.Static);
            IDictionary map = mapField == null ? null : mapField.GetValue(null) as IDictionary;
            MethodInfo getReadOnly = FindStaticMethod(managerType,
                "GetAdditionalPropertiesReadOnly", 1);
            object properties = getReadOnly == null ? null :
                getReadOnly.Invoke(null, new object[] { itemId });
            if (map == null || properties == null)
                throw new InvalidOperationException("Runtime item properties are unavailable for " + itemId + ".");

            MethodInfo cloneMethod = properties.GetType().GetMethod("Clone",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            object clone = cloneMethod == null ? null : cloneMethod.Invoke(properties, null);
            if (clone == null)
                throw new InvalidOperationException("Could not clone runtime item properties for " + itemId + ".");

            List<string> combined = new List<string>();
            IList current = GetField(properties, "ItemTraits") as IList;
            if (current != null)
                for (int i = 0; i < current.Count; i++)
                {
                    string id = Convert.ToString(current[i]);
                    if (!String.IsNullOrEmpty(id) && !combined.Contains(id))
                        combined.Add(id);
                }
            for (int i = 0; i < additions.Count; i++)
                if (!String.IsNullOrEmpty(additions[i]) && !combined.Contains(additions[i]))
                    combined.Add(additions[i]);

            // Update both runtime resolution and ToR's authoritative crafted-item save
            // record. Updating only _itemToInfoMap would work until the next load, then
            // silently discard every routed/full-set bonus appended after item creation.
            SetField(clone, "ItemTraits", combined);
            map[itemId] = clone;

            object artisan = CareerUniqueRuntime.GetArtisanBehavior();
            IDictionary savedItems = GetField(artisan, "_customCraftedItems") as IDictionary;
            if (savedItems == null)
                throw new InvalidOperationException("ToR's crafted-item save dictionary is unavailable for " + itemId + ".");

            object saveData = savedItems.Contains(item) ? savedItems[item] : null;
            if (saveData == null)
            {
                foreach (DictionaryEntry entry in savedItems)
                {
                    if (String.Equals(GetItemId(entry.Key), itemId,
                        StringComparison.Ordinal))
                    {
                        saveData = entry.Value;
                        break;
                    }
                }
            }
            if (saveData == null)
                throw new InvalidOperationException("No crafted-item save record exists for " + itemId + ".");

            SetProperty(saveData, "ItemTraits", new List<string>(combined));
            IList persisted = GetProperty(saveData, "ItemTraits") as IList;
            for (int i = 0; i < additions.Count; i++)
            {
                bool found = false;
                if (persisted != null)
                    for (int j = 0; j < persisted.Count; j++)
                        if (String.Equals(Convert.ToString(persisted[j]), additions[i],
                            StringComparison.Ordinal))
                        {
                            found = true;
                            break;
                        }
                if (!found)
                    throw new InvalidOperationException("Crafted-item save record for " +
                        itemId + " did not retain appended trait " + additions[i] + ".");
            }
        }

        private static void RemoveFixedTrait(object item, string removal)
        {
            string itemId = GetItemId(item);
            if (item == null || String.IsNullOrEmpty(itemId) ||
                String.IsNullOrEmpty(removal))
                return;

            Type managerType = TypeByName("TOR_Core.Items.ExtendedItemObjectManager");
            FieldInfo mapField = managerType == null ? null : managerType.GetField(
                "_itemToInfoMap", BindingFlags.NonPublic | BindingFlags.Static);
            IDictionary map = mapField == null ? null : mapField.GetValue(null) as IDictionary;
            MethodInfo getReadOnly = FindStaticMethod(managerType,
                "GetAdditionalPropertiesReadOnly", 1);
            object properties = getReadOnly == null ? null :
                getReadOnly.Invoke(null, new object[] { itemId });
            if (map == null || properties == null)
                throw new InvalidOperationException("Runtime item properties are unavailable for " + itemId + ".");

            MethodInfo cloneMethod = properties.GetType().GetMethod("Clone",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            object clone = cloneMethod == null ? null : cloneMethod.Invoke(properties, null);
            if (clone == null)
                throw new InvalidOperationException("Could not clone runtime item properties for " + itemId + ".");

            List<string> retained = new List<string>();
            IList current = GetField(properties, "ItemTraits") as IList;
            if (current != null)
                for (int i = 0; i < current.Count; i++)
                {
                    string id = Convert.ToString(current[i]);
                    if (!String.IsNullOrEmpty(id) &&
                        !String.Equals(id, removal, StringComparison.Ordinal) &&
                        !retained.Contains(id))
                        retained.Add(id);
                }
            SetField(clone, "ItemTraits", retained);
            map[itemId] = clone;

            object artisan = CareerUniqueRuntime.GetArtisanBehavior();
            IDictionary savedItems = GetField(artisan, "_customCraftedItems") as IDictionary;
            if (savedItems == null)
                throw new InvalidOperationException("ToR's crafted-item save dictionary is unavailable for " + itemId + ".");

            object saveData = savedItems.Contains(item) ? savedItems[item] : null;
            if (saveData == null)
                foreach (DictionaryEntry entry in savedItems)
                    if (String.Equals(GetItemId(entry.Key), itemId,
                        StringComparison.Ordinal))
                    {
                        saveData = entry.Value;
                        break;
                    }
            if (saveData == null)
                throw new InvalidOperationException("No crafted-item save record exists for " + itemId + ".");

            SetProperty(saveData, "ItemTraits", new List<string>(retained));
            IList persisted = GetProperty(saveData, "ItemTraits") as IList;
            if (ContainsTrait(persisted, removal) ||
                ContainsTrait(GetItemTraits(itemId), removal))
                throw new InvalidOperationException("Encounter-hero item " + itemId +
                    " retained mission-unsafe trait " + removal + ".");
        }

        private static void VerifyFixedTraits(string itemId, List<string> expected)
        {
            IList actual = GetItemTraits(itemId);
            for (int i = 0; i < expected.Count; i++)
            {
                bool found = false;
                if (actual != null)
                    for (int j = 0; j < actual.Count; j++)
                        if (String.Equals(Convert.ToString(actual[j]), expected[i], StringComparison.Ordinal))
                        {
                            found = true;
                            break;
                        }
                if (!found)
                    throw new InvalidOperationException("Hero item " + itemId +
                        " is missing routed trait " + expected[i] + " after assignment.");
            }
        }

        private static void AddTraitBySlot(Dictionary<string, List<string>> target,
            string slot, string traitId)
        {
            if (String.IsNullOrEmpty(slot) || String.IsNullOrEmpty(traitId))
                throw new InvalidOperationException("A hero set effect could not be assigned to a valid equipment slot.");
            List<string> list;
            if (!target.TryGetValue(slot, out list))
            {
                list = new List<string>();
                target.Add(slot, list);
            }
            if (!list.Contains(traitId))
                list.Add(traitId);
        }

        private static int CountRoutedTraits(Dictionary<string, List<string>> traits)
        {
            int count = 0;
            foreach (List<string> list in traits.Values)
                count += list.Count;
            return count;
        }

        private static string GetHeroSignature(SetDefinition definition, int pieceIndex)
        {
            return HeroPrefix + definition.CareerId.ToLowerInvariant() +
                "_p" + pieceIndex + "_sig";
        }

        private static string GetItemId(object item)
        {
            return Convert.ToString(GetProperty(item, "StringId")) ?? String.Empty;
        }

        private static string FindBestWeaponSlot(object equipment, object item,
            string protectedSlot)
        {
            string[] names = { "Weapon0", "Weapon1", "Weapon2", "Weapon3" };
            for (int i = 0; i < names.Length; i++)
                if (!String.Equals(names[i], protectedSlot, StringComparison.OrdinalIgnoreCase) &&
                    GetEquipmentItem(equipment, names[i]) == null && ItemFitsSlot(item, names[i]))
                    return names[i];
            for (int i = names.Length - 1; i >= 0; i--)
                if (!String.Equals(names[i], protectedSlot, StringComparison.OrdinalIgnoreCase) &&
                    ItemFitsSlot(item, names[i]))
                    return names[i];
            return null;
        }

        private static bool ItemFitsSlot(object item, string slotName)
        {
            Type equipmentType = TypeByName("TaleWorlds.Core.Equipment");
            Type indexType = TypeByName("TaleWorlds.Core.EquipmentIndex");
            MethodInfo fits = FindStaticMethod(equipmentType, "IsItemFitsToSlot", 2);
            if (item == null || indexType == null)
                return false;
            object index = Enum.Parse(indexType, slotName, true);
            return fits == null || ToBoolean(fits.Invoke(null, new object[] { index, item }));
        }

        private static object GetEquipmentItem(object equipment, string slotName)
        {
            if (equipment == null)
                return null;
            Type indexType = TypeByName("TaleWorlds.Core.EquipmentIndex");
            if (indexType == null)
                return null;
            object index = Enum.Parse(indexType, slotName, true);
            MethodInfo getter = equipment.GetType().GetMethod("get_Item",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, new[] { indexType }, null);
            object element = getter == null ? null : getter.Invoke(equipment, new[] { index });
            return GetProperty(element, "Item");
        }

        private static void SetEquipmentItem(object equipment, string slotName,
            object item)
        {
            Type indexType = TypeByName("TaleWorlds.Core.EquipmentIndex");
            Type itemType = TypeByName("TaleWorlds.Core.ItemObject");
            Type elementType = TypeByName("TaleWorlds.Core.EquipmentElement");
            if (equipment == null || indexType == null || itemType == null || elementType == null)
                throw new InvalidOperationException("Core equipment reflection types are unavailable.");
            object index = Enum.Parse(indexType, slotName, true);
            object element = null;
            ConstructorInfo[] constructors = elementType.GetConstructors(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            for (int i = 0; i < constructors.Length; i++)
            {
                ParameterInfo[] parameters = constructors[i].GetParameters();
                if (parameters.Length == 4 && parameters[0].ParameterType == itemType)
                {
                    element = constructors[i].Invoke(new object[] { item, null, null, false });
                    break;
                }
                if (parameters.Length == 1 && parameters[0].ParameterType == itemType)
                {
                    element = constructors[i].Invoke(new object[] { item });
                    break;
                }
            }
            if (element == null)
                throw new MissingMethodException(elementType.FullName,
                    ".ctor(ItemObject, ItemModifier, ItemObject, bool)");
            MethodInfo add = equipment.GetType().GetMethod("AddEquipmentToSlotWithoutAgent",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, new[] { indexType, elementType }, null);
            if (add == null)
                throw new MissingMethodException(equipment.GetType().FullName,
                    "AddEquipmentToSlotWithoutAgent");
            add.Invoke(equipment, new[] { index, element });
        }

        private static List<HeroEquipmentSlot> ReadEquipmentSlots(object equipment)
        {
            List<HeroEquipmentSlot> result = new List<HeroEquipmentSlot>();
            string[] names = { "Weapon0", "Weapon1", "Weapon2", "Weapon3",
                "Head", "Body", "Leg", "Gloves", "Cape", "Horse", "HorseHarness" };
            for (int i = 0; i < names.Length; i++)
            {
                object item = GetEquipmentItem(equipment, names[i]);
                if (item != null)
                    result.Add(new HeroEquipmentSlot { SlotName = names[i], Item = item });
            }
            return result;
        }

        private sealed class HeroEquipmentSlot
        {
            public string SlotName;
            public object Item;
        }
    }
}
