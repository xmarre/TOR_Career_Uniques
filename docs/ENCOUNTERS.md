# Career-set encounters

## Acquisition flow

- Guardian sites remain on the map permanently. Defeating the defenders only unlocks the site's contextual search option; no set-piece roll or consolation loot occurs until that option is selected.
- Roaming hosts leave a searchable aftermath prompt after victory. The player must deliberately search it.
- Declining a search forfeits that encounter's reward attempt. A failed UI call never grants anything and leaves the aftermath pending.
- A successful delving roll grants one uniformly selected undiscovered piece until the encounter career reaches 5/5.
- After 5/5, successful delving rolls remain active and grant duplicate pieces as new modifier rolls.
- Encounter heroes' equipped set pieces can also enter ordinary post-battle loot. Hero-only copies are normalized to canonical player items before the loot screen while preserving their modifier.
- Progress records each logical piece once. Duplicate physical copies never increase the 5/5 discovery count.
- Delving set pieces and themed consolation loot receive native Bannerlord equipment modifiers. Existing relic claims count as the relic piece on older saves.
- Failed rolls and searches after full-set completion produce one curated themed item normally, with a smaller chance of two items. Duplicate names and repeated equipment categories are removed when alternatives exist.
- `[ADMIN COPY]` items are outside this flow and never change encounter or recovery state.


## Persistent encounter leaders

### Guardian sites

- Grail Damsel — Ysabeau the Blighted
- Minor Vampire — Vicomte Aleron the Blooded
- Witch Hunter — Inquisitor Matthias Krieger
- Necromancer — Mordechai the Restless
- Necrarch — Azrad the Pallid
- Imperial Magister — Magister Erasmus Volker
- Spellsinger — Lethariel of the Withered Bough
- Grey Lord — Magister Severin Veyl
- Ironbreaker — Durgan Ironmantle
- Runelord — Baragor Embermark
- Orc Shaman — Nazgob Moon-Eater

### Roaming hosts

- Grail Knight — Sir Malrec the Unhallowed
- Warrior Priest — Lector Konrad Voss
- Blood Knight — Kastellan Varos the Red
- Mercenary — Captain Luccio Ferrante
- Black Grail Knight — Sir Severin, Keeper of the Black Grail
- Warrior Priest of Ulric — Hagen Wolfsbane
- Waywatcher — Aelir the Thorn-Eyed
- Warden — Caerwyn the Hunted
- Knight of the Old World — Sir Eckhardt of the Black Road
- Slayer — Kragni Oathscar
- Orc Boss — Morglug Ironjaw

Every original leader is persistent across save/load and encounter respawns. The party is disposable; the hero is not. Active leaders cannot enter generic captivity. After 5/5 set mastery and recruitment, the exact original becomes a player-clan companion and the encounter's separately saved, non-recruitable successor leads every later cycle.

## Set-mastery capstone

The default rule checks the currently controlled `Hero.MainHero` battle equipment at the discrete player-victory event. Discovery or inventory ownership alone is insufficient: the five distinct matching set signatures must all be equipped when the original leader is defeated. Mastery is saved once. The post-victory parley may be postponed and reopened from the selected encounter view while the complete set remains equipped.

Recruitment uses Bannerlord's native companion and party actions. The original reference is retained permanently and excluded from encounter reconciliation, equipment repair, death/capture guards, recovery and respawn attachment. All 22 successor references serialize independently and remain subject to the existing encounter audit and survival systems.

## Guardian-site sub-locations

- Grail Damsel — The Blighted Grail Chapel
- Minor Vampire — The Sepulchre of the Red Duke
- Witch Hunter — The Ashen Tribunal
- Necromancer — The Barrow of the Restless Host
- Necrarch — The Necrarch Ossuary
- Imperial Magister — The Ruined Collegiate Observatory
- Spellsinger — The Defiled Waystone
- Grey Lord — The Vault Beneath the Grey College
- Ironbreaker — The Goblin-Delved Underhold
- Runelord — The Desecrated Rune Vault
- Orc Shaman — The Moon-Idol Hollow

