using Sandbox;
using System.Collections.Generic;

/// <summary>
/// Host-authoritative Skinny Kid inventory. Core firearms live in a variable-
/// size delta-synchronized collection; the physical exclusive firearm and the
/// one dodgeball utility slot are synchronized separately. Role melee remains
/// an ability rather than an owned item.
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
	public LargeLadUtilityState UtilityState { get; private set; }

	[Sync( SyncFlags.FromHost )]
	public LargeLadInventorySelection ActiveSelection { get; private set; }

	[Sync( SyncFlags.FromHost )]
	public LargeLadWeaponId LastSelectedCoreWeapon { get; private set; }

	[Sync( SyncFlags.FromHost )]
	public bool IsReloading { get; private set; }

	[Sync( SyncFlags.FromHost )]
	public float ReloadEndTime { get; private set; }

	private LargeLadWeaponPickup exclusiveSource;
	private LargeLadDodgeballPickup utilitySource;
	private int nextOwnerUtilityThrowRequestSequence;
	private int lastHostUtilityThrowRequestSequence;
	private string pickupFeedback;
	private bool hasPickupFeedback;
	private TimeSince timeSincePickupFeedback;

	public int OwnedFirearmCount =>
		CoreWeapons.Count + (HasExclusiveWeapon ? 1 : 0);
	public int InventorySelectionCount =>
		LargeLadInventoryRules.GetInventorySelectionCount(
			CoreWeapons,
			ExclusiveWeapon,
			UtilityState );
	public bool HasExclusiveWeapon =>
		LargeLadInventoryRules.IsValidExclusiveState( ExclusiveWeapon );
	public bool HasUtility =>
		LargeLadUtilityRules.IsValidState( UtilityState );
	public bool IsExclusiveEquipped =>
		ActiveSelection.Kind ==
			LargeLadInventorySelectionKind.ExclusiveFirearm &&
		TryGetActiveFirearmState( out var state ) &&
		LargeLadInventoryRules.IsValidExclusiveState( state );
	public bool IsUtilityEquipped =>
		ActiveSelection.Kind == LargeLadInventorySelectionKind.Utility &&
		HasUtility &&
		ActiveSelection ==
			LargeLadUtilityRules.SelectionFor( UtilityState );
	public LargeLadUtilityId EquippedUtility =>
		IsUtilityEquipped
			? UtilityState.Utility
			: LargeLadUtilityId.None;
	public LargeLadWeaponId EquippedWeapon =>
		ActiveSelection.Kind ==
			LargeLadInventorySelectionKind.RoleAbility
			? LargeLadWeaponId.Melee
			: TryGetActiveFirearmState( out var state )
			? state.Weapon
			: LargeLadWeaponId.None;
	public LargeLadWeaponDefinition EquippedDefinition =>
		LargeLadWeaponCatalog.Get( EquippedWeapon );
	public int EquippedMagazine =>
		TryGetActiveFirearmState( out var state ) ? state.Magazine : 0;
	public int EquippedReserve =>
		TryGetActiveFirearmState( out var state ) ? state.Reserve : 0;
	public LargeLadAmmunitionMode EquippedAmmunitionMode =>
		TryGetActiveFirearmState( out var state )
			? state.AmmunitionMode
			: LargeLadAmmunitionMode.FiniteReserve;
	public bool EquippedHasInfiniteReserve =>
		TryGetActiveFirearmState( out var state ) && state.HasInfiniteReserve;
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

		var player = Components.Get<LargeLadPlayer>();

		if ( player?.IsEatBusy == true )
			return;

		// MIGRATION: while the native Pistol exists, this legacy component is
		// the temporary shared input router. It guarantees that only the native
		// active item or the old selection receives weapon input in a frame.
		if ( TryRouteNativePistolInput( player?.NativeInventory ) )
			return;

		for ( var index = 0;
			index < DirectSelectionActions.Length;
			index++ )
		{
			if ( Input.Pressed( DirectSelectionActions[index] ) )
				RequestSelectInventoryIndex( index );
		}

		if ( Input.MouseWheel.y > 0.0f ||
			Input.Pressed( "SlotPrev" ) )
		{
			RequestCycleInventory( -1 );
		}
		else if ( Input.MouseWheel.y < 0.0f ||
			Input.Pressed( "SlotNext" ) )
		{
			RequestCycleInventory( 1 );
		}

		if ( Input.Pressed( "Reload" ) )
			RequestReload();

		if ( Input.Pressed( "DropWeapon" ) )
			RequestDropSelectedItem();

		if ( IsUtilityEquipped && Input.Pressed( "Attack1" ) )
		{
			Components.Get<LargeLadPlayer>()?
				.WeaponPresentation?
				.TriggerPredictedUtilityUse();
			nextOwnerUtilityThrowRequestSequence++;
			RequestThrowSelectedUtility(
				nextOwnerUtilityThrowRequestSequence );
		}
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
		ReleaseUtilityForLifecycle(
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

		// MIGRATION: all Pistol grants now create the real native inventory item.
		// Other Core firearms remain on the legacy state list in this pass.
		if ( weapon == LargeLadWeaponId.Pistol &&
			player?.NativeInventory is LargeLadNativeInventory nativeInventory )
		{
			return nativeInventory.TryGrantNativePistol();
		}

		if ( !LargeLadInventoryRules.CanCollectCore(
			isHost: Networking.IsHost,
			player?.Role ?? LargeLadRole.Unassigned,
			player?.Health?.IsDead != false,
			CoreWeapons,
			weapon ) ||
			player?.IsEatBusy == true )
			return false;

		if ( !LargeLadInventoryRules.TryAddCoreWeapon(
			CoreWeapons,
			weapon ) )
		{
			return false;
		}

		if ( ActiveSelection.Kind is
			LargeLadInventorySelectionKind.None or
			LargeLadInventorySelectionKind.RoleAbility )
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
			player?.IsEatBusy != true &&
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
			LargeLadInventorySelectionKind.None or
			LargeLadInventorySelectionKind.RoleAbility )
		{
			SelectState( state );
		}

		return true;
	}

	internal bool CanAcceptUtility(
		LargeLadDodgeballPickup source,
		LargeLadUtilityState state,
		bool pickupAvailable )
	{
		var player = Components.Get<LargeLadPlayer>();

		return Networking.IsHost &&
			player?.IsEatBusy != true &&
			source is not null &&
			source.IsValid &&
			LargeLadUtilityRules.CanAccept(
				player?.Role ?? LargeLadRole.Unassigned,
				player?.Health?.IsDead != false,
				HasUtility,
				pickupAvailable,
				state );
	}

	internal bool TryGrantUtility(
		LargeLadDodgeballPickup source,
		LargeLadUtilityState state )
	{
		if ( !CanAcceptUtility(
			source,
			state,
			pickupAvailable: true ) )
		{
			return false;
		}

		UtilityState = state;
		utilitySource = source;

		if ( ActiveSelection.Kind is
			LargeLadInventorySelectionKind.None or
			LargeLadInventorySelectionKind.RoleAbility )
		{
			SelectUtility();
		}

		return true;
	}

	public bool TryGetFirearmAt(
		int index,
		out LargeLadWeaponState state )
	{
		return LargeLadInventoryRules.TryGetFirearmAt(
			CoreWeapons,
			ExclusiveWeapon,
			index,
			out state );
	}

	public bool TryGetInventorySelectionAt(
		int index,
		out LargeLadInventorySelection selection )
	{
		return LargeLadInventoryRules.TryGetInventorySelectionAt(
			CoreWeapons,
			ExclusiveWeapon,
			UtilityState,
			index,
			out selection );
	}

	public bool TryGetFirearmForSelection(
		LargeLadInventorySelection selection,
		out LargeLadWeaponState state )
	{
		return LargeLadInventoryRules.TryGetFirearmForSelection(
			CoreWeapons,
			ExclusiveWeapon,
			selection,
			out state );
	}

	public bool TryGetActiveFirearmState(
		out LargeLadWeaponState state )
	{
		return TryGetFirearmForSelection( ActiveSelection, out state );
	}

	public bool TryConsumeShot(
		out LargeLadWeaponDefinition definition )
	{
		definition = EquippedDefinition;

		if ( !CanHostMutateLivingSkinnyKid() ||
			IsReloading ||
			!TryGetActiveFirearmState( out var state ) ||
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

		if ( !TryGetActiveFirearmState( out var state ) ||
			player?.IsEatBusy == true ||
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

	internal void CancelConflictingActionForEat()
	{
		if ( Networking.IsHost )
			CancelReload();
	}

	public void HandleDeath( Vector3 dropPosition )
	{
		if ( !Networking.IsHost )
			return;

		ReleaseExclusiveForLifecycle(
			dropPosition,
			preferForward: false );
		ReleaseUtilityForLifecycle(
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
		ReleaseUtilityForLifecycle(
			GameObject.WorldPosition,
			preferForward: false );
		ClearInventoryState();
	}

	public void ClearForRoundReset()
	{
		if ( !Networking.IsHost )
			return;

		ResolveExclusiveSource()?.ReleaseCarrierForRoundReset( this );
		ResolveUtilitySource()?.ReleaseCarrierForRoundReset( this );
		ClearInventoryState();
	}

	internal void HandleMapTransition( Scene departingScene )
	{
		if ( !Networking.IsHost )
			return;

		if ( HasExclusiveWeapon )
		{
			ResolveExclusiveSource( departingScene )?.ReturnCarrierToOrigin(
				this,
				ExclusiveWeapon );
		}

		if ( HasUtility )
		{
			ResolveUtilitySource( departingScene )?.ReturnCarrierToOrigin(
				this,
				UtilityState );
		}

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

	internal void NotifyUtilitySlotFull()
	{
		if ( Networking.IsHost )
		{
			ReceivePickupFeedback(
				"You can only carry one utility item." );
		}
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	private void RequestSelectInventoryIndex( int index )
	{
		TrySelectInventoryIndex( index );
	}

	internal bool TrySelectInventoryIndex( int index )
	{
		var player = Components.Get<LargeLadPlayer>();

		if ( !LargeLadInventoryRules.CanProcessOwnerRequest(
				isHost: Networking.IsHost,
				ownerRequest: true,
				player?.Role ?? LargeLadRole.Unassigned,
				player?.Health?.IsDead != false ) ||
			player?.IsEatBusy == true )
		{
			return false;
		}

		if ( !TryGetInventorySelectionAt( index, out var selection ) )
			return false;

		if ( selection.Kind ==
			LargeLadInventorySelectionKind.RoleAbility )
		{
			CancelReload();
			SelectRoleMelee();
			return true;
		}

		if ( selection.Kind ==
			LargeLadInventorySelectionKind.Utility )
		{
			if ( !LargeLadUtilityRules.CanSelect(
				isHost: true,
				ownerRequest: true,
				player.Role,
				isDead: false,
				UtilityState ) )
			{
				return false;
			}

			CancelReload();
			SelectUtility();
			return true;
		}

		if ( !TryGetFirearmForSelection( selection, out var state ) ||
			!LargeLadInventoryRules.CanSelectFirearm(
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
	private void RequestCycleInventory( int direction )
	{
		if ( !Networking.IsHost ||
			!CanHostMutateLivingSkinnyKid() ||
			direction == 0 )
		{
			return;
		}

		var current = LargeLadInventoryRules.GetInventorySelectionIndex(
			CoreWeapons,
			ExclusiveWeapon,
			UtilityState,
			ActiveSelection );
		var candidate = LargeLadInventoryRules.GetCycledIndex(
			current,
			InventorySelectionCount,
			direction );

		TrySelectInventoryIndex( candidate );
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	private void RequestReload()
	{
		BeginReload();
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	private void RequestDropSelectedItem()
	{
		if ( !Networking.IsHost ||
			!CanHostMutateLivingSkinnyKid() )
		{
			return;
		}

		if ( IsExclusiveEquipped )
		{
			if ( !TryDropSelectedExclusive() )
			{
				ReceivePickupFeedback(
					"No safe place to drop the exclusive weapon." );
			}

			return;
		}

		if ( IsUtilityEquipped && !TryDropSelectedUtility() )
		{
			ReceivePickupFeedback(
				"No safe place to drop the dodgeball." );
		}
	}

	internal bool TryDropSelectedExclusive()
	{
		var player = Components.Get<LargeLadPlayer>();

		if ( !Networking.IsHost ||
			player?.IsEatBusy == true ||
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

	internal bool TryDropSelectedUtility()
	{
		var player = Components.Get<LargeLadPlayer>();

		if ( !Networking.IsHost ||
			player?.IsEatBusy == true ||
			!LargeLadUtilityRules.CanDrop(
				isHost: true,
				ownerRequest: true,
				player?.Role ?? LargeLadRole.Unassigned,
				player?.Health?.IsDead != false,
				UtilityState,
				ActiveSelection ) ||
			ResolveUtilitySource() is not
				LargeLadDodgeballPickup source )
		{
			return false;
		}

		var controller = Components.Get<PlayerController>();
		var forward = controller?.EyeTransform.Rotation.Forward ??
			GameObject.WorldRotation.Forward;

		if ( !source.TryDropFromCarrier(
			this,
			UtilityState,
			GameObject.WorldPosition,
			forward ) )
		{
			return false;
		}

		ClearUtilityAndSelectFallback();
		return true;
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	private void RequestThrowSelectedUtility( int ownerRequestSequence )
	{
		if ( !Networking.IsHost ||
			ownerRequestSequence <= lastHostUtilityThrowRequestSequence )
		{
			return;
		}

		// Consume the request before state validation. Replaying it after a
		// pickup, death, reset, or transfer can never throw the same ball again.
		lastHostUtilityThrowRequestSequence = ownerRequestSequence;
		TryThrowSelectedUtility();
	}

	internal bool TryThrowSelectedUtility()
	{
		var player = Components.Get<LargeLadPlayer>();

		if ( !Networking.IsHost ||
			player?.IsEatBusy == true ||
			!LargeLadUtilityRules.CanDrop(
				isHost: true,
				ownerRequest: true,
				player?.Role ?? LargeLadRole.Unassigned,
				player?.Health?.IsDead != false,
				UtilityState,
				ActiveSelection ) ||
			ResolveUtilitySource() is not
				LargeLadDodgeballPickup source )
		{
			return false;
		}

		var controller = Components.Get<PlayerController>();

		if ( controller is null )
			return false;

		var eye = controller.EyeTransform;
		var inheritedVelocity = controller.Body?.Velocity ?? Vector3.Zero;

		if ( !source.TryThrowFromCarrier(
			this,
			UtilityState,
			eye.Position,
			eye.Rotation.Forward,
			inheritedVelocity ) )
		{
			return false;
		}

		player.WeaponPresentation?.BroadcastUtilityUse();
		ClearUtilityAndSelectFallback();
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
			player?.IsEatBusy != true &&
			LargeLadInventoryRules.CanUseInventory(
				player?.Role ?? LargeLadRole.Unassigned,
				player?.Health?.IsDead != false );
	}

	private bool TryRouteNativePistolInput(
		LargeLadNativeInventory nativeInventory )
	{
		if ( nativeInventory?.HasNativePistol != true )
			return false;

		for ( var index = 0;
			index < DirectSelectionActions.Length;
			index++ )
		{
			if ( !Input.Pressed( DirectSelectionActions[index] ) )
				continue;

			if ( index == LargeLadNativeWeaponRules.MeleeSlot )
			{
				nativeInventory.HolsterNativeWeapon();
				RequestSelectInventoryIndex( index );
			}
			else if ( index == LargeLadNativeWeaponRules.CoreFirearmSlot )
			{
				nativeInventory.SelectNativePistol();
			}
			else
			{
				nativeInventory.HolsterNativeWeapon();
				RequestSelectInventoryIndex( index );
			}

			return true;
		}

		if ( Input.MouseWheel.y != 0.0f ||
			Input.Pressed( "SlotPrev" ) ||
			Input.Pressed( "SlotNext" ) )
		{
			if ( nativeInventory.HasNativeInputControl )
			{
				nativeInventory.HolsterNativeWeapon();
				RequestSelectInventoryIndex(
					LargeLadNativeWeaponRules.MeleeSlot );
			}
			else
			{
				nativeInventory.SelectNativePistol();
			}

			return true;
		}

		if ( !nativeInventory.HasNativeInputControl )
			return false;

		nativeInventory.Pump();
		return true;
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

		if ( !TryGetActiveFirearmState( out var state ) )
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
		ActiveSelection =
			LargeLadInventoryRules.FirearmSelectionFor( state );

		if ( ActiveSelection.Kind ==
			LargeLadInventorySelectionKind.CoreFirearm )
			LastSelectedCoreWeapon = state.Weapon;
	}

	private void SelectRoleMelee()
	{
		ActiveSelection =
			LargeLadInventorySelection.ForRoleMelee();
	}

	private void SelectUtility()
	{
		ActiveSelection =
			LargeLadUtilityRules.SelectionFor( UtilityState );
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
		ActiveSelection = LargeLadInventoryRules.GetFirearmFallback(
			CoreWeapons,
			LastSelectedCoreWeapon );

		if ( ActiveSelection.Kind ==
			LargeLadInventorySelectionKind.CoreFirearm )
			LastSelectedCoreWeapon = ActiveSelection.Weapon;
	}

	private void ClearUtilityAndSelectFallback()
	{
		CancelReload();
		UtilityState = default;
		utilitySource = null;
		ActiveSelection =
			LargeLadInventoryRules.GetUtilityRemovalFallback(
				CoreWeapons,
				ExclusiveWeapon,
				LastSelectedCoreWeapon );

		if ( ActiveSelection.Kind ==
			LargeLadInventorySelectionKind.CoreFirearm )
		{
			LastSelectedCoreWeapon = ActiveSelection.Weapon;
		}
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

	private LargeLadWeaponPickup ResolveExclusiveSource(
		Scene sourceScene = null )
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
			(sourceScene ?? Scene)?
				.GetAllComponents<LargeLadWeaponPickup>() ??
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

	private void ReleaseUtilityForLifecycle(
		Vector3 dropPosition,
		bool preferForward )
	{
		if ( !HasUtility )
			return;

		var source = ResolveUtilitySource();

		if ( source is null || !source.IsValid )
		{
			UtilityState = default;
			utilitySource = null;
			return;
		}

		var controller = Components.Get<PlayerController>();
		var forward = preferForward
			? controller?.EyeTransform.Rotation.Forward ??
				GameObject.WorldRotation.Forward
			: GameObject.WorldRotation.Forward;

		if ( !source.TryDropFromCarrier(
			this,
			UtilityState,
			dropPosition,
			forward ) )
		{
			source.ReturnCarrierToOrigin( this, UtilityState );
		}

		UtilityState = default;
		utilitySource = null;
	}

	private LargeLadDodgeballPickup ResolveUtilitySource(
		Scene sourceScene = null )
	{
		if ( !HasUtility )
			return null;

		if ( utilitySource is not null &&
			utilitySource.IsValid &&
			utilitySource.UtilityInstanceId == UtilityState.InstanceId )
		{
			return utilitySource;
		}

		foreach ( var pickup in
			(sourceScene ?? Scene)?
				.GetAllComponents<LargeLadDodgeballPickup>() ??
			System.Array.Empty<LargeLadDodgeballPickup>() )
		{
			if ( pickup.UtilityInstanceId != UtilityState.InstanceId )
				continue;

			utilitySource = pickup;
			return pickup;
		}

		return null;
	}

	internal void HandleUtilitySourceDestroyed(
		LargeLadDodgeballPickup source )
	{
		if ( !Networking.IsHost || source != utilitySource )
			return;

		ClearUtilityAndSelectFallback();
	}

	private void ClearInventoryState()
	{
		CoreWeapons.Clear();
		ExclusiveWeapon = default;
		exclusiveSource = null;
		UtilityState = default;
		utilitySource = null;
		ActiveSelection = LargeLadInventorySelection.None;
		LastSelectedCoreWeapon = LargeLadWeaponId.None;
		CancelReload();
	}

	private void CancelReload()
	{
		IsReloading = false;
		ReloadEndTime = 0.0f;
	}
}
