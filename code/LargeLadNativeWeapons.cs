using Sandbox;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// Deterministic Large Lad policy around the native inventory and shot-claim
/// framework. Engine-owned attack, ammo, reload, aim, claim, and presentation
/// behavior stays in <see cref="BaseCombatWeapon"/>.
/// </summary>
public static class LargeLadNativeWeaponRules
{
	public const int MeleeSlot = 0;
	public const int CoreFirearmSlot = 1;
	public const int ExclusiveFirearmSlot = 2;
	public const int UtilitySlot = 3;
	public const int SlotCount = 4;

	public const float ClaimCadenceTolerance = 0.05f;
	public const float ClaimOriginTolerance =
		LargeLadAimResolver.MaximumViewOriginOffset + 32.0f;
	public const float ClaimRangeTolerance = 16.0f;
	public const float ClaimValueTolerance = 0.01f;
	public const float MinimumClaimDirectionAlignment = 0.98f;

	public static bool CanAddCoreFirearm(
		LargeLadWeaponId weapon,
		IEnumerable<LargeLadWeaponId> ownedCoreFirearms )
	{
		return LargeLadWeaponCatalog.IsFirearm( weapon ) &&
			!(ownedCoreFirearms ?? Enumerable.Empty<LargeLadWeaponId>())
				.Contains( weapon );
	}

	public static bool CanAddExclusiveFirearm( int ownedExclusiveCount )
	{
		return ownedExclusiveCount == 0;
	}

	public static int GetCycledIndex(
		int currentIndex,
		int selectionCount,
		int direction )
	{
		if ( selectionCount <= 0 || direction == 0 )
			return -1;

		if ( currentIndex < 0 || currentIndex >= selectionCount )
			return direction > 0 ? 0 : selectionCount - 1;

		var candidate = (currentIndex + direction) % selectionCount;
		return candidate < 0 ? candidate + selectionCount : candidate;
	}

	public static bool CanOwnSkinnyKidItem(
		LargeLadRole role,
		bool isLiving,
		bool isEatBusy )
	{
		return role == LargeLadRole.SkinnyKid &&
			isLiving &&
			!isEatBusy;
	}

	public static bool CanDropExclusiveFirearm(
		LargeLadRole role,
		bool isLiving,
		bool isEatBusy,
		bool isExclusive,
		bool isActive )
	{
		return CanOwnSkinnyKidItem( role, isLiving, isEatBusy ) &&
			isExclusive &&
			isActive;
	}

	public static bool CanUseFirearm(
		LargeLadRole role,
		bool isLiving,
		LargeLadRoundPhase phase,
		bool isEatBusy,
		bool isMovementLocked,
		bool isGroundSlamBusy,
		bool isGroundSlamStaggered,
		bool isHeld,
		bool isActive )
	{
		return role == LargeLadRole.SkinnyKid &&
			isLiving &&
			phase == LargeLadRoundPhase.Playing &&
			!isEatBusy &&
			!isMovementLocked &&
			!isGroundSlamBusy &&
			!isGroundSlamStaggered &&
			isHeld &&
			isActive;
	}

	public static bool IsValidClaimEnvelope(
		int sequence,
		int lastSequence,
		float damage,
		float expectedDamage,
		float force,
		float expectedForce,
		int claimedPellets,
		int maximumPellets )
	{
		return sequence > lastSequence &&
			float.IsFinite( damage ) &&
			float.IsFinite( expectedDamage ) &&
			System.MathF.Abs( damage - expectedDamage ) <=
				ClaimValueTolerance &&
			float.IsFinite( force ) &&
			float.IsFinite( expectedForce ) &&
			System.MathF.Abs( force - expectedForce ) <=
				ClaimValueTolerance &&
			claimedPellets > 0 &&
			maximumPellets > 0 &&
			claimedPellets <= maximumPellets;
	}

	public static bool IsPlausibleClaimCadence(
		bool hasSchedule,
		float now,
		float nextAllowedTime )
	{
		return !hasSchedule ||
			(float.IsFinite( now ) &&
			float.IsFinite( nextAllowedTime ) &&
			now + ClaimCadenceTolerance >= nextAllowedTime);
	}

	public static bool IsPlausiblePellet(
		Vector3 ownerEyePosition,
		Vector3 origin,
		Vector3 position,
		Vector3 direction,
		float range )
	{
		if ( !LargeLadAimResolver.IsFinite( ownerEyePosition ) ||
			!LargeLadAimResolver.IsFinite( origin ) ||
			!LargeLadAimResolver.IsFinite( position ) ||
			!LargeLadAimResolver.IsFinite( direction ) ||
			!float.IsFinite( range ) ||
			range <= 0.0f )
		{
			return false;
		}

		var eyeOffset = origin - ownerEyePosition;
		if ( eyeOffset.LengthSquared >
			ClaimOriginTolerance * ClaimOriginTolerance )
		{
			return false;
		}

		var travel = position - origin;
		var maximumDistance = range + ClaimRangeTolerance;
		if ( travel.LengthSquared <= 0.001f ||
			travel.LengthSquared > maximumDistance * maximumDistance ||
			direction.LengthSquared <= 0.001f )
		{
			return false;
		}

		return Vector3.Dot( travel.Normal, direction.Normal ) >=
			MinimumClaimDirectionAlignment;
	}

	public static bool IsValidPlayerTarget(
		LargeLadRole attackerRole,
		LargeLadRole victimRole,
		bool victimIsLiving )
	{
		return attackerRole == LargeLadRole.SkinnyKid &&
			victimIsLiving &&
			victimRole is LargeLadRole.LargeLad or LargeLadRole.Minion;
	}

	public static LargeLadHitRegion ClassifyNativeDamage(
		bool damageHasHeadTag,
		bool hitboxHasHeadTag,
		string hitboxBoneName )
	{
		return LargeLadFirearmHitRules.ClassifyHitRegion(
			hitboxBoneName,
			damageHasHeadTag || hitboxHasHeadTag );
	}
}

