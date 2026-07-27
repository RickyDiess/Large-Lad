# Large Lad scene-mapping guide

`Assets/scenes/Gym.scene` is the current game-mode startup scene. A valid
`StartupScene` is required because an empty setting falls back to the missing
`start.scene`. Use normal editor Play for the scene that is currently open, and
use Play in Game Mode when testing the configured startup flow. Make future maps
by duplicating `Assets/scenes/template.scene`. Do not copy gameplay scripts
between maps and do not add a second gameplay bootstrap.

## Starting a map

1. Duplicate `template.scene` and give the copy a map-specific name.
2. Keep its single `Large Lad Gameplay Bootstrap` prefab instance.
3. Enter the editor's Mapping mode and build ordinary scene geometry.
4. Place or duplicate the gameplay prefabs from `Assets/Prefabs/Gameplay`.
5. Run the scene and resolve every `Map contract:` warning before testing it.

The bootstrap contains `NetworkHelper`, `LargeLadRoundManager`,
`LargeLadSpawnAllocator`, and `LargeLadMapDefinition`. The map definition owns
the three per-map timing values and validates the scene. Its defaults are a
10-second head start, a 60-second survival timer, and a 5-second intermission.

## Team spawns

Use the three spawn presets:

- `Lobby Team Spawn`
- `Skinny Kid Team Spawn`
- `Hunter Team Spawn`

The hunter group is shared by the Large Lad, converted or respawning Minions,
late joiners, and Large Lad respawns. A spawn component defines a horizontal
circle rather than one exact position:

- `SpawnRadius`: 160 units by default.
- `Capacity`: 16 by default.
- `MinimumSeparation`: 48 units by default.

One point per group is enough when its full circle lies above clear floor.
Multiple points may be used for unusually shaped rooms. There are no order
numbers or hand-wired spawn lists. NetworkHelper's generated lobby positions
appear as runtime children of the Lobby Team Spawn that produced them.

At runtime the allocator probes downward for the floor, checks the 32-by-72
player capsule, and reserves unique positions during batch spawns. Individual
respawns prefer the valid point farthest from living players. If a circle is
crowded it uses the least-crowded valid point; it never deliberately chooses a
position inside geometry.

The full map contract requires total configured capacity of 16 for Lobby, 15
for Skinny Kids, and 16 for Hunter. The colored editor gizmos preview each
circle and its configured capacity. Keep the circles out of walls even when the
component's center is clear.

## Stable player dimensions and movement

- Authoritative capsule: 32 units wide and 72 units tall.
- Step height: 18 units.

The authoritative gameplay baseline is
`Assets/Gameplay/default_role_profiles.llroles`:

| Role | Walk | Run | Maximum health | Incoming damage | Body tint | Visual scale |
| --- | ---: | ---: | ---: | ---: | --- | --- |
| Skinny Kid | 110 | 300 | 100 | 1.0x | White `(1, 1, 1, 1)` | `(1, 1, 1)` |
| Large Lad | 85 | 100 | 500 | 0.1x | White `(1, 1, 1, 1)` | `(1.25, 1.45, 1)` |
| Minion | 110 | 325 | 75 | 1.0x | White `(1, 1, 1, 1)` | `(1, 1, 1)` |

All three normalized body tints are identity white, so changing role does not
add role coloring. Clothing colors remain unchanged.

The player prefab's generic controller starts at 110 walk and 300 run.
It references the role-profile resource once on `LargeLadPlayer`.
`LargeLadPlayer` applies movement and body visuals at start and whenever its
host-synchronized role changes. `LargeLadHealth` and `LargeLadMeleeSystem`
resolve the current role through that player reference, so they do not contain
their own role balance copies. Missing profiles, non-positive movement,
health, scale, or melee values, and negative incoming-damage multipliers report
`Large Lad role profiles` validation warnings.

The Large Lad's width is visual only, so routes must fit the normal player
capsule. Useful greybox starting points are a 96-unit comfortable main corridor,
a 64-unit deliberately tight branch, a 72-unit doorway, and at least 96 units
of clear headroom. Leave more room around turns, spawn circles, and melee choke
points.

## Barricades

The two presets are:

- `Skinny Progression Barricade`: 300 health; Skinny Kid melee and firearms
  damage it. This lets maps place the first firearms beyond an opening melee
  barricade.
