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

Use a host plus one remote client through `game_shell.scene` (with Gym loaded by
its `MapInstance`) for engine/network lifecycle coverage that the pure rule
tests cannot provide:

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

## Map selection, rotation, and voting checklist

The deterministic `LargeLadMapRotationTests` cover successful-round counting,
the rounds-per-map threshold, one accepted vote per eligible connection,
invalid candidates, disconnect handling, plurality, tied-leader selection,
the deterministic no-vote result, source-neutral official/community rules,
stable identity for duplicate display names, bounded fallback ordering, and
resetting the counter only after the replacement map reaches `Ready`.

For a listening-host test, start `game_shell.scene` with a host and at least one
remote client:

1. Join the remote client before choosing a map. Confirm every peer sees a fully
   black game view with the map-flow UI above it, no spawned player falls or can
   move in the empty shell, and only the host sees the initial chooser. The
   remote client must instead see `Waiting for the host to pick a map` and must
   not be able to authorize a selection. Confirm its scene reports exactly one
   active, root-level Snapshot `Map Content Host`/`MapInstance` and never logs an
   unknown map-host GameObject or a zero-MapInstance bootstrap failure. Confirm
   the host's Gym card shows the
   Stage 1 display name, thumbnail, mapper, player range, description, and
   official badge.
2. Select Gym. Confirm the already-connected remote receives the authoritative
   map identifier, applies it to its local `MapInstance`, and remains
	black and locally movement-frozen until that instance reports loaded. Verify
   the remote sees the map's static world geometry and has matching floor/wall
   collision—not only its networked GameObjects—and cannot fall through the map.
   Verify the same client also sees every authored barricade piece, the pistol
   and SMG models rather than gray fallback boxes, red dodgeball models, and vent
   covers with their authored model and collision dimensions. No inactive
   client-authored Object-mode root should remain after its local map load.
   In the console, confirm the host reports the number of activated map-authored
   Object roots and publishes their authoritative GUID manifest. Confirm each
   client receives every listed root before loading its local `MapInstance`, then
   discards only inactive locally authored Object-mode roots while preserving
   active authoritative roots. Compare the one-time host/client map-object
   summaries: barricade, dodgeball, weapon-pickup, and Minion-passage counts must
   match exactly; a doubled client count is a blocking failure.
   Confirm the flow passes through `Loading` to `Playing`, map content appears
   only beneath the existing `Map Content Host`, the same bootstrap objects and
   player connections remain alive, all persistent players arrive at valid Lobby
   positions, and the first round does not begin until map preparation reaches
   `Ready`.
3. Temporarily set `Rounds Per Map` to 1. Complete one real round and confirm
   the vote opens only after the normal `EndRound` boundary; rejected round
   starts, lobby waiting, aborted starts, and partial rounds must not increment
   the count.
4. At vote start, confirm the listening host and remote client are both eligible.
   Submit the remote client's vote, then submit the listening host's vote. Confirm
   both accepted choices are displayed from host-authored state and both votes
   count exactly once. With those as the only eligible voters, confirm the vote
   may complete early after both submit. Repeated host clicks must not alter the
   accepted host vote. Forged or stale candidate IDs must still be rejected.
5. With multiple candidates configured, verify plurality wins. Force a top tie
   and confirm the host randomly selects only among tied leaders. Let a vote
   expire with no submissions and confirm the stable-ID-first candidate wins.
6. Join a client after voting begins. Confirm it can observe the vote but remains
   ineligible and cannot submit in that already-running vote. Disconnect an
   eligible voter before it submits, and in a separate run disconnect one after
   submission; neither connection nor its stale vote may block or decide
   completion.
7. Complete the vote. Confirm every peer enters `VoteResult`, displays the same
   winning map and final totals for five seconds, rejects further submissions,
   and does not begin map unload before the result timer expires. Confirm
   gameplay stays closed through the result pause, `Transitioning`, and map
   unload/load, the shell and all connected players survive, and the
   completed-round counter resets only when the selected map reaches `Ready`.
   With only Gym available, confirm selecting it follows the normal full reload
   path rather than bypassing transition cleanup. Confirm the host publishes a
   fresh Object-root GUID generation, every connected client receives the complete
   manifest and advances past `Loading Map`, and no client remains permanently
   short only the native top-level roots from the departing generation. Repeat the
   same-map vote reload once more to catch stale create/delete accumulation.
