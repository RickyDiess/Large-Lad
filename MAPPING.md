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

The bootstrap contains `NetworkHelper`, `LargeLadSpawnAllocator`, and one
`LargeLadGameManager`. The game manager is the single owner of round state,
transitions, minimum-player rules, timing, bootstrap references, and map
validation. Its defaults are two minimum players, a 0.5-second player-ready
delay, a 10-second head start, a 60-second survival timer, a 5-second
intermission, and 5-second Large Lad and other-player respawn delays.

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

After scene initialization the allocator applies the gizmo's deterministic
golden-angle layout, probes downward for the floor, checks the 32-by-72 player
capsule, and caches the valid projected positions for each authored area and
group. Map validation, NetworkHelper, round batches, and individual respawns all
reuse that same cache. Batch spawns reserve unique positions, and individual
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
`Rebuild And Validate Spawn Candidates` on the game manager. The colored
gizmos show the cached projected capsules when an allocator is available.
Ordinary validation, allocation, candidate-count, and gizmo reads only ensure
the data cache; runtime NetworkHelper GameObjects are refreshed explicitly by
initial configuration or either mapper rebuild command.

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
host-synchronized role changes. `LargeLadHealth` and `LargeLadMeleeCombat`
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

- `Skinny Progression Barricade`: 300 health; only Skinny Kid melee damages it.
  This lets maps place the first firearms beyond an opening melee barricade
  without allowing those firearms to break later progression barricades.
- `Lad Shortcut Barricade`: 300 health; only Large Lad melee damages it.
  Minions can use the route after it opens but cannot open it.

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

The component automatically uses a same-object `MeshComponent` or collider as
the authoritative blocker. There is no renderer assignment: same-object mesh
and renderer components are detected internally for the legacy simple
workflow, while a generic compound prefab can keep its controller root
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
3. Give each gib-producing child a `Prop` component and a model with authored
   gibs. A renderer-only child is still removed at its break point, but cannot
   generate model gibs.
4. Add cumulative Compound Stages and give each one a unique Remaining Health
   Fraction strictly between 0 and 1. Fractions are used so round health scaling
   does not move the visual break points.
5. Set Child Objects To Break to the number of next intact direct children that
   stage should destroy. Any children still intact are destroyed at zero health.

All child objects, props, and rigidbodies are frozen automatically while intact.
When a stage or final destruction reaches a child, the host creates its model
gibs and the authored child disappears immediately. The authored child itself
is retained invisibly so round reset can restore it; it never becomes a loose,
fully networked physics crate. The authoritative blocker remains solid through
all ordinary stages and is disabled at zero health. Opening passage before zero
requires the separate Enable Early Passage option plus its own valid Remaining
Health Fraction; leaving that option off cannot open passage accidentally.
Missing or duplicate stage thresholds, negative child counts, excessive stage
counts, and gib-producing children without a `Prop` are map-validation errors.

Round reset restores the authored health, blocker state, child enabled states,
local transforms, static/anchored/physics-motion state, and active-stage count.
`AuthoritativeDestroyed` is a host-only, once-per-round event that future
spawn-stage code can subscribe to without putting spawn behavior in the
barricade.

## Pickups and hazards

The gameplay prefab folder contains pistol, SMG, and kill-volume presets. Loose
ammunition pickups are not part of the map contract.

Only Skinny Kids have firearm inventory. Large Lad and Minions use their
built-in melee role ability and never receive firearm entries. Holding primary
attack auto-swings that role ability at the authoritative role-profile
cooldown:

| Role | Damage | Range | Cooldown | Fallback aim assist |
| --- | ---: | ---: | ---: | --- |
| Skinny Kid | 25 | 80 | 0.65 seconds | On |
| Large Lad | 10 | 100 | 0.1 seconds | Off |
| Minion | 25 | 80 | 0.5 seconds | On |

All roles use an 18-unit swing-trace radius. Fallback aim assist requires a
minimum facing dot of 0.55. Maintaining roughly one second of accurate Large
Lad contact drains a full-health Skinny Kid and converts them on death.

Melee remains a role ability rather than a firearm entry. Skinny Kids can select
their role melee before the catalog-ordered core weapons; Large Lad and Minions
always have built-in melee selected because they have no firearm inventory. In
first-person mode every active role receives overlay-rendered melee arms
bone-merged to the Citizen body. The optional Skinny Kid melee model remains
presentation-only; Large Lad and Minions attack unarmed.

Pickup policy is configured on each `LargeLadWeaponPickup` placement, not in the
weapon catalog. This lets the same pistol or SMG be a core pickup on one route
and an exclusive physical instance elsewhere.

The pistol and SMG prefab defaults use `Pickup Policy (Per Instance): Core`.
Each living Skinny Kid may unlock the pickup independently. Core pickups never
hide, duplicate touches do nothing, magazines are retained per weapon, and
reserve ammunition is explicitly infinite. The HUD displays `magazine / ∞`.
Core weapons cannot be dropped and are cleared on conversion or round reset.

For a limited physical weapon, set that placement's
`Pickup Policy (Per Instance)` to `Exclusive` and keep its root at Network Mode
`Object`. Every authored placement creates its own instance, including multiple
placements with the same weapon definition. An exclusive placement supplies
one full magazine plus the finite reserve defined by that weapon catalog
entry's `StartingReserve` for the round. Reserve ammunition is not configured
independently per pickup placement, and there are no ammo refills.

While an exclusive is carried or dropped, its authored pickup stays hidden and
reserved. Press the `Drop Exclusive Weapon` input (G by default) while it is
selected to place a runtime pickup near the player. Magazine and reserve values
survive switching, dropping, repicking, transfer, death, and disconnect. A
Skinny Kid who already carries one receives local HUD feedback and cannot
replace it. Round reset destroys any runtime drop, restores the authored pickup,
and alone restores a full magazine plus the catalog-defined `StartingReserve`.

Skinny Kid starting loadouts are configured as a list of core weapon
definitions on `LargeLadInventory`; they are not numeric slots. The HUD and
scroll order use stable weapon-catalog order with the carried exclusive last.

A kill volume is an ordinary GameObject with a trigger collider and
`LargeLadKillVolume`. Resize the collider to cover the hazard. Skinny Kids killed
by it become Minions, Minions use their normal respawn, and the Large Lad uses
the Large Lad respawn timer.

## Preflight checklist

- Exactly one gameplay bootstrap exists.
- At least one team-spawn component exists for each group.
- Configured capacities meet 32 Lobby, 31 Skinny Kid, and 32 Hunter.
- Spawn circles produce clear floor positions and do not cross walls.
- Every barricade has an authoritative root collider, uses Network Mode
  `Object`, and keeps compound `Prop` pieces as direct children in their break
  order. Root rendering is optional.
- Every weapon pickup has a deliberate per-instance policy, visible model, and
  trigger collider; exclusive pickups use Network Mode `Object`.
- Every kill volume has a trigger collider.
- The scene reports no `Map contract:` warnings.
- Host and remote clients complete a round, intermission, reset, late join,
  conversion, and respawn test.

Skinny Kids win only by surviving the timer. Route progress, weapon pickups, and
destroying barricades do not change that rule.
