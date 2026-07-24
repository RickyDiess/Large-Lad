using System;
using Editor.MapDoc;
using Editor.MapEditor;

/// <summary>
/// Hammer authoring shortcuts for turning selected raw meshes into complete,
/// self-contained Large Lad barricades.
/// </summary>
public static class LargeLadHammerBarricadeTools
{
	private const float DefaultHealth = 300.0f;
	private const float DefaultLadStructuralDamage = 100.0f;
	private const string NetworkStateName = "Barricade Network State";

	[Event( "hammer.mapview.contextmenu" )]
	public static void OnMapViewContextMenu( Menu menu, MapView view )
	{
		var selection = Selection.All.ToList();

		if ( selection.Count == 0 )
			return;

		menu.AddSeparator();

		var largeLadMenu = menu.AddMenu( "Large Lad", "sports_esports" );
		var barricadeMenu = largeLadMenu.AddMenu( "Create Barricade", "door_front" );
		var tiedMeshes = selection
			.OfType<MapMesh>()
			.Where( mesh => mesh.Parent is MapGameObject )
			.ToList();

		if ( tiedMeshes.Count == selection.Count )
		{
			var repairMenu = largeLadMenu.AddMenu( "Repair Barricade", "build" );
			repairMenu.AddOption(
				"Repair as Skinny Progression",
				"route",
				() => RepairBarricades( view, tiedMeshes, LargeLadBarricadeMode.SkinnyProgression ) );
			repairMenu.AddOption(
				"Repair as Lad Shortcut",
				"destruction",
				() => RepairBarricades( view, tiedMeshes, LargeLadBarricadeMode.LadShortcut ) );
		}

		if ( !TryGetRawMeshes( selection, out var meshes, out var problem ) )
		{
			var unavailable = barricadeMenu.AddOption(
				problem,
				"warning",
				() => { } );
			unavailable.Enabled = false;
			return;
		}

		AddModeMenu(
			barricadeMenu,
			"Skinny Progression",
			"route",
			view,
			meshes,
			LargeLadBarricadeMode.SkinnyProgression );

		AddModeMenu(
			barricadeMenu,
			"Lad Shortcut",
			"destruction",
			view,
			meshes,
			LargeLadBarricadeMode.LadShortcut );
	}

	private static void AddModeMenu(
		Menu parent,
		string title,
		string icon,
		MapView view,
		IReadOnlyList<MapMesh> meshes,
		LargeLadBarricadeMode mode )
	{
		var modeMenu = parent.AddMenu( title, icon );

		modeMenu.AddOption(
			"Group Selection",
			"select_all",
			() => CreateBarricades( view, meshes, mode, groupSelection: true ) );

		modeMenu.AddOption(
			"Separate Brushes",
			"splitscreen",
			() => CreateBarricades( view, meshes, mode, groupSelection: false ) );
	}

	private static bool TryGetRawMeshes(
		IReadOnlyList<MapNode> selection,
		out IReadOnlyList<MapMesh> meshes,
		out string problem )
	{
		var selectedMeshes = selection.OfType<MapMesh>().ToList();

		if ( selectedMeshes.Count != selection.Count )
		{
			meshes = Array.Empty<MapMesh>();
			problem = "Select only raw Hammer meshes";
			return false;
		}

		if ( selectedMeshes.Any( IsAlreadyTied ) )
		{
			meshes = Array.Empty<MapMesh>();
			problem = "Selection contains an already-tied mesh";
			return false;
		}

		meshes = selectedMeshes;
		problem = null;
		return selectedMeshes.Count > 0;
	}

	private static bool IsAlreadyTied( MapMesh mesh )
	{
		for ( MapNode parent = mesh.Parent; parent is not null; parent = parent.Parent )
		{
			if ( parent is MapGameObject || parent is MapEntity )
				return true;
		}

		return false;
	}

