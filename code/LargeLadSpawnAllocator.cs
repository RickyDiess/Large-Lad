using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Pure deterministic spawn rules shared by runtime allocation and unit tests.
/// </summary>
public static class LargeLadSpawnRules
{
	private const float GoldenAngle = 2.39996323f;

	public static int GetRequiredCapacity(
		LargeLadSpawnGroup group,
		int playerCount )
	{
		var safePlayerCount = System.Math.Max( 0, playerCount );

		return group switch
		{
			LargeLadSpawnGroup.SkinnyKid =>
				System.Math.Max( 0, safePlayerCount - 1 ),
			LargeLadSpawnGroup.Lobby or LargeLadSpawnGroup.Hunter =>
				safePlayerCount,
			_ => throw new ArgumentOutOfRangeException( nameof( group ) )
		};
	}

	public static int GetUsableAuthoredCapacity( int authoredCapacity )
	{
		return System.Math.Clamp(
			authoredCapacity,
			0,
			LargeLadGameManager.TargetPlayerCount );
	}

	public static Vector3 GetDeterministicLayoutOffset(
		int attempt,
		int desiredCount,
		float radius )
	{
		var safeCount = System.Math.Max( 1, desiredCount );
		var safeAttempt = System.Math.Max( 0, attempt );
		var safeRadius = System.MathF.Max( 0.0f, radius );
		var radialIndex = safeAttempt % safeCount;
		var normalizedRadius = System.MathF.Sqrt(
			(radialIndex + 0.5f) / safeCount );
		var angle = safeAttempt * GoldenAngle;
		var distance = normalizedRadius * safeRadius;

		return new Vector3(
			System.MathF.Cos( angle ) * distance,
			System.MathF.Sin( angle ) * distance,
			0.0f );
	}

	public static bool MeetsPairwiseSeparation(
		LargeLadSpawnLocation candidate,
		LargeLadSpawnLocation existing )
	{
		var separation = System.MathF.Max(
			candidate.MinimumSeparation,
			existing.MinimumSeparation );
		return candidate.Position.DistanceSquared( existing.Position ) >=
			separation * separation;
	}

	public static bool HasCompleteBatchAllocation<TPlayer>(
		IReadOnlyList<TPlayer> requestedPlayers,
		IReadOnlyDictionary<TPlayer, LargeLadSpawnLocation> allocations )
		where TPlayer : class
	{
		if ( requestedPlayers is null || allocations is null )
			return false;

		foreach ( var player in requestedPlayers )
		{
			if ( player is not null && !allocations.ContainsKey( player ) )
				return false;
		}

		return true;
	}
}

/// <summary>
/// Produces and caches safe spawn positions from every LargeLadTeamSpawn in the scene.
/// Map authors only place and size spawn areas; no ordering is required.
/// Runtime NetworkHelper points remain beneath their authored lobby spawn.
/// </summary>
public sealed class LargeLadSpawnAllocator : Component
{
	private const float PlayerRadius = 16.0f;
	private const float PlayerHeight = 72.0f;
	private const int AttemptsPerRequestedPosition = 48;

	private readonly List<GameObject> runtimeLobbyPoints = new();
	private readonly Dictionary<
		LargeLadTeamSpawn,
		IReadOnlyList<LargeLadSpawnLocation>> candidatesBySpawn = new();
	private readonly Dictionary<
		LargeLadSpawnGroup,
		IReadOnlyList<LargeLadSpawnLocation>> candidatesByGroup = new();
	private readonly List<LargeLadTeamSpawn> authoredTeamSpawns = new();
	private bool authoredTeamSpawnCacheDirty = true;
	private bool candidateCacheDirty = true;
	private LargeLadGameManager gameManager;
	private NetworkHelper configuredNetworkHelper;
	private GameObject configuredPlayerPrefab;

	protected override void OnStart()
	{
		EnsureCandidateCache();
	}

	protected override void OnValidate()
	{
		InvalidateCandidateCache();
	}

	protected override void OnDestroy()
	{
		ClearRuntimeLobbyPoints();
		candidatesBySpawn.Clear();
		candidatesByGroup.Clear();
		authoredTeamSpawns.Clear();
		gameManager = null;
		configuredNetworkHelper = null;
		configuredPlayerPrefab = null;
	}

