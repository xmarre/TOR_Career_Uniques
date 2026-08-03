## 1.7.41

- Prevented recovered career relics from being lost when a post-battle inventory, loot, or trade transaction commits an older roster snapshot.
- Added a bounded global ownership audit covering every living hero's battle and civilian equipment, all mobile-party inventories, settlement inventories, settlement stashes, and settlement-party inventories before any recovery action.
- Added **Repair missing recovered relics** to the existing TOR Career Uniques MCM page; it preserves existing owners, removes only the exact duplicate created by the faulty interim recovery build, and recreates a genuinely missing relic when its old runtime record no longer exists.
- Removed the obsolete `torcu.repair_orphaned_relic_rewards` console command and its orphan-recovery entry point.
- Fixed recycled TOR inventory rows retaining the magic-item background after being rebound to an ordinary item by restoring Bannerlord's current native row brush before TOR evaluates the active item.
- Fixed TOR inventory magic-item detection for items carrying a separately rolled loot modifier, including Warden's Spear of the Wild Hunt, so they retain the purple background while unequipped.
- Replaced TOR's unsafe weekly runtime-magic-item cleanup with a reference-safe pass that checks all live rosters and every living hero equipment slot before unregistering an item object.
- Repairs already-affected runtime magic items on an item-icon cache miss by re-registering the still-referenced item before Bannerlord resolves its thumbnail.
- Added no campaign-map polling or recurring global scans beyond TOR's existing weekly cleanup cadence.

## 1.7.40

- Restored lore-based non-aggression for roaming hosts after the shared-clan migration by rejecting protected direct and retained engage commands in both directions.
- Reused the existing once-per-session affinity cache and retained the single shared encounter clan without adding save-load scans, campaign-map polling, or recurring faction work.
- Added separate 25%-300% MCM controls for roaming-host and guardian-location base strength.
- Kept collection and veteran escalation multiplicative with the selected base strength; the default remains 100%.
- Applies changed host strength on respawn or roster rebuild and changed guardian strength whenever defenders materialize.
- Validated the relation and strength behavior in Bannerlord v1.3.15.

## 1.7.39

- Fixed Bannerlord's "clan was destroyed" notifications repeating on every load after the v1.7.38 legacy-clan migration.
- Added a TORCU-owned migration marker that is serialized with the campaign and prevents the native clan-destruction action from running again after a successful migration.
- Existing v1.7.38 saves whose legacy clans are already empty are recognized as migrated without replaying destruction notifications.
- Genuine unmigrated saves still run the complete shared-clan migration once, then persist completion.
- Retains the shared-clan load-time improvement with no campaign-map polling or recurring clan scans.

## 1.7.38

- Replaced the 22 serialized per-career encounter clans with one shared leader-backed encounter clan.
- Existing saves migrate every encounter hero and active encounter party to the shared clan, then remove only legacy clans verified to have no remaining heroes, parties, or settlements.
- The first load of an old save still deserializes its legacy faction graph; save once after migration so subsequent loads contain only one TORCU encounter clan.
- Preserved conversation-safe leadership, party-specific cultural affinity, forced hostility, recruitment, successors, and the Grey Lord/Cowl load repair.
- Added no campaign-map polling, per-frame scans, or recurring clan migration work.

## 1.7.37

- Added a temporary pre-session guard for TORCU encounter clans whose intentionally absent naval party template can be queried by native/TOR load paths before the existing bandit classifier guard runs.
- Registers TORCU relic and set traits before TOR restores saved crafted set items when the trait registry is available.
- Added unconditional one-shot timing for TOR artisan save restoration, crafted-item reconstruction, native bandit-cache rebuilding, the pre-session boundary, and TORCU's own session callback.
- Removes the temporary naval getter patch after loading so it adds no campaign-map Harmony overhead.
- Retains the v1.7.29 Grey Lord/Cowl repair, v1.7.35 visual fast path, and v1.7.36 deferred encounter maintenance.

# Changelog

## 1.7.36

- Removed the all-22-encounter maintenance pass from Bannerlord's synchronous save-load callback.
- Persistent encounter heroes are still fully reconciled and validated before load finalization, preserving the v1.7.29 Grey Lord/Cowl crash fix.
- Roaming-party and guardian-site maintenance now uses the existing one-definition-at-a-time initializer only after the campaign map is active.
- Added one-shot load and slow-initialization timing diagnostics to identify any remaining save-specific bottleneck without campaign-map polling.

## 1.7.35