/// <summary>
/// Large Lad's native item container. Buckets are the real item store; this
/// component does not mirror weapon state into a second synchronized list.
/// </summary>
public sealed class LargeLadNativeInventory : BaseInventoryComponent
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

	// The Facepunch world crowbar's raw origin is below the physical handle.
	// native_crowbar_worldmodel moves only its renderer child 1,0,-8: one local
	// unit centers the shaft on the model's off-center origin and -8 lands the
	// native hold attachment several inches up the grip. It then rolls that child
	// 180 degrees around the local shaft axis so the authored hook faces forward
	// rather than back toward the holder's head. There is no player-bone or
	// per-frame correction.
	public const string NativeCrowbarPrefabPath =
		"prefabs/gameplay/native_crowbar.prefab";
	// The Facepunch MP5 world model's authored root sits below and behind its
	// physical pistol grip. native_smg_worldmodel applies the verified 3,0,-7
	// renderer-child correction so BaseCombatWeapon's native hold_R attachment
	// lands at the top of the grip. The installed generic w_smg alternative has
	// no muzzle attachment, so it cannot preserve BaseWeaponModel presentation.
	public const string NativeDodgeballItemPrefabPath =
		"prefabs/gameplay/native_dodgeball_item.prefab";

	private bool ownerWantsNativeControl;
	private bool ownerWantsNativeCombatPresentation;
	private TimeSince timeSinceNativeSelectionRequest;
	private LargeLadWeaponPickup exclusiveSource;
	private LargeLadDodgeballPickup utilitySource;
	private int nextOwnerUtilityThrowRequestSequence;
	private int lastHostUtilityThrowRequestSequence;
	private string pickupFeedback;
	private bool hasPickupFeedback;
	private TimeSince timeSincePickupFeedback;

	[Property, Group( "Starting Loadout" )]
	public List<LargeLadWeaponId> SkinnyKidStartingCoreWeapons { get; set; } =
		new();

	public LargeLadFirearm ActiveFirearm => ActiveItem as LargeLadFirearm;
	public LargeLadMeleeWeapon ActiveMelee =>
		ActiveItem as LargeLadMeleeWeapon;
	public LargeLadDodgeballItem ActiveUtility =>
		ActiveItem as LargeLadDodgeballItem;
	public bool HasActiveNativeWeapon =>
		ActiveItem is LargeLadFirearm or LargeLadMeleeWeapon;
	public bool HasActiveNativeItem =>
		ActiveItem is LargeLadFirearm or LargeLadMeleeWeapon or
			LargeLadDodgeballItem;
	public bool HasSkinnyKidMelee =>
		GetSlotItems( LargeLadNativeWeaponRules.MeleeSlot )
			.OfType<LargeLadMeleeWeapon>()
			.Any();
	public bool HasNativePistol => HasCoreFirearm( LargeLadWeaponId.Pistol );
	public bool HasNativeSmg => HasCoreFirearm( LargeLadWeaponId.Smg );
	public bool HasExclusiveFirearm =>
		GetSlotItems( LargeLadNativeWeaponRules.ExclusiveFirearmSlot )
			.OfType<LargeLadFirearm>()
			.Any();
	public LargeLadDodgeballItem UtilityItem =>
		GetSlotItems( LargeLadNativeWeaponRules.UtilitySlot )
			.OfType<LargeLadDodgeballItem>()
			.FirstOrDefault();
	public LargeLadUtilityState UtilityState =>
		UtilityItem?.ToUtilityState() ?? default;
	public bool HasUtility =>
		LargeLadUtilityRules.IsValidState( UtilityState );
	public bool IsUtilityEquipped => ActiveUtility is not null;
	public LargeLadUtilityId EquippedUtility =>
		ActiveUtility?.UtilityId ?? LargeLadUtilityId.None;
	public int NativeItemCount => Items.Count();
	public bool HasPickupFeedback =>
		hasPickupFeedback &&
		timeSincePickupFeedback < PickupFeedbackDuration;
	public string PickupFeedback =>
		HasPickupFeedback ? pickupFeedback : null;

	/// <summary>
	/// Includes the short owner-side handoff before ActiveItem replicates. This
	/// prevents one input frame from also reaching role melee input.
	/// </summary>
	public bool HasNativeInputControl =>
		HasActiveNativeItem || (!IsProxy && ownerWantsNativeControl);

	/// <summary>
	/// Combat items use BaseCombatWeapon presentation. The native dodgeball item
	/// deliberately does not: it keeps the existing utility arms and held-model
	/// presentation while native inventory owns its selection.
	/// </summary>
	public bool HasNativeCombatPresentationControl =>
		HasActiveNativeWeapon ||
		(!IsProxy && ownerWantsNativeCombatPresentation);

	protected override void OnAwake()
	{
		Behaviour = InventoryBehaviour.Buckets;
		MaxSlots = LargeLadNativeWeaponRules.SlotCount;
		PickupMode = PickupBehaviour.None;
		UsesLoadout = false;
		GiveOnStart = false;
		AutoSwitchOnPickup = false;
		AutoSwitchOnEmpty = false;
		ManualPumping = true;
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();

		if ( IsProxy )
			return;

		var player = Components.Get<LargeLadPlayer>();
		if ( player?.IsEatBusy == true )
			return;

		if ( TryRouteInventoryInput() )
			return;

		if ( ownerWantsNativeControl &&
			timeSinceNativeSelectionRequest > 0.5f )
		{
			ownerWantsNativeControl = false;
			ownerWantsNativeCombatPresentation = false;
		}

		if ( HasNativeInputControl )
			Pump();
	}

	protected override void OnDestroy()
	{
		if ( Networking.IsHost )
			HandleDisconnect();

		base.OnDestroy();
	}

	protected override void OnValidate()
	{
		var seen = new HashSet<LargeLadWeaponId>();

		foreach ( var weapon in
			SkinnyKidStartingCoreWeapons ?? new List<LargeLadWeaponId>() )
		{
			if ( !LargeLadWeaponCatalog.IsFirearm( weapon ) )
			{
				Log.Warning(
					$"{GameObject.Name}: Skinny Kid starting loadout contains " +
					$"unsupported Core firearm '{weapon}'." );
			}
			else if ( !seen.Add( weapon ) )
			{
				Log.Warning(
					$"{GameObject.Name}: Skinny Kid starting loadout contains " +
					$"duplicate Core firearm '{weapon}'." );
			}
		}
	}

	protected override bool OnAdding( BaseInventoryItem item, int slot )
	{
		if ( !CanOwnNativeItems() )
			return false;

		if ( item is LargeLadMeleeWeapon )
		{
			return slot == LargeLadNativeWeaponRules.MeleeSlot &&
				!HasSkinnyKidMelee &&
				base.OnAdding( item, slot );
		}

		if ( item is LargeLadDodgeballItem )
		{
			return slot == LargeLadNativeWeaponRules.UtilitySlot &&
				UtilityItem is null &&
				base.OnAdding( item, slot );
		}

		if ( item is not LargeLadFirearm firearm )
			return false;

		if ( firearm.PickupPolicy == LargeLadPickupPolicy.Core )
		{
			return slot == LargeLadNativeWeaponRules.CoreFirearmSlot &&
				LargeLadNativeWeaponRules.CanAddCoreFirearm(
					firearm.WeaponId,
					GetCoreFirearms().Select( weapon => weapon.WeaponId ) ) &&
				base.OnAdding( item, slot );
		}

		if ( firearm.PickupPolicy == LargeLadPickupPolicy.Exclusive )
		{
			return slot == LargeLadNativeWeaponRules.ExclusiveFirearmSlot &&
				LargeLadNativeWeaponRules.CanAddExclusiveFirearm(
					GetExclusiveFirearms().Count() ) &&
				base.OnAdding( item, slot );
		}

		return false;
	}

	protected override bool CanPickupWorldItem( BaseInventoryItem item )
	{
		return CanOwnNativeItems() && base.CanPickupWorldItem( item );
	}

	public bool HasCoreFirearm( LargeLadWeaponId weapon )
	{
		return GetCoreFirearms().Any( firearm =>
			firearm.WeaponId == weapon );
	}

	public LargeLadFirearm GetExclusiveFirearm()
	{
		return GetExclusiveFirearms().FirstOrDefault();
	}

	/// <summary>
	/// Player-facing flattening of the native buckets: melee, every Core
	/// firearm by SlotOrder, the exclusive firearm, then utility.
	/// </summary>
	public IReadOnlyList<BaseInventoryItem> GetOrderedNativeItems()
	{
		return Items
			.Where( item => item.Slot >= LargeLadNativeWeaponRules.MeleeSlot &&
				item.Slot <= LargeLadNativeWeaponRules.UtilitySlot )
			.OrderBy( item => item.Slot )
			.ThenBy( item => item.SlotOrder )
			.ToArray();
	}

	public bool TryGrantCoreFirearm( LargeLadWeaponId weapon )
	{
		if ( !Networking.IsHost || !CanOwnNativeItems() ||
			HasCoreFirearm( weapon ) )
		{
			return false;
		}

		if ( !LargeLadWeaponCatalog.TryGetFirearm(
			weapon,
			out var definition ) ||
			string.IsNullOrWhiteSpace( definition.NativePrefabPath ) )
			return false;

		var item = Pickup(
			definition.NativePrefabPath,
			LargeLadNativeWeaponRules.CoreFirearmSlot ) as LargeLadFirearm;

		if ( item is null )
			return false;

		if ( ActiveItem is null )
			Switch( item );

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

		var item = Pickup(
			NativeDodgeballItemPrefabPath,
			LargeLadNativeWeaponRules.UtilitySlot ) as
			LargeLadDodgeballItem;

		if ( item is null )
			return false;

		item.InitializeState( state );
		utilitySource = source;

		if ( ActiveItem is null or LargeLadMeleeWeapon )
			SelectUtility();

		return true;
	}

	public bool TryGrantNativePistol()
	{
		return TryGrantCoreFirearm( LargeLadWeaponId.Pistol );
	}

	public bool SelectMelee()
	{
		var melee = GetSlotItems( LargeLadNativeWeaponRules.MeleeSlot )
			.OfType<LargeLadMeleeWeapon>()
			.FirstOrDefault();

		if ( melee is null )
			return false;

		BeginOwnerSelectionHandoff( combatPresentation: true );
		Switch( melee );
		return true;
	}

	public bool SelectCoreFirearm( LargeLadWeaponId weapon )
	{
		var firearm = GetCoreFirearms().FirstOrDefault( candidate =>
			candidate.WeaponId == weapon );

		if ( firearm is null )
			return false;

		BeginOwnerSelectionHandoff( combatPresentation: true );
		Switch( firearm );
		return true;
	}

	public bool SelectNextCoreFirearm()
	{
		var firearms = GetCoreFirearms().ToArray();

		if ( firearms.Length == 0 )
			return false;

		var current = System.Array.IndexOf( firearms, ActiveFirearm );
		var next = current < 0 || current + 1 >= firearms.Length
			? 0
			: current + 1;
		return SelectCoreFirearm( firearms[next].WeaponId );
	}

	public bool SelectExclusiveFirearm()
	{
		var firearm = GetExclusiveFirearm();

		if ( firearm is null )
			return false;

		BeginOwnerSelectionHandoff( combatPresentation: true );
		Switch( firearm );
		return true;
	}

	public bool SelectNativeItem( BaseInventoryItem item )
	{
		if ( item is null || !GetOrderedNativeItems().Contains( item ) )
			return false;

		if ( item is LargeLadMeleeWeapon )
			return SelectMelee();

		if ( item is LargeLadDodgeballItem )
			return SelectUtility();

		if ( item is not LargeLadFirearm firearm )
			return false;

		return firearm.IsExclusive
			? SelectExclusiveFirearm()
			: SelectCoreFirearm( firearm.WeaponId );
	}

	public bool SelectNativePistol()
	{
		return SelectCoreFirearm( LargeLadWeaponId.Pistol );
	}

	public bool SelectUtility()
	{
		var utility = UtilityItem;
		var player = Components.Get<LargeLadPlayer>();

		if ( utility is null ||
			player?.IsEatBusy == true ||
			!LargeLadUtilityRules.CanUseUtility(
				player?.Role ?? LargeLadRole.Unassigned,
				player?.Health?.IsDead != false ) )
		{
			return false;
		}

		BeginOwnerSelectionHandoff( combatPresentation: false );
		Switch( utility );
		return true;
	}

	public void HolsterNativeWeapon()
	{
		SelectMelee();
	}

	public bool RemoveExclusiveFirearm()
	{
		if ( !Networking.IsHost || GetExclusiveFirearm() is not
			LargeLadFirearm firearm )
		{
			return false;
		}

		Remove( firearm );
		return true;
	}

	internal void PrepareForRole( LargeLadRole role )
	{
		if ( !Networking.IsHost )
			return;

		ReleaseUtilityForLifecycle(
			GameObject.WorldPosition,
			preferForward: false );
		ReleaseExclusiveForLifecycle(
			GameObject.WorldPosition,
			preferForward: false );
		ClearNativeItems();
		exclusiveSource = null;
		utilitySource = null;
		ResetOwnerSelectionHandoff();

		if ( role != LargeLadRole.SkinnyKid )
			return;

		var melee = Pickup(
			NativeCrowbarPrefabPath,
			LargeLadNativeWeaponRules.MeleeSlot );

		foreach ( var weapon in
			SkinnyKidStartingCoreWeapons ?? new List<LargeLadWeaponId>() )
		{
			TryGrantCoreFirearm( weapon );
		}

		if ( melee is not null )
			Switch( melee );
	}

	internal void HandleDeath( Vector3 dropPosition )
	{
		if ( !Networking.IsHost )
			return;

		ReleaseUtilityForLifecycle(
			dropPosition,
			preferForward: false );
		ReleaseExclusiveForLifecycle(
			dropPosition,
			preferForward: false );
		ClearNativeItems();
		exclusiveSource = null;
		utilitySource = null;
		ResetOwnerSelectionHandoff();
	}

	internal void HandleDisconnect()
	{
		if ( !Networking.IsHost )
			return;

		ReleaseUtilityForLifecycle(
			GameObject.WorldPosition,
			preferForward: false );
		ReleaseExclusiveForLifecycle(
			GameObject.WorldPosition,
			preferForward: false );
		ClearNativeItems();
		exclusiveSource = null;
		utilitySource = null;
		ResetOwnerSelectionHandoff();
	}

	internal void ClearForRoundReset()
	{
		if ( !Networking.IsHost )
			return;

		ResolveUtilitySource()?.ReleaseCarrierForRoundReset( this );
		ResolveExclusiveSource()?.ReleaseCarrierForRoundReset( this );
		ClearNativeItems();
		exclusiveSource = null;
		utilitySource = null;
		ResetOwnerSelectionHandoff();
	}

	internal void HandleMapTransition( Scene departingScene )
	{
		if ( !Networking.IsHost )
			return;

		if ( HasExclusiveFirearm )
		{
			ResolveExclusiveSource( departingScene )?.ReturnCarrierToOrigin(
				this,
				GetExclusiveFirearm() );
		}

		if ( HasUtility )
		{
			ResolveUtilitySource( departingScene )?.ReturnCarrierToOrigin(
				this,
				UtilityState );
		}

		ClearNativeItems();
		exclusiveSource = null;
		utilitySource = null;
		ResetOwnerSelectionHandoff();
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

	internal void HandleUtilitySourceDestroyed(
		LargeLadDodgeballPickup source )
	{
		if ( !Networking.IsHost ||
			source is null ||
			!HasUtility ||
			source.UtilityInstanceId != UtilityState.InstanceId )
		{
			return;
		}

		RemoveUtilityItem();
		SelectMelee();
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	private void RequestDropSelectedExclusive()
	{
		if ( TryDropSelectedExclusive() )
			return;

		ReceivePickupFeedback(
			"No safe place to drop the exclusive weapon." );
	}

	internal bool TryDropSelectedExclusive()
	{
		var player = Components.Get<LargeLadPlayer>();
		var firearm = GetExclusiveFirearm();

		if ( !Networking.IsHost ||
			!LargeLadNativeWeaponRules.CanDropExclusiveFirearm(
				player?.Role ?? LargeLadRole.Unassigned,
				player?.Health?.IsDead == false,
				player?.IsEatBusy == true,
				firearm?.IsExclusive == true,
				ActiveFirearm == firearm ) ||
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
			firearm,
			GameObject.WorldPosition,
			forward ) )
		{
			return false;
		}

		exclusiveSource = null;
		SelectMelee();
		return true;
	}

	internal void RequestThrowSelectedUtilityFromOwner(
		LargeLadDodgeballItem item )
	{
		if ( item is null || item != ActiveUtility )
			return;

		Components.Get<LargeLadPlayer>()?
			.AbilityPresentation?
			.TriggerPredictedUtilityUse();
		nextOwnerUtilityThrowRequestSequence++;
		RequestThrowSelectedUtility(
			nextOwnerUtilityThrowRequestSequence );
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
		var state = UtilityState;

		if ( !Networking.IsHost ||
			player?.IsEatBusy == true ||
			!LargeLadUtilityRules.CanDrop(
				isHost: true,
				ownerRequest: true,
				player?.Role ?? LargeLadRole.Unassigned,
				player?.Health?.IsDead != false,
				state,
				ActiveUtility?.ToUtilityState() ?? default ) ||
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
			state,
			GameObject.WorldPosition,
			forward ) )
		{
			return false;
		}

		RemoveUtilityItem();
		SelectMelee();
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
		var state = UtilityState;

		if ( !Networking.IsHost ||
			player?.IsEatBusy == true ||
			!LargeLadUtilityRules.CanDrop(
				isHost: true,
				ownerRequest: true,
				player?.Role ?? LargeLadRole.Unassigned,
				player?.Health?.IsDead != false,
				state,
				ActiveUtility?.ToUtilityState() ?? default ) ||
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
			state,
			eye.Position,
			eye.Rotation.Forward,
			inheritedVelocity ) )
		{
			return false;
		}

		player.AbilityPresentation?.BroadcastUtilityUse();
		RemoveUtilityItem();
		SelectMelee();
		return true;
	}

	[Rpc.Owner( NetFlags.HostOnly )]
	private void ReceivePickupFeedback( string message )
	{
		pickupFeedback = message;
		hasPickupFeedback = !string.IsNullOrWhiteSpace( message );
		timeSincePickupFeedback = 0.0f;
	}

	private bool TryRouteInventoryInput()
	{
		if ( LargeLadLocalUiInput.ShouldSuppressGameplayInput )
			return true;

		for ( var index = 0;
			index < DirectSelectionActions.Length;
			index++ )
		{
			if ( !Input.Pressed( DirectSelectionActions[index] ) )
				continue;

			TrySelectLogicalInventoryIndex( index );
			return true;
		}

		if ( Input.MouseWheel.y != 0.0f ||
			Input.Pressed( "SlotPrev" ) ||
			Input.Pressed( "SlotNext" ) )
		{
			var items = GetOrderedNativeItems();
			var direction = Input.MouseWheel.y > 0.0f ||
				Input.Pressed( "SlotPrev" ) ? -1 : 1;
			var current = items.ToList().IndexOf( ActiveItem );
			var target = LargeLadNativeWeaponRules.GetCycledIndex(
				current,
				items.Count,
				direction );

			TrySelectLogicalInventoryIndex( target );

			return true;
		}

		if ( Input.Pressed( "DropWeapon" ) )
		{
			if ( ActiveFirearm?.IsExclusive == true )
				RequestDropSelectedExclusive();
			else if ( ActiveUtility is not null )
				RequestDropSelectedUtility();

			return true;
		}

		return false;
	}

	private bool TrySelectLogicalInventoryIndex( int index )
	{
		var items = GetOrderedNativeItems();

		return index >= 0 && index < items.Count &&
			SelectNativeItem( items[index] );
	}

	private void ReleaseUtilityForLifecycle(
		Vector3 dropPosition,
		bool preferForward )
	{
		if ( !HasUtility )
			return;

		var state = UtilityState;
		var source = ResolveUtilitySource();

		if ( source is null || !source.IsValid )
		{
			RemoveUtilityItem();
			return;
		}

		var controller = Components.Get<PlayerController>();
		var forward = preferForward
			? controller?.EyeTransform.Rotation.Forward ??
				GameObject.WorldRotation.Forward
			: GameObject.WorldRotation.Forward;

		if ( !source.TryDropFromCarrier(
			this,
			state,
			dropPosition,
			forward ) )
		{
			source.ReturnCarrierToOrigin( this, state );
		}

		RemoveUtilityItem();
	}

	private LargeLadDodgeballPickup ResolveUtilitySource(
		Scene sourceScene = null )
	{
		if ( !HasUtility )
			return null;

		var state = UtilityState;
		if ( utilitySource is not null &&
			utilitySource.IsValid &&
			utilitySource.UtilityInstanceId == state.InstanceId )
		{
			return utilitySource;
		}

		foreach ( var pickup in
			(sourceScene ?? Scene)?
				.GetAllComponents<LargeLadDodgeballPickup>() ??
			System.Array.Empty<LargeLadDodgeballPickup>() )
		{
			if ( pickup.UtilityInstanceId != state.InstanceId )
				continue;

			utilitySource = pickup;
			return pickup;
		}

		return null;
	}

	private void RemoveUtilityItem()
	{
		if ( UtilityItem is LargeLadDodgeballItem item )
			Remove( item );

		utilitySource = null;
	}

	private void BeginOwnerSelectionHandoff( bool combatPresentation )
	{
		ownerWantsNativeControl = true;
		ownerWantsNativeCombatPresentation = combatPresentation;
		timeSinceNativeSelectionRequest = 0.0f;
	}

	private void ResetOwnerSelectionHandoff()
	{
		ownerWantsNativeControl = false;
		ownerWantsNativeCombatPresentation = false;
	}

	private void ReleaseExclusiveForLifecycle(
		Vector3 dropPosition,
		bool preferForward )
	{
		if ( GetExclusiveFirearm() is not LargeLadFirearm firearm )
			return;

		var source = ResolveExclusiveSource();

		if ( source is null || !source.IsValid )
		{
			RemoveExclusiveFirearm();
			exclusiveSource = null;
			return;
		}

		var controller = Components.Get<PlayerController>();
		var forward = preferForward
			? controller?.EyeTransform.Rotation.Forward ??
				GameObject.WorldRotation.Forward
			: GameObject.WorldRotation.Forward;

		if ( !source.TryDropFromCarrier(
			this,
			firearm,
			dropPosition,
			forward ) )
		{
			source.ReturnCarrierToOrigin( this, firearm );
		}

		exclusiveSource = null;
	}

	private LargeLadWeaponPickup ResolveExclusiveSource(
		Scene sourceScene = null )
	{
		var firearm = GetExclusiveFirearm();
		if ( firearm is null )
			return null;

		if ( exclusiveSource is not null &&
			exclusiveSource.IsValid &&
			exclusiveSource.ExclusiveInstanceId ==
				firearm.ExclusiveInstanceId )
		{
			return exclusiveSource;
		}

		foreach ( var pickup in
			(sourceScene ?? Scene)?
				.GetAllComponents<LargeLadWeaponPickup>() ??
			System.Array.Empty<LargeLadWeaponPickup>() )
		{
			if ( pickup.PickupPolicy != LargeLadPickupPolicy.Exclusive ||
				pickup.Weapon != firearm.WeaponId ||
				pickup.ExclusiveInstanceId != firearm.ExclusiveInstanceId )
			{
				continue;
			}

			exclusiveSource = pickup;
			return pickup;
		}

		return null;
	}

	private bool CanOwnNativeItems()
	{
		var player = Components.Get<LargeLadPlayer>();
		return LargeLadNativeWeaponRules.CanOwnSkinnyKidItem(
			player?.Role ?? LargeLadRole.Unassigned,
			player?.Health?.IsDead == false,
			player?.IsEatBusy == true );
	}

	private IEnumerable<LargeLadFirearm> GetCoreFirearms()
	{
		return GetSlotItems( LargeLadNativeWeaponRules.CoreFirearmSlot )
			.OfType<LargeLadFirearm>()
			.OrderBy( firearm => firearm.SlotOrder );
	}

	private IEnumerable<LargeLadFirearm> GetExclusiveFirearms()
	{
		return GetSlotItems( LargeLadNativeWeaponRules.ExclusiveFirearmSlot )
			.OfType<LargeLadFirearm>();
	}

	private void ClearNativeItems()
	{
		ForceHolster();

		foreach ( var item in Items.ToArray() )
			Remove( item );
	}
}

