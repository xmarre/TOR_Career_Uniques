## TOR Career Uniques v1.7.31

- Fixed encounter careers whose declared TOR culture has no usable role-compatible armour source in the live catalogue.
- Added a bounded adjacent-culture inference step based on career-specific archetype terminology and relic-compatible equipment.
- Fixed the Grey Lord loadout when Eonir culture metadata is absent while compatible elven caster equipment remains available.
- The declared culture remains authoritative whenever it contains a viable role-compatible source.
- Exact-slot validation, coherent outfit selection, negative-role filtering, and the 11-weight caster armour cap remain enforced.
- Retains the transactional encounter-hero rollback introduced in v1.7.30.

Install by completely deleting the old `Modules/TORCareerUniques` folder, then extract the clean archive into the Bannerlord root directory.
