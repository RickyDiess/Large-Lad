# Running the automated tests

Large Lad's deterministic gameplay tests live in `UnitTests/` and use the
s&box-generated MSTest project. They do not start a multiplayer session or load
a gameplay scene. Capacity coverage verifies the 2-player minimum, the former
16-player boundary, the 31-player Skinny Kid maximum, and the complete
32-player roster across spawn requirements, deterministic layouts, and batch
collections.

When setting up the tests for the first time:

1. Open the project in the s&box editor. If the editor was already open when
   `UnitTests/` was added, restart it once so s&box generates the unit-test
   project.
2. From this directory, run:

   ```text
   dotnet test UnitTests/large_lad.unittest.csproj
   ```

The tests can also be run from Visual Studio's Test Explorer after the generated
unit-test project appears in the solution.

## Timer-only Hunter movement escalation

During the survival phase, the host's replicated start time and phase deadline
define normalized elapsed round time. Hunter movement stays at the role-profile
baseline through the first 60% of the round, then smoothsteps to 1.10x for the
Large Lad and 1.15x for Minions at the deadline. The ramp interval and both
maximums are configurable on the gameplay manager. Outside the survival phase
the modifier is exactly neutral; barricades, map progress, roster changes,
conversions, and remaining Skinny Kids are not inputs. Minion respawn delay is
unchanged.

## Fixed round-balance bands

The host selects a band from the number of Skinny Kids only after a round has
passed its start checks and spawn allocation. That selection remains fixed
through disconnects, deaths, Skinny Kid-to-Minion conversions, and late joins.
The next successful round start replaces it.

The configurable defaults live in
`Assets/Gameplay/default_round_balance.llbalance`:

| Skinny Kids | Band | Large Lad max health | SkinnyProgression barricade max health |
| --- | --- | ---: | ---: |
| 1–3 | Small | 0.9× | 0.9× |
| 4–7 | Medium (neutral baseline) | 1.0× | 1.0× |
| 8–15 | Large | 1.1× | 1.1× |
| 16–23 | Very Large | 1.2× | 1.2× |
| 24–31 | Full | 1.3× | 1.3× |

These values are provisional. Maximum health is always recalculated from its
authored baseline, and an optional map-specific health multiplier composes
multiplicatively with the fixed band instead of replacing or compounding it.
No damage, movement, range, Minion, timer, regeneration, or respawn values are
part of this system.

## 32-player map-capacity regression checklist

1. Confirm the project advertises a 2-player minimum and 32-player maximum.
2. Validate a map with the standard presets and confirm it requires 32 Lobby,
   31 Skinny Kid, and 32 Hunter positions.
3. Lower each group's authored capacity in turn. Confirm validation names the
   affected group and authored spawn objects and reports configured versus
   required capacity.
4. Restore capacity, then obstruct enough floor positions to make generated
   capacity insufficient. Confirm validation reports generated versus required
   positions for the group and each authored area, and Lobby failure disables
   player creation instead of stacking players.
5. Rebuild projected candidates after clearing the obstruction. Confirm a
   32-player Lobby batch and a 1 Hunter plus 31 Skinny Kid round-start batch
   allocate unique cached positions.

## Batch C multiplayer lifecycle regression checklist

Use a host plus one remote client in `Gym.scene` for engine/network lifecycle
coverage that the pure rule tests cannot provide:

1. Duplicate the `Large Lad Gameplay Bootstrap` object. Confirm one blocking
   error names both bootstrap objects, no manager advances its phase, and neither
   `NetworkHelper` creates players. Remove or disable the duplicate and confirm
   the original bootstrap hydrates existing registrations and resumes normally.
2. During `HeadStart` and again during `Playing`, disable and re-enable the sole
   game manager with both teams alive. Confirm the round does not end during
   hydration and `CurrentLargeLad` plus all role indexes are restored.
3. Kill a Skinny Kid, then disable/re-enable the player component and the game
   manager before its countdown expires. Confirm it remains dead, retains Minion
   as its pending role, finishes the synchronized countdown, and respawns at a
   Hunter spawn.
4. Repeat step 3 for the Large Lad and a Minion, including one environmental
   death. Confirm firearm, melee, and environment deaths all enter the same
   lifecycle once, with the authored ragdoll policy and respawn delay preserved.
5. Force a lethal hit while the bootstrap is duplicated. Confirm the rejected
   handoff restores positive health and does not leave `CurrentHealth == 0` with
   `IsDead == false`; remove the duplicate and confirm a later lethal hit commits.
6. Observe changed-role Skinny Kid-to-Minion conversion on both host and remote
   owner, then a same-role Minion respawn. Confirm movement and body presentation
   match the role profile in both cases, including for a late joiner. Confirm the
   inventory is prepared once per `RespawnAs` (no duplicate core grants and no
   firearm entries for Large Lad or Minions).