/// <summary>
/// Native slot-3 ownership item for an authored dodgeball.
/// The physical pickup remains the single world instance and continues to
/// resolve throws, impacts, damage, and rigid-body simulation on the host.
/// </summary>
public sealed class LargeLadDodgeballItem : BaseInventoryItem
{
	[Sync( SyncFlags.FromHost )]
	public int UtilityInstanceId { get; private set; }

	public LargeLadUtilityId UtilityId =>
		UtilityInstanceId > 0
			? LargeLadUtilityId.Dodgeball
			: LargeLadUtilityId.None;

	public LargeLadUtilityState ToUtilityState()
	{
		return UtilityInstanceId > 0
			? LargeLadUtilityState.CreateDodgeball( UtilityInstanceId )
			: default;
	}

	internal void InitializeState( LargeLadUtilityState state )
	{
		if ( !Networking.IsHost ||
			!LargeLadUtilityRules.IsValidState( state ) ||
			state.Utility != LargeLadUtilityId.Dodgeball )
		{
			return;
		}

		UtilityInstanceId = state.InstanceId;
	}

	protected override bool OnCanPickup( BaseInventoryComponent inventory )
	{
		return false;
	}

	protected override bool OnAdding( BaseInventoryComponent inventory )
	{
		return inventory is LargeLadNativeInventory;
	}

