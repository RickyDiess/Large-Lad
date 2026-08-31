# Large Lad lifetime Stats service definitions

Large Lad submits these cumulative counters through the earning player's local
`Sandbox.Services.Stats` context after the host commits the corresponding
gameplay event. The code-side identifier catalog does not create the visible
definitions on the published `sbox.game` package.

Create all entries manually on the published game package under
**Services → Stats**. Use **Sum** aggregation and the generic count/no-special-unit
setting for every v1 entry.

The game submits by string ident and remains safe while a definition is absent
during development; the dashboard definition supplies display/query metadata,
not gameplay authority.

| Ident | Display title | Description | Aggregation | Unit |
| --- | --- | --- | --- | --- |
| `rounds_played` | Rounds Played | Successfully completed rounds joined from the start. | Sum | Count |
| `skinny_rounds_played` | Skinny Rounds Played | Completed rounds started as a Skinny Kid. | Sum | Count |
| `large_lad_rounds_played` | Large Lad Rounds Played | Completed rounds started as the committed Large Lad. | Sum | Count |
| `skinny_kid_wins` | Skinny Kid Wins | Skinny victories earned while still a living Skinny Kid. | Sum | Count |
| `large_lad_wins` | Large Lad Wins | Hunter victories earned as the round's committed Large Lad. | Sum | Count |
| `minion_wins` | Minion Wins | Hunter victories earned while ending the round as a Minion. | Sum | Count |
| `last_skinny_kid_survivals` | Last Skinny Kid Survivals | Skinny wins after becoming and remaining the Last Skinny Kid. | Sum | Count |
| `perfect_large_lad_wins` | Perfect Large Lad Wins | Large Lad victories with zero committed Large Lad deaths that round. | Sum | Count |
| `kills` | Kills | Committed deaths with direct or inherited player kill credit. | Sum | Count |
| `assists` | Assists | Unique valid recent-damage assists on committed deaths. | Sum | Count |
| `deaths` | Deaths | Committed player deaths. | Sum | Count |
| `headshot_kills` | Headshot Kills | Kills whose actual lethal firearm hit was a headshot. | Sum | Count |
| `skinny_kids_eaten` | Skinny Kids Eaten | Skinny Kid deaths committed by a successful Large Lad Eat. | Sum | Count |
| `large_lad_kills` | Large Lad Kills | Credited player kills earned while acting as Large Lad. | Sum | Count |
| `minion_kills` | Minion Kills | Credited player kills earned while acting as a Minion. | Sum | Count |
| `skinny_kid_deaths` | Skinny Kid Deaths | Deaths committed while the victim was a Skinny Kid. | Sum | Count |
| `large_lad_deaths` | Large Lad Deaths | Deaths committed while the victim was Large Lad. | Sum | Count |
| `minion_deaths` | Minion Deaths | Deaths committed while the victim was a Minion. | Sum | Count |
| `conversions` | Conversions | Authoritative Skinny Kid-to-Minion conversion commits. | Sum | Count |
| `pistol_kills` | Pistol Kills | Kills whose actual lethal weapon was the Pistol. | Sum | Count |
| `smg_kills` | SMG Kills | Kills whose actual lethal weapon was the SMG. | Sum | Count |
| `shotgun_kills` | Shotgun Kills | Kills whose actual lethal weapon was the Shotgun. | Sum | Count |
| `rifle_kills` | Rifle Kills | Kills whose actual lethal weapon was the Rifle. | Sum | Count |
| `melee_kills` | Melee Kills | Kills whose actual lethal method was ordinary melee. | Sum | Count |
| `dodgeball_kills` | Dodgeball Kills | Kills whose actual lethal method was a dodgeball. | Sum | Count |
| `barricades_destroyed` | Barricades Destroyed | Final Skinny Progression barricade destructions credited to a Skinny Kid. | Sum | Count |
| `shortcuts_destroyed` | Shortcuts Destroyed | Final Lad Shortcut destructions credited to the Large Lad. | Sum | Count |

Stage 6 routes all four Core firearm methods through the one committed death
record. Only the actual lethal weapon earns its Pistol, SMG, Shotgun, or Rifle
counter; rejected/replayed claims and nonlethal pellets earn none.
