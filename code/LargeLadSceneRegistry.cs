using Sandbox;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

/// <summary>
/// Bridges component lifecycle registration to the game manager in the same
/// scene. There is deliberately no global "current" manager: every operation
/// is keyed by the component's exact scene.
/// </summary>
internal static class LargeLadSceneRegistry
{
	private sealed class SceneRegistrations
	{
		public readonly HashSet<LargeLadGameManager> Managers = new();
		public readonly HashSet<LargeLadSessionCoordinator> SessionCoordinators =
			new();
		public readonly HashSet<LargeLadPlayer> Players = new();
		public readonly HashSet<ILargeLadRoundResettable> Resettables = new();
		public readonly Dictionary<NetworkHelper, GameObject>
			SuppressedPlayerPrefabs = new();
		public LargeLadGameManager GameplayOwner;
		public string LastBlockingBootstrapSignature;
		public bool GameplayOwnershipDirty = true;
	}

	private static readonly ConditionalWeakTable<Scene, SceneRegistrations>
		RegistrationsByScene = new();

	public static void RegisterManager(
		Scene scene,
		LargeLadGameManager manager )
	{
		if ( scene is null || !IsActiveInScene( manager, scene ) )
			return;

		var registrations = RegistrationsByScene.GetValue(
			scene,
			_ => new SceneRegistrations() );
		Prune( scene, registrations );

		if ( registrations.Managers.Add( manager ) )
			registrations.GameplayOwnershipDirty = true;

		EnsureGameplayOwner( scene, registrations );
	}

	public static void RegisterSessionCoordinator(
		Scene scene,
		LargeLadSessionCoordinator coordinator )
	{
		if ( scene is null || !IsActiveInScene( coordinator, scene ) )
			return;

		var registrations = RegistrationsByScene.GetValue(
			scene,
			_ => new SceneRegistrations() );
		Prune( scene, registrations );

		if ( registrations.SessionCoordinators.Add( coordinator ) )
			registrations.GameplayOwnershipDirty = true;

		// A prefab's components enable in sequence. The manager registration will
		// reconcile the complete active object without reporting a transient
		// missing-manager error for the first component in that sequence.
		if ( registrations.Managers.Count == 0 )
			return;

		EnsureGameplayOwner( scene, registrations );
	}

	public static void UnregisterSessionCoordinator(
		Scene scene,
		LargeLadSessionCoordinator coordinator )
	{
		if ( scene is null || coordinator is null ||
			!RegistrationsByScene.TryGetValue( scene, out var registrations ) )
		{
			return;
		}

		if ( registrations.SessionCoordinators.Remove( coordinator ) )
			registrations.GameplayOwnershipDirty = true;

		if ( Game.IsClosing || !Game.IsPlaying || !scene.IsValid ||
			IsBootstrapObjectTearingDown( coordinator ) )
		{
			ReleaseGameplayOwnerForTeardown( registrations );
			return;
		}

		EnsureGameplayOwner( scene, registrations );
	}

	public static void UnregisterManager(
		Scene scene,
		LargeLadGameManager manager )
	{
		if ( scene is null || manager is null ||
			!RegistrationsByScene.TryGetValue( scene, out var registrations ) )
		{
			return;
		}

		if ( registrations.Managers.Remove( manager ) )
			registrations.GameplayOwnershipDirty = true;

		// Play Mode tears components down before the scene itself is destroyed.
		// Once the game is no longer actively playing, a missing manager is
		// expected teardown rather than a broken live gameplay bootstrap.
		if ( Game.IsClosing || !Game.IsPlaying || !scene.IsValid ||
			IsBootstrapObjectTearingDown( manager ) )
		{
			ReleaseGameplayOwnerForTeardown( registrations );
			return;
		}

		EnsureGameplayOwner( scene, registrations );
	}

	public static void RegisterPlayer(
		Scene scene,
		LargeLadPlayer player )
	{
		if ( scene is null || !IsActiveInScene( player, scene ) )
			return;

		var registrations = RegistrationsByScene.GetValue(
			scene,
			_ => new SceneRegistrations() );
		Prune( scene, registrations );

		if ( !registrations.Players.Add( player ) )
			return;

		// Players may enable before the bootstrap. Keep their registrations
		// without treating normal lifecycle ordering as a broken scene.
		if ( registrations.Managers.Count == 0 )
			return;

		EnsureGameplayOwner( scene, registrations );
		registrations.GameplayOwner?.RegisterPlayer( player );
	}

