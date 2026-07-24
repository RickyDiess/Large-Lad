using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;

public enum LargeLadSpawnGroup
{
	Lobby,
	SkinnyKid,
	Hunter
}

public enum LargeLadMapIssueSeverity
{
	Warning,
	Error
}

public static class LargeLadHammerPreview
{
	public const string ObjectName = "Large Lad Hammer Preview";

	public static void HideAtRuntime( GameObject root )
	{
		var preview = root?.Children.FirstOrDefault( child => child.Name == ObjectName );

		if ( preview is not null )
			preview.Enabled = false;
	}
}

public sealed class LargeLadMapIssue
{
	public LargeLadMapIssueSeverity Severity { get; }
	public string Message { get; }

	public LargeLadMapIssue( LargeLadMapIssueSeverity severity, string message )
	{
		Severity = severity;
		Message = message;
	}

	public override string ToString() => $"{Severity}: {Message}";
}

/// <summary>
/// A mapper-authored team spawn. One point supplies a randomized circular
/// formation instead of representing one hard-linked player slot.
/// </summary>
public sealed class LargeLadTeamSpawn : Component
{
	[Property]
	public LargeLadSpawnGroup Group { get; set; }

	[Property]
	public float SpawnRadius { get; set; } = 160.0f;

	[Property]
	public int Capacity { get; set; } = 16;

	[Property]
	public float MinimumSeparation { get; set; } = 48.0f;

	public Color MarkerColor => Group switch
	{
		LargeLadSpawnGroup.Lobby => Color.White,
		LargeLadSpawnGroup.SkinnyKid => new Color( 0.25f, 0.85f, 1.0f ),
		LargeLadSpawnGroup.Hunter => new Color( 1.0f, 0.22f, 0.08f ),
		_ => Color.Gray
	};

	protected override void OnStart()
	{
		LargeLadHammerPreview.HideAtRuntime( GameObject );
	}

	protected override void OnValidate()
	{
		SpawnRadius = System.MathF.Max( 0.0f, SpawnRadius );
		Capacity = System.Math.Clamp( Capacity, 1, LargeLadMapDefinition.TargetPlayerCount );
		MinimumSeparation = System.MathF.Max( 32.0f, MinimumSeparation );
	}

	protected override void DrawGizmos()
	{
		var radius = System.MathF.Max( 0.0f, SpawnRadius );
		var previewCount = System.Math.Clamp( Capacity, 1, LargeLadMapDefinition.TargetPlayerCount );
		var color = MarkerColor;
		const float goldenAngle = 2.39996323f;

		Gizmo.Draw.IgnoreDepth = true;
		Gizmo.Draw.Color = color.WithAlpha( 0.9f );
		Gizmo.Draw.LineThickness = 2.0f;
		Gizmo.Draw.LineCircle( Vector3.Zero, radius );

		for ( var index = 0; index < previewCount; index++ )
		{
			var normalizedRadius = System.MathF.Sqrt( (index + 0.5f) / previewCount );
			var angle = index * goldenAngle;
			var position = new Vector3(
				System.MathF.Cos( angle ) * radius * normalizedRadius,
				System.MathF.Sin( angle ) * radius * normalizedRadius,
				0.0f );

			Gizmo.Draw.Color = color.WithAlpha( 0.18f );
			Gizmo.Draw.SolidCapsule(
				position + Vector3.Up * 16.0f,
				position + Vector3.Up * 56.0f,
				16.0f,
				8,
				4 );
		}

		Gizmo.Draw.Color = color;
		Gizmo.Draw.Arrow( Vector3.Up * 54.0f, Vector3.Forward * 38.0f );
		Gizmo.Draw.Text(
			$"{Group} Spawn ({Capacity})",
			new Transform( Vector3.Up * 82.0f ),
			"Inter",
			14.0f );
		Gizmo.Draw.LineThickness = 1.0f;
		Gizmo.Draw.IgnoreDepth = false;
	}
}