Each named site is a hidden sub-location assigned to a distinct existing ToR landmark with valid campaign-map navigation. Enter the assigned native location and choose **Investigate [site name]**. No synthetic settlement or cloned map entity is created. The assigned landmark persists in save data and can be tracked through the MCM overview.

Guardian leaders are stored in Bannerlord's disabled hero state with no settlement presence or mobile party. A defender party is materialized only inside the player's **Assault the defenders** action and is passed immediately into that player encounter. It is destroyed when the encounter closes whether the player wins, loses, or encounter startup fails. Campaign AI therefore has no guardian party or guardian hero to target, and ordinary wars involving the native landmark cannot reach the hidden encounter.

## Roaming hosts

- Grail Knight — The Black Grail Procession
- Warrior Priest — The Purple Hand Purge
- Blood Knight — The Crimson Errantry
- Mercenary — The Border Princes' Black Company
- Black Grail Knight — The Black Grail Reliquary Guard
- Warrior Priest of Ulric — The White Wolf Hunt
- Waywatcher — The Beast-Hunters of Athel Loren
- Warden — The Wild Hunt's Quarry
- Knight of the Old World — The Black Road Brotherhood
- Slayer — The Troll King's Hunters
- Orc Boss — Grubnash's Rival Waaagh!

Hosts are bandit-classified mobile parties created near a themed home region. Their baseline is 100–125 troops, scaled by collection and capped veteran progression. After spawning, normal Bannerlord campaign AI controls pursuit, avoidance, travel and target selection. The mod does not impose patrol orders or teleport them. A party-specific eligibility prefix only rejects protected-faction targets and targets above 1.60 times the host's estimated strength. Every destruction schedules the configured respawn cooldown, including AI-vs-AI defeats. Missing previously spawned hosts also enter cooldown instead of reappearing immediately.

### v1.7.6 encounter identity audit

All 22 definitions now author three separate identities: the adversaries named by the encounter story, the troops that actually fight beside the persistent career hero, and the technical independent clan used for Bannerlord campaign-AI/war semantics. A career-led encounter can no longer inherit its adversary tokens as its troop roster or owner faction.

The following 13 encounters were corrected because the named hero's established career/dialogue conflicted with the old pre-hero enemy roster:

- Warrior Priest — **The Purple Hand Purge**: Konrad Voss leads Sigmarite/Imperial followers hunting Purple Hand cells.
- Witch Hunter — **The Ashen Tribunal**: Matthias Krieger holds a burned-out coven as a Witch Hunter evidence/interrogation cell with Imperial hunters and guards.
- Black Grail Knight — **The Black Grail Reliquary Guard**: Sir Severin leads Black Grail/Mousillon/undead retainers rather than ordinary Grail defenders.
- Warrior Priest of Ulric — **The White Wolf Hunt**: Hagen Wolfsbane leads Ulrican/Middenland followers hunting Beastmen and raiders.
- Imperial Magister — **The Ruined Collegiate Observatory**: Erasmus Volker is defended by an Imperial/Collegiate retinue, not cultists and rogue sorcerers.
- Waywatcher — **The Beast-Hunters of Athel Loren**: Aelir leads strict Asrai/Wood Elf hunters; Beastmen/Ungors remain the quarry, never his troops or owner identity.
- Spellsinger — **The Defiled Waystone**: Lethariel is protected by Asrai and forest guardians containing the wounded waystone.
- Warden — **The Wild Hunt's Quarry**: Caerwyn's condemned spear-line is strict Asrai. He held a threatened ward-line instead of answering Orion's horn, and the survivors were marked as quarry.
- Grey Lord — **The Vault Beneath the Grey College**: Severin Veyl's Imperial shadow agents defend the breached vault against cultists, sorcerers and thieves.
- Knight of the Old World — **The Black Road Brotherhood**: Sir Eckhardt leads an independent veteran knightly brotherhood rather than Chaos/Norscan reavers.
- Ironbreaker — **The Goblin-Delved Underhold**: Durgan and a surviving Dawi garrison hold the inner vault after the Greenskin breach.
- Slayer — **The Troll King's Hunters**: Kragni leads a Dawi Slayer oath-host hunting the Troll King's brood rather than commanding trolls and Greenskins.
- Runelord — **The Desecrated Rune Vault**: Baragor and Dawi rune-guards hold the surviving inner vault after its outer seals were desecrated.