- Replaced the unconditional full visual-catalogue migration on every save load with a structural fast audit of the generated set items already stored in the save.
- Saved visual bases are now checked through Bannerlord's indexed object lookup; compatible relic bases and exact-slot armour bases bypass all global catalogue resolution, while missing, wrong-slot, or otherwise invalid legacy visuals still enter the bounded v1.7.29+ fallback.
- Schema-current persistent encounter heroes now retain their verified TOR career record instead of rebuilding template capabilities, career tiers, choices, and abilities on every load.
- Deduplicated persistent encounter-hero auditing within the same session launch so encounter initialization cannot immediately repeat an audit that already succeeded during reconciliation.
- Retains the Grey Lord/Cowl repair, cross-culture role fallback, exact-slot validation, caster weight cap, and transactional incomplete-hero recovery.
- Adds no campaign-map polling, per-frame catalogue scans, or equipment fingerprint checks.

## 1.7.34

- Added an authoritative main-party roster fallback so every inventory-selectable companion is included even when Bannerlord or another mod has not synchronized that hero into the clan collections.
- Added a live set-owner reconciliation path for stale inventory equipment snapshots; hovering an equipped set piece now rebuilds that companion's state and applies the corrected bonuses immediately.
- Retains event-driven operation with no campaign-map polling, per-frame hero scans, or equipment fingerprint checks.

## 1.7.33

- Fixed ordinary companions being omitted from career-set snapshots because Bannerlord exposes them through `Clan.Companions` separately from `Clan.Heroes`.
- Set pieces equipped by companions now resolve the correct owner and report the actual equipped count, including `[ADMIN COPY]` test sets.
- Uses one deduplicated player-clan enumeration for bonus snapshots, equipment-owner lookup, and set-item normalization so lords, family heroes, and companions follow the same path.
- Retains the existing event-driven refresh model; no campaign-map scans, continuous clan enumeration, or equipment fingerprint polling were added.

## 1.7.32

- Added full individual-piece and 2/5–5/5 set-bonus activation for ordinary player-clan companions and other living player-clan heroes.
- Evaluates every hero independently so pieces equipped by different characters can never combine into a false set tier.
- Added owner-aware set tooltips for set pieces equipped by companions while retaining the controlled hero as the fallback for unequipped inventory items.
- Excludes persistent encounter-hero equipment whose intrinsic and tier traits were already baked into its generated wargear, preventing duplicate bonuses after recruitment.
- Keeps mastery, recognition, and encounter-hero recruitment checks tied to the currently controlled hero.
- Uses only campaign-session, inventory-entry, equipment-mutation, and companion-roster events; no campaign-map tick scans or equipment fingerprint polling were added.

## 1.7.31

- Fixed encounter careers whose declared TOR culture exposes no role-compatible armour source in the live object catalogue.
- Added a bounded adjacent-culture fallback inferred from career-specific archetype phrases and relic-compatible equipment instead of hard-coded item ids.
- Keeps the declared culture authoritative whenever it provides at least one usable role-compatible character.
- Fixed the Grey Lord equipment path when Eonir culture metadata is absent but compatible elven caster equipment is available.
- Preserved exact-slot validation, negative-role filtering, coherent outfit selection, and the 11-weight caster armour cap.

## 1.7.30

- Made encounter-hero creation transactional for every career.
- Added a finalizer-backed rollback around the shared `GetOrCreateEncounterHero` path so any exception during career, equipment, validation, or placement setup cannot leave a newly created hero registered in persistent state.
- Restores the pre-call encounter and successor mappings, clears pending recovery state, and unregisters failed heroes from the forced-death guard.
- Removes failed temporary heroes through Bannerlord's native removal action, with native hero disabling as a verified fallback.
- Preserved existing validated heroes and all career-specific equipment, culture, role, exact-slot, and caster-weight rules.

## 1.7.29

- Added the first bounded culture/role catalogue fallback when matched TOR archetype outfits could not produce a complete, slot-valid loadout within the applicable armour-weight limit.
- Applied the shared fallback to every encounter career rather than adding a Grey Lord-only item override.
- Added exact-slot visual-base validation for the Grey Lord's `Cowl of Unremembered Faces` failure path.
- Preserved the 11-weight caster cap, culture/role filtering, exact-slot checks, and native equipment validation.
- Rebuilt the repository from the canonical v1.7.28 source and removed historical validation reports, investigations, emergency binary-patch scripts, and transport artefacts.

## 1.7.28

- Recompiled the compatibility helper normally from source.
- Corrected the boolean Harmony prefix for Bannerlord's private native bandit classifier.
- Added exact target-signature validation and deterministic setup failures.
- Retained the finance, barter, race-equipment, garrison-avoidance, and patrol-radius compatibility fixes.

Earlier release history is preserved in Git tags and GitHub releases rather than as version-specific validation documents in the development tree.
