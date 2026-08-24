using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;

public enum LargeLadMapIssueSeverity
{
	BlockingError,
	Warning
}

/// <summary>
/// One mapper-facing map issue. Keep this deliberately small: maps only need
/// to distinguish readiness blockers from useful authoring advice.
/// </summary>
public sealed class LargeLadMapIssue
{
	public LargeLadMapIssue(
		LargeLadMapIssueSeverity severity,
		string objectName,
		string message,
		Component source = null )
	{
		Severity = severity;
		ObjectName = objectName?.Trim() ?? string.Empty;
		Message = message?.Trim() ?? string.Empty;
		Source = source;
	}

	public LargeLadMapIssueSeverity Severity { get; }
	public string ObjectName { get; }
	public string Message { get; }
	public Component Source { get; }
	public bool IsBlocking => Severity == LargeLadMapIssueSeverity.BlockingError;

	public override string ToString()
	{
		return string.IsNullOrWhiteSpace( ObjectName )
			? Message
			: $"{ObjectName}: {Message}";
	}
}

public sealed class LargeLadMapValidationResult
{
	internal LargeLadMapValidationResult(
		IReadOnlyList<LargeLadMapIssue> issues,
		LargeLadSpawnProjectionResult spawnProjection )
	{
		Issues = issues;
		SpawnProjection = spawnProjection;
	}

	public IReadOnlyList<LargeLadMapIssue> Issues { get; }
	public LargeLadSpawnProjectionResult SpawnProjection { get; }
	public int BlockingErrorCount => Issues.Count( issue => issue.IsBlocking );
	public int WarningCount => Issues.Count( issue => !issue.IsBlocking );
	public bool CanAdmitMap => BlockingErrorCount == 0;

	public void LogMapperSummary()
	{
		if ( Issues.Count == 0 )
		{
			Log.Info(
				"Large Lad map validation succeeded: no blocking errors or warnings." );
			return;
		}

		Log.Info(
			$"Large Lad map validation: {BlockingErrorCount} blocking " +
			$"{Pluralize( BlockingErrorCount, "error" )}, {WarningCount} " +
			$"{Pluralize( WarningCount, "warning" )}." );

		foreach ( var issue in Issues )
		{
			if ( issue.IsBlocking )
				Log.Error( $"Large Lad map: {issue}" );
			else
				Log.Warning( $"Large Lad map: {issue}" );
		}
	}

	private static string Pluralize( int count, string singular )
	{
		return count == 1 ? singular : $"{singular}s";
	}
}

/// <summary>
/// The focused source of truth for mapper-authored Large Lad content. It is
/// used explicitly from the content-scene editor and once during loaded-map
/// admission; gameplay continues to use registries and allocator caches.
/// </summary>
public static class LargeLadMapValidator
{
	private sealed class MapContent
	{
		public Scene Scene { get; init; }
		public IReadOnlyList<LargeLadMapProfile> Profiles { get; init; }
		public IReadOnlyList<LargeLadTeamSpawn> TeamSpawns { get; init; }
		public IReadOnlyList<LargeLadWeaponPickup> WeaponPickups { get; init; }
		public IReadOnlyList<LargeLadDodgeballPickup> Dodgeballs { get; init; }
		public IReadOnlyList<LargeLadBarricade> Barricades { get; init; }
		public IReadOnlyList<LargeLadMinionPassage> MinionPassages { get; init; }
		public IReadOnlyList<LargeLadGroundSlamReactiveProp> ReactiveProps { get; init; }
		public IReadOnlyList<LargeLadEatSmashable> EatSmashables { get; init; }
		public IReadOnlyList<LargeLadKillVolume> KillVolumes { get; init; }
		public IReadOnlyList<Component> ForbiddenComponents { get; init; }
	}

	public static LargeLadMapValidationResult ValidateScene(
		Scene scene,
		bool rebuildSpawnPreview = false )
	{
		var content = CaptureScene( scene );
		var projection = rebuildSpawnPreview
			? LargeLadSpawnProjection.RebuildAuthoringPreview(
				scene,
				content.TeamSpawns )
			: LargeLadSpawnProjection.Build( scene, content.TeamSpawns );
		return Validate(
			content,
			projection,
			includeLocalManifestRules: true );
	}

	public static LargeLadMapValidationResult ValidateLoadedContent(
		GameObject mapContentHost,
		LargeLadSpawnProjectionResult spawnProjection = null )
	{
		var content = CaptureRoot( mapContentHost );
		var projection = spawnProjection ?? LargeLadSpawnProjection.Build(
			content.Scene,
			content.TeamSpawns );
		return Validate(
			content,
			projection,
			includeLocalManifestRules: false );
	}

