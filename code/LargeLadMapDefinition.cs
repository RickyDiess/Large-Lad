using Sandbox;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Per-scene match settings and validation. Spawn generation is delegated to
/// LargeLadSpawnAllocator so this component stays focused on the map contract.
/// </summary>
public sealed class LargeLadMapDefinition : Component
{
	public const int TargetPlayerCount = 16;

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

	[Property]
	public LargeLadSpawnAllocator SpawnAllocator { get; set; }

	protected override void OnAwake()
	{
		ResolveGameplay();
	}

	protected override void OnStart()
	{
		ResolveGameplay();
		ConfigureGameplay();
		ValidateMap( logResults: true, validateGeometry: true );
	}

	protected override void OnValidate()
	{
		ResolveGameplay();
		RoundManager?.UseMapDefinition( this );

		// The bootstrap prefab is also compiled in isolation, where map-authored
		// spawns intentionally do not exist. Full validation still always runs
		// when a playable scene starts.
		if ( Scene?.GetAllComponents<LargeLadTeamSpawn>().Any() != true )
			return;

		ValidateMap( logResults: true, validateGeometry: false );
	}

	public IReadOnlyDictionary<LargeLadPlayer, LargeLadSpawnLocation> AllocateSpawnBatch(
		LargeLadSpawnGroup group,
		IReadOnlyList<LargeLadPlayer> players )
	{
		return SpawnAllocator?.AllocateBatch( group, players ) ??
			new Dictionary<LargeLadPlayer, LargeLadSpawnLocation>();
	}

	public bool TryAllocateSpawn(
		LargeLadSpawnGroup group,
		LargeLadPlayer player,
		out LargeLadSpawnLocation location )
	{
		if ( SpawnAllocator is not null )
			return SpawnAllocator.TryAllocate( group, player, out location );

		location = default;
		return false;
	}

