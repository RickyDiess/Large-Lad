using Sandbox;

/// <summary>
/// Temporary dodgeball-only bridge. Slot 3 has not migrated yet; all melee
/// and firearm ownership, selection, ammunition, reload, and lifecycle state
/// belong exclusively to <see cref="LargeLadNativeInventory"/>.
/// </summary>
public sealed class LargeLadInventory : Component
{
	private const float PickupFeedbackDuration = 2.75f;

	[Sync( SyncFlags.FromHost )]
	public LargeLadUtilityState UtilityState { get; private set; }

	private LargeLadDodgeballPickup utilitySource;
	private int nextOwnerUtilityThrowRequestSequence;
	private int lastHostUtilityThrowRequestSequence;
	private string pickupFeedback;
	private bool hasPickupFeedback;
	private TimeSince timeSincePickupFeedback;

	public bool HasUtility =>
		LargeLadUtilityRules.IsValidState( UtilityState );
	public bool IsUtilityEquipped =>
		HasUtility &&
		Components.Get<LargeLadNativeInventory>()?.ActiveItem is null;
	public LargeLadInventorySelection ActiveUtilitySelection =>
		IsUtilityEquipped
			? LargeLadUtilityRules.SelectionFor( UtilityState )
			: LargeLadInventorySelection.None;
	public LargeLadUtilityId EquippedUtility =>
		IsUtilityEquipped
			? UtilityState.Utility
			: LargeLadUtilityId.None;
	public bool HasPickupFeedback =>
		hasPickupFeedback &&
		timeSincePickupFeedback < PickupFeedbackDuration;
	public string PickupFeedback =>
		HasPickupFeedback ? pickupFeedback : null;

	protected override void OnUpdate()
	{
		if ( IsProxy || !IsUtilityEquipped ||
			Components.Get<LargeLadPlayer>()?.IsEatBusy == true ||
			!Input.Pressed( "Attack1" ) )
		{
			return;
		}

		Components.Get<LargeLadPlayer>()?
			.WeaponPresentation?
			.TriggerPredictedUtilityUse();
		nextOwnerUtilityThrowRequestSequence++;
		RequestThrowSelectedUtility(
			nextOwnerUtilityThrowRequestSequence );
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

		ReleaseUtilityForLifecycle(
			GameObject.WorldPosition,
			preferForward: false );
		ClearUtilityState();
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

		var nativeInventory = Components.Get<LargeLadNativeInventory>();
		if ( nativeInventory?.ActiveItem is null or
			LargeLadMeleeWeapon )
		{
			TrySelectUtility();
		}

		return true;
	}

	internal bool TrySelectUtility()
	{
		var player = Components.Get<LargeLadPlayer>();

		if ( !LargeLadUtilityRules.CanSelect(
			isHost: Networking.IsHost,
			ownerRequest: true,
			player?.Role ?? LargeLadRole.Unassigned,
			player?.Health?.IsDead != false,
			UtilityState ) ||
			player?.IsEatBusy == true )
		{
			return false;
		}

		player.NativeInventory?.HolsterForUtility();
		return true;
	}

	internal void RequestSelectUtilityFromOwner()
	{
		Components.Get<LargeLadNativeInventory>()?.HolsterForUtility();
		RequestSelectUtility();
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	private void RequestSelectUtility()
	{
		TrySelectUtility();
	}

	internal void RequestDropSelectedUtilityFromOwner()
	{
		RequestDropSelectedUtility();
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	private void RequestDropSelectedUtility()
	{
		if ( TryDropSelectedUtility() )
			return;

		ReceivePickupFeedback(
			"No safe place to drop the dodgeball." );
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
				ActiveUtilitySelection ) ||
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

		ClearUtilityAndSelectMelee();
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
				ActiveUtilitySelection ) ||
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
		ClearUtilityAndSelectMelee();
		return true;
	}

	internal void NotifyUtilitySlotFull()
	{
		if ( Networking.IsHost )
		{
			ReceivePickupFeedback(
				"You can only carry one utility item." );
		}
	}

	public void HandleDeath( Vector3 dropPosition )
	{
		if ( !Networking.IsHost )
			return;

		ReleaseUtilityForLifecycle(
			dropPosition,
			preferForward: false );
		ClearUtilityState();
	}

	public void HandleDisconnect()
	{
		if ( !Networking.IsHost )
			return;

		ReleaseUtilityForLifecycle(
			GameObject.WorldPosition,
			preferForward: false );
		ClearUtilityState();
	}

	public void ClearForRoundReset()
	{
		if ( !Networking.IsHost )
			return;

		ResolveUtilitySource()?.ReleaseCarrierForRoundReset( this );
		ClearUtilityState();
	}

	internal void HandleMapTransition( Scene departingScene )
	{
		if ( !Networking.IsHost )
			return;

		if ( HasUtility )
		{
			ResolveUtilitySource( departingScene )?.ReturnCarrierToOrigin(
				this,
				UtilityState );
		}

		ClearUtilityState();
	}

	internal void HandleUtilitySourceDestroyed(
		LargeLadDodgeballPickup source )
	{
		if ( !Networking.IsHost || source != utilitySource )
			return;

		ClearUtilityAndSelectMelee();
	}

	[Rpc.Owner( NetFlags.HostOnly )]
	private void ReceivePickupFeedback( string message )
	{
		pickupFeedback = message;
		hasPickupFeedback = !string.IsNullOrWhiteSpace( message );
		timeSincePickupFeedback = 0.0f;
	}

	private void ClearUtilityAndSelectMelee()
	{
		ClearUtilityState();
		Components.Get<LargeLadNativeInventory>()?.SelectMelee();
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
			ClearUtilityState();
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

		ClearUtilityState();
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

	private void ClearUtilityState()
	{
		UtilityState = default;
		utilitySource = null;
	}
}
