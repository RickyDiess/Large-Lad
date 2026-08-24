using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;

public enum LargeLadMapSessionState
{
	Unloaded,
	Loading,
	Ready,
	Unloading,
	Failed
}

/// <summary>
/// Fail-safe for a persistent gameplay bootstrap accidentally authored into a
/// content map. The outer MapInstance must remain the only loader.
/// </summary>
internal static class LargeLadBootstrapPlacement
{
	public static bool DisableIfEmbeddedMapContent( Component component )
	{
		if ( !IsEmbeddedMapContent( component ) )
			return false;

		var gameObject = component.GameObject;
		if ( gameObject.Enabled )
		{
			Log.Warning(
				$"Disabled gameplay bootstrap '{gameObject.Name}' because it is " +
				"authored inside loaded map content. Remove " +
				"prefabs/large_lad_gameplay.prefab from the map and keep only one " +
				"LargeLadMapProfile." );
			gameObject.Enabled = false;
		}

		return true;
	}

	public static bool IsEmbeddedMapContent( Component component )
	{
		if ( !Game.IsPlaying || component?.GameObject is null ||
			!component.GameObject.IsValid )
		{
			return false;
		}

		for ( var ancestor = component.GameObject.Parent;
			ancestor is not null;
			ancestor = ancestor.Parent )
		{
			if ( ancestor.Components.Get<MapInstance>(
				FindMode.EverythingInSelf ) is not null )
			{
				return true;
			}
		}

		var ownCoordinator = component.GameObject.Components
			.Get<LargeLadSessionCoordinator>( FindMode.EverythingInSelf );
		var ownMapHost = ownCoordinator?.MapInstance?.GameObject;
		var hasMapProfileOutsideOwnLoader = component.Scene
			.GetAllComponents<LargeLadMapProfile>()
			.Any( profile =>
				profile is not null &&
				profile.IsValid &&
				!IsDescendantOrSelf( profile.GameObject, ownMapHost ) );

		if ( hasMapProfileOutsideOwnLoader )
			return true;

		return component.Scene
			.GetAllComponents<LargeLadSessionCoordinator>()
			.Any( other =>
				other is not null &&
				other.IsValid &&
				other.Enabled &&
				other.GameObject.Enabled &&
				other.GameObject != component.GameObject &&
				other.MapState != LargeLadMapSessionState.Unloaded &&
				!string.IsNullOrWhiteSpace( other.CurrentMapName ) );
	}

	private static bool IsDescendantOrSelf(
		GameObject candidate,
		GameObject expectedAncestor )
	{
		if ( candidate is null || expectedAncestor is null )
			return false;

		for ( var current = candidate;
			current is not null;
			current = current.Parent )
		{
			if ( current == expectedAncestor )
				return true;
		}

		return false;
	}
}

/// <summary>
/// Owns the replaceable map inside one persistent Large Lad session scene.
/// MapInstance remains responsible for package mounting, asynchronous loading,
/// scene-map creation, and unloading; this component only turns its callbacks
/// into the scene-scoped lifecycle contract consumed by round flow.
/// </summary>
public sealed partial class LargeLadSessionCoordinator : Component
{
	public MapInstance MapInstance { get; private set; }

	[Property, RequireComponent]
	public LargeLadGameManager GameManager { get; set; }

	[Property, Title( "Startup Map" )]
	public string StartupMap { get; set; } = "scenes/gym.scene";

	[Property, Title( "Local Development Map" )]
	public string LocalDevelopmentMap { get; set; } = "scenes/gym.scene";

	[Property, Group( "Map Catalog" )]
	[Description(
		"First-party curation owned by Large Lad. Mapper-authored manifests cannot " +
		"grant themselves official status." )]
	public LargeLadOfficialMapCatalog OfficialMapCatalog { get; set; }

	[Sync( SyncFlags.FromHost ), Change( nameof( OnCurrentMapNameChanged ) )]
	public string CurrentMapName { get; private set; }

	[Sync( SyncFlags.FromHost ), Change( nameof( OnMapNetworkRootManifestChanged ) )]
	public string CurrentMapNetworkRootManifest { get; private set; }

	[Sync( SyncFlags.FromHost )]
	public LargeLadMapSessionState MapState { get; private set; } =
		LargeLadMapSessionState.Unloaded;

	public bool IsMapReady => MapState == LargeLadMapSessionState.Ready;

	/// <summary>
	/// Normalized metadata for the map that completed manifest validation on
	/// this peer. Gameplay uses the same descriptor shape for official and
	/// community maps.
	/// </summary>
	public LargeLadMapDescriptor CurrentMapDescriptor { get; private set; }

	public IReadOnlyList<string> LoadedMapValidationIssues =>
		loadedMapValidationIssues;