	protected override bool OnCanSwitchTo()
	{
		var player = Inventory?.Components.Get<LargeLadPlayer>();

		return LargeLadUtilityRules.IsValidState( ToUtilityState() ) &&
			player?.IsEatBusy != true &&
			LargeLadUtilityRules.CanUseUtility(
				player?.Role ?? LargeLadRole.Unassigned,
				player?.Health?.IsDead != false );
	}

	protected override void OnControl()
	{
		if ( !IsActive ||
			LargeLadLocalUiInput.ShouldSuppressGameplayInput ||
			!Input.Pressed( "Attack1" ) )
			return;

		(Inventory as LargeLadNativeInventory)?
			.RequestThrowSelectedUtilityFromOwner( this );
	}
}

/// <summary>
/// Native Skinny Kid crowbar. BaseCombatWeapon owns input, deployment, holder
/// pose, view/world models, and swing presentation. LargeLadMeleeCombat keeps
/// the existing host-authoritative target selection and damage rules.
/// </summary>
public sealed class LargeLadMeleeWeapon : BaseCombatWeapon
{
	private LargeLadMeleeCombat cachedMeleeCombat;

	protected override bool OnCanPickup( BaseInventoryComponent inventory )
	{
		return false;
	}

	protected override bool OnAdding( BaseInventoryComponent inventory )
	{
		return inventory is LargeLadNativeInventory &&
			CanBeOwnedBy( inventory );
	}