	public IReadOnlyList<LargeLadTeamSpawn> GetTeamSpawns(
		LargeLadSpawnGroup group )
	{
		EnsureAuthoredTeamSpawnCache();
		return GetCachedTeamSpawns( group );
	}

	public IReadOnlyList<LargeLadTeamSpawn> AuthoredTeamSpawns
	{
		get
		{
			EnsureAuthoredTeamSpawnCache();
			return authoredTeamSpawns;
		}
	}

	private IReadOnlyList<LargeLadTeamSpawn> GetCachedTeamSpawns(
		LargeLadSpawnGroup group )
	{
		return authoredTeamSpawns
			.Where( spawn => spawn.Group == group )
			.ToList();
	}

	public int GetConfiguredCapacity( LargeLadSpawnGroup group )
	{
		return GetTeamSpawns( group )
			.Sum( spawn =>
				LargeLadSpawnRules.GetUsableAuthoredCapacity(
					spawn.Capacity ) );
	}

	public int CountGeneratedCandidates( LargeLadSpawnGroup group )
	{
		return GetCachedCandidates( group ).Count;
	}

	public int CountGeneratedCandidates( LargeLadTeamSpawn spawn )
	{
		return TryGetCachedCandidates( spawn, out var candidates )
			? candidates.Count
			: 0;
	}

	/// <summary>
	/// Marks authored candidates stale without tracing immediately. The next
	/// validation, allocation, or gizmo query rebuilds the complete cache once.
	/// </summary>
	public void InvalidateCandidateCache()
	{
		authoredTeamSpawnCacheDirty = true;
		candidateCacheDirty = true;
	}

	/// <summary>
	/// Reprojects every authored spawn against current static scene geometry.
	/// This operation updates cached data only.
	/// </summary>
	public void RebuildCandidateCache()
	{
		BuildCandidateCache();
	}

	/// <summary>
	/// Recreates NetworkHelper's runtime Lobby GameObjects from the current cache.
	/// Candidate reads never invoke this operation implicitly.
	/// </summary>
	public void RefreshNetworkHelperLobbyPoints()
	{
		if ( configuredNetworkHelper is not null )
			BuildRuntimeLobbyPoints( configuredNetworkHelper );
	}

	/// <summary>
	/// Explicit mapper workflow after authored geometry changes.
	/// </summary>
	[Button]
	public void RebuildCandidatesAndRefreshLobbyPoints()
	{
		RebuildCandidateCache();
		RefreshNetworkHelperLobbyPoints();
	}

	public bool TryGetCachedCandidates(
		LargeLadTeamSpawn spawn,
		out IReadOnlyList<LargeLadSpawnLocation> candidates )
	{
		EnsureCandidateCache();
		return candidatesBySpawn.TryGetValue( spawn, out candidates );
	}

	public void ConfigureNetworkHelper( NetworkHelper helper )
	{
		if ( helper is null )
			return;

		EnsureCandidateCache();

		if ( configuredNetworkHelper != helper )
			configuredPlayerPrefab = helper.PlayerPrefab;

		configuredNetworkHelper = helper;
		RefreshNetworkHelperLobbyPoints();
	}

	public void ConfigureGameManager( LargeLadGameManager manager )
	{
		if ( manager is not null && manager.Scene == Scene )
			gameManager = manager;
	}

