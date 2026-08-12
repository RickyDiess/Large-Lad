using Sandbox;
using System.Collections.Generic;
using System.Linq;

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
	public const string NativePistolPrefabPath =
		"prefabs/gameplay/native_pistol.prefab";

	private bool ownerWantsNativeControl;
	private TimeSince timeSinceNativeSelectionRequest;

	public LargeLadFirearm ActiveFirearm => ActiveItem as LargeLadFirearm;
	public bool HasActiveNativeWeapon => ActiveFirearm is not null;
	public bool HasNativePistol => Items
		.OfType<LargeLadFirearm>()
		.Any( weapon => weapon.WeaponId == LargeLadWeaponId.Pistol );

	/// <summary>
	/// Includes the short owner-side handoff before ActiveItem replicates. This
	/// prevents one input frame from also reaching the legacy melee/fire paths.
	/// </summary>
	public bool HasNativeInputControl =>
		HasActiveNativeWeapon || (!IsProxy && ownerWantsNativeControl);

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

		if ( !IsProxy && ownerWantsNativeControl &&
			ActiveItem is null && timeSinceNativeSelectionRequest > 0.5f )
		{
			ownerWantsNativeControl = false;
		}
	}

	protected override bool OnAdding( BaseInventoryItem item, int slot )
	{
		return item is LargeLadFirearm firearm &&
			firearm.WeaponId == LargeLadWeaponId.Pistol &&
			slot == LargeLadNativeWeaponRules.CoreFirearmSlot &&
			CanOwnNativePistol() &&
			base.OnAdding( item, slot );
	}

	protected override bool CanPickupWorldItem( BaseInventoryItem item )
	{
		return CanOwnNativePistol() && base.CanPickupWorldItem( item );
	}

	public bool TryGrantNativePistol()
	{
		if ( !Networking.IsHost || !CanOwnNativePistol() || HasNativePistol )
			return false;

		var item = Pickup(
			NativePistolPrefabPath,
			LargeLadNativeWeaponRules.CoreFirearmSlot ) as LargeLadFirearm;

		if ( item is null )
			return false;

		Switch( item );
		return true;
	}

	public bool SelectNativePistol()
	{
		var pistol = GetSlotItems( LargeLadNativeWeaponRules.CoreFirearmSlot )
			.OfType<LargeLadFirearm>()
			.FirstOrDefault( weapon =>
				weapon.WeaponId == LargeLadWeaponId.Pistol );

		if ( pistol is null )
			return false;

		ownerWantsNativeControl = true;
		timeSinceNativeSelectionRequest = 0.0f;
		Switch( pistol );
		return true;
	}

	public void HolsterNativeWeapon()
	{
		ownerWantsNativeControl = false;
		Switch( null, allowHolster: true );
	}

	internal void PrepareForRole( LargeLadRole role )
	{
		if ( !Networking.IsHost )
			return;

		ClearNativeItems();
		ownerWantsNativeControl = false;
	}

	internal void HandleDeath()
	{
		if ( !Networking.IsHost )
			return;

		ClearNativeItems();
		ownerWantsNativeControl = false;
	}

	private bool CanOwnNativePistol()
	{
		var player = Components.Get<LargeLadPlayer>();
		return player?.Role == LargeLadRole.SkinnyKid &&
			player.Health?.IsDead == false &&
			!player.IsEatBusy;
	}

	private void ClearNativeItems()
	{
		ForceHolster();

		foreach ( var item in Items.ToArray() )
			Remove( item );
	}
}

/// <summary>
/// Thin Large Lad policy layer over the native firearm. BaseCombatWeapon owns
/// input, fire gating, ammo, reload, aim, claims, models, and presentation.
/// </summary>
public sealed class LargeLadFirearm : BaseCombatWeapon
{
	private const float ReloadSoundVolume = 0.3f;

	// BaseCombatWeapon numbers the first native shot claim as sequence zero.
	private int lastHostClaimSequence = -1;
	private bool hasHostClaimSchedule;
	private float nextHostClaimTime;
	private bool reloadSoundLatched;
	private bool hasPlayedReloadSound;
	private TimeSince timeSinceReloadSound;

	[Property]
	public LargeLadWeaponId WeaponId { get; set; } = LargeLadWeaponId.Pistol;

	public int LastAuthoritativeShotSequence { get; private set; }

	protected override bool OnCanPickup( BaseInventoryComponent inventory )
	{
		return inventory is LargeLadNativeInventory &&
			CanBeOwnedBy( inventory ) &&
			base.OnCanPickup( inventory );
	}

	protected override bool OnAdding( BaseInventoryComponent inventory )
	{
		return inventory is LargeLadNativeInventory &&
			CanBeOwnedBy( inventory ) &&
			base.OnAdding( inventory );
	}