	public static void UnregisterPlayer(
		Scene scene,
		LargeLadPlayer player )
	{
		if ( scene is null || player is null ||
			!RegistrationsByScene.TryGetValue( scene, out var registrations ) )
		{
			return;
		}

		registrations.Players.Remove( player );

		if ( registrations.Managers.Count == 0 )
			return;

		registrations.GameplayOwner?.UnregisterPlayer( player );
	}

	public static void NotifyPlayerRoleChanged(
		Scene scene,
		LargeLadPlayer player,
		LargeLadRole oldRole,
		LargeLadRole newRole )
	{
		if ( scene is null || player is null ||
			!RegistrationsByScene.TryGetValue( scene, out var registrations ) ||
			!registrations.Players.Contains( player ) )
		{
			return;
		}

		if ( registrations.Managers.Count == 0 )
			return;

		registrations.GameplayOwner?.UpdatePlayerRole(
			player,
			oldRole,
			newRole );
	}

	public static void RegisterRoundResettable(
		Scene scene,
		ILargeLadRoundResettable resettable )
	{
		if ( scene is null || !IsActiveInScene( resettable, scene ) )
			return;

		var registrations = RegistrationsByScene.GetValue(
			scene,
			_ => new SceneRegistrations() );
		Prune( scene, registrations );

		if ( !registrations.Resettables.Add( resettable ) )
			return;

		if ( registrations.Managers.Count == 0 )
			return;

		EnsureGameplayOwner( scene, registrations );
		registrations.GameplayOwner?.RegisterRoundResettable( resettable );
	}

	public static void UnregisterRoundResettable(
		Scene scene,
		ILargeLadRoundResettable resettable )
	{
		if ( scene is null || resettable is null ||
			!RegistrationsByScene.TryGetValue( scene, out var registrations ) )
		{
			return;
		}

		registrations.Resettables.Remove( resettable );

		if ( registrations.Managers.Count == 0 )
			return;

		registrations.GameplayOwner?.UnregisterRoundResettable( resettable );
	}

	public static LargeLadGameManager FindManager( Scene scene )
	{
		if ( scene is null ||
			!RegistrationsByScene.TryGetValue( scene, out var registrations ) )
		{
			return null;
		}

		EnsureGameplayOwner( scene, registrations );
		return registrations.GameplayOwner;
	}

	public static IReadOnlyList<string> GetRuntimeBootstrapIssues(
		Scene scene,
		LargeLadGameManager manager )
	{
		if ( scene is null ||
			!RegistrationsByScene.TryGetValue( scene, out var registrations ) )
		{
			return new[]
			{
				"LargeLadGameManager is not attached to a registered scene."
			};
		}

		EnsureGameplayOwner( scene, registrations );
		return GetRuntimeBootstrapIssues( scene, registrations, manager );
	}

	public static IReadOnlyList<string> GetBlockingBootstrapIssues(
		Scene scene,
		LargeLadGameManager manager )
	{
		if ( scene is null )
		{
			return new[]
			{
				"LargeLadGameManager is not attached to a scene."
			};
		}

		manager?.ResolveBootstrapReferencesForRegistry();
		var managers = GetActiveComponents<LargeLadGameManager>( scene );
		var helpers = GetActiveComponents<NetworkHelper>( scene );
		var allocators =
			GetActiveComponents<LargeLadSpawnAllocator>( scene );
		var coordinators =
			GetActiveComponents<LargeLadSessionCoordinator>( scene );
		var mapInstances = GetActiveComponents<MapInstance>( scene );
		var issues = new List<string>();

		AddUniquenessIssue( issues, nameof( LargeLadGameManager ), managers );
		AddUniquenessIssue( issues, nameof( NetworkHelper ), helpers );
		AddUniquenessIssue(
			issues,
			nameof( LargeLadSpawnAllocator ),
			allocators );
		AddUniquenessIssue(
			issues,
			nameof( LargeLadSessionCoordinator ),
			coordinators );
		AddUniquenessIssue( issues, nameof( MapInstance ), mapInstances );

		if ( manager is null || managers.Count != 1 || managers[0] != manager )
		{
			issues.Add(
				"The requesting LargeLadGameManager is not the scene's sole " +
				"active manager." );
			return issues;
		}

		if ( manager.NetworkHelper is null )
		{
			issues.Add(
				"LargeLadGameManager needs its bootstrap NetworkHelper reference." );
		}
		else if ( helpers.Count != 1 ||
			helpers[0] != manager.NetworkHelper ||
			manager.NetworkHelper.GameObject != manager.GameObject )
		{
			issues.Add(
				"LargeLadGameManager's NetworkHelper reference must target the " +
				"sole active NetworkHelper on the same gameplay bootstrap object." );
		}

		if ( manager.SpawnAllocator is null )
		{
			issues.Add(
				"LargeLadGameManager needs its bootstrap " +
				"LargeLadSpawnAllocator reference." );
		}
		else if ( allocators.Count != 1 ||
			allocators[0] != manager.SpawnAllocator ||
			manager.SpawnAllocator.GameObject != manager.GameObject )
		{
			issues.Add(
				"LargeLadGameManager's LargeLadSpawnAllocator reference must " +
				"target the sole active allocator on the same gameplay " +
				"bootstrap object." );
		}

		if ( manager.SessionCoordinator is null )
		{
			issues.Add(
				"LargeLadGameManager needs its bootstrap " +
				"LargeLadSessionCoordinator reference." );
		}
		else if ( coordinators.Count != 1 ||
			coordinators[0] != manager.SessionCoordinator ||
			manager.SessionCoordinator.GameObject != manager.GameObject )
		{
			issues.Add(
				"LargeLadGameManager's LargeLadSessionCoordinator reference " +
				"must target the sole active coordinator on the same gameplay " +
				"bootstrap object." );
		}
		else
		{
			manager.SessionCoordinator.ResolveBootstrapReferences( mapInstances[0] );

			if ( manager.SessionCoordinator.GameManager != manager )
			{
				issues.Add(
					"LargeLadSessionCoordinator must reference the sole game " +
					"manager on its gameplay bootstrap object." );
			}

			if ( mapInstances.Count != 1 ||
				manager.SessionCoordinator.MapInstance != mapInstances[0] ||
				!manager.SessionCoordinator.MapInstance.GameObject.IsRoot )
			{
				issues.Add(
					"LargeLadSessionCoordinator needs an active MapInstance on a " +
					"dedicated root-level Snapshot Map Content Host." );
			}
		}

		return issues;
	}