	public static bool TryGetSingleEnabledProfile(
		GameObject mapContentHost,
		out LargeLadMapProfile profile,
		out IReadOnlyList<LargeLadMapIssue> issues )
	{
		var profiles = GetComponents<LargeLadMapProfile>( mapContentHost )
			.Where( IsEnabled )
			.ToArray();
		return TryGetSingleEnabledProfile( profiles, out profile, out issues );
	}

	private static LargeLadMapValidationResult Validate(
		MapContent content,
		LargeLadSpawnProjectionResult projection,
		bool includeLocalManifestRules )
	{
		var issues = new List<LargeLadMapIssue>();

		if ( content.Scene is null )
		{
			issues.Add( Blocking(
				"Large Lad map",
				"no editable scene is available. Open the content scene and try again." ) );
			return new LargeLadMapValidationResult( issues, projection );
		}

		if ( !TryGetSingleEnabledProfile(
			content.Profiles.Where( IsEnabled ).ToArray(),
			out var profile,
			out var profileIssues ) )
		{
			issues.AddRange( profileIssues );
		}
		else if ( profile.Manifest is null )
		{
			issues.Add( Blocking(
				ProfileName( profile ),
				"no .llmap manifest is assigned. Create a Large Lad Map Manifest " +
				"and assign it to Manifest.",
				profile ) );
		}
		else if ( includeLocalManifestRules )
		{
			foreach ( var manifestIssue in
				LargeLadMapCatalog.GetLocalManifestValidationIssues(
					profile.Manifest ) )
			{
				issues.Add( Blocking(
					ProfileName( profile ),
					$"{manifestIssue} Fix the assigned .llmap manifest and validate again.",
					profile ) );
			}
		}

		foreach ( var forbidden in content.ForbiddenComponents )
		{
			issues.Add( Blocking(
				$"Forbidden object '{forbidden.GameObject.Name}'",
				$"contains {FriendlyForbiddenType( forbidden )}. Remove this " +
				"bootstrap/session component from the content map; the Large Lad " +
				"game shell supplies it.",
				forbidden ) );
		}

		ValidateSpawnAreas( issues, content.TeamSpawns, projection );
		ValidateWeaponPickups( issues, content.WeaponPickups );
		ValidateDodgeballs( issues, content.Dodgeballs );
		ValidateComponents(
			issues,
			content.Barricades,
			"Barricade",
			barricade => barricade.GetMapperValidationWarnings() );
		ValidateComponents(
			issues,
			content.MinionPassages,
			"Minion Vent Opening",
			passage => passage.GetMapperValidationWarnings() );
		ValidateComponents(
			issues,
			content.ReactiveProps,
			"Ground Slam Reactive Prop",
			prop => prop.GetValidationWarnings() );
		ValidateComponents(
			issues,
			content.EatSmashables,
			"Eat Smashable",
			smashable => smashable.GetValidationWarnings() );
		ValidateComponents(
			issues,
			content.KillVolumes,
			"Kill Volume",
			volume => volume.GetValidationWarnings() );

		return new LargeLadMapValidationResult( issues, projection );
	}

	private static bool TryGetSingleEnabledProfile(
		IReadOnlyList<LargeLadMapProfile> profiles,
		out LargeLadMapProfile profile,
		out IReadOnlyList<LargeLadMapIssue> issues )
	{
		profile = profiles?.Count == 1 ? profiles[0] : null;

		if ( profile is not null )
		{
			issues = Array.Empty<LargeLadMapIssue>();
			return true;
		}

		var count = profiles?.Count ?? 0;
		var message = count == 0
			? "no enabled Large Lad Map Profile exists. Keep exactly one profile " +
				"in the content scene and assign its .llmap manifest."
			: $"{count} enabled Large Lad Map Profiles exist " +
				$"({string.Join( ", ", profiles.Select( ProfileName ) )}). " +
				"Keep exactly one enabled profile.";
		issues = new[] { Blocking( "Large Lad map profile", message ) };
		return false;
	}