8. While the map-flow blackout is active, overlap two unassigned or Skinny Kid
   players. Confirm neither pre-physics calculation nor post-physics application
   moves either player through soft separation, including when the hold begins
   between those two callbacks. Return the session to `Playing` and confirm the
   ordinary light horizontal soft separation resumes without changed gameplay
   behavior.

For failure recovery, configure or request a package/map that never successfully
loads and allow `Map Load Timeout` to expire. Confirm the coordinator progresses
through load cancellation/unload into the next bounded fallback and does not
remain permanently stranded in `Unloading`. Then test a map that fails the
blocking spawn/content contract. Confirm each failed identifier is attempted at
most once in that transaction, no round/timer/spawn flow resumes, and fallback is
tried in this order: last known good map, configured startup map, then the first
valid official catalog entry. If every option fails, confirm the flow remains
visibly/logically `Failed` without restarting `game_shell.scene` or disconnecting
clients. Selecting a later valid map must recover through the same coordinator
and `MapInstance`.

For a dedicated-server test, launch the normal Large Lad project rather than a
content scene. Confirm startup priority is: an explicit launch map identifier,
then the configured coordinator `Startup Map`, then the first valid official
catalog entry. Each source must still enter the common descriptor resolution,
validation, preparation, and transition path. The editor MCP listening-host run
does not emulate a dedicated process or inject dedicated launch arguments, so
perform this final matrix with the normal s&box dedicated launch workflow; no
external .NET build step is needed.

## Tab scoreboard and private role-preference checklist

The deterministic `LargeLadRoleSelectionTests` cover private preference value
validation plus the full host selection contract: full-round eligibility, the
first-session bootstrap roster, immediate-repeat exclusion, preference tiers,
longest-waiting fairness, host-random true ties, and transactional history
commits. The host first builds the ordinarily eligible pool, excludes the
previous Large Lad when another eligible player exists, then ranks
`PreferLargeLad`, `NoPreference`, and `PreferSkinnyKid`. Within the applicable
tier, never-selected and longest-waiting players win; only genuine ties use
host randomness.

Use a listening host plus at least two remote clients through `game_shell.scene`
for the engine input, focus, ownership, and recipient filtering that pure tests
do not emulate:

1. Hold Tab on the host and confirm the scoreboard appears. Release Tab and
   confirm it disappears.
2. Repeat on a remote client and confirm each peer's scoreboard opens and closes
   independently without changing gameplay or pausing another peer.
3. Confirm every connected Large Lad player appears exactly once with their
   display name and current role/lifecycle status.
4. Confirm only the local player's row contains the role-preference dropdown.
   No remote player's preference is shown and no remote row contains a control.
5. On a remote client, select each option in turn: `I want to play Large Lad`,
   `I want to play a Skinny Kid`, and `I don't care`.
6. For each selection, inspect the host and confirm it records the validated
   authoritative value on that remote client's persistent player object.
7. Confirm only the requesting remote client receives and displays the accepted
   value. A second client must neither learn nor display it.
8. Attempt to invoke the preference RPC on a player object owned by another
   connection. Confirm the host rejects the caller/owner mismatch and the target
   player's settled preference does not change.
9. Use the listening host's own dropdown and confirm its host-local request
   resolves through its local/host connection and settles normally.
10. Launch the normal dedicated-server workflow and confirm the server has no
    local scoreboard, creates no server pseudo-player, and stores preferences
    only for connected player-owned objects.
11. While holding Tab, open the dropdown and click all three choices. Confirm
    the cursor remains usable and no firearm, melee, Eat, Ground Slam, dodgeball,
    inventory switch/drop, movement, or look input triggers through the panel.