	private Scene registeredScene;
	private MapInstance subscribedMapInstance;
	private string hostLoadingMapName;
	private string pendingReloadMapName;
	private int unloadNotificationVersion;
	private bool mapTransitionCleanupStarted;
	private int loadedMapPreparationVersion;
	private string pendingClientMapName;
	private string pendingClientMapNetworkRootManifest;
	private bool loggedPendingClientNetworkRootBarrier;
	private readonly List<string> loadedMapValidationIssues = new();

	protected override void OnEnabled()
	{
		base.OnEnabled();

		if ( LargeLadBootstrapPlacement.DisableIfEmbeddedMapContent( this ) )
			return;

		ResolveBootstrapReferences();
		AttachMapCallbacks();
		AttachToSceneRegistry();
	}

	protected override void OnDisabled()
	{
		InvalidateLoadedMapResolution();
		DetachFromSceneRegistry();
		DetachMapCallbacks();
		base.OnDisabled();
	}

	protected override void OnDestroy()
	{
		DetachFromSceneRegistry();
		DetachMapCallbacks();
		base.OnDestroy();
	}

	protected override void OnAwake()
	{
		if ( LargeLadBootstrapPlacement.DisableIfEmbeddedMapContent( this ) )
			return;

		ResolveBootstrapReferences();
		AttachMapCallbacks();
		AttachToSceneRegistry();
	}

	protected override void OnStart()
	{
		if ( LargeLadBootstrapPlacement.IsEmbeddedMapContent( this ) )
			return;

		ResolveBootstrapReferences();
		AttachMapCallbacks();

		if ( Networking.IsHost )
			InitializeMapFlow();
		else
			ApplyCurrentMapName();

		// OnLoad can complete before this component reaches OnStart. This is a
		// one-time lifecycle catch-up, not readiness polling; all later changes
		// come directly from MapInstance callbacks.
		if ( Networking.IsHost &&
			MapInstance?.IsLoaded == true &&
			!IsMapReady )
		{
			hostLoadingMapName = MapInstance.MapName?.Trim() ?? string.Empty;
			SetMapState( LargeLadMapSessionState.Loading );
			HandleMapLoaded();
		}
	}

	protected override void OnValidate()
	{
		ResolveBootstrapReferences();
	}

	/// <summary>
	/// Selects either a package ident (the production path) or a local scene-map
	/// resource such as scenes/gym.scene. MapInstance owns the actual operation.
	/// </summary>
	public void LoadMap( string mapName )
	{
		if ( !CanControlMap() || string.IsNullOrWhiteSpace( mapName ) )
			return;

		var selectedMapName = mapName.Trim();

		if ( string.Equals(
			GetHostSelectedOrLoadingMapName(),
			selectedMapName,
			StringComparison.OrdinalIgnoreCase ) )
		{
			if ( MapState == LargeLadMapSessionState.Failed )
				ReloadCurrentMap();

			return;
		}

		if ( MapState == LargeLadMapSessionState.Unloading )
		{
			pendingReloadMapName = selectedMapName;
			return;
		}

		if ( MapInstance?.IsLoaded == true ||
			MapState != LargeLadMapSessionState.Unloaded )
		{
			pendingReloadMapName = selectedMapName;
			var unloadedMapName = GetHostSelectedOrLoadingMapName();
			UnloadSelectedMap( unloadedMapName );
			return;
		}

		pendingReloadMapName = null;
		BeginLoadingMap( selectedMapName );
	}

	[Button( "Load Local Development Map" )]
	public void LoadLocalDevelopmentMap()
	{
		LoadMap( LocalDevelopmentMap );
	}

	[Button( "Unload Map" )]
	public void UnloadMap()
	{
		if ( !CanControlMap() )
			return;

		if ( MapState == LargeLadMapSessionState.Unloaded &&
			MapInstance?.IsLoaded != true &&
			string.IsNullOrWhiteSpace( GetHostSelectedOrLoadingMapName() ) )
		{
			return;
		}

		var unloadedMapName = GetHostSelectedOrLoadingMapName();
		pendingReloadMapName = null;
		UnloadSelectedMap( unloadedMapName );
	}

	[Button( "Reload Current Map" )]
	public void ReloadCurrentMap()
	{
		var selectedMapName = GetHostSelectedOrLoadingMapName();
		if ( !CanControlMap() ||
			string.IsNullOrWhiteSpace( selectedMapName ) )
		{
			return;
		}

		// Clear the selection before unloading so MapInstance does not reload it
		// on its next update. The unload callback restores the selected ident and
		// lets MapInstance perform a normal asynchronous replacement load.
		pendingReloadMapName = selectedMapName;
		var unloadedMapName = selectedMapName;
		UnloadSelectedMap( unloadedMapName );
	}

