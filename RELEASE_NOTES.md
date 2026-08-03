## TOR Career Uniques v1.7.41

- Makes career-relic rewards transaction-safe after battles and verifies the exact granted item after inventory, loot, and trade screens close.
- Adds **Repair missing recovered relics** to the existing TOR Career Uniques MCM page. The repair scans every living hero, both equipment sets, all mobile-party inventories, settlement inventories, stashes, and settlement-party inventories before changing anything.
- Preserves relics on inactive shared characters and other existing owners, removes only the exact duplicate created by the faulty interim recovery build, and recreates a genuinely missing relic when its saved runtime record is gone.
- Removes the obsolete orphan-recovery console command.
- Fixes ordinary inventory rows inheriting TOR's magic-item background when Gauntlet recycles a row previously used for a magic item.
- Prevents TOR's weekly cleanup from unregistering a runtime magic item that is still equipped or stored in a live player-owned location.
- Repairs icons already broken by an earlier unsafe cleanup when Bannerlord next requests the item's thumbnail.
- Retains the v1.7.40 lore-affinity and encounter-strength fixes and all earlier companion-set, load-time, and Grey Lord/Cowl fixes.

Install by completely deleting the old `Modules/TORCareerUniques` folder, then extract the clean archive into the Bannerlord root directory.
