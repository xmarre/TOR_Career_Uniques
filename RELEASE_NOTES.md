## TOR Career Uniques v1.7.35

- Greatly reduced save-load work introduced by the v1.7.29 and later encounter-visual repairs.
- Existing set items now validate their saved visual base through Bannerlord's indexed object lookup instead of rebuilding complete career outfits from the global character and item catalogues on every load.
- The expensive culture/role resolver still runs when a legacy item is missing, uses the wrong slot, has an incompatible relic kind, or when an incomplete encounter hero genuinely needs repair.
- Already validated persistent encounter heroes no longer rebuild their TOR career and template capabilities twice during one session launch.
- The confirmed Grey Lord/Cowl fix, cross-culture fallback, exact-slot validation, caster weight cap, and transactional recovery remain intact.
- No campaign-map polling or continuous scans were added.

Install by completely deleting the old `Modules/TORCareerUniques` folder, then extract the clean archive into the Bannerlord root directory.