	private void UnloadSelectedMap( string unloadedMapName )
	{
		InvalidateLoadedMapResolution();
		SetMapState( LargeLadMapSessionState.Unloading );
		BeginMapTransitionCleanup();

		var unloadVersionBefore = unloadNotificationVersion;
		var mapWasLoaded = MapInstance.IsLoaded;
		var localSceneChildren = unloadedMapName?.EndsWith(
			".scene",
			StringComparison.OrdinalIgnoreCase ) == true
			? MapInstance.GameObject.Children.ToArray()
			: null;

		// State and gameplay are closed before clearing MapName because changing
		// it can itself begin MapInstance's asynchronous unload.
		ClearMapNameOnAllPeers();
		MapInstance.UnloadMap();

		// Snapshot-networked objects from a direct local .scene can be retained by
		// MapInstance's client-snapshot safety check on a listen host. The dedicated
		// map host has no persistent children, so this snapshot contains only map
		// content. Package maps need no adapter.
		if ( localSceneChildren is not null )
		{
			foreach ( var child in localSceneChildren )
			{
				if ( child.IsValid )
					child.Destroy();
			}
		}

		// Loaded package maps invoke OnMapUnloaded directly. Current direct scene
		// maps and cancellation of an async package that never reached IsLoaded do
		// not, so close the same lifecycle contract after UnloadMap returns. A late
		// callback is ignored by HandleMapUnloaded once the fallback is Loading.
		if ( (localSceneChildren is not null || !mapWasLoaded) &&
			unloadNotificationVersion == unloadVersionBefore )
		{
			HandleMapUnloaded();
		}
	}

	internal void ResolveBootstrapReferences( MapInstance knownMapInstance = null )
	{
		if ( knownMapInstance is not null && knownMapInstance.IsValid )
		{
			MapInstance = knownMapInstance;
		}
		else if ( !IsUsableMapInstance( MapInstance ) )
		{
			MapInstance = Scene
				.GetAllComponents<MapInstance>()
				.FirstOrDefault( IsUsableMapInstance );
		}

		if ( !IsUsableBootstrapComponent( GameManager ) )
			GameManager = Components.Get<LargeLadGameManager>();
	}

	private async void HandleMapLoaded()
	{
		ResolveBootstrapReferences();

		if ( !Networking.IsHost )
			ReconcileClientAuthoredMapNetworkRoots();

		var preparedMapIdentifier = Networking.IsHost
			? hostLoadingMapName?.Trim() ?? string.Empty
			: MapInstance?.MapName?.Trim() ?? string.Empty;

		if ( Networking.IsHost &&
			(MapState != LargeLadMapSessionState.Loading ||
			string.IsNullOrWhiteSpace( preparedMapIdentifier )) )
		{
			Log.Warning(
				$"Ignored a completed map load while the Large Lad session was " +
				$"{MapState}." );
			return;
		}

		var preparationVersion = ++loadedMapPreparationVersion;
		if ( !LargeLadMapValidator.TryGetSingleEnabledProfile(
			MapInstance?.GameObject,
			out var profile,
			out var profileIssues ) )
		{
			FailLoadedMapResolution(
				profileIssues.Select( issue => issue.ToString() ) );
			return;
		}

		var packageMetadata = await LargeLadMapCatalog
			.FetchPublishedPackageMetadata( preparedMapIdentifier );

		var currentPreparingIdentifier = Networking.IsHost
			? hostLoadingMapName?.Trim() ?? string.Empty
			: MapInstance?.MapName?.Trim() ?? string.Empty;
		if ( preparationVersion != loadedMapPreparationVersion ||
			!string.Equals(
				currentPreparingIdentifier,
				preparedMapIdentifier,
				StringComparison.OrdinalIgnoreCase ) ||
			(Networking.IsHost &&
				MapState != LargeLadMapSessionState.Loading) )
		{
			return;
		}

		if ( !LargeLadMapCatalog.TryResolveLoadedMap(
			profile.Manifest,
			preparedMapIdentifier,
			packageMetadata,
			OfficialMapCatalog,
			out var descriptor,
			out var resolutionIssues ) )
		{
			FailLoadedMapResolution( resolutionIssues );
			return;
		}

		CurrentMapDescriptor = descriptor;
		loadedMapValidationIssues.Clear();

		GameObject[] authoritativeMapNetworkRoots = [];
		if ( Networking.IsHost &&
			!TryActivateHostMapNetworkRoots(
				out authoritativeMapNetworkRoots,
				out var networkRootIssue ) )
		{
			FailLoadedMapResolution( networkRootIssue );
			return;
		}

		LogLoadedGameplayObjectSummary( preparedMapIdentifier );

		if ( !Networking.IsHost )
		{
			Log.Info(
				$"Large Lad client finished its local MapInstance load for " +
				$"'{preparedMapIdentifier}' and reconciled its authored Object-mode " +
				$"roots against network authority." );
			return;
		}

		var isReady = GameManager?.PrepareLoadedMap( this, descriptor ) == true;
		if ( isReady )
		{
			// Host validation and Object spawning are complete before selection is
			// published. The accompanying GUID manifest is the delivery contract:
			// each client waits for every active proxy before starting its local
			// MapInstance deserialization. Message ordering alone is not a barrier.
			PublishLoadedMapNameToPeers(
				preparedMapIdentifier,
				authoritativeMapNetworkRoots );
			hostLoadingMapName = null;
		}

		SetMapState(
			isReady
				? LargeLadMapSessionState.Ready
				: LargeLadMapSessionState.Failed );

		if ( isReady )
		{
			mapTransitionCleanupStarted = false;
			HandleMapBecameReady( descriptor );
			Log.Info(
				$"Large Lad map '{descriptor.DisplayName}' " +
				$"({descriptor.MapInstanceIdentifier}) is ready; the " +
				"persistent session bootstrap remained active." );
		}
		else
		{
			HandleSelectedMapFailure(
				"the loaded map did not satisfy Large Lad's blocking structural " +
				"spawn and lobby contract" );
			Log.Error(
				$"Large Lad map '{MapInstance?.MapName}' loaded but did not " +
				"satisfy the blocking map contract. Round flow remains closed." );
		}
	}

