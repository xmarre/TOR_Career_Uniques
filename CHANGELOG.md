# Changelog

## 1.7.30

- Made encounter-hero creation transactional for every career.
- Added a finalizer-backed rollback around the shared `GetOrCreateEncounterHero` path so any exception during career, equipment, validation, or placement setup cannot leave a newly created hero registered in persistent state.
- Restores the pre-call encounter and successor mappings, clears pending recovery state, and unregisters failed heroes from the forced-death guard.
- Removes failed temporary heroes through Bannerlord's native removal action, with native hero disabling as a verified fallback.
- Preserved existing validated heroes and all career-specific equipment, culture, role, exact-slot, and caster-weight rules.

## 1.7.29

- Fixed a general save-loading failure caused when an encounter career's matched TOR archetype outfits could not produce a complete, slot-valid loadout within the applicable armour-weight limit.
- Added the bounded culture/role catalogue resolver for every career using the shared outfit-resolution path.
- Fixed the Grey Lord's `Cowl of Unremembered Faces` as the confirmed data path that exposed the general resolver defect.
- Prevented that resolver failure from leaving an incomplete encounter hero for Bannerlord's load-finalization validation.
- Preserved the 11-weight caster cap, culture/role filtering, exact-slot checks, and native equipment validation.
- Rebuilt the repository from the canonical v1.7.28 source and removed historical validation reports, investigations, emergency binary-patch scripts, and transport artefacts.

## 1.7.28

- Recompiled the compatibility helper normally from source.
- Corrected the boolean Harmony prefix for Bannerlord's private native bandit classifier.
- Added exact target-signature validation and deterministic setup failures.
- Retained the finance, barter, race-equipment, garrison-avoidance, and patrol-radius compatibility fixes.

Earlier release history is preserved in Git tags and GitHub releases rather than as version-specific validation documents in the development tree.