12. Release Tab while the dropdown is open. Confirm the scoreboard and popup
    close, cursor behavior returns to automatic gameplay handling, and ordinary
    movement and combat input resumes.
13. Hold Tab while a blocking initial selection, vote, result, loading,
    transition, recovery, or failure screen owns map flow. Confirm the map-flow
    UI wins and the scoreboard neither appears nor steals focus.
14. Die and respawn, including a Skinny Kid-to-Minion conversion, and confirm
    the same host-accepted preference remains selected.
15. Complete a round and confirm the preference survives the round boundary.
16. Complete a map vote and runtime map transition. Confirm the persistent
    player object's preference survives and is returned owner-only when the
    scoreboard next opens.
17. Start a fresh server session with enough initial players and confirm the
    first round starts even though nobody has full-round completion credit. The
    successfully started roster becomes the one narrow bootstrap roster.
18. Complete that round. Confirm each player who was present at successful start
    and remains connected at completion gains ordinary Large Lad eligibility.
    Death or conversion during the round must not revoke the credit.
19. Join a new player during `HeadStart` or `Playing`. Confirm that player does
    not receive credit for the active round and cannot be Large Lad in the
    immediately following round, even if they choose `PreferLargeLad`.
20. Keep that player connected from the next successful round start through its
    completion. Confirm they then gain ordinary eligibility for later rounds.
21. Disconnect a round starter before completion and confirm they receive no
    credit. Reconnecting creates a new session and must not restore eligibility
    or fairness history from the old connection.
22. Make the previous Large Lad the only `PreferLargeLad` volunteer while another
    eligible player is neutral. Confirm the previous Large Lad is excluded first
    and the neutral player is selected. Then repeat with literally no other
    eligible candidate and confirm the previous Large Lad may repeat.
23. Give two eligible, non-previous players the same preference and different
    last-selection histories. Confirm the player who has waited longer is chosen;
    a never-selected player must outrank a recently selected player.
24. Create a genuine tie in eligibility, repeat status, preference, and fairness
    history. Repeat enough times to confirm the host chooses only between those
    tied finalists and never chooses a lower preference tier.
25. Force Hunter or Skinny Kid batch allocation to fail after prospective
    selection. Confirm the round remains fail-closed and neither the successful
    round ordinal, previous Large Lad, nor selected player's fairness history
    changes. Restore valid spawns and confirm one successful start commits the
    selected history exactly once.
26. Perform a map vote and runtime transition. Confirm each connected player's
    private preference, full-round eligibility, and last-selection history, plus
    the manager's previous-Large-Lad identity, bootstrap state, and global
    fairness ordinal all survive. A round interrupted by the transition must not
    grant completion credit.

## Persistent session and map reload checklist

1. Start the game normally with a listening host and remote clients. On the host
   and every client, confirm `game_shell.scene` supplies exactly one persistent
   bootstrap with one `LargeLadSessionCoordinator`, `LargeLadGameManager`,
   `LargeLadSpawnAllocator`, and `NetworkHelper`, plus exactly one separate
   root-level **Snapshot** `Map Content Host` with the sole `MapInstance` and
   `UseMapFromLaunch = false`.
2. Confirm Gym appears only beneath `Map Content Host` and contains none of the
   session-global components itself.
3. On a listening host, observe `WaitingForInitialMapSelection -> Loading ->
   Playing` after choosing a map. On a dedicated server, observe the configured
   startup resolution enter `Loading -> Playing`. In both cases, confirm
   generated Lobby positions, blocking validation, and persistent-player Lobby
   placement finish before the underlying map state reaches `Ready`, and that no
   round flow advances during `Loading`.
4. Begin a valid two-or-more-player round, then use `Unload Map`, `Reload Current
   Map`, or host code calling `LoadMap`. Confirm state leaves `Ready` for
   `Unloading` before map content disappears and phase/timers stop immediately.
5. Confirm one `Large Lad map-transition cleanup completed` message for the
   transition, carried exclusive/utility items are cleared once, players become
   unassigned and movement-locked, and map-owned spawn data is invalidated.