	private bool TryActivateHostMapNetworkRoots(
		out GameObject[] authoritativeRoots,
		out string issue )
	{
		authoritativeRoots = [];
		issue = null;

		if ( !Networking.IsHost )
			return true;

		var mapHost = MapInstance?.GameObject;
		if ( mapHost is null || !mapHost.IsValid )
		{
			issue = "The loaded map has no valid MapInstance content host for its " +
				"authoritative Object-mode roots.";
			return false;
		}

		var authoredRoots = mapHost.GetAllObjects( true )
			.Where( candidate =>
				candidate is not null &&
				candidate.IsValid &&
				candidate != mapHost &&
				candidate.NetworkMode == NetworkMode.Object &&
				!HasObjectModeAncestor( candidate, mapHost ) )
			.ToArray();
		var authoredRootStates = authoredRoots
			.Select( root => (
				Root: root,
				Parent: root.Parent,
				WorldTransform: root.WorldTransform,
				Enabled: root.Enabled,
				Flags: root.Flags,
				WasNativeSpawn: root.Network.Active ) )
			.ToArray();
		var sourceIds = authoredRootStates
			.Select( state => state.Root.Id )
			.ToHashSet();
		var nativeSpawnCount = authoredRootStates.Count( state =>
			state.WasNativeSpawn );
		GameObject sourceBatch = null;
		GameObject clonedBatch = null;
		GameObject[] runtimeRoots = [];

		try
		{
			// Authored GUIDs are stable across a same-map reload. Reusing them for
			// network roots lets a replacement create race the departing proxy's
			// delete on clients. Clone all roots in one batch so the new generation
			// receives fresh GameObject/component IDs while cross-root references are
			// remapped by the engine's normal clone map.
			sourceBatch = new GameObject(
				mapHost,
				false,
				"Map Network Root Clone Source" )
			{
				NetworkMode = NetworkMode.Never
			};

			foreach ( var state in authoredRootStates )
			{
				// Active native network roots carry NotSaved. GameObject.Clone
				// intentionally omits children with that flag, so clear it only
				// while building the replacement generation. NetworkSpawn restores
				// the runtime flag on the fresh authoritative clone.
				state.Root.Flags &= ~GameObjectFlags.NotSaved;
				state.Root.SetParent( sourceBatch, keepWorldPosition: true );
				state.Root.WorldTransform = state.WorldTransform;
			}

			clonedBatch = sourceBatch.Clone(
				default,
				mapHost,
				startEnabled: false,
				name: "Map Network Root Clone Result" );
			if ( clonedBatch is null || !clonedBatch.IsValid )
				throw new InvalidOperationException( "GameObject.Clone returned no batch." );

			runtimeRoots = clonedBatch.Children.ToArray();
			if ( runtimeRoots.Length != authoredRootStates.Length )
			{
				throw new InvalidOperationException(
					$"GameObject.Clone produced {runtimeRoots.Length} of " +
					$"{authoredRootStates.Length} Object roots." );
			}

			for ( var index = 0; index < runtimeRoots.Length; index++ )
			{
				var runtimeRoot = runtimeRoots[index];
				var authoredState = authoredRootStates[index];
				runtimeRoot.Enabled = false;
					runtimeRoot.SetParent( mapHost, keepWorldPosition: true );
					runtimeRoot.WorldTransform = authoredState.WorldTransform;
					runtimeRoot.Flags = authoredState.Flags & ~GameObjectFlags.NotSaved;
					runtimeRoot.NetworkMode = NetworkMode.Object;
			}

			if ( runtimeRoots.Select( root => root.Id ).Distinct().Count() !=
				runtimeRoots.Length ||
				runtimeRoots.Any( root => sourceIds.Contains( root.Id ) ) )
			{
				throw new InvalidOperationException(
					"GameObject.Clone did not assign generation-unique root IDs." );
			}

			// Removing the source batch also withdraws MapInstance's short-lived
			// native top-level spawns. Their stable IDs are never published in the
			// manifest and cannot satisfy the new generation's client barrier.
			sourceBatch.DestroyImmediate();
			sourceBatch = null;
			clonedBatch.DestroyImmediate();
			clonedBatch = null;

			for ( var index = 0; index < runtimeRoots.Length; index++ )
			{
				var runtimeRoot = runtimeRoots[index];
				runtimeRoot.Enabled = authoredRootStates[index].Enabled;
				if ( !runtimeRoot.NetworkSpawn() )
				{
					throw new InvalidOperationException(
						$"NetworkSpawn returned false for '{runtimeRoot.Name}'." );
				}
			}
		}
		catch ( Exception exception )
		{
			foreach ( var runtimeRoot in runtimeRoots )
			{
				if ( runtimeRoot is not null && runtimeRoot.IsValid )
					runtimeRoot.DestroyImmediate();
			}

			if ( clonedBatch is not null && clonedBatch.IsValid )
				clonedBatch.DestroyImmediate();

			if ( sourceBatch is not null && sourceBatch.IsValid )
			{
				foreach ( var state in authoredRootStates )
				{
					if ( state.Root is null || !state.Root.IsValid )
						continue;

					state.Root.SetParent(
						state.Parent,
						keepWorldPosition: true );
					state.Root.WorldTransform = state.WorldTransform;
					state.Root.Flags = state.Flags;
				}

				sourceBatch.DestroyImmediate();
			}

			issue = $"Map-authored Object roots could not become a unique host " +
				$"network generation: {exception.Message}";
			return false;
		}

		authoritativeRoots = runtimeRoots
			.Where( candidate =>
				candidate is not null &&
				candidate.IsValid &&
				candidate.Network.Active &&
				candidate.Network.RootGameObject == candidate )
			.OrderBy( candidate => candidate.Id )
			.ToArray();

		if ( authoritativeRoots.Length != authoredRoots.Length )
		{
			issue = $"The host activated {authoritativeRoots.Length} of " +
				$"{authoredRoots.Length} map-authored Object-mode roots.";
			return false;
		}

		Log.Info(
			$"Large Lad host activated {authoritativeRoots.Length} generation-unique " +
			$"map-authored Object-mode roots ({nativeSpawnCount} native stable-ID " +
			"spawns replaced)." );
		return true;
	}