The other nine were audited and retained because hero, troops and story are already aligned: **The Blighted Grail Chapel**, **The Black Grail Procession**, **The Sepulchre of the Red Duke**, **The Crimson Errantry**, **The Border Princes' Black Company**, **The Barrow of the Restless Host**, **The Necrarch Ossuary**, **Grubnash's Rival Waaagh!**, and **The Moon-Idol Hollow**.

All 22 use strict authored combatant themes. Enemy-led encounters retain their old regional tie-breaker; career-led follower themes are authoritative and do not absorb troops merely because the encounter spawned near another culture. Technical owners remain native independent bandit-classified shells so hosts remain freely attackable and under native bandit campaign AI without joining a kingdom. Career-led owner selection rejects lore-incompatible monster factions; troop composition is independent of that technical shell.

Save schema 4 corrects only already-active roaming hosts whose career identity changed, preserving the persistent leader, collection/veteran progression, cooldowns, spawn serials and recruitment/succession state. Guardian defenders are materialized only on assault and therefore use the corrected definition on their next materialization. Fresh and respawned hosts use the corrected definitions directly.

## Encounter escalation

Both encounter types use the same persistent career progression:

| Discovered | Size multiplier | Elite target |
|---:|---:|---:|
| 0/5 | x1.00 | 20% |
| 1/5 | x1.10 | 22% |
| 2/5 | x1.20 | 27% |
| 3/5 | x1.35 | 31% |
| 4/5 | x1.50 | 36% |
| 5/5 | x1.67 | 40% |

Roaming hosts use a 100–125 base range; guardian sites use 110–135. After 5/5, each successful player clear advances one of five veteran tiers, adding +5% size and +1 percentage point elite share per tier. Collection and veteran multipliers are applied at spawn, while older active roaming hosts receive one bounded one-time reinforcement when the schema first loads.

Each of the 22 careers has five unique lore messages for its collection milestones. A collection message fires only when a new logical piece advances progress; duplicate physical items never replay it. Each encounter also has a distinct veteran-clear response.

## Roaming-host affinity

Affinity is enforced at party attack eligibility, in both directions. It does not change war state or make the encounter clan a kingdom member.

| Host career | Protected identifiers |
|---|---|
| Grail Knight | Bretonnia / Bretonnian |
| Warrior Priest | Empire / Imperial / Reikland / Middenland |
| Blood Knight | Vampire / Sylvania / Mousillon |
| Mercenary | Border Princes / Tilea / Estalia |
| Black Grail Knight | Mousillon |
| Warrior Priest of Ulric | Empire / Imperial / Middenland |
| Waywatcher | Wood Elf / Asrai / Athel Loren |
| Warden | Eonir / Wood Elf / Asrai / Athel Loren |
| Knight of the Old World | Empire / Imperial / Reikland |
| Slayer | Dwarf / Dawi / Karaz Ankor |
| Orc Boss | Greenskin / Orc / Badlands |

The cache is built once per campaign session from runtime kingdom and independent-clan descriptors. Ordinary AI calls do only a party-ID prefix check and bounded hash lookups. The player main party is excluded from protection and remains able to attack every host without kingdom diplomatic consequences.

## Encounter overview map tracking

The MCM overview has an explicit close path. **Track** registers a normal native-settlement marker only. For an active roaming encounter it selects the settlement closest to the host's current position; a missing host falls back to its saved home anchor, and guardian locations mark their assigned settlement. It never registers the moving party or starts a campaign-camera animation, so it is safe to use while the player is inside a town, castle or village.
