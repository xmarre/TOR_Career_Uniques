# Changelog

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
