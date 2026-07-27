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