	private void BuildRuntimeLobbyPoints( NetworkHelper helper )
	{
		ClearRuntimeLobbyPoints();
		var requiredCapacity = LargeLadSpawnRules.GetRequiredCapacity(
			LargeLadSpawnGroup.Lobby,
			LargeLadGameManager.TargetPlayerCount );
		var candidateParents =
			new List<(LargeLadSpawnLocation Location, LargeLadTeamSpawn Parent)>();

		foreach ( var spawn in GetTeamSpawns( LargeLadSpawnGroup.Lobby ) )
		{
			if ( !candidatesBySpawn.TryGetValue( spawn, out var candidates ) )
				continue;

			foreach ( var candidate in candidates )
				candidateParents.Add( (candidate, spawn) );
		}

		if ( candidateParents.Count < requiredCapacity )
		{
			// NetworkHelper otherwise falls back to its own transform and stacks
			// connecting players, or repeatedly selects an undersized set. An
			// invalid Lobby cache blocks player creation until a mapper fixes and
			// rebuilds the authored areas.
			var configuredCapacity =
				GetConfiguredCapacity( LargeLadSpawnGroup.Lobby );
			var authoredAreas = string.Join(
				", ",
				GetTeamSpawns( LargeLadSpawnGroup.Lobby )
					.Select( spawn =>
						$"'{spawn.GameObject.Name}' capacity {spawn.Capacity}, " +
						$"generated {CountGeneratedCandidates( spawn )}" ) );

			if ( string.IsNullOrWhiteSpace( authoredAreas ) )
				authoredAreas = "none";

			helper.PlayerPrefab = null;
			helper.SpawnPoints.Clear();
			Log.Error(
				$"Lobby generated {candidateParents.Count}/{requiredCapacity} " +
				$"required valid cached spawn positions from " +
				$"{configuredCapacity} configured capacity. Authored spawn " +
				$"areas: {authoredAreas}. " +
				"NetworkHelper player spawning is disabled until the spawn " +
				"areas or surrounding geometry are fixed and rebuilt." );
			return;
		}

		helper.PlayerPrefab = configuredPlayerPrefab;

		for ( var index = 0; index < candidateParents.Count; index++ )
		{
			var candidate = candidateParents[index].Location;
			var parent = candidateParents[index].Parent;
			var pointName = $"Runtime Lobby Spawn {index + 1:00}";
			var point = new GameObject(
				parent.GameObject,
				true,
				pointName );
			point.NetworkMode = NetworkMode.Never;
			point.IsStatic = true;
			point.WorldPosition = candidate.Position;
			point.WorldRotation = candidate.Rotation;
			runtimeLobbyPoints.Add( point );
		}

		helper.SpawnPoints.Clear();
		helper.SpawnPoints.AddRange( runtimeLobbyPoints );
	}

	public IReadOnlyDictionary<LargeLadPlayer, LargeLadSpawnLocation> AllocateBatch(
		LargeLadSpawnGroup group,
		IReadOnlyList<LargeLadPlayer> players )
	{
		return AllocateBatch(
			group,
			players,
			Array.Empty<LargeLadPlayer>(),
			Array.Empty<Vector3>() );
	}

	public IReadOnlyDictionary<LargeLadPlayer, LargeLadSpawnLocation> AllocateBatch(
		LargeLadSpawnGroup group,
		IReadOnlyList<LargeLadPlayer> players,
		IReadOnlyCollection<LargeLadPlayer> additionallyRelocatingPlayers,
		IReadOnlyList<Vector3> projectedOccupiedPositions )
	{
		var allocations = new Dictionary<LargeLadPlayer, LargeLadSpawnLocation>();

		if ( players is null || players.Count == 0 )
			return allocations;

		var allCandidates = GetCachedCandidates( group );
		var requestedPlayers = players
			.Where( player => player is not null )
			.ToList();

		if ( requestedPlayers.Count > LargeLadGameManager.TargetPlayerCount )
		{
			Log.Error(
				$"{group} batch allocation rejected: requested " +
				$"{requestedPlayers.Count} players, exceeding the supported " +
				$"{LargeLadGameManager.TargetPlayerCount}-player contract." );
			return allocations;
		}

		if ( allCandidates.Count < requestedPlayers.Count )
		{
			var configuredCapacity = GetConfiguredCapacity( group );
			Log.Error(
				$"{group} batch allocation rejected before reserving positions: " +
				$"requested {requestedPlayers.Count} unique positions, but the " +
				$"cache contains {allCandidates.Count} valid positions from " +
				$"{configuredCapacity} configured capacity. Increase or add " +
				"authored spawn areas, clear obstructing geometry, and rebuild " +
				"projected candidates." );
			return allocations;
		}

		var available = new List<LargeLadSpawnLocation>( allCandidates );
		var batch = requestedPlayers.ToHashSet();

		if ( additionallyRelocatingPlayers is not null )
			batch.UnionWith( additionallyRelocatingPlayers );

		var occupied = GetActivePlayers()
			.Where( player =>
				!batch.Contains( player ) &&
				player.Health?.IsDead != true )
			.Select( player => player.GameObject.WorldPosition )
			.ToList();

		if ( projectedOccupiedPositions is not null )
			occupied.AddRange( projectedOccupiedPositions );

		var reserved = new List<Vector3>();

		foreach ( var player in players.Where( player => player is not null ) )
		{
			var clear = available
				.Where( candidate =>
					IsFarEnough( candidate, occupied ) &&
					IsFarEnough( candidate, reserved ) )
				.ToList();
			LargeLadSpawnLocation selected;

			if ( clear.Count > 0 )
			{
				selected = clear
					.OrderByDescending( candidate => MinimumDistanceSquared(
						candidate.Position,
						occupied,
						reserved ) )
					.First();
			}
			else
			{
				selected = available
					.OrderByDescending( candidate => MinimumDistanceSquared(
						candidate.Position,
						occupied,
						reserved ) )
					.First();
			}

			allocations[player] = selected;
			reserved.Add( selected.Position );
			available.Remove( selected );
		}

		return allocations;
	}

