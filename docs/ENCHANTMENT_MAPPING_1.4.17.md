# TOR Career Uniques v1.4.17 — Native Enchantment Replacement Audit

Exactly one bespoke intrinsic property was replaced on each of the 110 set items. Relic property #1 and armour property #1 remain the stable signature properties used by set recognition. All 2/5, 3/5, 4/5 and 5/5 tier bonuses remain unchanged.

Native IDs below are real TOR `ItemTraitStringId` values. The runtime refuses readiness if one of these native traits is absent instead of synthesizing a look-alike custom trait.

## Grail Damsel

Lady-aligned Bretonnian blessings plus high-tier Azyr/Ghyran magic available to Bretonnia; prioritizes survival, spell reach and battlefield support.

| Item | Removed bespoke property | Native replacement | TOR trait ID |
|---|---|---|---|
| Chalice-Stave of the Lady (relic) | Blessing of the Lady — 15% resistance to holy damage. | Foresight of Azyr | `emp_enchant_azyr_foresight` |
| Circlet of the Lake (Head) | Silver Font — +15 maximum Winds of Magic. | Ward of the Lady | `bret_blessing_lady_ward` |
| Vestments of the Grail Spring (Body) | Life-Giving Waters — +12 maximum health. | Mists of the Sacred Lake | `bret_blessing_mists_sacred_lake` |
| Mantle of the Fay Enchantress (Cape) | Fay Veil — 5% resistance to magical damage. | Bloom of Ghyran | `emp_enchant_ghyran_bloom` |
| Slippers of the Sacred Shore (Leg) | Unbroken Current — +0.10 Winds of Magic recharge. | Divination of Azyr | `emp_enchant_azyr_divination` |

## Grail Knight

The Lady/Grail blessing line, culminating in Legacy of the Grail on the lance; emphasizes chivalric durability, riding and holy melee power.

| Item | Removed bespoke property | Native replacement | TOR trait ID |
|---|---|---|---|
| Blessed Lance of Couronne (relic) | Virtue of Heroism — +20 maximum health. | Legacy of the Grail | `bret_blessing_grail_legacy` |
| Helm of the Questing Vow (Head) | Virtue of Discipline — 6% resistance to physical damage. | Ward of the Lady | `bret_blessing_lady_ward` |
| Plate of the Sacred Oath (Body) | The Lady's Ward — 8% resistance to holy damage. | Wisdom and Virtue | `bret_blessing_wisdom_virtue` |
| Gauntlets of the Dragon's Bane (Hand) | Perfect Reins — +10 Riding. | Touch of the Eerie | `bret_blessing_eerie_touch` |
| Sabatons of the Unbroken Charge (Leg) | Thunderous Impact — +15% shield damage. | Guidance of the Fey | `bret_blessing_fey_guidance` |

## Minor Vampire

Dhar defensive enchantments on armour and Drinker of Blood on the nightblade; emphasizes predatory sustain and supernatural survivability.

| Item | Removed bespoke property | Native replacement | TOR trait ID |
|---|---|---|---|
| Von Carstein Nightblade (relic) | Blood-Strengthened Flesh — +25 maximum health. | Drinker of Blood | `vc_enchant_drinker_blood` |
| Masque of the Pale Court (Head) | Deathly Pallor — 5% resistance to physical damage. | Nightshroud | `vc_enchant_nightshroud` |
| Velvet of the Blood-Kin (Body) | Sated Hunger — +10% healing rate. | Ethereal Whispers | `vc_enchant_ethereal_whispers` |
| Cloak of No Moon (Cape) | Shadowed Presence — 6% resistance to magical damage. | Nightshroud | `vc_enchant_nightshroud` |
| Talons of the Von Carsteins (Hand) | Midnight Fencing — +6% swing speed. | Ethereal Whispers | `vc_enchant_ethereal_whispers` |

## Warrior Priest of Sigmar

Sigmar blessings dominate the set, with Hysh protection as the fourth armour enchantment; reinforces anti-undead/daemon holy combat.