## Role-based player collision multiplayer checklist

The deterministic tests cover every approved role pairing, lobby/reset tag
selection, collision symmetry, the Skinny Kid soft-response falloff and vertical
cutoff, preservation of existing impulse velocity, and a complete 32-Hunter
overlap matrix. Start a fresh game session so the project collision matrix is
reloaded before running the checks below. These still require live s&box physics
and network ownership:

1. With a host and remote owner, overlap Large Lad with a Minion and two Minions
   with each other. Confirm they can separate under input without blocking,
   jittering, launching, moving either camera, or making either character step
   upward or lift its feet. Inspect both the player root and the controller's
   live `ColliderObject.Tags`; confirm `player` and
   `large_lad_hunter_body` are present on both.
2. Walk Large Lad and a Minion into a Skinny Kid from both peers. Confirm each
   pairing remains fully solid without the camera being pushed. Then walk two
   Skinny Kids through each other from both ownership directions. Confirm they
   can pass, receive only a light horizontal nudge, never gain vertical velocity,
   and do not move either camera or disturb foot placement. Crowd several Skinny
   Kids together and confirm the same response does not form an immovable wall.
3. Kill a Skinny Kid and observe the synchronized Skinny Kid-to-Minion role
   change on host and owner. Confirm the player uses Hunter contact rules on the
   first live respawn frame and both root and live collider tags change from
   `large_lad_soft_player_body` to `large_lad_hunter_body`. Repeat a same-role
   Minion respawn and verify `CameraCollisionIgnore` still contains `player`.
4. End the round and start the next one. Confirm lobby players and newly assigned
   Skinny Kids regain the pass-through soft response immediately, while Large
   Lad and Minions regain Hunter contact rules.
5. At 32 players, convert or late-join enough Minions to fill the Hunter spawn
   group. Respawn several simultaneously and deliberately force a fallback onto
   an occupied Hunter candidate. Confirm no spawn blockage, physics explosion,
   camera movement, false step-up, or lasting overlap trap; distinct cached
   candidates should still be used when available.
6. Apply a direct Rigidbody impulse to a live Minion from its owning peer (the
   same path a future Ground Slam should use). Confirm the Minion moves even
   while overlapping Large Lad or another Minion, and confirm collision
   filtering never disables Rigidbody motion.

## Eat multiplayer checklist

Use a host, the Large Lad owner, a remote Skinny Kid victim, and another remote
client able to deal ordinary damage. Set the Large Lad below maximum health and
note the configured Missing-health Heal Fraction before testing completion.

1. Press Primary Attack with a valid Skinny Kid and an eligible Lad Shortcut
   barricade or explicitly authored Eat smashable both in range. Confirm the
   Skinny Kid is selected even when the structure is closer. Remove every valid
   Skinny Kid and confirm the same primary input can damage either eligible
   structural fallback, while an ordinary prop remains unchanged. Confirm the
   Large Lad never performs an ordinary player-damaging melee swing.
2. Accept an Eat on the remote Skinny Kid, then apply firearm or ordinary melee
   damage that would otherwise be lethal before the committed duration ends.
   Confirm the victim loses no health from that damage, the accepted Eat is not
   interrupted, and the Eat still executes lethally after its full sequence.
3. Repeat with the active victim entering a kill volume. Confirm the explicit
   environmental execution immediately wins the lethal edge once, cancels the
   Eat, grants no Eat healing, and retains the environmental cause. Separately
   apply ordinary nonlethal environmental/impact damage and confirm it remains
   rejected during the commitment so the accepted Eat can complete normally.
4. During separate accepted Eats, kill the Large Lad once with ordinary damage
   and once with an environmental hazard before completion. Confirm each Eat is
   cancelled, the victim is released alive, and no execution or healing occurs.
5. Complete a successful Eat from the noted Large Lad health. Confirm exactly
   one victim lethal transition/conversion and exactly one heal occur on the
   host and are observed by every remote client. The final health must equal
   `old health + (maximum health - old health) * configured fraction`; waiting,
   replaying presentation, and repeated cleanup must not execute or heal again.

## Batch H firearm hit-region and environmental-execution checklist

Use a host, a remote Skinny Kid shooter, two Minions, and a test area with solid
world geometry. Repeat the shooter checks once with the host as shooter and once
with the remote client as shooter. Enable `EnableFireDebug` only on the active
shooter's `LargeLadPrototypeWeapon`; confirm the eye trace and classification
overlays disappear again when it is disabled.

1. At close, medium, and long firearm range, shoot a stationary Minion at the
   center and edge of the visible head, the neck, and the upper torso. Repeat
   from the front, side, and an elevated angle while the Minion is standing and
   crouched. Confirm only a logged same-target `head` tag or recognized head-bone
   hitbox produces `Head`; edge misses, neck, and upper torso remain body hits.
