using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Produces safe spawn positions from every LargeLadTeamSpawn in the scene.
/// Map authors only place and size spawn areas; no ordering is required.
/// Runtime NetworkHelper points remain beneath their authored lobby spawn.
/// </summary>
public sealed class LargeLadSpawnAllocator : Component
{
	private const float PlayerRadius = 16.0f;
	private const float PlayerHeight = 72.0f;
	private const int AttemptsPerRequestedPosition = 48;

	private readonly List<GameObject> runtimeLobbyPoints = new();
	private int spawnGeneration;

	protected override void OnDestroy()
	{
		ClearRuntimeLobbyPoints();
	}

	public IReadOnlyList<LargeLadTeamSpawn> GetTeamSpawns(
		LargeLadSpawnGroup group )
	{
		if ( Scene is null )
			return Array.Empty<LargeLadTeamSpawn>();

		return Scene
			.GetAllComponents<LargeLadTeamSpawn>()
			.Where( spawn => spawn.Group == group )
			.ToList();
	}

	public int GetConfiguredCapacity( LargeLadSpawnGroup group )
	{
		return GetTeamSpawns( group )
			.Sum( spawn => System.Math.Max( 0, spawn.Capacity ) );
	}

	public int CountGeneratedCandidates( LargeLadSpawnGroup group )
	{
		return GenerateCandidates( group ).Count;
	}

	public void ConfigureNetworkHelper( NetworkHelper helper )
	{
		if ( helper is null )
			return;

		ClearRuntimeLobbyPoints();
		var candidates = new List<LargeLadSpawnLocation>();
		var candidateParents =
			new List<(LargeLadSpawnLocation Location, LargeLadTeamSpawn Parent)>();
		var random = CreateRandom();

		foreach ( var spawn in GetTeamSpawns( LargeLadSpawnGroup.Lobby ) )
		{
			var previousCount = candidates.Count;
			GenerateCandidates( spawn, candidates, random );

			for ( var index = previousCount; index < candidates.Count; index++ )
				candidateParents.Add( (candidates[index], spawn) );
		}

		if ( candidateParents.Count == 0 )
		{
			var emergency = GetEmergencySpawn( LargeLadSpawnGroup.Lobby );
			var emergencyParent = GetTeamSpawns(
				LargeLadSpawnGroup.Lobby ).FirstOrDefault();

			if ( emergency is not null && emergencyParent is not null )
				candidateParents.Add( (emergency.Value, emergencyParent) );
		}

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
		var allocations = new Dictionary<LargeLadPlayer, LargeLadSpawnLocation>();

		if ( players is null || players.Count == 0 )
			return allocations;

		var allCandidates = GenerateCandidates( group );
		var available = new List<LargeLadSpawnLocation>( allCandidates );
		var batch = players.Where( player => player is not null ).ToHashSet();
		var occupied = Scene
			.GetAllComponents<LargeLadPlayer>()
			.Where( player =>
				!batch.Contains( player ) &&
				player.Health?.IsDead != true )
			.Select( player => player.GameObject.WorldPosition )
			.ToList();
		var reserved = new List<Vector3>();
		var random = CreateRandom();

		foreach ( var player in players.Where( player => player is not null ) )
		{
			if ( allCandidates.Count == 0 )
			{
				var emergency = GetEmergencySpawn( group );
				if ( emergency is not null )
					allocations[player] = emergency.Value;
				continue;
			}

			if ( available.Count == 0 )
			{
				Log.Error(
					$"{group} has fewer valid positions than the requested " +
					$"{players.Count}-player batch. Reserved slots will not overlap." );
				break;
			}

			var clear = available
				.Where( candidate =>
					IsFarEnough( candidate, occupied ) &&
					IsFarEnough( candidate, reserved ) )
				.ToList();
			LargeLadSpawnLocation selected;

			if ( clear.Count > 0 )
			{
				selected = clear[random.Next( clear.Count )];
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
		var candidates = GenerateCandidates( group );

		if ( candidates.Count == 0 )
		{
			var emergency = GetEmergencySpawn( group );
			if ( emergency is not null )
			{
				location = emergency.Value;
				return true;
			}

			location = default;
			return false;
		}

		var occupied = Scene
			.GetAllComponents<LargeLadPlayer>()
			.Where( other => other != player && other.Health?.IsDead != true )
			.Select( other => other.GameObject.WorldPosition )
			.ToList();
		var clear = candidates
			.Where( candidate => IsFarEnough( candidate, occupied ) )
			.ToList();
		var pool = clear.Count > 0 ? clear : candidates;

		if ( occupied.Count == 0 )
		{
			var random = CreateRandom();
			location = pool[random.Next( pool.Count )];
			return true;
		}

		location = pool
			.OrderByDescending( candidate => MinimumDistanceSquared(
				candidate.Position,
				occupied,
				Array.Empty<Vector3>() ) )
			.First();
		return true;
	}

	private List<LargeLadSpawnLocation> GenerateCandidates(
		LargeLadSpawnGroup group )
	{
		var candidates = new List<LargeLadSpawnLocation>();
		var random = CreateRandom();

		foreach ( var spawn in GetTeamSpawns( group ) )
			GenerateCandidates( spawn, candidates, random );

		return candidates;
	}

	private void GenerateCandidates(
		LargeLadTeamSpawn spawn,
		List<LargeLadSpawnLocation> candidates,
		Random random )
	{
		var desiredCount = System.Math.Clamp(
			spawn.Capacity,
			1,
			LargeLadMapDefinition.TargetPlayerCount );
		var radius = System.MathF.Max( 0.0f, spawn.SpawnRadius );
		var separation = System.MathF.Max(
			PlayerRadius * 2.0f,
			spawn.MinimumSeparation );
		var generated = 0;
		var attempts = desiredCount * AttemptsPerRequestedPosition;

		for ( var attempt = 0; attempt < attempts && generated < desiredCount; attempt++ )
		{
			var angle = (float)random.NextDouble() * System.MathF.PI * 2.0f;
			var distance = System.MathF.Sqrt( (float)random.NextDouble() ) * radius;
			var desiredPosition = spawn.GameObject.WorldPosition +
				GetHorizontalOffset( spawn.GameObject.WorldRotation, angle, distance );

			if ( !TryProjectToSafeFloor( desiredPosition, out var position ) )
				continue;

			var candidate = new LargeLadSpawnLocation(
				position,
				spawn.GameObject.WorldRotation,
				separation );

			if ( !IsFarEnough(
				candidate,
				candidates.Select( existing => existing.Position ) ) )
			{
				continue;
			}

			candidates.Add( candidate );
			generated++;
		}
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

	private LargeLadSpawnLocation? GetEmergencySpawn(
		LargeLadSpawnGroup group )
	{
		var spawn = GetTeamSpawns( group ).FirstOrDefault();

		if ( spawn is null )
		{
			Log.Error( $"No {group} team spawn exists." );
			return null;
		}

		Log.Error(
			$"{group} spawn '{spawn.GameObject.Name}' produced no valid positions; " +
			"using its origin as an emergency fallback." );
		return new LargeLadSpawnLocation(
			spawn.GameObject.WorldPosition,
			spawn.GameObject.WorldRotation,
			System.MathF.Max( PlayerRadius * 2.0f, spawn.MinimumSeparation ) );
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
		float angle,
		float distance )
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

		return right * (System.MathF.Cos( angle ) * distance) +
			forward * (System.MathF.Sin( angle ) * distance);
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

	private Random CreateRandom()
	{
		spawnGeneration++;
		return new Random( unchecked(spawnGeneration * 7919 + 104729) );
	}
}