	private static void Prune(
		Scene scene,
		SceneRegistrations registrations )
	{
		var managerCount = registrations.Managers.Count;
		registrations.Managers.RemoveWhere(
			manager => !IsActiveInScene( manager, scene ) );

		if ( registrations.Managers.Count != managerCount )
			registrations.GameplayOwnershipDirty = true;

		var coordinatorCount = registrations.SessionCoordinators.Count;
		registrations.SessionCoordinators.RemoveWhere(
			coordinator => !IsActiveInScene( coordinator, scene ) );

		if ( registrations.SessionCoordinators.Count != coordinatorCount )
			registrations.GameplayOwnershipDirty = true;

		registrations.Players.RemoveWhere(
			player => !IsActiveInScene( player, scene ) );
		registrations.Resettables.RemoveWhere(
			resettable => !IsActiveInScene( resettable, scene ) );
	}

	private static void EnsureGameplayOwner(
		Scene scene,
		SceneRegistrations registrations )
	{
		if ( !registrations.GameplayOwnershipDirty )
		{
			if ( registrations.GameplayOwner is not null &&
				IsActiveInScene( registrations.GameplayOwner, scene ) )
			{
				return;
			}

			registrations.GameplayOwnershipDirty = true;
		}

		ReconcileGameplayOwner( scene, registrations );
	}

	private static void ReconcileGameplayOwner(
		Scene scene,
		SceneRegistrations registrations )
	{
		Prune( scene, registrations );

		// Component hotload and live inspector changes can produce an
		// unregister/register gap without changing the scene itself. Rehydrate
		// this scene's lifecycle cache during the explicit ownership audit so a
		// still-active authored manager cannot leave a permanent null owner.
		var activeManagers = GetActiveComponents<LargeLadGameManager>( scene );
		registrations.Managers.Clear();
		registrations.Managers.UnionWith( activeManagers );

		// Scene shutdown can invalidate every component on the persistent object
		// before Game.IsPlaying flips to false. An entirely absent bootstrap has
		// no live NetworkHelper to suppress and is expected teardown. Any partial
		// or duplicate bootstrap continues through the fail-closed audit below.
		if ( activeManagers.Count == 0 &&
			GetActiveComponents<LargeLadSessionCoordinator>( scene ).Count == 0 &&
			GetActiveComponents<NetworkHelper>( scene ).Count == 0 &&
			GetActiveComponents<LargeLadSpawnAllocator>( scene ).Count == 0 &&
			GetActiveComponents<MapInstance>( scene ).Count == 0 )
		{
			ReleaseGameplayOwnerForTeardown( registrations );
			return;
		}

		var candidate = activeManagers.Count == 1
			? activeManagers[0]
			: null;
		var bootstrapIssues = GetRuntimeBootstrapIssues(
			scene,
			registrations,
			candidate );
		registrations.GameplayOwnershipDirty = false;

		if ( bootstrapIssues.Count > 0 )
		{
			SuppressPlayerSpawning( scene, registrations );

			if ( registrations.GameplayOwner is not null )
			{
				registrations.GameplayOwner.ReleaseSceneGameplayOwnership();
				registrations.GameplayOwner = null;
			}

			LogBlockingBootstrapIssueOnce(
				scene,
				registrations,
				bootstrapIssues );
			return;
		}

		RestorePlayerSpawning( registrations );
		registrations.LastBlockingBootstrapSignature = null;

		if ( registrations.GameplayOwner == candidate )
			return;

		registrations.GameplayOwner?.ReleaseSceneGameplayOwnership();
		registrations.GameplayOwner = candidate;

		var players = registrations.Players
			.Where( player => IsActiveInScene( player, scene ) )
			.OrderBy( player => player.GameObject.Id )
			.ThenBy( player => player.Id )
			.ToList();
		var resettables = registrations.Resettables
			.Where( resettable => IsActiveInScene( resettable, scene ) )
			.OrderBy( resettable => ((Component)resettable).GameObject.Id )
			.ThenBy( resettable => ((Component)resettable).Id )
			.ToList();
		candidate.AcquireSceneGameplayOwnership( players, resettables );
	}

