## TOR Career Uniques v1.7.30

- Made encounter-hero creation transactional across all careers.
- Added automatic rollback when any newly created encounter hero fails career, equipment, validation, or placement setup.
- Restores the previous persistent mappings and clears recovery/death-guard registration before removing the failed temporary hero through Bannerlord's native lifecycle actions.
- Retains the general culture/role equipment fallback introduced in v1.7.29, including exact-slot validation and the 11-weight caster armour cap.
- Existing valid encounter heroes and recruited career characters are preserved.

Install by completely deleting the old `Modules/TORCareerUniques` folder, then extract the clean archive into the Bannerlord root directory.
