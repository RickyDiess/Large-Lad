using Sandbox;

public sealed class LargeLadInventory : Component
{
	public const int SlotCount = 4;

	[Sync( SyncFlags.FromHost )]
	public int EquippedSlot { get; private set; }

	[Sync( SyncFlags.FromHost )] public LargeLadWeaponId Slot1Weapon { get; private set; }
	[Sync( SyncFlags.FromHost )] public LargeLadWeaponId Slot2Weapon { get; private set; }
	[Sync( SyncFlags.FromHost )] public LargeLadWeaponId Slot3Weapon { get; private set; }
	[Sync( SyncFlags.FromHost )] public LargeLadWeaponId Slot4Weapon { get; private set; }

	[Sync( SyncFlags.FromHost )] public int Slot1Magazine { get; private set; }
	[Sync( SyncFlags.FromHost )] public int Slot2Magazine { get; private set; }
	[Sync( SyncFlags.FromHost )] public int Slot3Magazine { get; private set; }
	[Sync( SyncFlags.FromHost )] public int Slot4Magazine { get; private set; }

	[Sync( SyncFlags.FromHost )] public int Slot1Reserve { get; private set; }
	[Sync( SyncFlags.FromHost )] public int Slot2Reserve { get; private set; }
	[Sync( SyncFlags.FromHost )] public int Slot3Reserve { get; private set; }
	[Sync( SyncFlags.FromHost )] public int Slot4Reserve { get; private set; }

	[Sync( SyncFlags.FromHost )]
	public bool IsReloading { get; private set; }

	[Sync( SyncFlags.FromHost )]
	public float ReloadTimeRemaining { get; private set; }

	private readonly LargeLadWeaponPickup[] exclusiveSources =
		new LargeLadWeaponPickup[SlotCount];

	public LargeLadWeaponId EquippedWeapon => GetWeapon( EquippedSlot );
	public LargeLadWeaponDefinition EquippedDefinition =>
		LargeLadWeaponCatalog.Get( EquippedWeapon );
	public int EquippedMagazine => GetMagazine( EquippedSlot );
	public int EquippedReserve => GetReserve( EquippedSlot );

	protected override void OnUpdate()
	{
		if ( Networking.IsHost )
		{
			TickReload();
		}

		if ( IsProxy )
			return;

		if ( Input.Pressed( "Slot1" ) ) RequestEquipSlot( 1 );
		if ( Input.Pressed( "Slot2" ) ) RequestEquipSlot( 2 );
		if ( Input.Pressed( "Slot3" ) ) RequestEquipSlot( 3 );
		if ( Input.Pressed( "Slot4" ) ) RequestEquipSlot( 4 );

		if ( Input.MouseWheel.y > 0.0f )
		{
			RequestCycleSlot( -1 );
		}
		else if ( Input.MouseWheel.y < 0.0f )
		{
			RequestCycleSlot( 1 );
		}

		if ( Input.Pressed( "Reload" ) )
		{
			RequestReload();
		}
	}

	protected override void OnDestroy()
	{
		// A disconnected owner must not strand a globally exclusive weapon
		// in an unavailable state until the next round.
		if ( Networking.IsHost )
		{
			ReleaseExclusiveWeapons( GameObject.WorldPosition );
		}
	}

	internal void PrepareForRole( LargeLadRole role )
	{
		if ( !Networking.IsHost )
			return;

		ClearInventory( dropExclusive: false );

		if ( role is LargeLadRole.SkinnyKid or
			LargeLadRole.LargeLad or LargeLadRole.Minion )
		{
			SetSlot( 1, LargeLadWeaponId.Melee, 0, 0, null );
			EquippedSlot = 1;
		}
	}

	public bool TryGrantWeapon(
		LargeLadWeaponId weapon,
		LargeLadWeaponPickup exclusiveSource = null,
		int startingMagazine = -1,
		int startingReserve = -1 )
	{
		if ( !Networking.IsHost || !LargeLadWeaponCatalog.IsFirearm( weapon ) )
			return false;

		var player = Components.Get<LargeLadPlayer>();

		if ( player?.Role != LargeLadRole.SkinnyKid || player.Health?.IsDead == true )
			return false;

		var slot = LargeLadGameplayRules.FindWeaponGrantSlot(
			weapon,
			Slot1Weapon,
			Slot2Weapon,
			Slot3Weapon,
			Slot4Weapon );

		if ( slot <= 0 )
			return false;

		var definition = LargeLadWeaponCatalog.Get( weapon );
		SetSlot(
			slot,
			weapon,
			startingMagazine >= 0 ? startingMagazine : definition.MagazineSize,
			startingReserve >= 0 ? startingReserve : definition.StartingReserve,
			exclusiveSource );

		// Skinny Kids always keep melee in slot 1. Preserve the old pickup
		// behavior by equipping their first firearm automatically.
		if ( EquippedSlot == 0 ||
			!LargeLadWeaponCatalog.IsFirearm( EquippedWeapon ) )
		{
			EquippedSlot = slot;
		}

		return true;
	}

	public bool TryAddAmmo( LargeLadWeaponId weapon, int amount )
	{
		if ( !Networking.IsHost || amount <= 0 )
			return false;

		var slot = FindWeaponSlot( weapon );

		if ( slot <= 0 )
			return false;

		SetReserve( slot, GetReserve( slot ) + amount );
		return true;
	}

	public bool TryConsumeShot( out LargeLadWeaponDefinition definition )
	{
		definition = EquippedDefinition;

		if ( !Networking.IsHost || !definition.UsesAmmo ||
			IsReloading || EquippedMagazine <= 0 )
		{
			return false;
		}

		SetMagazine( EquippedSlot, EquippedMagazine - 1 );
		return true;
	}

