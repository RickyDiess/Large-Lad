# Running the automated tests

Large Lad's deterministic gameplay tests live in `UnitTests/` and use the
s&box-generated MSTest project. They do not start a multiplayer session or load
a gameplay scene.

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
