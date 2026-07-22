# Large Lad mapping contract

Start a gameplay greybox by duplicating `Assets/scenes/minimal.scene`. Keep one
instance of `Assets/Prefabs/large_lad_gameplay.prefab`, then place one
`LargeLadMapDefinition` in the scene. A map is ready to test when its map
definition reports no contract warnings.

## Required scene structure

```text
Map
├── Geometry and lighting
├── Large Lad Gameplay Bootstrap
│   ├── NetworkHelper
│   └── LargeLadRoundManager
├── Map Definition
├── Waiting Room
│   └── 16 ordered Lobby spawn markers
├── Skinny Kid Starts
│   └── 15 ordered SkinnyKid spawn markers
├── Hunter Starts
│   └── 16 ordered Hunter spawn markers
├── Pickups
├── Barricades
└── Hazards
```

The hunter group is shared by the Large Lad, converted Minions, respawning
Minions, and late joiners. Orders must be unique within each group. The map
definition copies the lobby group into `NetworkHelper` and supplies all three
groups to the round manager; gameplay code should never contain a map-specific
spawn reference.

## Stable player dimensions

- Authoritative capsule: 32 units wide, 72 units tall.
- Step height: 18 units.
- Skinny Kid movement: 110 walk, 320 run.
- Large Lad movement: 85 walk, 230 run.
- Minion movement: 110 walk, 300 run.
- Large Lad melee reach: 100 units.
- Minion melee reach: 80 units.
- The Large Lad's wider body is visual only. Do not make routes depend on that
  visual width.

Useful greybox starting points are 96 units for a comfortable main corridor,
64 units for a deliberately tight branch, 72 units for doors, and 96 units of
clear headroom. Leave extra room at turns, spawn clusters, and melee choke
points. Test the route at the real run speeds before adding detail.

## Authored gameplay objects

- `SkinnyProgression` barricades take damage only from Skinny Kid firearms.
- `LadShortcut` barricades take 100 structural damage per Large Lad swing.
  Minions can use an opened shortcut but cannot open it.
- A barricade is one self-contained hierarchy. Keep its visible/collidable
  `MeshComponent` on the static parent, then add one child named `Network State`
  with Network Mode set to `Object` and a `LargeLadBarricade` component. The
  component discovers its parent automatically; there are no renderer/collider
  references to assign. Move, duplicate, or delete the parent as one unit.
- Barricade and kill-volume gizmos follow their real collider bounds. Their
  inspector padding defaults to 2 units, and their colored outlines remain
  visible through nearby map geometry.
- Core weapon and ammo pickups are independently collectible once per Skinny
  Kid per round. One player's pickup never consumes another player's copy.
- Globally exclusive pickups may have only one owner. They move to the owner's
  death position with their remaining ammunition, then return to their authored
  transform on round reset.
- Kill volumes use the normal role respawn rules. A dead Skinny Kid converts to
  a Minion; a Minion respawns at the hunter start; the Large Lad uses his own
  death timer and returns to hunter spawn order 0.

Every destructible and pickup must implement the shared round-reset contract.
Map state is reset immediately before roles and positions are assigned for the
next round.

## Default timing

- Head start: 10 seconds.
- Skinny Kid survival timer: 60 seconds.
- Intermission: 5 seconds.

These values may be changed on the map definition. Map progress and branch
selection do not alter the survival timer; Skinny Kids win only by surviving it.