2. Repeat step 1 while the Minion walks, runs, changes direction, crouches, and
   plays normal movement animations. Confirm the movement capsule/box can appear
   as the first authoritative hit without masking a same-ray model-head hitbox,
   and confirm no world-height, camera-height, origin-distance, or upper-torso
   shortcut produces a headshot.
3. Partially obscure a Minion behind solid world geometry. Test a visible head,
   a hidden head with visible torso, and a fully hidden target. Confirm the
   first world obstruction remains authoritative, the bounded classification
   cannot promote a hitbox beyond it, fully hidden targets take no damage, and a
   hidden head never turns a visible body hit into a headshot.
4. Align two Minions on the same eye-origin ray. Exercise a body hit on the
   nearer Minion with the farther Minion's head behind it, then a valid head hit
   on the nearer Minion. Confirm the nearer authoritative victim is the only
   selected target, the farther head never promotes or receives the shot, and
   geometry behind either player is never bypassed.
5. For every accepted miss, body hit, headshot, and lethal Minion headshot,
   compare the firearm debug record with the visible result. It must contain the
   shot sequence, camera-selected point, eye start/direction, first object and
   component, Collider/Hitbox presence, bone and tags, selected target, final
   region, and obstruction/rejection reason. Confirm one trigger shot consumes
   one sequence and produces at most one player damage event, one owner feedback
   result/hitmarker, and one lethal transition; replaying the same sequence must
   produce none of those again.
6. Make one Skinny Kid the Last Skinny Kid and apply a known amount of ordinary
   environmental/impact damage; confirm the approved 50% reduction still
   applies. Enter a kill volume once and confirm it consumes all remaining
   health immediately, reports the environmental cause, skips Eat/firearm
   attribution, and creates exactly one lethal transition without requiring a
   repeated trigger. Repeat with a non-last Skinny Kid to confirm ordinary
   environmental damage is unreduced and kill-volume execution remains lethal.
7. Complete an Eat against the Last Skinny Kid and confirm it still consumes all
   current health once with the Eat cause. In a separate attempt, move the
   committed victim into a kill volume and confirm the environmental execution
   wins once, cancels Eat, and produces no Eat heal or second lethal transition.

## Ground Slam multiplayer checklist

Use a host, the Large Lad owner, a remote Skinny Kid, and a remote Minion.
Ground Slam diagnostics are disabled on the player prefab for normal play, and
reactive-prop logging defaults off on mapper components. Turn either option on
temporarily only while tracing a specific multiplayer problem.

An accepted activation consumes its authoritative cooldown at the start of the
windup. Death, role/phase changes, Eat conflicts, component teardown, or another
windup cancellation suppress the impact and clear all pending presentation,
including the ready cue, but do not refund or restart that accepted cooldown.

1. Attempt Secondary Attack while role, round phase, health, movement, or Eat
   state makes Slam invalid. Confirm rejection shows no cooldown HUD and the
   owner can try again. Then make the state valid, press Secondary Attack, and
   confirm the owner countdown starts only after host acceptance and matches
   the host's remaining cadence.
2. On both the host and each remote client, confirm the accepted windup animation
   and windup sound occur, followed exactly once by the impact sound, local
   dirt/debris particles, and camera shake. Check cameras at the impact, midway
   to the configured Feedback Radius, exactly at the boundary, and beyond it;
   screenshake must weaken with distance and be zero at and beyond the boundary.
   Confirm no target marker, direction cue, outline, pulse, or other gameplay
   reveal is present.
3. Confirm only the owning Large Lad receives the cooldown HUD and ready sound.
   Observers must receive neither. Spam and replay requests during the
   configured cooldown and confirm the host accepts no early activation and no
   peer receives a duplicate impact.
4. During separate accepted windups, kill the Large Lad, end the round, change
   its role, start Eat, disable the Slam component, and transition scenes.
   Confirm each case produces no impact, stops any windup cue, clears the owner
   cooldown and ready HUD, and never emits a delayed cooldown-ready sound or
   stale camera feedback. Trying again before the accepted cadence expires
   remains host-rejected; after it expires, a valid request can be accepted
   normally.
5. Put a living Skinny Kid inside the radius with clear line of sight. Confirm
   the impact applies the upward/radial impulse, briefly suppresses movement
   input without clearing velocity, deals no damage, and restores movement at
   the configured stagger deadline.
6. Repeat with the Skinny Kid beyond range, dead, behind a wall, across a floor
   on another level, and behind blocking map geometry. Confirm no impulse or
   stagger occurs. Confirm changing the configured radius changes the accepted
   boundary.
7. Put a Minion in visible range. Confirm the separately configured friendly
   impulse can move the Minion, but health never changes and movement is not
   staggered.
