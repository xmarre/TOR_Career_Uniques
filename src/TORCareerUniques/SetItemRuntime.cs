
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace TORCareerUniques
{
    internal static partial class SetItemRuntime
    {
        private const string RealPiecePrefix = "torcu_set_";
        private const string BonusPrefix = "torcu_setbonus_";
        private const string RoutedPrefix = "torcu_routed_";
        private const string DisplayPrefix = "torcu_setdisplay_";
        private const string AdminPrefix = "torcu_admin_";
        private const float CasterArmorWeightCap = 11f;
        private const float CasterPreferredArmorWeight = 7.5f;

        private static readonly SetDefinition[] Definitions = BuildDefinitions();
        private static readonly Dictionary<string, SetDefinition> DefinitionByCareer = BuildDefinitionMap();
        private static readonly Dictionary<string, PieceSignature> SignatureByTraitId = BuildSignatureMap();
        private static readonly HashSet<string> LoggedErrors = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, List<string>> BaseTraitsByItemId =
            new Dictionary<string, List<string>>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> AppliedBonusKeyByItemId =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, VisualProfile> VisualProfileByCareer =
            BuildVisualProfiles();
        private static readonly Dictionary<string, object> VisualSourceByCareer =
            new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, object> VisualItemByCareerSlot =
            new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> VisualOutfitSignatureOwner =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, HashSet<string>> VisualArchetypeItemIdsByCareer =
            new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, HashSet<string>> VisualCultureItemIdsByCareer =
            new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Dictionary<string, int>> VisualEquipmentPairCountsByCareer =
            new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> VisualOutfitResolutionAttempted =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static object _visualResolverSession;
        private static readonly Dictionary<string, SetItemInstance> KnownSetItemsById =
            new Dictionary<string, SetItemInstance>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> DescriptionKeyByItemId =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> DisplayStateKeyByCareer =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> MigratedVisualBaseByItemId =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly HashSet<string> VisualMigrationAttemptedItemIds =
            new HashSet<string>(StringComparer.Ordinal);

        private static bool _initialized;
        private static bool _busy;
        private static object _lastMainHero;
        private static int _lastCraftedItemCount = -1;
        private static bool _visualAuditAttempted;
        private static int _visualAuditRetryDelay;
        private static string _lastVisualAuditFailureKey;
        private static object _traitsInjectedManager;
        private static bool _visualMigrationPassCompleted;

        internal static void Initialize()
        {
            if (_initialized)
                return;
            _initialized = true;
            ModLog.Info("Loaded career-set runtime. Sets: " + Definitions.Length +
                "; armour pieces: " + CountArmorPieces() + ".");
        }

        internal static void Tick()
        {
            if (_busy)
                return;

            _busy = true;
            try
            {
                EnsureTraitsInjected();
                Dictionary<string, EquippedSetState> equipped = ScanEquippedSetState();
                RefreshEquippedBonuses(equipped);
                RefreshSetDescriptions(equipped);
                // Generic runtime/UI refreshes are deliberately limited to equipped-state
                // work. They do not enumerate the crafted-item dictionary and never enter
                // visual CharacterObject/ItemObject resolution. Known-item indexing and
                // existing-save visual migration are explicit one-shot/action paths only.
            }
            catch (Exception ex)
            {
                LogOnce("set-tick:" + ex.GetType().FullName + ":" + ex.Message,
                    "Career-set runtime failed: " + FormatException(ex));
            }
            finally
            {
                _busy = false;
            }
        }

        internal static string[] GetCareerIds()
        {
            string[] ids = new string[Definitions.Length];
            for (int i = 0; i < Definitions.Length; i++)
                ids[i] = Definitions[i].CareerId;
            return ids;
        }

        internal static string[] GetCareerChoiceLabels()
        {
            string[] labels = new string[Definitions.Length];
            for (int i = 0; i < Definitions.Length; i++)
                labels[i] = Definitions[i].CareerId + " — " + Definitions[i].SetName;
            return labels;
        }

        internal static string[] GetCareerChoiceLabelsFor(string[] careerIds)
        {
            if (careerIds == null)
                return new string[0];

            string[] labels = new string[careerIds.Length];
            for (int i = 0; i < careerIds.Length; i++)
            {
                SetDefinition definition;
                string careerId = careerIds[i] ?? String.Empty;
                if (!DefinitionByCareer.TryGetValue(careerId, out definition))
                    throw new ArgumentException("Unknown career id in admin selector: " + careerId);
                labels[i] = definition.CareerId + " — " + definition.SetName;
            }
            return labels;
        }

        internal static string ResolveCareerChoice(string selection)
        {
            if (String.IsNullOrWhiteSpace(selection))
                return null;

            string trimmed = selection.Trim();
            SetDefinition direct;
            if (DefinitionByCareer.TryGetValue(trimmed, out direct))
                return direct.CareerId;

            for (int i = 0; i < Definitions.Length; i++)
            {
                string prefix = Definitions[i].CareerId + " — ";
                if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return Definitions[i].CareerId;
            }
            return null;
        }

        internal static string GetSetName(string careerId)
        {
            SetDefinition definition;
            return DefinitionByCareer.TryGetValue(careerId ?? String.Empty, out definition)
                ? definition.SetName
                : careerId;
        }

        internal static int GetRecoveredCount(string careerId)
        {
            SetDefinition definition;
            if (!DefinitionByCareer.TryGetValue(careerId ?? String.Empty, out definition))
                return 0;

            int discovered = AdminBridge.GetDiscoveredSetPieceCount(definition.CareerId);
            if (discovered >= 0)
                return discovered;

            object artisan = CareerUniqueRuntime.GetArtisanBehavior();
            if (artisan == null)
                return 0;

            int count = 0;
            for (int pieceIndex = 0; pieceIndex < 5; pieceIndex++)
                if (CareerUniqueRuntime.HasClaimed(artisan, GetRealSignature(definition, pieceIndex)))
                    count++;
            return count;
        }

        internal static bool IsSetComplete(string careerId)
        {
            return GetRecoveredCount(careerId) >= 5;
        }

        internal static int GetEquippedRealSetPieceCount(string careerId)
        {
            SetDefinition definition;
            if (!DefinitionByCareer.TryGetValue(careerId ?? String.Empty,
                out definition))
                return 0;
            object hero = GetMainHeroIfReady();
            object equipment = GetProperty(hero, "BattleEquipment");
            if (equipment == null)
                return 0;

            HashSet<int> pieces = new HashSet<int>();
            foreach (object element in EnumerateEquipmentElements(equipment))
            {
                object item = GetProperty(element, "Item");
                string itemId = Convert.ToString(GetProperty(item, "StringId"));
                if (String.IsNullOrEmpty(itemId))
                    continue;
                PieceSignature signature = FindPieceSignatureForItem(item, itemId);
                if (signature == null ||
                    !Object.ReferenceEquals(signature.Definition, definition))
                    continue;
                IList traits = GetItemTraits(itemId);
                if (ContainsTraitId(traits,
                    GetRealSignature(definition, signature.PieceIndex)))
                    pieces.Add(signature.PieceIndex);
            }
            return pieces.Count;
        }

        internal static bool MigrateLegacyDiscoveryClaims()
        {
            object artisan = CareerUniqueRuntime.GetArtisanBehavior();
            if (artisan == null)
                return false;

            int added = 0;
            for (int d = 0; d < Definitions.Length; d++)
            {
                SetDefinition definition = Definitions[d];
                for (int pieceIndex = 0; pieceIndex < 5; pieceIndex++)
                {
                    if (!CareerUniqueRuntime.HasClaimed(artisan,
                        GetRealSignature(definition, pieceIndex)))
                        continue;
                    if (AdminBridge.RecordDiscoveredSetPiece(
                        definition.CareerId, pieceIndex))
                        added++;
                }
            }

            ModLog.Info("Legacy set-acquisition state migrated into the persistent discovery ledger. Newly recorded pieces: " + added + ".");
            return true;
        }

        internal static int NormalizeEncounterHeroLootRoster(ItemRoster roster)
        {
            if (roster == null)
                return 0;
            string error;
            if (!EnsureReady(out error))
            {
                ModLog.Error("Encounter-hero loot normalization could not start: " + error);
                return 0;
            }
            return NormalizeItemRoster(roster, false);
        }

        internal static bool MigratePlayerOwnedItemsAndDiscovery(
            bool includeSettlementInventories)
        {
            string error;
            if (!EnsureReady(out error))
            {
                ModLog.Error("Player-owned set-item migration could not start: " + error);
                return false;
            }

            MobileParty mainParty = MobileParty.MainParty;
            if (mainParty != null && mainParty.ItemRoster != null)
            {
                NormalizeItemRoster(mainParty.ItemRoster, true);
                RecordDiscoveryFromRoster(mainParty.ItemRoster);
            }

            NormalizePlayerClanHeroEquipment();

            if (includeSettlementInventories && Clan.PlayerClan != null)
            {
                foreach (Settlement settlement in Settlement.All)
                {
                    if (settlement == null || settlement.OwnerClan != Clan.PlayerClan ||
                        settlement.ItemRoster == null)
                        continue;
                    NormalizeItemRoster(settlement.ItemRoster, true);
                    RecordDiscoveryFromRoster(settlement.ItemRoster);
                }
            }

            RefreshRuntimeNow();
            return true;
        }

        internal static void ProcessPlayerInventoryAcquisitions(
            List<ValueTuple<ItemRosterElement, int>> boughtItems)
        {
            if (boughtItems == null || boughtItems.Count == 0)
                return;

            bool needsSetNormalization = false;
            bool discoveryChanged = false;
            for (int i = 0; i < boughtItems.Count; i++)
            {
                ValueTuple<ItemRosterElement, int> transaction = boughtItems[i];
                // Bannerlord's tuple is (roster element, total transaction price),
                // not (roster element, quantity). Loot transfers normally have a
                // zero price; the accepted quantity lives on ItemRosterElement.
                if (transaction.Item1.Amount <= 0)
                    continue;
                ItemObject item = transaction.Item1.EquipmentElement.Item;
                if (item == null)
                    continue;

                IList traits = GetItemTraits(item.StringId);
                PieceSignature signature = FindPieceSignature(traits);
                if (HasHeroSignature(traits) ||
                    (signature != null && NeedsNativeIntrinsicMigration(traits,
                        signature)))
                {
                    needsSetNormalization = true;
                    continue;
                }
                if (HasAdminSignature(traits))
                    continue;

                signature = signature ?? FindPieceSignatureForItem(item,
                    item.StringId);
                if (signature != null && AdminBridge.RecordDiscoveredSetPiece(
                    signature.Definition.CareerId, signature.PieceIndex))
                    discoveryChanged = true;
            }

            if (needsSetNormalization && MobileParty.MainParty != null &&
                MobileParty.MainParty.ItemRoster != null)
            {
                if (NormalizeItemRoster(MobileParty.MainParty.ItemRoster, true) > 0)
                    discoveryChanged = true;
            }

            if (discoveryChanged || needsSetNormalization)
                RefreshRuntimeNow();
        }

        private static int NormalizeItemRoster(ItemRoster roster,
            bool recordDiscovery)
        {
            List<ItemRosterElement> snapshot = new List<ItemRosterElement>();
            foreach (ItemRosterElement element in roster)
                snapshot.Add(element);

            Dictionary<string, ItemObject> canonicalByHeroItemId =
                new Dictionary<string, ItemObject>(StringComparer.Ordinal);
            int normalized = 0;
            for (int i = 0; i < snapshot.Count; i++)
            {
                ItemRosterElement rosterElement = snapshot[i];
                EquipmentElement oldElement = rosterElement.EquipmentElement;
                ItemObject oldItem = oldElement.Item;
                if (oldItem == null || rosterElement.Amount <= 0)
                    continue;

                IList traits = GetItemTraits(oldItem.StringId);
                PieceSignature signature = FindPieceSignature(traits);
                if (signature == null)
                    continue;

                bool isAdmin = HasAdminSignature(traits);
                bool isHero = HasHeroSignature(traits);
                if (!isHero && !NeedsNativeIntrinsicMigration(traits, signature))
                    continue;

                ItemObject canonical;
                if (!canonicalByHeroItemId.TryGetValue(oldItem.StringId,
                    out canonical))
                {
                    object created;
                    string createError;
                    bool createdOk = isAdmin ?
                        TryCreateAdminPiece(signature, out created, out createError) :
                        TryCreateCanonicalPiece(signature, out created, out createError);
                    if (!createdOk)
                    {
                        ModLog.Error("Could not normalize set item '" +
                            oldItem.StringId + "': " + createError);
                        continue;
                    }
                    canonical = created as ItemObject;
                    if (canonical == null)
                        continue;
                    canonicalByHeroItemId[oldItem.StringId] = canonical;
                }

                EquipmentElement replacement = new EquipmentElement(canonical,
                    oldElement.ItemModifier, null, false);
                roster.AddToCounts(replacement, rosterElement.Amount);
                try
                {
                    roster.AddToCounts(oldElement, -rosterElement.Amount);
                }
                catch
                {
                    roster.AddToCounts(replacement, -rosterElement.Amount);
                    throw;
                }
                normalized++;

                if (recordDiscovery && !isAdmin)
                    AdminBridge.RecordDiscoveredSetPiece(
                        signature.Definition.CareerId, signature.PieceIndex);
            }
            return normalized;
        }

        private static void RecordDiscoveryFromRoster(ItemRoster roster)
        {
            foreach (ItemRosterElement rosterElement in roster)
            {
                ItemObject item = rosterElement.EquipmentElement.Item;
                if (item != null && rosterElement.Amount > 0)
                    RecordDiscoveryFromCanonicalItem(item);
            }
        }

        private static void NormalizePlayerClanHeroEquipment()
        {
            HashSet<object> visited = new HashSet<object>();
            Hero mainHero = Hero.MainHero;
            if (mainHero != null)
            {
                visited.Add(mainHero);
                NormalizeEquipmentAndRecord(GetProperty(mainHero,
                    "BattleEquipment"));
                NormalizeEquipmentAndRecord(GetProperty(mainHero,
                    "CivilianEquipment"));
            }

            Clan playerClan = Clan.PlayerClan;
            foreach (object hero in EnumeratePlayerClanHeroes(playerClan))
            {
                if (hero == null || visited.Contains(hero))
                    continue;
                visited.Add(hero);
                NormalizeEquipmentAndRecord(GetProperty(hero,
                    "BattleEquipment"));
                NormalizeEquipmentAndRecord(GetProperty(hero,
                    "CivilianEquipment"));
            }
        }

        private static void NormalizeEquipmentAndRecord(object equipment)
        {
            if (equipment == null)
                return;

            Type indexType = TypeByName("TaleWorlds.Core.EquipmentIndex");
            Type elementType = TypeByName("TaleWorlds.Core.EquipmentElement");
            if (indexType == null || elementType == null)
                return;

            MethodInfo getter = equipment.GetType().GetMethod("get_Item",
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance, null, new[] { indexType }, null);
            MethodInfo setter = equipment.GetType().GetMethod(
                "AddEquipmentToSlotWithoutAgent",
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance, null,
                new[] { indexType, elementType }, null);
            if (getter == null || setter == null)
                return;

            string[] slotNames = { "Weapon0", "Weapon1", "Weapon2", "Weapon3",
                "Head", "Body", "Leg", "Gloves", "Cape", "Horse",
                "HorseHarness" };
            for (int i = 0; i < slotNames.Length; i++)
            {
                object index = Enum.Parse(indexType, slotNames[i], true);
                object boxedElement = getter.Invoke(equipment,
                    new object[] { index });
                object item = GetProperty(boxedElement, "Item");
                if (item == null)
                    continue;

                IList traits = GetItemTraits(Convert.ToString(
                    GetProperty(item, "StringId")));
                PieceSignature signature = FindPieceSignature(traits);
                bool isAdmin = HasAdminSignature(traits);
                bool needsNormalization = signature != null &&
                    (HasHeroSignature(traits) ||
                    NeedsNativeIntrinsicMigration(traits, signature));
                if (needsNormalization)
                {
                    object canonical;
                    string createError;
                    bool createdOk = isAdmin ?
                        TryCreateAdminPiece(signature, out canonical, out createError) :
                        TryCreateCanonicalPiece(signature, out canonical, out createError);
                    if (createdOk)
                    {
                        object modifier = GetProperty(boxedElement,
                            "ItemModifier");
                        object replacement = CreateEquipmentElement(canonical,
                            modifier);
                        if (replacement != null)
                        {
                            setter.Invoke(equipment, new[] { index, replacement });
                            item = canonical;
                            if (!isAdmin)
                                AdminBridge.RecordDiscoveredSetPiece(
                                    signature.Definition.CareerId,
                                    signature.PieceIndex);
                        }
                    }
                }

                RecordDiscoveryFromCanonicalItem(item);
            }
        }

        private static void RecordDiscoveryFromCanonicalItem(object item)
        {
            string itemId = Convert.ToString(GetProperty(item, "StringId"));
            IList traits = GetItemTraits(itemId);
            if (HasAdminSignature(traits) || HasHeroSignature(traits))
                return;
            PieceSignature signature = FindPieceSignatureForItem(item, itemId);
            if (signature != null)
                AdminBridge.RecordDiscoveredSetPiece(
                    signature.Definition.CareerId, signature.PieceIndex);
        }

        private static bool TryCreateCanonicalPiece(PieceSignature signature,
            out object created, out string error)
        {
            return TryCreatePieceVersion(signature, false, out created, out error);
        }

        private static bool TryCreateAdminPiece(PieceSignature signature,
            out object created, out string error)
        {
            return TryCreatePieceVersion(signature, true, out created, out error);
        }

        private static bool TryCreatePieceVersion(PieceSignature signature,
            bool adminCopy, out object created, out string error)
        {
            created = null;
            error = null;
            if (signature == null || signature.Definition == null)
            {
                error = "The set-piece signature is unavailable.";
                return false;
            }

            object baseItem;
            string itemName;
            List<string> traits;
            SetSlot? expectedSlot = null;
            if (signature.PieceIndex == 0)
            {
                CareerItemDefinition relic = CareerUniqueRuntime.GetDefinitionForSet(
                    signature.Definition.CareerId);
                if (relic == null)
                {
                    error = "The canonical relic definition is unavailable.";
                    return false;
                }
                baseItem = CareerUniqueRuntime.FindBaseItem(relic);
                itemName = adminCopy ? "[ADMIN COPY] " + relic.ItemName : relic.ItemName;
                traits = adminCopy ? GetAdminRelicTraitIds(signature.Definition, relic) :
                    new List<string>();
                if (!adminCopy)
                {
                    for (int i = 0; i < relic.Traits.Length; i++)
                        traits.Add(relic.Traits[i].Id);
                }
            }
            else
            {
                SetPieceDefinition piece = signature.Definition.Pieces[
                    signature.PieceIndex - 1];
                baseItem = FindArmorBaseItem(signature.Definition, piece);
                itemName = adminCopy ? "[ADMIN COPY] " + piece.ItemName : piece.ItemName;
                traits = adminCopy ? GetAdminPieceTraitIds(signature.Definition,
                    signature.PieceIndex, piece) : GetRealPieceTraitIds(piece);
                expectedSlot = piece.Slot;
            }

            if (baseItem == null)
            {
                error = "No canonical base item was resolved for " + itemName + ".";
                return false;
            }

            Type helperType = TypeByName(
                "TOR_Core.CampaignMechanics.Crafting.EnchantmentHelper");
            MethodInfo create = FindStaticMethod(helperType,
                "CreateEnchantedItem", 5);
            if (create == null)
            {
                error = "ToR's enchanted-item factory is unavailable.";
                return false;
            }

            IList reflectionTraits = new List<string>(traits);
            created = create.Invoke(null, new object[] { baseItem,
                reflectionTraits, itemName, false, null });
            if (created == null)
            {
                error = "ToR returned null while creating " + itemName + ".";
                return false;
            }
            if (expectedSlot.HasValue && !IsExactSlotItem(created,
                expectedSlot.Value))
            {
                error = "The normalized item was created in an incompatible equipment slot.";
                created = null;
                return false;
            }
            EnsureCraftedItemRecorded(baseItem, created, itemName,
                reflectionTraits);
            return true;
        }

        private static bool NeedsNativeIntrinsicMigration(IList traits,
            PieceSignature signature)
        {
            string expected = GetExpectedNativeIntrinsicTraitId(signature);
            if (String.IsNullOrEmpty(expected))
                return false;
            return !ContainsTraitId(traits, expected);
        }

        private static string GetExpectedNativeIntrinsicTraitId(
            PieceSignature signature)
        {
            if (signature == null || signature.Definition == null)
                return null;
            if (signature.PieceIndex == 0)
            {
                CareerItemDefinition relic = CareerUniqueRuntime.GetDefinitionForSet(
                    signature.Definition.CareerId);
                return relic != null && relic.Traits != null && relic.Traits.Length > 3 ?
                    relic.Traits[3].Id : null;
            }

            int pieceIndex = signature.PieceIndex - 1;
            if (pieceIndex < 0 || pieceIndex >= signature.Definition.Pieces.Length)
                return null;
            TraitDefinition[] effects = signature.Definition.Pieces[pieceIndex].Effects;
            return effects != null && effects.Length > 1 ? effects[1].Id : null;
        }

        private static bool ContainsTraitId(IList traits, string expectedId)
        {
            if (traits == null || String.IsNullOrEmpty(expectedId))
                return false;
            for (int i = 0; i < traits.Count; i++)
            {
                if (String.Equals(Convert.ToString(traits[i]), expectedId,
                    StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static object CreateEquipmentElement(object item,
            object modifier)
        {
            Type itemType = TypeByName("TaleWorlds.Core.ItemObject");
            Type modifierType = TypeByName("TaleWorlds.Core.ItemModifier");
            Type elementType = TypeByName("TaleWorlds.Core.EquipmentElement");
            ConstructorInfo constructor = elementType == null ? null :
                elementType.GetConstructor(BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.Instance, null,
                new[] { itemType, modifierType, itemType, typeof(bool) }, null);
            return constructor == null ? null : constructor.Invoke(
                new[] { item, modifier, null, (object)false });
        }

        private static bool HasHeroSignature(IList traits)
        {
            if (traits == null)
                return false;
            for (int i = 0; i < traits.Count; i++)
            {
                string id = Convert.ToString(traits[i]);
                if (!String.IsNullOrEmpty(id) && id.StartsWith(HeroPrefix,
                    StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        internal static bool TryGrantRandomRewardPiece(string careerId, int seed,
            out string itemName, out bool advancedDiscovery, out string error)
        {
            itemName = null;
            advancedDiscovery = false;
            error = null;

            SetDefinition definition;
            if (!DefinitionByCareer.TryGetValue(careerId ?? String.Empty, out definition))
            {
                error = "Unknown career id '" + careerId + "'.";
                return false;
            }

            if (!EnsureReady(out error))
                return false;

            List<int> remaining = new List<int>();
            for (int pieceIndex = 0; pieceIndex < 5; pieceIndex++)
            {
                if (!AdminBridge.HasDiscoveredSetPiece(definition.CareerId, pieceIndex))
                    remaining.Add(pieceIndex);
            }

            Random random = new Random(seed);
            int selected = remaining.Count > 0
                ? remaining[random.Next(remaining.Count)]
                : random.Next(5);
            if (selected == 0)
            {
                bool relicGranted = CareerUniqueRuntime.TryGrantCareerItemWithLootModifier(
                    careerId, out itemName, out error);
                if (relicGranted)
                {
                    advancedDiscovery = AdminBridge.RecordDiscoveredSetPiece(
                        definition.CareerId, selected);
                    RefreshRuntimeNow();
                }
                return relicGranted;
            }

            SetPieceDefinition piece = definition.Pieces[selected - 1];
            itemName = piece.ItemName;
            try
            {
                PrepareVisualResolutionForExplicitAction(definition);
                object baseItem = FindArmorBaseItem(definition, piece);
                if (baseItem == null)
                {
                    error = "No suitable " + piece.Slot + " base item was found for " +
                        definition.CareerId + ".";
                    return false;
                }

                List<string> traits = GetRealPieceTraitIds(piece);
                object created;
                string modifiedName;
                if (!CreateAndGrantWithLootModifier(baseItem, itemName, traits,
                    piece.Slot, out created, out modifiedName, out error))
                    return false;

                itemName = modifiedName;
                advancedDiscovery = AdminBridge.RecordDiscoveredSetPiece(
                    definition.CareerId, selected);

                ModLog.Info("Granted set piece '" + itemName + "' for " + definition.CareerId +
                    " using " + Convert.ToString(GetProperty(baseItem, "StringId")) + ".");
                RefreshRuntimeNow();
                return true;
            }
            catch (Exception ex)
            {
                error = FormatException(ex);
                ModLog.Error("Set-piece grant failed for " + careerId + ": " + error);
                return false;
            }
        }

        internal static bool TryGrantAdminSet(string careerId, out string result, out string error)
        {
            result = null;
            error = null;
            string requestedCareer = careerId ?? "<null>";
            ModLog.Info("Admin full-set grant started for " + requestedCareer + ".");

            try
            {
                SetDefinition definition;
                if (!DefinitionByCareer.TryGetValue(careerId ?? String.Empty, out definition))
                    return FailAdminGrant(requestedCareer, "career lookup",
                        "Unknown career id '" + careerId + "'.", out error);

                string readinessError;
                if (!EnsureReady(out readinessError))
                    return FailAdminGrant(definition.CareerId, "runtime readiness",
                        readinessError, out error);

                // An earlier one-shot migration may have attempted this career while the
                // live TOR object catalogue was still incomplete. Explicit user/admin
                // actions are allowed one fresh resolution attempt when the cached outfit
                // is incomplete. This never runs merely because MCM/options is open.
                PrepareVisualResolutionForExplicitAction(definition);

                object artisan = CareerUniqueRuntime.GetArtisanBehavior();
                bool[] acquisitionBefore = CaptureRealAcquisitionState(artisan, definition);
                List<GrantPlan> plans = new List<GrantPlan>();

                CareerItemDefinition relic = CareerUniqueRuntime.GetDefinitionForSet(careerId);
                if (relic == null)
                    return FailAdminGrant(definition.CareerId, "relic definition",
                        "The career relic definition is unavailable.", out error);

                object relicBase = CareerUniqueRuntime.FindBaseItem(relic);
                if (relicBase == null)
                    return FailAdminGrant(definition.CareerId, "relic base-item preflight",
                        "No suitable base item was found for the career relic.", out error);

                plans.Add(new GrantPlan
                {
                    BaseItem = relicBase,
                    ItemName = "[ADMIN COPY] " + relic.ItemName,
                    TraitIds = GetAdminRelicTraitIds(definition, relic),
                    ExpectedSlot = null
                });

                for (int i = 0; i < definition.Pieces.Length; i++)
                {
                    SetPieceDefinition piece = definition.Pieces[i];
                    object baseItem = FindArmorBaseItem(definition, piece);
                    if (baseItem == null)
                    {
                        return FailAdminGrant(definition.CareerId,
                            "armour base-item preflight for " + piece.Slot,
                            "No suitable " + piece.Slot + " base item was found for " +
                            piece.ItemName + ". Nothing was granted.", out error);
                    }

                    plans.Add(new GrantPlan
                    {
                        BaseItem = baseItem,
                        ItemName = "[ADMIN COPY] " + piece.ItemName,
                        TraitIds = GetAdminPieceTraitIds(definition, i + 1, piece),
                        ExpectedSlot = piece.Slot
                    });
                }

                List<string> preflight = new List<string>();
                for (int i = 0; i < plans.Count; i++)
                {
                    preflight.Add((i + 1) + ":" + plans[i].ItemName + " <- " +
                        (Convert.ToString(GetProperty(plans[i].BaseItem, "StringId")) ?? "<no-id>"));
                }
                ModLog.Info("Admin full-set preflight resolved five base items for " +
                    definition.CareerId + ": " + String.Join("; ", preflight.ToArray()) + ".");

                int granted = 0;
                for (int i = 0; i < plans.Count; i++)
                {
                    object created;
                    string grantError;
                    ModLog.Info("Admin grant creating item " + (i + 1) + "/" + plans.Count +
                        " for " + definition.CareerId + ": " + plans[i].ItemName + ".");
                    if (!CreateAndGrant(plans[i].BaseItem, plans[i].ItemName,
                        plans[i].TraitIds, plans[i].ExpectedSlot,
                        out created, out grantError))
                    {
                        return FailAdminGrant(definition.CareerId,
                            "item creation/inventory insertion " + (i + 1) + "/" + plans.Count,
                            "Granted " + granted + "/5 test items before failure: " + grantError,
                            out error);
                    }

                    granted++;
                    ModLog.Info("Admin grant inserted item " + granted + "/5 for " +
                        definition.CareerId + ": " + plans[i].ItemName + " [" +
                        (Convert.ToString(GetProperty(created, "StringId")) ?? "<no-id>") + "].");
                }

                bool[] acquisitionAfter = CaptureRealAcquisitionState(artisan, definition);
                for (int i = 0; i < acquisitionBefore.Length; i++)
                {
                    if (acquisitionBefore[i] != acquisitionAfter[i])
                    {
                        return FailAdminGrant(definition.CareerId, "acquisition isolation check",
                            "Admin grant changed real acquisition state for piece " + i +
                            ". Do not continue testing with this save until TORCareerUniques.log has been reviewed.",
                            out error);
                    }
                }

                RefreshRuntimeNow();
                result = "Granted five inventory copies for " + definition.SetName +
                    ". Their [ADMIN COPY] signatures activate the normal set tiers and do not count as recovered pieces.";
                ModLog.Info("Admin granted full test set for " + definition.CareerId +
                    "; all five inventory insertions were verified and acquisition state remained unchanged.");
                return true;
            }
            catch (Exception ex)
            {
                return FailAdminGrant(requestedCareer, "unhandled grant exception",
                    FormatException(ex), out error);
            }
        }

        private static void RefreshRuntimeNow()
        {
            EnsureTraitsInjected();
            Dictionary<string, EquippedSetState> equipped = ScanEquippedSetState();
            RefreshEquippedBonuses(equipped);
            DiscoverSetItems();
            RefreshSetDescriptions(equipped);
        }

        private static bool FailAdminGrant(string careerId, string stage, string message,
            out string error)
        {
            error = String.IsNullOrWhiteSpace(message) ? "Unknown admin grant failure." : message;
            ModLog.Error("Admin full-set grant failed for " + careerId + " at " + stage +
                ": " + error);
            return false;
        }

        internal static string DescribeSetProgress(string careerId)
        {
            SetDefinition definition;
            if (!DefinitionByCareer.TryGetValue(careerId ?? String.Empty, out definition))
                return "Unknown set.";

            int recovered = GetRecoveredCount(careerId);
            int equipped = GetEquippedPieceCount(definition);
            return definition.SetName + ": " + recovered + "/5 recovered, " +
                equipped + "/5 equipped.";
        }

        private static bool EnsureReady(out string error)
        {
            error = null;
            if (!CareerUniqueRuntime.EnsureReady(out error))
                return false;

            try
            {
                if (!EnsureTraitsInjected())
                {
                    error = "The career-set trait registry is not ready.";
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

        private static bool EnsureTraitsInjected()
        {
            Type managerType = TypeByName("TOR_Core.Items.ItemTraitManager");
            Type traitType = TypeByName("TOR_Core.Items.ItemTrait");
            if (managerType == null || traitType == null)
                return false;

            object manager = GetStaticProperty(managerType, "Instance");
            if (manager == null)
                return false;

            // The ToR registry is stable for the lifetime of its manager instance.
            // A successful pass therefore never needs to enumerate it again.
            if (Object.ReferenceEquals(manager, _traitsInjectedManager))
                return true;

            MethodInfo getTraits = managerType.GetMethod("GetItemTraits",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (getTraits == null)
                return false;

            IList traits = getTraits.Invoke(manager, null) as IList;
            if (traits == null)
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
                SetDefinition definition = Definitions[i];
                CareerItemDefinition relic = CareerUniqueRuntime.GetDefinitionForSet(definition.CareerId);
                if (relic == null)
                    continue;

                TraitDefinition relicAlias = CloneTrait(relic.Traits[0],
                    GetAdminSignature(definition, 0));
                if (InjectTrait(traits, existing, traitType, relicAlias, relic.ValidItemType))
                    added++;

                TraitDefinition heroRelicAlias = CloneTrait(relic.Traits[0],
                    GetHeroSignature(definition, 0));
                if (InjectTrait(traits, existing, traitType, heroRelicAlias, relic.ValidItemType))
                    added++;

                for (int p = 0; p < definition.Pieces.Length; p++)
                {
                    SetPieceDefinition piece = definition.Pieces[p];
                    for (int e = 0; e < piece.Effects.Length; e++)
                    {
                        TraitDefinition pieceEffect = piece.Effects[e];
                        if (e == 1)
                        {
                            // This slot is deliberately a native TOR enchantment/blessing/rune.
                            // It must resolve to TOR's real ItemTrait so native scripts/procs run.
                            if (!existing.Contains(pieceEffect.Id))
                            {
                                ModLog.Error("Required native TOR trait is missing: " +
                                    pieceEffect.Id + " (" + definition.CareerId +
                                    " set piece " + (p + 1) + ").");
                                return false;
                            }
                            continue;
                        }

                        if (InjectTrait(traits, existing, traitType, pieceEffect, "Armor"))
                            added++;
                    }

                    TraitDefinition adminAlias = CloneTrait(piece.Effects[0],
                        GetAdminSignature(definition, p + 1));
                    if (InjectTrait(traits, existing, traitType, adminAlias, "Armor"))
                        added++;

                    TraitDefinition heroAlias = CloneTrait(piece.Effects[0],
                        GetHeroSignature(definition, p + 1));
                    if (InjectTrait(traits, existing, traitType, heroAlias, "Armor"))
                        added++;

                    for (int e = 0; e < piece.Effects.Length; e++)
                    {
                        TraitDefinition pieceEffect = piece.Effects[e];
                        if (GetBonusTargetKind(pieceEffect) == BonusTargetKind.Armor)
                            continue;
                        TraitDefinition routed = CloneTrait(pieceEffect,
                            GetRoutedPieceTraitId(pieceEffect));
                        if (InjectTrait(traits, existing, traitType, routed,
                            GetBonusValidItemType(pieceEffect)))
                            added++;
                    }
                }

                for (int t = 0; t < definition.Tiers.Length; t++)
                {
                    for (int e = 0; e < definition.Tiers[t].Effects.Length; e++)
                    {
                        TraitDefinition tierEffect = definition.Tiers[t].Effects[e];
                        if (InjectTrait(traits, existing, traitType,
                            tierEffect, GetBonusValidItemType(tierEffect)))
                            added++;
                    }

                }
            }

            if (added > 0)
                ModLog.Info("Injected " + added + " career-set/admin traits into ToR's trait registry.");

            bool complete = Definitions.Length == 0 ||
                existing.Contains(Definitions[0].Pieces[0].Effects[0].Id);
            if (complete)
                _traitsInjectedManager = manager;
            return complete;
        }

        private static bool InjectTrait(IList traits, HashSet<string> existing, Type traitType,
            TraitDefinition spec, string validItemType)
        {
            if (spec == null || existing.Contains(spec.Id))
                return false;

            object trait = CareerUniqueRuntime.CreateTrait(traitType, spec, validItemType);
            traits.Add(trait);
            existing.Add(spec.Id);
            return true;
        }

        private static Dictionary<string, EquippedSetState> ScanEquippedSetState()
        {
            Dictionary<string, EquippedSetState> stateByCareer =
                new Dictionary<string, EquippedSetState>(StringComparer.OrdinalIgnoreCase);

            object hero = GetMainHeroIfReady();
            if (hero == null)
                return stateByCareer;

            if (!Object.ReferenceEquals(hero, _lastMainHero))
            {
                RemoveAllAppliedRuntimeTraits();
                _lastMainHero = hero;
                BaseTraitsByItemId.Clear();
                AppliedBonusKeyByItemId.Clear();
                // Visual resolution depends on the TOR object catalogue, not on which
                // hero is currently controlled.  Keep it across main-hero swaps (MCC)
                // and invalidate only when the Campaign session itself changes.
                KnownSetItemsById.Clear();
                DescriptionKeyByItemId.Clear();
                DisplayStateKeyByCareer.Clear();
                MigratedVisualBaseByItemId.Clear();
                _lastCraftedItemCount = -1;
            }

            object equipment = GetProperty(hero, "BattleEquipment");
            if (equipment == null)
                return stateByCareer;

            List<EquippedItemRef> equippedItems = new List<EquippedItemRef>();
            foreach (object element in EnumerateEquipmentElements(equipment))
            {
                object item = GetProperty(element, "Item");
                if (item == null)
                    continue;

                string itemId = Convert.ToString(GetProperty(item, "StringId"));
                if (String.IsNullOrEmpty(itemId))
                    continue;

                equippedItems.Add(new EquippedItemRef
                {
                    ItemId = itemId,
                    Item = item,
                    ItemTypeName = GetItemTypeName(item)
                });

                PieceSignature signature = FindPieceSignatureForItem(item, itemId);
                if (signature == null)
                    continue;

                EquippedSetState state;
                if (!stateByCareer.TryGetValue(signature.Definition.CareerId, out state))
                {
                    state = new EquippedSetState(signature.Definition);
                    stateByCareer.Add(signature.Definition.CareerId, state);
                }

                state.PieceIndices.Add(signature.PieceIndex);
                state.ItemIdsByPiece[signature.PieceIndex] = itemId;
                if (signature.PieceIndex == 0)
                {
                    state.RelicItemId = itemId;
                    state.RelicItem = item;
                }
                else if (String.IsNullOrEmpty(state.CarrierItemId))
                {
                    state.CarrierItemId = itemId;
                    state.CarrierItem = item;
                }
            }

            foreach (EquippedSetState state in stateByCareer.Values)
                state.EquippedItems.AddRange(equippedItems);
            return stateByCareer;
        }

        private static void RefreshDisplayTraitDescriptions(
            Dictionary<string, EquippedSetState> stateByCareer)
        {
            Type managerType = TypeByName("TOR_Core.Items.ItemTraitManager");
            Type traitType = TypeByName("TOR_Core.Items.ItemTrait");
            object manager = GetStaticProperty(managerType, "Instance");
            MethodInfo getTraits = managerType == null ? null :
                managerType.GetMethod("GetItemTraits",
                    BindingFlags.Public | BindingFlags.Instance);
            IList traits = getTraits == null || manager == null ? null :
                getTraits.Invoke(manager, null) as IList;
            if (traits == null || traitType == null)
                return;

            Dictionary<string, object> traitById =
                new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (object trait in traits)
            {
                string id = Convert.ToString(GetProperty(trait, "ItemTraitStringId"));
                if (!String.IsNullOrEmpty(id) && !traitById.ContainsKey(id))
                    traitById.Add(id, trait);
            }

            for (int d = 0; d < Definitions.Length; d++)
            {
                SetDefinition definition = Definitions[d];
                EquippedSetState equipped;
                stateByCareer.TryGetValue(definition.CareerId, out equipped);
                int equippedCount = equipped == null ? 0 : equipped.PieceIndices.Count;
                string stateKey = equippedCount.ToString();
                string previous;
                if (DisplayStateKeyByCareer.TryGetValue(definition.CareerId, out previous) &&
                    String.Equals(previous, stateKey, StringComparison.Ordinal))
                    continue;

                bool complete = true;
                for (int t = 0; t < definition.Tiers.Length; t++)
                {
                    SetTierDefinition tier = definition.Tiers[t];
                    string id = GetSetDisplayTraitId(definition, tier);
                    object trait;
                    if (!traitById.TryGetValue(id, out trait))
                    {
                        complete = false;
                        LogOnce("missing-display-trait:" + id,
                            "Set display trait '" + id +
                            "' is missing from ToR's registry; gameplay bonuses will continue.");
                        continue;
                    }

                    SetProperty(trait, "ItemTraitName",
                        tier.RequiredPieces + "/5 " +
                        (equippedCount >= tier.RequiredPieces ? "ACTIVE" : "LOCKED") +
                        " — " + tier.Name);
                    SetProperty(trait, "ItemTraitDescription",
                        BuildSetDisplayTraitDescription(definition, tier, equippedCount));
                }

                if (complete)
                    DisplayStateKeyByCareer[definition.CareerId] = stateKey;
            }
        }

        private static void RefreshEquippedBonuses(
            Dictionary<string, EquippedSetState> stateByCareer)
        {
            Dictionary<string, List<string>> desired =
                new Dictionary<string, List<string>>(StringComparer.Ordinal);
            Dictionary<string, object> targetItems =
                new Dictionary<string, object>(StringComparer.Ordinal);

            foreach (EquippedSetState state in stateByCareer.Values)
            {
                int count = state.PieceIndices.Count;

                // Armor-piece traits such as swing speed or armor penetration are
                // displayed on their set piece, while ToR evaluates those effects on
                // the currently used weapon/shield. Mirror only those incompatible
                // intrinsic effects to a compatible equipped target with a non-signature
                // alias, so the original armor piece remains the acquisition identity.
                foreach (int pieceIndex in state.PieceIndices)
                {
                    if (pieceIndex <= 0 || pieceIndex > state.Definition.Pieces.Length)
                        continue;
                    SetPieceDefinition equippedPiece =
                        state.Definition.Pieces[pieceIndex - 1];
                    for (int e = 0; e < equippedPiece.Effects.Length; e++)
                    {
                        TraitDefinition effect = equippedPiece.Effects[e];
                        BonusTargetKind targetKind = GetBonusTargetKind(effect);
                        if (targetKind == BonusTargetKind.Armor)
                            continue;
                        EquippedItemRef target = SelectBonusTarget(state, targetKind);
                        if (target == null)
                        {
                            LogOnce("piece-bonus-target:" + state.Definition.CareerId + ":" +
                                effect.Id,
                                "Equipped set-piece effect '" + effect.Name + "' for " +
                                state.Definition.CareerId + " has no equipped " +
                                DescribeBonusTarget(targetKind) + " target.");
                            continue;
                        }
                        AddDesiredTrait(desired, targetItems, target,
                            GetRoutedPieceTraitId(effect));
                    }
                }

                if (count < 2)
                    continue;

                for (int t = 0; t < state.Definition.Tiers.Length; t++)
                {
                    SetTierDefinition tier = state.Definition.Tiers[t];
                    if (count < tier.RequiredPieces)
                        continue;

                    for (int e = 0; e < tier.Effects.Length; e++)
                    {
                        TraitDefinition effect = tier.Effects[e];
                        BonusTargetKind targetKind = GetBonusTargetKind(effect);
                        EquippedItemRef target = SelectBonusTarget(state, targetKind);
                        if (target == null)
                        {
                            LogOnce("bonus-target:" + state.Definition.CareerId + ":" +
                                effect.Id + ":" + count,
                                "Active set bonus '" + effect.Name + "' for " +
                                state.Definition.CareerId + " has no equipped " +
                                DescribeBonusTarget(targetKind) + " target. The trait was not " +
                                "attached; equip a compatible weapon or shield.");
                            continue;
                        }

                        AddDesiredTrait(desired, targetItems, target, effect.Id);
                    }
                }
            }

            HashSet<string> allItemIds = new HashSet<string>(AppliedBonusKeyByItemId.Keys,
                StringComparer.Ordinal);
            foreach (string itemId in desired.Keys)
                allItemIds.Add(itemId);

            foreach (string itemId in allItemIds)
            {
                List<string> bonusIds;
                if (!desired.TryGetValue(itemId, out bonusIds))
                    bonusIds = new List<string>();

                string key = String.Join("|", bonusIds.ToArray());
                string previous;
                if (AppliedBonusKeyByItemId.TryGetValue(itemId, out previous) &&
                    String.Equals(previous, key, StringComparison.Ordinal))
                    continue;

                ApplyRuntimeBonusTraits(itemId, bonusIds);
                if (bonusIds.Count == 0)
                {
                    AppliedBonusKeyByItemId.Remove(itemId);
                    ModLog.Info("Removed conditional set-bonus traits from " + itemId + ".");
                }
                else
                {
                    object target;
                    if (!targetItems.TryGetValue(itemId, out target) || target == null)
                        throw new InvalidOperationException(
                            "Set-bonus target item disappeared before verification: " + itemId + ".");
                    VerifyResolvedBonusTraits(target, bonusIds);
                    AppliedBonusKeyByItemId[itemId] = key;
                    ModLog.Info("Activated " + bonusIds.Count +
                        " cumulative set-bonus traits on " + itemId +
                        "; ToR GetTraits() resolved every applied trait.");
                }
            }
        }

        private static void AddDesiredTrait(
            Dictionary<string, List<string>> desired,
            Dictionary<string, object> targetItems,
            EquippedItemRef target, string traitId)
        {
            if (target == null || String.IsNullOrEmpty(target.ItemId) ||
                target.Item == null || String.IsNullOrEmpty(traitId))
                return;

            List<string> ids;
            if (!desired.TryGetValue(target.ItemId, out ids))
            {
                ids = new List<string>();
                desired.Add(target.ItemId, ids);
                targetItems.Add(target.ItemId, target.Item);
            }
            if (!ids.Contains(traitId))
                ids.Add(traitId);
        }

        private static string GetRoutedPieceTraitId(TraitDefinition effect)
        {
            return RoutedPrefix + effect.Id;
        }

        private static BonusTargetKind GetBonusTargetKind(TraitDefinition effect)
        {
            if (effect == null || effect.Kind != TraitKind.Stat)
                return BonusTargetKind.Armor;

            switch (effect.EffectType)
            {
                case "ShieldHealth":
                    return BonusTargetKind.Shield;
                case "MissileSpeed":
                case "ReloadSpeed":
                case "MultiPenetration":
                case "ShieldPenetration":
                case "ScatterShot":
                    return BonusTargetKind.RangedWeapon;
                case "SwingSpeed":
                case "Cleave":
                    return BonusTargetKind.MeleeWeapon;
                case "ArmorPenetration":
                case "ShieldDamage":
                    return BonusTargetKind.AnyWeapon;
                default:
                    return BonusTargetKind.Armor;
            }
        }

        private static string GetBonusValidItemType(TraitDefinition effect)
        {
            switch (GetBonusTargetKind(effect))
            {
                case BonusTargetKind.Shield:
                    return "Shield";
                case BonusTargetKind.RangedWeapon:
                    return "Ranged";
                case BonusTargetKind.MeleeWeapon:
                    return "Melee";
                case BonusTargetKind.AnyWeapon:
                    return "Weapon";
                default:
                    return "Armor";
            }
        }

        private static EquippedItemRef SelectBonusTarget(EquippedSetState state,
            BonusTargetKind targetKind)
        {
            if (targetKind == BonusTargetKind.Armor)
            {
                if (state.CarrierItem != null && !String.IsNullOrEmpty(state.CarrierItemId))
                {
                    return new EquippedItemRef
                    {
                        ItemId = state.CarrierItemId,
                        Item = state.CarrierItem,
                        ItemTypeName = GetItemTypeName(state.CarrierItem)
                    };
                }
                return null;
            }

            if (state.RelicItem != null && !String.IsNullOrEmpty(state.RelicItemId))
            {
                EquippedItemRef relic = new EquippedItemRef
                {
                    ItemId = state.RelicItemId,
                    Item = state.RelicItem,
                    ItemTypeName = GetItemTypeName(state.RelicItem)
                };
                if (IsCompatibleBonusTarget(relic, targetKind))
                    return relic;
            }

            for (int i = 0; i < state.EquippedItems.Count; i++)
            {
                EquippedItemRef candidate = state.EquippedItems[i];
                if (IsCompatibleBonusTarget(candidate, targetKind))
                    return candidate;
            }
            return null;
        }

        private static bool IsCompatibleBonusTarget(EquippedItemRef item,
            BonusTargetKind targetKind)
        {
            if (item == null || item.Item == null || String.IsNullOrEmpty(item.ItemId))
                return false;

            string type = item.ItemTypeName ?? GetItemTypeName(item.Item);
            switch (targetKind)
            {
                case BonusTargetKind.MeleeWeapon:
                    return type == "OneHandedWeapon" || type == "TwoHandedWeapon" ||
                        type == "Polearm";
                case BonusTargetKind.RangedWeapon:
                    return type == "Bow" || type == "Crossbow" || type == "Pistol" ||
                        type == "Musket" || type == "Thrown" || type == "Sling";
                case BonusTargetKind.Shield:
                    return type == "Shield";
                case BonusTargetKind.AnyWeapon:
                    return type == "OneHandedWeapon" || type == "TwoHandedWeapon" ||
                        type == "Polearm" || type == "Bow" || type == "Crossbow" ||
                        type == "Pistol" || type == "Musket" || type == "Thrown" ||
                        type == "Sling" || type == "Shield";
                default:
                    return type == "HeadArmor" || type == "BodyArmor" ||
                        type == "LegArmor" || type == "HandArmor" || type == "Cape";
            }
        }

        private static string DescribeBonusTarget(BonusTargetKind kind)
        {
            switch (kind)
            {
                case BonusTargetKind.MeleeWeapon: return "melee-weapon";
                case BonusTargetKind.RangedWeapon: return "ranged-weapon";
                case BonusTargetKind.Shield: return "shield";
                case BonusTargetKind.AnyWeapon: return "weapon";
                default: return "armour";
            }
        }

        private static string GetItemTypeName(object item)
        {
            object value = GetItemTypeValue(item);
            return value == null ? String.Empty : Convert.ToString(value);
        }

        private static void VerifyResolvedBonusTraits(object item, IList<string> bonusIds)
        {
            Type extensions = TypeByName("TOR_Core.Extensions.ItemObjectExtensions");
            MethodInfo getTraits = FindStaticMethod(extensions, "GetTraits", 1);
            if (getTraits == null)
                throw new MissingMethodException(
                    "TOR_Core.Extensions.ItemObjectExtensions", "GetTraits(ItemObject)");

            IEnumerable resolved = getTraits.Invoke(null, new object[] { item }) as IEnumerable;
            if (resolved == null)
                throw new InvalidOperationException(
                    "ToR GetTraits() returned null for set-bonus carrier.");

            HashSet<string> resolvedIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (object trait in resolved)
            {
                string id = Convert.ToString(GetProperty(trait, "ItemTraitStringId"));
                if (!String.IsNullOrEmpty(id))
                    resolvedIds.Add(id);
            }

            for (int i = 0; i < bonusIds.Count; i++)
            {
                if (!resolvedIds.Contains(bonusIds[i]))
                    throw new InvalidOperationException(
                        "ToR GetTraits() did not resolve applied set-bonus trait '" +
                        bonusIds[i] + "'.");
            }
        }

        private static PieceSignature FindPieceSignature(IList traits)
        {
            if (traits == null)
                return null;

            for (int i = 0; i < traits.Count; i++)
            {
                PieceSignature signature;
                if (SignatureByTraitId.TryGetValue(Convert.ToString(traits[i]), out signature))
                    return signature;
            }
            return null;
        }

        private static PieceSignature FindPieceSignatureForItem(object item,
            string itemId)
        {
            SetItemInstance known;
            if (!String.IsNullOrWhiteSpace(itemId) &&
                KnownSetItemsById.TryGetValue(itemId, out known) && known != null)
                return known.Signature;

            PieceSignature signature = FindPieceSignature(GetItemTraits(itemId));
            if (signature != null)
                return signature;

            object artisan = CareerUniqueRuntime.GetArtisanBehavior();
            IDictionary crafted = GetField(artisan, "_customCraftedItems") as IDictionary;
            if (crafted != null)
            {
                foreach (DictionaryEntry entry in crafted)
                {
                    object craftedItem = entry.Key;
                    string craftedId = Convert.ToString(GetProperty(craftedItem, "StringId"));
                    if (!Object.ReferenceEquals(craftedItem, item) &&
                        !String.Equals(craftedId, itemId, StringComparison.Ordinal))
                        continue;

                    IList savedTraits = GetProperty(entry.Value, "ItemTraits") as IList;
                    if (savedTraits == null)
                        savedTraits = GetField(entry.Value, "ItemTraits") as IList;
                    signature = FindPieceSignature(savedTraits);
                    if (signature != null)
                        return signature;
                }
            }

            return FindPieceSignatureByName(item);
        }

        private static PieceSignature FindPieceSignatureByName(object item)
        {
            string name = Convert.ToString(GetProperty(item, "Name")) ?? String.Empty;
            name = name.Replace("[ADMIN COPY]", String.Empty).Trim();
            if (name.Length == 0)
                return null;

            for (int d = 0; d < Definitions.Length; d++)
            {
                SetDefinition definition = Definitions[d];
                CareerItemDefinition relic =
                    CareerUniqueRuntime.GetDefinitionForSet(definition.CareerId);
                if (relic != null && String.Equals(name, relic.ItemName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return new PieceSignature { Definition = definition, PieceIndex = 0 };
                }

                for (int p = 0; p < definition.Pieces.Length; p++)
                {
                    if (String.Equals(name, definition.Pieces[p].ItemName,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        return new PieceSignature
                        {
                            Definition = definition,
                            PieceIndex = p + 1
                        };
                    }
                }
            }
            return null;
        }

        internal static void IndexKnownSetItemsOnce()
        {
            DiscoverSetItems();
        }

        private static void DiscoverSetItems()
        {
            object artisan = CareerUniqueRuntime.GetArtisanBehavior();
            IDictionary crafted = GetField(artisan, "_customCraftedItems") as IDictionary;
            if (crafted == null)
                return;

            // The crafted dictionary count is the event-driven invalidation key. If it
            // did not change, the known-item index is already current and this path is O(1).
            if (crafted.Count == _lastCraftedItemCount && KnownSetItemsById.Count > 0)
                return;

            _lastCraftedItemCount = crafted.Count;
            foreach (DictionaryEntry entry in crafted)
            {
                object item = entry.Key;
                if (item == null)
                    continue;

                string itemId = Convert.ToString(GetProperty(item, "StringId"));
                if (String.IsNullOrWhiteSpace(itemId))
                    continue;

                PieceSignature signature = FindPieceSignature(GetItemTraits(itemId));
                if (signature == null)
                {
                    IList savedTraits = GetProperty(entry.Value, "ItemTraits") as IList;
                    if (savedTraits == null)
                        savedTraits = GetField(entry.Value, "ItemTraits") as IList;
                    signature = FindPieceSignature(savedTraits);
                }
                if (signature == null)
                    signature = FindPieceSignatureByName(item);
                if (signature == null)
                    continue;

                KnownSetItemsById[itemId] = new SetItemInstance
                {
                    Item = item,
                    SaveData = entry.Value,
                    Signature = signature,
                    IsAdmin = HasAdminSignature(GetItemTraits(itemId)) ||
                        (Convert.ToString(GetProperty(item, "Name")) ?? String.Empty)
                            .StartsWith("[ADMIN COPY]", StringComparison.OrdinalIgnoreCase)
                };
            }
        }

        // Existing-save visual migration is intentionally a campaign-session one-shot.
        // It is never called from MCM/options, inventory activation, equipment mutation,
        // or the ordinary set runtime tick. A failed item is also remembered for the
        // session so an unresolved TOR catalogue mapping cannot become recurring work.
        internal static void MigrateKnownVisualsOnce()
        {
            EnsureVisualResolverSession();
            if (_visualMigrationPassCompleted)
                return;

            DiscoverSetItems();
            if (!IsVisualResolverReady())
                return;

            _visualMigrationPassCompleted = true;
            List<SetItemInstance> snapshot = new List<SetItemInstance>();
            List<string> ids = new List<string>();
            foreach (KeyValuePair<string, SetItemInstance> pair in KnownSetItemsById)
            {
                ids.Add(pair.Key);
                snapshot.Add(pair.Value);
            }

            for (int i = 0; i < snapshot.Count; i++)
            {
                SetItemInstance known = snapshot[i];
                if (known == null || VisualMigrationAttemptedItemIds.Contains(ids[i]))
                    continue;
                VisualMigrationAttemptedItemIds.Add(ids[i]);
                try
                {
                    EnsureCorrectVisual(ids[i], known.Item, known.SaveData, known.Signature);
                }
                catch (Exception ex)
                {
                    LogOnce("visual-migration-exception:" + ids[i] + ":" +
                        ex.GetType().FullName + ":" + ex.Message,
                        "One-shot visual migration failed for " + ids[i] + ": " +
                        FormatException(ex));
                }
            }
        }

        private static bool HasAdminSignature(IList traits)
        {
            if (traits == null)
                return false;
            for (int i = 0; i < traits.Count; i++)
            {
                string id = Convert.ToString(traits[i]);
                if (!String.IsNullOrEmpty(id) &&
                    id.StartsWith(AdminPrefix, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static void EnsureCorrectVisual(string itemId, object item, object saveData,
            PieceSignature signature)
        {
            if (signature == null || signature.Definition == null || item == null)
                return;

            object desiredBase;
            string logicalName;
            if (signature.PieceIndex == 0)
            {
                CareerItemDefinition relic = CareerUniqueRuntime.GetDefinitionForSet(
                    signature.Definition.CareerId);
                if (relic == null)
                    return;
                desiredBase = CareerUniqueRuntime.FindBaseItem(relic);
                logicalName = relic.ItemName;
                if (desiredBase == null)
                    return;
                if (!CareerUniqueRuntime.IsBaseItemCompatible(relic, desiredBase))
                    throw new InvalidOperationException("Relic resolver returned an incompatible base for " +
                        relic.ItemName + ".");
            }
            else
            {
                SetPieceDefinition piece = signature.Definition.Pieces[signature.PieceIndex - 1];
                desiredBase = FindArmorBaseItem(signature.Definition, piece);
                logicalName = piece.ItemName;
                if (desiredBase == null)
                    return;
                if (!IsExactSlotItem(desiredBase, piece.Slot))
                    throw new InvalidOperationException("Visual resolver returned " +
                        Convert.ToString(GetItemTypeValue(desiredBase)) + " for " + piece.Slot +
                        " piece " + piece.ItemName + ".");
            }

            string desiredId = Convert.ToString(GetProperty(desiredBase, "StringId"));
            if (String.IsNullOrWhiteSpace(desiredId))
                return;

            string previous;
            if (MigratedVisualBaseByItemId.TryGetValue(itemId, out previous) &&
                String.Equals(previous, desiredId, StringComparison.Ordinal))
                return;

            string currentBaseId = Convert.ToString(GetProperty(saveData, "OriginalItemStringId"));
            if (String.Equals(currentBaseId, desiredId, StringComparison.OrdinalIgnoreCase))
            {
                MigratedVisualBaseByItemId[itemId] = desiredId;
                return;
            }

            try
            {
                Type extensions = TypeByName("TOR_Core.Extensions.ItemObjectExtensions");
                MethodInfo copy = FindStaticMethod(extensions, "CopyPropertiesFrom", 2);
                if (copy == null)
                    throw new MissingMethodException(
                        "TOR_Core.Extensions.ItemObjectExtensions", "CopyPropertiesFrom");

                string customName = Convert.ToString(GetProperty(saveData, "NewItemName"));
                copy.Invoke(null, new object[] { item, desiredBase });

                if (!String.IsNullOrWhiteSpace(customName))
                {
                    object text = CreateTextObject(customName);
                    if (text != null)
                        SetProperty(item, "Name", text);
                }

                InvokeNoArg(item, "DetermineItemCategoryForItem");
                SetProperty(saveData, "OriginalItemStringId", desiredId);
                MigratedVisualBaseByItemId[itemId] = desiredId;

                ModLog.Info("Migrated set-piece visual for " +
                    signature.Definition.CareerId + " / " + logicalName +
                    " from " + (currentBaseId ?? "<unknown>") + " to " + desiredId + ".");
            }
            catch (Exception ex)
            {
                LogOnce("visual-migration:" + itemId + ":" + desiredId,
                    "Set-piece visual migration failed for " + itemId + ": " +
                    FormatException(ex));
            }
        }

        private static bool EnsureSetDisplayTraitsOnItem(string itemId,
            SetDefinition definition)
        {
            if (String.IsNullOrWhiteSpace(itemId) || definition == null)
                return false;

            Type managerType = TypeByName("TOR_Core.Items.ExtendedItemObjectManager");
            FieldInfo mapField = managerType == null ? null :
                managerType.GetField("_itemToInfoMap",
                    BindingFlags.NonPublic | BindingFlags.Static);
            IDictionary map = mapField == null ? null : mapField.GetValue(null) as IDictionary;
            if (map == null || !map.Contains(itemId) || map[itemId] == null)
                return false;

            object properties = map[itemId];
            IList current = GetField(properties, "ItemTraits") as IList;
            List<string> traits = new List<string>();
            if (current != null)
            {
                for (int i = 0; i < current.Count; i++)
                {
                    string id = Convert.ToString(current[i]);
                    if (!String.IsNullOrEmpty(id) && !traits.Contains(id))
                        traits.Add(id);
                }
            }

            bool changed = false;
            for (int t = 0; t < definition.Tiers.Length; t++)
            {
                string id = GetSetDisplayTraitId(definition, definition.Tiers[t]);
                if (!traits.Contains(id))
                {
                    traits.Add(id);
                    changed = true;
                }
            }
            if (!changed)
                return true;

            MethodInfo cloneMethod = properties.GetType().GetMethod("Clone",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            object clone = cloneMethod == null ? null : cloneMethod.Invoke(properties, null);
            if (clone == null)
                return false;
            SetField(clone, "ItemTraits", traits);
            map[itemId] = clone;
            return HasAllSetDisplayTraits(itemId, definition);
        }

        private static bool HasAllSetDisplayTraits(string itemId,
            SetDefinition definition)
        {
            IList traits = GetItemTraits(itemId);
            if (traits == null || definition == null)
                return false;
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < traits.Count; i++)
                ids.Add(Convert.ToString(traits[i]));
            for (int t = 0; t < definition.Tiers.Length; t++)
            {
                if (!ids.Contains(GetSetDisplayTraitId(definition,
                    definition.Tiers[t])))
                    return false;
            }
            return true;
        }

        private static bool IsSetDisplayTraitId(string traitId)
        {
            return !String.IsNullOrEmpty(traitId) &&
                traitId.StartsWith(DisplayPrefix, StringComparison.Ordinal);
        }

        private static bool ContainsSetDisplayTrait(string itemId)
        {
            IList traits = GetItemTraits(itemId);
            if (traits == null)
                return false;
            for (int i = 0; i < traits.Count; i++)
            {
                if (IsSetDisplayTraitId(Convert.ToString(traits[i])))
                    return true;
            }
            return false;
        }

        private static void RefreshSetDescriptions(
            Dictionary<string, EquippedSetState> stateByCareer)
        {
            if (KnownSetItemsById.Count == 0)
                return;

            Type managerType = TypeByName("TOR_Core.Items.ExtendedItemObjectManager");
            FieldInfo mapField = managerType == null ? null :
                managerType.GetField("_itemToInfoMap",
                    BindingFlags.NonPublic | BindingFlags.Static);
            IDictionary map = mapField == null ? null : mapField.GetValue(null) as IDictionary;
            if (map == null)
                return;

            foreach (KeyValuePair<string, SetItemInstance> pair in KnownSetItemsById)
            {
                string itemId = pair.Key;
                SetItemInstance instance = pair.Value;
                EquippedSetState equipped;
                stateByCareer.TryGetValue(instance.Signature.Definition.CareerId, out equipped);

                string stateKey = BuildDescriptionStateKey(instance, equipped);
                string previous;
                if (DescriptionKeyByItemId.TryGetValue(itemId, out previous) &&
                    String.Equals(previous, stateKey, StringComparison.Ordinal) &&
                    !ContainsSetDisplayTrait(itemId))
                    continue;

                object properties = map[itemId];
                if (properties == null)
                    continue;

                MethodInfo cloneMethod = properties.GetType().GetMethod("Clone",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                object clone = cloneMethod == null ? null : cloneMethod.Invoke(properties, null);
                if (clone == null)
                    continue;

                SetField(clone, "Description", BuildSetDescription(instance, equipped));
                IList cloneTraits = GetField(clone, "ItemTraits") as IList;
                List<string> visibleTraits = new List<string>();
                if (cloneTraits != null)
                {
                    for (int i = 0; i < cloneTraits.Count; i++)
                    {
                        string id = Convert.ToString(cloneTraits[i]);
                        if (!String.IsNullOrEmpty(id) &&
                            !IsSetDisplayTraitId(id) &&
                            !visibleTraits.Contains(id))
                            visibleTraits.Add(id);
                    }
                }
                SetField(clone, "ItemTraits", visibleTraits);
                map[itemId] = clone;
                if (ContainsSetDisplayTrait(itemId))
                    throw new InvalidOperationException("Legacy set-summary traits were not " +
                        "removed from " + itemId + ".");
                DescriptionKeyByItemId[itemId] = stateKey;
            }
        }

        private static string BuildDescriptionStateKey(SetItemInstance instance,
            EquippedSetState equipped)
        {
            StringBuilder key = new StringBuilder();
            key.Append(instance.Signature.Definition.CareerId).Append('|')
                .Append(instance.Signature.PieceIndex).Append('|')
                .Append(instance.IsAdmin ? 'A' : 'R').Append('|');

            if (equipped != null)
            {
                for (int i = 0; i < 5; i++)
                    key.Append(equipped.PieceIndices.Contains(i) ? '1' : '0');
            }
            else
            {
                key.Append("00000");
            }
            return key.ToString();
        }

        internal static bool TryBuildTooltipForItemViewModel(object itemViewModel,
            out string itemId, out string description)
        {
            List<SetTooltipRow> ignored;
            return TryBuildTooltipForItemViewModel(itemViewModel, out itemId,
                out description, out ignored);
        }

        internal static bool TryBuildTooltipForItemViewModel(object itemViewModel,
            out string itemId, out string description, out List<SetTooltipRow> rows)
        {
            itemId = GetItemIdFromViewModel(itemViewModel);
            description = null;
            rows = null;
            if (String.IsNullOrWhiteSpace(itemId))
                return false;

            object item = GetItemFromViewModel(itemViewModel);
            PieceSignature signature = FindPieceSignatureForTooltip(item, itemId);
            if (signature == null)
                return false;

            // The item tooltip can be opened before the first application tick after a
            // save/session transition. Ensure the native ToR summary traits exist before
            // refreshing their text or adding their view models.
            if (!EnsureTraitsInjected())
                return false;

            IList traits = GetItemTraits(itemId);

            Dictionary<string, EquippedSetState> stateByCareer = ScanEquippedSetState();
            EquippedSetState equipped;
            stateByCareer.TryGetValue(signature.Definition.CareerId, out equipped);

            SetItemInstance instance = new SetItemInstance
            {
                Item = item,
                SaveData = null,
                Signature = signature,
                IsAdmin = HasAdminSignature(traits) ||
                    (Convert.ToString(GetProperty(item, "Name")) ?? String.Empty)
                        .StartsWith("[ADMIN COPY]", StringComparison.OrdinalIgnoreCase)
            };
            description = BuildSetDescription(instance, equipped);
            rows = BuildSetTooltipRows(instance, equipped);
            return !String.IsNullOrWhiteSpace(description);
        }

        // Tooltip refreshes are a hot UI path. Known items and their own traits/name
        // are sufficient here; the full ToR crafted-item save dictionary is reconciled
        // once by SetItemRuntime.Tick and must never be searched for ordinary items.
        private static PieceSignature FindPieceSignatureForTooltip(object item,
            string itemId)
        {
            SetItemInstance known;
            if (!String.IsNullOrWhiteSpace(itemId) &&
                KnownSetItemsById.TryGetValue(itemId, out known) && known != null)
                return known.Signature;

            PieceSignature signature = FindPieceSignature(GetItemTraits(itemId));
            return signature ?? FindPieceSignatureByName(item);
        }

        internal static bool IsSetItemViewModel(object itemViewModel)
        {
            string itemId = GetItemIdFromViewModel(itemViewModel);
            if (String.IsNullOrWhiteSpace(itemId))
                return false;
            object item = GetItemFromViewModel(itemViewModel);
            return FindPieceSignatureForItem(item, itemId) != null;
        }

        internal static List<string> GetSetDisplayTraitIdsForItemViewModel(
            object itemViewModel)
        {
            // Legacy compatibility entry point. Set tiers are rendered once in the
            // normal description; zero-value summary traits are no longer attached.
            return new List<string>();
        }

        internal static List<string> GetTooltipTraitIdsForItemViewModel(
            object itemViewModel)
        {
            List<string> result = new List<string>();
            string itemId = GetItemIdFromViewModel(itemViewModel);
            if (String.IsNullOrWhiteSpace(itemId))
                return result;

            IList traits = GetItemTraits(itemId);
            if (traits == null)
                return result;
            for (int i = 0; i < traits.Count; i++)
                result.Add(Convert.ToString(traits[i]));
            return result;
        }

        internal static bool IsHiddenTooltipTraitId(string traitId)
        {
            return !String.IsNullOrEmpty(traitId) &&
                (traitId.StartsWith(BonusPrefix, StringComparison.Ordinal) ||
                 traitId.StartsWith(RoutedPrefix, StringComparison.Ordinal) ||
                 traitId.StartsWith(DisplayPrefix, StringComparison.Ordinal));
        }

        private static object GetItemFromViewModel(object itemViewModel)
        {
            if (itemViewModel == null)
                return null;

            object rosterElement = GetProperty(itemViewModel, "ItemRosterElement");
            if (rosterElement == null)
                rosterElement = GetField(itemViewModel, "ItemRosterElement");
            if (rosterElement == null)
                return null;

            object equipmentElement = GetProperty(rosterElement, "EquipmentElement");
            if (equipmentElement == null)
                equipmentElement = GetField(rosterElement, "EquipmentElement");
            if (equipmentElement == null)
                return null;

            object item = GetProperty(equipmentElement, "Item");
            return item ?? GetField(equipmentElement, "Item");
        }

        private static string GetItemIdFromViewModel(object itemViewModel)
        {
            object item = GetItemFromViewModel(itemViewModel);
            return Convert.ToString(GetProperty(item, "StringId"));
        }

        private static string BuildSetDescription(SetItemInstance instance,
            EquippedSetState equipped)
        {
            SetDefinition definition = instance.Signature.Definition;
            int equippedCount = equipped == null ? 0 : equipped.PieceIndices.Count;
            StringBuilder text = new StringBuilder(900);

            text.Append("SET: ").Append(definition.SetName).Append("  [")
                .Append(equippedCount).Append("/5 equipped]").AppendLine();
            if (instance.IsAdmin)
                text.AppendLine("ADMIN TEST COPY — acquisition progress is unchanged");

            text.AppendLine();
            text.AppendLine("SET PIECES");
            for (int i = 0; i < 5; i++)
            {
                bool activePiece = equipped != null && equipped.PieceIndices.Contains(i);
                text.Append(activePiece ? "[X] " : "[ ] ")
                    .Append(GetPieceName(definition, i)).AppendLine();
            }

            text.AppendLine();
            text.AppendLine("SET BONUSES");
            for (int t = 0; t < definition.Tiers.Length; t++)
            {
                SetTierDefinition tier = definition.Tiers[t];
                bool active = equippedCount >= tier.RequiredPieces;
                text.Append(active ? "[ACTIVE] " : "[LOCKED] ")
                    .Append(tier.RequiredPieces).Append("/5 — ")
                    .Append(tier.Name).AppendLine();
                text.Append("  ");
                AppendEffectSummary(text, tier.Effects);
                if (t + 1 < definition.Tiers.Length)
                    text.AppendLine();
            }
            return text.ToString().TrimEnd(new char[0]);
        }

        private static void AppendEffectSummary(StringBuilder text,
            TraitDefinition[] effects)
        {
            if (effects == null || effects.Length == 0)
            {
                text.Append("None");
                return;
            }

            for (int i = 0; i < effects.Length; i++)
            {
                if (i > 0)
                    text.Append("; ");
                string value = effects[i].Description ?? effects[i].Name ?? String.Empty;
                value = value.Trim();
                while (value.EndsWith(".", StringComparison.Ordinal))
                    value = value.Substring(0, value.Length - 1).TrimEnd(new char[0]);
                text.Append(value);
            }
        }


        private static List<SetTooltipRow> BuildSetTooltipRows(
            SetItemInstance instance, EquippedSetState equipped)
        {
            // Kept for binary/source compatibility with the earlier tooltip bridge.
            // The visible set data is now emitted exactly once through ItemDescription.
            return new List<SetTooltipRow>();
        }

        private static string FormatEffectSummary(TraitDefinition[] effects)
        {
            StringBuilder text = new StringBuilder();
            AppendEffectSummary(text, effects);
            return text.ToString();
        }

        private static string GetPieceName(SetDefinition definition, int pieceIndex)
        {
            if (pieceIndex == 0)
            {
                CareerItemDefinition relic =
                    CareerUniqueRuntime.GetDefinitionForSet(definition.CareerId);
                return relic == null ? "Career relic" : relic.ItemName;
            }
            return definition.Pieces[pieceIndex - 1].ItemName;
        }

        private static TraitDefinition[] GetPieceEffects(SetDefinition definition,
            int pieceIndex)
        {
            if (pieceIndex == 0)
            {
                CareerItemDefinition relic =
                    CareerUniqueRuntime.GetDefinitionForSet(definition.CareerId);
                return relic == null ? new TraitDefinition[0] : relic.Traits;
            }
            return definition.Pieces[pieceIndex - 1].Effects;
        }

        private static int GetEquippedPieceCount(SetDefinition definition)
        {
            object hero = GetMainHeroIfReady();
            object equipment = GetProperty(hero, "BattleEquipment");
            if (equipment == null)
                return 0;

            HashSet<int> pieces = new HashSet<int>();
            foreach (object element in EnumerateEquipmentElements(equipment))
            {
                object item = GetProperty(element, "Item");
                string itemId = Convert.ToString(GetProperty(item, "StringId"));
                if (String.IsNullOrEmpty(itemId))
                    continue;

                PieceSignature signature = FindPieceSignatureForItem(item, itemId);
                if (signature != null &&
                    Object.ReferenceEquals(signature.Definition, definition))
                    pieces.Add(signature.PieceIndex);
            }
            return pieces.Count;
        }

        private static void RemoveAllAppliedRuntimeTraits()
        {
            if (AppliedBonusKeyByItemId.Count == 0)
                return;
            string[] itemIds = new string[AppliedBonusKeyByItemId.Count];
            AppliedBonusKeyByItemId.Keys.CopyTo(itemIds, 0);
            for (int i = 0; i < itemIds.Length; i++)
            {
                try { ApplyRuntimeBonusTraits(itemIds[i], new List<string>()); }
                catch (Exception ex)
                {
                    LogOnce("runtime-trait-cleanup:" + itemIds[i],
                        "Failed to remove stale set runtime traits from " + itemIds[i] +
                        ": " + FormatException(ex));
                }
            }
        }

        private static bool IsConditionalRuntimeTrait(string traitId)
        {
            return !String.IsNullOrEmpty(traitId) &&
                (traitId.StartsWith(BonusPrefix, StringComparison.Ordinal) ||
                 traitId.StartsWith(RoutedPrefix, StringComparison.Ordinal) ||
                 traitId.StartsWith(DisplayPrefix, StringComparison.Ordinal));
        }

        private static void ApplyRuntimeBonusTraits(string itemId, List<string> bonusIds)
        {
            Type managerType = TypeByName("TOR_Core.Items.ExtendedItemObjectManager");
            if (managerType == null)
                return;

            FieldInfo mapField = managerType.GetField("_itemToInfoMap",
                BindingFlags.NonPublic | BindingFlags.Static);
            IDictionary map = mapField == null ? null : mapField.GetValue(null) as IDictionary;
            if (map == null)
                return;

            MethodInfo getReadOnly = FindStaticMethod(managerType,
                "GetAdditionalPropertiesReadOnly", 1);
            object properties = getReadOnly == null ? null :
                getReadOnly.Invoke(null, new object[] { itemId });
            if (properties == null)
            {
                Type propertiesType = TypeByName("TOR_Core.Items.ExtendedItemObjectProperties");
                MethodInfo createDefault = FindStaticMethod(propertiesType, "CreateDefault", 1);
                if (createDefault == null)
                    throw new MissingMethodException(
                        "TOR_Core.Items.ExtendedItemObjectProperties", "CreateDefault");
                properties = createDefault.Invoke(null, new object[] { itemId });
                if (properties == null)
                    throw new InvalidOperationException(
                        "ToR could not create runtime item properties for " + itemId + ".");
                map[itemId] = properties;
            }

            List<string> baseTraits = new List<string>();
            IList current = GetField(properties, "ItemTraits") as IList;
            if (current != null)
            {
                for (int i = 0; i < current.Count; i++)
                {
                    string id = Convert.ToString(current[i]);
                    if (!String.IsNullOrEmpty(id) &&
                        !IsConditionalRuntimeTrait(id))
                        baseTraits.Add(id);
                }
            }
            BaseTraitsByItemId[itemId] = baseTraits;

            MethodInfo cloneMethod = properties.GetType().GetMethod("Clone",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            object clone = cloneMethod == null ? null : cloneMethod.Invoke(properties, null);
            if (clone == null)
                return;

            List<string> combined = new List<string>(baseTraits);
            for (int i = 0; i < bonusIds.Count; i++)
            {
                if (!combined.Contains(bonusIds[i]))
                    combined.Add(bonusIds[i]);
            }
            SetField(clone, "ItemTraits", combined);
            map[itemId] = clone;

            IList verified = GetField(map[itemId], "ItemTraits") as IList;
            for (int i = 0; i < bonusIds.Count; i++)
            {
                bool found = false;
                if (verified != null)
                {
                    for (int j = 0; j < verified.Count; j++)
                    {
                        if (String.Equals(Convert.ToString(verified[j]), bonusIds[i],
                            StringComparison.Ordinal))
                        {
                            found = true;
                            break;
                        }
                    }
                }
                if (!found)
                {
                    throw new InvalidOperationException("Set bonus trait '" + bonusIds[i] +
                        "' was not present after applying it to " + itemId + ".");
                }
            }

            ModLog.Info("Applied " + bonusIds.Count + " cumulative set-bonus traits to " +
                itemId + ".");
        }

        private static IList GetItemTraits(string itemId)
        {
            Type managerType = TypeByName("TOR_Core.Items.ExtendedItemObjectManager");
            MethodInfo getReadOnly = FindStaticMethod(managerType, "GetAdditionalPropertiesReadOnly", 1);
            object properties = getReadOnly == null ? null :
                getReadOnly.Invoke(null, new object[] { itemId });
            return properties == null ? null : GetField(properties, "ItemTraits") as IList;
        }

        private static bool CreateAndGrant(object baseItem, string itemName,
            List<string> traitIds, SetSlot? expectedSlot,
            out object newItem, out string error)
        {
            string ignoredName;
            return CreateAndGrantInternal(baseItem, itemName, traitIds,
                expectedSlot, false, out newItem, out ignoredName, out error);
        }

        private static bool CreateAndGrantWithLootModifier(object baseItem,
            string itemName, List<string> traitIds, SetSlot? expectedSlot,
            out object newItem, out string modifiedName, out string error)
        {
            return CreateAndGrantInternal(baseItem, itemName, traitIds,
                expectedSlot, true, out newItem, out modifiedName, out error);
        }

        private static bool CreateAndGrantInternal(object baseItem,
            string itemName, List<string> traitIds, SetSlot? expectedSlot,
            bool rollLootModifier, out object newItem, out string modifiedName,
            out string error)
        {
            newItem = null;
            modifiedName = itemName;
            error = null;

            Type mobilePartyType = TypeByName("TaleWorlds.CampaignSystem.Party.MobileParty");
            object mainParty = GetStaticProperty(mobilePartyType, "MainParty");
            object roster = GetProperty(mainParty, "ItemRoster");
            Type itemObjectType = TypeByName("TaleWorlds.Core.ItemObject");
            if (roster == null || itemObjectType == null)
            {
                error = "The player party item roster is unavailable.";
                return false;
            }

            Type helperType = TypeByName("TOR_Core.CampaignMechanics.Crafting.EnchantmentHelper");
            MethodInfo create = FindStaticMethod(helperType, "CreateEnchantedItem", 5);
            if (create == null)
            {
                error = "Unable to find ToR EnchantmentHelper.CreateEnchantedItem.";
                return false;
            }

            IList reflectionList = new List<string>(traitIds);
            newItem = create.Invoke(null, new object[] { baseItem, reflectionList, itemName, false, null });
            if (newItem == null)
            {
                error = "ToR returned null while creating " + itemName + ".";
                return false;
            }

            if (expectedSlot.HasValue && !IsExactSlotItem(newItem, expectedSlot.Value))
            {
                error = "ToR created '" + itemName + "' as " +
                    Convert.ToString(GetItemTypeValue(newItem)) + "; expected " +
                    GetExpectedItemTypeName(expectedSlot.Value) +
                    ". The invalid item was not recorded or added to inventory.";
                ModLog.Error(error);
                newItem = null;
                return false;
            }

            EnsureCraftedItemRecorded(baseItem, newItem, itemName, reflectionList);

            MethodInfo getItemNumber = FindInstanceMethod(roster.GetType(),
                "GetItemNumber", new[] { itemObjectType });
            int before = -1;
            if (getItemNumber != null)
                before = Convert.ToInt32(getItemNumber.Invoke(roster, new object[] { newItem }));

            object modifier = rollLootModifier
                ? CareerUniqueRuntime.RollLootModifier(newItem)
                : null;
            if (!CareerUniqueRuntime.AddToRoster(roster, newItem, modifier, 1,
                out error))
                return false;
            modifiedName = CareerUniqueRuntime.FormatModifiedItemName(itemName,
                modifier);

            if (getItemNumber != null)
            {
                int after = Convert.ToInt32(getItemNumber.Invoke(roster, new object[] { newItem }));
                if (after < before + 1)
                {
                    error = "ItemRoster.AddToCounts returned without increasing the inventory count for " +
                        modifiedName + " (before=" + before + ", after=" + after + ").";
                    ModLog.Error(error);
                    return false;
                }
                ModLog.Info("Inventory insertion verified for '" + modifiedName +
                    "' (before=" + before + ", after=" + after + ").");
            }
            else
            {
                ModLog.Info("Inventory insertion invoked for '" + modifiedName +
                    "'; GetItemNumber(ItemObject) was unavailable for post-insert verification.");
            }
            return true;
        }

        private static void EnsureCraftedItemRecorded(object baseItem, object newItem,
            string itemName, IList traitIds)
        {
            object artisan = CareerUniqueRuntime.GetArtisanBehavior();
            IDictionary dictionary = GetField(artisan, "_customCraftedItems") as IDictionary;
            if (dictionary == null)
                throw new InvalidOperationException("ToR's crafted-item save dictionary is unavailable.");

            if (dictionary.Contains(newItem))
                return;

            Type dataType = TypeByName("TOR_Core.CampaignMechanics.Crafting.TorItemDuplicationData");
            if (dataType == null)
                throw new InvalidOperationException("ToR's duplication data type is unavailable.");

            object data = Activator.CreateInstance(dataType);
            SetProperty(data, "OriginalItemStringId",
                Convert.ToString(GetProperty(baseItem, "StringId")));
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
        }

        private static void PrepareVisualResolutionForExplicitAction(
            SetDefinition definition)
        {
            if (definition == null)
                return;
            EnsureVisualResolverSession();

            bool complete = true;
            for (int i = 0; i < definition.Pieces.Length; i++)
            {
                SetPieceDefinition piece = definition.Pieces[i];
                object cached;
                if (!VisualItemByCareerSlot.TryGetValue(
                    definition.CareerId + "|" + piece.Slot, out cached) ||
                    cached == null || !IsExactSlotItem(cached, piece.Slot))
                {
                    complete = false;
                    break;
                }
            }
            if (complete)
                return;

            // Negative caching protects generic/event-driven refreshes. A concrete grant
            // request is the safe place to clear only this career's failed attempt and
            // resolve once against the now-stable catalogue.
            VisualOutfitResolutionAttempted.Remove(definition.CareerId);
            RemoveCachedOutfit(definition.CareerId);
        }

        private static object FindArmorBaseItem(SetDefinition definition,
            SetPieceDefinition piece)
        {
            string cacheKey = definition.CareerId + "|" + piece.Slot;
            object cached;
            if (VisualItemByCareerSlot.TryGetValue(cacheKey, out cached) && cached != null)
            {
                if (IsExactSlotItem(cached, piece.Slot))
                    return cached;
                VisualItemByCareerSlot.Remove(cacheKey);
                ModLog.Error("Discarded stale wrong-slot visual cache for " +
                    definition.CareerId + " / " + piece.ItemName + ".");
            }

            VisualProfile profile;
            if (!VisualProfileByCareer.TryGetValue(definition.CareerId, out profile))
            {
                ModLog.Error("No visual profile exists for career " + definition.CareerId + ".");
                return null;
            }

            ResolveArmorOutfit(definition, profile);
            if (VisualItemByCareerSlot.TryGetValue(cacheKey, out cached) && cached != null &&
                IsExactSlotItem(cached, piece.Slot))
                return cached;

            LogOnce("visual-unresolved:" + cacheKey,
                "No thematically valid coherent armour base item resolved for " +
                definition.CareerId + " / " + piece.ItemName + " (slot=" + piece.Slot + ").");
            return null;
        }

        private static void ResolveArmorOutfit(SetDefinition definition, VisualProfile profile)
        {
            if (definition == null || profile == null)
                return;

            EnsureVisualResolverSession();

            bool completeCache = true;
            for (int i = 0; i < definition.Pieces.Length; i++)
            {
                SetPieceDefinition piece = definition.Pieces[i];
                object cached;
                if (!VisualItemByCareerSlot.TryGetValue(definition.CareerId + "|" + piece.Slot,
                    out cached) || cached == null || !IsExactSlotItem(cached, piece.Slot))
                {
                    completeCache = false;
                    break;
                }
            }
            if (completeCache)
                return;
            if (VisualOutfitResolutionAttempted.Contains(definition.CareerId))
                return;
            if (!IsVisualResolverReady())
                return;

            // Mark before doing any global catalogue work.  A failed career is a valid
            // cached result for this immutable campaign object catalogue and must not be
            // rescanned once per missing slot / UI action.
            VisualOutfitResolutionAttempted.Add(definition.CareerId);
            RemoveCachedOutfit(definition.CareerId);

            List<VisualOutfitCandidate> primaryCandidates = BuildVisualOutfitCandidates(
                definition, profile, false);
            List<VisualOutfitCandidate> secondaryCandidates = BuildVisualOutfitCandidates(
                definition, profile, true);
            bool usedSecondaryFallback = primaryCandidates.Count == 0 &&
                secondaryCandidates.Count > 0;
            List<VisualOutfitCandidate> candidates = primaryCandidates.Count > 0 ?
                primaryCandidates : secondaryCandidates;

            bool caster = IsCasterSet(definition);
            bool namedArchetypeMissing = candidates.Count == 0;
            List<VisualOutfitCandidate> automaticRoleCandidates = namedArchetypeMissing ?
                BuildAutomaticRoleOutfitCandidates(definition, profile) :
                new List<VisualOutfitCandidate>();
            if (candidates.Count == 0 && automaticRoleCandidates.Count > 0)
                candidates = automaticRoleCandidates;

            bool namedArchetypeUnavailable = namedArchetypeMissing;
            List<VisualOutfitCandidate> completionCandidates = MergeVisualCandidates(
                primaryCandidates, secondaryCandidates);
            completionCandidates = MergeVisualCandidates(completionCandidates,
                automaticRoleCandidates);
            // Build the global ItemObject fallback lazily. Real matching/role-compatible
            // CharacterObject outfits are authoritative and often already complete; doing
            // a full catalogue pass before we know one is needed caused avoidable CPU work.
            Dictionary<SetSlot, List<VisualCatalogCandidate>> strictCatalog = null;
            bool useCatalogOnlyFallback = candidates.Count == 0;

        CatalogOnlyFallback:
            // TOR's CharacterObject names are not a reliable public taxonomy. In the
            // user's live 1.16 catalogue Waywatcher, Spellsinger and Warden have no
            // CharacterObject whose exposed id/name matches our career phrases. The
            // previous early-return made the item-catalog fallback unreachable exactly
            // in that case. Build a complete outfit from culture-correct, slot-correct
            // items classified by the relic/career role instead.
            // Automatic role candidates are real TOR CharacterObject loadouts and must be
            // evaluated as coherent outfits. v1.7.12 accidentally remembered that the
            // *named* archetype list was empty and jumped straight to catalog-only
            // resolution even after staff/bow/polearm role candidates had been found.
            // That discarded the very Spellsinger loadout that could supply its matching
            // footwear and recreated the generic-leg fallback.
            if (useCatalogOnlyFallback)
            {
                strictCatalog = BuildStrictArmorCatalogPools(definition, profile, caster,
                    namedArchetypeMissing);
                Dictionary<SetSlot, object> catalogItems;
                float catalogWeight;
                int catalogScore;
                if (!TryResolveCatalogOnlyOutfit(definition, profile, caster, strictCatalog,
                    out catalogItems, out catalogWeight, out catalogScore))
                {
                    LogOnce("visual-no-catalog-outfit:" + definition.CareerId,
                        "No valid automatic TOR armour outfit could be resolved for " +
                        definition.CareerId + " from either character archetypes or the " +
                        "culture/role item catalogue.");
                    return;
                }

                string catalogSignature = BuildOutfitSignature(definition, catalogItems);
                if (!String.IsNullOrEmpty(catalogSignature))
                    VisualOutfitSignatureOwner[catalogSignature] = definition.CareerId;

                StringBuilder catalogResolved = new StringBuilder();
                for (int p = 0; p < definition.Pieces.Length; p++)
                {
                    SetSlot slot = definition.Pieces[p].Slot;
                    object item;
                    if (!catalogItems.TryGetValue(slot, out item) || item == null)
                        continue;
                    VisualItemByCareerSlot[definition.CareerId + "|" + slot] = item;
                    if (catalogResolved.Length > 0)
                        catalogResolved.Append(", ");
                    catalogResolved.Append(slot).Append("=")
                        .Append(Convert.ToString(GetProperty(item, "StringId")) ?? "<no-id>");
                }

                ModLog.Info("Resolved automatic culture/role outfit for " +
                    definition.CareerId + " without a named CharacterObject archetype: " +
                    catalogResolved + "; armorWeight=" +
                    catalogWeight.ToString("0.00",
                        System.Globalization.CultureInfo.InvariantCulture) +
                    (caster ? " <= " + CasterArmorWeightCap : String.Empty) +
                    "; score=" + catalogScore + ".");
                return;
            }

            VisualOutfitCandidate best = null;
            Dictionary<SetSlot, object> bestItems = null;
            int bestScore = Int32.MinValue;
            string bestSignature = null;

            for (int i = 0; i < candidates.Count; i++)
            {
                VisualOutfitCandidate candidate = candidates[i];
                Dictionary<SetSlot, object> items =
                    new Dictionary<SetSlot, object>(candidate.Items);
                if (caster)
                    OptimizeCasterOutfitWeight(definition, profile, completionCandidates, items);

                CompleteOutfitFromArchetypePool(definition, profile, caster, candidate,
                    completionCandidates, items);
                if (namedArchetypeMissing)
                    ImproveAutomaticRoleOutfitSlots(definition, profile, caster,
                        completionCandidates, items);

                // Preserve complete real TOR loadouts. Catalogue fallback is only needed
                // for a genuinely missing slot. Named archetypes may additionally request
                // one strict-theme upgrade pass (e.g. a dedicated White Wolf asset), while
                // automatic staff/bow/polearm role outfits stay intact instead of being
                // fragmented back into generic catalogue pieces.
                bool missingRequiredSlot = !HasAllRequiredOutfitSlots(definition, items);
                bool allowThemeUpgrade = !namedArchetypeMissing &&
                    OutfitNeedsStrictThemeUpgrade(definition, profile, items);
                if (missingRequiredSlot || allowThemeUpgrade)
                {
                    if (strictCatalog == null)
                        strictCatalog = BuildStrictArmorCatalogPools(definition, profile, caster,
                            namedArchetypeMissing);
                    if (missingRequiredSlot)
                        CompleteOutfitFromStrictCatalog(definition, profile, caster, items,
                            strictCatalog);
                    if (allowThemeUpgrade)
                        ImproveOutfitWithStrictThemeCatalog(definition, profile, caster, items,
                            strictCatalog);
                }
                if (caster)
                    OptimizeCasterOutfitWeight(definition, profile, completionCandidates, items);

                float totalWeight;
                if (!ValidateResolvedOutfit(definition, caster, items, out totalWeight))
                    continue;

                string signature = BuildOutfitSignature(definition, items);
                string owner;
                int duplicatePenalty = 0;
                if (!String.IsNullOrEmpty(signature) &&
                    VisualOutfitSignatureOwner.TryGetValue(signature, out owner) &&
                    !String.Equals(owner, definition.CareerId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    duplicatePenalty = 100000;
                }

                int score = candidate.Score - duplicatePenalty;
                if (caster)
                {
                    // Within the hard TOR-safe cap, prefer lighter coherent caster outfits.
                    // TOR mage careers are weight-sensitive. Once theme and slot validity
                    // are satisfied, preserve substantial headroom below the native ceiling
                    // instead of merely squeezing under it.
                    score += (int)Math.Max(0f,
                        (CasterArmorWeightCap - totalWeight) * 450f);
                    if (totalWeight > CasterPreferredArmorWeight)
                        score -= (int)((totalWeight - CasterPreferredArmorWeight) * 1200f);
                }

                if (score > bestScore || (score == bestScore &&
                    String.CompareOrdinal(signature ?? String.Empty,
                        bestSignature ?? String.Empty) < 0))
                {
                    best = candidate;
                    bestItems = items;
                    bestScore = score;
                    bestSignature = signature;
                }
            }

            if (best == null || bestItems == null)
            {
                // Candidate discovery does not prove that any candidate can satisfy every
                // required slot and the caster weight cap. Reuse the already bounded,
                // culture/role-filtered catalogue fallback before treating the career as
                // unresolved. This repairs persistent heroes created by earlier versions
                // without weakening the cap or accepting wrong-slot equipment.
                useCatalogOnlyFallback = true;
                goto CatalogOnlyFallback;
            }

            if (!String.IsNullOrEmpty(bestSignature))
                VisualOutfitSignatureOwner[bestSignature] = definition.CareerId;

            StringBuilder resolved = new StringBuilder();
            float resolvedWeight = 0f;
            for (int i = 0; i < definition.Pieces.Length; i++)
            {
                SetPieceDefinition piece = definition.Pieces[i];
                object item;
                if (!bestItems.TryGetValue(piece.Slot, out item) || item == null)
                    continue;
                VisualItemByCareerSlot[definition.CareerId + "|" + piece.Slot] = item;
                resolvedWeight += GetArmorWeight(item);
                if (resolved.Length > 0)
                    resolved.Append(", ");
                resolved.Append(piece.Slot).Append("=")
                    .Append(Convert.ToString(GetProperty(item, "StringId")) ?? "<no-id>");
            }

            ModLog.Info("Resolved coherent thematic outfit for " + definition.CareerId +
                " from " + DescribeCharacter(best.Character) + " / " + best.SourceKind +
                (usedSecondaryFallback ? " (secondary archetype fallback)" :
                    (namedArchetypeUnavailable ? " (automatic culture/weapon-role archetype)" :
                        String.Empty)) +
                ": " + resolved + "; armorWeight=" +
                resolvedWeight.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) +
                (caster ? " <= " + CasterArmorWeightCap : String.Empty) +
                "; score=" + bestScore + ".");
        }

        private static bool HasAllRequiredOutfitSlots(SetDefinition definition,
            Dictionary<SetSlot, object> items)
        {
            if (definition == null || items == null)
                return false;
            for (int i = 0; i < definition.Pieces.Length; i++)
            {
                object item;
                SetSlot slot = definition.Pieces[i].Slot;
                if (!items.TryGetValue(slot, out item) || item == null ||
                    !IsExactSlotItem(item, slot))
                    return false;
            }
            return true;
        }

        private static bool OutfitNeedsStrictThemeUpgrade(SetDefinition definition,
            VisualProfile profile, Dictionary<SetSlot, object> items)
        {
            if (definition == null || profile == null || items == null)
                return false;
            for (int i = 0; i < definition.Pieces.Length; i++)
            {
                object item;
                SetSlot slot = definition.Pieces[i].Slot;
                if (!items.TryGetValue(slot, out item) || item == null)
                    return true;
                // Only ask the catalogue to improve a clearly generic slot. A real
                // archetype item with meaningful career/set identity stays authoritative.
                if (ScoreStrictThemeIdentity(definition, profile, item) < 800)
                    return true;
            }
            return false;
        }

        private static void RemoveCachedOutfit(string careerId)
        {
            if (String.IsNullOrEmpty(careerId))
                return;
            List<string> keys = new List<string>();
            foreach (string key in VisualItemByCareerSlot.Keys)
            {
                if (key.StartsWith(careerId + "|", StringComparison.OrdinalIgnoreCase))
                    keys.Add(key);
            }
            for (int i = 0; i < keys.Count; i++)
                VisualItemByCareerSlot.Remove(keys[i]);

            List<string> signatures = new List<string>();
            foreach (KeyValuePair<string, string> pair in VisualOutfitSignatureOwner)
            {
                if (String.Equals(pair.Value, careerId, StringComparison.OrdinalIgnoreCase))
                    signatures.Add(pair.Key);
            }
            for (int i = 0; i < signatures.Count; i++)
                VisualOutfitSignatureOwner.Remove(signatures[i]);
        }

        private static List<VisualOutfitCandidate> BuildVisualOutfitCandidates(
            SetDefinition definition, VisualProfile profile, bool secondaryFallback)
        {
            List<VisualOutfitCandidate> result = new List<VisualOutfitCandidate>();
            IEnumerable characters = GetStaticProperty(
                TypeByName("TaleWorlds.CampaignSystem.CharacterObject"), "All") as IEnumerable;
            if (characters == null)
                return result;

            bool caster = IsCasterSet(definition);
            HashSet<string> archetypeItemIds = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            Dictionary<string, int> pairCounts = GetOrCreateVisualEquipmentPairCounts(
                definition.CareerId);

            foreach (object character in characters)
            {
                int characterScore = secondaryFallback ?
                    ScoreSecondaryVisualCharacter(character, profile) :
                    ScoreVisualCharacter(character, profile);
                if (characterScore == Int32.MinValue)
                    continue;
                characterScore += ScoreDefinitionThemeOnObject(definition,
                    character, 420) + ScoreDefinitionArchetypeAffinity(definition, character);

                RegisterAllEquipmentItemIds(character, archetypeItemIds);
                RegisterCharacterEquipmentPairCounts(character, pairCounts, 4, 1);
                AddEquipmentOutfitCandidates(result, definition, profile, caster,
                    character, characterScore, GetProperty(character, "BattleEquipments") as IEnumerable,
                    "battle", 1200);
                AddEquipmentOutfitCandidates(result, definition, profile, caster,
                    character, characterScore, GetProperty(character, "CivilianEquipments") as IEnumerable,
                    "civilian", 200);
                AddEquipmentOutfitCandidates(result, definition, profile, caster,
                    character, characterScore, GetProperty(character, "StealthEquipments") as IEnumerable,
                    "stealth", 100);

                // Some template characters expose only First*Equipment even when the enumerable
                // property is empty. Add those explicitly; outfit signatures deduplicate them.
                AddSingleEquipmentOutfitCandidate(result, definition, profile, caster,
                    character, characterScore, GetProperty(character, "FirstBattleEquipment"),
                    "first-battle", 1150);
                AddSingleEquipmentOutfitCandidate(result, definition, profile, caster,
                    character, characterScore, GetProperty(character, "FirstCivilianEquipment"),
                    "first-civilian", 150);
            }

            // Cache both populated and empty archetype sets. An empty result is still a
            // completed one-shot lookup; leaving it uncached would make relic resolution
            // rescan every CharacterObject once per candidate item for unmatched careers.
            HashSet<string> cachedArchetypeIds;
            if (!VisualArchetypeItemIdsByCareer.TryGetValue(definition.CareerId,
                out cachedArchetypeIds))
            {
                cachedArchetypeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                VisualArchetypeItemIdsByCareer[definition.CareerId] = cachedArchetypeIds;
            }
            cachedArchetypeIds.UnionWith(archetypeItemIds);

            result.Sort(delegate(VisualOutfitCandidate a, VisualOutfitCandidate b)
            {
                if (a.Score != b.Score)
                    return b.Score.CompareTo(a.Score);
                return String.CompareOrdinal(a.Signature ?? String.Empty,
                    b.Signature ?? String.Empty);
            });
            return result;
        }

        private static List<VisualOutfitCandidate> BuildAutomaticRoleOutfitCandidates(
            SetDefinition definition, VisualProfile profile)
        {
            List<VisualOutfitCandidate> result = new List<VisualOutfitCandidate>();
            IEnumerable characters = GetStaticProperty(
                TypeByName("TaleWorlds.CampaignSystem.CharacterObject"), "All") as IEnumerable;
            if (characters == null || definition == null || profile == null)
                return result;

            CareerItemDefinition relic = CareerUniqueRuntime.GetDefinitionForSet(
                definition.CareerId);
            bool caster = IsCasterSet(definition);
            HashSet<string> cultureItemIds = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            Dictionary<string, int> pairCounts = GetOrCreateVisualEquipmentPairCounts(
                definition.CareerId);

            foreach (object character in characters)
            {
                if (character == null || !CharacterMatchesVisualCulture(character, profile))
                    continue;

                // Culture membership is authoritative even when TOR's character StringId/name
                // does not expose the career taxonomy. Remember every item actually worn by
                // that culture so null-culture ItemObjects remain eligible in the bounded
                // catalogue fallback.
                RegisterAllEquipmentItemIds(character, cultureItemIds);

                int roleMatches = CountCompatibleRelicItemsOnCharacter(character, relic);
                if (roleMatches <= 0)
                    continue;

                string search = NormalizeSearch(
                    (Convert.ToString(GetProperty(character, "StringId")) ?? String.Empty) + " " +
                    (Convert.ToString(GetProperty(character, "Name")) ?? String.Empty));
                int negative = CountPhraseMatches(search, profile.NegativePhrases);
                int tier = Math.Max(0, EnumNumber(GetProperty(character, "Tier")));
                int coverage = CountArmorSlotCoverage(character);
                if (coverage == 0)
                    continue;

                // Co-equipment evidence must come from role-compatible characters only.
                // Counting every Wood Elf loadout made ubiquitous generic boots look more
                // "Spellsinger" than the footwear worn by actual staff/caster templates.
                RegisterCharacterEquipmentPairCounts(character, pairCounts, 5, 1);

                int characterScore = 2600 + roleMatches * 2400 + tier * 140 +
                    coverage * 500 + ScoreDefinitionThemeOnObject(definition, character, 280) -
                    negative * 2400;

                AddEquipmentOutfitCandidates(result, definition, profile, caster,
                    character, characterScore,
                    GetProperty(character, "BattleEquipments") as IEnumerable,
                    "automatic-role-battle", 900);
                AddEquipmentOutfitCandidates(result, definition, profile, caster,
                    character, characterScore,
                    GetProperty(character, "CivilianEquipments") as IEnumerable,
                    "automatic-role-civilian", 100);
                AddEquipmentOutfitCandidates(result, definition, profile, caster,
                    character, characterScore,
                    GetProperty(character, "StealthEquipments") as IEnumerable,
                    "automatic-role-stealth", 100);
                AddSingleEquipmentOutfitCandidate(result, definition, profile, caster,
                    character, characterScore, GetProperty(character, "FirstBattleEquipment"),
                    "automatic-role-first-battle", 850);
            }

            VisualCultureItemIdsByCareer[definition.CareerId] = cultureItemIds;
            HashSet<string> archetypeIds;
            if (!VisualArchetypeItemIdsByCareer.TryGetValue(definition.CareerId,
                out archetypeIds))
            {
                archetypeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                VisualArchetypeItemIdsByCareer[definition.CareerId] = archetypeIds;
            }
            for (int i = 0; i < result.Count; i++)
                if (result[i] != null && result[i].Character != null)
                    RegisterAllEquipmentItemIds(result[i].Character, archetypeIds);

            result.Sort(delegate(VisualOutfitCandidate a, VisualOutfitCandidate b)
            {
                if (a.Score != b.Score)
                    return b.Score.CompareTo(a.Score);
                return String.CompareOrdinal(a.Signature ?? String.Empty,
                    b.Signature ?? String.Empty);
            });
            return result;
        }

        private static bool CharacterMatchesVisualCulture(object character,
            VisualProfile profile)
        {
            if (character == null || profile == null)
                return false;
            object culture = GetProperty(character, "Culture");
            string cultureSearch = NormalizeSearch(
                (Convert.ToString(GetProperty(culture, "StringId")) ?? String.Empty) + " " +
                (Convert.ToString(GetProperty(culture, "Name")) ?? String.Empty));
            return CountPhraseMatches(cultureSearch, profile.CulturePhrases) > 0;
        }

        private static int CountCompatibleRelicItemsOnCharacter(object character,
            CareerItemDefinition relic)
        {
            if (character == null || relic == null)
                return 0;
            HashSet<string> compatibleIds = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            Action<object> inspectEquipment = delegate(object equipment)
            {
                if (equipment == null)
                    return;
                foreach (object element in EnumerateEquipmentElements(equipment))
                {
                    object item = GetProperty(element, "Item");
                    if (item == null || !CareerUniqueRuntime.IsBaseItemCompatible(relic, item))
                        continue;
                    string id = Convert.ToString(GetProperty(item, "StringId")) ?? String.Empty;
                    compatibleIds.Add(id.Length == 0 ? item.GetHashCode().ToString() : id);
                }
            };

            IEnumerable battle = GetProperty(character, "BattleEquipments") as IEnumerable;
            if (battle != null)
                foreach (object equipment in battle)
                    inspectEquipment(equipment);
            inspectEquipment(GetProperty(character, "FirstBattleEquipment"));
            return compatibleIds.Count;
        }

        private static void AddEquipmentOutfitCandidates(List<VisualOutfitCandidate> result,
            SetDefinition definition, VisualProfile profile, bool caster, object character,
            int characterScore, IEnumerable equipments, string sourceKind, int sourceBonus)
        {
            if (equipments == null)
                return;
            foreach (object equipment in equipments)
            {
                AddSingleEquipmentOutfitCandidate(result, definition, profile, caster,
                    character, characterScore, equipment, sourceKind, sourceBonus);
            }
        }

        private static void AddSingleEquipmentOutfitCandidate(
            List<VisualOutfitCandidate> result, SetDefinition definition,
            VisualProfile profile, bool caster, object character, int characterScore,
            object equipment, string sourceKind, int sourceBonus)
        {
            if (equipment == null)
                return;

            Dictionary<SetSlot, object> items = new Dictionary<SetSlot, object>();
            int itemScore = 0;
            foreach (object element in EnumerateEquipmentElements(equipment))
            {
                object item = GetProperty(element, "Item");
                if (item == null)
                    continue;

                for (int p = 0; p < definition.Pieces.Length; p++)
                {
                    SetSlot slot = definition.Pieces[p].Slot;
                    if (!IsExactSlotItem(item, slot))
                        continue;
                    if (caster && !IsCasterArmorCompatible(item, slot))
                        continue;
                    int score = ScoreVisualItem(item, slot, profile);
                    if (score == Int32.MinValue)
                        continue;

                    object previous;
                    if (!items.TryGetValue(slot, out previous) ||
                        ScoreVisualItem(previous, slot, profile) < score)
                    {
                        items[slot] = item;
                    }
                }
            }

            if (items.Count == 0)
                return;

            float weight = 0f;
            foreach (KeyValuePair<SetSlot, object> pair in items)
            {
                weight += GetArmorWeight(pair.Value);
                itemScore += ScoreVisualItem(pair.Value, pair.Key, profile) +
                    ScoreArmorQuality(pair.Value, pair.Key, caster) +
                    ScoreDefinitionThemeOnObject(definition, pair.Value, 160);
            }
            VisualOutfitCandidate candidate = new VisualOutfitCandidate
            {
                Character = character,
                Items = items,
                Coverage = items.Count,
                Weight = weight,
                SourceKind = sourceKind,
                Score = characterScore + sourceBonus + items.Count * 6500 + itemScore -
                    (caster && weight > CasterArmorWeightCap ?
                        (int)((weight - CasterArmorWeightCap) * 900f) : 0),
                Signature = BuildPartialOutfitSignature(items)
            };

            for (int i = 0; i < result.Count; i++)
            {
                if (String.Equals(result[i].Signature, candidate.Signature,
                    StringComparison.OrdinalIgnoreCase))
                {
                    if (candidate.Score > result[i].Score)
                        result[i] = candidate;
                    return;
                }
            }
            result.Add(candidate);
        }

        private static void CompleteOutfitFromArchetypePool(SetDefinition definition,
            VisualProfile profile, bool caster, VisualOutfitCandidate baseCandidate,
            List<VisualOutfitCandidate> candidates, Dictionary<SetSlot, object> items)
        {
            float currentWeight = GetOutfitWeight(items);
            for (int p = 0; p < definition.Pieces.Length; p++)
            {
                SetSlot slot = definition.Pieces[p].Slot;
                if (items.ContainsKey(slot))
                    continue;

                object best = null;
                int bestScore = Int32.MinValue;
                string bestId = null;
                for (int i = 0; i < candidates.Count; i++)
                {
                    object candidateItem;
                    if (!candidates[i].Items.TryGetValue(slot, out candidateItem) ||
                        candidateItem == null)
                        continue;

                    float itemWeight = GetArmorWeight(candidateItem);
                    if (caster && currentWeight + itemWeight >
                        CasterArmorWeightCap + 0.001f)
                        continue;

                    int score = ScoreVisualItem(candidateItem, slot, profile) +
                        ScoreArmorQuality(candidateItem, slot, caster) +
                        candidates[i].Score / 20;
                    if (Object.ReferenceEquals(candidates[i].Character,
                        baseCandidate.Character))
                        score += 1800;

                    string id = Convert.ToString(GetProperty(candidateItem, "StringId")) ??
                        String.Empty;
                    if (score > bestScore || (score == bestScore &&
                        String.CompareOrdinal(id, bestId ?? String.Empty) < 0))
                    {
                        best = candidateItem;
                        bestScore = score;
                        bestId = id;
                    }
                }

                if (best != null)
                {
                    items[slot] = best;
                    currentWeight += GetArmorWeight(best);
                }
            }
        }

        private static void ImproveAutomaticRoleOutfitSlots(SetDefinition definition,
            VisualProfile profile, bool caster, List<VisualOutfitCandidate> candidates,
            Dictionary<SetSlot, object> items)
        {
            if (definition == null || profile == null || candidates == null || items == null)
                return;
            object body;
            if (!items.TryGetValue(SetSlot.Body, out body) || body == null)
                return;
            float totalWeight = GetOutfitWeight(items);

            for (int p = 0; p < definition.Pieces.Length; p++)
            {
                SetSlot slot = definition.Pieces[p].Slot;
                if (slot == SetSlot.Body)
                    continue;
                object current;
                if (!items.TryGetValue(slot, out current) || current == null)
                    continue;

                int currentFamily = ScoreCatalogFamilyAffinity(definition.CareerId, body, current);
                int currentScore = currentFamily * 2 +
                    ScoreStrictThemeIdentity(definition, profile, current) +
                    ScoreVisualItem(current, slot, profile) +
                    ScoreArmorQuality(current, slot, caster);
                object best = current;
                int bestScore = currentScore;
                string bestId = Convert.ToString(GetProperty(current, "StringId")) ?? String.Empty;
                float currentWeight = GetArmorWeight(current);

                for (int i = 0; i < candidates.Count; i++)
                {
                    VisualOutfitCandidate source = candidates[i];
                    object candidate;
                    if (source == null || !source.Items.TryGetValue(slot, out candidate) ||
                        candidate == null || Object.ReferenceEquals(candidate, current))
                        continue;
                    if (caster && !IsCasterArmorCompatible(candidate, slot))
                        continue;
                    float candidateWeight = GetArmorWeight(candidate);
                    if (caster && totalWeight - currentWeight + candidateWeight >
                        CasterArmorWeightCap + 0.001f)
                        continue;

                    int family = ScoreCatalogFamilyAffinity(definition.CareerId, body, candidate);
                    // A role-pool improvement must still belong to the selected outfit
                    // family. This prevents another staff user's generic boots from winning
                    // only because they are lighter or common across the culture.
                    if (family + 500 < currentFamily)
                        continue;
                    int score = family * 2 +
                        ScoreStrictThemeIdentity(definition, profile, candidate) +
                        ScoreVisualItem(candidate, slot, profile) +
                        ScoreArmorQuality(candidate, slot, caster) + source.Score / 30;
                    string id = Convert.ToString(GetProperty(candidate, "StringId")) ??
                        String.Empty;
                    if (score > bestScore + 500 ||
                        (score == bestScore && String.CompareOrdinal(id, bestId) < 0))
                    {
                        best = candidate;
                        bestScore = score;
                        bestId = id;
                    }
                }

                if (!Object.ReferenceEquals(best, current))
                {
                    totalWeight = totalWeight - currentWeight + GetArmorWeight(best);
                    items[slot] = best;
                }
            }
        }

        private static void OptimizeCasterOutfitWeight(SetDefinition definition,
            VisualProfile profile, List<VisualOutfitCandidate> candidates,
            Dictionary<SetSlot, object> items)
        {
            if (definition == null || profile == null || candidates == null || items == null)
                return;

            float total = GetOutfitWeight(items);
            int guard = 0;
            // TOR's native hard ceiling is 11. Prefer a materially lighter complete
            // outfit so the generated caster set leaves practical headroom instead of
            // merely landing at 10.x. If the installed TOR catalogue cannot reach the
            // preferred target, validation still accepts any coherent outfit <= 11.
            // Never fragment a coherent TOR outfit merely to chase the optional 7.5
            // preference. Candidate scoring already rewards lighter outfits. Replacement
            // is allowed only when the selected outfit would violate TOR's hard 11-weight
            // caster restriction.
            while (total > CasterArmorWeightCap + 0.001f && guard++ < 12)
            {
                SetSlot replacementSlot = SetSlot.Head;
                object replacement = null;
                float bestSaving = 0f;
                int bestReplacementScore = Int32.MinValue;
                string bestId = null;

                object bodyAnchor = null;
                items.TryGetValue(SetSlot.Body, out bodyAnchor);
                for (int p = 0; p < definition.Pieces.Length; p++)
                {
                    SetSlot slot = definition.Pieces[p].Slot;
                    // Body is the outfit anchor and is never swapped by the emergency
                    // weight reducer. Pick a different coherent candidate outfit instead.
                    if (slot == SetSlot.Body)
                        continue;
                    object current;
                    if (!items.TryGetValue(slot, out current) || current == null)
                        continue;
                    float currentWeight = GetArmorWeight(current);
                    int currentFamily = bodyAnchor == null ? 0 :
                        ScoreCatalogFamilyAffinity(definition.CareerId, bodyAnchor, current);
                    int currentIdentity = ScoreStrictThemeIdentity(definition, profile, current);
                    int currentQuality = ScoreArmorQuality(current, slot, true);

                    for (int i = 0; i < candidates.Count; i++)
                    {
                        object candidateItem;
                        if (!candidates[i].Items.TryGetValue(slot, out candidateItem) ||
                            candidateItem == null || Object.ReferenceEquals(candidateItem, current))
                            continue;
                        float candidateWeight = GetArmorWeight(candidateItem);
                        float saving = currentWeight - candidateWeight;
                        if (saving <= 0.001f)
                            continue;

                        int candidateFamily = bodyAnchor == null ? 0 :
                            ScoreCatalogFamilyAffinity(definition.CareerId, bodyAnchor, candidateItem);
                        int candidateIdentity = ScoreStrictThemeIdentity(definition, profile, candidateItem);
                        int candidateQuality = ScoreArmorQuality(candidateItem, slot, true);
                        // A hard-cap rescue may trade some quality, but it may not throw
                        // away the TOR outfit-family signal or career identity.
                        if (candidateFamily + 400 < currentFamily ||
                            candidateIdentity + 300 < currentIdentity ||
                            candidateQuality + 900 < currentQuality)
                            continue;

                        int score = ScoreVisualItem(candidateItem, slot, profile) +
                            ScoreArmorQuality(candidateItem, slot, true) +
                            candidates[i].Score / 25;
                        string id = Convert.ToString(GetProperty(candidateItem, "StringId")) ??
                            String.Empty;

                        // Weight saving is authoritative until the TOR cap is met; quality
                        // and deterministic ID ordering break ties between similarly light
                        // thematic pieces.
                        if (saving > bestSaving + 0.001f ||
                            (Math.Abs(saving - bestSaving) <= 0.001f &&
                                (score > bestReplacementScore ||
                                (score == bestReplacementScore &&
                                    String.CompareOrdinal(id, bestId ?? String.Empty) < 0))))
                        {
                            replacementSlot = slot;
                            replacement = candidateItem;
                            bestSaving = saving;
                            bestReplacementScore = score;
                            bestId = id;
                        }
                    }
                }

                if (replacement == null)
                    break;
                items[replacementSlot] = replacement;
                total -= bestSaving;
            }
        }


        private static bool TryResolveCatalogOnlyOutfit(SetDefinition definition,
            VisualProfile profile, bool caster,
            Dictionary<SetSlot, List<VisualCatalogCandidate>> pools,
            out Dictionary<SetSlot, object> items, out float totalWeight, out int totalScore)
        {
            items = new Dictionary<SetSlot, object>();
            totalWeight = 0f;
            totalScore = 0;
            if (definition == null || profile == null || pools == null)
                return false;

            // Body is the visual anchor. Other slots receive a deterministic family
            // affinity bonus from shared non-generic StringId tokens, which keeps the
            // automatically selected pieces in the same TOR equipment family when the
            // catalogue exposes such a family. No item ids are hard-coded.
            SetSlot[] order = new[]
            {
                SetSlot.Body, SetSlot.Head, SetSlot.Cape, SetSlot.Hand, SetSlot.Leg
            };
            object anchor = null;

            for (int o = 0; o < order.Length; o++)
            {
                SetSlot slot = order[o];
                bool required = false;
                for (int p = 0; p < definition.Pieces.Length; p++)
                    if (definition.Pieces[p].Slot == slot)
                    {
                        required = true;
                        break;
                    }
                if (!required)
                    continue;

                List<VisualCatalogCandidate> candidates;
                if (!pools.TryGetValue(slot, out candidates) || candidates == null ||
                    candidates.Count == 0)
                    return false;

                VisualCatalogCandidate best = null;
                int bestScore = Int32.MinValue;
                for (int i = 0; i < candidates.Count; i++)
                {
                    VisualCatalogCandidate candidate = candidates[i];
                    if (candidate == null || candidate.Item == null)
                        continue;

                    if (caster)
                    {
                        float minimumRemaining = GetMinimumUnselectedCatalogWeight(
                            definition, pools, items, slot);
                        if (Single.IsPositiveInfinity(minimumRemaining) ||
                            totalWeight + candidate.Weight + minimumRemaining >
                                CasterArmorWeightCap + 0.001f)
                            continue;
                    }

                    int score = candidate.Score +
                        ScoreAutomaticArmorRole(definition, candidate.Item, slot, caster);
                    if (anchor != null && slot != SetSlot.Body)
                        score += ScoreCatalogFamilyAffinity(definition.CareerId, anchor, candidate.Item);

                    if (score > bestScore ||
                        (score == bestScore && String.CompareOrdinal(
                            candidate.StringId ?? String.Empty,
                            best == null ? String.Empty : best.StringId ?? String.Empty) < 0))
                    {
                        best = candidate;
                        bestScore = score;
                    }
                }

                if (best == null)
                    return false;

                items[slot] = best.Item;
                totalWeight += best.Weight;
                totalScore += bestScore;
                if (slot == SetSlot.Body)
                    anchor = best.Item;
            }

            if (caster)
            {
                OptimizeCasterOutfitWeightFromCatalog(definition, pools, items);
                totalWeight = GetOutfitWeight(items);
            }

            float validatedWeight;
            if (!ValidateResolvedOutfit(definition, caster, items, out validatedWeight))
                return false;
            totalWeight = validatedWeight;

            string signature = BuildOutfitSignature(definition, items);
            string existingOwner;
            if (!String.IsNullOrEmpty(signature) &&
                VisualOutfitSignatureOwner.TryGetValue(signature, out existingOwner) &&
                !String.Equals(existingOwner, definition.CareerId,
                    StringComparison.OrdinalIgnoreCase))
            {
                // An exact duplicate complete outfit is never considered a good automatic
                // result. Try one deterministic alternative in the body slot, which changes
                // the family anchor and therefore the other-slot ranking on the next pass.
                List<VisualCatalogCandidate> bodyPool;
                if (pools.TryGetValue(SetSlot.Body, out bodyPool) && bodyPool != null &&
                    bodyPool.Count > 1)
                {
                    object selectedBody = null;
                    items.TryGetValue(SetSlot.Body, out selectedBody);
                    Dictionary<SetSlot, object> alternative =
                        TryBuildAlternativeCatalogOutfit(definition, profile, caster, pools,
                            selectedBody);
                    float alternativeWeight;
                    if (alternative != null &&
                        ValidateResolvedOutfit(definition, caster, alternative,
                            out alternativeWeight))
                    {
                        string alternativeSignature =
                            BuildOutfitSignature(definition, alternative);
                        string alternativeOwner;
                        if (String.IsNullOrEmpty(alternativeSignature) ||
                            !VisualOutfitSignatureOwner.TryGetValue(alternativeSignature,
                                out alternativeOwner) ||
                            String.Equals(alternativeOwner, definition.CareerId,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            items = alternative;
                            totalWeight = alternativeWeight;
                            totalScore -= 1000;
                        }
                    }
                }
            }

            return true;
        }

        private static Dictionary<SetSlot, object> TryBuildAlternativeCatalogOutfit(
            SetDefinition definition, VisualProfile profile, bool caster,
            Dictionary<SetSlot, List<VisualCatalogCandidate>> pools, object excludedBody)
        {
            List<VisualCatalogCandidate> bodies;
            if (!pools.TryGetValue(SetSlot.Body, out bodies) || bodies == null)
                return null;

            for (int b = 0; b < bodies.Count; b++)
            {
                VisualCatalogCandidate body = bodies[b];
                if (body == null || body.Item == null ||
                    Object.ReferenceEquals(body.Item, excludedBody))
                    continue;

                Dictionary<SetSlot, object> result = new Dictionary<SetSlot, object>();
                result[SetSlot.Body] = body.Item;
                float weight = body.Weight;
                bool failed = false;
                for (int p = 0; p < definition.Pieces.Length; p++)
                {
                    SetSlot slot = definition.Pieces[p].Slot;
                    if (slot == SetSlot.Body)
                        continue;
                    List<VisualCatalogCandidate> candidates;
                    if (!pools.TryGetValue(slot, out candidates) || candidates == null ||
                        candidates.Count == 0)
                    {
                        failed = true;
                        break;
                    }

                    VisualCatalogCandidate best = null;
                    int bestScore = Int32.MinValue;
                    for (int i = 0; i < candidates.Count; i++)
                    {
                        VisualCatalogCandidate candidate = candidates[i];
                        if (candidate == null || candidate.Item == null)
                            continue;
                        if (caster)
                        {
                            float minimumRemaining = GetMinimumUnselectedCatalogWeight(
                                definition, pools, result, slot);
                            if (Single.IsPositiveInfinity(minimumRemaining) ||
                                weight + candidate.Weight + minimumRemaining >
                                    CasterArmorWeightCap + 0.001f)
                                continue;
                        }
                        int score = candidate.Score +
                            ScoreCatalogFamilyAffinity(definition.CareerId, body.Item, candidate.Item) +
                            ScoreAutomaticArmorRole(definition, candidate.Item, slot, caster);
                        if (score > bestScore)
                        {
                            best = candidate;
                            bestScore = score;
                        }
                    }
                    if (best == null)
                    {
                        failed = true;
                        break;
                    }
                    result[slot] = best.Item;
                    weight += best.Weight;
                }

                if (!failed)
                {
                    if (caster)
                        OptimizeCasterOutfitWeightFromCatalog(definition, pools, result);
                    float validated;
                    if (ValidateResolvedOutfit(definition, caster, result, out validated))
                        return result;
                }
            }
            return null;
        }

        private static float GetMinimumUnselectedCatalogWeight(SetDefinition definition,
            Dictionary<SetSlot, List<VisualCatalogCandidate>> pools,
            Dictionary<SetSlot, object> selected, SetSlot selectingNow)
        {
            float total = 0f;
            for (int p = 0; p < definition.Pieces.Length; p++)
            {
                SetSlot slot = definition.Pieces[p].Slot;
                if (slot == selectingNow || selected.ContainsKey(slot))
                    continue;
                List<VisualCatalogCandidate> candidates;
                if (!pools.TryGetValue(slot, out candidates) || candidates == null ||
                    candidates.Count == 0)
                    return Single.PositiveInfinity;
                float minimum = Single.PositiveInfinity;
                for (int i = 0; i < candidates.Count; i++)
                    if (candidates[i] != null && candidates[i].Weight < minimum)
                        minimum = candidates[i].Weight;
                if (Single.IsPositiveInfinity(minimum))
                    return Single.PositiveInfinity;
                total += minimum;
            }
            return total;
        }

        private static void OptimizeCasterOutfitWeightFromCatalog(SetDefinition definition,
            Dictionary<SetSlot, List<VisualCatalogCandidate>> pools,
            Dictionary<SetSlot, object> items)
        {
            float total = GetOutfitWeight(items);
            if (total <= CasterArmorWeightCap + 0.001f)
                return;

            VisualProfile profile;
            VisualProfileByCareer.TryGetValue(definition.CareerId, out profile);
            object anchor = null;
            items.TryGetValue(SetSlot.Body, out anchor);
            int guard = 0;
            while (total > CasterArmorWeightCap + 0.001f && guard++ < 12)
            {
                SetSlot bestSlot = SetSlot.Head;
                VisualCatalogCandidate best = null;
                float bestSaving = 0f;
                int bestScoreLoss = Int32.MaxValue;

                for (int p = 0; p < definition.Pieces.Length; p++)
                {
                    SetSlot slot = definition.Pieces[p].Slot;
                    // The body is the visual anchor. Never trade it away merely to chase
                    // the optional preferred-weight target.
                    if (slot == SetSlot.Body)
                        continue;
                    object current;
                    if (!items.TryGetValue(slot, out current) || current == null)
                        continue;
                    float currentWeight = GetArmorWeight(current);
                    List<VisualCatalogCandidate> candidates;
                    if (!pools.TryGetValue(slot, out candidates) || candidates == null)
                        continue;

                    int currentScore = 0;
                    for (int i = 0; i < candidates.Count; i++)
                        if (Object.ReferenceEquals(candidates[i].Item, current))
                        {
                            currentScore = candidates[i].Score;
                            break;
                        }
                    int currentAffinity = anchor == null ? 0 :
                        ScoreCatalogFamilyAffinity(definition.CareerId, anchor, current);
                    int currentIdentity = profile == null ? 0 :
                        ScoreStrictThemeIdentity(definition, profile, current);
                    int currentQuality = ScoreArmorQuality(current, slot, true);

                    for (int i = 0; i < candidates.Count; i++)
                    {
                        VisualCatalogCandidate candidate = candidates[i];
                        if (candidate == null || candidate.Item == null ||
                            Object.ReferenceEquals(candidate.Item, current))
                            continue;
                        float saving = currentWeight - candidate.Weight;
                        if (saving <= 0.001f)
                            continue;

                        // v1.7.11 could replace a correct Spellsinger head/boots with
                        // unrelated lightweight generic gear here. A weight optimization
                        // may now proceed only when outfit-family, authored identity and
                        // quality are all preserved to within a small tolerance.
                        int candidateAffinity = anchor == null ? 0 :
                            ScoreCatalogFamilyAffinity(definition.CareerId, anchor, candidate.Item);
                        if (candidateAffinity + 400 < currentAffinity)
                            continue;
                        int candidateIdentity = profile == null ? 0 :
                            ScoreStrictThemeIdentity(definition, profile, candidate.Item);
                        if (candidateIdentity + 300 < currentIdentity)
                            continue;
                        int candidateQuality = ScoreArmorQuality(candidate.Item, slot, true);
                        if (candidateQuality + 650 < currentQuality)
                            continue;

                        int scoreLoss = Math.Max(0, currentScore - candidate.Score);
                        if (saving > bestSaving + 0.001f ||
                            (Math.Abs(saving - bestSaving) <= 0.001f &&
                                scoreLoss < bestScoreLoss))
                        {
                            bestSlot = slot;
                            best = candidate;
                            bestSaving = saving;
                            bestScoreLoss = scoreLoss;
                        }
                    }
                }

                if (best == null)
                    break;
                items[bestSlot] = best.Item;
                total -= bestSaving;
            }
        }

        private static int ScoreCatalogFamilyAffinity(string careerId, object anchor, object candidate)
        {
            if (anchor == null || candidate == null)
                return 0;
            string anchorIdRaw = Convert.ToString(GetProperty(anchor, "StringId")) ?? String.Empty;
            string candidateIdRaw = Convert.ToString(GetProperty(candidate, "StringId")) ?? String.Empty;
            string anchorId = NormalizeSearch(anchorIdRaw);
            string candidateId = NormalizeSearch(candidateIdRaw);
            string[] anchorTokens = Tokenize(anchorId);
            string[] candidateTokens = Tokenize(candidateId);
            int score = 0;
            for (int i = 0; i < anchorTokens.Length; i++)
            {
                string token = anchorTokens[i];
                if (token.Length < 4 || IsGenericEquipmentFamilyToken(token))
                    continue;
                for (int j = 0; j < candidateTokens.Length; j++)
                {
                    if (String.Equals(token, candidateTokens[j],
                        StringComparison.OrdinalIgnoreCase))
                    {
                        score += 650;
                        break;
                    }
                }
            }

            // Item names are not a reliable TOR outfit taxonomy.  The strongest automatic
            // signal is whether two pieces are actually co-equipped by a same-culture TOR
            // CharacterObject.  This keeps heads/boots/capes attached to the body family
            // even when their StringIds share no useful words.
            Dictionary<string, int> pairCounts;
            if (!String.IsNullOrWhiteSpace(careerId) &&
                VisualEquipmentPairCountsByCareer.TryGetValue(careerId, out pairCounts))
            {
                int count;
                if (pairCounts.TryGetValue(BuildEquipmentPairKey(anchorIdRaw, candidateIdRaw),
                    out count) && count > 0)
                {
                    // Presence in a relevant TOR loadout is the strong signal. Raw
                    // frequency is deliberately weak: ubiquitous generic boots should not
                    // beat a rarer career-specific variant merely because more templates
                    // reuse them.
                    score += 8000 + Math.Min(2400, Math.Max(0, count - 1) * 300);
                }
            }
            return Math.Min(18000, score);
        }

        private static string BuildEquipmentPairKey(string first, string second)
        {
            first = first ?? String.Empty;
            second = second ?? String.Empty;
            return String.CompareOrdinal(first, second) <= 0 ?
                first + "\n" + second : second + "\n" + first;
        }

        private static bool IsGenericEquipmentFamilyToken(string token)
        {
            return String.IsNullOrEmpty(token) || token == "armor" || token == "armour" ||
                token == "body" || token == "head" || token == "helmet" ||
                token == "helm" || token == "cape" || token == "cloak" ||
                token == "glove" || token == "gloves" || token == "gauntlet" ||
                token == "boots" || token == "boot" || token == "legs" ||
                token == "leg" || token == "shoulder" || token == "tor" ||
                token == "item" || token == "light" || token == "heavy";
        }

        private static int ScoreAutomaticArmorRole(SetDefinition definition, object item,
            SetSlot slot, bool caster)
        {
            if (definition == null || item == null)
                return Int32.MinValue / 4;
            if (caster)
            {
                if (!IsCasterArmorCompatible(item, slot))
                    return Int32.MinValue / 4;
                int appearance = ScoreCasterAppearance(item, slot);
                // Sparse TOR ids/names must not make a valid light culture garment
                // impossible to select. Exact caster semantics still dominate the score;
                // this positive baseline only keeps lightweight non-heavy exact-slot gear
                // eligible when the live catalogue exposes no literal "spellsinger/robe"
                // token (notably head/cape/feet variants).
                return 500 + appearance - (int)(GetArmorWeight(item) * 85f);
            }

            CareerItemDefinition relic = CareerUniqueRuntime.GetDefinitionForSet(
                definition.CareerId);
            BaseKind kind = relic == null ? BaseKind.Sword : relic.Kind;
            string search = NormalizeSearch(
                (Convert.ToString(GetProperty(item, "StringId")) ?? String.Empty) + " " +
                (Convert.ToString(GetProperty(item, "Name")) ?? String.Empty));
            object armor = GetProperty(item, "ArmorComponent");
            string material = NormalizeSearch(Convert.ToString(
                GetProperty(armor, "MaterialType")) ?? String.Empty);
            float weight = GetArmorWeight(item);
            bool heavy = material.Contains("plate") || material.Contains("chain") ||
                ContainsAny(search, " plate", "cuirass", "chainmail", " chain mail",
                    "hauberk", "brigandine", "gromril", "heavy armor", "heavy armour");
            int casterLook = ScoreCasterAppearance(item, slot);

            if (kind == BaseKind.Bow)
            {
                if (heavy)
                    return -5000;
                int score = 900 - (int)(weight * 90f);
                if (material.Contains("leather") || material.Contains("cloth"))
                    score += 700;
                if (ContainsAny(search, "hood", "cloak", "ranger", "scout", "archer",
                    "hunter", "shadow", "glade", "leaf", "forest"))
                    score += 900;
                if (slot == SetSlot.Body && casterLook > 1800)
                    score -= 1200;
                return score;
            }

            // Melee/shield/polearm careers use a martial silhouette automatically.
            // Explicit caster robes are rejected for the body; otherwise high-quality
            // culture-correct armour remains eligible even when TOR's StringId does not
            // repeat the career name.
            if (slot == SetSlot.Body && casterLook > 1800)
                return -4500;
            int martial = 700;
            if (heavy)
                martial += 650;
            if (ContainsAny(search, "guard", "knight", "warrior", "hunter", "rider",
                "scale", "mail", "helm", "greave", "cuirass", "battle"))
                martial += 650;
            martial += Math.Min(900, Math.Max(0, ScoreArmorQuality(item, slot, false) / 4));
            return martial;
        }

        private static void CompleteOutfitFromStrictCatalog(SetDefinition definition,
            VisualProfile profile, bool caster, Dictionary<SetSlot, object> items,
            Dictionary<SetSlot, List<VisualCatalogCandidate>> pools)
        {
            if (pools == null)
                return;
            float currentWeight = GetOutfitWeight(items);
            object anchor = null;
            items.TryGetValue(SetSlot.Body, out anchor);
            for (int p = 0; p < definition.Pieces.Length; p++)
            {
                SetPieceDefinition piece = definition.Pieces[p];
                if (items.ContainsKey(piece.Slot))
                    continue;

                List<VisualCatalogCandidate> candidates;
                if (!pools.TryGetValue(piece.Slot, out candidates) || candidates == null)
                    continue;
                float remaining = caster ? CasterArmorWeightCap - currentWeight :
                    Single.MaxValue;
                VisualCatalogCandidate best = null;
                int bestScore = Int32.MinValue;
                for (int i = 0; i < candidates.Count; i++)
                {
                    VisualCatalogCandidate candidate = candidates[i];
                    if (candidate == null || candidate.Item == null)
                        continue;
                    if (caster && candidate.Weight > remaining + 0.001f)
                        continue;
                    int score = candidate.Score;
                    if (anchor != null && piece.Slot != SetSlot.Body)
                        score += ScoreCatalogFamilyAffinity(definition.CareerId, anchor,
                            candidate.Item);
                    if (score > bestScore || (score == bestScore &&
                        String.CompareOrdinal(candidate.StringId ?? String.Empty,
                            best == null ? String.Empty : best.StringId ?? String.Empty) < 0))
                    {
                        best = candidate;
                        bestScore = score;
                    }
                }
                if (best == null)
                    continue;
                items[piece.Slot] = best.Item;
                currentWeight += best.Weight;
                if (piece.Slot == SetSlot.Body)
                    anchor = best.Item;
            }
        }

        private static void ImproveOutfitWithStrictThemeCatalog(
            SetDefinition definition, VisualProfile profile, bool caster,
            Dictionary<SetSlot, object> items,
            Dictionary<SetSlot, List<VisualCatalogCandidate>> pools)
        {
            if (definition == null || profile == null || items == null || pools == null)
                return;

            float currentWeight = GetOutfitWeight(items);
            for (int p = 0; p < definition.Pieces.Length; p++)
            {
                SetSlot slot = definition.Pieces[p].Slot;
                object current;
                if (!items.TryGetValue(slot, out current) || current == null)
                    continue;

                List<VisualCatalogCandidate> candidates;
                if (!pools.TryGetValue(slot, out candidates) || candidates == null ||
                    candidates.Count == 0)
                    continue;

                int currentIdentity = ScoreStrictThemeIdentity(definition, profile, current);
                if (caster)
                    currentIdentity += ScoreCasterAppearance(current, slot) * 2;
                object bodyAnchor = null;
                items.TryGetValue(SetSlot.Body, out bodyAnchor);
                int currentFamily = bodyAnchor == null || slot == SetSlot.Body ? 0 :
                    ScoreCatalogFamilyAffinity(definition.CareerId, bodyAnchor, current);
                object best = current;
                int bestIdentity = currentIdentity;
                int bestQuality = ScoreArmorQuality(current, slot, caster);
                string bestId = Convert.ToString(GetProperty(current, "StringId")) ?? String.Empty;
                float currentItemWeight = GetArmorWeight(current);

                for (int i = 0; i < candidates.Count; i++)
                {
                    object candidate = candidates[i].Item;
                    if (candidate == null || Object.ReferenceEquals(candidate, current))
                        continue;
                    int identity = ScoreStrictThemeIdentity(definition, profile, candidate);
                    if (caster)
                        identity += ScoreCasterAppearance(candidate, slot) * 2;
                    int candidateFamily = bodyAnchor == null || slot == SetSlot.Body ? 0 :
                        ScoreCatalogFamilyAffinity(definition.CareerId, bodyAnchor, candidate);

                    // Do not fragment a coherent troop outfit for a marginal text match.
                    // Replacements are reserved for clear career/set-specific catalogue
                    // assets (e.g. "White Wolf" / "Wild Hunt" / "Spellsinger").
                    if (identity < currentIdentity + 400)
                        continue;
                    // A directly co-equipped TOR family piece is stronger evidence than
                    // loose item-name text. Only an overwhelmingly stronger authored
                    // identity may replace it with a piece that loses that family link.
                    if (currentFamily >= 4000 && candidateFamily + 2000 < currentFamily &&
                        identity < currentIdentity + 5000)
                        continue;

                    float candidateWeight = candidates[i].Weight;
                    if (caster && currentWeight - currentItemWeight + candidateWeight >
                        CasterArmorWeightCap + 0.001f)
                        continue;

                    int quality = ScoreArmorQuality(candidate, slot, caster);
                    string id = candidates[i].StringId ?? String.Empty;
                    if (identity > bestIdentity ||
                        (identity == bestIdentity && quality > bestQuality) ||
                        (identity == bestIdentity && quality == bestQuality &&
                            String.CompareOrdinal(id, bestId) < 0))
                    {
                        best = candidate;
                        bestIdentity = identity;
                        bestQuality = quality;
                        bestId = id;
                    }
                }

                if (!Object.ReferenceEquals(best, current))
                {
                    float newWeight = GetArmorWeight(best);
                    items[slot] = best;
                    currentWeight = currentWeight - currentItemWeight + newWeight;
                }
            }
        }

        private static int ScoreStrictThemeIdentity(SetDefinition definition,
            VisualProfile profile, object item)
        {
            if (definition == null || profile == null || item == null)
                return Int32.MinValue / 4;
            string search = NormalizeSearch(
                (Convert.ToString(GetProperty(item, "StringId")) ?? String.Empty) + " " +
                (Convert.ToString(GetProperty(item, "Name")) ?? String.Empty));
            int primary = CountPhraseMatches(search, profile.PrimaryPhrases);
            int secondary = CountPhraseMatches(search, profile.SecondaryPhrases);
            int negative = CountPhraseMatches(search, profile.NegativePhrases);
            int theme = ScoreDefinitionThemeOnSearch(definition, search);
            if (negative > 0 && primary == 0 && theme == 0)
                return Int32.MinValue / 4;
            return theme * 6 + primary * 2600 + secondary * 500 - negative * 2200;
        }

        // Build the catalogue fallback once per career resolution.  v1.7.9 performed a
        // complete ItemObject traversal for every missing slot of every candidate outfit,
        // which multiplied a bounded one-shot resolver into a very large CPU spike.
        private static HashSet<string> BuildCultureEquipmentItemIds(string careerId,
            VisualProfile profile)
        {
            HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            IEnumerable characters = GetStaticProperty(
                TypeByName("TaleWorlds.CampaignSystem.CharacterObject"), "All") as IEnumerable;
            if (characters == null || profile == null)
                return ids;
            foreach (object character in characters)
            {
                if (CharacterMatchesVisualCulture(character, profile))
                    RegisterAllEquipmentItemIds(character, ids);
            }
            return ids;
        }

        private static Dictionary<string, int> GetOrCreateVisualEquipmentPairCounts(
            string careerId)
        {
            Dictionary<string, int> pairCounts;
            if (!VisualEquipmentPairCountsByCareer.TryGetValue(careerId ?? String.Empty,
                out pairCounts))
            {
                pairCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                VisualEquipmentPairCountsByCareer[careerId ?? String.Empty] = pairCounts;
            }
            return pairCounts;
        }

        private static void RegisterCharacterEquipmentPairCounts(object character,
            Dictionary<string, int> pairCounts, int battleWeight, int auxiliaryWeight)
        {
            if (character == null || pairCounts == null)
                return;
            IEnumerable battle = GetProperty(character, "BattleEquipments") as IEnumerable;
            if (battle != null)
                foreach (object equipment in battle)
                    RegisterEquipmentPairCounts(equipment, pairCounts, battleWeight);
            RegisterEquipmentPairCounts(GetProperty(character, "FirstBattleEquipment"),
                pairCounts, battleWeight);
            if (auxiliaryWeight <= 0)
                return;
            IEnumerable civilian = GetProperty(character, "CivilianEquipments") as IEnumerable;
            if (civilian != null)
                foreach (object equipment in civilian)
                    RegisterEquipmentPairCounts(equipment, pairCounts, auxiliaryWeight);
            IEnumerable stealth = GetProperty(character, "StealthEquipments") as IEnumerable;
            if (stealth != null)
                foreach (object equipment in stealth)
                    RegisterEquipmentPairCounts(equipment, pairCounts, auxiliaryWeight);
        }

        private static void RegisterEquipmentPairCounts(object equipment,
            Dictionary<string, int> pairCounts, int weight)
        {
            if (equipment == null || pairCounts == null || weight <= 0)
                return;
            List<string> armorIds = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (object element in EnumerateEquipmentElements(equipment))
            {
                object item = GetProperty(element, "Item");
                if (item == null)
                    continue;
                bool armor = IsExactSlotItem(item, SetSlot.Head) ||
                    IsExactSlotItem(item, SetSlot.Body) ||
                    IsExactSlotItem(item, SetSlot.Cape) ||
                    IsExactSlotItem(item, SetSlot.Hand) ||
                    IsExactSlotItem(item, SetSlot.Leg);
                if (!armor)
                    continue;
                string id = Convert.ToString(GetProperty(item, "StringId")) ?? String.Empty;
                if (String.IsNullOrWhiteSpace(id) ||
                    id.StartsWith("torcu_", StringComparison.OrdinalIgnoreCase) ||
                    !seen.Add(id))
                    continue;
                armorIds.Add(id);
            }
            for (int i = 0; i < armorIds.Count; i++)
            {
                for (int j = i + 1; j < armorIds.Count; j++)
                {
                    string key = BuildEquipmentPairKey(armorIds[i], armorIds[j]);
                    int current;
                    pairCounts.TryGetValue(key, out current);
                    pairCounts[key] = current + weight;
                }
            }
        }

        private static Dictionary<SetSlot, List<VisualCatalogCandidate>>
            BuildStrictArmorCatalogPools(SetDefinition definition, VisualProfile profile,
            bool caster, bool allowAutomaticRoleFallback)
        {
            Dictionary<SetSlot, List<VisualCatalogCandidate>> result =
                new Dictionary<SetSlot, List<VisualCatalogCandidate>>();
            for (int p = 0; p < definition.Pieces.Length; p++)
                result[definition.Pieces[p].Slot] = new List<VisualCatalogCandidate>();

            IEnumerable all = GetAllItemObjects();
            if (all == null)
                return result;

            // Culture-wide equipment discovery is required only for the last-resort
            // automatic role fallback. Co-equipment affinity itself is built while we are
            // already scanning matching/role-compatible CharacterObjects above. v1.7.12
            // added a second full same-culture character/equipment traversal for every
            // career resolution; besides favoring ubiquitous generic boots, that was the
            // CPU regression reintroduced by the head/leg hotfix.
            HashSet<string> cultureEquipmentIds = null;
            if (allowAutomaticRoleFallback &&
                !VisualCultureItemIdsByCareer.TryGetValue(definition.CareerId,
                    out cultureEquipmentIds))
            {
                cultureEquipmentIds = BuildCultureEquipmentItemIds(
                    definition.CareerId, profile);
                VisualCultureItemIdsByCareer[definition.CareerId] = cultureEquipmentIds;
            }

            foreach (object item in all)
            {
                if (item == null || ToBoolean(GetProperty(item, "IsCraftedByPlayer")))
                    continue;

                string id = Convert.ToString(GetProperty(item, "StringId")) ?? String.Empty;
                string name = Convert.ToString(GetProperty(item, "Name")) ?? String.Empty;
                string itemSearch = NormalizeSearch(id + " " + name);
                object culture = GetProperty(item, "Culture");
                string cultureSearch = NormalizeSearch(
                    (Convert.ToString(GetProperty(culture, "StringId")) ?? String.Empty) + " " +
                    (Convert.ToString(GetProperty(culture, "Name")) ?? String.Empty));
                int cultureMatches = CountPhraseMatches(cultureSearch, profile.CulturePhrases) +
                    CountPhraseMatches(itemSearch, profile.CulturePhrases) +
                    CountDefinitionCultureTokenMatches(definition, itemSearch);
                if (allowAutomaticRoleFallback && cultureEquipmentIds != null &&
                    cultureEquipmentIds.Contains(id))
                    cultureMatches += 2;
                int definitionTheme = ScoreDefinitionThemeOnSearch(definition, itemSearch);
                if (cultureMatches == 0 && definitionTheme == 0)
                    continue;

                int primary = CountPhraseMatches(itemSearch, profile.PrimaryPhrases);
                int secondary = CountPhraseMatches(itemSearch, profile.SecondaryPhrases);
                int negative = CountPhraseMatches(itemSearch, profile.NegativePhrases);
                if (negative > 0 && primary == 0 && definitionTheme == 0)
                    continue;

                for (int p = 0; p < definition.Pieces.Length; p++)
                {
                    SetPieceDefinition piece = definition.Pieces[p];
                    if (!IsExactSlotItem(item, piece.Slot))
                        continue;
                    if (caster && !IsCasterArmorCompatible(item, piece.Slot))
                        continue;

                    // Exact career/set semantics are preferred.  For a missing caster
                    // slot, a culture-correct lightweight robe/hood/cloth piece is a safe
                    // automatic fallback even when TOR's StringId does not contain the
                    // career name (the v1.7.9 Spellsinger-head failure).
                    int casterVisual = caster ? ScoreCasterAppearance(item, piece.Slot) : 0;
                    int automaticRole = allowAutomaticRoleFallback ?
                        ScoreAutomaticArmorRole(definition, item, piece.Slot, caster) :
                        Int32.MinValue / 4;
                    bool roleMatched = primary > 0 || secondary > 0 || definitionTheme > 0;
                    bool roleFallbackMatched = allowAutomaticRoleFallback &&
                        automaticRole > 0;
                    if (!roleMatched && !roleFallbackMatched &&
                        (!caster || casterVisual <= 0))
                        continue;

                    int baseScore = ScoreVisualItem(item, piece.Slot, profile);
                    if (baseScore == Int32.MinValue)
                        continue;
                    int score = baseScore + ScoreArmorQuality(item, piece.Slot, caster) +
                        cultureMatches * 900 + primary * 1600 + secondary * 450 +
                        definitionTheme * 4 +
                        (roleFallbackMatched ? automaticRole : 0);
                    result[piece.Slot].Add(new VisualCatalogCandidate
                    {
                        Item = item,
                        Score = score,
                        Weight = GetArmorWeight(item),
                        StringId = id
                    });
                }
            }

            foreach (List<VisualCatalogCandidate> list in result.Values)
            {
                list.Sort(delegate(VisualCatalogCandidate a, VisualCatalogCandidate b)
                {
                    if (a.Score != b.Score)
                        return b.Score.CompareTo(a.Score);
                    return String.CompareOrdinal(a.StringId ?? String.Empty,
                        b.StringId ?? String.Empty);
                });
            }
            return result;
        }


        private static int CountDefinitionCultureTokenMatches(SetDefinition definition,
            string normalizedSearch)
        {
            if (definition == null || definition.FactionTokens == null ||
                String.IsNullOrEmpty(normalizedSearch))
                return 0;
            int matches = 0;
            for (int i = 0; i < definition.FactionTokens.Length; i++)
            {
                string token = NormalizeSearch(definition.FactionTokens[i] ?? String.Empty);
                if (token.Length >= 3 && normalizedSearch.Contains(token))
                    matches++;
            }
            return matches;
        }

        private static IEnumerable GetAllItemObjects()
        {
            Type managerType = TypeByName("TaleWorlds.ObjectSystem.MBObjectManager");
            Type itemType = TypeByName("TaleWorlds.Core.ItemObject");
            object manager = GetStaticProperty(managerType, "Instance");
            if (managerType == null || itemType == null || manager == null)
                return null;
            MethodInfo[] methods = managerType.GetMethods(
                BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (method.Name == "GetObjectTypeList" && method.IsGenericMethodDefinition &&
                    method.GetParameters().Length == 0)
                    return method.MakeGenericMethod(itemType).Invoke(manager, null) as IEnumerable;
            }
            return null;
        }

        private static List<VisualOutfitCandidate> MergeVisualCandidates(
            List<VisualOutfitCandidate> primary, List<VisualOutfitCandidate> secondary)
        {
            List<VisualOutfitCandidate> result = new List<VisualOutfitCandidate>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Action<List<VisualOutfitCandidate>> add = delegate(List<VisualOutfitCandidate> source)
            {
                if (source == null)
                    return;
                for (int i = 0; i < source.Count; i++)
                {
                    string key = source[i].Signature ?? String.Empty;
                    if (seen.Add(key))
                        result.Add(source[i]);
                }
            };
            add(primary);
            add(secondary);
            return result;
        }

        private static void EnsureVisualResolverSession()
        {
            object session = GetStaticProperty(
                TypeByName("TaleWorlds.CampaignSystem.Campaign"), "Current");
            if (Object.ReferenceEquals(session, _visualResolverSession))
                return;
            _visualResolverSession = session;
            VisualSourceByCareer.Clear();
            VisualItemByCareerSlot.Clear();
            VisualOutfitSignatureOwner.Clear();
            VisualArchetypeItemIdsByCareer.Clear();
            VisualCultureItemIdsByCareer.Clear();
            VisualEquipmentPairCountsByCareer.Clear();
            VisualOutfitResolutionAttempted.Clear();
            VisualMigrationAttemptedItemIds.Clear();
            _visualMigrationPassCompleted = false;
            _visualAuditAttempted = false;
            _visualAuditRetryDelay = 0;
            _lastVisualAuditFailureKey = null;
        }

        private static bool IsVisualResolverReady()
        {
            IEnumerable characters = GetStaticProperty(
                TypeByName("TaleWorlds.CampaignSystem.CharacterObject"), "All") as IEnumerable;
            if (characters == null)
                return false;
            foreach (object character in characters)
                if (character != null)
                    return GetStaticProperty(TypeByName("TaleWorlds.ObjectSystem.MBObjectManager"),
                        "Instance") != null;
            return false;
        }

        private static bool ValidateResolvedOutfit(SetDefinition definition, bool caster,
            Dictionary<SetSlot, object> items, out float totalWeight)
        {
            totalWeight = 0f;
            if (items == null)
                return false;
            for (int i = 0; i < definition.Pieces.Length; i++)
            {
                SetSlot slot = definition.Pieces[i].Slot;
                object item;
                if (!items.TryGetValue(slot, out item) || item == null ||
                    !IsExactSlotItem(item, slot))
                    return false;
                float itemWeight = 0f;
                if (caster && !TryGetArmorWeight(item, out itemWeight))
                    return false;
                if (caster && !IsCasterArmorCompatible(item, slot))
                    return false;
                if (!caster)
                    itemWeight = GetArmorWeight(item);
                totalWeight += itemWeight;
            }
            return !caster || totalWeight <= CasterArmorWeightCap + 0.001f;
        }

        private static bool IsCasterSet(SetDefinition definition)
        {
            if (definition == null)
                return false;
            CareerItemDefinition relic = CareerUniqueRuntime.GetDefinitionForSet(
                definition.CareerId);
            return relic != null && relic.Kind == BaseKind.Staff;
        }

        private static bool TryGetArmorWeight(object item, out float weight)
        {
            weight = 0f;
            if (item == null)
                return false;
            try
            {
                object value = GetProperty(item, "Weight");
                if (value == null)
                    return false;
                weight = Math.Max(0f, Convert.ToSingle(value,
                    System.Globalization.CultureInfo.InvariantCulture));
                return !Single.IsNaN(weight) && !Single.IsInfinity(weight);
            }
            catch
            {
                weight = 0f;
                return false;
            }
        }

        private static float GetArmorWeight(object item)
        {
            float weight;
            return TryGetArmorWeight(item, out weight) ? weight : 0f;
        }

        private static float GetOutfitWeight(Dictionary<SetSlot, object> items)
        {
            float weight = 0f;
            if (items == null)
                return weight;
            foreach (KeyValuePair<SetSlot, object> pair in items)
                weight += GetArmorWeight(pair.Value);
            return weight;
        }

        private static int ScoreArmorQuality(object item, SetSlot slot, bool caster)
        {
            if (item == null)
                return 0;
            int tier = Math.Max(0, EnumNumber(GetProperty(item, "Tier")));
            int value = Math.Max(0, EnumNumber(GetProperty(item, "Value")));
            object armor = GetProperty(item, "ArmorComponent");
            int protection = 0;
            if (armor != null)
            {
                protection += Math.Max(0, EnumNumber(GetProperty(armor, "HeadArmor")));
                protection += Math.Max(0, EnumNumber(GetProperty(armor, "BodyArmor")));
                protection += Math.Max(0, EnumNumber(GetProperty(armor, "ArmArmor")));
                protection += Math.Max(0, EnumNumber(GetProperty(armor, "LegArmor")));
            }

            int score = Math.Min(7, tier) * 240 + Math.Min(450, value / 500) +
                Math.Min(900, protection * 9);
            if (caster)
            {
                score -= (int)(GetArmorWeight(item) * 220f);
                score += ScoreCasterAppearance(item, slot);
            }
            return score;
        }

        private static int ScoreCasterAppearance(object item, SetSlot slot)
        {
            if (item == null)
                return Int32.MinValue / 4;
            string search = NormalizeSearch(
                (Convert.ToString(GetProperty(item, "StringId")) ?? String.Empty) + " " +
                (Convert.ToString(GetProperty(item, "Name")) ?? String.Empty));
            object armor = GetProperty(item, "ArmorComponent");
            string material = NormalizeSearch(Convert.ToString(
                GetProperty(armor, "MaterialType")) ?? String.Empty);

            int score = 0;
            if (ContainsAny(search, " robe", "robes", " gown", "dress", "vestment",
                "spell", "mage", "wizard", "singer", "weaver", "druid"))
                score += 2200;
            // Hood/cowl/circlet/boots/sandals describe a slot silhouette, not a career.
            // Giving those generic words the old +2200 bonus is what let low-tier stock
            // headgear/boots beat opaque but actually co-equipped TOR set pieces.
            if (ContainsAny(search, "tunic", "cloth", "cowl", "hood", "circlet",
                "slipper", "sandal"))
                score += 500;
            if (material.Contains("cloth")) score += 1100;
            else if (material.Contains("leather")) score += 450;
            if (material.Contains("plate")) score -= 4200;
            if (material.Contains("chain")) score -= 3000;
            if (ContainsAny(search, " plate", "cuirass", "chainmail", " chain mail",
                "hauberk", "brigandine", "gromril", "heavy armor", "heavy armour"))
                score -= 3200;
            if (slot == SetSlot.Body && score == 0)
                score -= 500;
            return score;
        }

        private static bool IsCasterArmorCompatible(object item, SetSlot slot)
        {
            if (item == null)
                return false;
            string search = NormalizeSearch(
                (Convert.ToString(GetProperty(item, "StringId")) ?? String.Empty) + " " +
                (Convert.ToString(GetProperty(item, "Name")) ?? String.Empty));
            object armor = GetProperty(item, "ArmorComponent");
            string material = NormalizeSearch(Convert.ToString(
                GetProperty(armor, "MaterialType")) ?? String.Empty);
            bool explicitCaster = ContainsAny(search, " robe", "robes", " gown", "dress",
                "vestment", "tunic", "cloth", "spell", "mage", "wizard", "singer",
                "weaver", "druid", "cowl", "hood", "circlet", "slipper", "sandal");
            bool heavyMaterial = material.Contains("plate") || material.Contains("chain");
            bool heavyName = ContainsAny(search, " plate", "cuirass", "chainmail",
                " chain mail", "hauberk", "brigandine", "gromril", "heavy armor",
                "heavy armour");
            if ((heavyMaterial || heavyName) && !explicitCaster)
                return false;
            return true;
        }

        private static string BuildOutfitSignature(SetDefinition definition,
            Dictionary<SetSlot, object> items)
        {
            if (definition == null || items == null)
                return String.Empty;
            StringBuilder builder = new StringBuilder();
            for (int slotNumber = 0; slotNumber <= 4; slotNumber++)
            {
                SetSlot slot = (SetSlot)slotNumber;
                bool required = false;
                for (int i = 0; i < definition.Pieces.Length; i++)
                {
                    if (definition.Pieces[i].Slot == slot)
                    {
                        required = true;
                        break;
                    }
                }
                if (!required)
                    continue;
                object item;
                if (!items.TryGetValue(slot, out item) || item == null)
                    return String.Empty;
                if (builder.Length > 0)
                    builder.Append('|');
                builder.Append(slot).Append('=')
                    .Append(Convert.ToString(GetProperty(item, "StringId")) ?? String.Empty);
            }
            return builder.ToString();
        }

        private static string BuildPartialOutfitSignature(Dictionary<SetSlot, object> items)
        {
            if (items == null || items.Count == 0)
                return String.Empty;
            StringBuilder builder = new StringBuilder();
            for (int slotNumber = 0; slotNumber <= 4; slotNumber++)
            {
                object item;
                if (!items.TryGetValue((SetSlot)slotNumber, out item) || item == null)
                    continue;
                if (builder.Length > 0)
                    builder.Append('|');
                builder.Append((SetSlot)slotNumber).Append('=')
                    .Append(Convert.ToString(GetProperty(item, "StringId")) ?? String.Empty);
            }
            return builder.ToString();
        }

        private static int ScoreDefinitionThemeOnObject(SetDefinition definition,
            object value, int perMatch)
        {
            if (definition == null || value == null || perMatch <= 0)
                return 0;
            string search = NormalizeSearch(
                (Convert.ToString(GetProperty(value, "StringId")) ?? String.Empty) + " " +
                (Convert.ToString(GetProperty(value, "Name")) ?? String.Empty));
            List<string> words = SignificantThemeWords(definition.SetName);
            int matches = 0;
            for (int i = 0; i < words.Count; i++)
            {
                if (IsDefinitionFactionThemeWord(definition, words[i]))
                    continue;
                if (search.IndexOf(words[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    matches++;
            }
            return matches * perMatch;
        }

        // Strongly rank the TOR archetype whose own name expresses the authored set
        // identity.  This is derived from the career/set text, not from hard-coded item
        // StringIds.  Example: "Raiment of the White Wolf" automatically makes an
        // actual "Knight of the White Wolf" template outrank a generic Priest of Ulric.
        private static int ScoreDefinitionArchetypeAffinity(SetDefinition definition,
            object character)
        {
            if (definition == null || character == null)
                return 0;
            string search = NormalizeSearch(
                (Convert.ToString(GetProperty(character, "StringId")) ?? String.Empty) + " " +
                (Convert.ToString(GetProperty(character, "Name")) ?? String.Empty));
            int score = ScoreDefinitionThemeOnSearch(definition, search) * 6;

            string careerPhrase = NormalizeSearch(HumanizeIdentifier(definition.CareerId));
            if (!String.IsNullOrWhiteSpace(careerPhrase) && search.Contains(careerPhrase))
                score += 7000;
            else
            {
                string[] careerTokens = Tokenize(careerPhrase);
                int matched = 0;
                for (int i = 0; i < careerTokens.Length; i++)
                    if (careerTokens[i].Length >= 3 && search.Contains(careerTokens[i]))
                        matched++;
                if (careerTokens.Length > 0 && matched == careerTokens.Length)
                    score += 4200;
                else
                    score += matched * 650;
            }
            return score;
        }

        private static int ScoreDefinitionThemeOnSearch(SetDefinition definition,
            string normalizedSearch)
        {
            if (definition == null || String.IsNullOrEmpty(normalizedSearch))
                return 0;
            List<string> words = SignificantThemeWords(definition.SetName);
            // Faction/location words (e.g. "Athel Loren") identify culture, not the
            // career. Treating them as set-specific identity made generic high-tier
            // same-faction knight helmets outrank Waywatcher headgear.
            for (int i = words.Count - 1; i >= 0; i--)
                if (IsDefinitionFactionThemeWord(definition, words[i]))
                    words.RemoveAt(i);
            int score = 0;
            for (int i = 0; i < words.Count; i++)
                if (normalizedSearch.Contains(words[i]))
                    score += 180;
            for (int i = 0; i + 1 < words.Count; i++)
            {
                string phrase = words[i] + " " + words[i + 1];
                if (normalizedSearch.Contains(phrase))
                    score += 1800;
            }
            for (int i = 0; i + 2 < words.Count; i++)
            {
                string phrase = words[i] + " " + words[i + 1] + " " + words[i + 2];
                if (normalizedSearch.Contains(phrase))
                    score += 2800;
            }
            return score;
        }

        private static bool IsDefinitionFactionThemeWord(SetDefinition definition,
            string word)
        {
            if (definition == null || definition.FactionTokens == null ||
                String.IsNullOrWhiteSpace(word))
                return false;
            for (int i = 0; i < definition.FactionTokens.Length; i++)
            {
                string[] tokens = Tokenize(NormalizeSearch(
                    definition.FactionTokens[i] ?? String.Empty));
                for (int j = 0; j < tokens.Length; j++)
                    if (String.Equals(tokens[j], word, StringComparison.OrdinalIgnoreCase))
                        return true;
            }
            return false;
        }

        private static List<string> SignificantThemeWords(string value)
        {
            string[] words = Tokenize(value);
            List<string> result = new List<string>();
            for (int i = 0; i < words.Length; i++)
            {
                string word = words[i];
                if (word.Length < 3 || IsGenericThemeWord(word))
                    continue;
                result.Add(word);
            }
            return result;
        }

        private static bool IsGenericThemeWord(string word)
        {
            return String.Equals(word, "the", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(word, "and", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(word, "for", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(word, "set", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(word, "garb", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(word, "gear", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(word, "raiment", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(word, "regalia", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(word, "panoply", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(word, "vestments", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(word, "trappings", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(word, "weave", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(word, "armor", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(word, "armour", StringComparison.OrdinalIgnoreCase);
        }

        private static string HumanizeIdentifier(string value)
        {
            if (String.IsNullOrEmpty(value))
                return String.Empty;
            StringBuilder text = new StringBuilder(value.Length + 8);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (i > 0 && Char.IsUpper(c) &&
                    (Char.IsLower(value[i - 1]) || Char.IsDigit(value[i - 1])))
                    text.Append(' ');
                text.Append(c);
            }
            return text.ToString();
        }

        private static int ScoreSecondaryVisualCharacter(object character,
            VisualProfile profile)
        {
            if (character == null)
                return Int32.MinValue;

            object culture = GetProperty(character, "Culture");
            string cultureSearch = NormalizeSearch(
                (Convert.ToString(GetProperty(culture, "StringId")) ?? String.Empty) + " " +
                (Convert.ToString(GetProperty(culture, "Name")) ?? String.Empty));
            int cultureMatches = CountPhraseMatches(cultureSearch, profile.CulturePhrases);
            if (cultureMatches == 0)
                return Int32.MinValue;

            string search = NormalizeSearch(
                (Convert.ToString(GetProperty(character, "StringId")) ?? String.Empty) + " " +
                (Convert.ToString(GetProperty(character, "Name")) ?? String.Empty));
            int primary = CountPhraseMatches(search, profile.PrimaryPhrases);
            int secondary = CountPhraseMatches(search, profile.SecondaryPhrases);
            int negative = CountPhraseMatches(search, profile.NegativePhrases);
            if (primary > 0)
                return ScoreVisualCharacter(character, profile);
            if (secondary == 0 || negative > 0)
                return Int32.MinValue;

            int tier = 0;
            try { tier = Convert.ToInt32(GetProperty(character, "Tier")); }
            catch { tier = 0; }
            int coverage = CountArmorSlotCoverage(character);
            if (coverage == 0)
                return Int32.MinValue;
            return 2200 + cultureMatches * 650 + secondary * 700 +
                tier * 120 + coverage * 320;
        }

        private static void RegisterAllEquipmentItemIds(object character,
            HashSet<string> ids)
        {
            if (character == null || ids == null)
                return;
            RegisterEquipmentItemIds(GetProperty(character, "BattleEquipments") as IEnumerable, ids);
            RegisterEquipmentItemIds(GetProperty(character, "CivilianEquipments") as IEnumerable, ids);
            RegisterEquipmentItemIds(GetProperty(character, "StealthEquipments") as IEnumerable, ids);
            RegisterSingleEquipmentItemIds(GetProperty(character, "FirstBattleEquipment"), ids);
            RegisterSingleEquipmentItemIds(GetProperty(character, "FirstCivilianEquipment"), ids);
            RegisterSingleEquipmentItemIds(GetProperty(character, "FirstStealthEquipment"), ids);
        }

        private static void RegisterEquipmentItemIds(IEnumerable equipments,
            HashSet<string> ids)
        {
            if (equipments == null)
                return;
            foreach (object equipment in equipments)
            {
                foreach (object element in EnumerateEquipmentElements(equipment))
                {
                    object item = GetProperty(element, "Item");
                    string id = Convert.ToString(GetProperty(item, "StringId")) ?? String.Empty;
                    if (!String.IsNullOrWhiteSpace(id) &&
                        !id.StartsWith("torcu_", StringComparison.OrdinalIgnoreCase))
                        ids.Add(id);
                }
            }
        }

        private static void RegisterSingleEquipmentItemIds(object equipment,
            HashSet<string> ids)
        {
            if (equipment == null || ids == null)
                return;
            foreach (object element in EnumerateEquipmentElements(equipment))
            {
                object item = GetProperty(element, "Item");
                string id = Convert.ToString(GetProperty(item, "StringId")) ?? String.Empty;
                if (!String.IsNullOrWhiteSpace(id) &&
                    !id.StartsWith("torcu_", StringComparison.OrdinalIgnoreCase))
                    ids.Add(id);
            }
        }

        internal static int GetVisualArchetypeItemAffinity(string careerId, object item)
        {
            if (String.IsNullOrWhiteSpace(careerId) || item == null)
                return 0;
            EnsureVisualResolverSession();
            VisualProfile profile;
            if (!VisualProfileByCareer.TryGetValue(careerId, out profile))
                return 0;

            HashSet<string> ids;
            if (!VisualArchetypeItemIdsByCareer.TryGetValue(careerId, out ids))
            {
                ids = BuildVisualArchetypeItemIds(profile, false);
                if (ids.Count == 0)
                    ids = BuildVisualArchetypeItemIds(profile, true);
                // Empty is a valid cached result and must not trigger an O(items * characters)
                // rescan when a TOR installation has no matching archetype character.
                VisualArchetypeItemIdsByCareer[careerId] = ids;
            }

            string id = Convert.ToString(GetProperty(item, "StringId")) ?? String.Empty;
            if (ids.Contains(id))
                return 2200;

            object culture = GetProperty(item, "Culture");
            string cultureSearch = NormalizeSearch(
                (Convert.ToString(GetProperty(culture, "StringId")) ?? String.Empty) + " " +
                (Convert.ToString(GetProperty(culture, "Name")) ?? String.Empty));
            string itemSearch = NormalizeSearch(id + " " +
                (Convert.ToString(GetProperty(item, "Name")) ?? String.Empty));
            int cultureMatches = CountPhraseMatches(cultureSearch, profile.CulturePhrases) +
                CountPhraseMatches(itemSearch, profile.CulturePhrases);
            if (cultureMatches == 0)
                return 0;
            int primary = CountPhraseMatches(itemSearch, profile.PrimaryPhrases);
            int secondary = CountPhraseMatches(itemSearch, profile.SecondaryPhrases);
            return primary * 1200 + secondary * 350 + Math.Min(2, cultureMatches) * 250;
        }

        private static HashSet<string> BuildVisualArchetypeItemIds(VisualProfile profile,
            bool secondaryFallback)
        {
            HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            IEnumerable characters = GetStaticProperty(
                TypeByName("TaleWorlds.CampaignSystem.CharacterObject"), "All") as IEnumerable;
            if (characters == null)
                return ids;
            foreach (object character in characters)
            {
                int score = secondaryFallback ? ScoreSecondaryVisualCharacter(character, profile) :
                    ScoreVisualCharacter(character, profile);
                if (score == Int32.MinValue)
                    continue;
                RegisterAllEquipmentItemIds(character, ids);
            }
            return ids;
        }

        private static void AuditAllVisualMappings()
        {
            if (_visualAuditAttempted)
                return;
            if (_visualAuditRetryDelay > 0)
            {
                _visualAuditRetryDelay--;
                return;
            }

            IEnumerable characters = GetStaticProperty(
                TypeByName("TaleWorlds.CampaignSystem.CharacterObject"), "All") as IEnumerable;
            if (characters == null)
                return;

            bool anyCharacter = false;
            foreach (object ignored in characters)
            {
                anyCharacter = true;
                break;
            }
            if (!anyCharacter)
                return;

            int resolved = 0;
            List<string> failures = new List<string>();
            Dictionary<string, string> auditedOutfitOwners =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int d = 0; d < Definitions.Length; d++)
            {
                SetDefinition definition = Definitions[d];
                CareerItemDefinition relic = CareerUniqueRuntime.GetDefinitionForSet(
                    definition.CareerId);
                object relicItem = relic == null ? null :
                    CareerUniqueRuntime.FindBaseItem(relic);
                if (relic == null || relicItem == null)
                {
                    failures.Add(definition.CareerId + "/relic (missing canonical base)");
                }
                else if (!CareerUniqueRuntime.IsBaseItemCompatible(relic, relicItem))
                {
                    failures.Add(definition.CareerId + "/" + relic.ItemName +
                        " (incompatible relic base " +
                        Convert.ToString(GetProperty(relicItem, "StringId")) + ")");
                }
                else
                {
                    resolved++;
                }

                Dictionary<SetSlot, object> auditedItems =
                    new Dictionary<SetSlot, object>();
                float auditedWeight = 0f;
                for (int p = 0; p < definition.Pieces.Length; p++)
                {
                    SetPieceDefinition piece = definition.Pieces[p];
                    object item = FindArmorBaseItem(definition, piece);
                    if (item == null)
                    {
                        failures.Add(definition.CareerId + "/" + piece.ItemName +
                            " (missing " + piece.Slot + ")");
                        continue;
                    }
                    if (!IsExactSlotItem(item, piece.Slot))
                    {
                        failures.Add(definition.CareerId + "/" + piece.ItemName +
                            " (resolved " + Convert.ToString(GetItemTypeValue(item)) +
                            " for " + piece.Slot + ")");
                        continue;
                    }
                    auditedItems[piece.Slot] = item;
                    auditedWeight += GetArmorWeight(item);
                    resolved++;
                }

                if (auditedItems.Count == definition.Pieces.Length)
                {
                    if (IsCasterSet(definition) && auditedWeight >
                        CasterArmorWeightCap + 0.001f)
                    {
                        failures.Add(definition.CareerId +
                            "/outfit (caster armor weight " + auditedWeight.ToString("0.00",
                            System.Globalization.CultureInfo.InvariantCulture) + " > " +
                            CasterArmorWeightCap + ")");
                    }

                    string outfitSignature = BuildOutfitSignature(definition, auditedItems);
                    string existingOwner;
                    if (!String.IsNullOrEmpty(outfitSignature) &&
                        auditedOutfitOwners.TryGetValue(outfitSignature, out existingOwner) &&
                        !String.Equals(existingOwner, definition.CareerId,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        failures.Add(definition.CareerId + "/outfit duplicates " +
                            existingOwner + " exactly");
                    }
                    else if (!String.IsNullOrEmpty(outfitSignature))
                    {
                        auditedOutfitOwners[outfitSignature] = definition.CareerId;
                    }
                }
            }

            if (failures.Count == 0)
            {
                _visualAuditAttempted = true;
                _lastVisualAuditFailureKey = null;
                ModLog.Info("Visual/type audit passed for all " + resolved +
                    " career-set items across 22 sets (22 relics + 88 armour pieces). " +
                    "Relics passed strict authored-kind validation; armour passed coherent-archetype, " +
                    "exact-slot, duplicate-outfit and caster-weight validation.");
            }
            else
            {
                string failureKey = String.Join("|", failures.ToArray());
                if (!String.Equals(_lastVisualAuditFailureKey, failureKey,
                    StringComparison.Ordinal))
                {
                    _lastVisualAuditFailureKey = failureKey;
                    ModLog.Error("Visual/type audit failed for " + failures.Count +
                        " of 110 items: " + String.Join("; ", failures.ToArray()));
                }
                _visualAuditRetryDelay = 30;
            }
        }

        private static object ResolveVisualSource(SetDefinition definition,
            VisualProfile profile)
        {
            object cached;
            if (VisualSourceByCareer.TryGetValue(definition.CareerId, out cached) &&
                cached != null)
                return cached;

            IEnumerable characters = GetStaticProperty(
                TypeByName("TaleWorlds.CampaignSystem.CharacterObject"), "All") as IEnumerable;
            if (characters == null)
                return null;

            object best = null;
            int bestScore = Int32.MinValue;
            foreach (object character in characters)
            {
                int score = ScoreVisualCharacter(character, profile);
                if (score > bestScore)
                {
                    best = character;
                    bestScore = score;
                }
            }

            if (best != null)
            {
                VisualSourceByCareer[definition.CareerId] = best;
                ModLog.Info("Selected visual source for " + definition.CareerId + ": " +
                    DescribeCharacter(best) + " (score=" + bestScore + ").");
            }
            return best;
        }

        private static int ScoreVisualCharacter(object character, VisualProfile profile)
        {
            if (character == null)
                return Int32.MinValue;

            object culture = GetProperty(character, "Culture");
            string cultureSearch = NormalizeSearch(
                (Convert.ToString(GetProperty(culture, "StringId")) ?? String.Empty) + " " +
                (Convert.ToString(GetProperty(culture, "Name")) ?? String.Empty));
            int cultureMatches = CountPhraseMatches(cultureSearch, profile.CulturePhrases);
            if (cultureMatches == 0)
                return Int32.MinValue;

            string search = NormalizeSearch(
                (Convert.ToString(GetProperty(character, "StringId")) ?? String.Empty) + " " +
                (Convert.ToString(GetProperty(character, "Name")) ?? String.Empty));
            int primary = CountPhraseMatches(search, profile.PrimaryPhrases);
            int secondary = CountPhraseMatches(search, profile.SecondaryPhrases);
            int negative = CountPhraseMatches(search, profile.NegativePhrases);
            int tier = 0;
            try { tier = Convert.ToInt32(GetProperty(character, "Tier")); }
            catch { tier = 0; }

            if (profile.RequirePrimaryMatch && primary == 0)
                return Int32.MinValue;
            if (primary == 0 && secondary == 0)
                return Int32.MinValue;
            if (negative > 0 && primary == 0)
                return Int32.MinValue;

            int coverage = CountArmorSlotCoverage(character);
            int score = 5000 + cultureMatches * 700 + primary * 1800 +
                secondary * 220 - negative * 1500 + tier * 140 + coverage * 320;

            if (primary == 0)
                score -= 1800;
            if (coverage == 0)
                score -= 3000;
            if (ToBoolean(GetProperty(character, "IsTemplate")))
                score -= 200;
            return score;
        }

        private static int CountArmorSlotCoverage(object character)
        {
            HashSet<SetSlot> slots = new HashSet<SetSlot>();
            foreach (object item in EnumerateBattleEquipmentItems(character))
            {
                string id = Convert.ToString(GetProperty(item, "StringId")) ?? String.Empty;
                string name = Convert.ToString(GetProperty(item, "Name")) ?? String.Empty;
                string typeName = Convert.ToString(GetItemTypeValue(item)) ?? String.Empty;
                int typeNumber = EnumNumber(GetItemTypeValue(item));
                bool hasArmor = ToBoolean(GetProperty(item, "HasArmorComponent"));
                string search = NormalizeSearch(id + " " + name + " " + typeName);

                for (int i = 0; i < 5; i++)
                {
                    SetSlot slot = (SetSlot)i;
                    if (ScoreSlot(slot, search, typeName, typeNumber, hasArmor) >= 0)
                        slots.Add(slot);
                }
            }
            return slots.Count;
        }

        private static object FindArmorOnCharacter(object character, SetSlot slot,
            VisualProfile profile, out int bestScore)
        {
            object best = null;
            bestScore = Int32.MinValue;
            foreach (object item in EnumerateBattleEquipmentItems(character))
            {
                int score = ScoreVisualItem(item, slot, profile);
                if (score > bestScore)
                {
                    best = item;
                    bestScore = score;
                }
            }
            return best;
        }

        private static object FindArmorAcrossMatchingCharacters(SetDefinition definition,
            VisualProfile profile, SetSlot slot, object excludedSource,
            out object selectedSource, out int bestScore)
        {
            selectedSource = null;
            bestScore = Int32.MinValue;
            object best = null;

            IEnumerable characters = GetStaticProperty(
                TypeByName("TaleWorlds.CampaignSystem.CharacterObject"), "All") as IEnumerable;
            if (characters == null)
                return null;

            foreach (object character in characters)
            {
                if (Object.ReferenceEquals(character, excludedSource))
                    continue;

                int characterScore = ScoreVisualCharacter(character, profile);
                if (characterScore == Int32.MinValue)
                    continue;

                int itemScore;
                object item = FindArmorOnCharacter(character, slot, profile, out itemScore);
                if (item == null)
                    continue;

                int total = characterScore + itemScore;
                if (total > bestScore)
                {
                    best = item;
                    selectedSource = character;
                    bestScore = total;
                }
            }
            return best;
        }

        private static IEnumerable EnumerateBattleEquipmentItems(object character)
        {
            List<object> result = new List<object>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            IEnumerable equipments = GetProperty(character, "BattleEquipments") as IEnumerable;
            if (equipments == null)
            {
                object first = GetProperty(character, "FirstBattleEquipment");
                if (first != null)
                    AddEquipmentItems(first, result, seen);
                return result;
            }

            foreach (object equipment in equipments)
                AddEquipmentItems(equipment, result, seen);
            return result;
        }

        private static IEnumerable EnumerateEquipmentElements(object equipment)
        {
            List<object> result = new List<object>();
            if (equipment == null)
                return result;

            IEnumerable slots = GetField(equipment, "_itemSlots") as IEnumerable;
            if (slots != null)
            {
                foreach (object element in slots)
                    result.Add(element);
                return result;
            }

            PropertyInfo indexer = equipment.GetType().GetProperty("Item",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, null, new[] { typeof(int) }, null);
            if (indexer != null)
            {
                for (int i = 0; i < 12; i++)
                {
                    try { result.Add(indexer.GetValue(equipment, new object[] { i })); }
                    catch { break; }
                }
            }
            return result;
        }

        private static void AddEquipmentItems(object equipment, List<object> result,
            HashSet<string> seen)
        {
            foreach (object element in EnumerateEquipmentElements(equipment))
            {
                object item = GetProperty(element, "Item");
                if (item == null)
                    continue;

                string id = Convert.ToString(GetProperty(item, "StringId")) ?? String.Empty;
                if (String.IsNullOrWhiteSpace(id) ||
                    id.StartsWith("torcu_", StringComparison.OrdinalIgnoreCase) ||
                    !seen.Add(id))
                    continue;
                result.Add(item);
            }
        }

        private static int ScoreVisualItem(object item, SetSlot slot,
            VisualProfile profile)
        {
            if (item == null || ToBoolean(GetProperty(item, "IsCraftedByPlayer")))
                return Int32.MinValue;

            Type extendedManagerType = TypeByName("TOR_Core.Items.ExtendedItemObjectManager");
            MethodInfo isDuplicate = FindStaticMethod(
                extendedManagerType, "IsRuntimeDuplicatedItem", 1);
            if (isDuplicate != null &&
                ToBoolean(isDuplicate.Invoke(null, new object[] { item })))
                return Int32.MinValue;

            string id = Convert.ToString(GetProperty(item, "StringId")) ?? String.Empty;
            string name = Convert.ToString(GetProperty(item, "Name")) ?? String.Empty;
            string typeName = Convert.ToString(GetItemTypeValue(item)) ?? String.Empty;
            int typeNumber = EnumNumber(GetItemTypeValue(item));
            bool hasArmor = ToBoolean(GetProperty(item, "HasArmorComponent"));
            string search = NormalizeSearch(id + " " + name + " " + typeName);

            if (ContainsAny(search, " template", " quest ", " tournament ",
                " practice ", " training ", " dummy ", " blueprint ",
                " debug ", " test "))
                return Int32.MinValue;

            int slotScore = ScoreSlot(slot, search, typeName, typeNumber, hasArmor);
            if (slotScore < 0)
                return Int32.MinValue;

            int tier = Math.Max(0, EnumNumber(GetProperty(item, "Tier")));
            int value = Math.Max(0, EnumNumber(GetProperty(item, "Value")));
            int quality = Math.Min(7, tier) * 95 +
                Math.Min(180, value / 1200);

            return slotScore +
                CountPhraseMatches(search, profile.PrimaryPhrases) * 420 +
                CountPhraseMatches(search, profile.SecondaryPhrases) * 110 -
                CountPhraseMatches(search, profile.NegativePhrases) * 320 +
                quality;
        }

        private static object FindStrictArmorFallback(SetDefinition definition,
            VisualProfile profile, SetPieceDefinition piece, out int bestScore)
        {
            bestScore = Int32.MinValue;
            Type managerType = TypeByName("TaleWorlds.ObjectSystem.MBObjectManager");
            Type itemType = TypeByName("TaleWorlds.Core.ItemObject");
            object manager = GetStaticProperty(managerType, "Instance");
            if (managerType == null || itemType == null || manager == null)
                return null;

            MethodInfo getList = null;
            MethodInfo[] methods = managerType.GetMethods(
                BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < methods.Length; i++)
            {
                if (methods[i].Name == "GetObjectTypeList" &&
                    methods[i].IsGenericMethodDefinition &&
                    methods[i].GetParameters().Length == 0)
                {
                    getList = methods[i];
                    break;
                }
            }
            if (getList == null)
                return null;

            IEnumerable items = getList.MakeGenericMethod(itemType)
                .Invoke(manager, null) as IEnumerable;
            if (items == null)
                return null;

            object best = null;
            string[] fantasyTokens = Tokenize(piece.ItemName);
            foreach (object item in items)
            {
                int itemScore = ScoreVisualItem(item, piece.Slot, profile);
                if (itemScore == Int32.MinValue)
                    continue;

                string search = NormalizeSearch(
                    (Convert.ToString(GetProperty(item, "StringId")) ?? String.Empty) + " " +
                    (Convert.ToString(GetProperty(item, "Name")) ?? String.Empty));

                int culture = CountPhraseMatches(search, profile.CulturePhrases);
                int archetype = CountPhraseMatches(search, profile.PrimaryPhrases) +
                    CountPhraseMatches(search, profile.SecondaryPhrases);
                if (culture == 0 || archetype == 0)
                    continue;

                int total = itemScore + culture * 700 + archetype * 240 +
                    ScoreTokens(search, fantasyTokens, 8);
                if (total > bestScore)
                {
                    best = item;
                    bestScore = total;
                }
            }
            return best;
        }

        private static object FindSameCultureArmorFallback(VisualProfile profile,
            SetPieceDefinition piece, out int bestScore)
        {
            bestScore = Int32.MinValue;
            Type managerType = TypeByName("TaleWorlds.ObjectSystem.MBObjectManager");
            Type itemType = TypeByName("TaleWorlds.Core.ItemObject");
            object manager = GetStaticProperty(managerType, "Instance");
            if (managerType == null || itemType == null || manager == null)
                return null;

            MethodInfo getList = null;
            MethodInfo[] methods = managerType.GetMethods(
                BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < methods.Length; i++)
            {
                if (methods[i].Name == "GetObjectTypeList" &&
                    methods[i].IsGenericMethodDefinition &&
                    methods[i].GetParameters().Length == 0)
                {
                    getList = methods[i];
                    break;
                }
            }
            if (getList == null)
                return null;

            IEnumerable items = getList.MakeGenericMethod(itemType)
                .Invoke(manager, null) as IEnumerable;
            if (items == null)
                return null;

            object best = null;
            foreach (object item in items)
            {
                if (!IsExactSlotItem(item, piece.Slot) ||
                    ToBoolean(GetProperty(item, "IsCraftedByPlayer")))
                    continue;

                Type extendedManagerType =
                    TypeByName("TOR_Core.Items.ExtendedItemObjectManager");
                MethodInfo isDuplicate = FindStaticMethod(
                    extendedManagerType, "IsRuntimeDuplicatedItem", 1);
                if (isDuplicate != null &&
                    ToBoolean(isDuplicate.Invoke(null, new object[] { item })))
                    continue;

                object culture = GetProperty(item, "Culture");
                string cultureSearch = NormalizeSearch(
                    (Convert.ToString(GetProperty(culture, "StringId")) ?? String.Empty) +
                    " " +
                    (Convert.ToString(GetProperty(culture, "Name")) ?? String.Empty));
                int cultureMatches = CountPhraseMatches(
                    cultureSearch, profile.CulturePhrases);

                string id = Convert.ToString(GetProperty(item, "StringId")) ??
                    String.Empty;
                string name = Convert.ToString(GetProperty(item, "Name")) ??
                    String.Empty;
                string itemSearch = NormalizeSearch(id + " " + name);
                if (cultureMatches == 0)
                    cultureMatches = CountPhraseMatches(
                        itemSearch, profile.CulturePhrases);
                if (cultureMatches == 0)
                    continue;

                int archetype =
                    CountPhraseMatches(itemSearch, profile.PrimaryPhrases) * 4 +
                    CountPhraseMatches(itemSearch, profile.SecondaryPhrases);
                int negative = CountPhraseMatches(
                    itemSearch, profile.NegativePhrases);
                int value = Math.Max(0, EnumNumber(GetProperty(item, "Value")));
                int tier = Math.Max(0, EnumNumber(GetProperty(item, "Tier")));

                int total = 1000 + cultureMatches * 1000 +
                    archetype * 260 - negative * 360 +
                    Math.Min(7, tier) * 140 +
                    Math.Min(Math.Max(value, 0), 180000) / 900;
                if (total > bestScore)
                {
                    best = item;
                    bestScore = total;
                }
            }
            return best;
        }

        private static string DescribeCharacter(object character)
        {
            if (character == null)
                return "<none>";
            return "'" + (Convert.ToString(GetProperty(character, "Name")) ?? "<unnamed>") +
                "' [" + (Convert.ToString(GetProperty(character, "StringId")) ?? "<no-id>") +
                ", tier " + (Convert.ToString(GetProperty(character, "Tier")) ?? "?") + "]";
        }

        private static string NormalizeSearch(string text)
        {
            if (String.IsNullOrEmpty(text))
                return String.Empty;
            StringBuilder result = new StringBuilder(text.Length * 2);
            bool previousSpace = true;
            for (int i = 0; i < text.Length; i++)
            {
                char c = Char.ToLowerInvariant(text[i]);
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
            return " " + result.ToString().Trim() + " ";
        }

        private static int CountPhraseMatches(string normalizedSearch,
            string[] phrases)
        {
            if (String.IsNullOrEmpty(normalizedSearch) || phrases == null)
                return 0;

            // TOR culture display names can use irregular plurals while our semantic
            // profiles use the singular race name (notably "Wood Elves" vs
            // "wood elf"). Canonicalize only these irregular noun forms; broad
            // stemming would create false career matches such as unrelated ids that
            // merely share a suffix.
            string canonicalSearch = CanonicalizeIrregularSearchNouns(normalizedSearch);
            int count = 0;
            for (int i = 0; i < phrases.Length; i++)
            {
                string phrase = CanonicalizeIrregularSearchNouns(
                    NormalizeSearch(phrases[i])).Trim();
                if (phrase.Length > 0 &&
                    canonicalSearch.IndexOf(" " + phrase + " ",
                        StringComparison.OrdinalIgnoreCase) >= 0)
                    count++;
                else if (phrase.Length > 0 &&
                    canonicalSearch.IndexOf(phrase,
                        StringComparison.OrdinalIgnoreCase) >= 0)
                    count++;
            }
            return count;
        }

        private static string CanonicalizeIrregularSearchNouns(string normalizedSearch)
        {
            if (String.IsNullOrEmpty(normalizedSearch))
                return String.Empty;
            return normalizedSearch
                .Replace(" elves ", " elf ")
                .Replace(" dwarves ", " dwarf ");
        }

        private static string GetExpectedItemTypeName(SetSlot slot)
        {
            switch (slot)
            {
                case SetSlot.Head: return "HeadArmor";
                case SetSlot.Body: return "BodyArmor";
                case SetSlot.Cape: return "Cape";
                case SetSlot.Hand: return "HandArmor";
                case SetSlot.Leg: return "LegArmor";
                default: return String.Empty;
            }
        }

        private static bool IsExactSlotItem(object item, SetSlot slot)
        {
            if (item == null)
                return false;

            string actual = Convert.ToString(GetItemTypeValue(item)) ?? String.Empty;
            if (!String.Equals(actual, GetExpectedItemTypeName(slot),
                StringComparison.OrdinalIgnoreCase))
                return false;

            Type equipmentType = TypeByName("TaleWorlds.Core.Equipment");
            Type equipmentIndexType = TypeByName("TaleWorlds.Core.EquipmentIndex");
            MethodInfo fits = FindStaticMethod(equipmentType, "IsItemFitsToSlot", 2);
            if (equipmentIndexType == null || fits == null)
                return true;

            object equipmentIndex;
            try
            {
                equipmentIndex = Enum.Parse(equipmentIndexType,
                    GetExpectedEquipmentIndexName(slot), true);
            }
            catch
            {
                return false;
            }

            return ToBoolean(fits.Invoke(null, new object[] { equipmentIndex, item }));
        }

        private static string GetExpectedEquipmentIndexName(SetSlot slot)
        {
            switch (slot)
            {
                case SetSlot.Head: return "Head";
                case SetSlot.Body: return "Body";
                case SetSlot.Cape: return "Cape";
                case SetSlot.Hand: return "Gloves";
                case SetSlot.Leg: return "Leg";
                default: return "None";
            }
        }

        private static int ScoreSlot(SetSlot slot, string search, string typeName,
            int typeNumber, bool hasArmor)
        {
            string expected = GetExpectedItemTypeName(slot);
            if (!String.Equals(typeName ?? String.Empty, expected,
                StringComparison.OrdinalIgnoreCase))
                return -1;
            return 1000;
        }

        private static List<string> GetRealPieceTraitIds(SetPieceDefinition piece)
        {
            List<string> result = new List<string>();
            for (int i = 0; i < piece.Effects.Length; i++)
                result.Add(piece.Effects[i].Id);
            return result;
        }

        private static List<string> GetAdminPieceTraitIds(SetDefinition definition,
            int pieceIndex, SetPieceDefinition piece)
        {
            List<string> result = new List<string>();
            result.Add(GetAdminSignature(definition, pieceIndex));
            for (int i = 1; i < piece.Effects.Length; i++)
                result.Add(piece.Effects[i].Id);
            return result;
        }

        private static List<string> GetAdminRelicTraitIds(SetDefinition definition,
            CareerItemDefinition relic)
        {
            List<string> result = new List<string>();
            result.Add(GetAdminSignature(definition, 0));
            for (int i = 1; i < relic.Traits.Length; i++)
                result.Add(relic.Traits[i].Id);
            return result;
        }

        private static string GetRealSignature(SetDefinition definition, int pieceIndex)
        {
            if (pieceIndex == 0)
            {
                CareerItemDefinition relic = CareerUniqueRuntime.GetDefinitionForSet(definition.CareerId);
                return relic == null ? String.Empty : relic.SignatureTraitId;
            }
            return definition.Pieces[pieceIndex - 1].Effects[0].Id;
        }

        private static string GetAdminSignature(SetDefinition definition, int pieceIndex)
        {
            return AdminPrefix + definition.CareerId.ToLowerInvariant() +
                "_p" + pieceIndex + "_sig";
        }

        private static bool[] CaptureRealAcquisitionState(object artisan, SetDefinition definition)
        {
            bool[] result = new bool[5];
            for (int i = 0; i < result.Length; i++)
                result[i] = CareerUniqueRuntime.HasClaimed(artisan, GetRealSignature(definition, i));
            return result;
        }

        private static string GetSetDisplayTraitId(SetDefinition definition,
            SetTierDefinition tier)
        {
            return DisplayPrefix + definition.CareerId.ToLowerInvariant() +
                "_" + tier.RequiredPieces;
        }

        private static string BuildSetDisplayTraitDescription(SetDefinition definition,
            SetTierDefinition tier, int equippedCount)
        {
            bool active = equippedCount >= tier.RequiredPieces;
            return "[" + (active ? "ACTIVE" : "LOCKED") + "] " +
                definition.SetName + " — " + tier.RequiredPieces + "/5 " +
                tier.Name + ": " + FormatEffectSummary(tier.Effects);
        }

        private static TraitDefinition CreateSetDisplayTrait(SetDefinition definition,
            SetTierDefinition tier)
        {
            return new TraitDefinition
            {
                Id = GetSetDisplayTraitId(definition, tier),
                Name = tier.RequiredPieces + "/5 LOCKED — " + tier.Name,
                Description = BuildSetDisplayTraitDescription(definition, tier, 0),
                Kind = TraitKind.Stat,
                EffectType = "HealthMax",
                Value = 0f,
                IconName = "winds_icon_45"
            };
        }

        private static TraitDefinition CloneTrait(TraitDefinition source, string newId)
        {
            return new TraitDefinition
            {
                Id = newId,
                Name = source.Name,
                Description = source.Description,
                Kind = source.Kind,
                EffectType = source.EffectType,
                SkillId = source.SkillId,
                Value = source.Value,
                IconName = source.IconName
            };
        }

        private static Dictionary<string, SetDefinition> BuildDefinitionMap()
        {
            Dictionary<string, SetDefinition> result =
                new Dictionary<string, SetDefinition>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < Definitions.Length; i++)
                result.Add(Definitions[i].CareerId, Definitions[i]);
            return result;
        }

        private static Dictionary<string, PieceSignature> BuildSignatureMap()
        {
            Dictionary<string, PieceSignature> result =
                new Dictionary<string, PieceSignature>(StringComparer.Ordinal);

            for (int i = 0; i < Definitions.Length; i++)
            {
                SetDefinition definition = Definitions[i];
                CareerItemDefinition relic = CareerUniqueRuntime.GetDefinitionForSet(definition.CareerId);
                if (relic != null)
                {
                    AddSignature(result, relic.SignatureTraitId, definition, 0);
                    AddSignature(result, GetAdminSignature(definition, 0), definition, 0);
                    AddSignature(result, GetHeroSignature(definition, 0), definition, 0);
                }

                for (int p = 0; p < definition.Pieces.Length; p++)
                {
                    AddSignature(result, definition.Pieces[p].Effects[0].Id,
                        definition, p + 1);
                    AddSignature(result, GetAdminSignature(definition, p + 1),
                        definition, p + 1);
                    AddSignature(result, GetHeroSignature(definition, p + 1),
                        definition, p + 1);
                }
            }
            return result;
        }

        private static void AddSignature(Dictionary<string, PieceSignature> map,
            string id, SetDefinition definition, int pieceIndex)
        {
            if (String.IsNullOrEmpty(id))
                return;
            map.Add(id, new PieceSignature
            {
                Definition = definition,
                PieceIndex = pieceIndex
            });
        }

        private static Dictionary<string, VisualProfile> BuildVisualProfiles()
        {
            Dictionary<string, VisualProfile> result =
                new Dictionary<string, VisualProfile>(StringComparer.OrdinalIgnoreCase);

            AddVisualProfile(result, "GrailDamsel",
                new[] { "tor bretonnia", "tor breton", "bretonnia", "breton" },
                new[] { "grail damsel", "damsel", "prophetess", "fay enchantress", "enchantress" },
                new[] { "mage", "wizard", "grail", "lake", "fay", "sorceress" },
                new[] { "crossbow", "archer", "peasant", "men at arms", "foot knight" }, true);
            AddVisualProfile(result, "GrailKnight",
                new[] { "tor bretonnia", "tor breton", "bretonnia", "breton" },
                new[] { "grail knight", "grail guardian", "living saint", "royal knight" },
                new[] { "knight", "paladin", "grail", "companion", "cavalier" },
                new[] { "archer", "crossbow", "peasant", "damsel", "prophetess" }, true);
            AddVisualProfile(result, "MinorVampire",
                new[] { "tor sylvania", "sylvania", "vampire counts", "vampire" },
                new[] { "von carstein", "vampire noble", "vampire count", "midnight aristocrat" },
                new[] { "vampire", "blood", "court", "noble", "carstein" },
                new[] { "zombie", "skeleton", "necromancer", "necrarch", "blood knight" }, false);
            AddVisualProfile(result, "WarriorPriest",
                new[] { "tor empire", "empire", "reikland", "sigmar" },
                new[] { "warrior priest", "arch lector", "lector", "priest of sigmar" },
                new[] { "sigmar", "priest", "templar", "hammer", "holy" },
                new[] { "crossbow", "handgun", "archer", "wizard", "magister" }, true);
            AddVisualProfile(result, "BloodKnight",
                new[] { "tor sylvania", "sylvania", "vampire counts", "vampire" },
                new[] { "blood knight", "blood dragon", "walach harkon", "walach" },
                new[] { "vampire", "knight", "dragon", "crimson", "cavalry" },
                new[] { "zombie", "skeleton", "necromancer", "necrarch", "carstein" }, true);
            AddVisualProfile(result, "Mercenary",
                new[] { "tor empire", "empire", "tor southern realms", "southern realms", "tilea", "estalia", "border princes", "border prince" },
                new[] { "black company", "border prince", "mercenary captain", "free company captain" },
                new[] { "mercenary", "free company", "captain", "veteran", "paymaster" },
                new[] { "wizard", "priest", "knightly order", "witch hunter" }, false);
            AddVisualProfile(result, "WitchHunter",
                new[] { "tor empire", "empire", "reikland", "sigmar" },
                new[] { "witch hunter", "templar witch hunter", "order of the silver hammer" },
                new[] { "templar", "hunter", "purifier", "zealot", "silver hammer" },
                new[] { "wizard", "magister", "crossbowman", "handgunner", "knight" }, true);
            AddVisualProfile(result, "Necromancer",
                new[] { "tor sylvania", "sylvania", "vampire counts", "vampire" },
                new[] { "master necromancer", "necromancer", "mortuary cultist" },
                new[] { "death mage", "dark mage", "sorcerer", "grave", "ossuary" },
                new[] { "blood knight", "black knight", "skeleton", "zombie", "crossbow" }, true);
            AddVisualProfile(result, "BlackGrailKnight",
                new[] { "tor mousillon", "mousillon", "tor bretonnia", "bretonnia", "tor sylvania", "sylvania", "vampire" },
                new[] { "black grail knight", "black grail", "knight of mousillon" },
                new[] { "mousillon", "black knight", "accursed knight", "false grail" },
                new[] { "damsel", "peasant", "archer", "necromancer", "grail knight" }, true);
            AddVisualProfile(result, "Necrarch",
                new[] { "tor sylvania", "sylvania", "vampire counts", "vampire" },
                new[] { "necrarch", "necrarch vampire", "ossuary savant" },
                new[] { "necromancer", "dark mage", "sorcerer", "vampire mage" },
                new[] { "blood knight", "black knight", "carstein", "zombie", "skeleton" }, true);
            AddVisualProfile(result, "WarriorPriestUlric",
                new[] { "tor empire", "empire", "middenland", "middenheim", "ulric" },
                new[] { "warrior priest of ulric", "priest of ulric", "teutogen guard", "white wolf" },
                new[] { "ulric", "wolf", "middenheim", "middenland", "teutogen" },
                new[] { "sigmar", "wizard", "crossbow", "handgun", "archer" }, true);
            AddVisualProfile(result, "ImperialMagister",
                new[] { "tor empire", "empire", "reikland", "colleges of magic" },
                new[] { "imperial magister", "battle wizard lord", "wizard lord", "supreme patriarch" },
                new[] { "magister", "wizard", "mage", "college", "sorcerer", "volans" },
                new[] { "crossbow", "handgun", "archer", "knight", "warrior priest" }, true);
            AddVisualProfile(result, "Waywatcher",
                new[] { "tor asrai", "asrai", "wood elf", "athel loren" },
                new[] { "waywatcher", "waystalker", "deepwood scout" },
                new[] { "ranger", "scout", "archer", "shadow", "glade" },
                new[] { "spellsinger", "spellweaver", "wardancer", "eternal guard", "cavalry" }, true);
            AddVisualProfile(result, "Spellsinger",
                new[] { "tor asrai", "asrai", "wood elf", "athel loren" },
                new[] { "spellsinger", "spellweaver", "branchwraith", "mage of athel loren" },
                new[] { "mage", "wizard", "singer", "weaver", "druid", "sorceress" },
                new[] { "waywatcher", "archer", "eternal guard", "wild rider", "wardancer" }, true);
            AddVisualProfile(result, "Warden",
                new[] { "tor asrai", "asrai", "wood elf", "athel loren" },
                new[] { "warden of", "wild hunt warden", "warden", "spear of kurnous" },
                new[] { "wild hunt", "kurnous", "spear", "eternal guard", "wardancer" },
                new[] { "spellsinger", "waywatcher", "archer", "mage", "crossbow" }, true);
            AddVisualProfile(result, "GreyLord",
                new[] { "tor eonir", "eonir" },
                new[] { "grey lord wizard", "grey lord", "greylord", "warden of storms",
                    "warden of the storms", "storm warden" },
                new[] { "wizard", "mage", "spellweaver", "spellsinger", "storm", "warden" },
                new[] { "crossbow", "crossbowman", "archer", "ranger", "spearman",
                    "infantry", "swordsman" }, true);
            AddVisualProfile(result, "KnightOldWorld",
                new[] { "tor empire", "empire", "reikland", "old world" },
                new[] { "reiksguard inner circle", "reiksguard", "knight of the old world" },
                new[] { "inner circle", "knight", "cavalier", "templar", "order" },
                new[] { "crossbow", "handgun", "archer", "wizard", "priest" }, true);
            AddVisualProfile(result, "Ironbreaker",
                new[] { "tor dwarf", "tor dawi", "dwarf", "dawi", "karaz ankor" },
                new[] { "ironbreaker", "ironbeard", "gromril guard" },
                new[] { "gromril", "shield", "thane", "hammerer", "gate guard" },
                new[] { "slayer", "ranger", "thunderer", "quarreller", "runesmith" }, true);
            AddVisualProfile(result, "Slayer",
                new[] { "tor dwarf", "tor dawi", "dwarf", "dawi", "karaz ankor" },
                new[] { "daemon slayer", "dragon slayer", "giant slayer", "slayer", "doomseeker" },
                new[] { "doom", "oath", "troll slayer", "axe" },
                new[] { "ironbreaker", "ranger", "thunderer", "quarreller", "runesmith" }, true);
            AddVisualProfile(result, "Runelord",
                new[] { "tor dwarf", "tor dawi", "dwarf", "dawi", "karaz ankor" },
                new[] { "runelord", "master runesmith", "runesmith", "anvil of doom" },
                new[] { "rune", "anvil", "smith", "thungni" },
                new[] { "slayer", "ranger", "thunderer", "quarreller", "ironbreaker" }, true);
            AddVisualProfile(result, "OrcBoss",
                new[] { "tor greenskin", "greenskin", "orc", "orcs and goblins" },
                new[] { "black orc warboss", "orc warboss", "warboss", "black orc big boss" },
                new[] { "black orc", "big boss", "boss", "waaagh", "armoured orc" },
                new[] { "shaman", "goblin", "arrer", "archer", "savage orc" }, true);
            AddVisualProfile(result, "OrcShaman",
                new[] { "tor greenskin", "greenskin", "orc", "orcs and goblins" },
                new[] { "great shaman", "orc shaman", "savage orc shaman", "night goblin shaman" },
                new[] { "shaman", "waaagh", "mage", "moon", "baduum" },
                new[] { "warboss", "black orc", "arrer", "archer", "big boss" }, true);

            return result;
        }

        private static void AddVisualProfile(Dictionary<string, VisualProfile> map,
            string careerId, string[] culturePhrases, string[] primaryPhrases,
            string[] secondaryPhrases, string[] negativePhrases,
            bool requirePrimaryMatch)
        {
            map.Add(careerId, new VisualProfile
            {
                CulturePhrases = culturePhrases,
                PrimaryPhrases = primaryPhrases,
                SecondaryPhrases = secondaryPhrases,
                NegativePhrases = negativePhrases,
                RequirePrimaryMatch = requirePrimaryMatch
            });
        }

        private static SetDefinition[] BuildDefinitions()
        {
            return new[]
            {
SD("GrailDamsel", "Regalia of the Silver Chalice", new[] { "tor_br", "breton", "couronne", "grail" },
                    new[]
                    {
                        P("Circlet of the Lake", SetSlot.Head,
                        K("Lake-Born Insight", "+10 Spellcraft.", "Spellcraft", 10f),
                        S("Ward of the Lady", "10% extra magic resistance; once per battle, a fatal wound revives you with 50% HP.", "bret_blessing_lady_ward", 0f)),
                        P("Vestments of the Grail Spring", SetSlot.Body,
                        R("Water of Purity", "8% resistance to holy damage.", "Holy", 0.08f),
                        S("Mists of the Sacred Lake", "3% extra physical resistance and 15% extra prayer and spell radius.", "bret_blessing_mists_sacred_lake", 0f)),
                        P("Mantle of the Fay Enchantress", SetSlot.Cape,
                        S("Mist-Wreathed Aura", "+10% spell radius.", "SpellRadius", 10f),
                        S("Bloom of Ghyran", "6 max HP, -10% fire resistance, and a 10% chance to recover 5 HP upon receiving damage.", "emp_enchant_ghyran_bloom", 0f)),
                        P("Slippers of the Sacred Shore", SetSlot.Leg,
                        S("Walk Upon the Water", "+5% movement speed.", "MovementSpeed", 5f),
                        S("Divination of Azyr", "5% extra magic resistance and 7% extra party travel speed.", "emp_enchant_azyr_divination", 0f))
                    },
                    new[]
                    {
                        T(2, "Grace of the Sacred Spring",
                        S("Sacred Spring Renewal", "+10% healing rate.", "HealthRegen", 0.1f),
                        R("The Lady's Benediction", "5% resistance to holy damage.", "Holy", 0.05f)),
                        T(3, "The Lady's Reflection",
                        K("Oracle of the Lake", "+15 Spellcraft.", "Spellcraft", 15f),
                        S("Silver-Chalice Radiance", "+15% spell radius.", "SpellRadius", 15f)),
                        T(4, "Fay Confluence",
                        S("Deepened Grail Font", "+25 maximum Winds of Magic.", "WindsOfMagicMax", 25f),
                        A("Moonlit Sorcery", "+8% magical damage.", "Magical", 0.08f)),
                        T(5, "Manifest Blessing of the Lady",
                        S("Endless Grail Current", "+0.35 Winds of Magic recharge.", "WindsOfMagicRegen", 0.35f),
                        A("Avatar of the Lady", "+18% magical damage.", "Magical", 0.18f),
                        X("Grail-Light Judgement", "20% additional holy damage.", "Holy", 0.2f),
                        S("Grail-Blessed Vitality", "+40 maximum health.", "HealthMax", 40f))
                    }),
                SD("GrailKnight", "Panoply of the Companions", new[] { "tor_br", "breton", "couronne", "grail" },
                    new[]
                    {
                        P("Helm of the Questing Vow", SetSlot.Head,
                        K("Vigil of the Quest", "+10 Polearm.", "Polearm", 10f),
                        S("Ward of the Lady", "10% extra magic resistance; once per battle, a fatal wound revives you with 50% HP.", "bret_blessing_lady_ward", 0f)),
                        P("Plate of the Sacred Oath", SetSlot.Body,
                        S("Lionhearted", "+15 maximum health.", "HealthMax", 15f),
                        S("Wisdom and Virtue", "3 extra HP and 5% extra magic resistance.", "bret_blessing_wisdom_virtue", 0f)),
                        P("Gauntlets of the Dragon's Bane", SetSlot.Hand,
                        S("Dragon-Bane Grip", "+7% armor penetration.", "ArmorPenetration", 7f),
                        S("Touch of the Eerie", "2% extra physical resistance and 25 bonus Riding skill.", "bret_blessing_eerie_touch", 0f)),
                        P("Sabatons of the Unbroken Charge", SetSlot.Leg,
                        S("Unbroken Charge", "+5% movement speed.", "MovementSpeed", 5f),
                        S("Guidance of the Fey", "10% extra party travel speed and 5% magic resistance.", "bret_blessing_fey_guidance", 0f))
                    },
                    new[]
                    {
                        T(2, "Virtue of the Impetuous Knight",
                        K("Companion's Horsemanship", "+15 Riding.", "Riding", 15f),
                        S("Perfected Lance-Point", "+8% armor penetration.", "ArmorPenetration", 8f)),
                        T(3, "Virtue of Duty",
                        K("Command of the Companions", "+15 Leadership.", "Leadership", 15f),
                        R("Steadfast Under Arms", "8% resistance to physical damage.", "Physical", 0.08f)),
                        T(4, "Virtue of Heroism",
                        A("Heroic Might", "+10% physical damage.", "Physical", 0.1f),
                        S("Monster-Slayer's Reach", "+25% shield damage.", "ShieldDamage", 25f)),
                        T(5, "Living Legend of Bretonnia",
                        X("Grail-Fire Charge", "25% additional holy damage.", "Holy", 0.25f),
                        A("Paragon of Chivalry", "+20% physical damage.", "Physical", 0.2f),
                        S("Heart of the Grail", "+50 maximum health.", "HealthMax", 50f),
                        S("The Perfect Lance", "+20% armor penetration.", "ArmorPenetration", 20f))
                    }),
                SD("MinorVampire", "Raiment of the Midnight Court", new[] { "tor_vc", "vamp", "sylvan", "undead", "mousillon" },
                    new[]
                    {
                        P("Masque of the Pale Court", SetSlot.Head,
                        K("Courtly Predator", "+8 Charm.", "Charm", 8f),
                        S("Nightshroud", "5% extra magic resistance; 15% chance to reduce attack speed and damage of nearby enemies upon receiving damage.", "vc_enchant_nightshroud", 0f)),
                        P("Velvet of the Blood-Kin", SetSlot.Body,
                        S("Blood-Kin Vitality", "+15 maximum health.", "HealthMax", 15f),
                        S("Ethereal Whispers", "5% extra physical resistance; receiving ranged damage grants 50% ranged-damage immunity for a short time.", "vc_enchant_ethereal_whispers", 0f)),
                        P("Cloak of No Moon", SetSlot.Cape,
                        S("No-Moon Passage", "+5% movement speed.", "MovementSpeed", 5f),
                        S("Nightshroud", "5% extra magic resistance; 15% chance to reduce attack speed and damage of nearby enemies upon receiving damage.", "vc_enchant_nightshroud", 0f)),
                        P("Talons of the Von Carsteins", SetSlot.Hand,
                        X("Aristocrat's Talons", "+7% physical damage.", "Physical", 0.07f),
                        S("Ethereal Whispers", "5% extra physical resistance; receiving ranged damage grants 50% ranged-damage immunity for a short time.", "vc_enchant_ethereal_whispers", 0f))
                    },
                    new[]
                    {
                        T(2, "Noble Blood",
                        K("Sanguine Duellist", "+15 One Handed.", "OneHanded", 15f),
                        S("Unquiet Pulse", "+10% healing rate.", "HealthRegen", 0.1f)),
                        T(3, "Predator After Dusk",
                        S("Predator After Dusk", "+7% movement speed.", "MovementSpeed", 7f),
                        A("Midnight Ambush", "+8% physical damage.", "Physical", 0.08f)),
                        T(4, "The Red Thirst",
                        S("The Red Thirst", "+20% healing rate.", "HealthRegen", 0.2f),
                        X("Find the Artery", "+10% physical damage.", "Physical", 0.10f)),
                        T(5, "Scion of the Midnight Court",
                        A("Vampiric Ascendancy", "+22% physical damage.", "Physical", 0.22f),
                        X("Dhar-Tainted Blood", "18% additional magical damage.", "Magical", 0.18f),
                        S("Undying Aristocrat", "+45 maximum health.", "HealthMax", 45f),
                        S("Feeding Rapture", "+35% healing rate.", "HealthRegen", 0.35f))
                    }),
                SD("WarriorPriest", "Vestments of the Twin-Tailed Comet", new[] { "tor_emp", "empire", "sigmar", "reik", "nuln", "middenheim" },
                    new[]
                    {
                        P("Mitre of the War Altar", SetSlot.Head,
                        K("Battlefield Sermon", "+10 Leadership.", "Leadership", 10f),
                        S("Soulfire of Sigmar", "6% extra magic resistance, 3 extra HP, and a 10% chance to emit damaging holy energy when attacked in melee by Undead or Daemons.", "emp_blessing_sigmar_soulfire", 0f)),
                        P("Cuirass of Sigmar's Anvil", SetSlot.Body,
                        S("The Anvil Endures", "+15 maximum health.", "HealthMax", 15f),
                        S("Light of Sigmar", "6% extra magic resistance, 3 extra HP, and a 10% chance to gain minor regeneration when attacked in melee by Undead or Daemons.", "emp_blessing_sigmar_light", 0f)),
                        P("Gauntlets of Righteous Wrath", SetSlot.Hand,
                        S("Righteous Wrath", "+6% swing speed.", "SwingSpeed", 6f),
                        S("Beacon of Sigmar", "10% extra magic resistance and 15% extra prayer radius.", "emp_blessing_sigmar_beacon", 0f)),
                        P("Greaves of the Temple Road", SetSlot.Leg,
                        K("Temple-Road Pilgrim", "+10 Athletics.", "Athletics", 10f),
                        S("Sanctuary of Hysh", "3% extra physical resistance; 5% chance to snare nearby enemies with a net of Hysh upon receiving damage.", "emp_enchant_hysh_sanctuary", 0f))
                    },
                    new[]
                    {
                        T(2, "Litany of Protection",
                        R("Litany of Protection", "6% resistance to all damage.", "All", 0.06f),
                        S("Unbroken Prayer", "+0.12 career-resource generation.", "CustomResourceGain", 0.12f)),
                        T(3, "Hammer and Book",
                        K("Doctrine of War", "+15 One Handed.", "OneHanded", 15f),
                        S("Condemn the Heretic", "+22% shield damage.", "ShieldDamage", 22f)),
                        T(4, "Soulfire Congregation",
                        X("Soulfire Congregation", "15% additional holy damage.", "Holy", 0.15f),
                        S("Strength of the Congregation", "+25 maximum health.", "HealthMax", 25f)),
                        T(5, "Avatar of the Twin-Tailed Comet",
                        X("Twin-Tailed Comet", "30% additional holy damage.", "Holy", 0.3f),
                        A("Sigmar's Wrath", "+18% physical damage.", "Physical", 0.18f),
                        S("Unending Fervour", "+0.35 career-resource generation.", "CustomResourceGain", 0.35f),
                        R("Armour of Contempt", "15% resistance to all damage.", "All", 0.15f))
                    }),
                SD("BloodKnight", "Crimson Panoply of Walach", new[] { "tor_vc", "vamp", "sylvan", "undead", "mousillon" },
                    new[]
                    {
                        P("Dragon-Visored Helm", SetSlot.Head,
                        K("Walach's Duel-Lore", "+10 One Handed.", "OneHanded", 10f),
                        S("Nightshroud", "5% extra magic resistance; 15% chance to reduce attack speed and damage of nearby enemies upon receiving damage.", "vc_enchant_nightshroud", 0f)),
                        P("Cuirass of the Red Keep", SetSlot.Body,
                        S("Red Keep Endurance", "+18 maximum health.", "HealthMax", 18f),
                        S("Ethereal Whispers", "5% extra physical resistance; receiving ranged damage grants 50% ranged-damage immunity for a short time.", "vc_enchant_ethereal_whispers", 0f)),
                        P("Gauntlets of the Endless Duel", SetSlot.Hand,
                        S("Endless-Duel Tempo", "+7% swing speed.", "SwingSpeed", 7f),
                        S("Nightshroud", "5% extra magic resistance; 15% chance to reduce attack speed and damage of nearby enemies upon receiving damage.", "vc_enchant_nightshroud", 0f)),
                        P("Spurs of the Crimson Errantry", SetSlot.Leg,
                        K("Crimson Errantry", "+10 Riding.", "Riding", 10f),
                        S("Ethereal Whispers", "5% extra physical resistance; receiving ranged damage grants 50% ranged-damage immunity for a short time.", "vc_enchant_ethereal_whispers", 0f))
                    },
                    new[]
                    {
                        T(2, "Blood Dragon Discipline",
                        K("Blood Dragon Mastery", "+15 One Handed.", "OneHanded", 15f),
                        S("Perfect Killing Tempo", "+6% swing speed.", "SwingSpeed", 6f)),
                        T(3, "Challenge Without End",
                        X("Challenge Without End", "+10% physical damage.", "Physical", 0.10f),
                        R("Perverse Martial Honour", "8% resistance to physical damage.", "Physical", 0.08f)),
                        T(4, "Red Keep Ascendant",
                        A("Red Keep Ascendant", "+12% physical damage.", "Physical", 0.12f),
                        S("Victory Feast", "+18% healing rate.", "HealthRegen", 0.18f)),
                        T(5, "Heir of Walach Harkon",
                        A("Walach's Supreme Technique", "+25% physical damage.", "Physical", 0.25f),
                        X("Blood-Magic Edge", "20% additional magical damage.", "Magical", 0.2f),
                        S("Immortal Duelist", "+50 maximum health.", "HealthMax", 50f),
                        S("Mastered Red Thirst", "+30% healing rate.", "HealthRegen", 0.3f))
                    }),
                SD("Mercenary", "Black Company's Paid-in-Full", new[] { "tor_emp", "empire", "sigmar", "reik", "nuln", "middenheim" },
                    new[]
                    {
                        P("Captain's Sallet of Seven Sieges", SetSlot.Head,
                        K("Seven Sieges", "+10 Tactics.", "Tactics", 10f),
                        S("Wildform of Ghur", "3% extra physical resistance; 15% chance to gain a fleeting 3% physical-resistance bonus upon receiving damage, stacking up to 3 times.", "emp_enchant_ghur_wildform", 0f)),
                        P("Reinforced Coat of the Last Contract", SetSlot.Body,
                        K("Contract Logistics", "+10 Steward.", "Steward", 10f),
                        S("Azure Mirror of Azyr", "2% extra physical resistance; deal 15 lightning damage to enemies attacking you in melee.", "emp_enchant_azyr_azure_mirror", 0f)),
                        P("Paymaster's Gloves", SetSlot.Hand,
                        K("Exact Accounts", "+10 Trade.", "Trade", 10f),
                        S("Feathers to Lead", "8% extra physical resistance with a 5% movement-speed penalty.", "emp_enchant_chamon_feathers_lead", 0f)),
                        P("Boots of the Long March", SetSlot.Leg,
                        S("Long March", "+5% party map speed.", "PartySpeed", 5f),
                        S("Divination of Azyr", "5% extra magic resistance and 7% extra party travel speed.", "emp_enchant_azyr_divination", 0f))
                    },
                    new[]
                    {
                        T(2, "Veterans' Drill",
                        K("Veterans' Drill", "+15 One Handed.", "OneHanded", 15f),
                        S("Paid Volley", "+12% swing speed.", "SwingSpeed", 12f)),
                        T(3, "Campaign Proven",
                        K("Campaign Proven", "+15 Tactics.", "Tactics", 15f),
                        K("No Empty Wagons", "+15 Steward.", "Steward", 15f)),
                        T(4, "No Bad Ground, Only Bad Pay",
                        S("Forced March for Coin", "+8% party map speed.", "PartySpeed", 8f),
                        R("Hard Cases", "8% resistance to physical damage.", "Physical", 0.08f)),
                        T(5, "The Contract Is Paid in Full",
                        K("Captain of Captains", "+25 Leadership.", "Leadership", 25f),
                        K("A Profitable War", "+25 Trade.", "Trade", 25f),
                        S("Only Survivors Get Paid", "+40 maximum health.", "HealthMax", 40f),
                        A("No Fair Fights", "+15% physical damage.", "Physical", 0.15f))
                    }),
                SD("WitchHunter", "Ordo Templaris Purification Gear", new[] { "tor_emp", "empire", "sigmar", "reik", "nuln", "middenheim" },
                    new[]
                    {
                        P("Wide-Brimmed Hat of the Black Chamber", SetSlot.Head,
                        K("Professionally Suspicious", "+10 Scouting.", "Scouting", 10f),
                        S("Soulfire of Sigmar", "6% extra magic resistance, 3 extra HP, and a 10% chance to emit damaging holy energy when attacked in melee by Undead or Daemons.", "emp_blessing_sigmar_soulfire", 0f)),
                        P("Coat of Silvered Chains", SetSlot.Body,
                        R("Silvered Chains", "5% resistance to physical damage.", "Physical", 0.05f),
                        S("Light of Sigmar", "6% extra magic resistance, 3 extra HP, and a 10% chance to gain minor regeneration when attacked in melee by Undead or Daemons.", "emp_blessing_sigmar_light", 0f)),
                        P("Mantle of the Unblinking Eye", SetSlot.Cape,
                        K("The Unblinking Eye", "+8 Roguery.", "Roguery", 8f),
                        S("Beacon of Sigmar", "10% extra magic resistance and 15% extra prayer radius.", "emp_blessing_sigmar_beacon", 0f)),
                        P("Executioner's Gloves", SetSlot.Hand,
                        K("Steady Accusation", "+10 Crossbow.", "Crossbow", 10f),
                        S("Sanctuary of Hysh", "3% extra physical resistance; 5% chance to snare nearby enemies with a net of Hysh upon receiving damage.", "emp_enchant_hysh_sanctuary", 0f))
                    },
                    new[]
                    {
                        T(2, "Sanctioned Arsenal",
                        S("Sanctioned Quarrels", "+12% missile speed.", "MissileSpeed", 12f),
                        S("Silver-Tipped Proof", "+1 missile penetration.", "MultiPenetration", 1f)),
                        T(3, "No Witch Escapes",
                        K("No Witch Escapes", "+15 Scouting.", "Scouting", 15f),
                        S("Unrelenting Pursuit", "+7% movement speed.", "MovementSpeed", 7f)),
                        T(4, "Burn the Evidence",
                        R("Black-Chamber Counterspells", "12% resistance to magical damage.", "Magical", 0.12f),
                        X("Consecrated Ammunition", "15% additional holy damage.", "Holy", 0.15f)),
                        T(5, "Grand Theogonist's Final Writ",
                        S("Final Verdict", "+22% armor penetration.", "ArmorPenetration", 22f),
                        X("Purifying Pyre", "25% additional holy damage.", "Holy", 0.25f),
                        R("Nullify the Unclean", "22% resistance to magical damage.", "Magical", 0.22f),
                        S("One Bolt, Many Heretics", "+2 missile penetration.", "MultiPenetration", 2f))
                    }),
                SD("Necromancer", "Mortuary Regalia of the Restless Host", new[] { "tor_vc", "vamp", "sylvan", "undead", "mousillon" },
                    new[]
                    {
                        P("Crown of Nine Skulls", SetSlot.Head,
                        S("Nine Whispering Skulls", "+18 maximum Winds of Magic.", "WindsOfMagicMax", 18f),
                        S("Legacy of Arkhan", "12% bonus to magic damage and 15% extra spell radius.", "vc_enchant_legacy_arkhan", 0f)),
                        P("Grave-Robes of the First Barrow", SetSlot.Body,
                        R("Barrow-Cold Flesh", "8% resistance to frost damage.", "Frost", 0.08f),
                        S("Unhallowed Pact", "-5% physical resistance and 33% extra spell radius.", "vc_enchant_unhallowed_pact", 0f)),
                        P("Shroud of the Unquiet Dead", SetSlot.Cape,
                        S("Host Without End", "+12% spell radius.", "SpellRadius", 12f),
                        S("Secrets of W'soran", "5% bonus to magic damage and 4 extra Winds of Magic.", "vc_enchant_secrets_wsoran", 0f)),
                        P("Ossuary Grasp", SetSlot.Hand,
                        S("Finger-Bone Conduit", "+0.12 Winds of Magic recharge.", "WindsOfMagicRegen", 0.12f),
                        S("Caress of the Void", "-10% physical resistance and 10 extra Winds of Magic.", "vc_enchant_caress_void", 0f))
                    },
                    new[]
                    {
                        T(2, "Whispers Beneath the Soil",
                        K("Whispers Beneath the Soil", "+15 Spellcraft.", "Spellcraft", 15f),
                        S("Exhumed Reservoir", "+22 maximum Winds of Magic.", "WindsOfMagicMax", 22f)),
                        T(3, "Legion Without Breath",
                        S("Legion Without Breath", "+18% spell radius.", "SpellRadius", 18f),
                        R("Dead Flesh", "8% resistance to physical damage.", "Physical", 0.08f)),
                        T(4, "Dhar Saturation",
                        A("Dhar Saturation", "+12% magical damage.", "Magical", 0.12f),
                        S("Black-Wind Conduit", "+0.18 Winds of Magic recharge.", "WindsOfMagicRegen", 0.18f)),
                        T(5, "Master of the Restless Host",
                        A("Master of the Restless Host", "+25% magical damage.", "Magical", 0.25f),
                        X("Deathly Overflow", "22% additional magical damage.", "Magical", 0.22f),
                        S("Endless Dhar", "+0.38 Winds of Magic recharge.", "WindsOfMagicRegen", 0.38f),
                        S("Dominion of the Barrow", "+35% spell radius.", "SpellRadius", 35f))
                    }),
                SD("BlackGrailKnight", "Accursed Panoply of Mousillon", new[] { "tor_vc", "vamp", "sylvan", "undead", "mousillon" },
                    new[]
                    {
                        P("Helm of the Hollow Grail", SetSlot.Head,
                        K("The False Vow", "+10 Polearm.", "Polearm", 10f),
                        S("Nightshroud", "5% extra magic resistance; 15% chance to reduce attack speed and damage of nearby enemies upon receiving damage.", "vc_enchant_nightshroud", 0f)),
                        P("Blackened Plate of the False Vow", SetSlot.Body,
                        S("Undeath Beneath Plate", "+18 maximum health.", "HealthMax", 18f),
                        S("Ethereal Whispers", "5% extra physical resistance; receiving ranged damage grants 50% ranged-damage immunity for a short time.", "vc_enchant_ethereal_whispers", 0f)),
                        P("Tattered Mantle of the Red Duke", SetSlot.Cape,
                        S("Procession of Terror", "+5% movement speed.", "MovementSpeed", 5f),
                        S("Nightshroud", "5% extra magic resistance; 15% chance to reduce attack speed and damage of nearby enemies upon receiving damage.", "vc_enchant_nightshroud", 0f)),
                        P("Greaves of the Drowned Chapel", SetSlot.Leg,
                        K("Night Charge", "+10 Riding.", "Riding", 10f),
                        S("Ethereal Whispers", "5% extra physical resistance; receiving ranged damage grants 50% ranged-damage immunity for a short time.", "vc_enchant_ethereal_whispers", 0f))
                    },
                    new[]
                    {
                        T(2, "Oath of the Hollow Cup",
                        R("Oath of the Hollow Cup", "10% resistance to holy damage.", "Holy", 0.1f),
                        S("Undying Vow", "+20 maximum health.", "HealthMax", 20f)),
                        T(3, "Black Errantry",
                        K("Black Errantry", "+15 Riding.", "Riding", 15f),
                        S("Impale the Living", "+10% armor penetration.", "ArmorPenetration", 10f)),
                        T(4, "Mousillon's Procession",
                        A("Mousillon's Procession", "+10% physical damage.", "Physical", 0.1f),
                        X("Accursed Grail", "15% additional magical damage.", "Magical", 0.15f)),
                        T(5, "Champion of the False Grail",
                        A("Champion of the False Grail", "+22% physical damage.", "Physical", 0.22f),
                        X("Black Grail Overflow", "25% additional magical damage.", "Magical", 0.25f),
                        S("Deathless Chivalry", "+55 maximum health.", "HealthMax", 55f),
                        R("Armour of the Damned", "14% resistance to all damage.", "All", 0.14f))
                    }),
                SD("Necrarch", "Vestments of the Ossuary Savant", new[] { "tor_vc", "vamp", "sylvan", "undead", "mousillon" },
                    new[]
                    {
                        P("Cranial Diadem of Ushoran's Exile", SetSlot.Head,
                        K("Forbidden Anatomy", "+12 Spellcraft.", "Spellcraft", 12f),
                        S("Secrets of W'soran", "5% bonus to magic damage and 4 extra Winds of Magic.", "vc_enchant_secrets_wsoran", 0f)),
                        P("Hide-Robes of the Flensed Apprentice", SetSlot.Body,
                        R("Flensed Protections", "5% resistance to physical damage.", "Physical", 0.05f),
                        S("Caress of the Void", "-10% physical resistance and 10 extra Winds of Magic.", "vc_enchant_caress_void", 0f)),
                        P("Wing-Mantle of the Cave", SetSlot.Cape,
                        S("Cave-Glide", "+5% movement speed.", "MovementSpeed", 5f),
                        S("Legacy of Arkhan", "12% bonus to magic damage and 15% extra spell radius.", "vc_enchant_legacy_arkhan", 0f)),
                        P("Claws of the Anatomist", SetSlot.Hand,
                        X("Arcane Dissection", "+6% magical damage.", "Magical", 0.06f),
                        S("Unhallowed Pact", "-5% physical resistance and 33% extra spell radius.", "vc_enchant_unhallowed_pact", 0f))
                    },
                    new[]
                    {
                        T(2, "The Ossuary Thesis",
                        K("The Ossuary Thesis", "+18 Spellcraft.", "Spellcraft", 18f),
                        S("Abyssal Reserve", "+25 maximum Winds of Magic.", "WindsOfMagicMax", 25f)),
                        T(3, "Specimen: The Living",
                        X("Specimen: The Living", "+10% magical damage.", "Magical", 0.10f),
                        R("Unholy Proof", "10% resistance to holy damage.", "Holy", 0.1f)),
                        T(4, "Necrarch Metamorphosis",
                        A("Necrarch Metamorphosis", "+14% magical damage.", "Magical", 0.14f),
                        S("Impossible Geometry", "+20% spell radius.", "SpellRadius", 20f)),
                        T(5, "Perfected Monstrosity",
                        A("Perfected Monstrosity", "+30% magical damage.", "Magical", 0.3f),
                        S("Unbound Dhar", "+0.42 Winds of Magic recharge.", "WindsOfMagicRegen", 0.42f),
                        S("Vast Ossuary Mind", "+55 maximum Winds of Magic.", "WindsOfMagicMax", 55f),
                        R("Ancient Abomination", "12% resistance to all damage.", "All", 0.12f))
                    }),
                SD("WarriorPriestUlric", "White Wolf War-Garb", new[] { "tor_emp", "empire", "sigmar", "reik", "nuln", "middenheim" },
                    new[]
                    {
                        P("Wolf-Skull Helm of Middenheim", SetSlot.Head,
                        K("Hunter of Winter", "+10 Athletics.", "Athletics", 10f),
                        S("Frenzy of Ulric", "5% extra physical resistance; receiving melee damage has a 15% chance to drive nearby allies into a battle frenzy.", "emp_blessing_ulric_frenzy", 0f)),
                        P("White Wolf Pelt of the High Temple", SetSlot.Body,
                        S("Fauschlag Endurance", "+15 maximum health.", "HealthMax", 15f),
                        S("Gift of the Winterfather", "5% extra magic resistance and 20 bonus Two Handed skill.", "emp_blessing_ulric_winterfather_gift", 0f)),
                        P("Mantle of the Winter Hunt", SetSlot.Cape,
                        S("Winter Hunt", "+5% movement speed.", "MovementSpeed", 5f),
                        S("Wildform of Ghur", "3% extra physical resistance; 15% chance to gain a fleeting 3% physical-resistance bonus upon receiving damage, stacking up to 3 times.", "emp_enchant_ghur_wildform", 0f)),
                        P("Gauntlets of the Fauschlag", SetSlot.Hand,
                        X("Fauschlag Bite", "+7% physical damage.", "Physical", 0.07f),
                        S("Frenzy of Ulric", "5% extra physical resistance; receiving melee damage has a 15% chance to drive nearby allies into a battle frenzy.", "emp_blessing_ulric_frenzy", 0f))
                    },
                    new[]
                    {
                        T(2, "Howl Across the Snow",
                        K("Howl Across the Snow", "+15 Leadership.", "Leadership", 15f),
                        S("Pack Chase", "+7% movement speed.", "MovementSpeed", 7f)),
                        T(3, "Winter's Teeth",
                        X("Winter's Teeth", "15% additional frost damage.", "Frost", 0.15f),
                        X("Ice-Rimed Edge", "+10% physical damage.", "Physical", 0.10f)),
                        T(4, "Fury of the White Wolf",
                        A("Fury of the White Wolf", "+12% physical damage.", "Physical", 0.12f),
                        S("Predator's Tempo", "+8% swing speed.", "SwingSpeed", 8f)),
                        T(5, "Chosen of Ulric",
                        X("Ulric's Killing Frost", "28% additional frost damage.", "Frost", 0.28f),
                        A("Chosen of Ulric", "+22% physical damage.", "Physical", 0.22f),
                        S("Fauschlag Unyielding", "+50 maximum health.", "HealthMax", 50f),
                        R("Lord of Winter", "14% resistance to all damage.", "All", 0.14f))
                    }),
                SD("ImperialMagister", "Regalia of the Eight Colleges", new[] { "tor_emp", "empire", "sigmar", "reik", "nuln", "middenheim" },
                    new[]
                    {
                        P("Volans' Star-Circlet", SetSlot.Head,
                        K("Volans' Method", "+10 Spellcraft.", "Spellcraft", 10f),
                        S("Providence of Hysh", "5% extra magic resistance and 10 bonus Spellcraft.", "emp_enchant_hysh_providence", 0f)),
                        P("Robes of the Conclave", SetSlot.Body,
                        R("Conclave Wards", "7% resistance to magical damage.", "Magical", 0.07f),
                        S("Clarity of Hysh", "3 extra Winds of Magic.", "emp_enchant_hysh_clarity", 0f)),
                        P("Mantle of the Eight Winds", SetSlot.Cape,
                        S("Eightfold Reach", "+12% spell radius.", "SpellRadius", 12f),
                        S("Messengers of Shyish", "5% extra magic resistance; 10% chance to inflict horrific visions and a damaging DoT on nearby enemies upon taking damage.", "emp_enchant_shyish_messengers", 0f)),
                        P("Formulaic Gloves of Binding", SetSlot.Hand,
                        S("Formula of Binding", "+0.10 career-resource generation.", "CustomResourceGain", 0.1f),
                        S("Divination of Azyr", "5% extra magic resistance and 7% extra party travel speed.", "emp_enchant_azyr_divination", 0f))
                    },
                    new[]
                    {
                        T(2, "Collegiate Discipline",
                        K("Collegiate Discipline", "+15 Spellcraft.", "Spellcraft", 15f),
                        S("Conclave Reservoir", "+22 maximum Winds of Magic.", "WindsOfMagicMax", 22f)),
                        T(3, "Conjunction of Three Winds",
                        S("Conjunction of Three Winds", "+18% spell radius.", "SpellRadius", 18f),
                        R("Counter-Formulae", "10% resistance to magical damage.", "Magical", 0.1f)),
                        T(4, "The Eightfold Equation",
                        A("The Eightfold Equation", "+12% magical damage.", "Magical", 0.12f),
                        S("Arcane Channel", "+0.18 Winds of Magic recharge.", "WindsOfMagicRegen", 0.18f)),
                        T(5, "Successor to Volans",
                        A("Successor to Volans", "+26% magical damage.", "Magical", 0.26f),
                        S("Mastered Conjunction", "+0.38 Winds of Magic recharge.", "WindsOfMagicRegen", 0.38f),
                        S("Eightfold Reservoir", "+50 maximum Winds of Magic.", "WindsOfMagicMax", 50f),
                        R("Wards of All Eight Colleges", "16% resistance to all damage.", "All", 0.16f))
                    }),
                SD("Waywatcher", "Silent Talons of Athel Loren", new[] { "tor_we_", "woodelf", "wood_elf", "asrai", "athel", "loren" },
                    new[]
                    {
                        P("Hood of the Moonless Glade", SetSlot.Head,
                        K("Moonless Hawkeye", "+10 Bow.", "Bow", 10f),
                        S("Embrace of Isha", "4% extra Ward Save; 10% chance to recover 10 HP upon receiving damage.", "asrai_enchant_embrace_isha", 0f)),
                        P("Shadowweave Jerkin", SetSlot.Body,
                        R("Shadowweave", "5% resistance to physical damage.", "Physical", 0.05f),
                        S("Oakheart's Blessing", "4% extra physical resistance and 4% bonus to magic damage.", "asrai_enchant_oakhart_blessing", 0f)),
                        P("Cloak of Falling Leaves", SetSlot.Cape,
                        S("Vanish with the Leaves", "+6% movement speed.", "MovementSpeed", 6f),
                        S("The Tree Lords' Bargain", "5 max HP; 15% chance to summon a Dryad upon taking damage.", "asrai_enchant_tree_lord", 0f)),
                        P("Boots of the Hidden Path", SetSlot.Leg,
                        S("Hidden Path", "+4% party map speed.", "PartySpeed", 4f),
                        S("Leylines and the Weave", "Gain 8 Forest Harmony daily and 3% extra physical resistance.", "asrai_enchant_leylines_weave", 0f))
                    },
                    new[]
                    {
                        T(2, "Needle Through Bark",
                        S("Needle Through Bark", "+1 missile penetration.", "MultiPenetration", 1f),
                        S("Asrai Fletching", "+12% missile speed.", "MissileSpeed", 12f)),
                        T(3, "Noiseless Quarry",
                        K("Noiseless Quarry", "+15 Scouting.", "Scouting", 15f),
                        S("Forest Stalker", "+7% movement speed.", "MovementSpeed", 7f)),
                        T(4, "The Glade Decides",
                        S("The Glade Decides", "+14% armor penetration.", "ArmorPenetration", 14f),
                        A("Perfect Ambush", "+10% physical damage.", "Physical", 0.1f)),
                        T(5, "Invisible Death of Athel Loren",
                        K("Invisible Death", "+30 Bow.", "Bow", 30f),
                        S("Ghost Arrows", "+2 missile penetration.", "MultiPenetration", 2f),
                        S("Flight of Loren", "+25% missile speed.", "MissileSpeed", 25f),
                        A("Silent Talon", "+22% physical damage.", "Physical", 0.22f))
                    }),
                SD("Spellsinger", "Calaingor's Living Weave", new[] { "tor_we_", "woodelf", "wood_elf", "asrai", "athel", "loren" },
                    new[]
                    {
                        P("Crown of Living Branches", SetSlot.Head,
                        K("Song-Lore", "+10 Spellcraft.", "Spellcraft", 10f),
                        S("Radiance of the Woods", "3% Ward Save and 4 extra Winds of Magic.", "asrai_enchant_radiance_woods", 0f)),
                        P("Robe of Sap and Starlight", SetSlot.Body,
                        S("Sap Renewal", "+8% healing rate.", "HealthRegen", 0.08f),
                        S("Embrace of Isha", "4% extra Ward Save; 10% chance to recover 10 HP upon receiving damage.", "asrai_enchant_embrace_isha", 0f)),
                        P("Mantle of Whispering Leaves", SetSlot.Cape,
                        S("Whispering Canopy", "+12% spell radius.", "SpellRadius", 12f),
                        S("The Tree Lords' Bargain", "5 max HP; 15% chance to summon a Dryad upon taking damage.", "asrai_enchant_tree_lord", 0f)),
                        P("Rootstep Sandals", SetSlot.Leg,
                        S("Rootstep", "+4% party map speed.", "PartySpeed", 4f),
                        S("Touch of Lileath", "5% magic resistance and 3 extra Winds of Magic.", "we_enchant_touch_lileath", 0f))
                    },
                    new[]
                    {
                        T(2, "Duet with the Deepwood",
                        S("Duet with the Deepwood", "+0.14 Winds of Magic recharge.", "WindsOfMagicRegen", 0.14f),
                        S("Living Renewal", "+10% healing rate.", "HealthRegen", 0.1f)),
                        T(3, "Canopy Chorus",
                        K("Canopy Chorus", "+15 Spellcraft.", "Spellcraft", 15f),
                        S("Calaingor's Canopy", "+18% spell radius.", "SpellRadius", 18f)),
                        T(4, "The Forest Answers",
                        A("The Forest Answers", "+12% magical damage.", "Magical", 0.12f),
                        R("Bark and Starlight", "8% resistance to all damage.", "All", 0.08f)),
                        T(5, "Voice of Athel Loren",
                        A("Voice of Athel Loren", "+25% magical damage.", "Magical", 0.25f),
                        S("Evergreen Wellspring", "+0.36 Winds of Magic recharge.", "WindsOfMagicRegen", 0.36f),
                        S("Worldroot Reach", "+35% spell radius.", "SpellRadius", 35f),
                        S("Life-Song", "+28% healing rate.", "HealthRegen", 0.28f))
                    }),
                SD("Warden", "Harness of Kurnous' Wild Hunt", new[] { "tor_we_", "woodelf", "wood_elf", "asrai", "athel", "loren" },
                    new[]
                    {
                        P("Antlered Helm of the Hunt", SetSlot.Head,
                        K("Hunter's Sight", "+8 Scouting.", "Scouting", 8f),
                        S("Oakheart's Blessing", "4% extra physical resistance and 4% bonus to magic damage.", "asrai_enchant_oakhart_blessing", 0f)),
                        P("Thornscale Cuirass", SetSlot.Body,
                        R("Thornscale", "6% resistance to physical damage.", "Physical", 0.06f),
                        S("Embrace of Isha", "4% extra Ward Save; 10% chance to recover 10 HP upon receiving damage.", "asrai_enchant_embrace_isha", 0f)),
                        P("Cloak of the Stag's Shadow", SetSlot.Cape,
                        S("Stag's Shadow", "+5% movement speed.", "MovementSpeed", 5f),
                        S("The Tree Lords' Bargain", "5 max HP; 15% chance to summon a Dryad upon taking damage.", "asrai_enchant_tree_lord", 0f)),
                        P("Greaves of the Spear-Dancer", SetSlot.Leg,
                        K("Spear-Dancer's Step", "+10 Athletics.", "Athletics", 10f),
                        S("Leylines and the Weave", "Gain 8 Forest Harmony daily and 3% extra physical resistance.", "asrai_enchant_leylines_weave", 0f))
                    },
                    new[]
                    {
                        T(2, "Horn of the Hunt",
                        K("Horn of the Hunt", "+12 Leadership.", "Leadership", 12f),
                        S("Wild Pursuit", "+7% movement speed.", "MovementSpeed", 7f)),
                        T(3, "Kurnous' Quarry",
                        K("Kurnous' Quarry", "+15 Polearm.", "Polearm", 15f),
                        S("Spear Through Shield", "+22% shield damage.", "ShieldDamage", 22f)),
                        T(4, "The Wild Hunt Rides",
                        A("The Wild Hunt Rides", "+12% physical damage.", "Physical", 0.12f),
                        S("Impaling Momentum", "+12% armor penetration.", "ArmorPenetration", 12f)),
                        T(5, "Spear of Kurnous Incarnate",
                        A("Kurnous Incarnate", "+26% physical damage.", "Physical", 0.26f),
                        S("Great Impalement", "+24% armor penetration.", "ArmorPenetration", 24f),
                        S("Shatter the Quarry's Guard", "+40% shield damage.", "ShieldDamage", 40f),
                        S("Heart of the Great Stag", "+40 maximum health.", "HealthMax", 40f))
                    }),
                SD("GreyLord", "Shrouds of the Grey College", new[] { "tor_eonir", "eonir", "grey lord", "warden of the storms", "storm warden" },
                    new[]
                    {
                        P("Cowl of Unremembered Faces", SetSlot.Head,
                        K("Borrowed Faces", "+10 Roguery.", "Roguery", 10f),
                        S("Sanctuary of Saphery", "Upon receiving ranged damage, gain complete ranged-damage immunity for 30 seconds.", "eo_enchant_sanctuary_saphery", 0f)),
                        P("Grey Robes of the Ninth Door", SetSlot.Body,
                        R("Ninth-Door Ward", "7% resistance to magical damage.", "Magical", 0.07f),
                        S("Wisdom of Hoeth", "7 extra Winds of Magic and 5% bonus to magic damage.", "eo_enchant_wisdom_hoeth", 0f)),
                        P("Mantle of Ulgu's Mists", SetSlot.Cape,
                        S("Mists of Ulgu", "+10% spell radius.", "SpellRadius", 10f),
                        S("Dusk of the Woods", "-8% physical resistance and 8 extra Winds of Magic.", "asrai_enchant_dusk_wood", 0f)),
                        P("Gloves of the Hidden Hand", SetSlot.Hand,
                        S("Hidden-Hand Channel", "+0.10 Winds of Magic recharge.", "WindsOfMagicRegen", 0.1f),
                        S("Touch of Lileath", "5% magic resistance and 3 extra Winds of Magic.", "we_enchant_touch_lileath", 0f))
                    },
                    new[]
                    {
                        T(2, "Veiled Intent",
                        R("Veiled Intent", "10% resistance to magical damage.", "Magical", 0.1f),
                        K("Perfect Deception", "+15 Roguery.", "Roguery", 15f)),
                        T(3, "The Ninth Door Opens",
                        K("Keeper of the Ninth Door", "+15 Spellcraft.", "Spellcraft", 15f),
                        S("Impossible Doorway", "+18% spell radius.", "SpellRadius", 18f)),
                        T(4, "Shroud the Battlefield",
                        S("Shroud the Battlefield", "+8% movement speed.", "MovementSpeed", 8f),
                        A("Shadow-Strike", "+10% magical damage.", "Magical", 0.1f)),
                        T(5, "Lord of Ulgu's Labyrinth",
                        A("Lord of Ulgu's Labyrinth", "+25% magical damage.", "Magical", 0.25f),
                        R("The Unseen Cannot Be Struck", "16% resistance to all damage.", "All", 0.16f),
                        S("Endless Mists", "+0.35 Winds of Magic recharge.", "WindsOfMagicRegen", 0.35f),
                        S("Lost Space", "+32% spell radius.", "SpellRadius", 32f))
                    }),
                SD("KnightOldWorld", "Heirlooms of the Reiksguard Exemplar", new[] { "tor_emp", "empire", "sigmar", "reik", "nuln", "middenheim" },
                    new[]
                    {
                        P("Crested Helm of the Old Orders", SetSlot.Head,
                        K("Lore of the Old Orders", "+8 Leadership.", "Leadership", 8f),
                        S("Feathers to Lead", "8% extra physical resistance with a 5% movement-speed penalty.", "emp_enchant_chamon_feathers_lead", 0f)),
                        P("Runeforged Plate of the Imperial Road", SetSlot.Body,
                        S("Imperial-Road Endurance", "+15 maximum health.", "HealthMax", 15f),
                        S("Azure Mirror of Azyr", "2% extra physical resistance; deal 15 lightning damage to enemies attacking you in melee.", "emp_enchant_azyr_azure_mirror", 0f)),
                        P("Gauntlets of the Twelve Duels", SetSlot.Hand,
                        K("Twelve Duels", "+10 One Handed.", "OneHanded", 10f),
                        S("Wildform of Ghur", "3% extra physical resistance; 15% chance to gain a fleeting 3% physical-resistance bonus upon receiving damage, stacking up to 3 times.", "emp_enchant_ghur_wildform", 0f)),
                        P("Spurs of the Old World", SetSlot.Leg,
                        K("Old-World Cavalry", "+10 Riding.", "Riding", 10f),
                        S("Divination of Azyr", "5% extra magic resistance and 7% extra party travel speed.", "emp_enchant_azyr_divination", 0f))
                    },
                    new[]
                    {
                        T(2, "Drill of the Old Orders",
                        K("Drill of the Old Orders", "+15 One Handed.", "OneHanded", 15f),
                        K("Reiksguard Seat", "+15 Riding.", "Riding", 15f)),
                        T(3, "Hold the Imperial Road",
                        R("Hold the Imperial Road", "10% resistance to physical damage.", "Physical", 0.1f),
                        S("Formation Breaker", "+22% shield damage.", "ShieldDamage", 22f)),
                        T(4, "Exemplar's Command",
                        K("Exemplar's Command", "+18 Leadership.", "Leadership", 18f),
                        S("Veteran of a Hundred Roads", "+25 maximum health.", "HealthMax", 25f)),
                        T(5, "Living Heirloom of the Empire",
                        A("Living Heirloom", "+22% physical damage.", "Physical", 0.22f),
                        R("Runeguard of the Old World", "14% resistance to all damage.", "All", 0.14f),
                        S("Exemplar's Heart", "+50 maximum health.", "HealthMax", 50f),
                        S("Break the Enemy Line", "+45% shield damage.", "ShieldDamage", 45f))
                    }),
                SD("Ironbreaker", "Gromril Oathwall of Karaz-a-Karak", new[] { "tor_dw", "dwarf", "karak", "gromril" },
                    new[]
                    {
                        P("Fullhelm of the Deep Gate", SetSlot.Head,
                        K("Deep-Gate Watch", "+8 Athletics.", "Athletics", 8f),
                        S("Master Rune of Gromril", "5% Ward Save.", "dw_master_rune_gromril", 0f)),
                        P("Gromril Plate of the Last Hold", SetSlot.Body,
                        S("The Last Hold", "+20 maximum health.", "HealthMax", 20f),
                        S("Master Rune of Steel", "10% extra physical resistance.", "dw_master_rune_steel", 0f)),
                        P("Gauntlets of the Gatewarden", SetSlot.Hand,
                        S("Gatewarden's Brace", "+150 shield hit points.", "ShieldHealth", 150f),
                        S("Rune of Preservation", "6 max HP; 10% chance to ignore lethal damage and recover 5 HP upon taking damage.", "dw_master_rune_preservation", 0f)),
                        P("Ironshod Boots of the Underway", SetSlot.Leg,
                        S("Rooted in Stone", "+3% movement speed.", "MovementSpeed", 3f),
                        S("Rune of Spell Eating", "10% extra magic resistance; 50% chance to gain 100% magic resistance for a short time upon taking damage.", "dw_rune_spell_eating", 0f))
                    },
                    new[]
                    {
                        T(2, "Close the Gate",
                        S("Close the Gate", "+250 shield hit points.", "ShieldHealth", 250f),
                        R("Unbroken Line", "8% resistance to physical damage.", "Physical", 0.08f)),
                        T(3, "Oathwall",
                        K("Oathwall Drill", "+15 Athletics.", "Athletics", 15f),
                        S("Counter-Bash", "+25% shield damage.", "ShieldDamage", 25f)),
                        T(4, "Nothing Passes",
                        R("Nothing Passes", "10% resistance to all damage.", "All", 0.1f),
                        S("Stone-Heart", "+30 maximum health.", "HealthMax", 30f)),
                        T(5, "Living Gate of Karaz-a-Karak",
                        S("Living Gate", "+600 shield hit points.", "ShieldHealth", 600f),
                        R("Gromril Oathwall", "25% resistance to physical damage.", "Physical", 0.25f),
                        R("Runic Oathwall", "22% resistance to magical damage.", "Magical", 0.22f),
                        S("Endurance of the Mountain", "+65 maximum health.", "HealthMax", 65f))
                    }),
                SD("Slayer", "Doomseeker's Last Oath", new[] { "tor_dw", "dwarf", "karak", "gromril" },
                    new[]
                    {
                        P("Crest of the Unfulfilled Oath", SetSlot.Head,
                        K("Unfulfilled Oath", "+10 Two Handed.", "TwoHanded", 10f),
                        S("Rune of Fortitude", "3% Ward Save and 4 extra HP.", "dw_rune_fortitude", 0f)),
                        P("Trophy-Cloak of Worthy Foes", SetSlot.Cape,
                        K("Catalogue of Worthy Foes", "+8 Tactics.", "Tactics", 8f),
                        S("Rune of Vigour", "5 extra HP.", "dw_rune_vigour", 0f)),
                        P("Bracers of the Deathblow", SetSlot.Hand,
                        S("Deathblow Grip", "+1 cleave.", "Cleave", 1f),
                        S("Rune of Protection", "5% extra magic resistance and 2 extra HP.", "dw_rune_protection", 0f)),
                        P("Ironbound Boots of the Long Doom", SetSlot.Leg,
                        K("Long Doom-Walk", "+10 Athletics.", "Athletics", 10f),
                        S("Rune of Iron", "7% extra physical resistance.", "dw_rune_iron", 0f))
                    },
                    new[]
                    {
                        T(2, "No Time for Shields",
                        A("No Time for Shields", "+10% physical damage.", "Physical", 0.1f),
                        S("Doom-Frenzy", "+7% swing speed.", "SwingSpeed", 7f)),
                        T(3, "Find a Worthier Foe",
                        K("Find a Worthier Foe", "+18 Two Handed.", "TwoHanded", 18f),
                        X("Monster-Rending Edge", "+12% physical damage.", "Physical", 0.12f)),
                        T(4, "Too Angry to Die",
                        S("Too Angry to Die", "+18% healing rate.", "HealthRegen", 0.18f),
                        S("Grim Determination", "+25 maximum health.", "HealthMax", 25f)),
                        T(5, "The Doom That Walks",
                        A("The Doom That Walks", "+32% physical damage.", "Physical", 0.32f),
                        S("Axe Through a Throng", "+2 cleave.", "Cleave", 2f),
                        X("The Final Blow", "+25% physical damage.", "Physical", 0.25f),
                        S("One Last Breath", "+55 maximum health.", "HealthMax", 55f))
                    }),
                SD("Runelord", "Thungni's Master-Rune Regalia", new[] { "tor_dw", "dwarf", "karak", "gromril" },
                    new[]
                    {
                        P("Runic Crown of the Ancestor Gods", SetSlot.Head,
                        K("Ancestor-Lore", "+8 Spellcraft.", "Spellcraft", 8f),
                        S("Master Rune of Gromril", "5% Ward Save.", "dw_master_rune_gromril", 0f)),
                        P("Apron-Plate of the Anvil Guard", SetSlot.Body,
                        S("Anvil Guard", "+18 maximum health.", "HealthMax", 18f),
                        S("Rune of Spell Eating", "10% extra magic resistance; 50% chance to gain 100% magic resistance for a short time upon taking damage.", "dw_rune_spell_eating", 0f)),
                        P("Gauntlets of Inscription", SetSlot.Hand,
                        K("Perfect Inscription", "+10 Engineering.", "Engineering", 10f),
                        S("Rune of Preservation", "6 max HP; 10% chance to ignore lethal damage and recover 5 HP upon taking damage.", "dw_master_rune_preservation", 0f)),
                        P("Mantle of Warded Stone", SetSlot.Cape,
                        R("Warded Stone", "6% resistance to physical damage.", "Physical", 0.06f),
                        S("Master Rune of Steel", "10% extra physical resistance.", "dw_master_rune_steel", 0f))
                    },
                    new[]
                    {
                        T(2, "Rune of Furnace and Form",
                        K("Rune of Furnace and Form", "+15 Engineering.", "Engineering", 15f),
                        R("Master Rune of Fire-Warding", "12% resistance to fire damage.", "Fire", 0.12f)),
                        T(3, "Rune of Preservation",
                        R("Rune of Preservation", "10% resistance to all damage.", "All", 0.1f),
                        S("Ancestral Endurance", "+25 maximum health.", "HealthMax", 25f)),
                        T(4, "Rune of Power",
                        S("Master Rune of Power", "+0.22 career-resource generation.", "CustomResourceGain", 0.22f),
                        X("Rune of Striking", "12% additional magical damage.", "Magical", 0.12f)),
                        T(5, "Thungni's Living Anvil",
                        R("Master Rune of Warding", "28% resistance to magical damage.", "Magical", 0.28f),
                        R("Master Rune of Adamant", "22% resistance to physical damage.", "Physical", 0.22f),
                        X("Ancestral Rune-Strike", "25% additional magical damage.", "Magical", 0.25f),
                        S("Living Anvil", "+60 maximum health.", "HealthMax", 60f))
                    }),
                SD("OrcBoss", "Da Biggest Boss's War-Kit", new[] { "tor_orc", "greenskin", "orc", "goblin", "waaagh" },
                    new[]
                    {
                        P("Biggest Horned 'Elmet", SetSlot.Head,
                        K("Da Biggest Noggin", "+10 Leadership.", "Leadership", 10f),
                        S("Tuffness uv Gork", "4% extra physical resistance with a 5% movement-speed penalty.", "gs_enchant_tuffness_gork", 0f)),
                        P("Boss-Plate of Stolen Gromril", SetSlot.Body,
                        S("Bigger Than You", "+20 maximum health.", "HealthMax", 20f),
                        S("Tuffness uv Gork", "4% extra physical resistance with a 5% movement-speed penalty.", "gs_enchant_tuffness_gork", 0f)),
                        P("Trophy-Rack of Da Best Fights", SetSlot.Cape,
                        K("Da Best Fights", "+10 Tactics.", "Tactics", 10f),
                        S("Call uv da Great Green", "3 max Winds of Magic and 4% extra magic resistance.", "gs_enchant_call_great_green", 0f)),
                        P("Iron-Kapped Stompas", SetSlot.Leg,
                        K("Stomp 'Em Flat", "+10 Athletics.", "Athletics", 10f),
                        S("Call uv da Great Green", "3 max Winds of Magic and 4% extra magic resistance.", "gs_enchant_call_great_green", 0f))
                    },
                    new[]
                    {
                        T(2, "Bash 'Em Good",
                        S("Bash 'Em Good", "+25% shield damage.", "ShieldDamage", 25f),
                        S("Bigger Swings", "+7% swing speed.", "SwingSpeed", 7f)),
                        T(3, "Da Boyz Are Watchin'",
                        K("Da Boyz Are Watchin'", "+18 Leadership.", "Leadership", 18f),
                        S("WAAAGH! Momentum", "+0.15 career-resource generation.", "CustomResourceGain", 0.15f)),
                        T(4, "Right Proper Boss",
                        A("Right Proper Boss", "+14% physical damage.", "Physical", 0.14f),
                        S("Chop Through More Gitz", "+1 cleave.", "Cleave", 1f)),
                        T(5, "Da Biggest Boss There Is",
                        A("Da Biggest Boss There Is", "+30% physical damage.", "Physical", 0.3f),
                        S("Too Big to Kill", "+70 maximum health.", "HealthMax", 70f),
                        S("One Chop, Lotsa Gitz", "+2 cleave.", "Cleave", 2f),
                        S("Never-Ending WAAAGH!", "+0.35 career-resource generation.", "CustomResourceGain", 0.35f))
                    }),
                SD("OrcShaman", "Moon-Idol Trappings of Baduum", new[] { "tor_orc", "greenskin", "orc", "goblin", "waaagh" },
                    new[]
                    {
                        P("Moon-Horn Crown", SetSlot.Head,
                        K("More Voices in Da Head", "+10 Spellcraft.", "Spellcraft", 10f),
                        S("Call uv da Great Green", "3 max Winds of Magic and 4% extra magic resistance.", "gs_enchant_call_great_green", 0f)),
                        P("Mushroom-Smoke Robes", SetSlot.Body,
                        R("Mushroom-Smoke Confusion", "6% resistance to magical damage.", "Magical", 0.06f),
                        S("Call uv da Great Green", "3 max Winds of Magic and 4% extra magic resistance.", "gs_enchant_call_great_green", 0f)),
                        P("Squig-Hide Fetish Mantle", SetSlot.Cape,
                        S("Bouncy Squig-Hide", "+5% movement speed.", "MovementSpeed", 5f),
                        S("Tuffness uv Gork", "4% extra physical resistance with a 5% movement-speed penalty.", "gs_enchant_tuffness_gork", 0f)),
                        P("Barely-Magical Stompas", SetSlot.Leg,
                        S("Sparky Footwork", "+0.10 Winds of Magic recharge.", "WindsOfMagicRegen", 0.1f),
                        S("Call uv da Great Green", "3 max Winds of Magic and 4% extra magic resistance.", "gs_enchant_call_great_green", 0f))
                    },
                    new[]
                    {
                        T(2, "Da Moon Is Lookin'",
                        S("Da Moon Is Lookin'", "+16% spell radius.", "SpellRadius", 16f),
                        S("Green Conduit", "+0.14 Winds of Magic recharge.", "WindsOfMagicRegen", 0.14f)),
                        T(3, "Gork's Idea (Maybe Mork's)",
                        K("Gork's Idea (Maybe Mork's)", "+15 Spellcraft.", "Spellcraft", 15f),
                        S("Stolen Waaagh! Power", "+25 maximum Winds of Magic.", "WindsOfMagicMax", 25f)),
                        T(4, "Louder Is Better",
                        A("Louder Is Better", "+14% magical damage.", "Magical", 0.14f),
                        S("Really, Really Loud", "+20% spell radius.", "SpellRadius", 20f)),
                        T(5, "Baduum's Moon-Idol Ascendant",
                        A("Baduum's Moon-Idol", "+28% magical damage.", "Magical", 0.28f),
                        X("Great Green Blast", "25% additional magical damage.", "Magical", 0.25f),
                        S("Endless Green Conduit", "+0.40 Winds of Magic recharge.", "WindsOfMagicRegen", 0.4f),
                        S("Full Moon Reservoir", "+55 maximum Winds of Magic.", "WindsOfMagicMax", 55f))
                    })
            };
        }

        private static SetDefinition SD(string careerId, string setName,
            string[] factionTokens, SetPieceDefinition[] pieces, SetTierDefinition[] tiers)
        {
            string career = careerId.ToLowerInvariant();
            for (int p = 0; p < pieces.Length; p++)
            {
                for (int e = 0; e < pieces[p].Effects.Length; e++)
                    pieces[p].Effects[e].Id = RealPiecePrefix + career +
                        "_p" + (p + 1) + "_e" + e;

                // The second intrinsic slot is a real TOR enchantment/blessing/rune.
                // Preserve TOR's native trait ID so scripted procs and native mechanics remain intact.
                if (pieces[p].Effects.Length > 1)
                    pieces[p].Effects[1].Id = pieces[p].Effects[1].EffectType;
            }

            for (int t = 0; t < tiers.Length; t++)
            {
                for (int e = 0; e < tiers[t].Effects.Length; e++)
                    tiers[t].Effects[e].Id = BonusPrefix + career + "_" +
                        tiers[t].RequiredPieces + "_e" + e;
            }

            return new SetDefinition
            {
                CareerId = careerId,
                SetName = setName,
                FactionTokens = factionTokens,
                Pieces = pieces,
                Tiers = tiers
            };
        }

        private static SetPieceDefinition P(string itemName, SetSlot slot,
            params TraitDefinition[] effects)
        {
            return new SetPieceDefinition
            {
                ItemName = itemName,
                Slot = slot,
                Effects = effects
            };
        }

        private static SetTierDefinition T(int count, string name,
            params TraitDefinition[] effects)
        {
            return new SetTierDefinition
            {
                RequiredPieces = count,
                Name = name,
                Effects = effects
            };
        }

        private static TraitDefinition S(string name, string description,
            string statType, float value)
        {
            return new TraitDefinition
            {
                Name = name,
                Description = description,
                Kind = TraitKind.Stat,
                EffectType = statType,
                Value = value,
                IconName = "traits_magic_icon"
            };
        }

        private static TraitDefinition K(string name, string description,
            string skillId, float value)
        {
            return new TraitDefinition
            {
                Name = name,
                Description = description,
                Kind = TraitKind.Stat,
                EffectType = "Skill",
                SkillId = skillId,
                Value = value,
                IconName = "traits_magic_icon"
            };
        }

        private static TraitDefinition A(string name, string description,
            string damageType, float value)
        {
            return new TraitDefinition
            {
                Name = name,
                Description = description,
                Kind = TraitKind.Amplifier,
                EffectType = damageType,
                Value = value,
                IconName = DamageIcon(damageType)
            };
        }

        private static TraitDefinition R(string name, string description,
            string damageType, float value)
        {
            return new TraitDefinition
            {
                Name = name,
                Description = description,
                Kind = TraitKind.Resistance,
                EffectType = damageType,
                Value = value,
                IconName = DamageIcon(damageType)
            };
        }

        private static TraitDefinition X(string name, string description,
            string damageType, float value)
        {
            return new TraitDefinition
            {
                Name = name,
                Description = description,
                Kind = TraitKind.AdditionalDamage,
                EffectType = damageType,
                Value = value,
                IconName = DamageIcon(damageType)
            };
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

        private static int CountArmorPieces()
        {
            int count = 0;
            for (int i = 0; i < Definitions.Length; i++)
                count += Definitions[i].Pieces.Length;
            return count;
        }

        private static string[] Tokenize(string text)
        {
            if (String.IsNullOrWhiteSpace(text))
                return new string[0];

            string normalized = text.ToLowerInvariant();
            char[] separators = new[]
            {
                ' ', '\t', '\r', '\n', '-', '_', '\'', '"', ',', '.', ':',
                ';', '!', '?', '(', ')', '[', ']', '{', '}', '/'
            };
            string[] raw = normalized.Split(separators,
                StringSplitOptions.RemoveEmptyEntries);
            List<string> tokens = new List<string>();
            for (int i = 0; i < raw.Length; i++)
            {
                string token = raw[i];
                if (token.Length < 3 ||
                    token == "the" || token == "and" || token == "of" ||
                    token == "for" || token == "with")
                    continue;
                if (!tokens.Contains(token))
                    tokens.Add(token);
            }
            return tokens.ToArray();
        }

        private static int ScoreTokens(string text, string[] tokens, int perMatch)
        {
            if (tokens == null)
                return 0;
            int score = 0;
            for (int i = 0; i < tokens.Length; i++)
            {
                string token = tokens[i];
                if (!String.IsNullOrWhiteSpace(token) &&
                    text.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                    score += perMatch;
            }
            return score;
        }

        private static bool ContainsAny(string text, params string[] tokens)
        {
            if (String.IsNullOrEmpty(text) || tokens == null)
                return false;
            for (int i = 0; i < tokens.Length; i++)
                if (text.IndexOf(tokens[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        private static Type TypeByName(string fullName)
        {
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

        private static object GetMainHeroIfReady()
        {
            object campaign = GetStaticProperty(
                TypeByName("TaleWorlds.CampaignSystem.Campaign"),
                "Current");
            if (campaign == null)
                return null;

            try
            {
                return GetStaticProperty(
                    TypeByName("TaleWorlds.CampaignSystem.Hero"),
                    "MainHero");
            }
            catch (TargetInvocationException ex)
            {
                if (ex.InnerException is NullReferenceException)
                    return null;
                throw;
            }
            catch (NullReferenceException)
            {
                return null;
            }
        }

        private static object GetStaticProperty(Type type, string name)
        {
            if (type == null)
                return null;
            PropertyInfo property = type.GetProperty(name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            return property == null ? null : property.GetValue(null, null);
        }

        private static object GetProperty(object instance, string name)
        {
            if (instance == null)
                return null;
            Type type = instance.GetType();
            while (type != null)
            {
                PropertyInfo property = type.GetProperty(name,
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (property != null)
                    return property.GetValue(instance, null);
                type = type.BaseType;
            }
            return null;
        }

        private static object GetField(object instance, string name)
        {
            if (instance == null)
                return null;
            Type type = instance.GetType();
            while (type != null)
            {
                FieldInfo field = type.GetField(name,
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (field != null)
                    return field.GetValue(instance);
                type = type.BaseType;
            }
            return null;
        }

        private static void SetField(object instance, string name, object value)
        {
            if (instance == null)
                throw new ArgumentNullException("instance");
            FieldInfo field = instance.GetType().GetField(name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null)
                throw new MissingMemberException(instance.GetType().FullName, name);
            field.SetValue(instance, value);
        }

        private static void SetProperty(object instance, string name, object value)
        {
            PropertyInfo property = instance.GetType().GetProperty(name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (property == null)
                throw new MissingMemberException(instance.GetType().FullName, name);
            if (value != null && !property.PropertyType.IsInstanceOfType(value))
                value = Convert.ChangeType(value, property.PropertyType);
            property.SetValue(instance, value, null);
        }

        private static MethodInfo FindStaticMethod(Type type, string name, int count)
        {
            if (type == null)
                return null;
            MethodInfo[] methods = type.GetMethods(BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.Static);
            for (int i = 0; i < methods.Length; i++)
                if (methods[i].Name == name && methods[i].GetParameters().Length == count)
                    return methods[i];
            return null;
        }

        private static MethodInfo FindInstanceMethod(Type type, string name,
            Type[] parameterTypes)
        {
            if (type == null)
                return null;
            MethodInfo[] methods = type.GetMethods(BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.Instance);
            for (int i = 0; i < methods.Length; i++)
            {
                if (methods[i].Name != name)
                    continue;
                ParameterInfo[] parameters = methods[i].GetParameters();
                if (parameters.Length != parameterTypes.Length)
                    continue;

                bool match = true;
                for (int p = 0; p < parameters.Length; p++)
                {
                    if (parameterTypes[p] == null ||
                        parameters[p].ParameterType != parameterTypes[p])
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                    return methods[i];
            }
            return null;
        }

        private static void InvokeNoArg(object instance, string methodName)
        {
            if (instance == null)
                return;
            MethodInfo method = instance.GetType().GetMethod(methodName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, Type.EmptyTypes, null);
            if (method != null)
                method.Invoke(instance, null);
        }

        private static bool TrySetProperty(object instance, string name, object value)
        {
            if (instance == null)
                return false;
            Type type = instance.GetType();
            while (type != null)
            {
                PropertyInfo property = type.GetProperty(name,
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (property != null)
                {
                    if (value != null && !property.PropertyType.IsInstanceOfType(value))
                        value = Convert.ChangeType(value, property.PropertyType);
                    property.SetValue(instance, value, null);
                    return true;
                }
                type = type.BaseType;
            }
            return false;
        }

        private static object CreateTextObject(string text)
        {
            Type textType = TypeByName("TaleWorlds.Localization.TextObject");
            if (textType == null)
                return null;

            ConstructorInfo[] constructors = textType.GetConstructors(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            for (int i = 0; i < constructors.Length; i++)
            {
                ParameterInfo[] parameters = constructors[i].GetParameters();
                if (parameters.Length == 2 && parameters[0].ParameterType == typeof(string))
                    return constructors[i].Invoke(new object[] { text, null });
                if (parameters.Length == 1 && parameters[0].ParameterType == typeof(string))
                    return constructors[i].Invoke(new object[] { text });
            }
            return null;
        }

        private static object GetItemTypeValue(object item)
        {
            // Bannerlord 1.3.15 stores Type as a public field and also exposes the
            // derived ItemType property. Read the real field first.
            object value = GetField(item, "Type");
            if (value != null)
                return value;

            value = GetProperty(item, "ItemType");
            if (value != null)
                return value;

            // Compatibility fallback for wrappers that expose Type as a property.
            return GetProperty(item, "Type");
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

        private static void LogOnce(string key, string message)
        {
            if (LoggedErrors.Add(key))
                ModLog.Error(message);
        }

        private static string FormatException(Exception ex)
        {
            TargetInvocationException tie = ex as TargetInvocationException;
            if (tie != null && tie.InnerException != null)
                ex = tie.InnerException;
            return ex.GetType().FullName + ": " + ex.Message +
                Environment.NewLine + ex.StackTrace;
        }

        private sealed class GrantPlan
        {
            public object BaseItem;
            public string ItemName;
            public List<string> TraitIds;
            public SetSlot? ExpectedSlot;
        }

        private sealed class PieceSignature
        {
            public SetDefinition Definition;
            public int PieceIndex;
        }

        private sealed class EquippedSetState
        {
            public readonly SetDefinition Definition;
            public readonly HashSet<int> PieceIndices = new HashSet<int>();
            public readonly Dictionary<int, string> ItemIdsByPiece =
                new Dictionary<int, string>();
            public readonly List<EquippedItemRef> EquippedItems =
                new List<EquippedItemRef>();
            public string CarrierItemId;
            public object CarrierItem;
            public string RelicItemId;
            public object RelicItem;

            public EquippedSetState(SetDefinition definition)
            {
                Definition = definition;
            }
        }

        private sealed class EquippedItemRef
        {
            public string ItemId;
            public object Item;
            public string ItemTypeName;
        }

        private enum BonusTargetKind
        {
            Armor,
            MeleeWeapon,
            RangedWeapon,
            Shield,
            AnyWeapon
        }

        private sealed class SetItemInstance
        {
            public object Item;
            public object SaveData;
            public PieceSignature Signature;
            public bool IsAdmin;
        }

        private sealed class VisualOutfitCandidate
        {
            public object Character;
            public Dictionary<SetSlot, object> Items;
            public int Coverage;
            public float Weight;
            public int Score;
            public string SourceKind;
            public string Signature;
        }

        private sealed class VisualCatalogCandidate
        {
            public object Item;
            public int Score;
            public float Weight;
            public string StringId;
        }

        private sealed class VisualProfile
        {
            public string[] CulturePhrases;
            public string[] PrimaryPhrases;
            public string[] SecondaryPhrases;
            public string[] NegativePhrases;
            public bool RequirePrimaryMatch;
        }
    }

    internal sealed class SetTooltipRow
    {
        public readonly string Definition;
        public readonly string Value;

        public SetTooltipRow(string definition, string value)
        {
            Definition = definition ?? String.Empty;
            Value = value ?? String.Empty;
        }
    }

    internal enum SetSlot
    {
        Head,
        Body,
        Cape,
        Hand,
        Leg
    }

    internal sealed class SetDefinition
    {
        public string CareerId;
        public string SetName;
        public string[] FactionTokens;
        public SetPieceDefinition[] Pieces;
        public SetTierDefinition[] Tiers;
    }

    internal sealed class SetPieceDefinition
    {
        public string ItemName;
        public SetSlot Slot;
        public TraitDefinition[] Effects;
    }

    internal sealed class SetTierDefinition
    {
        public int RequiredPieces;
        public string Name;
        public TraitDefinition[] Effects;
    }
}