6. Confirm unload completion enters `Unloaded`, or proceeds directly to the
   replacement's `Loading`, without advancing round state between callbacks.
7. Confirm the same player objects, bootstrap, manager, network helper,
   coordinator, allocator, and map host survive; only map-host children change.
8. Reload Gym and confirm spawn candidates rebuild and persistent players reach
   new Lobby positions before state returns to `Ready`.
9. Load a content scene that lacks the blocking spawn contract (or otherwise
   break that contract). Confirm state becomes `Failed`, the blocking errors are
   logged, and timers, spawns, respawns, conversions, and round transitions stay
   stopped. Restore Gym and confirm recovery through `Unloading -> Loading ->
   Ready`.
10. Run with one player longer than `PlayerReadyDelay`. Confirm the lobby remains
    waiting and no round-start attempt or rejection is emitted. Add a second
    player and confirm normal round eligibility resumes.
11. Verify the game project exposes no pre-launch map selector (`MapSelect` is
    `Empty`, `MapList` is empty), always starts `game_shell.scene`, and keeps the
    shell `MapInstance.UseMapFromLaunch` disabled. Content-scene direct Play is
    an editor mapping preview, not a supported game launch path.
12. As the production package-path smoke test, call the coordinator's `LoadMap`
    with a valid package ident. Confirm its synchronized selection drives the
    built-in download, mount, load, and unload path without a package polling or
    secondary bootstrap layer.
13. Repeat valid and invalid transitions with a host plus remote client. Confirm
    the remote remains connected, observes the host-authored state, keeps the
    same player/session objects, and never receives duplicate map content or
    session infrastructure.

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
3. Repeat with the active victim entering a kill volume. Confirm the committed
   victim takes no environmental damage, the Eat remains active, and no
   environmental lethal event is emitted. Confirm Eat completes lethally once
   with the Eat cause and heals the Large Lad exactly once. Separately apply
   ordinary nonlethal environmental/impact damage and confirm it is also
   rejected during the commitment.
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
with the remote client as shooter.

1. At close, medium, and long firearm range, shoot a stationary Minion at the
   center and edge of the visible head, the neck, and the upper torso. Repeat
   from the front, side, and an elevated angle while the Minion is standing and
   crouched. Confirm only a logged same-target `head` tag or recognized head-bone
   hitbox produces `Head`; edge misses, neck, and upper torso remain body hits.
2. Repeat step 1 while the Minion walks, runs, changes direction, crouches, and
   plays normal movement animations. Confirm the movement capsule/box can appear
   first without becoming the classification result. Among bounded same-target
   model hitboxes, confirm the nearest hitbox alone determines Head or Body: a
   nearer body must not be promoted by a later head, while a nearer head remains
   Head. Confirm no world-height, camera-height, origin-distance, or upper-torso
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
   committed victim into a kill volume before completion. Confirm the victim
   takes no environmental damage, Eat stays active and completes as the sole
   lethal event, the Large Lad heals once, and no environmental lethal event is
   emitted.

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

Use a host and at least one remote client. Author one core pickup, two exclusive
placements, and one `LargeLadDodgeballPickup`; make the two exclusives use the
same weapon definition to verify they remain independent physical instances.

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
   remain exact through every transition and the exclusive is the final firearm.
5. Kill a Skinny Kid carrying an exclusive. Confirm the weapon drops near the
   death location (or safely returns to origin when blocked), native Core items
   are cleared, and the respawned Minion has built-in melee only.
6. Disconnect a client while carrying an exclusive. Confirm the instance is
   dropped or returned to its authored pickup and can be collected again.
7. End the round with exclusives carried and dropped. Confirm every persistent
   native firearm returns to its authored origin without duplication and its
   configured magazine and reserve are restored exactly once.
8. Collect the dodgeball and confirm it appears after the exclusive in direct
   slot and mouse-wheel order. Confirm the utility HUD row has no ammunition,
   selecting it cannot fire or reload a firearm, Primary Attack throws it, and
   G drops the same authored instance for another Skinny Kid to collect.