| Item | Removed bespoke property | Native replacement | TOR trait ID |
|---|---|---|---|
| Warhammer of the Twin-Tailed Comet (relic) | Armour of Contempt — 10% physical damage resistance. | Exorcism of Sigmar | `emp_blessing_sigmar_exorcism` |
| Mitre of the War Altar (Head) | Unshaken Faith — 6% resistance to magical damage. | Soulfire of Sigmar | `emp_blessing_sigmar_soulfire` |
| Cuirass of Sigmar's Anvil (Body) | Tempered Conviction — 6% resistance to physical damage. | Light of Sigmar | `emp_blessing_sigmar_light` |
| Gauntlets of Righteous Wrath (Hand) | Smite the Unclean — +18% shield damage. | Beacon of Sigmar | `emp_blessing_sigmar_beacon` |
| Greaves of the Temple Road (Leg) | Marching Fervour — +0.10 career-resource generation. | Sanctuary of Hysh | `emp_enchant_hysh_sanctuary` |

## Blood Knight

Drinker of Blood plus repeated martial Dhar defenses; avoids caster-focused armour bonuses that would dilute the Blood Knight duelist role.

| Item | Removed bespoke property | Native replacement | TOR trait ID |
|---|---|---|---|
| Blood Dragon's Crimson Blade (relic) | Crimson Rend — +12% armor penetration. | Drinker of Blood | `vc_enchant_drinker_blood` |
| Dragon-Visored Helm (Head) | Blood Dragon Pride — 5% resistance to physical damage. | Nightshroud | `vc_enchant_nightshroud` |
| Cuirass of the Red Keep (Body) | Night-Steel Plates — 5% resistance to magical damage. | Ethereal Whispers | `vc_enchant_ethereal_whispers` |
| Gauntlets of the Endless Duel (Hand) | Walach's Precision — +7% armor penetration. | Nightshroud | `vc_enchant_nightshroud` |
| Spurs of the Crimson Errantry (Leg) | Never-Broken Pursuit — +5% movement speed. | Ethereal Whispers | `vc_enchant_ethereal_whispers` |

## Mercenary

Practical Empire enchantments chosen for survivability, armour, campaign speed and a powerful Chamon melee capstone.

| Item | Removed bespoke property | Native replacement | TOR trait ID |
|---|---|---|---|
| Paymaster's Blade of the Border Princes (relic) | Old Campaigner — +15 maximum health. | Crucible of Chamon | `emp_enchant_chamon_crucible` |
| Captain's Sallet of Seven Sieges (Head) | Useful Dents — 4% resistance to physical damage. | Wildform of Ghur | `emp_enchant_ghur_wildform` |
| Reinforced Coat of the Last Contract (Body) | Patched, Never Pretty — +12 maximum health. | Azure Mirror of Azyr | `emp_enchant_azyr_azure_mirror` |
| Paymaster's Gloves (Hand) | Practical Drill — +8% reload speed. | Feathers to Lead | `emp_enchant_chamon_feathers_lead` |
| Boots of the Long March (Leg) | Forage on the Move — +8 Scouting. | Divination of Azyr | `emp_enchant_azyr_divination` |

## Witch Hunter

Sigmar blessings and Hysh protection; focuses on exorcism, anti-corruption defense and sanctioned holy warfare.

| Item | Removed bespoke property | Native replacement | TOR trait ID |
|---|---|---|---|
| Silvered Blade of the Templars (relic) | Relentless Pursuit — +8% movement speed. | Exorcism of Sigmar | `emp_blessing_sigmar_exorcism` |
| Wide-Brimmed Hat of the Black Chamber (Head) | Stitched Hexwards — 7% resistance to magical damage. | Soulfire of Sigmar | `emp_blessing_sigmar_soulfire` |
| Coat of Silvered Chains (Body) | Long Interrogations — +12 maximum health. | Light of Sigmar | `emp_blessing_sigmar_light` |
| Mantle of the Unblinking Eye (Cape) | Relentless Pursuer — +5% movement speed. | Beacon of Sigmar | `emp_blessing_sigmar_beacon` |
| Executioner's Gloves (Hand) | Prepared Bolt — +10% reload speed. | Sanctuary of Hysh | `emp_enchant_hysh_sanctuary` |

