using System;
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

	[Sync( SyncFlags.FromHost ), Change( nameof( OnCurrentMapNameChanged ) )]
	public string CurrentMapName { get; private set; }

	[Sync( SyncFlags.FromHost )]
	public LargeLadMapSessionState MapState { get; private set; } =
		LargeLadMapSessionState.Unloaded;

	public bool IsMapReady => MapState == LargeLadMapSessionState.Ready;

	private Scene registeredScene;
	private MapInstance subscribedMapInstance;
	private string pendingReloadMapName;
	private int unloadNotificationVersion;
	private bool mapTransitionCleanupStarted;

	protected override void OnEnabled()
	{
		base.OnEnabled();
		ResolveBootstrapReferences();
		AttachMapCallbacks();
		AttachToSceneRegistry();
	}

	protected override void OnDisabled()
	{
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
		ResolveBootstrapReferences();
		AttachMapCallbacks();
		AttachToSceneRegistry();
	}

	protected override void OnStart()
	{
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

	private void HandleMapLoaded()
	{
		if ( !Networking.IsHost )
			return;

		ResolveBootstrapReferences();

		if ( MapState != LargeLadMapSessionState.Loading ||
			string.IsNullOrWhiteSpace( CurrentMapName ) )
		{
			Log.Warning(
				$"Ignored a completed map load while the Large Lad session was " +
				$"{MapState}." );
			return;
		}

		mapTransitionCleanupStarted = false;
		var isReady = GameManager?.PrepareLoadedMap( this ) == true;
		SetMapState(
			isReady
				? LargeLadMapSessionState.Ready
				: LargeLadMapSessionState.Failed );

		if ( isReady )
		{
			Log.Info(
				$"Large Lad map '{MapInstance.MapName}' is ready; the " +
				"persistent session bootstrap remained active." );
		}
		else
		{
			Log.Error(
				$"Large Lad map '{MapInstance?.MapName}' loaded but did not " +
				"satisfy the blocking map contract. Round flow remains closed." );
		}
	}

	private void HandleMapUnloaded()
	{
		unloadNotificationVersion++;

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