public readonly struct LargeLadSpawnLocation
{
	public Vector3 Position { get; }
	public Rotation Rotation { get; }
	public float MinimumSeparation { get; }

	public LargeLadSpawnLocation(
		Vector3 position,
		Rotation rotation,
		float minimumSeparation )
	{
		Position = position;
		Rotation = rotation;
		MinimumSeparation = minimumSeparation;
	}
}

/// <summary>
/// The authored contract for a Large Lad map. Gameplay code discovers this
/// component, so duplicating a conforming map never requires code changes.
/// </summary>
public sealed class LargeLadMapDefinition : Component
{
	public const int TargetPlayerCount = 16;
	private const int RuntimeSetupGraceFrames = 30;
	private const float PlayerRadius = 16.0f;
	private const float PlayerHeight = 72.0f;
	private const int AttemptsPerRequestedPosition = 48;

	[Property]
	public float HeadStartDuration { get; set; } = 10.0f;

	[Property]
	public float SurvivalDuration { get; set; } = 60.0f;

	[Property]
	public float IntermissionDuration { get; set; } = 5.0f;

	[Property]
	public NetworkHelper NetworkHelper { get; set; }

	[Property]
	public LargeLadRoundManager RoundManager { get; set; }

	private readonly List<GameObject> runtimeLobbySpawnPoints = new();
	private int runtimeSetupFrames;
	private int spawnGeneration;
	private bool runtimeSetupComplete;

	public IReadOnlyList<LargeLadTeamSpawn> LobbySpawns =>
		GetTeamSpawns( LargeLadSpawnGroup.Lobby );

	public IReadOnlyList<LargeLadTeamSpawn> SkinnyKidSpawns =>
		GetTeamSpawns( LargeLadSpawnGroup.SkinnyKid );

	public IReadOnlyList<LargeLadTeamSpawn> HunterSpawns =>
		GetTeamSpawns( LargeLadSpawnGroup.Hunter );

	protected override void OnAwake()
	{
		ResolveManagers();
	}

	protected override void OnStart()
	{
		LargeLadHammerPreview.HideAtRuntime( GameObject );
		runtimeSetupFrames = 0;
		runtimeSetupComplete = false;
	}

	protected override void OnDestroy()
	{
		ClearRuntimeLobbySpawnPoints();
	}

	protected override void OnUpdate()
	{
		if ( runtimeSetupComplete )
			return;

		// Hammer streams embedded GameObjects in map order. Keep discovering the
		// three spawn groups for a short grace period before final validation.
		runtimeSetupFrames++;
		ResolveManagers();
		RoundManager?.UseMapDefinition( this );

		if ( HasCompleteRuntimeContract() || runtimeSetupFrames >= RuntimeSetupGraceFrames )
		{
			runtimeSetupComplete = true;
			ConfigureNetworkHelperSpawns();
			ValidateMap( logResults: true );
		}
	}

	protected override void OnValidate()
	{
		ResolveManagers();
		RoundManager?.UseMapDefinition( this );
	}

	public IReadOnlyDictionary<LargeLadPlayer, LargeLadSpawnLocation> AllocateSpawnBatch(
		LargeLadSpawnGroup group,
		IReadOnlyList<LargeLadPlayer> players )
	{
		var allocations = new Dictionary<LargeLadPlayer, LargeLadSpawnLocation>();

		if ( players is null || players.Count == 0 )
			return allocations;

		var allCandidates = GenerateSpawnCandidates( group );
		var availableCandidates = new List<LargeLadSpawnLocation>( allCandidates );
		var batch = players.Where( player => player is not null ).ToHashSet();
		var occupied = Scene
			.GetAllComponents<LargeLadPlayer>()
			.Where( player => !batch.Contains( player ) && player.Health?.IsDead != true )
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

			if ( availableCandidates.Count == 0 )
			{
				Log.Error(
					$"{group} has fewer geometrically valid positions than the requested " +
					$"{players.Count}-player spawn batch. Refusing to overlap reserved slots." );
				break;
			}

			var clear = availableCandidates
				.Where( candidate => IsFarEnough( candidate, occupied ) &&
					IsFarEnough( candidate, reserved ) )
				.ToList();
			LargeLadSpawnLocation selected;

			if ( clear.Count > 0 )
			{
				selected = clear[random.Next( clear.Count )];
			}
			else
			{
				// Every safe slot is occupied. Preserve geometric validity and use
				// the unreserved point with the greatest clearance from everybody.
				selected = availableCandidates
					.OrderByDescending( candidate => MinimumDistanceSquared(
						candidate.Position,
						occupied,
						reserved ) )
					.First();
			}

			allocations[player] = selected;
			reserved.Add( selected.Position );
			availableCandidates.Remove( selected );
		}