	private static void ValidateSpawnAreas(
		List<LargeLadMapIssue> issues,
		IReadOnlyList<LargeLadTeamSpawn> spawns,
		LargeLadSpawnProjectionResult projection )
	{
		foreach ( var spawn in spawns.Where( IsEnabled ) )
		{
			if ( !float.IsFinite( spawn.SpawnRadius ) || spawn.SpawnRadius <= 0.0f )
			{
				issues.Add( Warning(
					SpawnName( spawn ),
					"Spawn Radius must be finite and greater than zero. Resize the " +
					"circle over walkable floor, then rebuild spawns.",
					spawn ) );
			}

			if ( !float.IsFinite( spawn.MinimumSeparation ) ||
				spawn.MinimumSeparation <
					LargeLadGameplayRules.PlayerBodyRadius * 2.0f )
			{
				issues.Add( Warning(
					SpawnName( spawn ),
					$"Minimum Separation must be at least " +
					$"{LargeLadGameplayRules.PlayerBodyRadius * 2.0f:0} for the " +
					"authoritative player capsule. Use the supplied spawn preset's " +
					"default unless the area needs more spacing.",
					spawn ) );
			}
		}

		foreach ( var group in Enum.GetValues<LargeLadSpawnGroup>() )
		{
			var groupSpawns = spawns
				.Where( spawn => IsEnabled( spawn ) && spawn.Group == group )
				.ToArray();
			var areaCapacities = groupSpawns
				.Select( spawn => new LargeLadSpawnAreaCapacity(
					spawn.GameObject.Name,
					spawn.Capacity,
					projection?.GetCandidates( spawn ).Count ?? 0 ) )
				.ToArray();
			var evaluation = LargeLadSpawnRules.EvaluateGroupCapacity(
				group,
				LargeLadGameManager.TargetPlayerCount,
				areaCapacities );

			if ( evaluation.Failure == LargeLadSpawnCapacityFailure.None )
				continue;

			var groupName = FriendlyGroupName( group );
			var objectName = $"{groupName} spawns";
			var areaDescription = DescribeSpawnAreas( areaCapacities );
			var message = evaluation.Failure switch
			{
				LargeLadSpawnCapacityFailure.MissingArea =>
					$"no {groupName} Team Spawn area exists. Place the supplied " +
					$"{groupName} Team Spawn prefab; this map needs " +
					$"{evaluation.RequiredCapacity} usable positions.",
				LargeLadSpawnCapacityFailure.ConfiguredCapacityShortfall =>
					$"configured capacity is {evaluation.ConfiguredCapacity}/" +
					$"{evaluation.RequiredCapacity}. Areas: {areaDescription}. " +
					"Increase Capacity or add another supplied team-spawn prefab, " +
					"then rebuild spawns.",
				LargeLadSpawnCapacityFailure.GeometryShortfall =>
					$"geometry projection produced {evaluation.ValidCapacity}/" +
					$"{evaluation.RequiredCapacity} usable positions from " +
					$"{evaluation.ConfiguredCapacity} configured. Areas: " +
					$"{areaDescription}. Move or resize the named circles over " +
					"walkable floor, clear nearby walls or ceilings, then use " +
					"Rebuild Spawns and Validate.",
				_ => "the spawn group is invalid."
			};
			issues.Add( Blocking( objectName, message ) );
		}
	}

	private static void ValidateWeaponPickups(
		List<LargeLadMapIssue> issues,
		IReadOnlyList<LargeLadWeaponPickup> pickups )
	{
		ValidateComponents(
			issues,
			pickups,
			"Weapon Pickup",
			pickup => pickup.GetValidationWarnings() );
	}

	private static void ValidateDodgeballs(
		List<LargeLadMapIssue> issues,
		IReadOnlyList<LargeLadDodgeballPickup> pickups )
	{
		ValidateComponents(
			issues,
			pickups,
			"Dodgeball Pickup",
			pickup => pickup.GetValidationWarnings() );
	}

	private static void ValidateComponents<T>(
		List<LargeLadMapIssue> issues,
		IReadOnlyList<T> components,
		string friendlyType,
		Func<T, IReadOnlyList<string>> getWarnings )
		where T : Component
	{
		foreach ( var component in components.Where( IsEnabled ) )
		{
			foreach ( var warning in getWarnings( component ) )
			{
				issues.Add( Warning(
					$"{friendlyType} '{component.GameObject.Name}'",
					warning,
					component ) );
			}
		}
	}

	private static MapContent CaptureScene( Scene scene )
	{
		var forbidden = new List<Component>();
		AddComponents( forbidden, scene?.GetAllComponents<LargeLadGameManager>() );
		AddComponents( forbidden, scene?.GetAllComponents<LargeLadSessionCoordinator>() );
		AddComponents( forbidden, scene?.GetAllComponents<LargeLadSpawnAllocator>() );
		AddComponents( forbidden, scene?.GetAllComponents<NetworkHelper>() );
		AddComponents( forbidden, scene?.GetAllComponents<MapInstance>() );

		return new MapContent
		{
			Scene = scene,
			Profiles = GetSceneComponents<LargeLadMapProfile>( scene ),
			TeamSpawns = GetSceneComponents<LargeLadTeamSpawn>( scene ),
			WeaponPickups = GetSceneComponents<LargeLadWeaponPickup>( scene ),
			Dodgeballs = GetSceneComponents<LargeLadDodgeballPickup>( scene ),
			Barricades = GetSceneComponents<LargeLadBarricade>( scene ),
			MinionPassages = GetSceneComponents<LargeLadMinionPassage>( scene ),
			ReactiveProps = GetSceneComponents<LargeLadGroundSlamReactiveProp>( scene ),
			EatSmashables = GetSceneComponents<LargeLadEatSmashable>( scene ),
			KillVolumes = GetSceneComponents<LargeLadKillVolume>( scene ),
			ForbiddenComponents = forbidden.Where( IsValid ).ToArray()
		};
	}

