# Large Lad scene-mapping guide

## Community mapper quick-start

1. Create an s&box **Addon Project**, set **Large Lad** as its Target Game in
   Project Settings, and restart the editor if the target changed.
2. Duplicate/open `Assets/scenes/template.scene`. Keep its one **Large Lad Map
   Profile** and its three clearly named starter spawn-prefab instances. Move the
   spawn areas to suit your layout; do not add gameplay/session infrastructure.
3. Create a **Large Lad Map Manifest** (`.llmap`). Assign it to the Map Profile
   and fill in a stable lowercase map id, contract version `1`, local display
   name, mapper credit, local thumbnail, and recommended player range. Leave
   **Published Package Ident** empty during local development.
4. Build ordinary scene geometry, lighting, props, and collision.
5. Place Large Lad objects from the Asset Browser's `Large Lad/` prefab menus:
   team spawns, weapon/dodgeball pickups, barricades, Minion vents, and kill
   volumes. Add the one-component Eat Smashable or Ground Slam Reactive Prop
   only to custom objects that need those behaviors.
6. Select **Large Lad Map Profile**. Use **Rebuild Spawns and Validate** after
   moving floors, walls, ceilings, or spawn circles; use **Validate Large Lad
   Map** for a normal audit. Fix every blocking error and review warnings.
7. Test the content through `Assets/scenes/game_shell.scene`. A content scene is
   intentionally not a standalone Large Lad session.
8. Publish the scene through normal s&box map publishing. Configure the package
   page's title, summary, and thumbnail.
9. Put the published `organization.ident` into the manifest's **Published
   Package Ident**, publish that manifest-bearing revision, and point the shell
   coordinator at the same ident.
10. Test the published ident with a host and remote client. It must pass the same
    descriptor, map-content validation, spawn projection, and admission path as
    a local scene.

The content scene should contain map-owned content only. Never add
`LargeLadGameManager`, `LargeLadSessionCoordinator`, `LargeLadSpawnAllocator`,
`NetworkHelper`, `MapInstance`, a scene registry, or
`prefabs/large_lad_gameplay.prefab`. Validation names any forbidden object and
blocks admission.

## Mapper-facing objects

### Map Profile and manifest

Place exactly one enabled **Large Lad Map Profile** (the template supplies it)
and assign one `.llmap`. Normally edit the manifest's identity, local
presentation, mapper credit, player range, and approved overrides. Common
failures are a missing/duplicate profile, missing manifest, malformed stable id,
unsupported contract version, missing local thumbnail/credit/name, or an invalid
player range. Published Package Ident is not required until publishing.

### Lobby spawn

Use `Large Lad/Spawns/Lobby Team Spawn` for joining and between-round players.
Normally move the circle and change `Spawn Radius` only when the room requires
it; total configured and projected capacity must both reach 32. Failures report
no area, configured shortfall, or geometry-projected shortfall separately.

### Skinny Kid spawn

Use `Large Lad/Spawns/Skinny Kid Team Spawn` for round-start survivors. Normally
move/resize it while keeping the preset capacity of 31. Walls, low ceilings,
missing walkable floor, or excessive overlap with another Skinny Kid area reduce
its projected usable count.

### Hunter spawn

Use `Large Lad/Spawns/Hunter Team Spawn` for the Large Lad, Minions, conversions,
and Hunter respawns. Normally move/resize it while keeping capacity 32. The same
missing/configured/geometry distinctions used by the other spawn groups apply.

### Weapon pickup

Place the supplied Pistol, SMG, Shotgun, or Rifle pickup when Skinny
Kids should gain that firearm.

The Stage 6 Core firearm defaults are:

| Order | Weapon | Damage | Magazine | Fire | Cadence | Range | Pellets | Spread base / growth / recovery |
| ---: | --- | ---: | ---: | --- | ---: | ---: | ---: | --- |
| 0 | Pistol | 50 | 8 | Semi | 0.35 s | 2400 | 1 | `0,0` / `0,0` / `0.2` |
| 1 | SMG | 25 | 30 | Automatic | 0.09 s | 2000 | 1 | `0.2,0.2` / `1.5,1.5` / `0.2` |
| 2 | Shotgun | 15 per pellet | 6 | Semi | 0.8 s | 1400 | 8 | `3,3` / `2,2` / `0.8` |
| 3 | Rifle | 60 | 20 | Semi | 0.25 s | 4000 | 1 | `0.1,0.1` / `0.25,0.25` / `0.25` |

The shotgun uses the native incremental reload (`0.55` seconds per shell) and
the Spaghelli M4 graph's authored shell-reload animations.

