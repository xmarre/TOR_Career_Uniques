using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace TORCareerUniques
{
    internal static class CareerUniqueRuntime
    {
        private const string ModPrefix = "torcu_";
        private static readonly CareerItemDefinition[] Definitions = BuildDefinitions();
        private static readonly Dictionary<string, CareerItemDefinition> DefinitionByCareer = BuildDefinitionMap();
        private static readonly HashSet<string> LoggedErrors = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, object> ResolvedBaseItemByCareer =
            new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        private static object _baseItemCacheSession;

        private static bool _busy;
        private static bool _initialized;
        private static object _traitsInjectedManager;

        internal static void Initialize()
        {
            if (_initialized)
                return;

            _initialized = true;
            Log("Loaded item runtime. Career definitions: " + Definitions.Length + ".");
        }

        internal static void Tick()
        {
            if (_busy)
                return;

            _busy = true;
            try
            {
                EnsureTraitsInjected();
            }
            catch (Exception ex)
            {
                LogOnce("runtime-tick:" + ex.GetType().FullName + ":" + ex.Message,
                    "Item runtime initialization failed: " + FormatException(ex));
            }
            finally
            {
                _busy = false;
            }
        }

        internal static bool EnsureReady(out string error)
        {
            error = null;
            try
            {
                if (!EnsureTraitsInjected())
                {
                    error = "ToR's item-trait registry is not ready.";
                    return false;
                }

                object artisanBehavior = GetArtisanBehavior();
                if (artisanBehavior == null || GetField(artisanBehavior, "_customCraftedItems") == null)
                {
                    error = "ToR's artisan item save behavior is not ready.";
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = FormatException(ex);
                return false;
            }
        }

        internal static string GetCurrentCareerId()
        {
            object hero = GetStaticProperty(TypeByName("TaleWorlds.CampaignSystem.Hero"), "MainHero");
            return hero == null ? null : GetCareerId(hero);
        }

        internal static string GetItemName(string careerId)
        {
            CareerItemDefinition definition;
            return DefinitionByCareer.TryGetValue(careerId ?? String.Empty, out definition) ? definition.ItemName : careerId;
        }

        internal static string[] GetCareerIds()
        {
            string[] result = new string[Definitions.Length];
            for (int i = 0; i < Definitions.Length; i++)
                result[i] = Definitions[i].CareerId;
            return result;
        }

        internal static CareerItemDefinition GetDefinitionForSet(string careerId)
        {
            CareerItemDefinition definition;
            return DefinitionByCareer.TryGetValue(careerId ?? String.Empty, out definition) ? definition : null;
        }

        internal static bool IsClaimed(string careerId)
        {
            CareerItemDefinition definition;
            if (!DefinitionByCareer.TryGetValue(careerId ?? String.Empty, out definition))
                return false;
            object artisanBehavior = GetArtisanBehavior();
            return artisanBehavior != null && HasClaimed(artisanBehavior, definition.SignatureTraitId);
        }

        internal static bool TryGrantCareerItem(string careerId, out string itemName, out string error)
        {
            itemName = GetItemName(careerId);
            error = null;
            CareerItemDefinition definition;
            if (!DefinitionByCareer.TryGetValue(careerId ?? String.Empty, out definition))
            {
                error = "Unknown career id '" + careerId + "'.";
                return false;
            }

            if (!EnsureReady(out error))
                return false;

            object artisanBehavior = GetArtisanBehavior();
            if (HasClaimed(artisanBehavior, definition.SignatureTraitId))
            {
                error = "The relic has already been claimed.";
                return false;
            }

            try
            {
                return Grant(definition, artisanBehavior, out error);
            }
            catch (Exception ex)
            {
                error = FormatException(ex);
                Log("Grant failed for " + careerId + ": " + error);
                return false;
            }
        }

        internal static bool TryGrantCareerItemWithLootModifier(string careerId,
            out string itemName, out string error)
        {
            itemName = GetItemName(careerId);
            error = null;
            CareerItemDefinition definition;
            if (!DefinitionByCareer.TryGetValue(careerId ?? String.Empty, out definition))
            {
                error = "Unknown career id '" + careerId + "'.";
                return false;
            }

            if (!EnsureReady(out error))
                return false;

            try
            {
                object artisanBehavior = GetArtisanBehavior();
                return Grant(definition, artisanBehavior, true, out itemName, out error);
            }
            catch (Exception ex)
            {
                error = FormatException(ex);
                Log("Modified relic grant failed for " + careerId + ": " + error);
                return false;
            }
        }

        internal static string GrantThemedLoot(string[] tokens, int count, int seed)
        {
            if (count <= 0)
                return String.Empty;

            try
            {
                Type managerType = TypeByName("TaleWorlds.ObjectSystem.MBObjectManager");
                Type itemType = TypeByName("TaleWorlds.Core.ItemObject");
                Type mobilePartyType = TypeByName("TaleWorlds.CampaignSystem.Party.MobileParty");
                object manager = GetStaticProperty(managerType, "Instance");
                object mainParty = GetStaticProperty(mobilePartyType, "MainParty");
                object roster = GetProperty(mainParty, "ItemRoster");
                if (manager == null || itemType == null || roster == null)
                    return String.Empty;

                MethodInfo generic = null;
                foreach (MethodInfo candidate in managerType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                    if (candidate.Name == "GetObjectTypeList" && candidate.IsGenericMethodDefinition && candidate.GetParameters().Length == 0)
                        generic = candidate;
                if (generic == null)
                    return String.Empty;

                IEnumerable items = generic.MakeGenericMethod(itemType).Invoke(manager, null) as IEnumerable;
                MethodInfo add = FindInstanceMethod(roster.GetType(), "AddToCounts", new[] { itemType, typeof(int) });
                if (items == null || add == null)
                    return String.Empty;

                Type extendedManagerType = TypeByName("TOR_Core.Items.ExtendedItemObjectManager");
                MethodInfo isRuntimeDuplicate = FindStaticMethod(extendedManagerType, "IsRuntimeDuplicatedItem", 1);
                Dictionary<string, LootCandidate> deduplicated = new Dictionary<string, LootCandidate>(StringComparer.OrdinalIgnoreCase);
                foreach (object item in items)
                {
                    if (isRuntimeDuplicate != null && ToBoolean(isRuntimeDuplicate.Invoke(null, new object[] { item })))
                        continue;
                    ItemDescriptor descriptor = DescribeItem(item);
                    if (!descriptor.IsUsable || ToBoolean(GetProperty(item, "NotMerchandise")))
                        continue;

                    string id = Convert.ToString(GetProperty(item, "StringId")) ?? String.Empty;
                    string name = Convert.ToString(GetProperty(item, "Name")) ?? id;
                    string normalizedName = NormalizeLootKey(name);
                    if (String.IsNullOrEmpty(normalizedName))
                        normalizedName = NormalizeLootKey(id);
                    if (String.IsNullOrEmpty(normalizedName))
                        continue;

                    int themeScore = ScoreTokens(descriptor.SearchText, tokens, 30);
                    if (themeScore <= 0)
                        continue;

                    int tier = Math.Max(0, EnumNumber(GetProperty(item, "Tier")));
                    int value = Math.Max(0, EnumNumber(GetProperty(item, "Value")));
                    string category = GetLootCategory(descriptor.SearchText, descriptor.WeaponClass);
                    int score = themeScore + Math.Min(24, tier * 4) + Math.Min(18, value / 2500) + GetLootDesirability(category);
                    LootCandidate candidate = new LootCandidate
                    {
                        Item = item,
                        Score = score,
                        Name = name,
                        StringId = id,
                        Key = normalizedName,
                        Category = category
                    };

                    LootCandidate existing;
                    if (!deduplicated.TryGetValue(normalizedName, out existing) || candidate.Score > existing.Score ||
                        (candidate.Score == existing.Score && String.CompareOrdinal(candidate.StringId, existing.StringId) < 0))
                        deduplicated[normalizedName] = candidate;
                }

                List<LootCandidate> candidates = new List<LootCandidate>(deduplicated.Values);
                candidates.Sort(delegate(LootCandidate a, LootCandidate b)
                {
                    int byScore = b.Score.CompareTo(a.Score);
                    return byScore != 0 ? byScore : String.CompareOrdinal(a.StringId, b.StringId);
                });
                if (candidates.Count == 0)
                    return String.Empty;

                int pool = Math.Min(18, candidates.Count);
                candidates = candidates.GetRange(0, pool);
                Random random = new Random(seed);
                List<string> names = new List<string>();
                HashSet<string> usedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                HashSet<string> usedCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int targetCount = Math.Min(count, candidates.Count);

                for (int i = 0; i < targetCount; i++)
                {
                    List<LootCandidate> eligible = new List<LootCandidate>();
                    for (int j = 0; j < candidates.Count; j++)
                    {
                        LootCandidate candidate = candidates[j];
                        if (usedKeys.Contains(candidate.Key))
                            continue;
                        if (i > 0 && usedCategories.Contains(candidate.Category))
                            continue;
                        eligible.Add(candidate);
                    }
                    if (eligible.Count == 0)
                    {
                        for (int j = 0; j < candidates.Count; j++)
                            if (!usedKeys.Contains(candidates[j].Key))
                                eligible.Add(candidates[j]);
                    }
                    if (eligible.Count == 0)
                        break;

                    int minimumScore = eligible[eligible.Count - 1].Score;
                    int totalWeight = 0;
                    for (int j = 0; j < eligible.Count; j++)
                        totalWeight += Math.Max(1, eligible[j].Score - minimumScore + 6);
                    int roll = random.Next(totalWeight);
                    LootCandidate choice = eligible[0];
                    for (int j = 0; j < eligible.Count; j++)
                    {
                        roll -= Math.Max(1, eligible[j].Score - minimumScore + 6);
                        if (roll < 0)
                        {
                            choice = eligible[j];
                            break;
                        }
                    }

                    usedKeys.Add(choice.Key);
                    usedCategories.Add(choice.Category);
                    object modifier = RollLootModifier(choice.Item);
                    string addError;
                    if (!AddToRoster(roster, choice.Item, modifier, 1, out addError))
                    {
                        Log("Themed loot insertion failed for " + choice.StringId + ": " + addError);
                        continue;
                    }
                    names.Add(FormatModifiedItemName(
                        String.IsNullOrWhiteSpace(choice.Name) ? choice.StringId : choice.Name,
                        modifier));
                }
                return String.Join(", ", names.ToArray());
            }
            catch (Exception ex)
            {
                Log("Themed loot grant failed: " + FormatException(ex));
                return String.Empty;
            }
        }

        internal static object RollLootModifier(object item)
        {
            if (item == null)
                return null;
            try
            {
                object component = GetProperty(item, "ItemComponent");
                object group = GetProperty(component, "ItemModifierGroup");
                if (group == null)
                    return null;
                MethodInfo roll = group.GetType().GetMethod(
                    "GetRandomItemModifierLootScoreBased",
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance, null, Type.EmptyTypes, null);
                return roll == null ? null : roll.Invoke(group, null);
            }
            catch (Exception ex)
            {
                LogOnce("loot-modifier:" + ex.GetType().FullName + ":" + ex.Message,
                    "Loot modifier roll failed: " + FormatException(ex));
                return null;
            }
        }

        internal static bool AddToRoster(object roster, object item, object modifier,
            int count, out string error)
        {
            error = null;
            if (roster == null || item == null || count == 0)
            {
                error = "The destination roster or item is unavailable.";
                return false;
            }

            try
            {
                Type itemType = TypeByName("TaleWorlds.Core.ItemObject");
                Type modifierType = TypeByName("TaleWorlds.Core.ItemModifier");
                Type elementType = TypeByName("TaleWorlds.Core.EquipmentElement");
                if (itemType == null || elementType == null)
                {
                    error = "Core item/equipment types are unavailable.";
                    return false;
                }

                ConstructorInfo constructor = elementType.GetConstructor(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null, new[] { itemType, modifierType, itemType, typeof(bool) }, null);
                MethodInfo addElement = FindInstanceMethod(roster.GetType(),
                    "AddToCounts", new[] { elementType, typeof(int) });
                if (constructor != null && addElement != null)
                {
                    object element = constructor.Invoke(new[] { item, modifier, null, (object)false });
                    addElement.Invoke(roster, new object[] { element, count });
                    return true;
                }

                MethodInfo addItem = FindInstanceMethod(roster.GetType(),
                    "AddToCounts", new[] { itemType, typeof(int) });
                if (addItem == null)
                {
                    error = "No compatible ItemRoster.AddToCounts overload was found.";
                    return false;
                }
                addItem.Invoke(roster, new object[] { item, count });
                return true;
            }
            catch (Exception ex)
            {
                error = FormatException(ex);
                return false;
            }
        }

        internal static string FormatModifiedItemName(string baseName, object modifier)
        {
            string modifierName = Convert.ToString(GetProperty(modifier, "Name"));
            if (String.IsNullOrWhiteSpace(modifierName))
                return baseName ?? String.Empty;
            return modifierName.Trim() + " " + (baseName ?? String.Empty);
        }

        private static string NormalizeLootKey(string value)
        {
            if (String.IsNullOrWhiteSpace(value))
                return String.Empty;
            char[] buffer = value.ToLowerInvariant().ToCharArray();
            System.Text.StringBuilder result = new System.Text.StringBuilder(buffer.Length);
            bool previousSpace = false;
            for (int i = 0; i < buffer.Length; i++)
            {
                char c = buffer[i];
                if (Char.IsLetterOrDigit(c))
                {
                    result.Append(c);
                    previousSpace = false;
                }
                else if (!previousSpace)
                {
                    result.Append(' ');
                    previousSpace = true;
                }
            }
            return result.ToString().Trim();
        }

        private static string GetLootCategory(string text, string weaponClass)
        {
            text = text ?? String.Empty;
            weaponClass = weaponClass ?? String.Empty;
            if (ContainsAny(text, "boot", "shoe", "greave", "sabatons")) return "feet";
            if (ContainsAny(text, "glove", "gauntlet", "bracer")) return "hands";
            if (ContainsAny(text, "helmet", "helm", "hood", "hat", "coif", "crown")) return "head";
            if (ContainsAny(text, "body_armor", "body armour", "cuirass", "breastplate", "robe", "tunic", "mail_shirt")) return "body";
            if (text.Contains("shield")) return "shield";
            if (ContainsAny(weaponClass, "bow", "crossbow") || ContainsAny(text, "_bow", " bow", "crossbow")) return "ranged weapon";
            if (ContainsAny(weaponClass, "sword", "axe", "mace", "polearm", "spear") || ContainsAny(text, "sword", "axe", "hammer", "mace", "spear", "lance", "staff", "stave")) return "weapon";
            if (ContainsAny(text, "arrow", "bolt", "ammunition", "ammo")) return "ammunition";
            if (ContainsAny(text, "horse", "mount", "saddle", "harness")) return "mount";
            if (ContainsAny(text, "potion", "ingredient", "ore", "ingot", "gem", "powder", "scroll", "grimoire", "book")) return "arcane salvage";
            return "other";
        }

        private static int GetLootDesirability(string category)
        {
            switch (category)
            {
                case "weapon": return 20;
                case "ranged weapon": return 18;
                case "shield": return 16;
                case "body": return 14;
                case "head": return 11;
                case "arcane salvage": return 10;
                case "ammunition": return 7;
                case "hands": return 3;
                case "feet": return 2;
                case "mount": return 1;
                default: return 5;
            }
        }

        internal static void Notify(string text)
        {
            DisplayMessage(text);
        }

        internal static object GetArtisanBehavior()
        {
            return GetStaticProperty(TypeByName("TOR_Core.CampaignMechanics.Crafting.TORArtisanDistrictCampaignBehavior"), "Instance");
        }
        private static bool EnsureTraitsInjected()
        {
            Type managerType = TypeByName("TOR_Core.Items.ItemTraitManager");
            Type traitType = TypeByName("TOR_Core.Items.ItemTrait");
            if (managerType == null || traitType == null)
                return false;

            object manager = GetStaticProperty(managerType, "Instance");
            if (manager == null)
                return false;

            if (Object.ReferenceEquals(manager, _traitsInjectedManager))
                return true;

            MethodInfo getTraits = managerType.GetMethod("GetItemTraits", BindingFlags.Public | BindingFlags.Instance);
            if (getTraits == null)
                return false;

            IList traits = getTraits.Invoke(manager, null) as IList;
            if (traits == null || traits.Count == 0)
                return false;

            HashSet<string> existing = new HashSet<string>(StringComparer.Ordinal);
            foreach (object trait in traits)
            {
                string id = Convert.ToString(GetProperty(trait, "ItemTraitStringId"));
                if (!String.IsNullOrEmpty(id))
                    existing.Add(id);
            }

            int added = 0;
            for (int i = 0; i < Definitions.Length; i++)
            {
                CareerItemDefinition definition = Definitions[i];
                for (int j = 0; j < definition.Traits.Length; j++)
                {
                    TraitDefinition spec = definition.Traits[j];

                    // Slot 4 is deliberately a native TOR enchantment/blessing/rune.
                    // Never fabricate a look-alike trait: scripted TOR effects depend on
                    // resolving the real ItemTrait object from TOR's loaded registry.
                    if (j == 3)
                    {
                        if (!existing.Contains(spec.Id))
                        {
                            ModLog.Error("Required native TOR trait is missing: " + spec.Id +
                                " (career relic " + definition.CareerId + ").");
                            return false;
                        }
                        continue;
                    }

                    if (existing.Contains(spec.Id))
                        continue;

                    object trait = CreateTrait(traitType, spec, definition.ValidItemType);
                    traits.Add(trait);
                    existing.Add(spec.Id);
                    added++;
                }
            }

            if (added > 0)
                Log("Injected " + added + " unique-item traits into ToR's trait registry.");

            bool complete = Definitions.Length == 0 ||
                existing.Contains(Definitions[0].SignatureTraitId);
            if (complete)
                _traitsInjectedManager = manager;
            return complete;
        }

        internal static object CreateTrait(Type traitType, TraitDefinition spec, string validItemType)
        {
            object trait = Activator.CreateInstance(traitType, true);
            SetProperty(trait, "ItemTraitStringId", spec.Id);
            SetProperty(trait, "ItemTraitName", spec.Name);
            SetProperty(trait, "ItemTraitDescription", spec.Description);
            SetProperty(trait, "IconName", spec.IconName ?? "traits_magic_icon");
            SetProperty(trait, "IsCraftable", false);
            SetEnumProperty(trait, "ValidItemType", validItemType);

            Type statsTupleType = TypeByName("TOR_Core.Items.StatsTuple");
            object statsTuple = Activator.CreateInstance(statsTupleType);
            if (spec.Kind == TraitKind.Stat)
            {
                SetEnumProperty(statsTuple, "StatType", spec.EffectType);
                SetProperty(statsTuple, "SkillId", spec.SkillId ?? "none");
                SetProperty(statsTuple, "Value", spec.Value);
            }
            SetProperty(trait, "StatsTuple", statsTuple);

            if (spec.Kind == TraitKind.Amplifier)
            {
                Type tupleType = TypeByName("TOR_Core.Extensions.ExtendedInfoSystem.AmplifierTuple");
                object tuple = Activator.CreateInstance(tupleType);
                SetEnumField(tuple, "AmplifiedDamageType", spec.EffectType);
                SetField(tuple, "DamageAmplifier", spec.Value);
                SetProperty(trait, "AmplifierTuple", tuple);
            }
            else if (spec.Kind == TraitKind.Resistance)
            {
                Type tupleType = TypeByName("TOR_Core.Extensions.ExtendedInfoSystem.ResistanceTuple");
                object tuple = Activator.CreateInstance(tupleType);
                SetEnumField(tuple, "ResistedDamageType", spec.EffectType);
                SetField(tuple, "ReductionPercent", spec.Value);
                SetProperty(trait, "ResistanceTuple", tuple);
            }
            else if (spec.Kind == TraitKind.AdditionalDamage)
            {
                Type tupleType = TypeByName("TOR_Core.Extensions.ExtendedInfoSystem.DamageProportionTuple");
                object tuple = Activator.CreateInstance(tupleType);
                SetEnumField(tuple, "DamageType", spec.EffectType);
                SetField(tuple, "Percent", spec.Value);
                SetProperty(trait, "AdditionalDamageTuple", tuple);
            }

            return trait;
        }

        private static string GetCareerId(object hero)
        {
            Type extensions = TypeByName("TOR_Core.Extensions.HeroExtensions");
            if (extensions == null)
                return null;

            MethodInfo method = FindStaticMethod(extensions, "GetCareer", 1);
            if (method == null)
                return null;

            object career = method.Invoke(null, new object[] { hero });
            if (career == null)
                return null;

            return Convert.ToString(GetProperty(career, "StringId"));
        }

        internal static bool HasClaimed(object artisanBehavior, string signatureTraitId)
        {
            object dictionaryObject = GetField(artisanBehavior, "_customCraftedItems");
            IEnumerable dictionary = dictionaryObject as IEnumerable;
            if (dictionary == null)
                return false;

            foreach (object entry in dictionary)
            {
                object data = GetProperty(entry, "Value");
                if (data == null)
                    continue;

                IEnumerable traitIds = GetProperty(data, "ItemTraits") as IEnumerable;
                if (traitIds == null)
                    continue;

                foreach (object id in traitIds)
                {
                    if (String.Equals(Convert.ToString(id), signatureTraitId, StringComparison.Ordinal))
                        return true;
                }
            }

            return false;
        }

        private static bool Grant(CareerItemDefinition definition, object artisanBehavior, out string error)
        {
            string ignoredName;
            return Grant(definition, artisanBehavior, false, out ignoredName, out error);
        }

        private static bool Grant(CareerItemDefinition definition, object artisanBehavior,
            bool rollLootModifier, out string grantedName, out string error)
        {
            grantedName = definition == null ? String.Empty : definition.ItemName;
            error = null;
            Type mobilePartyType = TypeByName("TaleWorlds.CampaignSystem.Party.MobileParty");
            object mainParty = GetStaticProperty(mobilePartyType, "MainParty");
            object roster = GetProperty(mainParty, "ItemRoster");
            if (roster == null)
            {
                error = "The player party item roster is unavailable.";
                return false;
            }

            object baseItem = FindBaseItem(definition);
            if (baseItem == null)
            {
                error = "No suitable base item was found for " + definition.CareerId + " (" + definition.Kind + ").";
                LogOnce("missing-base:" + definition.CareerId, error);
                return false;
            }

            Type helperType = TypeByName("TOR_Core.CampaignMechanics.Crafting.EnchantmentHelper");
            MethodInfo create = FindStaticMethod(helperType, "CreateEnchantedItem", 5);
            if (create == null)
            {
                error = "Unable to find ToR EnchantmentHelper.CreateEnchantedItem.";
                LogOnce("missing-enchantment-helper", error);
                return false;
            }

            Type listType = typeof(List<>).MakeGenericType(typeof(string));
            IList traitIds = (IList)Activator.CreateInstance(listType);
            for (int i = 0; i < definition.Traits.Length; i++)
                traitIds.Add(definition.Traits[i].Id);

            object newItem = create.Invoke(null, new object[] { baseItem, traitIds, definition.ItemName, false, null });
            if (newItem == null)
                throw new InvalidOperationException("ToR returned null while creating " + definition.ItemName + ".");

            EnsureClaimRecorded(artisanBehavior, baseItem, newItem, definition.ItemName, traitIds, definition.SignatureTraitId);
            object modifier = rollLootModifier ? RollLootModifier(newItem) : null;
            if (!AddToRoster(roster, newItem, modifier, 1, out error))
                return false;
            grantedName = FormatModifiedItemName(definition.ItemName, modifier);

            string baseId = Convert.ToString(GetProperty(baseItem, "StringId"));
            Log("Granted '" + grantedName + "' for career " + definition.CareerId + " using base item " + baseId + ".");
            return true;
        }


        private static void EnsureClaimRecorded(object artisanBehavior, object baseItem, object newItem, string itemName,
            IList traitIds, string signatureTraitId)
        {
            object dictionaryObject = GetField(artisanBehavior, "_customCraftedItems");
            IDictionary dictionary = dictionaryObject as IDictionary;
            Type dataType = TypeByName("TOR_Core.CampaignMechanics.Crafting.TorItemDuplicationData");
            if (dictionary == null || dataType == null)
                throw new InvalidOperationException("ToR did not record the unique item and its crafting save dictionary is unavailable.");

            if (dictionary.Contains(newItem))
                return;

            object data = Activator.CreateInstance(dataType);
            SetProperty(data, "OriginalItemStringId", Convert.ToString(GetProperty(baseItem, "StringId")));
            SetProperty(data, "NewItemName", itemName);
            SetProperty(data, "ItemTraits", traitIds);
            SetProperty(data, "IsPlayerCrafted", false);
            Type extendedManager = TypeByName("TOR_Core.Items.ExtendedItemObjectManager");
            MethodInfo addCrafted = FindStaticMethod(extendedManager, "AddCraftedItem", 3);
            if (addCrafted == null)
                throw new MissingMethodException("TOR_Core.Items.ExtendedItemObjectManager", "AddCraftedItem");

            dictionary.Add(newItem, data);
            try
            {
                addCrafted.Invoke(null, new object[]
                {
                    Convert.ToString(GetProperty(baseItem, "StringId")),
                    Convert.ToString(GetProperty(newItem, "StringId")),
                    traitIds
                });
            }
            catch
            {
                dictionary.Remove(newItem);
                throw;
            }
            Log("ToR's duplication event had not recorded the item; applied the equivalent save registration directly.");
        }

        internal static object FindBaseItem(CareerItemDefinition definition)
        {
            if (definition == null)
                return null;

            EnsureBaseItemCacheSession();
            object cached;
            if (ResolvedBaseItemByCareer.TryGetValue(definition.CareerId, out cached) &&
                cached != null)
                return cached;

            Type managerType = TypeByName("TaleWorlds.ObjectSystem.MBObjectManager");
            Type itemType = TypeByName("TaleWorlds.Core.ItemObject");
            if (managerType == null || itemType == null)
                return null;

            object manager = GetStaticProperty(managerType, "Instance");
            if (manager == null)
                return null;

            MethodInfo generic = null;
            MethodInfo[] methods = managerType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo candidate = methods[i];
                if (candidate.Name == "GetObjectTypeList" && candidate.IsGenericMethodDefinition && candidate.GetParameters().Length == 0)
                {
                    generic = candidate;
                    break;
                }
            }

            if (generic == null)
                return null;

            IEnumerable items = generic.MakeGenericMethod(itemType).Invoke(manager, null) as IEnumerable;
            if (items == null)
                return null;

            Type extendedManagerType = TypeByName("TOR_Core.Items.ExtendedItemObjectManager");
            MethodInfo isRuntimeDuplicate = FindStaticMethod(extendedManagerType, "IsRuntimeDuplicatedItem", 1);

            // Prefer a correctly typed item from the intended culture/faction.  A second
            // exact-kind pool exists only for careers whose TOR item metadata does not
            // expose a culture token.  Both pools use the same strict weapon-kind gate.
            object themed = null;
            ItemDescriptor themedDescriptor = null;
            int themedScore = Int32.MinValue;
            object fallback = null;
            ItemDescriptor fallbackDescriptor = null;
            int fallbackScore = Int32.MinValue;

            foreach (object item in items)
            {
                if (isRuntimeDuplicate != null && ToBoolean(isRuntimeDuplicate.Invoke(null, new object[] { item })))
                    continue;

                ItemDescriptor descriptor = DescribeItem(item);
                if (!descriptor.IsUsable)
                    continue;

                int kindScore = ScoreKind(definition.Kind, descriptor);
                if (kindScore < 0)
                    continue;

                int preferredScore = ScoreTokens(descriptor.SearchText,
                    definition.PreferredTokens, 180);
                int qualityScore = ScoreBaseItemQuality(definition.Kind, descriptor);
                int archetypeScore = SetItemRuntime.GetVisualArchetypeItemAffinity(
                    definition.CareerId, item);
                int genericScore = kindScore + preferredScore + qualityScore + archetypeScore;
                if (IsBetterBaseCandidate(genericScore, descriptor.StringId,
                    fallbackScore, fallbackDescriptor == null ? null : fallbackDescriptor.StringId))
                {
                    fallbackScore = genericScore;
                    fallback = item;
                    fallbackDescriptor = descriptor;
                }

                int factionScore = ScoreTokens(descriptor.SearchText,
                    definition.FactionTokens, 650);
                if (factionScore <= 0 && archetypeScore <= 0 && preferredScore <= 0)
                    continue;

                // All genuinely themed candidates compete in one pool.  An item actually
                // equipped by the matching TOR archetype may have a sparse StringId/culture
                // tag, so it must be allowed to beat a generic faction-tagged weapon.
                int score = genericScore + Math.Max(0, factionScore);
                if (IsBetterBaseCandidate(score, descriptor.StringId,
                    themedScore, themedDescriptor == null ? null : themedDescriptor.StringId))
                {
                    themedScore = score;
                    themed = item;
                    themedDescriptor = descriptor;
                }
            }

            object selected = themed ?? fallback;
            ItemDescriptor selectedDescriptor = themed != null ? themedDescriptor : fallbackDescriptor;
            if (selected != null && selectedDescriptor != null)
            {
                ResolvedBaseItemByCareer[definition.CareerId] = selected;
                ModLog.Info("Resolved relic base for " + definition.CareerId + " / " +
                    definition.ItemName + ": " + selectedDescriptor.StringId + " (kind=" +
                    definition.Kind + ", class=" + selectedDescriptor.WeaponClass + ", tier=" +
                    selectedDescriptor.Tier + ", value=" + selectedDescriptor.Value +
                    ", damage=" + selectedDescriptor.DamageScore +
                    ", archetypeAffinity=" + SetItemRuntime.GetVisualArchetypeItemAffinity(
                        definition.CareerId, selected) +
                    (themed != null ? ", themed=true" : ", themed=false") + ").");
            }
            return selected;
        }

        private static void EnsureBaseItemCacheSession()
        {
            object session = GetStaticProperty(
                TypeByName("TaleWorlds.CampaignSystem.Campaign"), "Current");
            if (Object.ReferenceEquals(session, _baseItemCacheSession))
                return;
            _baseItemCacheSession = session;
            ResolvedBaseItemByCareer.Clear();
        }

        internal static bool IsBaseItemCompatible(CareerItemDefinition definition, object item)
        {
            return definition != null && item != null &&
                ScoreKind(definition.Kind, DescribeItem(item)) >= 0;
        }

        private static bool IsBetterBaseCandidate(int score, string stringId,
            int currentScore, string currentStringId)
        {
            if (score != currentScore)
                return score > currentScore;
            if (currentStringId == null)
                return true;
            return String.CompareOrdinal(stringId ?? String.Empty,
                currentStringId) < 0;
        }

        private static int ScoreBaseItemQuality(BaseKind kind, ItemDescriptor item)
        {
            // Tier is the strongest generic quality signal. Value catches special/high-end
            // equipment whose tier metadata is sparse. Physical damage matters for actual
            // damage-dealing weapons; mage staves and shields are intentionally not forced
            // into a melee-damage contest.
            int score = Math.Min(7, Math.Max(0, item.Tier)) * 170 +
                Math.Min(220, Math.Max(0, item.Value) / 900);

            if (kind != BaseKind.Staff && kind != BaseKind.Shield)
                score += Math.Min(700, Math.Max(0, item.DamageScore) * 5);
            if (item.NotMerchandise)
                score += 60;
            return score;
        }

        private static ItemDescriptor DescribeItem(object item)
        {
            string id = Convert.ToString(GetProperty(item, "StringId")) ?? "";
            string name = Convert.ToString(GetProperty(item, "Name")) ?? "";
            string typeName = Convert.ToString(GetItemTypeValue(item)) ?? "";
            int itemTypeNumber = EnumNumber(GetItemTypeValue(item));
            object primaryWeapon = GetProperty(item, "PrimaryWeapon");
            string weaponClass = Convert.ToString(GetProperty(primaryWeapon, "WeaponClass")) ?? "";
            bool hasWeapon = ToBoolean(GetProperty(item, "HasWeaponComponent"));
            bool hasArmor = ToBoolean(GetProperty(item, "HasArmorComponent"));
            bool isCrafted = ToBoolean(GetProperty(item, "IsCraftedByPlayer"));
            object culture = GetProperty(item, "Culture");
            string cultureId = Convert.ToString(GetProperty(culture, "StringId")) ?? "";
            string cultureName = Convert.ToString(GetProperty(culture, "Name")) ?? "";
            int tier = Math.Max(0, EnumNumber(GetProperty(item, "Tier")));
            int value = Math.Max(0, EnumNumber(GetProperty(item, "Value")));
            int swingDamage = Math.Max(0, EnumNumber(GetProperty(primaryWeapon, "SwingDamage")));
            int thrustDamage = Math.Max(0, EnumNumber(GetProperty(primaryWeapon, "ThrustDamage")));
            int damageScore = Math.Max(swingDamage, thrustDamage);
            bool notMerchandise = ToBoolean(GetProperty(item, "NotMerchandise"));
            string searchText = (id + " " + name + " " + typeName + " " + weaponClass +
                " " + cultureId + " " + cultureName).ToLowerInvariant();
            bool isInternalTemplate = ContainsAny(searchText, "_template", " template", "_quest", "quest_", "tournament",
                "practice", "training", "dummy", "blueprint", "debug_", "_debug", "test_", "_test");
            bool usable = !String.IsNullOrWhiteSpace(id) && !isCrafted && !id.StartsWith(ModPrefix, StringComparison.OrdinalIgnoreCase)
                && !isInternalTemplate && (hasWeapon || hasArmor || itemTypeNumber == 8);

            return new ItemDescriptor
            {
                IsUsable = usable,
                HasWeapon = hasWeapon,
                ItemTypeNumber = itemTypeNumber,
                StringId = id,
                SearchText = searchText,
                WeaponClass = weaponClass.ToLowerInvariant(),
                Tier = tier,
                Value = value,
                DamageScore = damageScore,
                NotMerchandise = notMerchandise
            };
        }

        private static int ScoreKind(BaseKind kind, ItemDescriptor item)
        {
            string text = item.SearchText;
            string wc = item.WeaponClass;
            bool looksLikeStaff = ContainsAny(text, "staff", "stave", "wand");
            bool polearmClass = ContainsAny(wc, "polearm", "spear");

            switch (kind)
            {
                case BaseKind.Staff:
                    // TOR caster staves can use unconventional mechanical weapon classes.
                    // Visual semantics are authoritative here; generic polearms are never
                    // allowed to masquerade as a staff.
                    if (item.HasWeapon && looksLikeStaff) return 1200;
                    return -1;

                case BaseKind.Lance:
                    if (!item.HasWeapon || looksLikeStaff || !polearmClass)
                        return -1;
                    if (text.Contains("lance")) return 1250;
                    return -1;

                case BaseKind.Sword:
                    if (!item.HasWeapon || looksLikeStaff)
                        return -1;
                    // Every current sword relic is authored around One Handed bonuses.
                    // Reject two-handed swords outright so quality scoring can never make
                    // those bonuses dead by selecting a mechanically incompatible base.
                    return wc.Contains("onehandedsword") ? 1250 : -1;

                case BaseKind.Hammer:
                    if (!item.HasWeapon || looksLikeStaff || !wc.Contains("mace"))
                        return -1;
                    // Prefer an explicitly hammer/mace-themed model. A mace-class fallback
                    // remains legal because TOR can classify visually valid warhammers under
                    // the shared mace weapon class even when the item text is sparse.
                    if (ContainsAny(text, "hammer", "warhammer", "mace"))
                        return 1250;
                    return 1050;

                case BaseKind.Bow:
                    // Shared strict ranged gate: native bows and TOR pistol-class firearms.
                    // Crossbows remain excluded so the Waywatcher resolver cannot drift into
                    // generic crossbows; Witch Hunter pistols are identified by their item text.
                    if (!item.HasWeapon || wc.Contains("crossbow"))
                        return -1;
                    if (text.Contains("pistol"))
                        return 1250;
                    return wc.Contains("bow") ? 1250 : -1;

                case BaseKind.Shield:
                    return item.ItemTypeNumber == 8 ? 1250 : -1;

                case BaseKind.Axe:
                    if (!item.HasWeapon || looksLikeStaff ||
                        ContainsAny(text, " bow", "_bow", "crossbow"))
                        return -1;
                    if (wc.Contains("onehandedaxe")) return 1250;
                    if (wc.Contains("twohandedaxe")) return 1120;
                    return -1;

                case BaseKind.GreatAxe:
                    if (!item.HasWeapon || looksLikeStaff)
                        return -1;
                    return wc.Contains("twohandedaxe") ? 1250 : -1;

                case BaseKind.Spear:
                    if (!item.HasWeapon || looksLikeStaff || !polearmClass)
                        return -1;
                    if (ContainsAny(text, "spear", "trident", "pike", "halberd", "glaive"))
                        return 1250;
                    return -1;

                default:
                    return -1;
            }
        }

        private static int ScoreTokens(string text, string[] tokens, int points)
        {
            if (tokens == null)
                return 0;

            int score = 0;
            for (int i = 0; i < tokens.Length; i++)
            {
                if (!String.IsNullOrEmpty(tokens[i]) && text.Contains(tokens[i].ToLowerInvariant()))
                    score += points;
            }
            return score;
        }

        private static bool ContainsAny(string text, params string[] values)
        {
            if (text == null)
                return false;
            for (int i = 0; i < values.Length; i++)
                if (text.Contains(values[i])) return true;
            return false;
        }

        private static void DisplayMessage(string text)
        {
            try
            {
                Type infoType = TypeByName("TaleWorlds.Library.InformationMessage");
                Type managerType = TypeByName("TaleWorlds.Library.InformationManager") ??
                    TypeByName("TaleWorlds.Core.InformationManager") ??
                    TypeByName("TaleWorlds.Core.MBInformationManager");
                if (infoType == null || managerType == null)
                    return;

                object message = Activator.CreateInstance(infoType, new object[] { text });
                MethodInfo display = FindStaticCompatibleMethod(managerType, "DisplayMessage", infoType);
                if (display != null)
                    display.Invoke(null, new object[] { message });
            }
            catch
            {
                // The item grant is authoritative; notification failure is non-fatal.
            }
        }

        private static CareerItemDefinition[] BuildDefinitions()
        {
            string[] bret = { "tor_br", "breton", "couronne", "grail" };
            string[] vampire = { "tor_vc", "vamp", "sylvan", "undead" };
            string[] empire = { "tor_emp", "empire", "sigmar", "reik", "nuln" };
            string[] woodElf = { "tor_we_", "woodelf", "wood_elf", "asrai", "athel", "loren" };
            string[] dwarf = { "tor_dw", "dwarf", "karak", "gromril" };
            string[] orc = { "tor_orc", "greenskin", "orc", "goblin" };

            return new[]
            {
                D("GrailDamsel", "Chalice-Stave of the Lady", BaseKind.Staff, "Weapon", bret, new[]{"staff","stave","lady","grail","damsel","fay"},
                    S("core", "The Lady's Font", "+30 maximum Winds of Magic.", "WindsOfMagicMax", 30f),
                    S("renewal", "Sacred Renewal", "+0.25 Winds of Magic recharge.", "WindsOfMagicRegen", 0.25f),
                    S("aura", "Grail Radiance", "+20% spell radius.", "SpellRadius", 20f),
                    S("native", "Foresight of Azyr", "15% extra lightning damage and 15% physical resistance; 15% chance to grant nearby troops the same boons for 20 seconds upon dealing damage.", "emp_enchant_azyr_foresight", 0f)),

                D("GrailKnight", "Blessed Lance of Couronne", BaseKind.Lance, "Weapon", bret, new[]{"lance","grail","couronne","breton","knight"},
                    A("core", "Grail-Forged Edge", "+12% physical damage.", "Physical", 0.12f),
                    S("piercing", "Dragon-Piercing Point", "+15% armor penetration.", "ArmorPenetration", 15f),
                    K("cavalier", "Peerless Cavalier", "+20 Riding.", "Riding", 20f),
                    S("native", "Legacy of the Grail", "30% extra holy damage; weapon gains the Cleave trait.", "bret_blessing_grail_legacy", 0f)),

                D("MinorVampire", "Von Carstein Nightblade", BaseKind.Sword, "Weapon", vampire, new[]{"sword","blade","von carstein","vampire"},
                    S("core", "The Hunger", "+20% healing rate.", "HealthRegen", 0.20f),
                    S("swiftness", "Unnatural Swiftness", "+8% movement speed.", "MovementSpeed", 8f),
                    A("predator", "Midnight Predator", "+10% physical damage.", "Physical", 0.10f),
                    S("native", "Drinker of Blood", "15 extra magic damage; below 50% HP, recover 1 HP for every instance of melee damage dealt.", "vc_enchant_drinker_blood", 0f)),

                D("WarriorPriest", "Warhammer of the Twin-Tailed Comet", BaseKind.Hammer, "Weapon", empire, new[]{"hammer","warhammer","sigmar","priest","holy"},
                    X("core", "Soulfire", "20% additional holy damage.", "Holy", 0.20f),
                    S("fervour", "Battle Prayer", "+0.20 career-resource generation.", "CustomResourceGain", 0.20f),
                    S("unyielding", "Sigmar's Bulwark", "+25 maximum health.", "HealthMax", 25f),
                    S("native", "Exorcism of Sigmar", "20% extra holy damage; 10% chance to deal 100 damage against Undead and Daemons.", "emp_blessing_sigmar_exorcism", 0f)),

                D("BloodKnight", "Blood Dragon's Crimson Blade", BaseKind.Sword, "Weapon", vampire, new[]{"blood","dragon","sword","blood knight"},
                    A("core", "Red Thirst", "+15% physical damage.", "Physical", 0.15f),
                    S("speed", "Blood Dragon Technique", "+12% swing speed.", "SwingSpeed", 12f),
                    S("renewal", "Vampiric Renewal", "+15% healing rate.", "HealthRegen", 0.15f),
                    S("native", "Drinker of Blood", "15 extra magic damage; below 50% HP, recover 1 HP for every instance of melee damage dealt.", "vc_enchant_drinker_blood", 0f)),

                D("Mercenary", "Paymaster's Blade of the Border Princes", BaseKind.Sword, "Weapon", empire, new[]{"sword","blade","mercenary","border"},
                    K("core", "Veteran's Edge", "+20 One Handed.", "OneHanded", 20f),
                    S("march", "Road-Hardened", "+10% party map speed.", "PartySpeed", 10f),
                    A("pragmatist", "No Fair Fights", "+8% physical damage.", "Physical", 0.08f),
                    S("native", "Crucible of Chamon", "20% extra magic damage; weapon gains the Cleave trait.", "emp_enchant_chamon_crucible", 0f)),

                D("WitchHunter", "Silvered Blade of the Templars", BaseKind.Bow, "Weapon", empire, new[]{"witch","silver","pistol","templar"},
                    A("core", "Judgement", "+10% physical damage.", "Physical", 0.10f),
                    R("ward", "Hexward Silver", "15% resistance to magical damage.", "Magical", 0.15f),
                    K("duellist", "Templar's Training", "+20 Crossbow.", "Crossbow", 20f),
                    S("native", "Exorcism of Sigmar", "20% extra holy damage; 10% chance to deal 100 damage against Undead and Daemons.", "emp_blessing_sigmar_exorcism", 0f)),

                D("Necromancer", "Staff of Damnation", BaseKind.Staff, "Weapon", vampire, new[]{"staff","bone","necro","damnation"},
                    S("core", "Reservoir of Dhar", "+40 maximum Winds of Magic.", "WindsOfMagicMax", 40f),
                    S("dhar", "Dhar Conduit", "+0.30 Winds of Magic recharge.", "WindsOfMagicRegen", 0.30f),
                    S("legion", "Master of the Restless Dead", "+20% spell radius.", "SpellRadius", 20f),
                    S("native", "Call from Beyond", "15% bonus to magic damage; summon 3 Grave Guard for every enemy felled in melee.", "vc_enchant_call_beyond", 0f)),

                D("BlackGrailKnight", "Lance of the Black Grail", BaseKind.Lance, "Weapon", new[]{"mousillon","black","grail","tor_vc","tor_br"}, new[]{"lance","black","grail","mousillon","knight"},
                    A("core", "Dark Chivalry", "+15% physical damage.", "Physical", 0.15f),
                    X("curse", "Accursed Grail", "15% additional magical damage.", "Magical", 0.15f),
                    S("undeath", "Undying Vow", "+30 maximum health.", "HealthMax", 30f),
                    S("native", "The Crimson Flood", "25 extra magic damage; 10% chance to send out a damaging wave of magic with each strike, draining 2 HP from the wearer.", "vc_enchant_crimson_flood", 0f)),

                D("Necrarch", "Necrarch Bone Staff", BaseKind.Staff, "Weapon", vampire, new[]{"staff","bone","necrarch","dhar"},
                    S("core", "Abyssal Reservoir", "+50 maximum Winds of Magic.", "WindsOfMagicMax", 50f),
                    S("conduit", "Unbound Dhar", "+0.35 Winds of Magic recharge.", "WindsOfMagicRegen", 0.35f),
                    A("mastery", "Necrarch Mastery", "+18% magical damage.", "Magical", 0.18f),
                    S("native", "The Crimson Flood", "25 extra magic damage; 10% chance to send out a damaging wave of magic with each strike, draining 2 HP from the wearer.", "vc_enchant_crimson_flood", 0f)),

                D("WarriorPriestUlric", "Winter's Bite", BaseKind.Axe, "Weapon", empire, new[]{"axe","ulric","wolf","midden","teutogen"},
                    X("core", "Winter's Bite", "20% additional frost damage.", "Frost", 0.20f),
                    A("fury", "Ulric's Fury", "+12% physical damage.", "Physical", 0.12f),
                    K("hunter", "Wolf's Endurance", "+20 Athletics.", "Athletics", 20f),
                    S("native", "Wrath of Ulric", "350% extra shield damage; 15% chance to gain a fleeting 2% attack-speed bonus upon dealing damage, stacking up to 5 times.", "emp_blessing_ulric_wrath", 0f)),

                D("ImperialMagister", "Collegiate Staff of Volans", BaseKind.Staff, "Weapon", empire, new[]{"staff","magister","collegiate","wizard"},
                    S("core", "Collegiate Reservoir", "+35 maximum Winds of Magic.", "WindsOfMagicMax", 35f),
                    S("channel", "Arcane Channel", "+0.30 Winds of Magic recharge.", "WindsOfMagicRegen", 0.30f),
                    S("geometry", "Mastered Conjunction", "+20% spell radius.", "SpellRadius", 20f),
                    S("native", "Foresight of Azyr", "15% extra lightning damage and 15% physical resistance; 15% chance to grant nearby troops the same boons for 20 seconds upon dealing damage.", "emp_enchant_azyr_foresight", 0f)),

                D("Waywatcher", "The Bow of Loren", BaseKind.Bow, "Ranged", woodElf, new[]{"bow","waywatcher","loren","asrai"},
                    K("core", "Hawkeye", "+25 Bow.", "Bow", 25f),
                    S("flight", "Asrai Fletching", "+15% missile speed.", "MissileSpeed", 15f),
                    S("pierce", "Needle Through Oak", "+1 missile penetration.", "MultiPenetration", 1f),
                    S("native", "Predator of Anath Raema", "15 extra magic damage and missile speed; dismount damaged cavalry units.", "asrai_enchant_anath_raema", 0f)),

                D("Spellsinger", "Calaingor's Stave", BaseKind.Staff, "Weapon", woodElf, new[]{"staff","stave","spellsinger","wood","asrai"},
                    S("core", "Deepwood Wellspring", "+30 maximum Winds of Magic.", "WindsOfMagicMax", 30f),
                    S("song", "Song of Renewal", "+0.25 Winds of Magic recharge.", "WindsOfMagicRegen", 0.25f),
                    S("canopy", "Calaingor's Canopy", "+25% spell radius.", "SpellRadius", 25f),
                    S("native", "Tranquillity of Cadai", "15% extra magic damage and magic resistance; 30% chance to grant nearby troops the same boons for a short time upon dealing damage.", "we_enchant_tranquillity_cadai", 0f)),

                D("Warden", "Warden's Spear of the Wild Hunt", BaseKind.Spear, "Weapon", woodElf, new[]{"spear","trident","warden","kurnous","wild hunt"},
                    K("core", "Spear-Dancer", "+25 Polearm.", "Polearm", 25f),
                    A("hunt", "Kurnous' Hunt", "+12% physical damage.", "Physical", 0.12f),
                    S("stride", "Fleet of Foot", "+8% movement speed.", "MovementSpeed", 8f),
                    S("native", "Trance of Loec", "10% extra swing speed, physical damage, and physical resistance.", "asrai_enchant_trance_loec", 0f)),

                D("GreyLord", "Shadowstaff of the Grey Order", BaseKind.Staff, "Weapon", empire, new[]{"staff","grey","shadow","ulgu"},
                    K("core", "Master of Ulgu", "+25 Spellcraft.", "Spellcraft", 25f),
                    S("reserve", "Veiled Reservoir", "+30 maximum Winds of Magic.", "WindsOfMagicMax", 30f),
                    S("mist", "Mists of Ulgu", "+0.25 Winds of Magic recharge.", "WindsOfMagicRegen", 0.25f),
                    S("native", "Dusk and Dawn", "40 extra magic and holy damage.", "we_enchant_dusk_dawn", 0f)),

                D("KnightOldWorld", "Runeblade of the Old World", BaseKind.Sword, "Weapon", empire, new[]{"sword","runeblade","knight","old world"},
                    K("core", "Knightly Mastery", "+20 One Handed.", "OneHanded", 20f),
                    K("cavalry", "Saddle-Born", "+20 Riding.", "Riding", 20f),
                    R("plate", "Tempered Plate", "10% physical damage resistance.", "Physical", 0.10f),
                    S("native", "Crucible of Chamon", "20% extra magic damage; weapon gains the Cleave trait.", "emp_enchant_chamon_crucible", 0f)),

                D("Ironbreaker", "Gromril Bulwark of Karaz-a-Karak", BaseKind.Shield, "Shield", dwarf, new[]{"shield","gromril","iron","ironbreaker"},
                    S("core", "Gromril Face", "+300 shield hit points.", "ShieldHealth", 300f),
                    R("stone", "Hold Like Stone", "18% physical damage resistance.", "Physical", 0.18f),
                    S("stout", "Dawi Constitution", "+25 maximum health.", "HealthMax", 25f),
                    S("native", "Master Rune of Adamant", "10% Ward Save and 50% extra shield HP.", "dw_master_rune_adamant", 0f)),

                D("Slayer", "Oath-Axe of Karak Kadrin", BaseKind.GreatAxe, "Weapon", dwarf, new[]{"axe","slayer","karak","kadrin"},
                    K("core", "Slayer's Oath", "+25 Two Handed.", "TwoHanded", 25f),
                    A("doom", "Seek a Worthy Doom", "+18% physical damage.", "Physical", 0.18f),
                    S("frenzy", "Deathblow", "+12% swing speed.", "SwingSpeed", 12f),
                    S("native", "Rune of Beastslaying", "500% damage against mounts and large enemies.", "dw_rune_beastslaying", 0f)),

                D("Runelord", "Anvil-Hammer of Thungni", BaseKind.Hammer, "Weapon", dwarf, new[]{"hammer","rune","anvil","runelord","thungni"},
                    R("core", "Master Rune of Warding", "20% resistance to magical damage.", "Magical", 0.20f),
                    S("power", "Runic Reservoir", "+0.25 career-resource generation.", "CustomResourceGain", 0.25f),
                    S("ancestry", "Ancestral Endurance", "+30 maximum health.", "HealthMax", 30f),
                    S("native", "Master Rune of Skalf Blackhammer", "50% extra physical damage and 50% extra magic damage.", "dw_master_rune_skalf", 0f)),

                D("OrcBoss", "Ulag's Akrit Axe", BaseKind.Axe, "Weapon", orc, new[]{"axe","choppa","orc","boss"},
                    A("core", "Akk'rit Edge", "+15% physical damage.", "Physical", 0.15f),
                    S("cleave", "Right Proper Choppa", "+1 cleave.", "Cleave", 1f),
                    S("big", "Da Biggest Boss", "+30 maximum health.", "HealthMax", 30f),
                    S("native", "Wallopin' Great Krunch", "40 extra physical damage; weapon gains the Cleave trait.", "gs_enchant_wallopin_krunch", 0f)),

                D("OrcShaman", "Staff of Baduum", BaseKind.Staff, "Weapon", orc, new[]{"staff","shaman","orc","waaagh","baduum"},
                    S("core", "WAAAGH! Reservoir", "+35 maximum Winds of Magic.", "WindsOfMagicMax", 35f),
                    S("green", "Green Conduit", "+0.25 Winds of Magic recharge.", "WindsOfMagicRegen", 0.25f),
                    S("loud", "Louder Is Better", "+20% spell radius.", "SpellRadius", 20f),
                    S("native", "Shadow uv da Bad Moon", "15 extra magic damage; enemy wizards damaging you with spells lose 30 Winds of Magic.", "gs_enchant_shadow_bad_moon", 0f))
            };
        }

        private static Dictionary<string, CareerItemDefinition> BuildDefinitionMap()
        {
            Dictionary<string, CareerItemDefinition> result = new Dictionary<string, CareerItemDefinition>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < Definitions.Length; i++)
                result.Add(Definitions[i].CareerId, Definitions[i]);
            return result;
        }

        private static CareerItemDefinition D(string careerId, string itemName, BaseKind kind, string validItemType,
            string[] factionTokens, string[] preferredTokens, params TraitDefinition[] traits)
        {
            string prefix = ModPrefix + careerId.ToLowerInvariant() + "_";
            for (int i = 0; i < traits.Length; i++)
                traits[i].Id = prefix + traits[i].Id;

            // Slot 4 is intentionally a real TOR enchantment/blessing/rune.
            // Keep its native ItemTraitStringId so TOR executes the original scripted effect.
            if (traits.Length > 3)
                traits[3].Id = traits[3].EffectType;

            return new CareerItemDefinition
            {
                CareerId = careerId,
                ItemName = itemName,
                Kind = kind,
                ValidItemType = validItemType,
                FactionTokens = factionTokens,
                PreferredTokens = preferredTokens,
                Traits = traits
            };
        }

        private static TraitDefinition S(string id, string name, string description, string statType, float value)
        {
            return new TraitDefinition { Id = id, Name = name, Description = description, Kind = TraitKind.Stat,
                EffectType = statType, Value = value, IconName = "traits_magic_icon" };
        }

        private static TraitDefinition K(string id, string name, string description, string skillId, float value)
        {
            return new TraitDefinition { Id = id, Name = name, Description = description, Kind = TraitKind.Stat,
                EffectType = "Skill", SkillId = skillId, Value = value, IconName = "traits_magic_icon" };
        }

        private static TraitDefinition A(string id, string name, string description, string damageType, float value)
        {
            return new TraitDefinition { Id = id, Name = name, Description = description, Kind = TraitKind.Amplifier,
                EffectType = damageType, Value = value, IconName = DamageIcon(damageType) };
        }

        private static TraitDefinition R(string id, string name, string description, string damageType, float value)
        {
            return new TraitDefinition { Id = id, Name = name, Description = description, Kind = TraitKind.Resistance,
                EffectType = damageType, Value = value, IconName = DamageIcon(damageType) };
        }

        private static TraitDefinition X(string id, string name, string description, string damageType, float value)
        {
            return new TraitDefinition { Id = id, Name = name, Description = description, Kind = TraitKind.AdditionalDamage,
                EffectType = damageType, Value = value, IconName = DamageIcon(damageType) };
        }

        private static string DamageIcon(string damageType)
        {
            switch (damageType)
            {
                case "Fire": return "traits_fire_icon";
                case "Holy": return "traits_holy_icon";
                case "Lightning": return "traits_lightning_icon";
                case "Frost": return "traits_frost_icon";
                default: return "traits_magic_icon";
            }
        }

        private static Type TypeByName(string fullName)
        {
            if (String.IsNullOrEmpty(fullName))
                return null;

            Type direct = Type.GetType(fullName, false);
            if (direct != null)
                return direct;

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type found = assemblies[i].GetType(fullName, false);
                if (found != null)
                    return found;
            }
            return null;
        }

        private static object GetStaticProperty(Type type, string name)
        {
            if (type == null)
                return null;
            PropertyInfo property = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            return property == null ? null : property.GetValue(null, null);
        }

        private static object GetProperty(object instance, string name)
        {
            if (instance == null)
                return null;

            Type type = instance.GetType();
            while (type != null)
            {
                PropertyInfo property = type.GetProperty(
                    name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (property != null)
                    return property.GetValue(instance, null);
                type = type.BaseType;
            }
            return null;
        }

        private static void SetProperty(object instance, string name, object value)
        {
            PropertyInfo property = instance.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (property == null)
                throw new MissingMemberException(instance.GetType().FullName, name);
            property.SetValue(instance, ConvertValue(value, property.PropertyType), null);
        }

        private static void SetEnumProperty(object instance, string name, string enumName)
        {
            PropertyInfo property = instance.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (property == null)
                throw new MissingMemberException(instance.GetType().FullName, name);
            property.SetValue(instance, Enum.Parse(property.PropertyType, enumName, false), null);
        }

        private static object GetField(object instance, string name)
        {
            if (instance == null)
                return null;
            FieldInfo field = instance.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return field == null ? null : field.GetValue(instance);
        }

        private static void SetField(object instance, string name, object value)
        {
            FieldInfo field = instance.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null)
                throw new MissingMemberException(instance.GetType().FullName, name);
            field.SetValue(instance, ConvertValue(value, field.FieldType));
        }

        private static void SetEnumField(object instance, string name, string enumName)
        {
            FieldInfo field = instance.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null)
                throw new MissingMemberException(instance.GetType().FullName, name);
            field.SetValue(instance, Enum.Parse(field.FieldType, enumName, false));
        }

        private static object ConvertValue(object value, Type targetType)
        {
            if (value == null)
                return null;
            if (targetType.IsInstanceOfType(value))
                return value;
            return Convert.ChangeType(value, targetType);
        }

        private static MethodInfo FindStaticMethod(Type type, string name, int parameterCount)
        {
            if (type == null)
                return null;
            MethodInfo[] methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            for (int i = 0; i < methods.Length; i++)
                if (methods[i].Name == name && methods[i].GetParameters().Length == parameterCount) return methods[i];
            return null;
        }

        private static MethodInfo FindInstanceMethod(Type type, string name, Type[] parameterTypes)
        {
            if (type == null || parameterTypes == null)
                return null;
            MethodInfo[] methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            for (int i = 0; i < methods.Length; i++)
            {
                if (methods[i].Name != name)
                    continue;
                ParameterInfo[] parameters = methods[i].GetParameters();
                if (parameters.Length != parameterTypes.Length)
                    continue;
                bool matches = true;
                for (int j = 0; j < parameters.Length; j++)
                {
                    if (parameterTypes[j] == null || parameters[j].ParameterType != parameterTypes[j])
                    {
                        matches = false;
                        break;
                    }
                }
                if (matches)
                    return methods[i];
            }
            return null;
        }

        private static MethodInfo FindStaticCompatibleMethod(Type type, string name, Type parameterType)
        {
            MethodInfo[] methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            for (int i = 0; i < methods.Length; i++)
            {
                ParameterInfo[] parameters = methods[i].GetParameters();
                if (methods[i].Name == name && parameters.Length == 1 && parameters[0].ParameterType.IsAssignableFrom(parameterType))
                    return methods[i];
            }
            return null;
        }

        private static object GetItemTypeValue(object item)
        {
            object value = GetProperty(item, "Type");
            if (value != null)
                return value;

            // Compatibility fallback for older test doubles or external wrappers.
            return GetProperty(item, "ItemType");
        }

        private static int EnumNumber(object value)
        {
            if (value == null)
                return -1;
            try { return Convert.ToInt32(value); }
            catch { return -1; }
        }

        private static bool ToBoolean(object value)
        {
            if (value == null)
                return false;
            try { return Convert.ToBoolean(value); }
            catch { return false; }
        }



        private static void Log(string message)
        {
            ModLog.Info(message);
        }

        private static void LogOnce(string key, string message)
        {
            if (LoggedErrors.Add(key))
                Log(message);
        }

        private static string FormatException(Exception ex)
        {
            TargetInvocationException tie = ex as TargetInvocationException;
            if (tie != null && tie.InnerException != null)
                ex = tie.InnerException;
            return ex.GetType().FullName + ": " + ex.Message + Environment.NewLine + ex.StackTrace;
        }
        private sealed class LootCandidate
        {
            public object Item;
            public int Score;
            public string Name;
            public string StringId;
            public string Key;
            public string Category;
        }
    }

    internal enum BaseKind { Staff, Lance, Sword, Hammer, Bow, Shield, Axe, GreatAxe, Spear }
    internal enum TraitKind { Stat, Amplifier, Resistance, AdditionalDamage }

    internal sealed class CareerItemDefinition
    {
        public string CareerId;
        public string ItemName;
        public BaseKind Kind;
        public string ValidItemType;
        public string[] FactionTokens;
        public string[] PreferredTokens;
        public TraitDefinition[] Traits;
        public string SignatureTraitId { get { return Traits[0].Id; } }
    }

    internal sealed class TraitDefinition
    {
        public string Id;
        public string Name;
        public string Description;
        public TraitKind Kind;
        public string EffectType;
        public string SkillId;
        public float Value;
        public string IconName;
    }

    internal sealed class ItemDescriptor
    {
        public bool IsUsable;
        public bool HasWeapon;
        public bool NotMerchandise;
        public int ItemTypeNumber;
        public int Tier;
        public int Value;
        public int DamageScore;
        public string StringId;
        public string SearchText;
        public string WeaponClass;
    }
}