	protected override bool OnCanSwitchTo()
	{
		return CanBeOwnedBy( Inventory ) && base.OnCanSwitchTo();
	}

	public override bool CanPrimaryAttack()
	{
		return !LargeLadLocalUiInput.ShouldSuppressGameplayInput &&
			ResolveMeleeCombat()?.CanNativeAttack( this ) == true &&
			base.CanPrimaryAttack();
	}

	public override void PrimaryAttack()
	{
		if ( ResolveMeleeCombat()?.TryRequestNativeAttack( this ) != true )
			return;

		// PrimaryAttack's default is ballistic. The native effects-only path
		// drives the crowbar graph and Citizen attack gesture while the existing
		// host RPC resolves the actual melee target and damage.
		ShootEffects();
	}

	protected override void OnEquipped()
	{
		base.OnEquipped();
		EnsureNativePresentation();
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();

		if ( !IsHeld || !IsActive )
			return;

		// Model instances are local presentation objects rather than networked
		// inventory state. Re-establish them after an equip/hotload if this peer
		// observed the active item before BaseCombatWeapon completed its normal
		// model lifecycle. The native creation methods remain responsible for
		// attachment, deploy animation, and weapon-model binding.
		EnsureNativePresentation();

		if ( !IsProxy )
			ApplyLocalPresentationMode( Scene?.Camera );
	}

	protected override void CreateWorldModel()
	{
		base.CreateWorldModel();
	}

	protected override void CreateViewModel()
	{
		base.CreateViewModel();

		if ( !IsProxy )
			ApplyLocalPresentationMode( Scene?.Camera );
	}

	protected override void PlaceViewModel(
		CameraComponent camera,
		in CameraView view )
	{
		var thirdPerson = ApplyLocalPresentationMode( camera );

		if ( !thirdPerson )
			base.PlaceViewModel( camera, in view );
	}

	protected override void OnHolstered()
	{
		if ( !IsProxy )
			Scene?.Camera?.RenderExcludeTags.Remove( "firstperson" );

		base.OnHolstered();
	}

	public override void DrawHud(
		Sandbox.Rendering.HudPainter hud,
		Vector2 point )
	{
		// Large Lad's HUD already draws the melee reticle and hit confirmation.
	}

	internal bool IsAuthoritativelyHeldBy( LargeLadPlayer attacker )
	{
		return attacker is not null &&
			Owner?.GameObject == attacker.GameObject &&
			Inventory is LargeLadNativeInventory inventory &&
			inventory.ActiveItem == this &&
			IsActive &&
			IsHeld;
	}

	private static void SetPresentationEnabled(
		GameObject presentation,
		bool enabled )
	{
		if ( presentation is not null && presentation.IsValid )
			presentation.Enabled = enabled;
	}

	private bool ApplyLocalPresentationMode( CameraComponent camera )
	{
		var thirdPerson = Owner?.ThirdPerson == true;

		if ( camera is not null && camera.IsValid )
		{
			if ( thirdPerson )
				camera.RenderExcludeTags.Add( "firstperson" );
			else
				camera.RenderExcludeTags.Remove( "firstperson" );
		}

		SetPresentationEnabled( ViewModel, !thirdPerson );
		SetPresentationEnabled( WorldModel, thirdPerson );
		return thirdPerson;
	}

	private void EnsureNativePresentation()
	{
		var owner = Owner;

		if ( owner is null )
			return;

		if ( WorldModel is null || !WorldModel.IsValid )
			CreateWorldModel();

		if ( !IsProxy && (ViewModel is null || !ViewModel.IsValid) )
			CreateViewModel();
	}

	private LargeLadMeleeCombat ResolveMeleeCombat()
	{
		var owner = Owner;

		if ( owner is null )
			return null;

		if ( cachedMeleeCombat is null ||
			!cachedMeleeCombat.IsValid ||
			cachedMeleeCombat.GameObject != owner.GameObject )
		{
			cachedMeleeCombat =
				owner.Components.Get<LargeLadMeleeCombat>();
		}

		return cachedMeleeCombat;
	}

	private static bool CanBeOwnedBy( BaseInventoryComponent inventory )
	{
		var player = inventory?.Components.Get<LargeLadPlayer>();
		return LargeLadNativeWeaponRules.CanOwnSkinnyKidItem(
			player?.Role ?? LargeLadRole.Unassigned,
			player?.Health?.IsDead == false,
			player?.IsEatBusy == true );
	}
}

/// <summary>
/// Thin Large Lad policy layer over the native firearm. BaseCombatWeapon owns
/// input, fire gating, ammo, reload, aim, claims, models, and presentation.
/// </summary>
public enum LargeLadFirearmHitResult
{
	Miss,
	PlayerHit,
	PlayerHeadshot,
	BarricadeHit
}