	private void ReconcileClientAuthoredMapNetworkRoots()
	{
		if ( Networking.IsHost )
			return;

		var mapHost = MapInstance?.GameObject;
		if ( mapHost is null || !mapHost.IsValid )
			return;

		var disposableRoots = mapHost.GetAllObjects( true )
			.Where( candidate =>
				candidate is not null &&
				candidate.IsValid &&
				candidate != mapHost &&
				candidate.NetworkMode == NetworkMode.Object &&
				!candidate.Network.Active &&
				!HasObjectModeAncestor( candidate, mapHost ) )
			.ToArray();

		foreach ( var disposableRoot in disposableRoots )
		{
			if ( disposableRoot.IsValid && !disposableRoot.Network.Active )
				disposableRoot.DestroyImmediate();
		}

		var authoritativeRootCount = mapHost.GetAllObjects( true )
			.Count( candidate =>
				candidate is not null &&
				candidate.IsValid &&
				candidate != mapHost &&
				candidate.NetworkMode == NetworkMode.Object &&
				candidate.Network.Active &&
				candidate.Network.RootGameObject == candidate );

		Log.Info(
			$"Large Lad client map reconciliation discarded " +
			$"{disposableRoots.Length} locally authored Object-mode roots and " +
			$"preserved {authoritativeRootCount} active authoritative roots." );
	}

	private static bool HasObjectModeAncestor(
		GameObject gameObject,
		GameObject mapHost )
	{
		for ( var current = gameObject.Parent;
			current is not null && current != mapHost;
			current = current.Parent )
		{
			if ( current.NetworkMode == NetworkMode.Object )
				return true;
		}

		return false;
	}