9. Attempt dodgeball collection and selection as Large Lad, Minion, a dead
   Skinny Kid, and a Skinny Kid already carrying a dodgeball. Confirm every
   attempt is rejected without changing the physical or synchronized state.
10. Repeat role change, death, disconnect, round reset, and map transition while
   the dodgeball is carried or dropped. Confirm it drops safely or returns to
   origin, never duplicates, and only round reset restores its authored
   transform.
11. Repeat selection, reload, pickup, and drop requests from the remote client
   while dead and after conversion. Confirm the host rejects them without ammo,
   ownership, visibility, or active-selection changes.
12. Throw directly into a living Minion and confirm one immediate lethal event
   and one Minion-kill presentation. Repeat against the Large Lad and confirm a
   strong bounded knockback horizontally away from the thrower, no stun/movement
   lock, the configured 0-5 damage, and one Large-Lad-hit presentation. Friendly,
   dead, low-speed, and replayed contacts must produce no combat event.
13. Throw from both host and remote ownership. Confirm launch starts clear of
   the thrower, including while sprinting, and the solid ball passes through all
   Skinny Kid bodies without being deflected while touch callbacks remain active.
   Confirm the separate pickup trigger stays disabled for its configured cooldown
   and then still retrieves the ball, world motion is host simulated and smoothly
   interpolated, and the first solid non-self impact makes the ball harmless until
   it is picked up and thrown again.
14. Ground Slam a resting and airborne dodgeball repeatedly. Confirm impulse is
   visibly capped, the ball remains intact and collectible, and it never enters
   the mapper's broken, unanchored, or cleaned-up state.
15. Throw and roll the ball against both sides of every Minion vent entrance.
   Confirm the opening remains solid to the ball whether its cover is intact or
   broken, while a Minion still traverses the open passage.

## Native weapon and role-ability presentation manual checks

These visual and audio checks are intentionally separate from the
engine-independent role-ability presentation tests in `UnitTests/`.

### First-person checks

1. As a Skinny Kid, equip native crowbar, Pistol, and SMG in turn. Confirm the
   native weapon prefabs own draw, idle, attack, reload, arms, muzzle, sound, and
   model visibility with no duplicate geometry or effects from the player prefab.
2. Fire Pistol taps and a sustained SMG burst, empty and reload both magazines,
   and switch during every transition. Confirm one native animation, sound, muzzle
   effect, and authoritative shot per action, with no stale model after switching.
3. As Large Lad and a converted Minion, confirm the custom human arms still use
   the punching graph and `b_attack` gesture. Select a dodgeball as Skinny Kid and
   confirm those custom arms hold one correctly scaled red ball, then clear as the
   authoritative physical ball is thrown.
4. Toggle first/third person and recreate the local camera while using fists and
   the dodgeball. Confirm custom arms bind only to the current owned camera and
   the normal body renderer returns when the ability is hidden.
5. Disable/destroy `LargeLadRoleAbilityPresentation` in a test prefab copy.
   Confirm its local arms and dodgeball objects are removed and the body renderer
   is restored; native crowbar/firearm presentation must remain unaffected.

### Multiplayer checks

1. Use a host, an owning Skinny Kid, and at least one remote observer. Confirm
   native crowbar/Pistol/SMG viewmodels are owner-only and their native worldmodels
   remain aligned through idle, locomotion, attack, reload, and camera changes.
2. Select a dodgeball and confirm every observer sees one correctly scaled red
   ball in the holder's right hand and the configured throw gesture, followed by
   no stale held ball. Confirm Large Lad/Minion fist gestures still replicate.
3. Confirm native Pistol/SMG deploy, fire, reload, sound, and muzzle presentation
   occur once per action on every peer, with no doubled shots, hitmarkers, damage,
   or effects.
4. Repeat switching, utility selection, Exclusive drop, death, conversion, role
   change, round end/reset, late join, and map transition. Confirm no stale custom
   ability objects or native weapon models survive their owning state.
