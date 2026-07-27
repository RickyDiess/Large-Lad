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
   inventory is prepared once per `RespawnAs` (no duplicate melee or grants).
