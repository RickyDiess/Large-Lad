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
gibs and the authored child disappears immediately. The authored child itself
is retained invisibly so round reset can restore it; it never becomes a loose,
fully networked physics crate. The authoritative blocker remains solid through
all ordinary stages and is disabled at zero health. Opening passage before zero
requires the separate Enable Early Passage option plus its own valid Remaining
Health Fraction; leaving that option off cannot open passage accidentally.
Missing or duplicate stage thresholds, negative child counts, and stage counts
that exceed the available direct children are map-validation errors.

Round reset restores the authored health, blocker state, child enabled states,
local transforms, static/anchored/physics-motion state, and active-stage count.
`AuthoritativeDestroyed` is a host-only, once-per-round event that future
spawn-stage code can subscribe to without putting spawn behavior in the
barricade.

## Pickups and hazards

The gameplay prefab folder contains pistol, SMG, and kill-volume presets. Loose
ammunition pickups are not part of the map contract.

Only Skinny Kids have firearm inventory and the single dodgeball utility slot.
Primary attacks remain role abilities rather than firearm entries. Large Lad
and Minions cannot collect or select the dodgeball.

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

Skinny Kids select ordinary melee before the catalog-ordered core firearms,
then any carried exclusive firearm, then the separately categorized dodgeball.
The exclusive therefore remains the final firearm. Minions always have ordinary
melee selected because they have no firearm or utility inventory. The Large Lad
also has neither inventory, and primary input is routed exclusively to Eat
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
reserved. Press the drop input (G by default) while it is
selected to place a runtime pickup near the player. Magazine and reserve values
survive switching, dropping, repicking, transfer, death, and disconnect. A
Skinny Kid who already carries one receives local HUD feedback and cannot
replace it. Round reset destroys any runtime drop, restores the authored pickup,
and alone restores a full magazine plus the catalog-defined `StartingReserve`.

Skinny Kid starting loadouts are configured as a list of core weapon
definitions on `LargeLadInventory`; they are not numeric slots. The HUD, direct
slots, and scroll order are role melee, stable weapon-catalog core order, the
carried exclusive firearm, and then the separately synchronized utility.

Author the utility with the `Large Lad/Pickups/Dodgeball` prefab. Each placement
is exactly one stable physical ball and each Skinny Kid has one utility slot.
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

- Exactly one gameplay bootstrap exists.
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
- The scene reports no `Map contract:` warnings.
- Host and remote clients complete a round, intermission, reset, late join,
  conversion, and respawn test.

Skinny Kids win only by surviving the timer. Route progress, weapon pickups, and
destroying barricades do not change that rule.
