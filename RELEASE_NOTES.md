## TOR Career Uniques v1.7.38

- Collapses the 22 legacy serialized encounter factions into one shared leader-backed encounter clan.
- Preserves independent encounter heroes, conversation safety, per-career party affinity, hostility, recruitment, and successors.
- Existing saves are migrated after their first successful v1.7.38 load. Save the campaign once, then reload to measure the reduced native object-graph load time.
- Legacy clans are destroyed only after all heroes and active parties have been moved and the clan is verified empty.
- Retains the Grey Lord/Cowl repair, companion-set support, early TOR trait registration, and deferred encounter maintenance.

Install by completely deleting the old `Modules/TORCareerUniques` folder, then extract the clean archive into the Bannerlord root directory.