		return allocations;
	}

	public bool TryAllocateSpawn(
		LargeLadSpawnGroup group,
		LargeLadPlayer player,
		out LargeLadSpawnLocation location )
	{
		var candidates = GenerateSpawnCandidates( group );

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

	public IReadOnlyList<LargeLadMapIssue> ValidateMap( bool logResults )
	{
		var issues = new List<LargeLadMapIssue>();
		ResolveManagers();

		var networkHelpers = Scene?.GetAllComponents<NetworkHelper>().ToList() ?? new();
		var roundManagers = Scene?.GetAllComponents<LargeLadRoundManager>().ToList() ?? new();
		var mapDefinitions = Scene?.GetAllComponents<LargeLadMapDefinition>().ToList() ?? new();

		if ( networkHelpers.Count != 1 )
			AddError( issues, $"Expected exactly one NetworkHelper, found {networkHelpers.Count}." );

		if ( roundManagers.Count != 1 )
			AddError( issues, $"Expected exactly one LargeLadRoundManager, found {roundManagers.Count}." );

		if ( mapDefinitions.Count != 1 )
			AddError( issues, $"Expected exactly one LargeLadMapDefinition, found {mapDefinitions.Count}." );

		if ( HeadStartDuration < 0.0f )
			AddError( issues, "Head-start duration cannot be negative." );

		if ( SurvivalDuration <= 0.0f )
			AddError( issues, "Survival duration must be greater than zero." );

		if ( IntermissionDuration < 0.0f )
			AddError( issues, "Intermission duration cannot be negative." );

		ValidateSpawnGroup( issues, LargeLadSpawnGroup.Lobby, TargetPlayerCount );
		ValidateSpawnGroup( issues, LargeLadSpawnGroup.SkinnyKid, TargetPlayerCount - 1 );
		ValidateSpawnGroup( issues, LargeLadSpawnGroup.Hunter, TargetPlayerCount );

		var weaponPickups = Scene?.GetAllComponents<LargeLadWeaponPickup>().ToList() ?? new();
		var ammoPickups = Scene?.GetAllComponents<LargeLadAmmoPickup>().ToList() ?? new();

		foreach ( var pickup in weaponPickups )
		{
			if ( !LargeLadWeaponCatalog.IsFirearm( pickup.Weapon ) )
				AddError( issues, $"Weapon pickup '{pickup.GameObject.Name}' has no valid firearm." );

			if ( pickup.PickupCollider is null )
				AddError( issues, $"Weapon pickup '{pickup.GameObject.Name}' is missing a trigger collider." );

			if ( pickup.Policy == LargeLadPickupPolicy.GloballyExclusive &&
				pickup.GameObject.NetworkMode != NetworkMode.Object )
			{
				AddError( issues, $"Exclusive weapon pickup '{pickup.GameObject.Name}' must use Network Mode Object." );
			}
		}

		foreach ( var pickup in ammoPickups )
		{
			if ( !LargeLadWeaponCatalog.IsFirearm( pickup.Weapon ) )
				AddError( issues, $"Ammo pickup '{pickup.GameObject.Name}' has no valid firearm." );

			if ( pickup.PickupCollider is null )
				AddError( issues, $"Ammo pickup '{pickup.GameObject.Name}' is missing a trigger collider." );
		}

		ValidateCorePickup( issues, weaponPickups, ammoPickups, LargeLadWeaponId.Pistol );
		ValidateCorePickup( issues, weaponPickups, ammoPickups, LargeLadWeaponId.Smg );

		foreach ( var barricade in Scene?.GetAllComponents<LargeLadBarricade>() ?? Enumerable.Empty<LargeLadBarricade>() )
		{
			if ( !barricade.HasVisibleGeometry || !barricade.HasCollision )
				AddError( issues, $"Barricade '{barricade.GameObject.Name}' needs visible geometry and collision on the same GameObject." );

			if ( barricade.GameObject.NetworkMode != NetworkMode.Never )
				AddError( issues, $"Barricade geometry '{barricade.GameObject.Name}' must use Network Mode Never." );

			if ( !barricade.HasNetworkState )
			{
				AddError( issues, $"Barricade '{barricade.GameObject.Name}' is missing its network state child." );
			}
			else if ( barricade.NetworkState.GameObject.NetworkMode != NetworkMode.Object )
			{
				AddError( issues, $"Barricade state for '{barricade.GameObject.Name}' must use Network Mode Object." );
			}

			if ( barricade.HasRedundantBoxCollider )
			{
				AddWarning(
					issues,
					$"Barricade '{barricade.GameObject.Name}' has a BoxCollider in addition to its tied Hammer mesh. " +
					"Remove it unless the extra collision was intentional." );
			}
		}

		foreach ( var killVolume in Scene?.GetAllComponents<LargeLadKillVolume>() ?? Enumerable.Empty<LargeLadKillVolume>() )
		{
			if ( !killVolume.HasTriggerShape )
				AddError( issues, $"Kill volume '{killVolume.GameObject.Name}' needs a tied Hammer mesh or trigger collider." );
		}

		if ( logResults )
		{
			if ( issues.Count == 0 )
			{
				Log.Info( $"Map '{GameObject.Name}' passes the Large Lad 16-player contract." );
			}
			else
			{
				foreach ( var issue in issues )
				{
					if ( issue.Severity == LargeLadMapIssueSeverity.Error )
						Log.Error( $"Map contract: {issue.Message}" );
					else
						Log.Warning( $"Map contract: {issue.Message}" );
				}
			}
		}

		return issues;
	}

	private IReadOnlyList<LargeLadTeamSpawn> GetTeamSpawns( LargeLadSpawnGroup group )
	{
		if ( Scene is null )
			return Array.Empty<LargeLadTeamSpawn>();

		return Scene
			.GetAllComponents<LargeLadTeamSpawn>()
			.Where( spawn => spawn.Group == group )
			.ToList();
	}

	private List<LargeLadSpawnLocation> GenerateSpawnCandidates( LargeLadSpawnGroup group )
	{
		var candidates = new List<LargeLadSpawnLocation>();
		var random = CreateRandom();

		foreach ( var spawn in GetTeamSpawns( group ) )
			GenerateSpawnCandidates( spawn, candidates, random );

		return candidates;
	}

	private void GenerateSpawnCandidates(
		LargeLadTeamSpawn spawn,
		List<LargeLadSpawnLocation> candidates,
		Random random )
	{
		var desiredCount = System.Math.Clamp( spawn.Capacity, 1, TargetPlayerCount );
		var radius = System.MathF.Max( 0.0f, spawn.SpawnRadius );
		var separation = System.MathF.Max( PlayerRadius * 2.0f, spawn.MinimumSeparation );
		var generatedForAnchor = 0;
		var attempts = desiredCount * AttemptsPerRequestedPosition;

		for ( var attempt = 0; attempt < attempts && generatedForAnchor < desiredCount; attempt++ )
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

			if ( !IsFarEnough( candidate, candidates.Select( item => item.Position ) ) )
				continue;

			candidates.Add( candidate );
			generatedForAnchor++;
		}
	}

	private bool TryProjectToSafeFloor( Vector3 desiredPosition, out Vector3 safePosition )
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
		var clearanceTrace = Scene.Trace
			.Capsule( capsule )
			.WithoutTags( "player" )
			.Run();

		return !clearanceTrace.Hit && !clearanceTrace.StartedSolid;
	}

	private LargeLadSpawnLocation? GetEmergencySpawn( LargeLadSpawnGroup group )
	{
		var spawn = GetTeamSpawns( group ).FirstOrDefault();

		if ( spawn is null )
		{
			Log.Error( $"No {group} team spawn exists." );
			return null;
		}

		Log.Error(
			$"{group} spawn '{spawn.GameObject.Name}' produced no geometrically valid positions; " +
			"using its origin as an emergency fallback." );
		return new LargeLadSpawnLocation(
			spawn.GameObject.WorldPosition,
			spawn.GameObject.WorldRotation,
			System.MathF.Max( PlayerRadius * 2.0f, spawn.MinimumSeparation ) );
	}

	private void ResolveManagers()
	{
		NetworkHelper ??= Scene?.GetAllComponents<NetworkHelper>().FirstOrDefault();
		RoundManager ??= Scene?.GetAllComponents<LargeLadRoundManager>().FirstOrDefault();
	}

	private void ConfigureNetworkHelperSpawns()
	{
		if ( NetworkHelper is null )
			return;

		ClearRuntimeLobbySpawnPoints();
		var candidates = GenerateSpawnCandidates( LargeLadSpawnGroup.Lobby );

		if ( candidates.Count == 0 )
		{
			var emergency = GetEmergencySpawn( LargeLadSpawnGroup.Lobby );
			if ( emergency is not null )
				candidates.Add( emergency.Value );
		}

		for ( var index = 0; index < candidates.Count; index++ )
		{
			var candidate = candidates[index];
			var point = new GameObject( true, $"Runtime Lobby Spawn {index + 1:00}" );
			point.NetworkMode = NetworkMode.Never;
			point.IsStatic = true;
			point.WorldPosition = candidate.Position;
			point.WorldRotation = candidate.Rotation;
			runtimeLobbySpawnPoints.Add( point );
		}

		NetworkHelper.SpawnPoints.Clear();
		NetworkHelper.SpawnPoints.AddRange( runtimeLobbySpawnPoints );
	}

	private void ClearRuntimeLobbySpawnPoints()
	{
		foreach ( var point in runtimeLobbySpawnPoints )
		{
			if ( point is not null && point.IsValid )
				point.Destroy();
		}

		runtimeLobbySpawnPoints.Clear();
	}

	private bool HasCompleteRuntimeContract()
	{
		if ( Scene is null || NetworkHelper is null || RoundManager is null )
			return false;

		if ( Scene.GetAllComponents<LargeLadMapDefinition>().Count() != 1 )
			return false;

		return HasSpawnCapacity( LargeLadSpawnGroup.Lobby, TargetPlayerCount ) &&
			HasSpawnCapacity( LargeLadSpawnGroup.SkinnyKid, TargetPlayerCount - 1 ) &&
			HasSpawnCapacity( LargeLadSpawnGroup.Hunter, TargetPlayerCount );
	}

	private bool HasSpawnCapacity( LargeLadSpawnGroup group, int requiredCapacity )
	{
		var spawns = GetTeamSpawns( group );
		return spawns.Count > 0 && spawns.Sum( spawn => System.Math.Max( 0, spawn.Capacity ) ) >= requiredCapacity;
	}

	private void ValidateSpawnGroup(
		List<LargeLadMapIssue> issues,
		LargeLadSpawnGroup group,
		int requiredCapacity )
	{
		var spawns = GetTeamSpawns( group );

		if ( spawns.Count == 0 )
		{
			AddError( issues, $"{group} needs at least one generic team spawn." );
			return;
		}

		var configuredCapacity = spawns.Sum( spawn => System.Math.Max( 0, spawn.Capacity ) );
		if ( configuredCapacity < requiredCapacity )
		{
			AddError(
				issues,
				$"{group} spawn capacity is {configuredCapacity}/{requiredCapacity}." );
		}

		foreach ( var spawn in spawns )
		{
			if ( spawn.Capacity <= 0 )
				AddError( issues, $"{group} spawn '{spawn.GameObject.Name}' has no capacity." );

			if ( spawn.SpawnRadius < 0.0f )
				AddError( issues, $"{group} spawn '{spawn.GameObject.Name}' has a negative radius." );

			if ( spawn.MinimumSeparation < PlayerRadius * 2.0f )
			{
				AddWarning(
					issues,
					$"{group} spawn '{spawn.GameObject.Name}' separation is below the 32-unit player width." );
			}

			if ( runtimeSetupComplete )
			{
				var anchorCandidates = new List<LargeLadSpawnLocation>();
				GenerateSpawnCandidates( spawn, anchorCandidates, CreateRandom() );

				if ( anchorCandidates.Count == 0 )
				{
					AddError(
						issues,
						$"{group} spawn '{spawn.GameObject.Name}' produced no geometrically valid positions." );
				}
			}
		}

		if ( runtimeSetupComplete )
		{
			var generatedCapacity = GenerateSpawnCandidates( group ).Count;
			if ( generatedCapacity == 0 )
			{
				AddError( issues, $"{group} produced no geometrically valid spawn positions." );
			}
			else if ( generatedCapacity < requiredCapacity )
			{
				AddWarning(
					issues,
					$"{group} produced {generatedCapacity}/{requiredCapacity} clear positions; " +
					"crowded spawns will use the least-crowded valid fallback." );
			}
		}
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

		forward = forward.LengthSquared > 0.001f ? forward.Normal : Vector3.Forward;
		right = right.LengthSquared > 0.001f ? right.Normal : Vector3.Right;

		return right * (System.MathF.Cos( angle ) * distance) +
			forward * (System.MathF.Sin( angle ) * distance);
	}

	private static bool IsFarEnough(
		LargeLadSpawnLocation candidate,
		IEnumerable<Vector3> otherPositions )
	{
		var minimumDistanceSquared = candidate.MinimumSeparation * candidate.MinimumSeparation;
		return otherPositions.All( position =>
			candidate.Position.DistanceSquared( position ) >= minimumDistanceSquared );
	}

	private static float MinimumDistanceSquared(
		Vector3 position,
		IReadOnlyList<Vector3> occupied,
		IReadOnlyList<Vector3> reserved )
	{
		var minimum = float.MaxValue;

		foreach ( var other in occupied )
			minimum = System.MathF.Min( minimum, position.DistanceSquared( other ) );

		foreach ( var other in reserved )
			minimum = System.MathF.Min( minimum, position.DistanceSquared( other ) );

		return minimum;
	}

	private Random CreateRandom()
	{
		spawnGeneration++;
		return new Random( unchecked(spawnGeneration * 7919 + 104729) );
	}

	private static void ValidateCorePickup(
		List<LargeLadMapIssue> issues,
		IReadOnlyList<LargeLadWeaponPickup> weaponPickups,
		IReadOnlyList<LargeLadAmmoPickup> ammoPickups,
		LargeLadWeaponId weapon )
	{
		var displayName = LargeLadWeaponCatalog.Get( weapon ).DisplayName;

		if ( !weaponPickups.Any( pickup =>
			pickup.Weapon == weapon &&
			pickup.Policy == LargeLadPickupPolicy.CorePerPlayer ) )
		{
			AddWarning( issues, $"No core {displayName} pickup is placed." );
		}

		if ( !ammoPickups.Any( pickup => pickup.Weapon == weapon ) )
			AddWarning( issues, $"No {displayName} ammo refill is placed." );
	}

	private static void AddWarning( List<LargeLadMapIssue> issues, string message )
	{
		issues.Add( new LargeLadMapIssue( LargeLadMapIssueSeverity.Warning, message ) );
	}

	private static void AddError( List<LargeLadMapIssue> issues, string message )
	{
		issues.Add( new LargeLadMapIssue( LargeLadMapIssueSeverity.Error, message ) );
	}
}