- `Lad Shortcut Barricade`: 300 health; only Large Lad melee damages it.
  Minions can use the route after it opens but cannot open it.

A barricade is one self-contained GameObject using Network Mode `Object`. Its
visible mesh or renderer, collision, and `LargeLadBarricade` component all live
on that same object. Health and destruction synchronize directly on the
component; there is no network-state child or external controller.

For a custom Scene Mapping barrier:

1. Create and texture the geometry in Mapping mode.
2. Select the resulting mesh GameObject.
3. Add `LargeLadBarricade` to that same object.
4. Set Network Mode to `Object`.
5. Choose `SkinnyProgression` or `LadShortcut`.

The component automatically uses a same-object `MeshComponent`, or a
same-object renderer and collider. Destruction disables rendering and collision
on every client. Round reset restores both. Optional local cosmetic debris may
be assigned in the component without adding networked physics debris.

## Pickups and hazards

The gameplay prefab folder contains pistol, SMG, pistol-ammo, SMG-ammo, and kill
volume presets. Their temporary models are explicit scene content and can be
replaced with production assets later.

Every active role inherently receives melee in inventory slot 1. A Skinny Kid's
first firearm pickup is automatically equipped, while melee remains selectable.
Weapon placement controls when ranged combat becomes available; the typical
route puts the first firearm pickups beyond the first Skinny Progression
Barricade.

Holding primary attack auto-swings melee for every role at that role's
cooldown in the authoritative role-profile resource:

| Role | Damage | Range | Cooldown | Fallback aim assist |
| --- | ---: | ---: | ---: | --- |
| Skinny Kid | 25 | 80 | 0.65 seconds | On |
| Large Lad | 10 | 100 | 0.1 seconds | Off |
| Minion | 25 | 80 | 0.5 seconds | On |

All roles use an 18-unit swing-trace radius. Fallback aim assist requires a
minimum facing dot of 0.55. Maintaining roughly one second of accurate Large
Lad contact drains a full-health Skinny Kid and converts them on death.

The player prefab's `LargeLadMeleeSystem` has one optional Skinny Kid
`MeleeModel`. The verified attachment baseline is the Citizen crowbar
(`models/citizen_props/crowbar01.vmdl`) on the `hold_R` bone, with hold-relative
position `(0, 0, 0)`, angles `(0, 0, 0)`, model-space grip point `(0, 0, 0)`,
and scale `0.25`. First-person melee arms are enabled with position and angles
both `(0, 0, 0)`. The grip point remains locked to the hand when the model
scale changes. Clear or replace the model without changing combat logic. The
Large Lad and Minions always attack unarmed and never display this model.

In first-person mode the local player receives overlay-rendered melee arms,
bone-merged to the same Citizen animation as the world body. The melee model
uses those first-person hand bones, while third-person and remote players use
the world body's hand bones.

Core weapon pickups are independently collectible once by every Skinny Kid each
round; one player does not consume the pickup for anyone else. Core weapons do
not drop on infection. Ammo placements grant a finite weapon-specific refill
once per Skinny Kid per round and reset for the next round.

Globally exclusive bonus weapons are supported for later content. An exclusive
weapon drops with its remaining ammunition when its owner dies, then its
authored pickup returns on round reset.

A kill volume is an ordinary GameObject with a trigger collider and
`LargeLadKillVolume`. Resize the collider to cover the hazard. Skinny Kids killed
by it become Minions, Minions use their normal respawn, and the Large Lad uses
the Large Lad respawn timer.

## Preflight checklist

- Exactly one gameplay bootstrap exists.
- At least one team-spawn component exists for each group.
- Configured capacities meet 16 Lobby, 15 Skinny Kid, and 16 Hunter.
- Spawn circles produce clear floor positions and do not cross walls.
- Every barricade has same-object rendering and collision and uses Network Mode
  `Object`.
- Every pickup has a visible model and trigger collider.
- Every kill volume has a trigger collider.
- The scene reports no `Map contract:` warnings.
- Host and remote clients complete a round, intermission, reset, late join,
  conversion, and respawn test.

Skinny Kids win only by surviving the timer. Route progress, weapon pickups, and
destroying barricades do not change that rule.
