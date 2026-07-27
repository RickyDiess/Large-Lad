using Sandbox;
using System.Collections.Generic;

/// <summary>
/// Host-authoritative Skinny Kid firearm inventory. Core weapons live in a
/// variable-size delta-synchronized collection; the one physical exclusive
/// instance is synchronized separately. Large Lad and Minion melee remains a
/// role ability owned by LargeLadPlayer, not an inventory entry.
/// </summary>
public sealed class LargeLadInventory : Component
{
	private const float PickupFeedbackDuration = 2.75f;

	private static readonly string[] DirectSelectionActions =
	{
		"Slot1",
		"Slot2",
		"Slot3",
		"Slot4",
		"Slot5",
		"Slot6",
		"Slot7",
		"Slot8",
		"Slot9",
		"Slot0"
	};

	[Property, Group( "Starting Loadout" )]
	public List<LargeLadWeaponId> SkinnyKidStartingCoreWeapons { get; set; } =
		new();

	[Sync( SyncFlags.FromHost )]
	public NetList<LargeLadWeaponState> CoreWeapons { get; private set; } =
		new();

	[Sync( SyncFlags.FromHost )]
	public LargeLadWeaponState ExclusiveWeapon { get; private set; }

	[Sync( SyncFlags.FromHost )]
	public LargeLadWeaponSelection ActiveSelection { get; private set; }

	[Sync( SyncFlags.FromHost )]
	public LargeLadWeaponId LastSelectedCoreWeapon { get; private set; }

	[Sync( SyncFlags.FromHost )]
	public bool IsReloading { get; private set; }

	[Sync( SyncFlags.FromHost )]
	public float ReloadEndTime { get; private set; }

	private LargeLadWeaponPickup exclusiveSource;
	private string pickupFeedback;
	private bool hasPickupFeedback;
	private TimeSince timeSincePickupFeedback;

	public int OwnedWeaponCount =>
		CoreWeapons.Count + (HasExclusiveWeapon ? 1 : 0);
	public int WeaponSelectionCount => OwnedWeaponCount + 1;
	public bool HasExclusiveWeapon =>
		LargeLadInventoryRules.IsValidExclusiveState( ExclusiveWeapon );
	public bool IsExclusiveEquipped =>
		ActiveSelection.Kind == LargeLadWeaponSelectionKind.Exclusive &&
		TryGetActiveState( out var state ) &&
		LargeLadInventoryRules.IsValidExclusiveState( state );
	public LargeLadWeaponId EquippedWeapon =>
		ActiveSelection.Kind ==
			LargeLadWeaponSelectionKind.RoleAbility
			? LargeLadWeaponId.Melee
			: TryGetActiveState( out var state )
			? state.Weapon
			: LargeLadWeaponId.None;
	public LargeLadWeaponDefinition EquippedDefinition =>
		LargeLadWeaponCatalog.Get( EquippedWeapon );
	public int EquippedMagazine =>
		TryGetActiveState( out var state ) ? state.Magazine : 0;
	public int EquippedReserve =>
		TryGetActiveState( out var state ) ? state.Reserve : 0;
	public LargeLadAmmunitionMode EquippedAmmunitionMode =>
		TryGetActiveState( out var state )
			? state.AmmunitionMode
			: LargeLadAmmunitionMode.FiniteReserve;
	public bool EquippedHasInfiniteReserve =>
		TryGetActiveState( out var state ) && state.HasInfiniteReserve;
	public float ReloadTimeRemaining =>
		IsReloading
			? LargeLadGameplayRules.GetTimerTimeRemaining(
				ReloadEndTime,
				Time.Now )
			: 0.0f;
	public bool HasPickupFeedback =>
		hasPickupFeedback &&
		timeSincePickupFeedback < PickupFeedbackDuration;
	public string PickupFeedback => HasPickupFeedback ? pickupFeedback : null;

	protected override void OnUpdate()
	{
		if ( Networking.IsHost )
			TickReload();

		if ( IsProxy )
			return;

		for ( var index = 0;
			index < DirectSelectionActions.Length;
			index++ )
		{
			if ( Input.Pressed( DirectSelectionActions[index] ) )
				RequestSelectWeaponIndex( index );
		}

		if ( Input.MouseWheel.y > 0.0f ||
			Input.Pressed( "SlotPrev" ) )
		{
			RequestCycleWeapon( -1 );
		}
		else if ( Input.MouseWheel.y < 0.0f ||
			Input.Pressed( "SlotNext" ) )
		{
			RequestCycleWeapon( 1 );
		}

		if ( Input.Pressed( "Reload" ) )
			RequestReload();

		if ( Input.Pressed( "DropWeapon" ) )
			RequestDropExclusive();
	}