	private static IReadOnlyList<string> GetRuntimeBootstrapIssues(
		Scene scene,
		SceneRegistrations registrations,
		LargeLadGameManager candidate )
	{
		var issues = new List<string>();
		var managers = GetActiveComponents<LargeLadGameManager>( scene );
		AddUniquenessIssue( issues, nameof( LargeLadGameManager ), managers );
		// Bootstrap reconciliation is the explicit fail-closed uniqueness audit;
		// enumerating here also makes component enable ordering atomic from the
		// registry's point of view.
		var coordinators =
			GetActiveComponents<LargeLadSessionCoordinator>( scene );
		var helpers = GetActiveComponents<NetworkHelper>( scene );
		var allocators =
			GetActiveComponents<LargeLadSpawnAllocator>( scene );
		var mapInstances = GetActiveComponents<MapInstance>( scene );
		AddUniquenessIssue(
			issues,
			nameof( LargeLadSessionCoordinator ),
			coordinators );
		AddUniquenessIssue( issues, nameof( NetworkHelper ), helpers );
		AddUniquenessIssue(
			issues,
			nameof( LargeLadSpawnAllocator ),
			allocators );
		AddUniquenessIssue( issues, nameof( MapInstance ), mapInstances );

		if ( managers.Count != 1 ||
			coordinators.Count != 1 ||
			helpers.Count != 1 ||
			allocators.Count != 1 ||
			mapInstances.Count != 1 )
			return issues;

		if ( candidate != managers[0] )
		{
			issues.Add(
				"The requesting LargeLadGameManager is not the scene's sole " +
				"registered manager." );
			return issues;
		}

		candidate.ResolveBootstrapReferencesForRegistry();
		var coordinator = coordinators[0];
		coordinator.ResolveBootstrapReferences( mapInstances[0] );

		if ( candidate.NetworkHelper != helpers[0] ||
			candidate.NetworkHelper.GameObject != candidate.GameObject )
		{
			issues.Add(
				"LargeLadGameManager needs an active bootstrap NetworkHelper " +
				"reference on the same gameplay bootstrap object." );
		}

		if ( candidate.SpawnAllocator != allocators[0] ||
			candidate.SpawnAllocator.GameObject != candidate.GameObject )
		{
			issues.Add(
				"LargeLadGameManager needs an active bootstrap " +
				"LargeLadSpawnAllocator reference on the same gameplay " +
				"bootstrap object." );
		}

		if ( candidate.SessionCoordinator != coordinator ||
			coordinator.GameManager != candidate ||
			coordinator.GameObject != candidate.GameObject )
		{
			issues.Add(
				"LargeLadGameManager and LargeLadSessionCoordinator must " +
				"cross-reference each other on the same gameplay bootstrap " +
				"object." );
		}

		if ( coordinator.MapInstance != mapInstances[0] ||
			!coordinator.MapInstance.GameObject.IsRoot )
		{
			issues.Add(
				"LargeLadSessionCoordinator needs an active MapInstance " +
				"reference on a dedicated root-level Snapshot Map Content " +
				"Host." );
		}

		return issues;
	}