## Necromancer

Necromancy/Dhar effects that expand magic, spell radius, summons and dangerous power-at-a-price caster scaling.

| Item | Removed bespoke property | Native replacement | TOR trait ID |
|---|---|---|---|
| Staff of Damnation (relic) | Black Sorcery — +15% magical damage. | Call from Beyond | `vc_enchant_call_beyond` |
| Crown of Nine Skulls (Head) | Grave-Wisdom — +10 Spellcraft. | Legacy of Arkhan | `vc_enchant_legacy_arkhan` |
| Grave-Robes of the First Barrow (Body) | Borrowed Grave-Flesh — +12 maximum health. | Unhallowed Pact | `vc_enchant_unhallowed_pact` |
| Shroud of the Unquiet Dead (Cape) | Dhar-Woven Shroud — 5% resistance to magical damage. | Secrets of W'soran | `vc_enchant_secrets_wsoran` |
| Ossuary Grasp (Hand) | Grave Command — +0.10 career-resource generation. | Caress of the Void | `vc_enchant_caress_void` |

## Black Grail Knight

The Crimson Flood on the lance plus martial Dhar defenses; emphasizes cursed melee pressure and undead knight resilience.

| Item | Removed bespoke property | Native replacement | TOR trait ID |
|---|---|---|---|
| Lance of the Black Grail (relic) | Black Lance — +15% armor penetration. | The Crimson Flood | `vc_enchant_crimson_flood` |
| Helm of the Hollow Grail (Head) | Hollow Within — 8% resistance to holy damage. | Nightshroud | `vc_enchant_nightshroud` |
| Blackened Plate of the False Vow (Body) | Blackened Plate — 6% resistance to physical damage. | Ethereal Whispers | `vc_enchant_ethereal_whispers` |
| Tattered Mantle of the Red Duke (Cape) | Dread Chivalry — +6% magical damage. | Nightshroud | `vc_enchant_nightshroud` |
| Greaves of the Drowned Chapel (Leg) | Drowned-Chapel Trample — +18% shield damage. | Ethereal Whispers | `vc_enchant_ethereal_whispers` |

## Necrarch

High-end Dhar/Necromancy effects focused on Winds, spell radius and volatile magical output.

| Item | Removed bespoke property | Native replacement | TOR trait ID |
|---|---|---|---|
| Necrarch Bone Staff (relic) | Deathly Reach — +25% spell radius. | The Crimson Flood | `vc_enchant_crimson_flood` |
| Cranial Diadem of Ushoran's Exile (Head) | Expanded Cranium — +20 maximum Winds of Magic. | Secrets of W'soran | `vc_enchant_secrets_wsoran` |
| Hide-Robes of the Flensed Apprentice (Body) | Preserved Tissue — +12 maximum health. | Caress of the Void | `vc_enchant_caress_void` |
| Wing-Mantle of the Cave (Cape) | Winged Reach — +12% spell radius. | Legacy of Arkhan | `vc_enchant_legacy_arkhan` |
| Claws of the Anatomist (Hand) | Nerve-Conduit — +0.12 Winds of Magic recharge. | Unhallowed Pact | `vc_enchant_unhallowed_pact` |

## Warrior Priest of Ulric

Ulric blessings are reused where appropriate, with Ghur Wildform for the hunt/beast theme; no unrelated Chamon armour enchantment remains.

| Item | Removed bespoke property | Native replacement | TOR trait ID |
|---|---|---|---|
| Winter's Bite (relic) | Winter Hunt — +8% movement speed. | Wrath of Ulric | `emp_blessing_ulric_wrath` |
| Wolf-Skull Helm of Middenheim (Head) | Winter-Hardened — 10% resistance to frost damage. | Frenzy of Ulric | `emp_blessing_ulric_frenzy` |
| White Wolf Pelt of the High Temple (Body) | White-Wolf Hide — 5% resistance to physical damage. | Gift of the Winterfather | `emp_blessing_ulric_winterfather_gift` |
| Mantle of the Winter Hunt (Cape) | Snow-Trail Hunter — +8 Scouting. | Wildform of Ghur | `emp_enchant_ghur_wildform` |
| Gauntlets of the Fauschlag (Hand) | Wolf-God's Fury — +6% swing speed. | Frenzy of Ulric | `emp_blessing_ulric_frenzy` |