	protected override void OnValidate()
	{
		var seen = new HashSet<LargeLadWeaponId>();

		foreach ( var weapon in
			SkinnyKidStartingCoreWeapons ??
			new List<LargeLadWeaponId>() )
		{
			if ( !LargeLadWeaponCatalog.IsFirearm( weapon ) )
			{
				Log.Warning(
					$"{GameObject.Name}: Skinny Kid starting loadout contains " +
					$"invalid firearm '{weapon}'." );
			}
			else if ( !seen.Add( weapon ) )
			{
				Log.Warning(
					$"{GameObject.Name}: Skinny Kid starting loadout contains " +
					$"duplicate core firearm '{weapon}'." );
			}
		}
	}

	protected override void OnDestroy()
	{
		if ( Networking.IsHost )
			HandleDisconnect();

		base.OnDestroy();
	}

	internal void PrepareForRole( LargeLadRole role )
	{
		if ( !Networking.IsHost )
			return;

		ReleaseExclusiveForLifecycle(
			GameObject.WorldPosition,
			preferForward: false );
		ClearInventoryState();

		if ( role != LargeLadRole.SkinnyKid )
			return;

		SelectRoleMelee();

		foreach ( var weapon in
			SkinnyKidStartingCoreWeapons ??
			new List<LargeLadWeaponId>() )
		{
			TryGrantCoreWeapon( weapon );
		}
	}

	public bool TryGrantCoreWeapon( LargeLadWeaponId weapon )
	{
		var player = Components.Get<LargeLadPlayer>();

		if ( !LargeLadInventoryRules.CanCollectCore(
			isHost: Networking.IsHost,
			player?.Role ?? LargeLadRole.Unassigned,
			player?.Health?.IsDead != false,
			CoreWeapons,
			weapon ) )
			return false;

		if ( !LargeLadInventoryRules.TryAddCoreWeapon(
			CoreWeapons,
			weapon ) )
		{
			return false;
		}

		if ( ActiveSelection.Kind is
			LargeLadWeaponSelectionKind.None or
			LargeLadWeaponSelectionKind.RoleAbility )
		{
			SelectCore( weapon );
		}

		return true;
	}

	internal bool CanAcceptExclusive(
		LargeLadWeaponPickup source,
		LargeLadWeaponState state,
		bool pickupAvailable )
	{
		var player = Components.Get<LargeLadPlayer>();

		return Networking.IsHost &&
			source is not null &&
			source.IsValid &&
			LargeLadInventoryRules.CanAcceptExclusive(
				player?.Role ?? LargeLadRole.Unassigned,
				player?.Health?.IsDead != false,
				HasExclusiveWeapon,
				pickupAvailable,
				state );
	}

	internal bool TryGrantExclusiveWeapon(
		LargeLadWeaponPickup source,
		LargeLadWeaponState state )
	{
		if ( !CanAcceptExclusive(
			source,
			state,
			pickupAvailable: true ) )
		{
			return false;
		}

		ExclusiveWeapon = state;
		exclusiveSource = source;

		if ( ActiveSelection.Kind is
			LargeLadWeaponSelectionKind.None or
			LargeLadWeaponSelectionKind.RoleAbility )
		{
			SelectState( state );
		}

		return true;
	}

	public bool TryGetWeaponAt(
		int index,
		out LargeLadWeaponState state )
	{
		return LargeLadInventoryRules.TryGetWeaponAt(
			CoreWeapons,
			ExclusiveWeapon,
			index,
			out state );
	}

	public bool TryGetActiveState( out LargeLadWeaponState state )
	{
		var index = LargeLadInventoryRules.GetSelectionIndex(
			CoreWeapons,
			ExclusiveWeapon,
			ActiveSelection );
		return TryGetWeaponAt( index, out state );
	}

	public bool TryConsumeShot(
		out LargeLadWeaponDefinition definition )
	{
		definition = EquippedDefinition;

		if ( !CanHostMutateLivingSkinnyKid() ||
			IsReloading ||
			!TryGetActiveState( out var state ) ||
			!LargeLadInventoryRules.TryConsumeShot( ref state ) )
		{
			return false;
		}

		SetOwnedState( state );
		return true;
	}

	public bool BeginReload()
	{
		var player = Components.Get<LargeLadPlayer>();

		if ( !TryGetActiveState( out var state ) ||
			!LargeLadInventoryRules.CanReload(
				isHost: Networking.IsHost,
				ownerRequest: true,
				player?.Role ?? LargeLadRole.Unassigned,
				player?.Health?.IsDead != false,
				IsReloading,
				state ) )
		{
			return false;
		}

		IsReloading = true;
		ReloadEndTime = LargeLadGameplayRules.GetTimerDeadline(
			Time.Now,
			LargeLadWeaponCatalog.Get( state.Weapon ).ReloadDuration );
		return true;
	}

