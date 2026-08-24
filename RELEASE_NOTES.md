## TOR Career Uniques v1.7.42

- Fixes one missing or TOR-filtered native item trait making the entire TOR Career Uniques trait registry report as unavailable.
- Restores unrelated encounter creation and admin set grants when another career has a broken native TOR trait dependency, including the reported Runelord / Desecrated Rune Vault and Warrior Priest failure paths.
- Keeps a genuinely missing native TOR trait local to the item or career that actually requires it instead of disabling all 22 careers.
- Does not fabricate, rename, or approximate TOR enchantments: configured native ids are restored before any item can consume them, and TOR's live registry is never modified.
- Serializes the short registration-validation window and restores temporary validation state on both normal and exception paths.
- Adds a targeted diagnostic directing users to verify `TOR_Core` ModuleData when current TOR/WiTM native traits are absent from the loaded registry.
- Retains all v1.7.41 magic-item inventory/icon lifetime and encounter-captivity fixes.

Install by completely deleting the old `Modules/TORCareerUniques` folder, then extract the clean archive into the Bannerlord root directory.