Official model paths are
`models/weapons/sbox_shotgun_spaghellim4/v_spaghellim4.vmdl`,
`models/weapons/sbox_shotgun_spaghellim4/w_spaghellim4.vmdl`,
`models/weapons/sbox_assault_m4a1/v_m4a1.vmdl`, and
`models/weapons/sbox_assault_m4a1/w_m4a1.vmdl`. The M4A1 renderers use bodygroup
mask `0`, the package's clean default configuration, with no authored optics,
suppressor, laser, or other attachment.
Normally change placement and `Pickup Policy (Per Instance)`; use `Core` for an
independent permanent unlock point or `Exclusive` for one physical item. Common
failures are an invalid weapon/policy, missing trigger/model, or an Exclusive
root that is not Network Mode `Object`.

### Dodgeball pickup

Place `Large Lad/Pickups/Dodgeball` when the map should offer the utility ball.
Normally change only placement. Its solid ball collider, separate pickup trigger,
Rigidbody, collision tag, Object networking, and Move-only Ground Slam reaction
are prefab setup; validation tells you to restore/replace the prefab if damaged.

### Skinny Progression barricade

Place `Large Lad/Barricades/Skinny Progression` where Skinny Kids may open route
progress with melee. Normally change placement, health, optional compound stages,
and optional destruction announcement/display name. Common failures are a
missing root blocker, invalid stage thresholds, child ordering, or an enabled
announcement without a name.

### Lad Shortcut barricade

Place `Large Lad/Barricades/Lad Shortcut` where the Large Lad's Eat fallback may
break a shortcut. Normally change placement, health, and optional stages. It uses
the same root-collider/stage validation as Skinny Progression but cannot announce
route progress.

### Minion vent

Place `Large Lad/Passages/Minion Vent Opening` across each vent entrance that
only Minions may traverse. Normally change placement and optionally enable/tune
the supplied breakable cover. Common failures name a missing/non-solid/non-root
opening collider, missing passage tag, wrong network mode, or a cover containing
extra collision. Restore the supplied prefab instead of hand-assembling the gate.

### Eat smashable

Add `LargeLadEatSmashable` to a custom collidable structure only when Large Lad
Eat should damage it after no player victim is available. Normally change
`Maximum Health` and assign the intended collider. A missing collider or
non-positive/non-finite health is reported on that object.

### Ground Slam reactive prop

Add `LargeLadGroundSlamReactiveProp` to a non-critical prop that should Move,
Unanchor, or Break. Normally select the behavior, whether it starts frozen, and
bounded impulse/cleanup options. Validation rejects critical gameplay objects,
blockers, incompatible physics, invalid impulses, and destructive dodgeball
behaviors.

### Kill volume

Place `Large Lad/Hazards/Kill Volume`, resize its box, and cover the lethal area.
Normally change only transform, collider size, and optional gizmo padding. A
missing collider or collider without **Is Trigger** is reported on the named
volume.

## Deeper architecture and publishing details

`Assets/scenes/game_shell.scene` is the game-mode startup scene. It contains one
persistent `Large Lad Gameplay Bootstrap` plus a separate root-level Snapshot
`Map Content Host`; that host's built-in `MapInstance` loads replaceable map
content beneath itself. `Assets/scenes/Gym.scene` is the current proof map and local-development
default. It contains geometry, spawns, pickups, passages, barricades, lighting,
and other map-owned state, but no game manager, network helper, spawn allocator,
session coordinator, or map instance.

Use Play in Game Mode to test the configured shell flow. Playing a content scene
directly is useful for mapping and visual inspection, but intentionally does not
start a Large Lad session. Make future maps by duplicating
`Assets/scenes/template.scene`. Do not add `large_lad_gameplay.prefab` or any
session-global owner to a map-content scene.

## Game shell and map-content boundary

The persistent bootstrap owns exactly one `LargeLadSessionCoordinator`,
`NetworkHelper`, `LargeLadSpawnAllocator`, and `LargeLadGameManager`. The shell's
separate Map Content Host owns exactly one built-in `MapInstance`. The coordinator
owns map selection, loading, unloading,
the host-synchronized current map name, and the synchronized `Unloaded`,
`Loading`, `Ready`, `Unloading`, or `Failed` lifecycle state. `IsMapReady` is
derived from that state rather than being an independently mutable flag.
`MapInstance` itself owns asynchronous loading, package mounting, scene-map
creation, and the loaded/unloaded callbacks; there is no Large Lad polling or
package-loader layer. The `MapInstance` lives on the dedicated `Map Content Host`,
whose children are exclusively map-owned. The local `.scene` adapter therefore
finishes deleting only the exact pre-unload map-host children that the engine's
listen-host snapshot safety can retain. Published package maps use the native
callback/unload path directly.