	public void HandleDeath( Vector3 dropPosition )
	{
		if ( !Networking.IsHost )
			return;

		ReleaseExclusiveForLifecycle(
			dropPosition,
			preferForward: false );
		ClearInventoryState();
	}

	public void HandleDisconnect()
	{
		if ( !Networking.IsHost )
			return;

		ReleaseExclusiveForLifecycle(
			GameObject.WorldPosition,
			preferForward: false );
		ClearInventoryState();
	}

	public void ClearForRoundReset()
	{
		if ( !Networking.IsHost )
			return;

		ResolveExclusiveSource()?.ReleaseCarrierForRoundReset( this );
		ClearInventoryState();
	}

	internal void NotifyExclusiveSlotFull()
	{
		if ( Networking.IsHost )
		{
			ReceivePickupFeedback(
				"You can only carry one exclusive weapon." );
		}
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	private void RequestSelectWeaponIndex( int index )
	{
		TrySelectWeaponIndex( index );
	}

	internal bool TrySelectWeaponIndex( int index )
	{
		var player = Components.Get<LargeLadPlayer>();

		if ( !LargeLadInventoryRules.CanProcessOwnerRequest(
				isHost: Networking.IsHost,
				ownerRequest: true,
				player?.Role ?? LargeLadRole.Unassigned,
				player?.Health?.IsDead != false ) )
		{
			return false;
		}

		if ( index == 0 )
		{
			CancelReload();
			SelectRoleMelee();
			return true;
		}

		if ( !TryGetWeaponAt( index - 1, out var state ) ||
			!LargeLadInventoryRules.CanSelect(
				isHost: true,
				ownerRequest: true,
				player.Role,
				isDead: false,
				state ) )
		{
			return false;
		}

		CancelReload();
		SelectState( state );
		return true;
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	private void RequestCycleWeapon( int direction )
	{
		if ( !Networking.IsHost ||
			!CanHostMutateLivingSkinnyKid() ||
			direction == 0 )
		{
			return;
		}

		var firearmIndex = LargeLadInventoryRules.GetSelectionIndex(
				CoreWeapons,
				ExclusiveWeapon,
				ActiveSelection );
		var current =
			ActiveSelection.Kind ==
				LargeLadWeaponSelectionKind.RoleAbility
				? 0
				: firearmIndex >= 0
					? firearmIndex + 1
					: -1;
		var candidate = LargeLadInventoryRules.GetCycledIndex(
			current,
			WeaponSelectionCount,
			direction );

		TrySelectWeaponIndex( candidate );
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	private void RequestReload()
	{
		BeginReload();
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	private void RequestDropExclusive()
	{
		if ( !Networking.IsHost ||
			!CanHostMutateLivingSkinnyKid() ||
			!IsExclusiveEquipped )
		{
			return;
		}

		if ( !TryDropSelectedExclusive() )
		{
			ReceivePickupFeedback(
				"No safe place to drop the exclusive weapon." );
		}
	}

	internal bool TryDropSelectedExclusive()
	{
		var player = Components.Get<LargeLadPlayer>();

		if ( !Networking.IsHost ||
			!LargeLadInventoryRules.CanDropExclusive(
				isHost: true,
				ownerRequest: true,
				player?.Role ?? LargeLadRole.Unassigned,
				player?.Health?.IsDead != false,
				ExclusiveWeapon,
				ActiveSelection ) ||
			ResolveExclusiveSource() is not
				LargeLadWeaponPickup source )
		{
			return false;
		}

		var controller = Components.Get<PlayerController>();
		var forward = controller?.EyeTransform.Rotation.Forward ??
			GameObject.WorldRotation.Forward;

		if ( !source.TryDropFromCarrier(
			this,
			ExclusiveWeapon,
			GameObject.WorldPosition,
			forward,
			out _ ) )
		{
			return false;
		}

		ClearExclusiveAndSelectFallback();
		return true;
	}

	[Rpc.Owner( NetFlags.HostOnly )]
	private void ReceivePickupFeedback( string message )
	{
		pickupFeedback = message;
		hasPickupFeedback = !string.IsNullOrWhiteSpace( message );
		timeSincePickupFeedback = 0.0f;
	}

	private bool CanHostMutateLivingSkinnyKid()
	{
		var player = Components.Get<LargeLadPlayer>();
		return Networking.IsHost &&
			LargeLadInventoryRules.CanUseFirearmInventory(
				player?.Role ?? LargeLadRole.Unassigned,
				player?.Health?.IsDead != false );
	}

	private void TickReload()
	{
		if ( !IsReloading ||
			!LargeLadGameplayRules.HasTimerReachedDeadline(
				ReloadEndTime,
				Time.Now ) )
		{
			return;
		}

		if ( !TryGetActiveState( out var state ) )
		{
			CancelReload();
			return;
		}

		SetOwnedState(
			LargeLadInventoryRules.CompleteReload( state ) );
		CancelReload();
	}

	private bool SetOwnedState( LargeLadWeaponState state )
	{
		if ( LargeLadInventoryRules.IsValidExclusiveState( state ) )
		{
			if ( !HasExclusiveWeapon ||
				ExclusiveWeapon.ExclusiveInstanceId !=
					state.ExclusiveInstanceId )
			{
				return false;
			}

			ExclusiveWeapon = state;
			return true;
		}

		if ( !LargeLadInventoryRules.IsValidCoreState( state ) )
			return false;

		var index = LargeLadInventoryRules.FindCoreWeapon(
			CoreWeapons,
			state.Weapon );

		if ( index < 0 )
			return false;

		CoreWeapons[index] = state;
		return true;
	}

	private void SelectState( LargeLadWeaponState state )
	{
		ActiveSelection = LargeLadInventoryRules.SelectionFor( state );

		if ( ActiveSelection.Kind == LargeLadWeaponSelectionKind.Core )
			LastSelectedCoreWeapon = state.Weapon;
	}

	private void SelectRoleMelee()
	{
		ActiveSelection =
			LargeLadWeaponSelection.ForRoleMelee();
	}

	private void SelectCore( LargeLadWeaponId weapon )
	{
		var index = LargeLadInventoryRules.FindCoreWeapon(
			CoreWeapons,
			weapon );

		if ( index >= 0 )
			SelectState( CoreWeapons[index] );
	}

	private void ClearExclusiveAndSelectFallback()
	{
		CancelReload();
		ExclusiveWeapon = default;
		exclusiveSource = null;
		ActiveSelection = LargeLadInventoryRules.GetCoreFallback(
			CoreWeapons,
			LastSelectedCoreWeapon );

		if ( ActiveSelection.Kind == LargeLadWeaponSelectionKind.Core )
			LastSelectedCoreWeapon = ActiveSelection.Weapon;
	}

	private void ReleaseExclusiveForLifecycle(
		Vector3 dropPosition,
		bool preferForward )
	{
		if ( !HasExclusiveWeapon )
			return;

		var source = ResolveExclusiveSource();

		if ( source is null || !source.IsValid )
		{
			// A missing source is invalid map/runtime state. Clear the local
			// reservation rather than leaving a disconnected owner armed.
			ExclusiveWeapon = default;
			exclusiveSource = null;
			return;
		}

		var controller = Components.Get<PlayerController>();
		var forward = preferForward
			? controller?.EyeTransform.Rotation.Forward ??
				GameObject.WorldRotation.Forward
			: GameObject.WorldRotation.Forward;

		if ( source.TryDropFromCarrier(
			this,
			ExclusiveWeapon,
			dropPosition,
			forward,
			out _ ) )
		{
			ExclusiveWeapon = default;
			exclusiveSource = null;
			return;
		}

		// Death/disconnect must never strand the instance. Safe placement
		// failure returns it to the authored origin with current ammunition.
		source.ReturnCarrierToOrigin(
			this,
			ExclusiveWeapon );
		ExclusiveWeapon = default;
		exclusiveSource = null;
	}

	private LargeLadWeaponPickup ResolveExclusiveSource()
	{
		if ( !HasExclusiveWeapon )
			return null;

		if ( exclusiveSource is not null &&
			exclusiveSource.IsValid &&
			exclusiveSource.ExclusiveInstanceId ==
				ExclusiveWeapon.ExclusiveInstanceId )
		{
			return exclusiveSource;
		}

		foreach ( var pickup in
			Scene?.GetAllComponents<LargeLadWeaponPickup>() ??
			System.Array.Empty<LargeLadWeaponPickup>() )
		{
			if ( pickup.PickupPolicy !=
					LargeLadPickupPolicy.Exclusive ||
				pickup.Weapon != ExclusiveWeapon.Weapon ||
				pickup.ExclusiveInstanceId !=
					ExclusiveWeapon.ExclusiveInstanceId )
			{
				continue;
			}

			exclusiveSource = pickup;
			return pickup;
		}

		return null;
	}

	private void ClearInventoryState()
	{
		CoreWeapons.Clear();
		ExclusiveWeapon = default;
		exclusiveSource = null;
		ActiveSelection = LargeLadWeaponSelection.None;
		LastSelectedCoreWeapon = LargeLadWeaponId.None;
		CancelReload();
	}

	private void CancelReload()
	{
		IsReloading = false;
		ReloadEndTime = 0.0f;
	}
}
