using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// One deterministic projection of the enabled team-spawn areas supplied by a
/// caller. This contains no allocation, networking, or persistent session state.
/// </summary>
public sealed class LargeLadSpawnProjectionResult
{
	private readonly IReadOnlyDictionary<
		LargeLadTeamSpawn,
		IReadOnlyList<LargeLadSpawnLocation>> candidatesBySpawn;
	private readonly IReadOnlyDictionary<
		LargeLadSpawnGroup,
		IReadOnlyList<LargeLadSpawnLocation>> candidatesByGroup;

	internal LargeLadSpawnProjectionResult(
		IReadOnlyList<LargeLadTeamSpawn> authoredSpawns,
		IReadOnlyDictionary<
			LargeLadTeamSpawn,
			IReadOnlyList<LargeLadSpawnLocation>> candidatesBySpawn,
		IReadOnlyDictionary<
			LargeLadSpawnGroup,
			IReadOnlyList<LargeLadSpawnLocation>> candidatesByGroup )
	{
		AuthoredSpawns = authoredSpawns;
		this.candidatesBySpawn = candidatesBySpawn;
		this.candidatesByGroup = candidatesByGroup;
	}

	public IReadOnlyList<LargeLadTeamSpawn> AuthoredSpawns { get; }

	public IReadOnlyList<LargeLadTeamSpawn> GetSpawns(
		LargeLadSpawnGroup group )
	{
		return AuthoredSpawns
			.Where( spawn => spawn.Group == group )
			.ToArray();
	}

	public IReadOnlyList<LargeLadSpawnLocation> GetCandidates(
		LargeLadSpawnGroup group )
	{
		return candidatesByGroup.TryGetValue( group, out var candidates )
			? candidates
			: Array.Empty<LargeLadSpawnLocation>();
	}

	public IReadOnlyList<LargeLadSpawnLocation> GetCandidates(
		LargeLadTeamSpawn spawn )
	{
		return spawn is not null &&
			candidatesBySpawn.TryGetValue( spawn, out var candidates )
			? candidates
			: Array.Empty<LargeLadSpawnLocation>();
	}
}

/// <summary>
/// Shared geometry calculation used by content-scene authoring, map admission,
/// and the runtime allocator cache.
/// </summary>
public static class LargeLadSpawnProjection
{
	private const int AttemptsPerRequestedPosition = 48;

	public static LargeLadSpawnProjectionResult Build(
		Scene scene,
		IEnumerable<LargeLadTeamSpawn> authoredSpawns )
	{
		var spawns = authoredSpawns?
			.Where( spawn =>
				spawn is not null &&
				spawn.IsValid &&
				spawn.Enabled &&
				spawn.GameObject.Enabled &&
				spawn.Scene == scene )
			.Distinct()
			.OrderBy( spawn => spawn.GameObject.Id )
			.ThenBy( spawn => spawn.Id )
			.ToArray() ?? [];
		var bySpawn = new Dictionary<
			LargeLadTeamSpawn,
			IReadOnlyList<LargeLadSpawnLocation>>();
		var byGroup = new Dictionary<
			LargeLadSpawnGroup,
			IReadOnlyList<LargeLadSpawnLocation>>();

		foreach ( var group in Enum.GetValues<LargeLadSpawnGroup>() )
		{
			var groupCandidates = new List<LargeLadSpawnLocation>();

			foreach ( var spawn in spawns.Where( spawn => spawn.Group == group ) )
			{
				var candidates = GenerateCandidates(
					scene,
					spawn,
					groupCandidates );
				bySpawn.Add( spawn, candidates );
				groupCandidates.AddRange( candidates );
			}

			byGroup.Add( group, groupCandidates );
		}

		return new LargeLadSpawnProjectionResult(
			spawns,
			bySpawn,
			byGroup );
	}

	public static LargeLadSpawnProjectionResult BuildScene( Scene scene )
	{
		return Build(
			scene,
			scene?.GetAllComponents<LargeLadTeamSpawn>() ??
				Enumerable.Empty<LargeLadTeamSpawn>() );
	}

	/// <summary>
	/// Explicit mapper operation. Projection data is transient and is never
	/// serialized into the map or backed by a hidden allocator component.
	/// </summary>
	public static LargeLadSpawnProjectionResult RebuildAuthoringPreview(
		Scene scene,
		IEnumerable<LargeLadTeamSpawn> authoredSpawns = null )
	{
		var result = authoredSpawns is null
			? BuildScene( scene )
			: Build( scene, authoredSpawns );

		foreach ( var spawn in result.AuthoredSpawns )
			spawn.SetProjectedCandidatesPreview( result.GetCandidates( spawn ) );

		return result;
	}

	private static IReadOnlyList<LargeLadSpawnLocation> GenerateCandidates(
		Scene scene,
		LargeLadTeamSpawn spawn,
		IReadOnlyList<LargeLadSpawnLocation> existingGroupCandidates )
	{
		var desiredCount =
			LargeLadSpawnRules.GetUsableAuthoredCapacity( spawn.Capacity );
		var radius = MathF.Max( 0.0f, spawn.SpawnRadius );
		var separation = MathF.Max(
			LargeLadGameplayRules.PlayerBodyRadius * 2.0f,
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

			if ( !TryProjectToSafeFloor(
				scene,
				desiredPosition,
				out var position ) )
			{
				continue;
			}

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

	private static bool TryProjectToSafeFloor(
		Scene scene,
		Vector3 desiredPosition,
		out Vector3 safePosition )
	{
		if ( scene is null )
		{
			safePosition = default;
			return false;
		}

		var floorTrace = scene.Trace
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
			safePosition +
				Vector3.Up * LargeLadGameplayRules.PlayerBodyRadius,
			safePosition +
				Vector3.Up *
					(LargeLadGameplayRules.PlayerBodyHeight -
						LargeLadGameplayRules.PlayerBodyRadius),
			LargeLadGameplayRules.PlayerBodyRadius - 0.5f );
		var clearance = scene.Trace
			.Capsule( capsule )
			.IgnoreDynamic()
			.WithoutTags( "player" )
			.Run();

		return !clearance.Hit && !clearance.StartedSolid;
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

		return right * layoutOffset.x + forward * layoutOffset.y;
	}
}