The game manager remains the owner of round state, transitions, timing, and
player lifecycle. `LargeLadMapValidator` owns mapper-authored map rules, while
the runtime allocator owns the admitted projection cache and allocation. A loaded callback first resolves the
loaded map's one `LargeLadMapProfile` into the common map descriptor and checks
its compatibility version. It then asks the manager to rebuild the map-owned
projection, run the focused structural validation, and cache the same result for
runtime allocation. Only then can the
coordinator enter `Ready`. Before an unload or replacement request, the
coordinator enters `Unloading`, immediately holds persistent players, uses the
existing inventory map-transition cleanup once, returns the round to its waiting
state, clears the active map descriptor and balance layer, and invalidates
map-owned spawn data. The unloaded callback then enters `Unloaded` or begins the
replacement's `Loading` state. A loaded map that fails blocking validation
enters `Failed`. No timer, round transition, gameplay respawn, or conversion
advances outside `Ready`.

The game project declares `MapSelect` as `Empty`, has no external `MapList`, and
starts `scenes/game_shell.scene`. The shell's `MapInstance` keeps `Use Map From
Launch` disabled. Consequently the platform does not replace the startup shell
with a selected official or community map; map selection must enter through the
persistent coordinator and its built-in `MapInstance`.

The coordinator's `Startup Map` and `LoadMap` accept a published package ident
such as `organization.map_name`; that is the production map-selection path and
uses the engine's automatic download/mount behavior. For local development,
`Startup Map` and `Local Development Map` are currently
`scenes/gym.scene`. The coordinator synchronizes its selected name from the host
and passes either form directly to each peer's local `MapInstance`. The dedicated
root-level `Map Content Host` uses Network Mode `Snapshot`, so the authored loader
is present in the initial scene snapshot for both early and late joiners. The
coordinator resolves that
existing scene loader on every peer; it never fabricates a replacement or
serializes a component reference across the gameplay bootstrap's network-object
boundary. It sends the authoritative selected identifier through synchronized
state and a reliable runtime RPC. Every peer therefore performs the engine's
normal peer-local map load, including static world geometry, collision, lights,
and presentation. Local `.scene` content keeps those top-level static roots in
Network Mode `Never`; they are deterministically created by every peer's
`MapInstance` and are not replicated as ordinary network GameObjects. Map-authored
Object-mode gameplay objects remain host-authoritative. After a host load, the
coordinator collects every highest authored Object root, including top-level and
nested roots, and clones the complete set as one generation. The engine therefore
assigns fresh GameObject/component GUIDs and remaps cross-root references together.
The coordinator removes the stable-ID authored sources, places the generation
roots directly beneath `Map Content Host` with their authored world transforms,
and network-spawns them. Fresh identities are required on every load: reusing an
authored GUID during a same-map reload can make a replacement create collide with
the departing proxy's delete on a client. The coordinator publishes the exact
generation root GUIDs alongside the selected map. Each client waits until every
listed GUID resolves to an active network root before starting its local
`MapInstance` load; host readiness or message ordering alone is not treated as a
delivery barrier. After loading, the client destroys only inactive, highest
authored Object roots left by scene deserialization and preserves every active
authoritative root. Static map content continues to load locally and is never part
of reconciliation. This boundary is generic and does not special-case gameplay
component types.

## Map manifest and compatibility

Every playable Large Lad content scene must contain exactly one enabled
`LargeLadMapProfile`. Assign it one `Large Lad Map Manifest` (`.llmap`) asset.
The profile keeps the manifest referenced by the scene so normal map publishing
includes it, and it lets the persistent shell inspect the exact loaded map
rather than a cache of previously mounted resources. `Gym.scene` and
`Gameplay/Maps/gym_manifest.llmap` are the working local example. The mapping
template already contains an unassigned profile.

Set these manifest fields:

- `Stable Map Id`: a permanent lowercase key such as
  `my_org.school_escape`. It may contain letters, digits, `.`, `_`, and `-`.
  Never derive it from a changeable display name.
- `Large Lad Contract Version`: currently `1`. Compatibility is an exact
  integer match. An unsupported version reports the map, its declared version,
  the version this game supports, and blocks `Ready`.
- `Published Package Ident`: the immutable `organization.ident` selected when
  publishing the map. It must match the value later passed to `MapInstance`.
  Local `.scene`/`.vmap` identity instead comes from the shell's active
  `MapInstance` selection, avoiding a scene-to-manifest-to-scene asset cycle.
- `Local/Fallback Display Name`, `Mapper/Author Credit`, and
  `Local/Fallback Thumbnail`: supply complete local-development presentation.
  The thumbnail picker accepts only textures and image files. Never assign the
  map scene, another map, a manifest, or a prefab: the scene already references
  its manifest, so pointing the manifest back at scene content creates a
  recursive dependency. For a published map, its package title and thumbnail
  are used first; mapper credit remains available because a publishing
  organization is not always the individual mapper.
- Optional `Backstory` and `Gameplay Tip`.
- An ordered recommended player range from 2 through 32. This is presentation
  and validation metadata only; it never changes Large Lad's global supported
  maximum of 32 players.

A manifest has no official/community switch. `Gameplay/Maps/official_maps.llmaps`
is the first-party catalog owned by Large Lad. Matching membership in that
catalog is the only source of the normalized descriptor's official-curation
status. A community map with the same stable id as an official map remains a
community map and cannot promote itself. Official and community entries use the
same descriptor, compatibility rules, loader, structural validation, balance
composition, round systems, and future voting inputs.

The `Approved Balance Overrides` group deliberately contains only:

- survival duration in seconds;
- Skinny Progression barricade maximum-health multiplier;
- Large Lad maximum-health multiplier;
- late-round Hunter escalation multiplier.

Zero means no override. Survival duration replaces the shell default for that
map. The two health factors multiply the existing fixed player-count band from
`default_round_balance.llbalance`. The Hunter factor multiplies both existing
role-specific late-round maximums without changing the timer-only ramp interval.
Every value is resolved from the game defaults and the newly loaded manifest;
no resolved value is written back into either source, so reloads and map changes
cannot compound. Firearms, Eat, Ground Slam, conversion/respawn, Last Skinny
Kid, dodgeball, roles, inventory, and movement rules have no map override.

For a community map, use the currently supported asset workflow:

1. Create an Addon Project and choose Large Lad as its Target Game in Project
   Settings. Changing the target requires an editor restart. The target exposes
   Large Lad's components, prefabs, and custom resource types without copying
   game code into the map project.
2. Create a scene map, preferably starting from Large Lad's mapping template,
   and author it in Mapping mode.
3. Create a `Large Lad Map Manifest`, configure the stable id, current contract
   version, metadata, recommended range, and only any intentional
   approved overrides. Assign it to the scene's one map profile.
4. Place the supplied team spawns and gameplay prefabs. Select the Map Profile,
   use `Rebuild Spawns and Validate`, and fix every blocking map-content issue.
5. Point the persistent Large Lad shell at the local scene and test through
   `game_shell.scene`, not a second gameplay bootstrap in the content scene.
6. Publish the scene asset from the Asset Browser through normal s&box map
   publishing. Configure the package page title, summary, thumbnail, and other
   presentation there; these are the published authoritative values.
7. Put the chosen published `organization.ident` into `Published Package Ident`
   and publish that manifest-bearing scene revision. Test by giving the same
   ident to the shell coordinator. `MapInstance` performs its normal automatic
   download, mount, asynchronous load, and unload.

Discovery or a future browser may obtain package idents, but it must hand them
to this catalog/descriptor layer. It must not create another map format or
loader.

## Starting a map

1. Duplicate `template.scene` and give the copy a map-specific name.
2. Keep the scene content-only; it must not contain a gameplay bootstrap.
   In particular, never place `prefabs/large_lad_gameplay.prefab`, a
   `MapInstance`, `LargeLadGameManager`, `LargeLadSessionCoordinator`,
   `LargeLadSpawnAllocator`, or `NetworkHelper` in the content scene. The shell
   supplies exactly one of each. Runtime disables an embedded bootstrap before
   it can recursively load the map and blocks the map with one corrective
   error.
3. Create a map manifest and assign it to the template's map profile.
4. Enter the editor's Mapping mode and build ordinary scene geometry.
5. Place or duplicate the gameplay prefabs from `Assets/Prefabs/Gameplay`.
6. Point the shell bootstrap coordinator's `Startup Map` or
   `Local Development Map` at the new scene.
7. Validate from the Map Profile, then run through `game_shell.scene` and resolve
   every blocking map error plus any relevant mapper warning.

The game manager's prefab defaults are two minimum players, a 0.5-second
player-ready delay, a 10-second head start, a 60-second survival timer, a
5-second intermission, and 5-second Large Lad and other-player respawn delays.
Session-specific tuning belongs on the shell's one bootstrap instance except
for the four explicitly approved manifest overrides above. The current shell
retains Gym's development timing defaults, while the round minimum remains the
supported value of two players. Project/session metadata may still allow a
one-player lobby, but it cannot begin a round.

## Team spawns

Use the three spawn presets:

- `Lobby Team Spawn`
- `Skinny Kid Team Spawn`
- `Hunter Team Spawn`

The hunter group is shared by the Large Lad, converted or respawning Minions,
late joiners, and Large Lad respawns. A spawn component defines a horizontal
circle rather than one exact position:

- `SpawnRadius`: 192 units by default. At the default 48-unit separation this
  is large enough for the deterministic layout to produce all 32 positions.
- `Capacity`: 32 by default on the generic component. The Lobby and Hunter
  presets use 32; the Skinny Kid preset uses 31 because one supported player is
  always the Large Lad.
- `MinimumSeparation`: 48 units by default.

One point per group is enough when its full circle lies above clear floor.
Multiple points may be used for unusually shaped rooms. There are no order
numbers or hand-wired spawn lists. NetworkHelper's generated lobby positions
appear as runtime children of the Lobby Team Spawn that produced them.

The shared spawn-projection service applies the gizmo's deterministic
golden-angle layout, probes downward for the floor, and checks the authoritative
32-by-72 player capsule. It works directly in a content scene with no manager or
allocator. During loaded-map admission, the runtime allocator accepts that same
projection as its cache for NetworkHelper, round batches, and individual
respawns. Batch spawns reserve unique positions, and individual
respawns prefer the valid point farthest from living players. If a circle is
crowded it uses the least-crowded valid point; it never deliberately chooses a
position inside geometry.

There is no shared-origin emergency spawn. Lobby, Skinny Kid, and Hunter
candidate shortfalls are blocking map-contract errors, and a round will remain
in the waiting phase until all three groups provide their required valid cached
positions. An invalid Lobby group also disables NetworkHelper player creation
instead of allowing its default same-transform fallback or an undersized
spawn-point set. Each error reports the authored spawn object's name, configured
capacity, generated valid count, and the geometry or settings to check.

Changing a team spawn's authored properties invalidates the cache
automatically. After changing static floor or wall geometry, select any team
spawn and use `Rebuild Projected Candidates`, or use
`Rebuild Spawns and Validate` on the map profile. The colored gizmos show the
spawn group, configured capacity, current valid projected count, and an
individual clearance warning when the projected count is short.
Editor projection previews are transient and never serialize an allocator or
runtime spawn objects into the content scene. During admission the shell applies
the same projection to its allocator and explicitly rebuilds NetworkHelper's
runtime Lobby points.

The full 32-player map contract requires total configured capacity of 32 for
Lobby, 31 for Skinny Kids, and 32 for Hunter. The colored editor gizmos preview
each circle and its configured capacity. Keep the circles out of walls even
when the component's center is clear.

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
host-synchronized role changes. `LargeLadHealth` and the ordinary melee system
resolve the current role through that player reference, so they do not contain
their own role balance copies. Missing profiles, non-positive movement,
health, scale, or ordinary-melee values, and negative incoming-damage
multipliers report `Large Lad role profiles` validation warnings.

The Large Lad's width is visual only, so routes must fit the normal player
capsule. Useful greybox starting points are a 96-unit comfortable main corridor,
a 64-unit deliberately tight branch, a 72-unit doorway, and at least 96 units
of clear headroom. Leave more room around turns, spawn circles, and melee choke
points.

## Minion passages

Place `Large Lad/Passages/Minion Vent Opening` across every usable entrance or
exit to a Minion-only route. It is a shallow doorway gate, not a second collision
shell through the vent. The prefab has exactly two mapper-facing objects:

- `Minion Vent Opening`: the networked root, component, and one 8-by-64-by-80
  solid collider.
- `Destroyable Vent Cover`: one visual child that can be replaced with the
  finished cover model. It uses a `ModelRenderer`, not an editable prefab
  brush, so prefab refreshes do not create per-instance polygon-mesh blobs.

Duplicate and rotate this prefab for additional openings. The tunnel geometry
itself needs no special collision shell or layer. Loose items cannot enter the
route because dropped weapons, dodgeballs, generic props, and other ordinary
physics bodies remain solid against each opening gate.

The one opening collider handles both gameplay states. With `Enable Breakable
Cover` off, it carries `large_lad_minion_passage`: Minions ignore it while
Skinny Kids, the Large Lad, and unassigned players remain solid. With the cover
enabled and intact, the component temporarily removes that ignore tag from the
same collider so everyone is blocked. Once Minion melee destroys the default
50-health cover, the component restores the tag. The collider never disappears,
so the permanent role restriction remains in place without overlapping blocker
and cover geometry. The shared Hunter tag is retained, so player-vs-player
collision is unchanged.

`Intact Cover Root` points at the `Destroyable Vent Cover` child. Replace its
model without adding any collider to the visual cover hierarchy. The optional
`Cover Prop` supplies model gibs, `Broken Cover Visual` supplies a retained
destroyed state, and the hit/break `SoundEvent` properties are the audio hooks.
Skinny Kid firearms and non-Minion melee never damage this focused cover.
Authoritative health, destruction, gate state, and presentation reset for the
next round.

Resize the root collider and cover child together. Keep the networked root at
scale `(1, 1, 1)` instead of non-uniformly scaling it. The editor gizmo shows
the opening gate bounds. Map validation reports a missing or misplaced root
collider, incorrect tags or project rules, a non-networked root, or an invalid
cover child.

## Barricades

The two presets are:

- `Skinny Progression Barricade`: 300 health; only Skinny Kid melee damages it.
  This lets maps place the first firearms beyond an opening melee barricade
  without allowing those firearms to break later progression barricades.
- `Lad Shortcut Barricade`: 300 health; only the Large Lad's Eat structural
  fallback damages it. Minions can use the route after it opens but cannot
  open it.

Firearms, Minion melee, environmental damage, and unrelated damage types do not
damage either gameplay barricade preset.

A barricade has one authoritative root GameObject using Network Mode `Object`.
The `LargeLadBarricade` component, health, damage acceptance, and one blocking
collider live on that root. The default remains a single renderer/collider
barricade with no stages. The root may instead be a headless controller with
mapper-authored visuals beneath it. An optional compound barricade automatically
treats each direct child GameObject as one breakable piece without adding
another health or destruction owner.

For a custom Scene Mapping barrier:

1. Create and texture the geometry in Mapping mode.
2. Select the resulting mesh GameObject.
3. Add `LargeLadBarricade` to that same object.
4. Set Network Mode to `Object`.
5. Choose `SkinnyProgression` or `LadShortcut`.

Keep this editable mesh scene-local. Do not turn the brush itself into a prefab
instance or move it beneath an unbroken prefab instance root: the editor stores
editable prefab meshes as per-instance polygon blobs, and a prefab refresh or
undo can invalidate that override. A reusable barricade prefab should instead
keep a stable root collider and use model-backed renderer children. The supplied
Lad Shortcut and Minion Vent prefabs follow that layout. New Skinny Progression
placements automatically break their prefab link because that preset is a
scene-local mapping template. For an older connected Skinny Progression
placement, use `Break Prefab` before adding an editable brush beneath it.

The component automatically uses a same-object `MeshComponent` or collider as
the authoritative blocker. There is no renderer assignment: same-object mesh
and renderer components are detected internally for the simple workflow, while
a generic compound prefab can keep its controller root
headless. Destruction announcements are off by default. A mapper may opt in on
a `SkinnyProgression` barricade with Announce Destruction, then provide the
required short Display Name. Final destruction broadcasts only
`<Display Name> destroyed.` It never adds a position, direction, route, marker,
outline, or other world-location detail. `LadShortcut` barricades and unrelated
breakable or decorative objects never use this announcement.

For an optional compound barricade:

1. Keep the authoritative blocker assigned to `BarricadeCollider`.
2. Put each breakable piece in its own direct child GameObject. Their hierarchy
   order is their break order. Nested objects are part of their nearest direct
   child piece.
3. Give a child a `Prop` component only when that piece should use a model with
   authored gibs. A renderer-only child is equally valid: it disappears at its
   break point and produces no model gibs.
4. Add cumulative Compound Stages and give each one a unique Remaining Health
   Fraction strictly between 0 and 1. Fractions are used so round health scaling
   does not move the visual break points.
5. Set Child Objects To Break to the number of next intact direct children that
   stage should destroy. Any children still intact are destroyed at zero health.

All child objects, props, and rigidbodies are frozen automatically while intact.
When a stage or final destruction reaches a child, the host creates its model
gibs, retains about 30% of those pieces for barricade debris, and the authored
child disappears immediately. Retained barricade gibs stay solid against the
map and ordinary props but ignore player collision entirely, so they cannot
block movement. This reduction and player-collision exception are local to
barricades; other Prop systems keep their normal gib counts and collision. The
authored child itself is retained
invisibly so round reset can restore it; it never becomes a loose, fully
networked physics crate. The authoritative blocker remains solid through all
ordinary stages and is disabled at zero health. Opening passage before zero
requires the separate Enable Early Passage option plus its own valid Remaining
Health Fraction; leaving that option off cannot open passage accidentally.
Missing or duplicate stage thresholds, negative child counts, and stage counts
that exceed the available direct children are map-validation errors.

Round reset removes every surviving barricade gib, then restores the authored
health, blocker state, child enabled states, local transforms,
static/anchored/physics-motion state, and active-stage count.
`AuthoritativeDestroyed` is a host-only, once-per-round event that future
spawn-stage code can subscribe to without putting spawn behavior in the
barricade.

## Pickups and hazards

The gameplay prefab folder contains Pistol, SMG, Shotgun, Rifle, and
kill-volume presets. Loose ammunition pickups are not part of the map contract.

Only Skinny Kids own native inventory items: the crowbar, firearms, and the
single dodgeball utility slot. Large Lad and Minion attacks remain role
abilities, and neither role can collect or select the dodgeball.

The Large Lad's only primary attack is committed Eat. The host searches for a
valid living Skinny Kid first; that victim takes priority even when an eligible
structural fallback is closer. Once accepted, Eat runs its short committed
sequence and lethally executes the victim at completion. Ordinary damage,
including otherwise-lethal damage and environmental hazards, cannot interrupt
the accepted victim's sequence. Killing the Large Lad cancels it. A successful
execution heals the Large Lad by the configured percentage of health currently
missing, exactly once.

If no valid Skinny Kid is caught, the same primary input may apply its
configured structural damage to an eligible `Lad Shortcut` barricade or a
mapper-authored `LargeLadEatSmashable`. Generic props and other damageable
objects are not fallback targets. Ground Slam is the Large Lad's secondary
attack; it is documented separately below.

Skinny Kids and Minions retain their ordinary primary melee attacks. Holding
primary attack auto-swings at the authoritative role-profile cooldown:

| Role | Damage | Range | Cooldown | Fallback aim assist |
| --- | ---: | ---: | ---: | --- |
| Skinny Kid | 25 | 80 | 0.65 seconds | On |
| Minion | 25 | 80 | 0.5 seconds | On |

Both ordinary melee roles use an 18-unit swing-trace radius. Fallback aim
assist requires a minimum facing dot of 0.55.

Skinny Kids select the native crowbar before core firearms ordered by their
native prefab `SlotOrder`, then any carried exclusive firearm, then the native
dodgeball item. The exclusive therefore remains the final firearm. Minions use
ordinary role melee because they have no native item inventory. The Large Lad
also has no item inventory, and primary input is routed exclusively to Eat
rather than the ordinary melee system.

## Large Lad Ground Slam

The Large Lad uses Secondary Attack (right mouse or left trigger) for Ground
Slam. The player prefab owns the configurable cooldown, brief windup, radial
range, Skinny Kid stagger duration, and separate horizontal/upward impulses for
Skinny Kids and Minions. The host completes the windup and decides every hit.
Walls, floors, and blocking geometry stop the radial visibility trace, so a
player or prop on the other side of geometry is not affected.

Ground Slam is zero-damage crowd control. A visible living Skinny Kid in range
receives the configured physics impulse and briefly loses movement input without
losing the impulse's velocity. A visible living Minion can receive the
separately tuned friendly impulse, but never takes friendly damage or stagger.
Replicated windup, impact, camera/audio-feedback, cooldown-started, and
cooldown-ready presentation events are available on `LargeLadGroundSlam`; those
events cannot add targets or apply gameplay effects.

Ground Slam and reactive-prop diagnostic logging default to off. The options
remain available for a mapper to enable temporarily while tracing a focused
multiplayer problem.

Ordinary physics bodies never react: only explicitly authored props are
eligible. To opt in, add `LargeLadGroundSlamReactiveProp` directly to a
collidable model or `Prop` and choose one behavior. The component automatically
makes its GameObject a network object before play. A plain collidable model also
receives an automatic Rigidbody for `Move` or `Unanchor`; `Prop` keeps using its
own generated physics.
`Start Frozen` is enabled by default, holding that physics body at its authored
transform until its first slam. Disable it for an already-live dodgeball.

- `Move` applies the bounded configured horizontal/upward impulse to an already
  dynamic Rigidbody. This is the intended dodgeball configuration and never
  destroys it.
- `Unanchor` changes the mapped prop to dynamic, then applies the bounded
  impulse.
- `Break` creates Prop gibs when available and disables the mapped presentation
  and collision until reset. It is not a damage event.

The optional `Reactive Root` may point to a child containing the visuals and
physics while the networked mapper component remains active on its parent.
Critical gameplay objects, firearm pickups, spawns, kill volumes, Eat
smashables, barricades, and Minion-passage blockers are rejected even if someone
adds the component. The dodgeball utility pickup is the focused exception: it
may react while available on the ground, but never while carried. Round reset
restores the authored local transform, object/component enabled state,
static/anchored state, Rigidbody state, and broken state.

Out-of-bounds cleanup is opt-in. It disables a lost prop until round reset when
the prop falls below the configured world Z or exceeds the configured distance
from its authored position. This cleanup does not clear vents or protected
passages; the protected-passage clearance system remains responsible for an
object entering those routes.

Pickup policy is configured on each `LargeLadWeaponPickup` placement, not in the
weapon catalog. This lets the same firearm be a core pickup on one route
and an exclusive physical instance elsewhere.

All five Core firearm prefab defaults use `Pickup Policy (Per Instance): Core`.
Each living Skinny Kid may unlock the pickup independently. Core pickups never
hide, duplicate touches do nothing, magazines are retained per weapon, and
reserve ammunition is explicitly infinite. The HUD displays `magazine / ∞`.
Core weapons cannot be dropped and are cleared on conversion or round reset.

For a limited physical weapon, set that placement's
`Pickup Policy (Per Instance)` to `Exclusive` and keep its root at Network Mode
`Object`. Every authored placement creates one persistent native firearm from
the catalog's `NativePrefabPath`, including multiple placements of the same
weapon id. Its native prefab supplies `ClipMaxSize` and
`ExclusiveStartingReserve`; reserve is not configured independently per map
placement, and there are no ammo refills.

The authored Exclusive pickup is only an invisible origin/reset marker. Its one
native firearm supplies the visible world model and pickup trigger at the
origin, while dropped, and while transferring between carriers. Press the drop
input (G by default) while selected to drop that same native item near the
player. Magazine and reserve remain on it through switching, dropping,
repicking, transfer, death, and disconnect. A Skinny Kid who already carries one
receives local HUD feedback and cannot replace it. Round reset returns that same
item to its authored origin and alone restores the prefab's full magazine and
`ExclusiveStartingReserve`.

Skinny Kid starting loadouts are configured as a list of core weapon ids on
`LargeLadNativeInventory`; they are not numeric slots. The HUD, direct slots,
and scroll order are native crowbar, native-prefab `SlotOrder` within the Core
bucket, the carried exclusive firearm, and then the native slot-3 utility item.
The shipped starting Core list remains empty: maps grant firearms through their
pickups rather than spawning every player with the full arsenal. If a map opts
into starting weapons, Core ordering is always Pistol, SMG, Shotgun, then Rifle.

Author the utility with the `Large Lad/Pickups/Dodgeball` prefab. Each placement
is exactly one stable physical ball and each Skinny Kid has one native utility
slot. A thin `LargeLadDodgeballItem` in that slot owns carried state and
selection; the authored pickup remains the only physical ball.
The prefab already supplies the visible model, solid ball collider, separate
pickup trigger, Rigidbody with enhanced continuous collision detection, the
`large_lad_dodgeball` vent-blocking tag, Network Mode `Object`, interpolation,
host-safe orphan handling, and a Move-only Ground Slam reaction. The solid ball
uses a touch-only response against Skinny Kid bodies, so it cannot physically
deflect off them while the separate trigger still receives pickup overlaps after
its cooldown; Large Lad and Minion bodies remain solid targets. Do not replace
its solid collider with a trigger or change the slam behavior to Unanchor or
Break.

The authored object hides while carried. Primary Attack throws it from a
clearance-tested point beyond the carrier; G manually drops it at a safe nearby
position. Both paths reclaim host physics, cap linear/angular velocity, apply a
pickup cooldown, and preserve the same instance identity. Ownership may move to
the carrier while hidden, but world physics and hit decisions always return to
the host. The launch offset plus thrower grace prevents self contact from
consuming a throw. Death, role change, and disconnect safely drop the carried
ball or return it to origin when no safe drop exists. Map transition returns it
to its departing authored source. Round reset invalidates stale throw tokens,
clears motion and interpolation, and restores exactly the authored object; the
dodgeball path never creates a runtime copy.

A thrown ball becomes harmless on its first non-self impact. A direct living
Minion hit consumes all current Minion health. A direct living Large Lad hit
applies configured bounded knockback horizontally away from the thrower without
a movement lock or stun, plus zero damage by default (configurable only from 0
through 5). Friendly, dead, low-speed, world, stale, and replayed impacts cannot
produce a combat effect.
Ground Slam may impulse an available ball, but runtime validation prevents
breaking, unanchoring, or cleanup. The component exposes distinct replicated
presentation phases for Throw, Impact, Pickup, MinionKill, and LargeLadHit.

A kill volume is an ordinary GameObject with a trigger collider and
`LargeLadKillVolume`. Resize the collider to cover the hazard. Skinny Kids killed
by it become Minions, Minions use their normal respawn, and the Large Lad uses
the Large Lad respawn timer.

## Preflight checklist

- Exactly one gameplay bootstrap exists in `game_shell.scene`; the map-content
  scene has none.
- Exactly one enabled `LargeLadMapProfile` exists in the content scene, it
  references the intended `.llmap`, and its stable id, loader identifier,
  contract version, presentation metadata, recommended range, and optional
  approved balance values pass validation.
- The shell has one coordinator on the gameplay bootstrap and one separate
  root-level Snapshot Map Content Host/`MapInstance`, and map readiness is
  confirmed before round testing.
- At least one team-spawn component exists for each group.
- Configured capacities meet 32 Lobby, 31 Skinny Kid, and 32 Hunter.
- Spawn circles produce clear floor positions and do not cross walls.
- Every barricade has an authoritative root collider, uses Network Mode
  `Object`, and keeps compound `Prop` pieces as direct children in their break
  order. Root rendering is optional. Reusable barricade and vent prefabs contain
  no editable `MeshComponent`; custom mapping brushes remain scene-local.
- Every weapon pickup has a deliberate per-instance policy, visible model, and
  trigger collider; exclusive pickups use Network Mode `Object`.
- Every dodgeball placement uses the supplied prefab with its solid ball
  collider, separate pickup trigger, Rigidbody, dodgeball collision tag,
  interpolation, and Move-only non-cleanup Ground Slam configuration intact.
- Every Ground Slam-reactive prop is an intentional component opt-in with a
  valid Move/Unanchor/Break behavior and no critical gameplay or
  authoritative-blocker component in its hierarchy. Its Network Mode `Object`
  setting is automatic.
- Every kill volume has a trigger collider.
- The Map Profile reports zero blocking errors; every remaining warning has been
  reviewed and is intentional.
- Host and remote clients complete a round, intermission, reset, late join,
  conversion, and respawn test.

Skinny Kids win only by surviving the timer. Route progress, weapon pickups, and
destroying barricades do not change that rule.
