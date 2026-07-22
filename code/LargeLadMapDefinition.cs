using Sandbox;
using System.Collections.Generic;
using System.Linq;

public enum LargeLadSpawnGroup
{
	Lobby,
	SkinnyKid,
	Hunter
}

public sealed class LargeLadSpawnMarker : Component
{
	[Property]
	public LargeLadSpawnGroup Group { get; set; }

	[Property]
	public int Order { get; set; }

	public Color MarkerColor => Group switch
	{
		LargeLadSpawnGroup.Lobby => Color.White,
		LargeLadSpawnGroup.SkinnyKid => new Color( 0.25f, 0.85f, 1.0f ),
		LargeLadSpawnGroup.Hunter => new Color( 1.0f, 0.22f, 0.08f ),
		_ => Color.Gray
	};

	protected override void DrawGizmos()
	{
		Gizmo.Draw.Color = MarkerColor.WithAlpha( 0.35f );
		Gizmo.Draw.SolidCapsule(
			Vector3.Up * 16.0f,
			Vector3.Up * 56.0f,
			16.0f,
			8,
			4 );
		Gizmo.Draw.Color = MarkerColor;
		Gizmo.Draw.Arrow( Vector3.Up * 54.0f, Vector3.Forward * 38.0f );
	}
}

/// <summary>
/// The authored contract for a Large Lad map. Gameplay code discovers this
/// component, so duplicating a conforming scene never requires code changes.
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

	public IReadOnlyList<LargeLadSpawnMarker> LobbySpawns =>
		GetOrderedSpawns( LargeLadSpawnGroup.Lobby );

	public IReadOnlyList<LargeLadSpawnMarker> SkinnyKidSpawns =>
		GetOrderedSpawns( LargeLadSpawnGroup.SkinnyKid );

	public IReadOnlyList<LargeLadSpawnMarker> HunterSpawns =>
		GetOrderedSpawns( LargeLadSpawnGroup.Hunter );

	protected override void OnAwake()
	{
		ResolveManagers();
		ConfigureGameplay();
	}

	protected override void OnStart()
	{
		ValidateMap( logResults: true );
	}

	protected override void OnValidate()
	{
		ResolveManagers();
		ConfigureGameplay();
		ValidateMap( logResults: true );
	}

	public GameObject GetSpawn( LargeLadSpawnGroup group, int orderedIndex )
	{
		var spawns = GetOrderedSpawns( group );

		if ( spawns.Count == 0 )
			return null;

		var index = System.Math.Abs( orderedIndex ) % spawns.Count;
		return spawns[index].GameObject;
	}

	public IReadOnlyList<string> ValidateMap( bool logResults )
	{
		var issues = new List<string>();
		ResolveManagers();

		if ( NetworkHelper is null )
			issues.Add( "Missing NetworkHelper." );

		if ( RoundManager is null )
			issues.Add( "Missing LargeLadRoundManager." );

		ValidateSpawnGroup( issues, LargeLadSpawnGroup.Lobby, TargetPlayerCount );
		ValidateSpawnGroup( issues, LargeLadSpawnGroup.SkinnyKid, TargetPlayerCount - 1 );
		ValidateSpawnGroup( issues, LargeLadSpawnGroup.Hunter, TargetPlayerCount );

		foreach ( var pickup in Scene.GetAllComponents<LargeLadWeaponPickup>() )
		{
			if ( !LargeLadWeaponCatalog.IsFirearm( pickup.Weapon ) )
				issues.Add( $"Weapon pickup '{pickup.GameObject.Name}' has no valid firearm." );

			if ( pickup.PickupCollider is null )
				issues.Add( $"Weapon pickup '{pickup.GameObject.Name}' is missing its trigger collider reference." );
		}

		foreach ( var pickup in Scene.GetAllComponents<LargeLadAmmoPickup>() )
		{
			if ( !LargeLadWeaponCatalog.IsFirearm( pickup.Weapon ) )
				issues.Add( $"Ammo pickup '{pickup.GameObject.Name}' has no valid firearm." );

			if ( pickup.PickupCollider is null )
				issues.Add( $"Ammo pickup '{pickup.GameObject.Name}' is missing its trigger collider reference." );
		}

		foreach ( var barricade in Scene.GetAllComponents<LargeLadBarricade>() )
		{
			if ( barricade.GameObject.Parent is null )
				issues.Add( $"Barricade '{barricade.GameObject.Name}' must be parented beneath its geometry." );

			if ( barricade.BarricadeCollider is null || barricade.BarricadeRenderer is null )
				issues.Add( $"Barricade '{barricade.GameObject.Name}' parent needs visible geometry and collision." );
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
					Log.Warning( $"Map contract: {issue}" );
			}
		}

		return issues;
	}

	private void ResolveManagers()
	{
		NetworkHelper ??= Scene?.GetAllComponents<NetworkHelper>().FirstOrDefault();
		RoundManager ??= Scene?.GetAllComponents<LargeLadRoundManager>().FirstOrDefault();
	}

	private void ConfigureGameplay()
	{
		if ( NetworkHelper is not null )
		{
			NetworkHelper.SpawnPoints.Clear();
			NetworkHelper.SpawnPoints.AddRange( LobbySpawns.Select( marker => marker.GameObject ) );
		}

		RoundManager?.UseMapDefinition( this );
	}

	private IReadOnlyList<LargeLadSpawnMarker> GetOrderedSpawns(
		LargeLadSpawnGroup group )
	{
		if ( Scene is null )
			return new List<LargeLadSpawnMarker>();

		return Scene
			.GetAllComponents<LargeLadSpawnMarker>()
			.Where( marker => marker.Group == group )
			.OrderBy( marker => marker.Order )
			.ThenBy( marker => marker.GameObject.Name )
			.ToList();
	}

	private void ValidateSpawnGroup(
		List<string> issues,
		LargeLadSpawnGroup group,
		int requiredCount )
	{
		var markers = GetOrderedSpawns( group );

		if ( markers.Count < requiredCount )
		{
			issues.Add( $"{group} has {markers.Count}/{requiredCount} required spawn markers." );
		}

		foreach ( var duplicate in markers.GroupBy( marker => marker.Order ).Where( set => set.Count() > 1 ) )
		{
			issues.Add( $"{group} spawn order {duplicate.Key} is duplicated." );
		}
	}
}
