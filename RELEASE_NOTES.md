## TOR Career Uniques v1.7.40

- Restores lore-based non-aggression for roaming hosts after the shared-clan migration, including direct and retained engage commands in both directions.
- Keeps the single shared encounter clan and reuses the existing once-per-session affinity cache without adding save-load scans, campaign-map polling, or recurring faction work.
- Adds separate 25%-300% MCM controls for roaming-host and guardian-location base strength.
- Keeps collection and veteran escalation multiplicative with the selected base strength; the default remains 100%.
- Applies changed host strength on respawn or roster rebuild and changed guardian strength whenever defenders materialize.
- Includes the v1.7.39 legacy-clan migration notification guard and all previous companion-set and Grey Lord/Cowl fixes.

Install by completely deleting the old `Modules/TORCareerUniques` folder, then extract the clean archive into the Bannerlord root directory.
