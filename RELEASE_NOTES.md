## TOR Career Uniques v1.7.41

- Fixes TOR magic items with separately rolled loot modifiers, including Warden's Spear of the Wild Hunt, losing the purple magic-item background while unequipped.
- Resolves the inventory row from the actual registered runtime item and applies TOR's magic brush directly, while preserving native unusable-item styling.
- Fixes ordinary inventory rows retaining a purple background when Gauntlet recycles a row previously used for a magic item.
- Prevents TOR's weekly magical-loot cleanup from unregistering runtime items that remain referenced by inventories, equipment, stashes, settlement storage, or pending loot.
- Repairs icons already affected by unsafe cleanup when Bannerlord next requests the referenced runtime item's thumbnail.
- Prevents persistent encounter heroes from becoming stuck as prisoners and releases encounter heroes already captive in existing saves so normal encounter recovery can continue.
- Retains the v1.7.40 lore-affinity and encounter-strength fixes and all earlier companion-set, load-time, and Grey Lord/Cowl fixes.

Install by completely deleting the old `Modules/TORCareerUniques` folder, then extract the clean archive into the Bannerlord root directory.