	public bool TryAllocate(
		LargeLadSpawnGroup group,
		LargeLadPlayer player,
		out LargeLadSpawnLocation location )
	{
		var candidates = GetCachedCandidates( group );

		if ( candidates.Count == 0 )
		{
			location = default;
			return false;
		}

		var occupied = GetActivePlayers()
			.Where( other => other != player && other.Health?.IsDead != true )
			.Select( other => other.GameObject.WorldPosition )
			.ToList();
		var clear = candidates
			.Where( candidate => IsFarEnough( candidate, occupied ) )
			.ToList();
		var pool = clear.Count > 0 ? clear : candidates;

		location = pool
			.OrderByDescending( candidate => MinimumDistanceSquared(
				candidate.Position,
				occupied,
				Array.Empty<Vector3>() ) )
			.First();
		return true;
	}

	private IReadOnlyList<LargeLadSpawnLocation> GetCachedCandidates(
		LargeLadSpawnGroup group )
	{
		EnsureCandidateCache();

		return candidatesByGroup.TryGetValue( group, out var candidates )
			? candidates
			: Array.Empty<LargeLadSpawnLocation>();
	}

	private IReadOnlyList<LargeLadPlayer> GetActivePlayers()
	{
		if ( gameManager is null ||
			!gameManager.IsValid ||
			!gameManager.Enabled ||
			gameManager.Scene != Scene )
		{
			gameManager = LargeLadGameManager.FindForScene( Scene );
		}

		return gameManager?.ActivePlayers ??
			Array.Empty<LargeLadPlayer>();
	}

	private void EnsureCandidateCache()
	{
		if ( candidateCacheDirty )
			BuildCandidateCache();
	}

	private void BuildCandidateCache()
	{
		candidatesBySpawn.Clear();
		candidatesByGroup.Clear();
		EnsureAuthoredTeamSpawnCache();

		foreach ( var group in Enum.GetValues<LargeLadSpawnGroup>() )
		{
			var groupCandidates = new List<LargeLadSpawnLocation>();

			foreach ( var spawn in GetCachedTeamSpawns( group ) )
			{
				var spawnCandidates = GenerateCandidates(
					spawn,
					groupCandidates );
				candidatesBySpawn.Add( spawn, spawnCandidates );
				groupCandidates.AddRange( spawnCandidates );
			}

			candidatesByGroup.Add( group, groupCandidates );
		}

		candidateCacheDirty = false;
	}

	private void EnsureAuthoredTeamSpawnCache()
	{
		if ( !authoredTeamSpawnCacheDirty )
			return;

		authoredTeamSpawns.Clear();

		if ( Scene is not null )
		{
			// One authored-scene pass per invalidation. Group reads, validation,
			// and round allocation reuse the cached list below.
			authoredTeamSpawns.AddRange(
				Scene.GetAllComponents<LargeLadTeamSpawn>()
					.Where( spawn =>
						spawn is not null &&
						spawn.IsValid &&
						spawn.Enabled &&
						spawn.Scene == Scene )
					.OrderBy( spawn => spawn.GameObject.Id )
					.ThenBy( spawn => spawn.Id ) );
		}

		authoredTeamSpawnCacheDirty = false;
	}

