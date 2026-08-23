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
public sealed class LargeLadSessionCoordinator : Component
{
	[Property]
	public MapInstance MapInstance { get; set; }

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
	private string pendingReloadMapName;
	private int unloadNotificationVersion;
	private bool mapTransitionCleanupStarted;
	private int loadedMapPreparationVersion;
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

		if ( Networking.IsHost && string.IsNullOrWhiteSpace( CurrentMapName ) )
		{
			BeginLoadingMap( StartupMap );
		}
		else
		{
			ApplyCurrentMapName();
		}

		// OnLoad can complete before this component reaches OnStart. This is a
		// one-time lifecycle catch-up, not readiness polling; all later changes
		// come directly from MapInstance callbacks.
		if ( Networking.IsHost &&
			MapInstance?.IsLoaded == true &&
			!IsMapReady )
		{
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
			CurrentMapName,
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
			var unloadedMapName = CurrentMapName;
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
			string.IsNullOrWhiteSpace( CurrentMapName ) )
		{
			return;
		}

		var unloadedMapName = CurrentMapName;
		pendingReloadMapName = null;
		UnloadSelectedMap( unloadedMapName );
	}

	[Button( "Reload Current Map" )]
	public void ReloadCurrentMap()
	{
		if ( !CanControlMap() ||
			string.IsNullOrWhiteSpace( CurrentMapName ) )
		{
			return;
		}

		// Clear the selection before unloading so MapInstance does not reload it
		// on its next update. The unload callback restores the selected ident and
		// lets MapInstance perform a normal asynchronous replacement load.
		pendingReloadMapName = CurrentMapName;
		var unloadedMapName = CurrentMapName;
		UnloadSelectedMap( unloadedMapName );
	}

	private void UnloadSelectedMap( string unloadedMapName )
	{
		InvalidateLoadedMapResolution();
		SetMapState( LargeLadMapSessionState.Unloading );
		BeginMapTransitionCleanup();

		var unloadVersionBefore = unloadNotificationVersion;
		var localSceneChildren = unloadedMapName?.EndsWith(
			".scene",
			StringComparison.OrdinalIgnoreCase ) == true
			? MapInstance.GameObject.Children.ToArray()
			: null;

		// State and gameplay are closed before clearing MapName because changing
		// it can itself begin MapInstance's asynchronous unload.
		SelectMapName( string.Empty );
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

		// Package maps invoke OnMapUnloaded directly. Current direct scene-map
		// resources do not, so close the same readiness contract here when the
		// callback did not run, even if that map failed readiness validation.
		// This remains lifecycle-driven without polling.
		if ( localSceneChildren is not null &&
			unloadNotificationVersion == unloadVersionBefore )
		{
			HandleMapUnloaded();
		}
	}

	internal void ResolveBootstrapReferences()
	{
		if ( !IsUsableMapInstance( MapInstance ) )
		{
			MapInstance = GameObject.Components.Get<MapInstance>(
				FindMode.EverythingInSelfAndDescendants );
		}

		if ( !IsUsableBootstrapComponent( GameManager ) )
			GameManager = Components.Get<LargeLadGameManager>();
	}

	private async void HandleMapLoaded()
	{
		ResolveBootstrapReferences();

		if ( !Networking.IsHost )
		{
			SuppressClientAuthoredNetworkCopies();
		}
		else if ( MapState != LargeLadMapSessionState.Loading ||
			string.IsNullOrWhiteSpace( CurrentMapName ) )
		{
			Log.Warning(
				$"Ignored a completed map load while the Large Lad session was " +
				$"{MapState}." );
			return;
		}

		var preparationVersion = ++loadedMapPreparationVersion;
		var preparedMapIdentifier = CurrentMapName?.Trim() ?? string.Empty;
		var embeddedBootstrapIssues = GetEmbeddedBootstrapValidationIssues(
			preparedMapIdentifier );

		if ( embeddedBootstrapIssues.Count > 0 )
		{
			FailLoadedMapResolution( embeddedBootstrapIssues );
			return;
		}

		var profiles = MapInstance?.GameObject?.Components
			.GetAll<LargeLadMapProfile>(
				FindMode.EverythingInSelfAndDescendants )
			.Where( profile =>
				profile is not null &&
				profile.IsValid &&
				profile.Enabled )
			.ToArray() ?? [];

		if ( profiles.Length != 1 )
		{
			var issue = profiles.Length == 0
				? $"Map '{preparedMapIdentifier}' is missing its required enabled " +
					"LargeLadMapProfile. Place exactly one profile in the map and " +
					"assign its Large Lad Map Manifest."
				: $"Map '{preparedMapIdentifier}' contains {profiles.Length} enabled " +
					"LargeLadMapProfile components. Keep exactly one map profile.";
			FailLoadedMapResolution( issue );
			return;
		}

		var packageMetadata = await LargeLadMapCatalog
			.FetchPublishedPackageMetadata( preparedMapIdentifier );

		if ( preparationVersion != loadedMapPreparationVersion ||
			!string.Equals(
				CurrentMapName,
				preparedMapIdentifier,
				StringComparison.OrdinalIgnoreCase ) ||
			(Networking.IsHost &&
				MapState != LargeLadMapSessionState.Loading) )
		{
			return;
		}

		if ( !LargeLadMapCatalog.TryResolveLoadedMap(
			profiles[0].Manifest,
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

		if ( !Networking.IsHost )
			return;

		mapTransitionCleanupStarted = false;
		PromoteNestedMapNetworkObjects();
		var isReady = GameManager?.PrepareLoadedMap( this, descriptor ) == true;
		SetMapState(
			isReady
				? LargeLadMapSessionState.Ready
				: LargeLadMapSessionState.Failed );

		if ( isReady )
		{
			Log.Info(
				$"Large Lad map '{descriptor.DisplayName}' " +
				$"({descriptor.MapInstanceIdentifier}) is ready; the " +
				"persistent session bootstrap remained active." );
		}
		else
		{
			Log.Error(
				$"Large Lad map '{MapInstance?.MapName}' loaded but did not " +
				"satisfy the blocking map contract. Round flow remains closed." );
		}
	}

	private void SuppressClientAuthoredNetworkCopies()
	{
		if ( Networking.IsHost || MapInstance is null )
			return;

		var mapHost = MapInstance.GameObject;
		var authoredNetworkCopies = GetNestedMapNetworkRoots( mapHost );

		foreach ( var sourceObject in authoredNetworkCopies )
		{
			sourceObject.Components
				.Get<LargeLadDodgeballPickup>( FindMode.EverythingInSelf )?
				.SuppressClientAuthoredCopy();
			sourceObject.Enabled = false;
		}

		Log.Info(
			$"Suppressed {authoredNetworkCopies.Length} client-authored " +
			"network/physics copies beneath the loaded map." );
	}

	private void PromoteNestedMapNetworkObjects()
	{
		if ( !Networking.IsHost || MapInstance is null )
			return;

		var mapHost = MapInstance.GameObject;

		// Nested scene-map network roots depend on Snapshot parents that are not
		// stable in the persistent shell's late-join graph. Dynamic physics props
		// have the same problem when MapInstance promotes them automatically.
		// Promote only those roots beneath the known map host. Static/local map
		// content and already-direct network roots stay untouched, and keeping the
		// authored objects preserves their network identities for late joiners.
		var authoredNetworkRoots = GetNestedMapNetworkRoots( mapHost );

		foreach ( var sourceObject in authoredNetworkRoots )
			PromoteNestedMapNetworkObject( sourceObject, mapHost );
	}

	private static void PromoteNestedMapNetworkObject(
		GameObject sourceObject,
		GameObject mapHost )
	{
		var sourceParent = sourceObject.Parent;
		var sourceWorldTransform = sourceObject.WorldTransform;
		var sourceNetworkMode = sourceObject.NetworkMode;

		try
		{
			// Reparent in place so authored identities, exact transforms, and any
			// live physics bodies survive the promotion. Cloning ModelPhysics also
			// rebuilds articulated bodies around the scene origin.
			sourceObject.SetParent( mapHost, keepWorldPosition: true );
			sourceObject.WorldTransform = sourceWorldTransform;
			sourceObject.NetworkMode = NetworkMode.Object;

			if ( sourceObject.Network.Active &&
				sourceObject.Network.RootGameObject == sourceObject )
			{
				sourceObject.Network.Refresh();
			}
			else if ( !sourceObject.NetworkSpawn() )
			{
				throw new InvalidOperationException(
					"NetworkSpawn returned false." );
			}
		}
		catch ( Exception exception )
		{
			sourceObject.SetParent( sourceParent, keepWorldPosition: true );
			sourceObject.WorldTransform = sourceWorldTransform;
			sourceObject.NetworkMode = sourceNetworkMode;
			Log.Error(
				$"{sourceObject.Name}: failed to promote its persistent-map " +
				$"network object: {exception.Message}" );
		}
	}

	private static GameObject[] GetNestedMapNetworkRoots( GameObject mapHost )
	{
		if ( mapHost is null )
			return [];

		return mapHost.GetAllObjects( true )
			.Where( candidate =>
				candidate != mapHost &&
				candidate.Parent != mapHost &&
				IsMapNetworkRoot( candidate ) &&
				!HasMapNetworkRootAncestor( candidate, mapHost ) )
			.ToArray();
	}

	private static bool HasMapNetworkRootAncestor(
		GameObject gameObject,
		GameObject mapHost )
	{
		for ( var current = gameObject.Parent;
			current is not null && current != mapHost;
			current = current.Parent )
		{
			if ( IsMapNetworkRoot( current ) )
				return true;
		}

		return false;
	}

	private static bool IsMapNetworkRoot( GameObject gameObject )
	{
		if ( gameObject.NetworkMode == NetworkMode.Object )
			return true;

		return IsDynamicPhysicsProp( gameObject );
	}

	private static bool IsDynamicPhysicsProp( GameObject gameObject )
	{
		if ( gameObject.Components.Get<Prop>(
			FindMode.EverythingInSelf ) is null )
		{
			return false;
		}

		return gameObject.Components.Get<ModelPhysics>(
				FindMode.EverythingInSelf ) is not null ||
			gameObject.Components.Get<Rigidbody>(
				FindMode.EverythingInSelf ) is not null;
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
			SelectMapName( string.Empty );
			SetMapState( LargeLadMapSessionState.Unloaded );
			return;
		}

		// Loading is published before MapName because assigning MapName begins the
		// built-in asynchronous load operation.
		SetMapState( LargeLadMapSessionState.Loading );
		SelectMapName( selectedMapName );
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
		}
	}

	private IReadOnlyList<string> GetEmbeddedBootstrapValidationIssues(
		string mapIdentifier )
	{
		if ( Scene is null )
			return Array.Empty<string>();

		var unexpected = new List<Component>();
		AddUnexpectedComponents(
			unexpected,
			Scene.GetAllComponents<LargeLadGameManager>(),
			GameManager );
		AddUnexpectedComponents(
			unexpected,
			Scene.GetAllComponents<LargeLadSessionCoordinator>(),
			this );
		AddUnexpectedComponents(
			unexpected,
			Scene.GetAllComponents<LargeLadSpawnAllocator>(),
			GameManager?.SpawnAllocator );
		AddUnexpectedComponents(
			unexpected,
			Scene.GetAllComponents<NetworkHelper>(),
			GameManager?.NetworkHelper );
		AddUnexpectedComponents(
			unexpected,
			Scene.GetAllComponents<MapInstance>(),
			MapInstance );

		if ( unexpected.Count == 0 )
			return Array.Empty<string>();

		var examples = string.Join(
			", ",
			unexpected
				.Select( component =>
					$"{component.GetType().Name} on " +
					$"'{component.GameObject.Name}'" )
				.Distinct()
				.Take( 8 ) );

		return new[]
		{
			$"Map '{mapIdentifier}' contains persistent gameplay-bootstrap " +
			$"components ({examples}). Remove " +
			"prefabs/large_lad_gameplay.prefab and any map-local MapInstance, " +
			"manager, coordinator, allocator, or NetworkHelper. A content map " +
			"needs exactly one LargeLadMapProfile; game_shell owns the only " +
			"gameplay bootstrap and loader."
		};
	}

	private static void AddUnexpectedComponents<T>(
		List<Component> destination,
		IEnumerable<T> candidates,
		T expected )
		where T : Component
	{
		destination.AddRange(
			candidates.Where( candidate =>
				candidate is not null &&
				candidate.IsValid &&
				candidate != expected ) );
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

	private void SelectMapName( string mapName )
	{
		CurrentMapName = mapName?.Trim() ?? string.Empty;
		ApplyCurrentMapName();
	}

	private void OnCurrentMapNameChanged(
		string oldMapName,
		string newMapName )
	{
		if ( !string.Equals(
			oldMapName,
			newMapName,
			StringComparison.OrdinalIgnoreCase ) )
		{
			InvalidateLoadedMapResolution();
		}

		ApplyCurrentMapName();
	}

	private void ApplyCurrentMapName()
	{
		ResolveBootstrapReferences();

		if ( MapInstance is not null )
			MapInstance.MapName = CurrentMapName ?? string.Empty;
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
			mapInstance.Scene == Scene &&
			mapInstance.GameObject.Parent == GameObject;
	}
}