	private void LogLoadedGameplayObjectSummary( string mapIdentifier )
	{
		var mapHost = MapInstance?.GameObject;
		if ( mapHost is null || !mapHost.IsValid )
			return;

		var findMode = FindMode.EverythingInSelfAndDescendants;
		var barricades = mapHost.Components
			.GetAll<LargeLadBarricade>( findMode )
			.Where( component => component is not null && component.IsValid )
			.ToArray();
		var dodgeballs = mapHost.Components
			.GetAll<LargeLadDodgeballPickup>( findMode )
			.Where( component => component is not null && component.IsValid )
			.ToArray();
		var weaponPickups = mapHost.Components
			.GetAll<LargeLadWeaponPickup>( findMode )
			.Where( component => component is not null && component.IsValid )
			.ToArray();
		var minionPassages = mapHost.Components
			.GetAll<LargeLadMinionPassage>( findMode )
			.Where( component => component is not null && component.IsValid )
			.ToArray();
		var barricadesWithPresentation = barricades.Count( component =>
			component.GameObject.Components
				.GetAll<Renderer>( findMode )
				.Any( renderer => renderer is not null && renderer.IsValid ) );
		var dodgeballsWithPresentation = dodgeballs.Count( component =>
			component.GameObject.Components
				.GetAll<Renderer>( findMode )
				.Any( renderer => renderer is not null && renderer.IsValid ) );
		var weaponPickupsWithPresentation = weaponPickups.Count( component =>
			component.GameObject.Components
				.GetAll<Renderer>( findMode )
				.Any( renderer => renderer is not null && renderer.IsValid ) );
		var minionPassagesWithPresentation = minionPassages.Count( component =>
			component.GameObject.Components
				.GetAll<Renderer>( findMode )
				.Any( renderer => renderer is not null && renderer.IsValid ) );

		Log.Info(
			$"Large Lad map object summary on " +
			$"{(Networking.IsHost ? "host" : "client")} for " +
			$"'{mapIdentifier}': barricades={barricades.Length} " +
			$"(presented={barricadesWithPresentation}), " +
			$"dodgeballs={dodgeballs.Length} " +
			$"(presented={dodgeballsWithPresentation}), " +
			$"weapon-pickups={weaponPickups.Length} " +
			$"(presented={weaponPickupsWithPresentation}), " +
			$"minion-passages={minionPassages.Length} " +
			$"(presented={minionPassagesWithPresentation})." );
	}

	private static bool IsDescendantOf(
		GameObject gameObject,
		GameObject expectedAncestor )
	{
		for ( var current = gameObject.Parent;
			current is not null;
			current = current.Parent )
		{
			if ( current == expectedAncestor )
				return true;
		}

		return false;
	}

	private void HandleMapUnloaded()
	{
		if ( Networking.IsHost &&
			MapState != LargeLadMapSessionState.Unloading )
		{
			Log.Info(
				$"Ignored stale MapInstance unload completion while the map " +
				$"session was {MapState}." );
			return;
		}

		unloadNotificationVersion++;
		InvalidateLoadedMapResolution();

		if ( Networking.IsHost )
		{
			SetMapState( LargeLadMapSessionState.Unloading );
			BeginMapTransitionCleanup();
		}

		Log.Info(
			"Large Lad map content unloaded; the persistent session " +
			"bootstrap remains active and round flow is closed." );

		if ( !Networking.IsHost )
		{
			return;
		}

		if ( string.IsNullOrWhiteSpace( pendingReloadMapName ) )
		{
			SetMapState( LargeLadMapSessionState.Unloaded );
			return;
		}

		var reloadMapName = pendingReloadMapName;
		pendingReloadMapName = null;
		BeginLoadingMap( reloadMapName );
	}

	private void BeginLoadingMap( string mapName )
	{
		if ( !Networking.IsHost )
			return;

		InvalidateLoadedMapResolution();
		var selectedMapName = mapName?.Trim() ?? string.Empty;

		if ( string.IsNullOrWhiteSpace( selectedMapName ) )
		{
			ClearMapNameOnAllPeers();
			SetMapState( LargeLadMapSessionState.Unloaded );
			return;
		}

		// Loading is published before MapName because assigning MapName begins the
		// built-in asynchronous load operation.
		SetMapState( LargeLadMapSessionState.Loading );
		NotifyMapLoadAttemptStarted( selectedMapName );

		try
		{
			hostLoadingMapName = selectedMapName;
			Log.Info(
				$"Host is loading Large Lad map '{selectedMapName}' before " +
				"releasing it to connected clients." );
			ApplyMapNameLocally( selectedMapName, "host-first map load" );
		}
		catch ( Exception exception )
		{
			SetMapState( LargeLadMapSessionState.Failed );
			HandleSelectedMapFailure(
				$"MapInstance rejected the map load: {exception.Message}" );
		}
	}

	private void BeginMapTransitionCleanup()
	{
		if ( !Networking.IsHost || mapTransitionCleanupStarted )
			return;

		mapTransitionCleanupStarted = true;
		GameManager?.HandleMapTransition( this );
	}

	private void FailLoadedMapResolution( string issue )
	{
		FailLoadedMapResolution( new[] { issue } );
	}

	private void FailLoadedMapResolution(
		IEnumerable<string> issues )
	{
		CurrentMapDescriptor = null;
		loadedMapValidationIssues.Clear();
		loadedMapValidationIssues.AddRange(
			issues?.Where( issue => !string.IsNullOrWhiteSpace( issue ) ) ?? [] );

		if ( Networking.IsHost )
		{
			foreach ( var issue in loadedMapValidationIssues )
				Log.Error( $"Map manifest blocks readiness: {issue}" );

			SetMapState( LargeLadMapSessionState.Failed );
			HandleSelectedMapFailure(
				loadedMapValidationIssues.Count == 0
					? "the loaded map failed manifest/descriptor validation"
					: string.Join( " | ", loadedMapValidationIssues ) );
		}
	}