	private static void SuppressPlayerSpawning(
		Scene scene,
		SceneRegistrations registrations )
	{
		if ( scene.IsEditor )
			return;

		foreach ( var helper in GetActiveComponents<NetworkHelper>( scene ) )
		{
			if ( !registrations.SuppressedPlayerPrefabs.ContainsKey( helper ) )
				registrations.SuppressedPlayerPrefabs.Add(
					helper,
					helper.PlayerPrefab );

			helper.PlayerPrefab = null;
		}
	}

	private static void RestorePlayerSpawning(
		SceneRegistrations registrations )
	{
		foreach ( var entry in registrations.SuppressedPlayerPrefabs.ToList() )
		{
			if ( entry.Key is not null && entry.Key.IsValid )
				entry.Key.PlayerPrefab = entry.Value;
		}

		registrations.SuppressedPlayerPrefabs.Clear();
	}

	private static void ReleaseGameplayOwnerForTeardown(
		SceneRegistrations registrations )
	{
		registrations.GameplayOwner?.ReleaseSceneGameplayOwnership();
		registrations.GameplayOwner = null;
		RestorePlayerSpawning( registrations );
		registrations.LastBlockingBootstrapSignature = null;
		registrations.GameplayOwnershipDirty = false;
	}

	private static void LogBlockingBootstrapIssueOnce(
		Scene scene,
		SceneRegistrations registrations,
		IReadOnlyList<string> issues )
	{
		var signature = string.Join( " | ", issues );

		if ( registrations.LastBlockingBootstrapSignature == signature )
			return;

		registrations.LastBlockingBootstrapSignature = signature;
		Log.Error(
			"Large Lad gameplay is blocked and NetworkHelper player spawning " +
			$"is disabled in scene '{scene.Name}': {string.Join( " ", issues )}" );
	}

	private static List<T> GetActiveComponents<T>( Scene scene )
		where T : Component
	{
		// Reserved for explicit bootstrap validation and fail-closed suppression.
		// Normal registry reads never enumerate the scene.
		return scene.GetAllComponents<T>()
			.Where( component => IsActiveInScene( component, scene ) )
			.OrderBy( component => component.GameObject.Id )
			.ThenBy( component => component.Id )
			.ToList();
	}

	private static void AddUniquenessIssue<T>(
		List<string> issues,
		string componentName,
		IReadOnlyList<T> components )
		where T : Component
	{
		if ( components.Count == 1 )
			return;

		var objects = components.Count == 0
			? "none"
			: string.Join(
				", ",
				components.Select( component =>
					$"'{component.GameObject.Name}' " +
					$"(object {component.GameObject.Id}, component {component.Id})" ) );
		var duplicateLabel = components.Count > 1
			? " Duplicate bootstrap components:"
			: string.Empty;
		issues.Add(
			$"Expected exactly one active {componentName}, found " +
			$"{components.Count}.{duplicateLabel} {objects}." );
	}

	private static bool IsActiveInScene( Component component, Scene scene )
	{
		return component is not null &&
			component.IsValid &&
			component.Enabled &&
			component.Scene == scene;
	}

	private static bool IsBootstrapObjectTearingDown( Component component )
	{
		return component?.GameObject is null ||
			!component.GameObject.IsValid ||
			!component.GameObject.Enabled;
	}

	private static bool IsActiveInScene(
		ILargeLadRoundResettable resettable,
		Scene scene )
	{
		return resettable is Component component &&
			IsActiveInScene( component, scene );
	}
}

/// <summary>
/// Lifecycle-aware base for authored state that participates in round reset.
/// Disabled and destroyed components leave the scene registry immediately.
/// </summary>
public abstract class LargeLadRoundResettableComponent : Component,
	ILargeLadRoundResettable
{
	private Scene registeredScene;

	protected override void OnEnabled()
	{
		base.OnEnabled();
		RegisterForRoundReset();
	}

	protected override void OnDisabled()
	{
		UnregisterFromRoundReset();
		base.OnDisabled();
	}

	protected override void OnDestroy()
	{
		UnregisterFromRoundReset();
		base.OnDestroy();
	}

	public abstract void ResetForRound();

	private void RegisterForRoundReset()
	{
		if ( registeredScene is not null && registeredScene != Scene )
		{
			LargeLadSceneRegistry.UnregisterRoundResettable(
				registeredScene,
				this );
		}

		registeredScene = Scene;
		LargeLadSceneRegistry.RegisterRoundResettable( registeredScene, this );
	}

	private void UnregisterFromRoundReset()
	{
		if ( registeredScene is null )
			return;

		LargeLadSceneRegistry.UnregisterRoundResettable(
			registeredScene,
			this );
		registeredScene = null;
	}
}