	private static MapContent CaptureRoot( GameObject root )
	{
		var forbidden = new List<Component>();
		AddComponents( forbidden, GetComponents<LargeLadGameManager>( root ) );
		AddComponents( forbidden, GetComponents<LargeLadSessionCoordinator>( root ) );
		AddComponents( forbidden, GetComponents<LargeLadSpawnAllocator>( root ) );
		AddComponents( forbidden, GetComponents<NetworkHelper>( root ) );
		AddComponents(
			forbidden,
			GetComponents<MapInstance>( root ).Where( component =>
				component.GameObject != root ) );

		return new MapContent
		{
			Scene = root?.Scene,
			Profiles = GetComponents<LargeLadMapProfile>( root ),
			TeamSpawns = GetComponents<LargeLadTeamSpawn>( root ),
			WeaponPickups = GetComponents<LargeLadWeaponPickup>( root ),
			Dodgeballs = GetComponents<LargeLadDodgeballPickup>( root ),
			Barricades = GetComponents<LargeLadBarricade>( root ),
			MinionPassages = GetComponents<LargeLadMinionPassage>( root ),
			ReactiveProps = GetComponents<LargeLadGroundSlamReactiveProp>( root ),
			EatSmashables = GetComponents<LargeLadEatSmashable>( root ),
			KillVolumes = GetComponents<LargeLadKillVolume>( root ),
			ForbiddenComponents = forbidden.Where( IsValid ).ToArray()
		};
	}

	private static IReadOnlyList<T> GetSceneComponents<T>( Scene scene )
		where T : Component
	{
		return scene?.GetAllComponents<T>()
			.Where( IsValid )
			.ToArray() ?? [];
	}

	private static IReadOnlyList<T> GetComponents<T>( GameObject root )
		where T : Component
	{
		return root?.Components.GetAll<T>(
			FindMode.EverythingInSelfAndDescendants )
			.Where( IsValid )
			.ToArray() ?? [];
	}

	private static void AddComponents<T>(
		List<Component> destination,
		IEnumerable<T> components )
		where T : Component
	{
		if ( components is not null )
			destination.AddRange( components.Where( IsValid ) );
	}

	private static bool IsValid( Component component )
	{
		return component is not null && component.IsValid;
	}

	private static bool IsEnabled( Component component )
	{
		return IsValid( component ) &&
			component.Enabled &&
			component.GameObject.Enabled;
	}

	private static string ProfileName( LargeLadMapProfile profile )
	{
		return $"Large Lad Map Profile '{profile?.GameObject?.Name ?? "unknown"}'";
	}

	private static string SpawnName( LargeLadTeamSpawn spawn )
	{
		return $"{FriendlyGroupName( spawn.Group )} Team Spawn " +
			$"'{spawn.GameObject.Name}'";
	}

	private static string FriendlyGroupName( LargeLadSpawnGroup group )
	{
		return group switch
		{
			LargeLadSpawnGroup.SkinnyKid => "Skinny Kid",
			LargeLadSpawnGroup.Lobby => "Lobby",
			LargeLadSpawnGroup.Hunter => "Hunter",
			_ => group.ToString()
		};
	}

	private static string DescribeSpawnAreas(
		IReadOnlyList<LargeLadSpawnAreaCapacity> areas )
	{
		return areas is null || areas.Count == 0
			? "none"
			: string.Join(
				", ",
				areas.Select( area =>
					$"'{area.Name}' {area.ValidCapacity}/" +
					$"{LargeLadSpawnRules.GetUsableAuthoredCapacity( area.ConfiguredCapacity )} usable" ) );
	}

	private static string FriendlyForbiddenType( Component component )
	{
		return component switch
		{
			LargeLadGameManager => "a Large Lad Game Manager",
			LargeLadSessionCoordinator => "a Large Lad Session Coordinator",
			LargeLadSpawnAllocator => "a runtime Spawn Allocator",
			NetworkHelper => "a Network Helper",
			MapInstance => "a Map Instance loader",
			_ => component.GetType().Name
		};
	}

	private static LargeLadMapIssue Blocking(
		string objectName,
		string message,
		Component source = null )
	{
		return new LargeLadMapIssue(
			LargeLadMapIssueSeverity.BlockingError,
			objectName,
			message,
			source );
	}

	private static LargeLadMapIssue Warning(
		string objectName,
		string message,
		Component source = null )
	{
		return new LargeLadMapIssue(
			LargeLadMapIssueSeverity.Warning,
			objectName,
			message,
			source );
	}
}