	private void InvalidateLoadedMapResolution()
	{
		loadedMapPreparationVersion++;
		CurrentMapDescriptor = null;
		loadedMapValidationIssues.Clear();
	}

	private void SetMapState( LargeLadMapSessionState newState )
	{
		if ( !Networking.IsHost || MapState == newState )
			return;

		var oldState = MapState;
		MapState = newState;
		Log.Info(
			$"Large Lad map session state changed from {oldState} to " +
			$"{newState}." );
	}

	private string GetHostSelectedOrLoadingMapName()
	{
		if ( Networking.IsHost && !string.IsNullOrWhiteSpace( hostLoadingMapName ) )
			return hostLoadingMapName.Trim();

		return CurrentMapName?.Trim() ?? string.Empty;
	}

	private void PublishLoadedMapNameToPeers(
		string mapName,
		IEnumerable<GameObject> authoritativeMapNetworkRoots )
	{
		var selectedMapName = mapName?.Trim() ?? string.Empty;
		var networkRootManifest = BuildMapNetworkRootManifest(
			selectedMapName,
			authoritativeMapNetworkRoots );

		CurrentMapNetworkRootManifest = networkRootManifest;
		CurrentMapName = selectedMapName;
		Log.Info(
			$"Host loaded and validated Large Lad map '{CurrentMapName}'; " +
			"published its authoritative Object-root manifest to connected clients." );

		if ( Game.IsPlaying && Networking.IsHost )
		{
			ApplySelectedMapNameOnClients(
				CurrentMapName,
				CurrentMapNetworkRootManifest );
		}
	}