	protected override bool OnCanSwitchTo()
	{
		return IsValidHolderState( requirePlayingRound: false ) &&
			base.OnCanSwitchTo();
	}

	public override bool CanPrimaryAttack()
	{
		return IsValidHolderState( requirePlayingRound: true ) &&
			base.CanPrimaryAttack();
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		BindNativeModelAttachments( ViewModel );
		BindNativeModelAttachments( WorldModel );
		UpdateWorldModelTransform();

		if ( !IsProxy && IsHeld && IsActive )
			ApplyLocalPresentationMode( Scene?.Camera );
	}

	protected override void OnPreRender()
	{
		// Avoid expanding the entire animated skeleton into GameObjects just to
		// follow one hand bone; those proxies show as blue bone gizmos in play.
		UpdateWorldModelTransform();
	}

	protected override void CreateWorldModel()
	{
		base.CreateWorldModel();
		BindNativeModelAttachments( WorldModel );
		UpdateWorldModelTransform();
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

		var definition = LargeLadWeaponCatalog.Get( WeaponId );
		if ( string.IsNullOrWhiteSpace( definition.MuzzleAttachment ) )
			return;

		// Model attachments are empty transform GameObjects. Enabling their
		// hierarchy does not create renderers or bone debug geometry.
		renderer.CreateAttachments = true;
		var muzzle = renderer.GetAttachmentObject(
			definition.MuzzleAttachment );

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

		var definition = LargeLadWeaponCatalog.Get( WeaponId );
		var packageIdent = definition.ReloadSoundPackageIdent;
		if ( string.IsNullOrWhiteSpace( packageIdent ) )
			return;

		var sound = LargeLadWeaponPresentationAssets.GetSound( packageIdent );
		if ( sound is null && packageIdent == "drakefruit/pistol_reload" )
			sound = Cloud.SoundEvent( "drakefruit/pistol_reload" );
		else if ( sound is null && packageIdent == "drakefruit/rifle_reload" )
			sound = Cloud.SoundEvent( "drakefruit/rifle_reload" );
		if ( sound is null )
			return;

		var source = WeaponModel?.GameObject;
		if ( source is null || !source.IsValid )
			source = GameObject;

		var handle = source.PlaySound( sound, Vector3.Zero );
		if ( handle is null )
			return;

		// The owning viewmodel is listener-relative.
		handle.Volume = ReloadSoundVolume;
		handle.SpacialBlend = 0.0f;
		handle.DistanceAttenuation = false;
		handle.OcclusionEnabled = false;
		handle.ReverbEnabled = false;
	}

	private void UpdateWorldModelTransform()
	{
		var worldModel = WorldModel;
		var renderer = HolderRenderer;

		if ( worldModel is null || !worldModel.IsValid ||
			renderer is null || !renderer.IsValid )
		{
			return;
		}

		var attachment = renderer.GetAttachment( HoldBone, worldSpace: true );
		if ( attachment.HasValue )
		{
			ApplyWorldModelGripTransform( worldModel, attachment.Value );
			return;
		}

		if ( renderer.TryGetBoneTransform( HoldBone, out var boneTransform ) )
		{
			ApplyWorldModelGripTransform( worldModel, boneTransform );
		}
	}

	private void ApplyWorldModelGripTransform(
		GameObject worldModel,
		Transform holdTransform )
	{
		var definition = LargeLadWeaponCatalog.Get( WeaponId );
		var scale = System.MathF.Max(
			0.01f,
			definition.ThirdPersonModelScale );

		// Match the established presentation transform: hold_R is the body-side
		// socket, while the catalog offset corrects the world model's authored
		// origin into the palm. Keep this unparented so player role scale cannot
		// make the weapon microscopic or enormous.
		worldModel.WorldPosition = holdTransform.Position +
			holdTransform.Rotation * definition.ThirdPersonModelPosition;
		worldModel.WorldRotation = holdTransform.Rotation *
			definition.ThirdPersonModelRotation.ToRotation();
		worldModel.WorldScale = new Vector3( scale, scale, scale );
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

		foreach ( var pellet in pellets )
		{
			if ( !ValidatePellet( attacker, owner, pellet, ballistics.Range ) )
				return false;
		}

		LastAuthoritativeShotSequence = claim.Sequence;
		hasHostClaimSchedule = true;
		nextHostClaimTime = System.MathF.Max(
			Time.Now,
			nextHostClaimTime ) + PrimaryDelay;
		return base.OnValidateShotClaim( claim );
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
		float range )
	{
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
		if ( victim is not null &&
			pellet.Tags?.Has( LargeLadFirearmHitRules.HeadHitboxTag ) == true )
		{
			var hostRegion = ResolveHostHitRegion(
				victim,
				owner.EyePosition,
				hostAim.ShotDirection,
				range );

			if ( hostRegion != LargeLadHitRegion.Head )
				return false;
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