public sealed class LargeLadFirearm : BaseCombatWeapon,
	Component.ITriggerListener
{
	private const float ConfirmedHitmarkerDuration = 0.14f;
	private const float ReloadSoundVolume = 0.3f;

	// BaseCombatWeapon numbers the first native shot claim as sequence zero.
	private int lastHostClaimSequence = -1;
	private int lastOwnerHitResultSequence = -1;
	private bool hasHostClaimSchedule;
	private bool hasConfirmedHit;
	private float nextHostClaimTime;
	private bool reloadSoundLatched;
	private bool hasPlayedReloadSound;
	private TimeSince timeSinceConfirmedHit;
	private TimeSince timeSinceReloadSound;
	private SoundEvent reloadSound;
	private BoxCollider exclusiveWorldCollider;
	private Transform preparedExclusiveWorldTransform;
	private bool hasPreparedExclusiveWorldDrop;

	[Property]
	public LargeLadWeaponId WeaponId { get; set; } = LargeLadWeaponId.Pistol;

	[Property]
	[Sync( SyncFlags.FromHost )]
	public LargeLadPickupPolicy PickupPolicy { get; set; } =
		LargeLadPickupPolicy.Core;

	[Property, Group( "Exclusive Ammunition" )]
	public int ExclusiveStartingReserve { get; set; }

	[Property, Group( "Native Presentation" )]
	public string MuzzleAttachment { get; set; } = "muzzle";

	[Property, Group( "Native Presentation" )]
	public string ReloadSoundPackageIdent { get; set; }

	[Sync( SyncFlags.FromHost )]
	public int ExclusiveInstanceId { get; private set; }

	[Sync( SyncFlags.FromHost )]
	public int ExclusiveReserve { get; private set; }

	public int LastAuthoritativeShotSequence { get; private set; }
	public LargeLadFirearmHitResult LastHitResult { get; private set; } =
		LargeLadFirearmHitResult.Miss;
	public bool HasConfirmedHitmarker =>
		hasConfirmedHit &&
		timeSinceConfirmedHit < ConfirmedHitmarkerDuration;

	public bool IsExclusive =>
		PickupPolicy == LargeLadPickupPolicy.Exclusive;

	protected override async Task OnLoad()
	{
		if ( Application.IsDedicatedServer ||
			string.IsNullOrWhiteSpace( ReloadSoundPackageIdent ) )
		{
			return;
		}

		try
		{
			reloadSound = await Cloud.Load<SoundEvent>(
				ReloadSoundPackageIdent );
		}
		catch ( System.Exception exception )
		{
			Log.Warning(
				$"Unable to mount native reload sound package " +
				$"'{ReloadSoundPackageIdent}': {exception.Message}" );
		}
	}

	internal bool InitializeExclusiveState(
		int exclusiveInstanceId )
	{
		if ( !Networking.IsHost )
			return false;

		PickupPolicy = LargeLadPickupPolicy.Exclusive;
		PreferredSlot = LargeLadNativeWeaponRules.ExclusiveFirearmSlot;
		SlotOrder = 0;
		ExclusiveInstanceId = System.Math.Max( 0, exclusiveInstanceId );
		Clip1 = System.Math.Max( 0, ClipMaxSize );
		ExclusiveReserve = System.Math.Max( 0, ExclusiveStartingReserve );

		return ExclusiveInstanceId > 0 && EnsureExclusiveWorldPresentation();
	}

	internal void ResetExclusiveAmmunition()
	{
		if ( !Networking.IsHost || !IsExclusive )
			return;

		Clip1 = System.Math.Max( 0, ClipMaxSize );
		ExclusiveReserve = System.Math.Max( 0, ExclusiveStartingReserve );
	}

	internal void PrepareExclusiveWorldDrop( Transform worldTransform )
	{
		if ( !Networking.IsHost || !IsExclusive )
			return;

		preparedExclusiveWorldTransform = worldTransform;
		hasPreparedExclusiveWorldDrop = true;
	}

	internal void CancelExclusiveWorldDrop()
	{
		hasPreparedExclusiveWorldDrop = false;
	}

	internal void PlaceExclusiveWorldItem( Transform worldTransform )
	{
		if ( !Networking.IsHost || !IsExclusive || Inventory is not null )
			return;

		EnsureExclusiveWorldPresentation();
		SetExclusiveWorldPresentationEnabled( false );
		GameObject.WorldTransform = worldTransform;

		if ( GameObject.Network.Active )
			GameObject.Network.ClearInterpolation();

		SetExclusiveWorldPresentationEnabled( true );
	}

	protected override int GetReserveAmmo( BaseAmmoResource ammoType )
	{
		return IsExclusive
			? ExclusiveReserve
			: base.GetReserveAmmo( ammoType );
	}

	protected override int TakeReserveAmmo(
		BaseAmmoResource ammoType,
		int amount )
	{
		if ( !IsExclusive )
			return base.TakeReserveAmmo( ammoType, amount );

		var taken = System.Math.Min(
			ExclusiveReserve,
			System.Math.Max( 0, amount ) );
		ExclusiveReserve -= taken;
		return taken;
	}

	protected override bool OnCanPickup( BaseInventoryComponent inventory )
	{
		return inventory is LargeLadNativeInventory &&
			(!IsExclusive || ExclusiveInstanceId > 0) &&
			CanBeOwnedBy( inventory ) &&
			base.OnCanPickup( inventory );
	}

	protected override bool OnAdding( BaseInventoryComponent inventory )
	{
		return inventory is LargeLadNativeInventory &&
			CanBeOwnedBy( inventory );
	}

	protected override void OnAdded( BaseInventoryComponent inventory )
	{
		base.OnAdded( inventory );
		lastHostClaimSequence = -1;
		hasHostClaimSchedule = false;
		nextHostClaimTime = 0.0f;

		if ( !IsExclusive )
			return;

		hasPreparedExclusiveWorldDrop = false;
		SetExclusiveWorldPresentationEnabled( false );
		ResolveExclusiveSource()?.NotifyExclusivePickedUp(
			this,
			inventory as LargeLadNativeInventory );
	}

	protected override void OnRemoved( BaseInventoryComponent inventory )
	{
		base.OnRemoved( inventory );

		if ( IsExclusive && Inventory is null )
			SetExclusiveWorldPresentationEnabled( true );
	}

	protected override bool OnDrop()
	{
		if ( IsExclusive && !hasPreparedExclusiveWorldDrop )
			return false;

		var worldTransform = preparedExclusiveWorldTransform;

		if ( !base.OnDrop() )
			return false;

		if ( IsExclusive )
		{
			hasPreparedExclusiveWorldDrop = false;
			PlaceExclusiveWorldItem( worldTransform );
		}

		return true;
	}

	protected override void OnDestroy()
	{
		if ( Networking.IsHost && IsExclusive )
			ResolveExclusiveSource()?.HandleExclusiveInstanceDestroyed( this );

		base.OnDestroy();
	}

	public void OnTriggerEnter( Collider other )
	{
		if ( !Networking.IsHost || !IsExclusive || Inventory is not null )
			return;

		var player = other?.GameObject?.Components.Get<LargeLadPlayer>(
			FindMode.EverythingInSelfAndAncestors );

		if ( player?.Role != LargeLadRole.SkinnyKid ||
			player.Health?.IsDead != false )
		{
			return;
		}

		var inventory = player.NativeInventory;

		if ( inventory is null )
			return;

		if ( inventory.HasExclusiveFirearm )
		{
			inventory.NotifyExclusiveSlotFull();
			return;
		}

		var source = ResolveExclusiveSource();
		if ( source is null )
			return;

		inventory.PickupWorldItem( this );

		if ( Inventory == inventory )
			source.NotifyExclusivePickedUp( this, inventory );
	}

	public void OnTriggerExit( Collider other )
	{
	}

	protected override bool OnCanSwitchTo()
	{
		return IsValidHolderState( requirePlayingRound: false ) &&
			base.OnCanSwitchTo();
	}

	public override bool CanPrimaryAttack()
	{
		return !LargeLadLocalUiInput.ShouldSuppressGameplayInput &&
			IsValidHolderState( requirePlayingRound: true ) &&
			base.CanPrimaryAttack();
	}

	protected override void OnEquipped()
	{
		base.OnEquipped();
		EnsureNativePresentation();
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();

		if ( IsExclusive )
		{
			ResolveExclusiveWorldPresentation();
			SetExclusiveWorldPresentationEnabled( Inventory is null );
		}

		if ( IsHeld && IsActive )
			EnsureNativePresentation();

		BindNativeModelAttachments( ViewModel );
		BindNativeModelAttachments( WorldModel );

		if ( !IsProxy && IsHeld && IsActive )
			ApplyLocalPresentationMode( Scene?.Camera );
	}

	protected override void CreateWorldModel()
	{
		base.CreateWorldModel();
		BindNativeModelAttachments( WorldModel );
	}

	protected override void CreateViewModel()
	{
		base.CreateViewModel();
		BindNativeModelAttachments( ViewModel );

		if ( !IsProxy )
			ApplyLocalPresentationMode( Scene?.Camera );
	}

	protected override void OnShootEffects( ShotEffect shot )
	{
		// The native tracer and muzzle-flash code both consume the active
		// BaseWeaponModel's MuzzleGameObject. Keep the shot trace eye-aimed, but
		// bind presentation to the authored muzzle attachment before the native
		// effects run.
		BindNativeModelAttachments( WeaponModel );
		base.OnShootEffects( shot );
	}

	protected override void OnReloadStarted()
	{
		base.OnReloadStarted();

		// Reload state can be observed once from local prediction and again from
		// replication. Only the owning firearm emits first-person reload audio,
		// and it may do so once until this reload finishes or is cancelled.
		if ( IsProxy || reloadSoundLatched ||
			(hasPlayedReloadSound && timeSinceReloadSound < 0.25f) )
		{
			return;
		}

		reloadSoundLatched = true;
		hasPlayedReloadSound = true;
		timeSinceReloadSound = 0.0f;
		PlayReloadSound();
	}

	protected override void OnReloadFinished()
	{
		reloadSoundLatched = false;
		base.OnReloadFinished();
	}

	protected override void OnReloadCancelled()
	{
		reloadSoundLatched = false;
		base.OnReloadCancelled();
	}

	protected override void PlaceViewModel(
		CameraComponent camera,
		in CameraView view )
	{
		var thirdPerson = ApplyLocalPresentationMode( camera );

		if ( !thirdPerson )
			base.PlaceViewModel( camera, in view );
	}

	protected override void OnHolstered()
	{
		if ( !IsProxy )
			Scene?.Camera?.RenderExcludeTags.Remove( "firstperson" );

		base.OnHolstered();
	}

	public override void DrawHud(
		Sandbox.Rendering.HudPainter hud,
		Vector2 point )
	{
		// Large Lad's existing HUD draws the native Clip1 / infinity state and
		// crosshair, so suppress only the stock overlay to avoid double drawing.
	}

	public override void PrimaryAttack()
	{
		if ( Networking.IsHost )
			LastAuthoritativeShotSequence++;

		base.PrimaryAttack();
	}

	private static void SetPresentationEnabled(
		GameObject presentation,
		bool enabled )
	{
		if ( presentation is not null && presentation.IsValid )
			presentation.Enabled = enabled;
	}

	private bool ApplyLocalPresentationMode( CameraComponent camera )
	{
		var thirdPerson = Owner?.ThirdPerson == true;

		// BaseCombatWeapon uses this tag to select the native view/world model
		// that receives muzzle, brass, tracer and animation presentation.
		if ( camera is not null && camera.IsValid )
		{
			if ( thirdPerson )
				camera.RenderExcludeTags.Add( "firstperson" );
			else
				camera.RenderExcludeTags.Remove( "firstperson" );
		}

		SetPresentationEnabled( ViewModel, !thirdPerson );
		SetPresentationEnabled( WorldModel, thirdPerson );
		return thirdPerson;
	}

	private void EnsureNativePresentation()
	{
		var owner = Owner;

		if ( owner is null )
			return;

		if ( WorldModel is null || !WorldModel.IsValid )
			CreateWorldModel();

		if ( !IsProxy && (ViewModel is null || !ViewModel.IsValid) )
			CreateViewModel();
	}

	private void BindNativeModelAttachments( GameObject presentation )
	{
		if ( presentation is null || !presentation.IsValid )
			return;

		BindNativeModelAttachments(
			presentation.Components.Get<BaseWeaponModel>() );
	}

	private void BindNativeModelAttachments( BaseWeaponModel weaponModel )
	{
		var renderer = weaponModel?.Renderer;
		if ( weaponModel is null || !weaponModel.IsValid ||
			renderer is null || !renderer.IsValid )
		{
			return;
		}

		if ( string.IsNullOrWhiteSpace( MuzzleAttachment ) )
			return;

		// Model attachments are empty transform GameObjects. Enabling their
		// hierarchy does not create renderers or bone debug geometry.
		renderer.CreateAttachments = true;
		var muzzle = renderer.GetAttachmentObject(
			MuzzleAttachment );

		if ( muzzle is not null && muzzle.IsValid &&
			weaponModel.MuzzleGameObject != muzzle )
		{
			weaponModel.MuzzleGameObject = muzzle;
		}
	}

	private void PlayReloadSound()
	{
		if ( Application.IsDedicatedServer )
			return;

		if ( reloadSound is null )
			return;

		var source = WeaponModel?.GameObject;
		if ( source is null || !source.IsValid )
			source = GameObject;

		var handle = source.PlaySound( reloadSound, Vector3.Zero );
		if ( handle is null )
			return;

		// The owning viewmodel is listener-relative.
		handle.Volume = ReloadSoundVolume;
		handle.SpacialBlend = 0.0f;
		handle.DistanceAttenuation = false;
		handle.OcclusionEnabled = false;
		handle.ReverbEnabled = false;
	}

	protected override bool OnValidateShotClaim( in ShotClaim claim )
	{
		var ballistics = Ballistics;
		var pellets = claim.Pellets;

		if ( !LargeLadNativeWeaponRules.IsValidClaimEnvelope(
			claim.Sequence,
			lastHostClaimSequence,
			claim.Damage,
			ballistics.Damage,
			claim.Force,
			ballistics.Force,
			pellets?.Length ?? 0,
			ballistics.Pellets ) )
		{
			return false;
		}

		// Consume every new sequence before the remaining validation so a bad
		// payload cannot be replayed after the holder state changes.
		lastHostClaimSequence = claim.Sequence;

		if ( !Networking.IsHost ||
			!IsValidHolderState( requirePlayingRound: true ) ||
			!LargeLadNativeWeaponRules.IsPlausibleClaimCadence(
				hasHostClaimSchedule,
				Time.Now,
				nextHostClaimTime ) )
		{
			return false;
		}

		var owner = Owner;
		var attacker = owner?.Components.Get<LargeLadPlayer>();
		if ( owner is null || attacker is null )
			return false;

		var shotResult = LargeLadFirearmHitResult.Miss;

		foreach ( var pellet in pellets )
		{
			if ( !ValidatePellet(
				attacker,
				owner,
				pellet,
				ballistics.Range,
				out var pelletResult ) )
			{
				return false;
			}

			if ( pelletResult == LargeLadFirearmHitResult.PlayerHeadshot ||
				(pelletResult == LargeLadFirearmHitResult.PlayerHit &&
					shotResult != LargeLadFirearmHitResult.PlayerHeadshot) ||
				(pelletResult == LargeLadFirearmHitResult.BarricadeHit &&
					shotResult == LargeLadFirearmHitResult.Miss) )
			{
				shotResult = pelletResult;
			}
		}

		LastAuthoritativeShotSequence = claim.Sequence;
		hasHostClaimSchedule = true;
		nextHostClaimTime = System.MathF.Max(
			Time.Now,
			nextHostClaimTime ) + PrimaryDelay;

		if ( !base.OnValidateShotClaim( claim ) )
			return false;

		ReceiveHitResult( claim.Sequence, shotResult );
		return true;
	}

	[Rpc.Owner( NetFlags.HostOnly )]
	private void ReceiveHitResult(
		int shotSequence,
		LargeLadFirearmHitResult result )
	{
		if ( shotSequence <= lastOwnerHitResultSequence )
			return;

		lastOwnerHitResultSequence = shotSequence;
		LastHitResult = result;

		if ( result is not (LargeLadFirearmHitResult.PlayerHit or
			LargeLadFirearmHitResult.PlayerHeadshot or
			LargeLadFirearmHitResult.BarricadeHit) )
		{
			return;
		}

		hasConfirmedHit = true;
		timeSinceConfirmedHit = 0.0f;
	}

	internal bool IsAuthoritativelyHeldBy( LargeLadPlayer attacker )
	{
		return attacker is not null &&
			Owner?.GameObject == attacker.GameObject &&
			Inventory is LargeLadNativeInventory inventory &&
			inventory.ActiveItem == this &&
			IsActive &&
			IsHeld;
	}

	private bool ValidatePellet(
		LargeLadPlayer attacker,
		PlayerController owner,
		PelletClaim pellet,
		float range,
		out LargeLadFirearmHitResult result )
	{
		result = LargeLadFirearmHitResult.Miss;

		if ( pellet.HitObject is null || !pellet.HitObject.IsValid ||
			!LargeLadNativeWeaponRules.IsPlausiblePellet(
				owner.EyePosition,
				pellet.Origin,
				pellet.Position,
				pellet.Direction,
				range ) )
		{
			return false;
		}

		var victim = pellet.HitObject.Components.Get<LargeLadPlayer>(
			FindMode.EverythingInSelfAndAncestors );
		if ( victim is not null &&
			!LargeLadNativeWeaponRules.IsValidPlayerTarget(
				attacker.Role,
				victim.Role,
				victim.Health?.IsDead == false ) )
		{
			return false;
		}

		// Native third-person claims originate at the camera. Rebuild the shot
		// from the authoritative eye toward the claimed impact so a displaced
		// camera origin cannot shoot around host-side obstructions.
		if ( !LargeLadAimResolver.TryResolveAuthoritative(
			Scene,
			owner,
			attacker.GameObject,
			range,
			pellet.Position,
			out var hostAim,
			out _ ) )
		{
			return false;
		}

		var trace = hostAim.ShotTrace;

		if ( !trace.Hit ||
			!IsSameHierarchy( trace.GameObject, pellet.HitObject ) )
		{
			return false;
		}

		// Claimed DamageInfo has no Hitbox. Hitgroups are client-reported tags,
		// so a head claim is accepted only when the host's bounded re-trace also
		// resolves the selected target's hitbox as a head.
		var claimedHeadshot = victim is not null &&
			pellet.Tags?.Has(
				LargeLadFirearmHitRules.HeadHitboxTag ) == true;

		if ( claimedHeadshot )
		{
			var hostRegion = ResolveHostHitRegion(
				victim,
				owner.EyePosition,
				hostAim.ShotDirection,
				range );

			if ( hostRegion != LargeLadHitRegion.Head )
				return false;
		}

		if ( victim is not null )
		{
			result = claimedHeadshot
				? LargeLadFirearmHitResult.PlayerHeadshot
				: LargeLadFirearmHitResult.PlayerHit;
		}
		else if ( LargeLadBarricade.FindFor( pellet.HitObject ) is not null )
		{
			result = LargeLadFirearmHitResult.BarricadeHit;
		}

		return true;
	}

	private LargeLadHitRegion ResolveHostHitRegion(
		LargeLadPlayer victim,
		Vector3 origin,
		Vector3 direction,
		float range )
	{
		var traces = Scene.Trace
			.Ray( origin, origin + direction * range )
			.UseHitboxes( true )
			.WithoutTags( LargeLadGameplayRules.MinionPassageTag )
			.IgnoreGameObjectHierarchy( GameObject )
			.RunAll();
		var candidates = new List<LargeLadFirearmHitboxCandidate>();

		foreach ( var trace in traces )
		{
			var hitbox = trace.Hitbox;
			var hitPlayer = trace.GameObject?.Components.Get<LargeLadPlayer>(
				FindMode.EverythingInSelfAndAncestors );

			candidates.Add( new LargeLadFirearmHitboxCandidate(
				hitPlayer == victim,
				hitbox is not null,
				trace.Distance,
				hitbox?.Bone?.Name,
				hitbox?.Tags?.Has(
					LargeLadFirearmHitRules.HeadHitboxTag ) == true ) );
		}

		return LargeLadFirearmHitRules.ResolveSelectedTargetHitRegion(
			candidates,
			range );
	}

	private bool IsValidHolderState( bool requirePlayingRound )
	{
		var owner = Owner;
		var player = owner?.Components.Get<LargeLadPlayer>();
		var inventory = Inventory as LargeLadNativeInventory;
		var manager = LargeLadGameManager.FindForScene( Scene );
		var phase = manager?.Phase ?? LargeLadRoundPhase.WaitingForPlayers;

		if ( player is null || inventory is null )
			return false;

		if ( !requirePlayingRound )
		{
			return player.Role == LargeLadRole.SkinnyKid &&
				player.Health?.IsDead == false &&
				!player.IsEatBusy &&
				!player.MovementLocked &&
				!player.IsGroundSlamBusy &&
				!player.IsGroundSlamStaggered;
		}

		return LargeLadNativeWeaponRules.CanUseFirearm(
			player.Role,
			player.Health?.IsDead == false,
			phase,
			player.IsEatBusy,
			player.MovementLocked,
			player.IsGroundSlamBusy,
			player.IsGroundSlamStaggered,
			IsHeld,
			inventory.ActiveItem == this && IsActive );
	}

	private bool EnsureExclusiveWorldPresentation()
	{
		if ( !IsExclusive )
			return false;

		ResolveExclusiveWorldPresentation();

		if ( WorldModel is not null && WorldModel.IsValid &&
			exclusiveWorldCollider is not null &&
			exclusiveWorldCollider.IsValid )
		{
			return true;
		}

		if ( !Networking.IsHost )
			return false;

		if ( WorldModel is null || !WorldModel.IsValid )
			CreateWorldModel();

		if ( WorldModel is null || !WorldModel.IsValid )
		{
			Log.Warning(
				$"{GameObject.Name}: cannot create its native world pickup " +
				"because its WorldModelPrefab could not be created." );
			return false;
		}

		exclusiveWorldCollider ??=
			Components.Create<BoxCollider>();
		exclusiveWorldCollider.Center = Vector3.Up * 9.0f;
		exclusiveWorldCollider.Scale =
			new Vector3( 44.0f, 44.0f, 18.0f );
		exclusiveWorldCollider.IsTrigger = true;
		exclusiveWorldCollider.Static = true;
		GameObject.Tags.Add( "pickup" );
		return true;
	}

	private void ResolveExclusiveWorldPresentation()
	{
		exclusiveWorldCollider =
			exclusiveWorldCollider is not null &&
			exclusiveWorldCollider.IsValid
				? exclusiveWorldCollider
				: Components.Get<BoxCollider>();
	}

	private void SetExclusiveWorldPresentationEnabled( bool enabled )
	{
		ResolveExclusiveWorldPresentation();
		SetPresentationEnabled( WorldModel, enabled );

		if ( exclusiveWorldCollider is not null )
			exclusiveWorldCollider.Enabled = enabled;
	}

	private LargeLadWeaponPickup ResolveExclusiveSource()
	{
		if ( !IsExclusive || ExclusiveInstanceId <= 0 )
			return null;

		foreach ( var pickup in
			Scene?.GetAllComponents<LargeLadWeaponPickup>() ??
				System.Array.Empty<LargeLadWeaponPickup>() )
		{
			if ( pickup.PickupPolicy == LargeLadPickupPolicy.Exclusive &&
				pickup.Weapon == WeaponId &&
				pickup.ExclusiveInstanceId == ExclusiveInstanceId )
			{
				return pickup;
			}
		}

		return null;
	}

	private static bool CanBeOwnedBy( BaseInventoryComponent inventory )
	{
		var player = inventory?.Components.Get<LargeLadPlayer>();
		return player?.Role == LargeLadRole.SkinnyKid &&
			player.Health?.IsDead == false &&
			!player.IsEatBusy;
	}

	private static bool IsSameHierarchy( GameObject left, GameObject right )
	{
		return left is not null && right is not null &&
			(left == right ||
			left.IsDescendant( right ) ||
			right.IsDescendant( left ));
	}
}