	private void ClearMapNameOnAllPeers()
	{
		hostLoadingMapName = null;
		RequestApplyMapNameLocally(
			string.Empty,
			string.Empty,
			"host map unload" );
		CurrentMapNetworkRootManifest = string.Empty;
		CurrentMapName = string.Empty;

		if ( Game.IsPlaying && Networking.IsHost )
			ApplySelectedMapNameOnClients( string.Empty, string.Empty );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	private void ApplySelectedMapNameOnClients(
		string mapName,
		string networkRootManifest )
	{
		if ( Networking.IsHost )
			return;

		RequestApplyMapNameLocally(
			mapName,
			networkRootManifest,
			"reliable host RPC" );
	}

	private void OnCurrentMapNameChanged(
		string oldMapName,
		string newMapName )
	{
		if ( !Networking.IsHost && !string.Equals(
			oldMapName,
			newMapName,
			StringComparison.OrdinalIgnoreCase ) )
		{
			InvalidateLoadedMapResolution();
		}

		if ( !Networking.IsHost )
		{
			RequestApplyMapNameLocally(
				newMapName,
				CurrentMapNetworkRootManifest,
				"synchronized state change" );
		}
	}

	private void OnMapNetworkRootManifestChanged(
		string oldManifest,
		string newManifest )
	{
		if ( Networking.IsHost )
			return;

		RequestApplyMapNameLocally(
			CurrentMapName,
			newManifest,
			"synchronized Object-root manifest" );
	}

	private void ApplyCurrentMapName()
	{
		RequestApplyMapNameLocally(
			CurrentMapName,
			CurrentMapNetworkRootManifest,
			"client startup reconciliation" );
	}

	private void RequestApplyMapNameLocally(
		string mapName,
		string networkRootManifest,
		string source )
	{
		var desiredMapName = mapName?.Trim() ?? string.Empty;
		if ( Networking.IsHost || string.IsNullOrWhiteSpace( desiredMapName ) )
		{
			pendingClientMapName = null;
			pendingClientMapNetworkRootManifest = null;
			loggedPendingClientNetworkRootBarrier = false;
			ApplyMapNameLocally( desiredMapName, source );
			return;
		}

		pendingClientMapName = desiredMapName;
		pendingClientMapNetworkRootManifest = networkRootManifest;
		TryApplyPendingClientMap();
	}

	private void TryApplyPendingClientMap()
	{
		if ( Networking.IsHost || string.IsNullOrWhiteSpace( pendingClientMapName ) )
			return;

		if ( !TryParseMapNetworkRootManifest(
			pendingClientMapNetworkRootManifest,
			pendingClientMapName,
			out var expectedNetworkRootIds ) )
		{
			return;
		}

		var missingRootCount = expectedNetworkRootIds.Count( id =>
		{
			var candidate = Scene?.Directory?.FindByGuid( id );
			return candidate is null ||
				!candidate.IsValid ||
				!candidate.Network.Active ||
				candidate.Network.RootGameObject != candidate;
		} );

		if ( missingRootCount > 0 )
		{
			if ( !loggedPendingClientNetworkRootBarrier )
			{
				Log.Info(
					$"Large Lad client is waiting for {missingRootCount} of " +
					$"{expectedNetworkRootIds.Length} authoritative map Object " +
					$"roots before loading '{pendingClientMapName}'." );
				loggedPendingClientNetworkRootBarrier = true;
			}

			return;
		}

		var readyMapName = pendingClientMapName;
		pendingClientMapName = null;
		pendingClientMapNetworkRootManifest = null;
		loggedPendingClientNetworkRootBarrier = false;
		Log.Info(
			$"Large Lad client received all {expectedNetworkRootIds.Length} " +
			$"authoritative map Object roots; loading '{readyMapName}'." );
		ApplyMapNameLocally( readyMapName, "authoritative Object-root barrier" );
	}

	private void ApplyMapNameLocally( string mapName, string source )
	{
		ResolveBootstrapReferences();
		AttachMapCallbacks();

		var desiredMapName = mapName?.Trim() ?? string.Empty;

		if ( MapInstance is null )
		{
			Log.Error(
				$"Large Lad could not reconcile map '{desiredMapName}' from " +
				$"{source}: this peer has no root-level MapInstance." );
			return;
		}

		var localMapName = MapInstance.MapName?.Trim() ?? string.Empty;
		if ( string.Equals(
			localMapName,
			desiredMapName,
			StringComparison.OrdinalIgnoreCase ) )
		{
			return;
		}

		MapInstance.MapName = desiredMapName;
	}

	private static string BuildMapNetworkRootManifest(
		string mapName,
		IEnumerable<GameObject> authoritativeRoots )
	{
		var ids = authoritativeRoots is null
			? Enumerable.Empty<string>()
			: authoritativeRoots
				.Where( root => root is not null && root.IsValid )
				.Select( root => root.Id.ToString( "D" ) )
				.OrderBy( id => id, StringComparer.Ordinal );
		return $"1|{mapName?.Trim()}|{string.Join( ',', ids )}";
	}

	private static bool TryParseMapNetworkRootManifest(
		string manifest,
		string expectedMapName,
		out Guid[] networkRootIds )
	{
		networkRootIds = [];
		if ( string.IsNullOrWhiteSpace( manifest ) )
			return false;

		var fields = manifest.Split( '|', 3 );
		if ( fields.Length != 3 ||
			fields[0] != "1" ||
			!string.Equals(
				fields[1],
				expectedMapName?.Trim(),
				StringComparison.OrdinalIgnoreCase ) )
		{
			return false;
		}

		if ( string.IsNullOrWhiteSpace( fields[2] ) )
			return true;

		var serializedIds = fields[2].Split(
			',',
			StringSplitOptions.RemoveEmptyEntries |
			StringSplitOptions.TrimEntries );
		var parsedIds = new Guid[serializedIds.Length];
		for ( var index = 0; index < serializedIds.Length; index++ )
		{
			if ( !Guid.TryParse( serializedIds[index], out parsedIds[index] ) )
				return false;
		}

		networkRootIds = parsedIds;
		return true;
	}

	private bool CanControlMap()
	{
		ResolveBootstrapReferences();

		if ( MapInstance is null )
		{
			Log.Error(
				"Large Lad session cannot control maps without its bootstrap " +
				"MapInstance reference." );
			return false;
		}

		if ( Game.IsPlaying && !Networking.IsHost )
		{
			Log.Warning( "Only the host can change the Large Lad map." );
			return false;
		}

		return true;
	}

	private void AttachMapCallbacks()
	{
		if ( subscribedMapInstance == MapInstance )
			return;

		DetachMapCallbacks();

		if ( MapInstance is null )
			return;

		subscribedMapInstance = MapInstance;
		subscribedMapInstance.OnMapLoaded += HandleMapLoaded;
		subscribedMapInstance.OnMapUnloaded += HandleMapUnloaded;
	}

	private void DetachMapCallbacks()
	{
		if ( subscribedMapInstance is null )
			return;

		if ( subscribedMapInstance.IsValid )
		{
			subscribedMapInstance.OnMapLoaded -= HandleMapLoaded;
			subscribedMapInstance.OnMapUnloaded -= HandleMapUnloaded;
		}

		subscribedMapInstance = null;
	}

	private void AttachToSceneRegistry()
	{
		if ( registeredScene is not null && registeredScene != Scene )
			DetachFromSceneRegistry();

		registeredScene = Scene;
		LargeLadSceneRegistry.RegisterSessionCoordinator(
			registeredScene,
			this );
	}

	private void DetachFromSceneRegistry()
	{
		if ( registeredScene is null )
			return;

		LargeLadSceneRegistry.UnregisterSessionCoordinator(
			registeredScene,
			this );
		registeredScene = null;
	}

	private bool IsUsableBootstrapComponent( Component component )
	{
		return component is not null &&
			component.IsValid &&
			component.Scene == Scene &&
			component.GameObject == GameObject;
	}

	private bool IsUsableMapInstance( MapInstance mapInstance )
	{
		return mapInstance is not null &&
			mapInstance.IsValid &&
			mapInstance.GameObject.IsRoot;
	}
}