## Imperial Magister

A cross-college high-tier selection centered on the Observatory/Azyr theme while preserving general Magister spellcasting utility.

| Item | Removed bespoke property | Native replacement | TOR trait ID |
|---|---|---|---|
| Collegiate Staff of Volans (relic) | Magister's Learning — +20 Spellcraft. | Foresight of Azyr | `emp_enchant_azyr_foresight` |
| Volans' Star-Circlet (Head) | Star Reservoir — +18 maximum Winds of Magic. | Providence of Hysh | `emp_enchant_hysh_providence` |
| Robes of the Conclave (Body) | Disciplined Channel — +0.10 Winds of Magic recharge. | Clarity of Hysh | `emp_enchant_hysh_clarity` |
| Mantle of the Eight Winds (Cape) | Balanced Winds — 4% resistance to all damage. | Messengers of Shyish | `emp_enchant_shyish_messengers` |
| Formulaic Gloves of Binding (Hand) | Perfect Notation — +8 Engineering. | Divination of Azyr | `emp_enchant_azyr_divination` |

## Waywatcher

Asrai hunting and forest enchantments, with Predator of Anath Raema on the bow and survival/forest-harmony effects on armour.

| Item | Removed bespoke property | Native replacement | TOR trait ID |
|---|---|---|---|
| The Bow of Loren (relic) | Forest Stalker — +8% movement speed. | Predator of Anath Raema | `asrai_enchant_anath_raema` |
| Hood of the Moonless Glade (Head) | Forest-Sense — +8 Scouting. | Embrace of Isha | `asrai_enchant_embrace_isha` |
| Shadowweave Jerkin (Body) | Breath of the Deepwood — +10 maximum health. | Oakheart's Blessing | `asrai_enchant_oakhart_blessing` |
| Cloak of Falling Leaves (Cape) | Forest Misdirection — 4% resistance to physical damage. | The Tree Lords' Bargain | `asrai_enchant_tree_lord` |
| Boots of the Hidden Path (Leg) | Silent Footing — +8% missile speed. | Leylines and the Weave | `asrai_enchant_leylines_weave` |

## Spellsinger

High Magic/Asrai forest enchantments emphasizing Ward Save, healing, summons, Winds and magical support.

| Item | Removed bespoke property | Native replacement | TOR trait ID |
|---|---|---|---|
| Calaingor's Stave (relic) | The Living Weave — +12% magical damage. | Tranquillity of Cadai | `we_enchant_tranquillity_cadai` |
| Crown of Living Branches (Head) | Sap Reservoir — +15 maximum Winds of Magic. | Radiance of the Woods | `asrai_enchant_radiance_woods` |
| Robe of Sap and Starlight (Body) | Starlight Ward — 6% resistance to magical damage. | Embrace of Isha | `asrai_enchant_embrace_isha` |
| Mantle of Whispering Leaves (Cape) | Leaf-Borne Step — +4% movement speed. | The Tree Lords' Bargain | `asrai_enchant_tree_lord` |
| Rootstep Sandals (Leg) | Deepwood Current — +0.10 Winds of Magic recharge. | Touch of Lileath | `we_enchant_touch_lileath` |

## Warden

Asrai melee/forest enchantments emphasizing mobile spear combat, resilience and Forest Harmony.