	private static void CreateBarricades(
		MapView view,
		IReadOnlyList<MapMesh> requestedMeshes,
		LargeLadBarricadeMode mode,
		bool groupSelection )
	{
		var currentSelection = Selection.All.ToList();

		if ( !TryGetRawMeshes( currentSelection, out var currentMeshes, out var problem ) ||
			requestedMeshes.Count != currentMeshes.Count ||
			requestedMeshes.Any( mesh => !currentMeshes.Contains( mesh ) ) )
		{
			EditorUtility.DisplayDialog(
				"Cannot create barricade",
				problem ?? "The Hammer selection changed. Select the raw brushes again.",
				"OK",
				"warning",
				Hammer.Window );
			return;
		}

		var undoName = groupSelection
			? $"Create {GetDisplayName( mode )}"
			: $"Create {currentMeshes.Count} {GetDisplayName( mode )}s";

		History.MarkUndoPosition( undoName );

		foreach ( var mesh in currentMeshes )
		{
			History.Keep( mesh );
		}

		var created = new List<MapGameObject>();

		if ( groupSelection )
		{
			created.Add( CreateBarricade( view, currentMeshes, mode ) );
		}
		else
		{
			foreach ( var mesh in currentMeshes )
			{
				created.Add( CreateBarricade( view, new[] { mesh }, mode ) );
			}
		}

		Selection.Clear();
		foreach ( var barricade in created )
		{
			Selection.Add( barricade );
		}

		Log.Info(
			$"Created {created.Count} {GetDisplayName( mode )}" +
			$"{(created.Count == 1 ? string.Empty : "s")} from " +
			$"{currentMeshes.Count} Hammer mesh{(currentMeshes.Count == 1 ? string.Empty : "es")}." );
	}

	private static MapGameObject CreateBarricade(
		MapView view,
		IReadOnlyList<MapMesh> meshes,
		LargeLadBarricadeMode mode )
	{
		var scene = view.MapDoc.World.Scene;

		using ( scene.Push() )
		{
			var gameObject = new GameObject( true, GetDisplayName( mode ) );
			gameObject.IsStatic = true;
			gameObject.NetworkMode = NetworkMode.Never;

			// Hammer's built-in tie operation installs this component internally.
			// Project editor assemblies cannot call that internal hook, so create
			// the identical component explicitly before attaching the map meshes.
			var hammerMesh = gameObject.GetOrAddComponent<HammerMesh>();
			hammerMesh.UseRenderer = true;
			hammerMesh.UseCollision = true;
			hammerMesh.IsTrigger = false;
			hammerMesh.Static = true;

			var barricade = gameObject.Components.Create<LargeLadBarricade>();
			barricade.Mode = mode;
			barricade.MaximumHealth = DefaultHealth;
			barricade.LadStructuralDamage = DefaultLadStructuralDamage;

			var networkState = new GameObject( true, NetworkStateName );
			networkState.SetParent( gameObject, false );
			networkState.IsStatic = true;
			networkState.NetworkMode = NetworkMode.Object;
			networkState.Components.Create<LargeLadBarricadeState>();

			var mapGameObject = new MapGameObject( view.MapDoc, gameObject );

			// Capture the empty wrapper as the new object before attaching existing
			// meshes. The meshes themselves were captured with History.Keep, so one
			// undo restores them to their original parents instead of deleting them.
			History.KeepNew( mapGameObject );

			foreach ( var mesh in meshes )
			{
				mesh.Parent = mapGameObject;
			}
			return mapGameObject;
		}
	}

	private static void RepairBarricades(
		MapView view,
		IReadOnlyList<MapMesh> requestedMeshes,
		LargeLadBarricadeMode mode )
	{
		var currentMeshes = Selection.All.OfType<MapMesh>().ToList();

		if ( currentMeshes.Count != Selection.All.Count() ||
			requestedMeshes.Count != currentMeshes.Count ||
			requestedMeshes.Any( mesh => !currentMeshes.Contains( mesh ) ) ||
			currentMeshes.Any( mesh => mesh.Parent is not MapGameObject ) )
		{
			EditorUtility.DisplayDialog(
				"Cannot repair barricade",
				"Select only the tied Hammer meshes belonging to the barricade, then try again.",
				"OK",
				"warning",
				Hammer.Window );
			return;
		}

		var groups = currentMeshes
			.GroupBy( mesh => (MapGameObject)mesh.Parent )
			.ToList();
		History.MarkUndoPosition( $"Repair {groups.Count} Large Lad Barricade(s)" );

		foreach ( var group in groups )
		{
			History.Keep( group.Key );
			foreach ( var mesh in group )
				History.Keep( mesh );
		}

		var repaired = new List<MapGameObject>();
		foreach ( var group in groups )
		{
			repaired.Add( CreateBarricade( view, group.ToList(), mode ) );
			view.MapDoc.DeleteNode( group.Key );
		}

		Selection.Clear();
		foreach ( var barricade in repaired )
			Selection.Add( barricade );

		Log.Info( $"Repaired {repaired.Count} barricade{(repaired.Count == 1 ? string.Empty : "s")}." );
	}

	private static string GetDisplayName( LargeLadBarricadeMode mode ) => mode switch
	{
		LargeLadBarricadeMode.SkinnyProgression => "Skinny Progression Barricade",
		LargeLadBarricadeMode.LadShortcut => "Lad Shortcut Barricade",
		_ => "Large Lad Barricade"
	};
}
