## TOR Career Uniques v1.7.36

- Removed synchronous maintenance of all 22 encounter definitions from the save-loading critical path.
- Persistent encounter-hero repair, equipment validation, and the Grey Lord/Cowl safeguards still complete before Bannerlord finalizes the loaded save.
- Encounter party and guardian-site maintenance is now processed one definition at a time only after the campaign map becomes active.
- Added one-shot timing diagnostics for TORCU's synchronous load stage and any unusually slow deferred encounter definition.
- No continuous campaign-map scans or equipment polling were added.

Install by completely deleting the old `Modules/TORCareerUniques` folder, then extract the clean archive into the Bannerlord root directory.