| Item | Removed bespoke property | Native replacement | TOR trait ID |
|---|---|---|---|
| Warden's Spear of the Wild Hunt (relic) | Thorn-Point — +12% armor penetration. | Trance of Loec | `asrai_enchant_trance_loec` |
| Antlered Helm of the Hunt (Head) | Spear-Dancer's Eye — +10 Polearm. | Oakheart's Blessing | `asrai_enchant_oakhart_blessing` |
| Thornscale Cuirass (Body) | Hart's Heart — +12 maximum health. | Embrace of Isha | `asrai_enchant_embrace_isha` |
| Cloak of the Stag's Shadow (Cape) | Leaf-Ward — 4% resistance to physical damage. | The Tree Lords' Bargain | `asrai_enchant_tree_lord` |
| Greaves of the Spear-Dancer (Leg) | Thorn-Point — +7% armor penetration. | Leylines and the Weave | `asrai_enchant_leylines_weave` |

## Grey Lord

Eonir/Asrai high magic with shadow-aligned Dusk effects; prioritizes magical defense, Winds and a strong magic/holy weapon capstone.

| Item | Removed bespoke property | Native replacement | TOR trait ID |
|---|---|---|---|
| Shadowstaff of the Grey Order (relic) | Shroud of Shadows — 15% resistance to magical damage. | Dusk and Dawn | `we_enchant_dusk_dawn` |
| Cowl of Unremembered Faces (Head) | Ulgu Discipline — +10 Spellcraft. | Sanctuary of Saphery | `eo_enchant_sanctuary_saphery` |
| Grey Robes of the Ninth Door (Body) | Hidden Reserve — +15 maximum Winds of Magic. | Wisdom of Hoeth | `eo_enchant_wisdom_hoeth` |
| Mantle of Ulgu's Mists (Cape) | Fade Between Steps — +5% movement speed. | Dusk of the Woods | `asrai_enchant_dusk_wood` |
| Gloves of the Hidden Hand (Hand) | Misdirected Blow — +5% armor penetration. | Touch of Lileath | `we_enchant_touch_lileath` |

## Knight of the Old World

Broad Empire enchantments suited to a heavily armoured travelling knight, capped by Chamon Cleave on the runeblade.

| Item | Removed bespoke property | Native replacement | TOR trait ID |
|---|---|---|---|
| Runeblade of the Old World (relic) | Old World Veteran — +20 maximum health. | Crucible of Chamon | `emp_enchant_chamon_crucible` |
| Crested Helm of the Old Orders (Head) | Crest Held High — 5% resistance to physical damage. | Feathers to Lead | `emp_enchant_chamon_feathers_lead` |
| Runeforged Plate of the Imperial Road (Body) | Old Runeforging — 5% resistance to magical damage. | Azure Mirror of Azyr | `emp_enchant_azyr_azure_mirror` |
| Gauntlets of the Twelve Duels (Hand) | Guard-Breaking Form — +15% shield damage. | Wildform of Ghur | `emp_enchant_ghur_wildform` |
| Spurs of the Old World (Leg) | Imperial Road March — +4% party map speed. | Divination of Azyr | `emp_enchant_azyr_divination` |

## Ironbreaker

Top defensive Dawi runes: Adamant on the shield, Gromril/Steel/Preservation/Spell Eating on armour.

| Item | Removed bespoke property | Native replacement | TOR trait ID |
|---|---|---|---|
| Gromril Bulwark of Karaz-a-Karak (relic) | Shieldwall Veteran — +15 Athletics. | Master Rune of Adamant | `dw_master_rune_adamant` |
| Fullhelm of the Deep Gate (Head) | Gromril Brow — 7% resistance to physical damage. | Master Rune of Gromril | `dw_master_rune_gromril` |
| Gromril Plate of the Last Hold (Body) | Runes Under Gromril — 7% resistance to magical damage. | Master Rune of Steel | `dw_master_rune_steel` |
| Gauntlets of the Gatewarden (Hand) | Gromril Bash — +18% shield damage. | Rune of Preservation | `dw_master_rune_preservation` |
| Ironshod Boots of the Underway (Leg) | Underway Veteran — +8 Scouting. | Rune of Spell Eating | `dw_rune_spell_eating` |

## Slayer

Offensive Beastslaying on the oath-axe with strong runes that avoid lethal-save mechanics conflicting with the Slayer death-oath fantasy.

