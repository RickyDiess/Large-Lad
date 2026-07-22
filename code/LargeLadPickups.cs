using Sandbox;
using System.Collections.Generic;

public sealed class LargeLadWeaponPickup : Component,
	Component.ITriggerListener,
	ILargeLadRoundResettable
{
	[Property]
	public LargeLadWeaponId Weapon { get; set; } = LargeLadWeaponId.Pistol;

	[Property]
	public LargeLadPickupPolicy Policy { get; set; } =
		LargeLadPickupPolicy.CorePerPlayer;

	[Property]
	public Renderer PickupRenderer { get; set; }

	[Property]
	public Collider PickupCollider { get; set; }

	[Sync( SyncFlags.FromHost ), Change( nameof( OnAvailableChanged ) )]
	public bool Available { get; private set; } = true;

	private readonly HashSet<GameObject> collectedPlayers = new();
	private Transform authoredTransform;
	private bool hasAuthoredTransform;
	private int droppedMagazine = -1;
	private int droppedReserve = -1;

	protected override void OnStart()
	{
		authoredTransform = GameObject.WorldTransform;
		hasAuthoredTransform = true;

		if ( PickupCollider is not null )
		{
			PickupCollider.IsTrigger = true;
		}

		ApplyAvailableState();
	}

	protected override void OnValidate()
	{
		if ( !LargeLadWeaponCatalog.IsFirearm( Weapon ) )
		{
			Log.Warning( $"{GameObject.Name}: weapon pickup must use a firearm definition." );
		}
	}

	public void OnTriggerEnter( Collider other )
	{
		if ( !Networking.IsHost || !Available )
			return;

		var player = other?.GameObject?.Components.Get<LargeLadPlayer>(
			FindMode.EverythingInSelfAndAncestors );

		if ( player?.Role != LargeLadRole.SkinnyKid || player.Health?.IsDead == true )
			return;

		if ( Policy == LargeLadPickupPolicy.CorePerPlayer &&
			collectedPlayers.Contains( player.GameObject ) )
		{
			return;
		}

		var exclusiveSource = Policy == LargeLadPickupPolicy.GloballyExclusive
			? this
			: null;

		if ( player.Inventory?.TryGrantWeapon(
			Weapon,
			exclusiveSource,
			droppedMagazine,
			droppedReserve ) != true )
		{
			return;
		}

		if ( Policy == LargeLadPickupPolicy.CorePerPlayer )
		{
			collectedPlayers.Add( player.GameObject );
		}
		else
		{
			SetAvailable( false );
		}

		droppedMagazine = -1;
		droppedReserve = -1;
		Log.Info( $"{player.GameObject.Name} collected {LargeLadWeaponCatalog.Get( Weapon ).DisplayName}." );
	}

	public void OnTriggerExit( Collider other )
	{
	}

	public void ReleaseExclusive(
		Vector3 worldPosition,
		int remainingMagazine,
		int remainingReserve )
	{
		if ( !Networking.IsHost || Policy != LargeLadPickupPolicy.GloballyExclusive )
			return;

		droppedMagazine = System.Math.Max( 0, remainingMagazine );
		droppedReserve = System.Math.Max( 0, remainingReserve );
		GameObject.WorldPosition = worldPosition + Vector3.Up * 18.0f;
		SetAvailable( true );
	}

	public void ResetForRound()
	{
		if ( !Networking.IsHost )
			return;

		collectedPlayers.Clear();
		droppedMagazine = -1;
		droppedReserve = -1;

		if ( hasAuthoredTransform )
		{
			GameObject.WorldTransform = authoredTransform;
			GameObject.Network.ClearInterpolation();
		}

		SetAvailable( true );
		Log.Info(
			$"Reset {LargeLadWeaponCatalog.Get( Weapon ).DisplayName} pickup " +
			$"({Policy}) for the new round." );
	}

	private void SetAvailable( bool available )
	{
		Available = available;
		ApplyAvailableState();
	}

	private void OnAvailableChanged( bool oldValue, bool newValue )
	{
		ApplyAvailableState();
	}

	private void ApplyAvailableState()
	{
		if ( PickupRenderer is not null )
		{
			PickupRenderer.Enabled = Available;
		}

		if ( PickupCollider is not null )
		{
			PickupCollider.Enabled = Available;
		}
	}

	protected override void DrawGizmos()
	{
		Gizmo.Draw.Color = LargeLadWeaponCatalog.Get( Weapon ).PickupColor.WithAlpha( 0.45f );
		Gizmo.Draw.SolidBox( new BBox( new Vector3( -12.0f ), new Vector3( 12.0f ) ) );
	}
}

public sealed class LargeLadAmmoPickup : Component,
	Component.ITriggerListener,
	ILargeLadRoundResettable
{
	[Property]
	public LargeLadWeaponId Weapon { get; set; } = LargeLadWeaponId.Pistol;

	[Property, Title( "Ammo Amount (0 = Two Magazines)" )]
	public int AmmoAmount { get; set; }

	[Property]
	public Collider PickupCollider { get; set; }

	private readonly HashSet<GameObject> collectedPlayers = new();

	public int ResolvedAmmoAmount
	{
		get
		{
			var definition = LargeLadWeaponCatalog.Get( Weapon );
			return AmmoAmount > 0 ? AmmoAmount : definition.MagazineSize * 2;
		}
	}

	protected override void OnStart()
	{
		if ( PickupCollider is not null )
		{
			PickupCollider.IsTrigger = true;
		}
	}

	protected override void OnValidate()
	{
		if ( !LargeLadWeaponCatalog.IsFirearm( Weapon ) )
		{
			Log.Warning( $"{GameObject.Name}: ammo pickup must target a firearm." );
		}
	}

	public void OnTriggerEnter( Collider other )
	{
		if ( !Networking.IsHost )
			return;

		var player = other?.GameObject?.Components.Get<LargeLadPlayer>(
			FindMode.EverythingInSelfAndAncestors );

		if ( player?.Role != LargeLadRole.SkinnyKid || player.Health?.IsDead == true ||
			collectedPlayers.Contains( player.GameObject ) )
		{
			return;
		}

		if ( player.Inventory?.TryAddAmmo( Weapon, ResolvedAmmoAmount ) != true )
			return;

		collectedPlayers.Add( player.GameObject );
		Log.Info( $"{player.GameObject.Name} collected {ResolvedAmmoAmount} {LargeLadWeaponCatalog.Get( Weapon ).DisplayName} rounds." );
	}

	public void OnTriggerExit( Collider other )
	{
	}

	public void ResetForRound()
	{
		if ( Networking.IsHost )
		{
			collectedPlayers.Clear();
			Log.Info(
				$"Reset {LargeLadWeaponCatalog.Get( Weapon ).DisplayName} ammo pickup " +
				"for the new round." );
		}
	}

	protected override void DrawGizmos()
	{
		Gizmo.Draw.Color = LargeLadWeaponCatalog.Get( Weapon ).PickupColor.WithAlpha( 0.3f );
		Gizmo.Draw.SolidBox(
			new BBox( new Vector3( -9.0f, -9.0f, -5.0f ), new Vector3( 9.0f, 9.0f, 5.0f ) ) );
	}
}
