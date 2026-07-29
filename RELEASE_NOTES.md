## TOR Career Uniques v1.7.33

- Fixed career sets equipped by ordinary companions incorrectly showing `0/5` and leaving every set bonus locked.
- Companion equipment is now discovered through Bannerlord's dedicated companion collection as well as the normal clan-hero collections.
- Both real career-set items and `[ADMIN COPY]` test sets now resolve the equipped companion as their owner and activate the correct piece and tier bonuses.
- Player-clan hero enumeration is deduplicated and remains limited to existing session, inventory, equipment-change, and roster-change refresh events.
- No campaign-map polling, continuous scans, or equipment fingerprint checks were added.

Install by completely deleting the old `Modules/TORCareerUniques` folder, then extract the clean archive into the Bannerlord root directory.