| Item | Removed bespoke property | Native replacement | TOR trait ID |
|---|---|---|---|
| Oath-Axe of Karak Kadrin (relic) | Too Angry to Die — +20 maximum health. | Rune of Beastslaying | `dw_rune_beastslaying` |
| Crest of the Unfulfilled Oath (Head) | Shame-Fed Rage — +6% swing speed. | Rune of Fortitude | `dw_rune_fortitude` |
| Trophy-Cloak of Worthy Foes (Cape) | Troll-Hide Strip — +12 maximum health. | Rune of Vigour | `dw_rune_vigour` |
| Bracers of the Deathblow (Hand) | Axe Bites Deep — +8% armor penetration. | Rune of Protection | `dw_rune_protection` |
| Ironbound Boots of the Long Doom (Leg) | Pursue the Biggest — +5% movement speed. | Rune of Iron | `dw_rune_iron` |

## Runelord

The strongest fitting Dawi runic package, capped by Master Rune of Skalf Blackhammer and top defensive/master runes on armour.

| Item | Removed bespoke property | Native replacement | TOR trait ID |
|---|---|---|---|
| Anvil-Hammer of Thungni (relic) | Rune of Striking — +10% physical damage. | Master Rune of Skalf Blackhammer | `dw_master_rune_skalf` |
| Runic Crown of the Ancestor Gods (Head) | Crown-Ward — 8% resistance to magical damage. | Master Rune of Gromril | `dw_master_rune_gromril` |
| Apron-Plate of the Anvil Guard (Body) | Forge-Hardened — 10% resistance to fire damage. | Rune of Spell Eating | `dw_rune_spell_eating` |
| Gauntlets of Inscription (Hand) | Rune-Power Flow — +0.10 career-resource generation. | Rune of Preservation | `dw_master_rune_preservation` |
| Mantle of Warded Stone (Cape) | Weight of Ages — +8% spell radius. | Master Rune of Steel | `dw_master_rune_steel` |

## Orc Boss

Wallopin Great Krunch on the choppa; armour splits the two Greenskin-valid armour enchantments to avoid four stacked movement penalties or three caster-only slots.

| Item | Removed bespoke property | Native replacement | TOR trait ID |
|---|---|---|---|
| Ulag's Akrit Axe (relic) | WAAAGH! Momentum — +10% swing speed. | Wallopin' Great Krunch | `gs_enchant_wallopin_krunch` |
| Biggest Horned 'Elmet (Head) | Thick Skull — 5% resistance to physical damage. | Tuffness uv Gork | `gs_enchant_tuffness_gork` |
| Boss-Plate of Stolen Gromril (Body) | Stolen Gromril — 4% resistance to magical damage. | Tuffness uv Gork | `gs_enchant_tuffness_gork` |
| Trophy-Rack of Da Best Fights (Cape) | Looks Proper Scary — +4% movement speed. | Call uv da Great Green | `gs_enchant_call_great_green` |
| Iron-Kapped Stompas (Leg) | Boot Through Shield — +18% shield damage. | Call uv da Great Green | `gs_enchant_call_great_green` |

## Orc Shaman

Bad Moon weapon magic plus Great Green/Tuffness armour enchantments, prioritizing Winds, magic resistance and shamanic identity.

| Item | Removed bespoke property | Native replacement | TOR trait ID |
|---|---|---|---|
| Staff of Baduum (relic) | Favour of Gork (or Mork) — +15% magical damage. | Shadow uv da Bad Moon | `gs_enchant_shadow_bad_moon` |
| Moon-Horn Crown (Head) | Moon-Power Reservoir — +18 maximum Winds of Magic. | Call uv da Great Green | `gs_enchant_call_great_green` |
| Mushroom-Smoke Robes (Body) | Fungus-Fed Toughness — +12 maximum health. | Call uv da Great Green | `gs_enchant_call_great_green` |
| Squig-Hide Fetish Mantle (Cape) | Dangly Fetish Reach — +10% spell radius. | Tuffness uv Gork | `gs_enchant_tuffness_gork` |
| Barely-Magical Stompas (Leg) | Grounded, Sort Of — 10% resistance to lightning damage. | Call uv da Great Green | `gs_enchant_call_great_green` |