	public void HandleDeath( Vector3 dropPosition )
	{
		if ( !Networking.IsHost )
			return;

		ReleaseExclusiveWeapons( dropPosition );
		ClearInventory( dropExclusive: false );
	}

	private void ReleaseExclusiveWeapons( Vector3 dropPosition )
	{
		for ( var slot = 1; slot <= SlotCount; slot++ )
		{
			var source = exclusiveSources[slot - 1];

			if ( source is null || !source.IsValid )
				continue;

			source.ReleaseExclusive(
				dropPosition,
				GetMagazine( slot ),
				GetReserve( slot ) );
		}
	}

	public void ClearForRoundReset()
	{
		if ( Networking.IsHost )
		{
			ClearInventory( dropExclusive: false );
		}
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	private void RequestEquipSlot( int slot )
	{
		if ( !Networking.IsHost || slot < 1 || slot > SlotCount ||
			GetWeapon( slot ) == LargeLadWeaponId.None )
		{
			return;
		}

		CancelReload();
		EquippedSlot = slot;
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	private void RequestCycleSlot( int direction )
	{
		if ( !Networking.IsHost || direction == 0 )
			return;

		var start = EquippedSlot <= 0 ? 1 : EquippedSlot;

		for ( var offset = 1; offset <= SlotCount; offset++ )
		{
			var candidate = (start - 1 + direction * offset) % SlotCount;

			if ( candidate < 0 )
				candidate += SlotCount;

			candidate++;

			if ( GetWeapon( candidate ) == LargeLadWeaponId.None )
				continue;

			CancelReload();
			EquippedSlot = candidate;
			return;
		}
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	private void RequestReload()
	{
		BeginReload();
	}

	public bool BeginReload()
	{
		if ( !Networking.IsHost || IsReloading )
			return false;

		var definition = EquippedDefinition;

		if ( !definition.UsesAmmo || EquippedMagazine >= definition.MagazineSize ||
			EquippedReserve <= 0 )
		{
			return false;
		}

		IsReloading = true;
		ReloadTimeRemaining = definition.ReloadDuration;
		return true;
	}

	private void TickReload()
	{
		if ( !IsReloading )
			return;

		ReloadTimeRemaining = System.MathF.Max(
			0.0f,
			ReloadTimeRemaining - Time.Delta );

		if ( ReloadTimeRemaining > 0.0f )
			return;

		var definition = EquippedDefinition;
		var needed = System.Math.Max( 0, definition.MagazineSize - EquippedMagazine );
		var loaded = System.Math.Min( needed, EquippedReserve );

		SetMagazine( EquippedSlot, EquippedMagazine + loaded );
		SetReserve( EquippedSlot, EquippedReserve - loaded );
		CancelReload();
	}

	private void CancelReload()
	{
		IsReloading = false;
		ReloadTimeRemaining = 0.0f;
	}

	private void ClearInventory( bool dropExclusive )
	{
		if ( dropExclusive )
		{
			HandleDeath( GameObject.WorldPosition );
			return;
		}

		for ( var slot = 1; slot <= SlotCount; slot++ )
		{
			SetSlot( slot, LargeLadWeaponId.None, 0, 0, null );
		}

		EquippedSlot = 0;
		CancelReload();
	}

	private int FindWeaponSlot( LargeLadWeaponId weapon )
	{
		for ( var slot = 1; slot <= SlotCount; slot++ )
		{
			if ( GetWeapon( slot ) == weapon )
				return slot;
		}

		return 0;
	}

	private void SetSlot(
		int slot,
		LargeLadWeaponId weapon,
		int magazine,
		int reserve,
		LargeLadWeaponPickup exclusiveSource )
	{
		switch ( slot )
		{
			case 1: Slot1Weapon = weapon; break;
			case 2: Slot2Weapon = weapon; break;
			case 3: Slot3Weapon = weapon; break;
			case 4: Slot4Weapon = weapon; break;
		}

		SetMagazine( slot, magazine );
		SetReserve( slot, reserve );

		if ( slot >= 1 && slot <= SlotCount )
		{
			exclusiveSources[slot - 1] = exclusiveSource;
		}
	}

	private LargeLadWeaponId GetWeapon( int slot )
	{
		return slot switch
		{
			1 => Slot1Weapon,
			2 => Slot2Weapon,
			3 => Slot3Weapon,
			4 => Slot4Weapon,
			_ => LargeLadWeaponId.None
		};
	}

	private int GetMagazine( int slot )
	{
		return slot switch
		{
			1 => Slot1Magazine,
			2 => Slot2Magazine,
			3 => Slot3Magazine,
			4 => Slot4Magazine,
			_ => 0
		};
	}

	private int GetReserve( int slot )
	{
		return slot switch
		{
			1 => Slot1Reserve,
			2 => Slot2Reserve,
			3 => Slot3Reserve,
			4 => Slot4Reserve,
			_ => 0
		};
	}

	private void SetMagazine( int slot, int amount )
	{
		amount = System.Math.Max( 0, amount );

		switch ( slot )
		{
			case 1: Slot1Magazine = amount; break;
			case 2: Slot2Magazine = amount; break;
			case 3: Slot3Magazine = amount; break;
			case 4: Slot4Magazine = amount; break;
		}
	}

	private void SetReserve( int slot, int amount )
	{
		amount = System.Math.Max( 0, amount );

		switch ( slot )
		{
			case 1: Slot1Reserve = amount; break;
			case 2: Slot2Reserve = amount; break;
			case 3: Slot3Reserve = amount; break;
			case 4: Slot4Reserve = amount; break;
		}
	}
}