	public IReadOnlyList<string> ValidateMap(
		bool logResults,
		bool validateGeometry = false )
	{
		var issues = new List<string>();
		ResolveGameplay();

		var networkHelpers = Scene?.GetAllComponents<NetworkHelper>().ToList() ?? new();
		var roundManagers = Scene?.GetAllComponents<LargeLadRoundManager>().ToList() ?? new();
		var definitions = Scene?.GetAllComponents<LargeLadMapDefinition>().ToList() ?? new();
		var allocators = Scene?.GetAllComponents<LargeLadSpawnAllocator>().ToList() ?? new();

		if ( networkHelpers.Count != 1 )
			issues.Add( $"Expected one NetworkHelper, found {networkHelpers.Count}." );

		if ( roundManagers.Count != 1 )
			issues.Add( $"Expected one LargeLadRoundManager, found {roundManagers.Count}." );

		if ( definitions.Count != 1 )
			issues.Add( $"Expected one LargeLadMapDefinition, found {definitions.Count}." );

		if ( allocators.Count != 1 )
			issues.Add( $"Expected one LargeLadSpawnAllocator, found {allocators.Count}." );

		if ( HeadStartDuration < 0.0f )
			issues.Add( "Head-start duration cannot be negative." );

		if ( SurvivalDuration <= 0.0f )
			issues.Add( "Survival duration must be greater than zero." );

		if ( IntermissionDuration < 0.0f )
			issues.Add( "Intermission duration cannot be negative." );

		ValidateSpawnGroup(
			issues,
			LargeLadSpawnGroup.Lobby,
			TargetPlayerCount,
			validateGeometry );
		ValidateSpawnGroup(
			issues,
			LargeLadSpawnGroup.SkinnyKid,
			TargetPlayerCount - 1,
			validateGeometry );
		ValidateSpawnGroup(
			issues,
			LargeLadSpawnGroup.Hunter,
			TargetPlayerCount,
			validateGeometry );

		foreach ( var pickup in
			Scene?.GetAllComponents<LargeLadWeaponPickup>() ??
			Enumerable.Empty<LargeLadWeaponPickup>() )
		{
			if ( !LargeLadWeaponCatalog.IsFirearm( pickup.Weapon ) )
				issues.Add( $"Weapon pickup '{pickup.GameObject.Name}' has no valid firearm." );

			if ( pickup.PickupCollider is null )
				issues.Add( $"Weapon pickup '{pickup.GameObject.Name}' needs a trigger collider." );

			if ( pickup.PickupRenderer is null )
				issues.Add( $"Weapon pickup '{pickup.GameObject.Name}' needs visible scene geometry." );
		}

		foreach ( var pickup in
			Scene?.GetAllComponents<LargeLadAmmoPickup>() ??
			Enumerable.Empty<LargeLadAmmoPickup>() )
		{
			if ( !LargeLadWeaponCatalog.IsFirearm( pickup.Weapon ) )
				issues.Add( $"Ammo pickup '{pickup.GameObject.Name}' has no valid firearm." );

			if ( pickup.PickupCollider is null )
				issues.Add( $"Ammo pickup '{pickup.GameObject.Name}' needs a trigger collider." );

			if ( pickup.PickupRenderer is null )
				issues.Add( $"Ammo pickup '{pickup.GameObject.Name}' needs visible scene geometry." );
		}

		foreach ( var barricade in
			Scene?.GetAllComponents<LargeLadBarricade>() ??
			Enumerable.Empty<LargeLadBarricade>() )
		{
			if ( !barricade.HasVisibleGeometry || !barricade.HasCollision )
			{
				issues.Add(
					$"Barricade '{barricade.GameObject.Name}' needs rendering and collision." );
			}

			if ( barricade.GameObject.NetworkMode != NetworkMode.Object )
			{
				issues.Add(
					$"Barricade '{barricade.GameObject.Name}' must use Network Mode Object." );
			}
		}

		foreach ( var killVolume in
			Scene?.GetAllComponents<LargeLadKillVolume>() ??
			Enumerable.Empty<LargeLadKillVolume>() )
		{
			if ( killVolume.TriggerCollider is null )
				issues.Add( $"Kill volume '{killVolume.GameObject.Name}' needs a trigger collider." );
		}

		if ( logResults )
		{
			if ( issues.Count == 0 )
			{
				Log.Info(
					$"Scene containing '{GameObject.Name}' " +
					"passes the Large Lad map contract." );
			}
			else
			{
				foreach ( var issue in issues )
					Log.Warning( $"Map contract: {issue}" );
			}
		}

		return issues;
	}

	private void ResolveGameplay()
	{
		NetworkHelper ??= Scene?.GetAllComponents<NetworkHelper>().FirstOrDefault();
		RoundManager ??= Scene?.GetAllComponents<LargeLadRoundManager>().FirstOrDefault();
		SpawnAllocator ??= Scene?.GetAllComponents<LargeLadSpawnAllocator>().FirstOrDefault();
	}

	private void ConfigureGameplay()
	{
		SpawnAllocator?.ConfigureNetworkHelper( NetworkHelper );
		RoundManager?.UseMapDefinition( this );
	}

	private void ValidateSpawnGroup(
		List<string> issues,
		LargeLadSpawnGroup group,
		int requiredCapacity,
		bool validateGeometry )
	{
		if ( SpawnAllocator is null )
			return;

		var spawns = SpawnAllocator.GetTeamSpawns( group );

		if ( spawns.Count == 0 )
		{
			issues.Add( $"{group} needs at least one team spawn." );
			return;
		}

		var capacity = SpawnAllocator.GetConfiguredCapacity( group );
		if ( capacity < requiredCapacity )
		{
			issues.Add(
				$"{group} spawn capacity is {capacity}/{requiredCapacity}." );
		}

		if ( !validateGeometry )
			return;

		var generated = SpawnAllocator.CountGeneratedCandidates( group );
		if ( generated == 0 )
		{
			issues.Add( $"{group} produced no geometrically valid positions." );
		}
		else if ( generated < requiredCapacity )
		{
			issues.Add(
				$"{group} produced {generated}/{requiredCapacity} clear positions." );
		}
	}
}
