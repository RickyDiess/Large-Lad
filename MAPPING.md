# Large Lad Hammer mapping guide

Large Lad maps are authored in Hammer. Hammer owns the world geometry,
lighting, materials, and route layout; reusable GameObject prefabs embedded in
the `.vmap` provide the gameplay logic.

The starter map is:

```text
Assets/maps/large_lad_mapping_template.vmap
```

Open it in Hammer and use **Save As** before beginning a real map. The original
is deliberately a plain room: its center acts as a compact lobby containing
the three generic team spawns alongside practical examples of every
authored gameplay object.

## Starting a map

In Hammer, right-click in the 3D viewport and choose:

```text
Create GameObject
+-- Large Lad
    +-- Gameplay Bootstrap
    +-- Lobby Examples (Complete)
    +-- Spawns
    +-- Pickups
    +-- Barricades
    +-- Hazards
```

The starter `.vmap` already contains exactly one **Gameplay Bootstrap** and
one generic spawn for each group. Keep the bootstrap at world origin. It contains
the network helper, round manager, and the map definition whose timing fields
you edit per map. The lobby example prefab contains:

- core Pistol and SMG pickups with their ammunition;
- one exclusive bonus-weapon test pickup;
- one Skinny Kid progression barricade;
- one Large Lad shortcut barricade; and
- one kill volume.

Each generic spawn supports 16 players by default. It chooses clear randomized
positions within a 160-unit circle, with 48 units of separation. Change the
radius or capacity only when a particular room needs it. Multiple points for
the same group are allowed and contribute to one shared spawn pool.

Do not add a separate map definition. There are no orders, per-player markers,
or serialized links to maintain.

## Required map contract

Every playable `.vmap` must contain:

```text
Hammer map
+-- World geometry and lighting
+-- Large Lad Gameplay Bootstrap (exactly one; includes Map Definition)
+-- Lobby Team Spawn (one or more; total capacity 16)
+-- Skinny Kid Team Spawn (one or more; total capacity 15)
+-- Hunter Team Spawn (one or more; total capacity 16)
+-- Pickups and ammunition
+-- Barricades
+-- Hazards
```

The bootstrap discovers these objects at runtime and generates hidden lobby
positions for `NetworkHelper`. The round manager uses the same allocator for
every round start, late join, conversion, and respawn. There are no map-specific
object IDs or code references.

The hunter group is shared by the Large Lad, converted Minions, respawning
Minions, and late joiners. Candidates are floor-probed and checked against the
normal 32-by-72 player capsule. If all clear candidates are occupied, the game
uses the least-crowded valid position rather than putting somebody in a wall.

## Barricades and hazards

The menu prefabs are self-contained examples and can be placed directly in
Hammer. They are expanded into ordinary map GameObjects when placed, so the
compiled map does not depend on loose project prefab files. For production
maps, make barriers and hazards from Hammer meshes so their shape can be
edited with the normal vertex, edge, and face tools.

- **Skinny Progression** has 300 health and accepts only Skinny Kid firearm
  damage.
- **Lad Shortcut** has 300 health and accepts only Large Lad melee damage at
  100 structural damage per swing. Minions can use the opened route but cannot
  open it.
- Destroyed barricades disable authoritative rendering and collision. Their
  debris is short-lived local decoration, never networked physics.
- Kill volumes use the normal death flow: Skinny Kids become Minions, Minions
  respawn, and the Large Lad uses his own respawn timer.

### Brush barricade (recommended one-click workflow)

1. Build and texture one or more normal Hammer meshes.
2. Select only the raw meshes in the 3D viewport.
3. Right-click and choose **Large Lad -> Create Barricade**.
4. Choose **Skinny Progression** or **Lad Shortcut**.
5. Choose **Group Selection** or **Separate Brushes**.

**Group Selection** ties every selected mesh to one barricade with one shared
health pool; all of its pieces disappear together. **Separate Brushes** makes
one independent barricade from each selected mesh, with its own health and
round reset. The resulting barricade GameObject is selected automatically.
Hammer undo restores the original untied meshes in one step.

The command preserves the brush geometry and materials and installs
`LargeLadBarricade`. Hammer's generated `HammerMesh` stays local map geometry
and remains both the renderer and exact solid collision. A small network-state
child is created automatically to synchronize health and destruction. Do not
network the brush itself, and do not add a `BoxCollider` for an ordinary
tied-brush barricade.

