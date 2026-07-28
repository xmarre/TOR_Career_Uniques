# Changelog

## 1.7.29

- Fixed a save-loading crash caused by incomplete GreyLord encounter-hero initialization.
- Added the bounded culture/role catalogue resolver when matched CharacterObject outfits cannot satisfy every required slot and the caster armour-weight cap.
- Fixed `Cowl of Unremembered Faces` failing to resolve an exact-slot visual base item in that fallback path.
- Prevented partially initialized GreyLord heroes from reaching Bannerlord's load-finalization validation.
- Preserved the 11-weight caster cap, culture/role filtering, exact-slot checks, and native equipment validation.
- Rebuilt the repository from the canonical v1.7.28 source and removed historical validation reports, investigations, emergency binary-patch scripts, and transport artefacts.

## 1.7.28

- Recompiled the compatibility helper normally from source.
- Corrected the boolean Harmony prefix for Bannerlord's private native bandit classifier.
- Added exact target-signature validation and deterministic setup failures.
- Retained the finance, barter, race-equipment, garrison-avoidance, and patrol-radius compatibility fixes.

Earlier release history is preserved in Git tags and GitHub releases rather than as version-specific validation documents in the development tree.