8. Place one ordinary Rigidbody and three otherwise identical collidable models
   or Props. Attach only `LargeLadGroundSlamReactiveProp`, select Move,
   Unanchor, and Break, then save and reopen the scene. Confirm each mapped root
   was automatically set to Network Mode Object on the host and remote client.
   With Start Frozen enabled, confirm the mapped props remain at their authored
   transforms until hit. Confirm the ordinary body does nothing, Move releases
   and remains intact, Unanchor becomes dynamic, and Break disables only its
   mapped prop state. Put each behind geometry and confirm it does not react.
   After exercising all three behaviors, join a new remote client and confirm it
   sees the current moved, unanchored, and broken states without replaying the
   Slam.
9. Configure a dodgeball stand-in as Move. Confirm repeated slams move but never
   destroy it. Attempt to add the mapper to a pickup, spawn, barricade, Eat
   smashable, kill volume, or Minion-passage gate and confirm map validation
   rejects it and runtime slam leaves it unchanged.
10. End the round after moving, unanchoring, breaking, late joining, and cleaning
   up props. Confirm reset restores authored transform, enabled state,
   anchored/static state, Rigidbody state, and break state on all peers,
   including the late joiner.
11. Enable out-of-bounds cleanup and move a mapped prop below Minimum World Z and
   beyond Maximum Distance From Start. Confirm it stays cleaned up until reset.
   Push one toward a Minion vent and confirm this generic cleanup does not claim
   vent clearance; protected-passage clearance remains responsible there.

## Minion passage multiplayer checklist

Start a fresh session after changing `ProjectSettings/Collision.config`, because
the project collision matrix is loaded at startup.

1. Place `Large Lad/Passages/Minion Vent Opening` across each end of an ordinary
   vent without enabling its cover. Confirm the tunnel needs no duplicate
   role-blocking geometry. Walk a Minion through each shallow gate from both
   directions on host and remote ownership. Confirm Skinny Kids, the Large Lad,
   and lobby-role players remain solid.
2. While the passage is open, drop a weapon and push or throw a generic physics
   prop at each gate from both directions. Confirm both collide with the opening
   instead of entering the vent. Repeat with a dodgeball once that gameplay item
   is available; it must remain solid for the same reason.
3. Enable the stock cover. Confirm every role is blocked while it is intact,
   only Minion melee reduces its 50 health, and exactly two baseline Minion hits
   destroy it. Firearms and other melee roles must neither damage nor open it.
4. Assign distinct hit and break sounds plus optional broken presentation or a
   gib-capable Prop. Confirm accepted hits play once for host and remote clients,
   the final hit uses only the break hook, late joiners see the current intact or
   broken state, and the next round restores the authored cover and closes the
   route.
5. After opening the cover, confirm only Minions traverse. The same opening
   collider must remain enabled and regain `large_lad_minion_passage` on every
   peer; the prefab must still contain only its one root collider. Reset the
   round and confirm it loses the tag while the intact cover blocks everyone
   again.
6. Break each authored contract in turn: remove the root collider, move it to a
   child, make it a trigger, remove the passage tag, change the root network
   mode, enable a cover without its direct visual child, or add a collider
   beneath that child. Confirm validation names the affected opening and the
   specific correction.

## Release inventory multiplayer checklist

Use a host and at least one remote client. Author one core pickup plus two
exclusive placements; make the two exclusives use the same weapon definition to
verify they remain independent physical instances.

1. Let both Skinny Kids touch the same core pickup. Confirm both unlock it, the
   pickup remains visible, and a second touch changes neither magazine nor HUD.
2. Fire a partial core magazine, switch away and back, then reload. Confirm the
   magazine persists, reload duration applies, and the HUD reads
   `magazine / ∞`.
3. Collect both authored exclusives with different Skinny Kids. Confirm both can
   exist simultaneously. Attempt to collect another while carrying one and
   confirm the local `You can only carry one exclusive weapon.` feedback.
4. Fire an exclusive, switch weapons, drop it with G, and have the other player
   repick it after dropping their own exclusive. Confirm magazine and reserve
   remain exact through every transition and the exclusive is last in scrolling.
5. Kill a Skinny Kid carrying an exclusive. Confirm the weapon drops near the
   death location (or safely returns to origin when blocked), core inventory is
   cleared, and the respawned Minion has built-in melee only.
6. Disconnect a client while carrying an exclusive. Confirm the instance is
   dropped or returned to its authored pickup and can be collected again.
7. End the round with exclusives carried and dropped. Confirm every runtime drop
   disappears, every authored exclusive returns, and its configured magazine and
   reserve are full exactly once.
8. Repeat selection, reload, pickup, and drop requests from the remote client
   while dead and after conversion. Confirm the host rejects them without ammo,
   ownership, visibility, or active-selection changes.