The command is unavailable if the selection includes entities, GameObjects,
already-tied meshes, or anything other than raw Hammer meshes. This prevents a
mixed selection from producing a partially converted map.

### Manual barricade fallback

If an older tied barricade is missing its state child or still has the brush
itself networked:

1. Select its tied Hammer mesh or meshes.
2. Right-click and choose **Large Lad -> Repair Barricade**.
3. Choose **Repair as Skinny Progression** or **Repair as Lad Shortcut**.

The repair is one undo operation and preserves the selected geometry and
materials. If the context command is unavailable after an editor-code update,
restart the S&box editor so Hammer reloads the project editor assembly.

For a fully manual fallback:

1. Build and texture the barricade as a normal Hammer mesh.
2. Select the mesh and click **Tie Selected Meshes to GameObject**.
3. Add `LargeLadBarricade` to that same GameObject.
4. Pick `SkinnyProgression` or `LadShortcut`.
5. Set the brush GameObject to Network Mode `Never`.
6. Add a child GameObject named `Barricade Network State`, set it to Network
   Mode `Object`, and add `LargeLadBarricadeState`.

The component reasserts rendering, solid collision, and static behavior when
the compiled map streams in. The map validator reports an incorrectly
networked brush or missing state child, and warns about an extra `BoxCollider`
without deleting it.

### Brush kill volume

1. Build a normal Hammer mesh covering the lethal area.
2. Tie it to a GameObject with **Tie Selected Meshes to GameObject**.
3. Add `LargeLadKillVolume` to the same GameObject.

The component turns that Hammer mesh into an invisible static trigger at
runtime. The brush remains visible and editable in Hammer. Do not also add a
`trigger_hurt`; the Large Lad component routes the death through the game's
infection and respawn rules.

For a tied brush, the Hammer mesh itself is the exact authoring preview; the
components deliberately do not draw a second, easily offset box over it. The
colored through-wall gizmos are retained only for the prefab/BoxCollider
versions, where they use the collider's world bounds plus `Gizmo Padding`.
Kill-volume brushes are invisible in a running game by design.

## Pickups

Core Pistol and SMG pickups are not consumed globally. Every Skinny Kid can
collect each placement once per round. Their ammo placements work the same way
and default to two magazines.

Exclusive bonus pickups have one global owner. They drop with their remaining
ammunition when that owner dies and return to the authored position on round
reset. Core weapons never drop on infection; the new Minion inventory is
cleared.

Core weapon examples are recommended but optional, so their absence produces a
validation warning instead of preventing a map from running.

## Stable layout measurements

- Player capsule: 32 units wide, 72 units tall.
- Step height: 18 units.
- Skinny Kid: 110 walk, 320 run.
- Large Lad: 85 walk, 230 run.
- Minion: 110 walk, 300 run.
- Large Lad melee reach: 100 units.
- Minion melee reach: 80 units.
- Comfortable main corridor: 96 units wide.
- Deliberately tight branch: 64 units wide.
- Ordinary doorway: 72 units wide.
- Recommended clear headroom: at least 96 units.

The Large Lad's wider appearance is visual only. The normal capsule remains
authoritative, so do not size routes around the rendered belly.

## Timing and reset

The map definition defaults to a 10-second head start, 60-second survival
timer, and 5-second intermission. Skinny Kids win only by surviving the timer;
route progress never changes it.

Immediately before a new round, the reset contract restores barricades,
pickups, dropped bonus weapons, temporary map objects, and player loadouts.

## Running a Hammer map

`Assets/scenes/hammer_runtime.scene` is the thin startup scene. Its
`MapInstance` uses the Hammer map supplied at launch. The configured map name
remains a fallback for ordinary editor play:

```text
maps/large_lad_mapping_template.vmap
```

Use Hammer's normal build/run command while the map is open. Do not add a
second `MapInstance` for the same map; the launch map is loaded once and its
embedded gameplay objects become the authoritative runtime scene. Map rotation
and a production map selector are intentionally deferred.

Before detailing a route, test these basics with two clients:

1. The validator reports no errors.
2. Both roles receive distinct clear positions inside their generic spawn areas
   with no inherited velocity.
3. Core pickups remain available independently to both Skinny Kids.
4. Skinny firearms open only the progression barrier.
5. Large Lad melee opens only the shortcut barrier.
6. A kill volume follows the correct conversion or respawn rule.
7. A full round reset restores every authored object.