	private List<LargeLadSpawnLocation> GenerateCandidates(
		LargeLadTeamSpawn spawn,
		IReadOnlyList<LargeLadSpawnLocation> existingGroupCandidates )
	{
		var desiredCount =
			LargeLadSpawnRules.GetUsableAuthoredCapacity(
				spawn.Capacity );
		var radius = System.MathF.Max( 0.0f, spawn.SpawnRadius );
		var separation = System.MathF.Max(
			PlayerRadius * 2.0f,
			spawn.MinimumSeparation );
		var attempts = desiredCount * AttemptsPerRequestedPosition;
		var candidates = new List<LargeLadSpawnLocation>( desiredCount );

		for ( var attempt = 0;
			attempt < attempts && candidates.Count < desiredCount;
			attempt++ )
		{
			var layoutOffset =
				LargeLadSpawnRules.GetDeterministicLayoutOffset(
					attempt,
					desiredCount,
					radius );
			var desiredPosition = spawn.GameObject.WorldPosition +
				GetHorizontalOffset(
					spawn.GameObject.WorldRotation,
					layoutOffset );

			if ( !TryProjectToSafeFloor( desiredPosition, out var position ) )
				continue;

			var projectedCandidate = new LargeLadSpawnLocation(
				position,
				spawn.GameObject.WorldRotation,
				separation );

			if ( existingGroupCandidates
				.Concat( candidates )
				.Any( existing =>
					!LargeLadSpawnRules.MeetsPairwiseSeparation(
						projectedCandidate,
						existing ) ) )
			{
				continue;
			}

			candidates.Add( projectedCandidate );
		}

		return candidates;
	}

	private bool TryProjectToSafeFloor(
		Vector3 desiredPosition,
		out Vector3 safePosition )
	{
		var floorTrace = Scene.Trace
			.Ray(
				desiredPosition + Vector3.Up * 64.0f,
				desiredPosition - Vector3.Up * 256.0f )
			.IgnoreDynamic()
			.WithoutTags( "player" )
			.Run();

		if ( !floorTrace.Hit )
		{
			safePosition = default;
			return false;
		}

		safePosition = floorTrace.EndPosition + Vector3.Up;
		var capsule = new Capsule(
			safePosition + Vector3.Up * PlayerRadius,
			safePosition + Vector3.Up * (PlayerHeight - PlayerRadius),
			PlayerRadius - 0.5f );
		var clearance = Scene.Trace
			.Capsule( capsule )
			.IgnoreDynamic()
			.WithoutTags( "player" )
			.Run();

		return !clearance.Hit && !clearance.StartedSolid;
	}

	private void ClearRuntimeLobbyPoints()
	{
		foreach ( var point in runtimeLobbyPoints )
		{
			if ( point is not null && point.IsValid )
				point.Destroy();
		}

		runtimeLobbyPoints.Clear();
	}

	private static Vector3 GetHorizontalOffset(
		Rotation rotation,
		Vector3 layoutOffset )
	{
		var forward = rotation.Forward;
		var right = rotation.Right;
		forward.z = 0.0f;
		right.z = 0.0f;

		forward = forward.LengthSquared > 0.001f
			? forward.Normal
			: Vector3.Forward;
		right = right.LengthSquared > 0.001f
			? right.Normal
			: Vector3.Right;

		return right * layoutOffset.x +
			forward * layoutOffset.y;
	}

	private static bool IsFarEnough(
		LargeLadSpawnLocation candidate,
		IEnumerable<Vector3> positions )
	{
		var minimumSquared =
			candidate.MinimumSeparation * candidate.MinimumSeparation;
		return positions.All( position =>
			candidate.Position.DistanceSquared( position ) >= minimumSquared );
	}

	private static float MinimumDistanceSquared(
		Vector3 position,
		IReadOnlyList<Vector3> occupied,
		IReadOnlyList<Vector3> reserved )
	{
		var minimum = float.MaxValue;

		foreach ( var other in occupied )
			minimum = System.MathF.Min(
				minimum,
				position.DistanceSquared( other ) );

		foreach ( var other in reserved )
			minimum = System.MathF.Min(
				minimum,
				position.DistanceSquared( other ) );

		return minimum;
	}
}
