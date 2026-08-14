using Sandbox;
using Sandbox.Citizen;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// Read-only weapon presentation for one synchronized player. The local owner
/// gets a camera-owned, non-networked first-person viewmodel; every peer builds
/// a separate third-person world model from authoritative equipped state.
/// Nothing in this component traces, consumes ammunition, reloads, or deals
/// damage.
/// </summary>
public sealed class LargeLadWeaponPresentation : Component
{
	private const float ViewmodelFullMoveSpeed = 250.0f;
	private const string ThirdPersonGripBone = "hold_R";
	private const float ThirdPersonGripDebugAxisLength = 8.0f;
	private const string BareArmsAnimationGraphPath =
		"models/first_person/v_first_person_arms_punching.vanmgrph";

	[Property, Group( "First Person - Motion" )]
	public float SprintFirePoseDuration { get; set; } = 0.22f;

	[Property, Group( "First Person - Fists" )]
	public Vector3 FistsPositionOffset { get; set; } =
		new( 8.0f, 0.0f, -10.0f );

	[Property, Group( "First Person - Fists" )]
	public float FistsModelScale { get; set; } = 0.78f;

	[Property, Group( "First Person - Fists" )]
	public float FistsAttackVariant { get; set; }

	[Property, Group( "First Person - Dodgeball" )]
	public Vector3 DodgeballPositionOffset { get; set; } =
		new( 10.0f, 0.0f, -14.0f );

	[Property, Group( "First Person - Dodgeball" )]
	public float DodgeballArmsScale { get; set; } = 0.72f;

	[Property, Group( "First Person - Dodgeball" )]
	public float DodgeballAttackVariant { get; set; } = 1.0f;

	[Property, Group( "First Person - Reload Audio" )]
	public SoundEvent PistolReloadSoundOverride { get; set; }

	[Property, Group( "First Person - Reload Audio" )]
	public SoundEvent SmgReloadSoundOverride { get; set; }

	[Property, Group( "First Person - Reload Audio" )]
	public bool ForcePistolCockingReload { get; set; }

	[Property, Group( "First Person - Reload Audio" )]
	public bool ForceSmgCockingReload { get; set; }

	[Property, Group( "Third Person - Development" )]
	public bool ShowThirdPersonGripDebug { get; set; }

	[Property, Group( "Third Person - Fists" )]
	public CitizenAnimationHelper.HoldTypes FistsHoldType { get; set; } =
		CitizenAnimationHelper.HoldTypes.Punch;

	[Property, Group( "Third Person - Fists" )]
	public CitizenAnimationHelper.Hand FistsHandedness { get; set; } =
		CitizenAnimationHelper.Hand.Both;

	[Property, Group( "Third Person - Fists" )]
	public float FistsHoldPose { get; set; }

	[Property, Group( "Third Person - Fists" )]
	public float FistsHandPose { get; set; }

	[Property, Group( "Third Person - Fists" )]
	public float FistsThirdPersonAttackVariant { get; set; }

	[Property, Group( "Third Person - Crowbar" )]
	public CitizenAnimationHelper.HoldTypes CrowbarHoldType { get; set; } =
		CitizenAnimationHelper.HoldTypes.Swing;

	[Property, Group( "Third Person - Crowbar" )]
	public CitizenAnimationHelper.Hand CrowbarHandedness { get; set; } =
		CitizenAnimationHelper.Hand.Right;

	[Property, Group( "Third Person - Crowbar" )]
	public float CrowbarHoldPose { get; set; }

	[Property, Group( "Third Person - Crowbar" )]
	public float CrowbarHandPose { get; set; }

	[Property, Group( "Third Person - Crowbar" )]
	public float CrowbarAttackVariant { get; set; }

	[Property, Group( "Third Person - Pistol" )]
	public CitizenAnimationHelper.HoldTypes PistolHoldType { get; set; } =
		CitizenAnimationHelper.HoldTypes.Pistol;

	[Property, Group( "Third Person - Pistol" )]
	public CitizenAnimationHelper.Hand PistolHandedness { get; set; } =
		CitizenAnimationHelper.Hand.Right;

	[Property, Group( "Third Person - Pistol" )]
	public float PistolHoldPose { get; set; }

	[Property, Group( "Third Person - Pistol" )]
	public float PistolHandPose { get; set; }

	[Property, Group( "Third Person - Pistol" )]
	public float PistolAttackVariant { get; set; }

	[Property, Group( "Third Person - SMG" )]
	public CitizenAnimationHelper.HoldTypes SmgHoldType { get; set; } =
		CitizenAnimationHelper.HoldTypes.Rifle;

	[Property, Group( "Third Person - SMG" )]
	public CitizenAnimationHelper.Hand SmgHandedness { get; set; } =
		CitizenAnimationHelper.Hand.Both;

	[Property, Group( "Third Person - SMG" )]
	public float SmgHoldPose { get; set; }

	[Property, Group( "Third Person - SMG" )]
	public float SmgHandPose { get; set; }

	[Property, Group( "Third Person - SMG" )]
	public float SmgAttackVariant { get; set; }

	[Property, Group( "Third Person - Dodgeball" )]
	public CitizenAnimationHelper.HoldTypes DodgeballHoldType { get; set; } =
		CitizenAnimationHelper.HoldTypes.HoldItem;

	[Property, Group( "Third Person - Dodgeball" )]
	public CitizenAnimationHelper.Hand DodgeballHandedness { get; set; } =
		CitizenAnimationHelper.Hand.Right;

	[Property, Group( "Third Person - Dodgeball" )]
	public float DodgeballHoldPose { get; set; }

	[Property, Group( "Third Person - Dodgeball" )]
	public float DodgeballHandPose { get; set; }

	[Property, Group( "Third Person - Dodgeball" )]
	public float DodgeballThirdPersonAttackVariant { get; set; }

	[Property, Group( "Third Person - Dodgeball" )]
	public float DodgeballAttackPoseDuration { get; set; } = 0.65f;

	private enum FirstPersonTransient
	{
		None,
		Draw,
		Fire,
		Reload
	}

	private enum ThirdPersonAttackKind
	{
		Current,
		Melee,
		Dodgeball
	}

	private readonly List<SoundHandle> activeSounds = new();
	private readonly List<GameObject> activeEffects = new();
	private readonly HashSet<string> warnedMissingModels = new();
	private readonly HashSet<string> failedSoundBindingKeys = new();
	private readonly HashSet<string> supportedFirstPersonParameters = new();
	private readonly HashSet<string> unsupportedFirstPersonParameters = new();

	private LargeLadPlayer cachedPlayer;
	private PlayerController cachedController;
	private LocalPlayerSetup cachedLocalPlayerSetup;
	private LargeLadGameManager cachedGameManager;

	private GameObject firstPersonRoot;
	private GameObject firstPersonWeaponObject;
	private GameObject firstPersonArmsObject;
	private GameObject firstPersonUtilityObject;
	private SkinnedModelRenderer firstPersonWeaponRenderer;
	private SkinnedModelRenderer firstPersonArmsRenderer;
	private ModelRenderer firstPersonUtilityRenderer;
	private GameObject boundCameraObject;
	private string appliedFirstPersonWeaponPath;
	private string appliedFirstPersonArmsPath;
	private string appliedFirstPersonUtilityModelPath;
	private string failedFirstPersonBindingKey;
	private string failedFirstPersonUtilityBindingKey;
	private bool firstPersonWeaponUsesArmsModel;
	private bool hasFirstPersonGroundState;
	private bool wasFirstPersonGrounded;
	private bool hasRecentFirstPersonShot;
	private TimeSince timeSinceFirstPersonShot;
	private SceneModel configuredFirstPersonAudioSceneModel;
	private string lastFirstPersonAnimationSoundName;
	private TimeSince timeSinceFirstPersonAnimationSound;
	private bool hasRecentThirdPersonDodgeballAttack;
	private TimeSince timeSinceThirdPersonDodgeballAttack;

	private GameObject thirdPersonGripRoot;
	private GameObject thirdPersonWeaponModelPivot;
	private GameObject thirdPersonWeaponObject;
	private ModelRenderer thirdPersonWeaponRenderer;
	private string appliedThirdPersonModelPath;
	private string failedThirdPersonBindingKey;
	private string failedThirdPersonAttachmentBindingKey;
	private string failedMuzzleBindingKey;

	private LargeLadWeaponPresentationState previousState;
	private bool hasPreviousState;
	private int lastPresentedShotSequence;
	private int lastPresentedEmptySequence;
	private FirstPersonTransient firstPersonTransient;
	private bool firstPersonTransientUsesSequence;
	private int presentationRevision;
	private bool nativePresentationSuppressed;

	protected override void OnAwake()
	{
		ResolveCachedReferences();
	}

	protected override async Task OnLoad()
	{
		// Compile-time Cloud.Model references ship these assets in published
		// builds. Editor clients and late joiners can still arrive before their
		// local cloud cache is mounted, so explicitly await the same packages
		// before this component is allowed to build presentation objects.
		await LargeLadWeaponPresentationAssets.EnsureLoadedAsync();
	}

	protected override void OnStart()
	{
		ResolveCachedReferences();
	}

	protected override void OnEnabled()
	{
		base.OnEnabled();
		ResolveCachedReferences();
	}

	protected override void OnDisabled()
	{
		ResetPresentation( restoreBody: true );
		base.OnDisabled();
	}

	protected override void OnDestroy()
	{
		ResetPresentation( restoreBody: true );
		base.OnDestroy();
	}

	protected override void OnUpdate()
	{
		ResolveCachedReferences();

		// The active native combat item owns both models and the Citizen hold
		// pose. Tear down legacy objects once, then stay completely out of its
		// render path.
		if ( cachedPlayer?.NativeInventory?.HasNativeInputControl == true )
		{
			if ( !nativePresentationSuppressed )
				ResetPresentation( restoreBody: true );

			nativePresentationSuppressed = true;
			return;
		}

		nativePresentationSuppressed = false;
		var state = CaptureState( out var ownedCamera );
		var currentView =
			LargeLadWeaponPresentationRules.ResolveView( state );

		if ( !hasPreviousState )
		{
			InitializePresentation( state, currentView, ownedCamera );
			return;
		}

		var cameraChanged = currentView ==
			LargeLadWeaponPresentationView.FirstPerson &&
			boundCameraObject != ownedCamera?.GameObject;
		var actions = LargeLadWeaponPresentationRules.ResolveTransition(
			previousState,
			state,
			cameraChanged );

		if ( actions.HasFlag(
			LargeLadWeaponPresentationAction.Interrupt ) )
		{
			InterruptPresentation();
		}
		else if ( cameraChanged )
		{
			// Camera-owned geometry is rebuilt below. Never leave an overlay
			// muzzle effect positioned relative to the retired camera.
			DestroyPresentationEffects();
		}

		UpdateVisibilityAndModels( state, currentView, ownedCamera );
		ApplyThirdPersonPose( state, currentView );

		var definition = GetAnimationDefinition( state );
		var startsReload = actions.HasFlag(
			LargeLadWeaponPresentationAction.StartReload );

		if ( startsReload )
		{
			PresentReloadStarted( definition, currentView, playAudio: true );
		}
		else if ( actions.HasFlag(
			LargeLadWeaponPresentationAction.Draw ) )
		{
			PresentDraw( definition, currentView, playAudio: true );
		}
		else if ( actions.HasFlag(
			LargeLadWeaponPresentationAction.FinishReload ) )
		{
			PresentIdle( definition );
		}

		if ( cameraChanged &&
			!actions.HasFlag( LargeLadWeaponPresentationAction.Draw ) )
		{
			RestoreCurrentFirstPersonAnimation( state, definition );
		}

		ConsumeAuthoritativeFireSignals( state, currentView, definition );
		TickFirstPersonAnimation( state, definition );
		CleanupFinishedPresentationObjects();

		previousState = state;
	}

	protected override void OnPreRender()
	{
		if ( nativePresentationSuppressed ||
			cachedPlayer?.NativeInventory?.HasNativeInputControl == true ||
			!hasPreviousState )
			return;

		var view = LargeLadWeaponPresentationRules.ResolveView(
			previousState );
		var definition = GetAnimationDefinition( previousState );

		if ( view == LargeLadWeaponPresentationView.FirstPerson )
		{
			UpdateFirstPersonViewmodelTransform( previousState, definition );
			UpdateFirstPersonUtilityTransform( previousState );
			ConfigureFirstPersonAnimationAudio();
		}
		else if ( view == LargeLadWeaponPresentationView.ThirdPerson )
		{
			// Citizen bones are finalized immediately before rendering. Attaching
			// here keeps an unscaled world weapon on the exact rendered hand pose
			// instead of one update behind it, which was visible as low-rate jitter.
			UpdateThirdPersonItemTransform( previousState );
		}
	}

	/// <summary>
	/// Immediate owner-side melee feedback. The host still exclusively decides
	/// whether the swing hits and broadcasts the third-person attack gesture.
	/// </summary>
	internal void TriggerPredictedSwing()
	{
		var state = CaptureState( out _ );
		var view = LargeLadWeaponPresentationRules.ResolveView( state );

		if ( state.Weapon != LargeLadWeaponId.Melee ||
			view == LargeLadWeaponPresentationView.Hidden )
		{
			return;
		}

		if ( view == LargeLadWeaponPresentationView.FirstPerson )
		{
			var definition = LargeLadWeaponCatalog.Get( state.Weapon );
			ApplyFirstPersonAttackVariant( state );
			PlayFirstPersonAnimation(
				definition,
				definition.FireAnimation,
				looping: false,
				FirstPersonTransient.Fire );
			return;
		}

		TriggerThirdPersonAttack( ThirdPersonAttackKind.Melee, state );
	}

	internal void BroadcastSwing()
	{
		BroadcastMeleeSwingAnimation();
	}

	internal void TriggerPredictedUtilityUse()
	{
		var state = CaptureState( out _ );
		var view = LargeLadWeaponPresentationRules.ResolveView( state );
		if ( !LargeLadWeaponPresentationRules.IsDodgeballSelected( state ) ||
			view == LargeLadWeaponPresentationView.Hidden )
		{
			return;
		}

		if ( view == LargeLadWeaponPresentationView.ThirdPerson )
		{
			TriggerThirdPersonAttack(
				ThirdPersonAttackKind.Dodgeball,
				state );
			return;
		}

		var definition = GetAnimationDefinition( state );
		ApplyFirstPersonAttackVariant( state );
		PlayFirstPersonAnimation(
			definition,
			definition.FireAnimation,
			looping: false,
			FirstPersonTransient.Fire );
	}

	internal void BroadcastUtilityUse()
	{
		BroadcastUtilityUseAnimation();
	}

	[Rpc.Broadcast]
	private void BroadcastMeleeSwingAnimation()
	{
		// The owner already played its camera-appropriate predicted gesture.
		if ( !IsProxy )
			return;

		var state = CaptureState( out _ );
		if ( state.Weapon == LargeLadWeaponId.Melee &&
			LargeLadWeaponPresentationRules.ResolveView( state ) ==
				LargeLadWeaponPresentationView.ThirdPerson )
		{
			TriggerThirdPersonAttack(
				ThirdPersonAttackKind.Melee,
				state );
		}
	}

	[Rpc.Broadcast]
	private void BroadcastUtilityUseAnimation()
	{
		// The owner already played its camera-local predicted gesture.
		if ( !IsProxy )
			return;

		// The accepted throw clears the utility selection immediately. Do not
		// require the old selection to survive until this cosmetic RPC arrives.
		if ( cachedPlayer?.Role == LargeLadRole.SkinnyKid &&
			cachedPlayer.Health?.IsDead == false )
		{
			TriggerThirdPersonAttack(
				ThirdPersonAttackKind.Dodgeball,
				default );
		}
	}

	private void InitializePresentation(
		LargeLadWeaponPresentationState state,
		LargeLadWeaponPresentationView view,
		CameraComponent ownedCamera )
	{
		hasPreviousState = true;
		previousState = state;
		lastPresentedShotSequence =
			cachedPlayer?.PrototypeWeapon?.PresentationShotSequence ?? 0;
		lastPresentedEmptySequence =
			cachedPlayer?.PrototypeWeapon?.PresentationEmptySequence ?? 0;

		UpdateVisibilityAndModels( state, view, ownedCamera );
		ApplyThirdPersonPose( state, view );

		if ( view == LargeLadWeaponPresentationView.Hidden )
			return;

		var definition = GetAnimationDefinition( state );
		if ( state.IsReloading )
			PresentReloadStarted( definition, view, playAudio: false );
		else
			PresentDraw( definition, view, playAudio: false );
	}

	private LargeLadWeaponPresentationState CaptureState(
		out CameraComponent ownedCamera )
	{
		var player = cachedPlayer;
		var gameManager = GetGameManager();
		ownedCamera = GetOwnedCamera();
		var selection = player?.ActiveInventorySelection ??
			LargeLadInventorySelection.None;
		var role = player?.Role ?? LargeLadRole.Unassigned;

		return new LargeLadWeaponPresentationState
		{
			Role = role,
			RoundPhase = gameManager?.Phase ??
				LargeLadRoundPhase.WaitingForPlayers,
			Weapon = LargeLadWeaponPresentationRules.ResolvePresentedWeapon(
				role,
				selection ),
			Selection = selection,
			IsDead = player?.Health?.IsDead != false,
			IsLocalOwner = !IsProxy,
			HasOwnedCamera = ownedCamera is not null,
			IsThirdPersonCamera = cachedController?.ThirdPerson != false,
			IsReloading = player?.NativeInventory?.ActiveFirearm?
				.IsReloading == true
		};
	}

	private CameraComponent GetOwnedCamera()
	{
		if ( IsProxy )
			return null;

		var configuredCamera = cachedLocalPlayerSetup?.PlayerCamera;
		var configuredCameraObject = configuredCamera?.GameObject;
		if ( configuredCamera is not null &&
			configuredCamera.IsValid &&
			configuredCamera.Enabled &&
			configuredCameraObject is not null &&
			configuredCameraObject.IsValid )
		{
			return configuredCamera;
		}

		// Retain a fallback for scenes that construct an owned camera without
		// LocalPlayerSetup, but never accept another player's scene camera.
		var camera = Scene?.Camera;
		var cameraObject = camera?.GameObject;

		if ( cameraObject is null || !cameraObject.IsValid )
			return null;

		return cameraObject == GameObject ||
			cameraObject.IsDescendant( GameObject )
			? camera
			: null;
	}

	private void UpdateVisibilityAndModels(
		LargeLadWeaponPresentationState state,
		LargeLadWeaponPresentationView view,
		CameraComponent ownedCamera )
	{
		SetBodyVisible( view != LargeLadWeaponPresentationView.FirstPerson );

		if ( view == LargeLadWeaponPresentationView.FirstPerson )
		{
			EnsureFirstPersonViewmodel( state, ownedCamera );
			SetObjectEnabled( firstPersonRoot, true );
			DestroyThirdPersonWeapon();
			return;
		}

		DestroyFirstPersonViewmodel();

		if ( view == LargeLadWeaponPresentationView.ThirdPerson &&
			ShouldCreateThirdPersonItem( state ) )
		{
			EnsureThirdPersonItem( state );
			SetObjectEnabled( thirdPersonWeaponObject, true );
			return;
		}

		DestroyThirdPersonWeapon();
	}

	private void EnsureFirstPersonViewmodel(
		LargeLadWeaponPresentationState state,
		CameraComponent camera )
	{
		if ( camera?.GameObject is null )
		{
			DestroyFirstPersonViewmodel();
			return;
		}

		var definition = GetAnimationDefinition( state );
		ResolveFirstPersonModels(
			state,
			definition,
			out var weaponPath,
			out var armsPath );

		if ( string.IsNullOrWhiteSpace( weaponPath ) )
		{
			DestroyFirstPersonViewmodel();
			return;
		}

		var bindingKey =
			$"{presentationRevision}:{camera.GameObject.Id}:" +
			$"{weaponPath}:{armsPath}";

		var mustRebuild = firstPersonRoot is null ||
			!firstPersonRoot.IsValid ||
			boundCameraObject != camera.GameObject ||
			appliedFirstPersonWeaponPath != weaponPath ||
			appliedFirstPersonArmsPath != armsPath;

		if ( mustRebuild )
		{
			if ( failedFirstPersonBindingKey == bindingKey )
				return;

			DestroyFirstPersonViewmodel();
			if ( !CreateFirstPersonViewmodel(
				camera.GameObject,
				weaponPath,
				armsPath,
				definition,
				LoadFirstPersonWeaponModel( definition, weaponPath ),
				LoadFirstPersonArmsModel( definition ) ) )
			{
				failedFirstPersonBindingKey = bindingKey;
				return;
			}

			failedFirstPersonBindingKey = null;
		}

		if ( firstPersonRoot is null || !firstPersonRoot.IsValid )
			return;

		firstPersonRoot.LocalTransform = global::Transform.Zero;
		UpdateFirstPersonViewmodelTransform( state, definition );
		EnsureFirstPersonUtility( state );
		UpdateFirstPersonUtilityTransform( state );

		if ( firstPersonArmsRenderer is not null )
		{
			firstPersonArmsRenderer.UseAnimGraph = false;
			firstPersonArmsRenderer.BoneMergeTarget =
				firstPersonWeaponRenderer;
			firstPersonArmsRenderer.Tint =
				cachedPlayer?.BodyRenderer?.Tint ?? Color.White;
		}
		else if ( firstPersonWeaponUsesArmsModel &&
			firstPersonWeaponRenderer is not null )
		{
			firstPersonWeaponRenderer.Tint =
				cachedPlayer?.BodyRenderer?.Tint ?? Color.White;
		}
	}

	private void UpdateFirstPersonViewmodelTransform(
		LargeLadWeaponPresentationState state,
		LargeLadWeaponDefinition definition )
	{
		if ( firstPersonRoot is null || !firstPersonRoot.IsValid ||
			firstPersonWeaponObject is null ||
			!firstPersonWeaponObject.IsValid ||
			firstPersonWeaponRenderer is null )
		{
			return;
		}

		var isBareArms = firstPersonWeaponUsesArmsModel;
		var isDodgeball = LargeLadWeaponPresentationRules
			.IsDodgeballSelected( state );
		var positionOffset = definition.FirstPersonPositionOffset;
		var rotationOffset = definition.FirstPersonRotationOffset;
		var scaleMultiplier = 1.0f;
		if ( isBareArms )
		{
			positionOffset += isDodgeball
				? DodgeballPositionOffset
				: FistsPositionOffset;
			scaleMultiplier = isDodgeball
				? DodgeballArmsScale
				: FistsModelScale;
		}
		var scale = System.MathF.Max(
			0.01f,
			definition.FirstPersonModelScale * scaleMultiplier );
		// This is intentionally camera-local. Sampling the animated camera bone
		// and feeding its previous world pose back into the model transform made
		// the correction accumulate while turning, which let the entire rig drift
		// and rotate away from its owner.
		firstPersonWeaponObject.LocalPosition =
			positionOffset;
		firstPersonWeaponObject.LocalRotation =
			rotationOffset.ToRotation();
		firstPersonWeaponObject.LocalScale =
			new Vector3( scale, scale, scale );
		if ( firstPersonArmsObject is not null &&
			firstPersonArmsObject.IsValid )
		{
			firstPersonArmsObject.LocalPosition =
				positionOffset;
			firstPersonArmsObject.LocalRotation =
				rotationOffset.ToRotation();
			firstPersonArmsObject.LocalScale =
				new Vector3( scale, scale, scale );
		}
	}

	private static Model LoadFirstPersonArmsModel(
		LargeLadWeaponDefinition definition )
	{
		if ( definition.FirstPersonModelIncludesArms ||
			string.IsNullOrWhiteSpace( definition.FirstPersonArmsModelPath ) )
		{
			return null;
		}

		// Keep this as a direct literal Cloud.Model call. The compiler discovers
		// direct calls and distributes the package reference to every client;
		// hiding the call in a catalog delegate leaves the reference table empty.
		if ( definition.FirstPersonArmsPackageIdent ==
			"facepunch/v_first_person_arms_human" )
		{
			var mounted = LargeLadWeaponPresentationAssets.GetModel(
				"facepunch/v_first_person_arms_human" );
			if ( mounted is not null && !mounted.IsError )
				return mounted;

			// Joining clients have already mounted compiler-declared server
			// packages before their player starts. Resolve the known resource path
			// directly; package-primary lookup was intermittently returning the
			// error model on remote clients despite a completed installation.
			var pathModel = Model.Load( definition.FirstPersonArmsModelPath );
			if ( pathModel is not null && !pathModel.IsError )
				return pathModel;

			return Cloud.Model( "facepunch/v_first_person_arms_human" );
		}

		return Model.Load( definition.FirstPersonArmsModelPath );
	}

	private static Model LoadFirstPersonWeaponModel(
		LargeLadWeaponDefinition definition,
		string weaponPath )
	{
		if ( weaponPath == definition.FirstPersonArmsModelPath )
			return LoadFirstPersonArmsModel( definition );

		// Like the arms dependency, this must remain a direct literal call so
		// the compiler ships the cloud package to joining clients.
		if ( definition.FirstPersonModelPackageIdent ==
			"facepunch/v_crowbar" )
		{
			var mounted = LargeLadWeaponPresentationAssets.GetModel(
				"facepunch/v_crowbar" );
			if ( mounted is not null && !mounted.IsError )
				return mounted;
			var pathModel = Model.Load( weaponPath );
			if ( pathModel is not null && !pathModel.IsError )
				return pathModel;

			return Cloud.Model( "facepunch/v_crowbar" );
		}
		if ( definition.FirstPersonModelPackageIdent ==
			"facepunch/v_usp" )
		{
			var mounted = LargeLadWeaponPresentationAssets.GetModel(
				"facepunch/v_usp" );
			if ( mounted is not null && !mounted.IsError )
				return mounted;
			var pathModel = Model.Load( weaponPath );
			if ( pathModel is not null && !pathModel.IsError )
				return pathModel;

			return Cloud.Model( "facepunch/v_usp" );
		}
		if ( definition.FirstPersonModelPackageIdent ==
			"facepunch/v_mp5" )
		{
			var mounted = LargeLadWeaponPresentationAssets.GetModel(
				"facepunch/v_mp5" );
			if ( mounted is not null && !mounted.IsError )
				return mounted;
			var pathModel = Model.Load( weaponPath );
			if ( pathModel is not null && !pathModel.IsError )
				return pathModel;

			return Cloud.Model( "facepunch/v_mp5" );
		}

		return Model.Load( weaponPath );
	}

	private bool CreateFirstPersonViewmodel(
		GameObject cameraObject,
		string weaponPath,
		string armsPath,
		LargeLadWeaponDefinition definition,
		Model definitionWeaponModel,
		Model definitionArmsModel )
	{
		var weaponUsesArmsModel = definitionArmsModel is not null &&
			(string.IsNullOrWhiteSpace( armsPath ) ||
				weaponPath == armsPath);
		var weaponModel = weaponUsesArmsModel
			? definitionArmsModel
			: definitionWeaponModel;
		if ( weaponModel is null || weaponModel.IsError )
		{
			WarnMissingModelOnce( "first-person model", weaponPath );
			return false;
		}

		firstPersonRoot = new GameObject(
			cameraObject,
			true,
			"First Person Weapon Viewmodel (Local)" )
		{
			NetworkMode = NetworkMode.Never
		};
		firstPersonWeaponObject = new GameObject(
			firstPersonRoot,
			true,
			"First Person Weapon" )
		{
			NetworkMode = NetworkMode.Never
		};
		firstPersonWeaponRenderer =
			firstPersonWeaponObject.Components
				.Create<SkinnedModelRenderer>();
		firstPersonWeaponRenderer.Model = weaponModel;
		firstPersonWeaponUsesArmsModel = weaponUsesArmsModel;
		// Assign the punching graph explicitly for bare arms. Relying on the model's
		// default graph animated visually, but left AnimationGraph null here, so
		// parameter introspection rejected b_attack and fists never swung.
		var animationGraphPath = weaponUsesArmsModel
			? BareArmsAnimationGraphPath
			: definition.FirstPersonAnimationGraphPath;
		if ( !string.IsNullOrWhiteSpace( animationGraphPath ) )
		{
			var animationGraph = AnimationGraph.Load( animationGraphPath );
			if ( animationGraph is not null && !animationGraph.IsError )
			{
				firstPersonWeaponRenderer.AnimationGraph = animationGraph;
				firstPersonWeaponRenderer.UseAnimGraph = true;
			}
			else
			{
				WarnMissingModelOnce(
					"first-person animation graph",
					animationGraphPath );
				firstPersonWeaponRenderer.UseAnimGraph = false;
			}
		}
		else
		{
			firstPersonWeaponRenderer.UseAnimGraph = false;
		}
		firstPersonWeaponRenderer.CreateAttachments = true;
		ConfigureFirstPersonRenderer( firstPersonWeaponRenderer );
		TrySetFirstPersonParameter(
			"skeleton",
			definition.FirstPersonSkeleton );
		ConfigureFirstPersonAnimationAudio();

		if ( !string.IsNullOrWhiteSpace( armsPath ) )
		{
			var armsModel = definitionArmsModel ?? Model.Load( armsPath );
			if ( armsModel is not null && !armsModel.IsError )
			{
				firstPersonArmsObject = new GameObject(
					firstPersonRoot,
					true,
					"First Person Arms" )
				{
					NetworkMode = NetworkMode.Never
				};
				// Bone merging is evaluated in world space. Keep the arms as a
				// sibling at the weapon origin so the parent's viewmodel transform
				// is not applied a second time to the merged skeleton.
				firstPersonArmsObject.WorldTransform =
					firstPersonWeaponObject.WorldTransform;
				firstPersonArmsRenderer =
					firstPersonArmsObject.Components
						.Create<SkinnedModelRenderer>();
				firstPersonArmsRenderer.Model = armsModel;
				firstPersonArmsRenderer.UseAnimGraph = false;
				firstPersonArmsRenderer.BoneMergeTarget =
					firstPersonWeaponRenderer;
				ConfigureFirstPersonRenderer( firstPersonArmsRenderer );
			}
			else
			{
				WarnMissingModelOnce(
					"purpose-built first-person arms",
					armsPath );
			}
		}

		boundCameraObject = cameraObject;
		appliedFirstPersonWeaponPath = weaponPath;
		appliedFirstPersonArmsPath = armsPath;
		return true;
	}

	private static void ConfigureFirstPersonRenderer( Renderer renderer )
	{
		// Overlay changes draw order, but it does not opt the renderer into the
		// regular game pass. These objects are already owner-only because they
		// are non-networked camera children and only exist in first person.
		renderer.RenderOptions.Game = true;
		renderer.RenderOptions.Overlay = true;
		renderer.RenderOptions.Bloom = false;
		renderer.RenderOptions.AfterUI = false;
	}

	private void ConfigureFirstPersonAnimationAudio()
	{
		var sceneModel = firstPersonWeaponRenderer?.SceneModel;
		if ( sceneModel is null ||
			configuredFirstPersonAudioSceneModel == sceneModel )
		{
			return;
		}

		// SkinnedModelRenderer's stock handler plays animation sounds at the event's
		// sampled world position. Replacing the scene callback lets the owner hear
		// the same authored timing from the moving camera/viewmodel without also
		// allowing the stock callback to emit a second, reverberant world sound.
		sceneModel.OnSoundEvent = HandleFirstPersonAnimationSound;
		configuredFirstPersonAudioSceneModel = sceneModel;
	}

	private void HandleFirstPersonAnimationSound(
		SceneModel.SoundEvent soundEvent )
	{
		if ( string.IsNullOrWhiteSpace( soundEvent.Name ) )
			return;

		var state = previousState;
		var reloadOverride = GetReloadSoundOverride( state.Weapon );
		if ( state.IsReloading && reloadOverride is not null )
			return;

		// Fire sounds are already sourced from the viewmodel when the accepted-shot
		// signal arrives. Suppress an authored gunshot event in the same short
		// window so a graph cannot layer a second report over it.
		if ( hasRecentFirstPersonShot &&
			timeSinceFirstPersonShot < 0.5f )
		{
			return;
		}

		// Some graphs contain the same sound marker on overlapping transition
		// branches. Treat only near-simultaneous identical markers as duplicates;
		// magazine, bolt, and cloth events with distinct names remain intact.
		if ( soundEvent.Name == lastFirstPersonAnimationSoundName &&
			timeSinceFirstPersonAnimationSound < 0.075f )
		{
			return;
		}

		var sound = ResourceLibrary.Get<SoundEvent>( soundEvent.Name );
		if ( sound is null )
		{
			WarnMissingModelOnce(
				"first-person animation sound",
				soundEvent.Name );
			return;
		}

		lastFirstPersonAnimationSoundName = soundEvent.Name;
		timeSinceFirstPersonAnimationSound = 0.0f;
		PlayPresentationSound(
			sound,
			LargeLadWeaponPresentationView.FirstPerson );
	}

	private void EnsureFirstPersonUtility(
		LargeLadWeaponPresentationState state )
	{
		if ( !LargeLadWeaponPresentationRules.IsDodgeballSelected( state ) ||
			!LargeLadUtilityPresentationCatalog.TryGet(
				state.Selection.Utility,
				out var definition ) ||
			firstPersonRoot is null ||
			!firstPersonRoot.IsValid )
		{
			DestroyFirstPersonUtility();
			return;
		}

		var path = definition.FirstPersonHeldModelPath;
		var bindingKey = $"{presentationRevision}:{path}";
		if ( firstPersonUtilityObject is not null &&
			firstPersonUtilityObject.IsValid &&
			appliedFirstPersonUtilityModelPath == path )
		{
			return;
		}

		if ( failedFirstPersonUtilityBindingKey == bindingKey )
			return;

		DestroyFirstPersonUtility();
		var model = string.IsNullOrWhiteSpace( path )
			? null
			: Model.Load( path );
		if ( model is null || model.IsError )
		{
			WarnMissingModelOnce( "first-person utility model", path );
			failedFirstPersonUtilityBindingKey = bindingKey;
			return;
		}

		firstPersonUtilityObject = new GameObject(
			firstPersonRoot,
			true,
			"First Person Dodgeball" )
		{
			NetworkMode = NetworkMode.Never
		};
		firstPersonUtilityRenderer =
			firstPersonUtilityObject.Components.Create<ModelRenderer>();
		firstPersonUtilityRenderer.Model = model;
		firstPersonUtilityRenderer.Tint =
			LargeLadUtilityRules.DodgeballColor;
		ConfigureFirstPersonRenderer( firstPersonUtilityRenderer );
		appliedFirstPersonUtilityModelPath = path;
		failedFirstPersonUtilityBindingKey = null;
	}

	private void UpdateFirstPersonUtilityTransform(
		LargeLadWeaponPresentationState state )
	{
		if ( !LargeLadWeaponPresentationRules.IsDodgeballSelected( state ) ||
			!LargeLadUtilityPresentationCatalog.TryGet(
				state.Selection.Utility,
				out var definition ) ||
			firstPersonWeaponRenderer is null ||
			firstPersonUtilityObject is null ||
			!firstPersonUtilityObject.IsValid )
		{
			return;
		}

		var bindingKey =
			$"{presentationRevision}:{definition.FirstPersonHeldModelPath}:" +
			definition.FirstPersonHeldAttachmentBone;
		if ( string.IsNullOrWhiteSpace(
				definition.FirstPersonHeldAttachmentBone ) ||
			!firstPersonWeaponRenderer.TryGetBoneTransform(
				definition.FirstPersonHeldAttachmentBone,
				out var handTransform ) )
		{
			if ( failedFirstPersonUtilityBindingKey != bindingKey )
			{
				failedFirstPersonUtilityBindingKey = bindingKey;
				WarnMissingModelOnce(
					"first-person utility attachment bone",
					definition.FirstPersonHeldAttachmentBone );
			}
			SetObjectEnabled( firstPersonUtilityObject, false );
			return;
		}

		failedFirstPersonUtilityBindingKey = null;
		SetObjectEnabled( firstPersonUtilityObject, true );
		var scale = System.MathF.Max(
			0.01f,
			definition.FirstPersonHeldModelScale );
		firstPersonUtilityObject.WorldPosition = handTransform.PointToWorld(
			definition.FirstPersonHeldPositionOffset );
		firstPersonUtilityObject.WorldRotation = handTransform.Rotation *
			definition.FirstPersonHeldRotationOffset.ToRotation();
		firstPersonUtilityObject.WorldScale =
			new Vector3( scale, scale, scale );
	}

	private static void ResolveFirstPersonModels(
		LargeLadWeaponPresentationState state,
		LargeLadWeaponDefinition definition,
		out string weaponPath,
		out string armsPath )
	{
		if ( LargeLadWeaponPresentationRules.IsDodgeballSelected( state ) &&
			LargeLadUtilityPresentationCatalog.TryGet(
				state.Selection.Utility,
				out var utility ) )
		{
			weaponPath = utility.FirstPersonArmsModelPath;
			armsPath = null;
			return;
		}

		var usesBareRoleArms = state.Weapon == LargeLadWeaponId.Melee &&
			state.Role is LargeLadRole.LargeLad or LargeLadRole.Minion;

		if ( usesBareRoleArms )
		{
			weaponPath = definition.FirstPersonArmsModelPath;
			armsPath = null;
			return;
		}

		weaponPath = definition.FirstPersonModelPath;
		armsPath = definition.FirstPersonModelIncludesArms
			? null
			: definition.FirstPersonArmsModelPath;
	}

	private void EnsureThirdPersonItem(
		LargeLadWeaponPresentationState state )
	{
		LargeLadUtilityPresentationDefinition utilityDefinition = null;
		var isDodgeball =
			LargeLadWeaponPresentationRules.IsDodgeballSelected( state ) &&
			LargeLadUtilityPresentationCatalog.TryGet(
				state.Selection.Utility,
				out utilityDefinition );
		var weaponDefinition = isDodgeball
			? null
			: LargeLadWeaponCatalog.Get( state.Weapon );
		var path = isDodgeball
			? utilityDefinition.ThirdPersonWorldModelPath
			: weaponDefinition.ThirdPersonWorldModelPath;
		var bindingKey = $"{presentationRevision}:{path}";

		if ( string.IsNullOrWhiteSpace( path ) )
		{
			DestroyThirdPersonWeapon();
			return;
		}

		if ( thirdPersonGripRoot is not null &&
			thirdPersonGripRoot.IsValid &&
			thirdPersonWeaponModelPivot is not null &&
			thirdPersonWeaponModelPivot.IsValid &&
			thirdPersonWeaponObject is not null &&
			thirdPersonWeaponObject.IsValid &&
			appliedThirdPersonModelPath == path )
		{
			return;
		}

		if ( failedThirdPersonBindingKey == bindingKey )
			return;

		DestroyThirdPersonWeapon();
		var model = isDodgeball
			? Model.Load( path )
			: LoadThirdPersonWeaponModel( weaponDefinition );
		if ( model is null || model.IsError )
		{
			WarnMissingModelOnce( "third-person held model", path );
			failedThirdPersonBindingKey = bindingKey;
			return;
		}
		failedThirdPersonBindingKey = null;

		// Keep world weapons out of the role-scaled player hierarchy. Parenting
		// here multiplied the definition scale by Skinny Kid/Large Lad body
		// scale, so hosts saw microscopic weapons while proxies saw huge ones.
		thirdPersonGripRoot = new GameObject(
			true,
			"Third Person Grip Root (Local Presentation)" )
		{
			NetworkMode = NetworkMode.Never
		};
		thirdPersonWeaponModelPivot = new GameObject(
			thirdPersonGripRoot,
			true,
			"Third Person Weapon Model Pivot" )
		{
			NetworkMode = NetworkMode.Never
		};
		thirdPersonWeaponObject = new GameObject(
			thirdPersonWeaponModelPivot,
			true,
			"Third Person Weapon Model" )
		{
			NetworkMode = NetworkMode.Never
		};
		thirdPersonWeaponRenderer =
			thirdPersonWeaponObject.Components.Create<ModelRenderer>();
		thirdPersonWeaponRenderer.Model = model;
		thirdPersonWeaponRenderer.Tint = isDodgeball
			? LargeLadUtilityRules.DodgeballColor
			: Color.White;
		thirdPersonWeaponRenderer.CreateAttachments = true;
		thirdPersonWeaponRenderer.RenderOptions.Game = true;
		thirdPersonWeaponRenderer.RenderOptions.Overlay = false;
		thirdPersonWeaponObject.Enabled = true;
		appliedThirdPersonModelPath = path;
	}

	private static Model LoadThirdPersonWeaponModel(
		LargeLadWeaponDefinition definition )
	{
		// Direct literal calls are discovered by the compiler and make the world
		// model packages available to every observer, not just pickup owners.
		if ( definition.ThirdPersonWorldModelPackageIdent ==
			"facepunch/w_crowbar" )
		{
			var mounted = LargeLadWeaponPresentationAssets.GetModel(
				"facepunch/w_crowbar" );
			if ( mounted is not null && !mounted.IsError )
				return mounted;
			var pathModel = Model.Load(
				definition.ThirdPersonWorldModelPath );
			if ( pathModel is not null && !pathModel.IsError )
				return pathModel;

			return Cloud.Model( "facepunch/w_crowbar" );
		}
		if ( definition.ThirdPersonWorldModelPackageIdent ==
			"facepunch/w_usp" )
		{
			var mounted = LargeLadWeaponPresentationAssets.GetModel(
				"facepunch/w_usp" );
			if ( mounted is not null && !mounted.IsError )
				return mounted;
			var pathModel = Model.Load(
				definition.ThirdPersonWorldModelPath );
			if ( pathModel is not null && !pathModel.IsError )
				return pathModel;

			return Cloud.Model( "facepunch/w_usp" );
		}
		if ( definition.ThirdPersonWorldModelPackageIdent ==
			"facepunch/w_mp5" )
		{
			var mounted = LargeLadWeaponPresentationAssets.GetModel(
				"facepunch/w_mp5" );
			if ( mounted is not null && !mounted.IsError )
				return mounted;
			var pathModel = Model.Load(
				definition.ThirdPersonWorldModelPath );
			if ( pathModel is not null && !pathModel.IsError )
				return pathModel;

			return Cloud.Model( "facepunch/w_mp5" );
		}

		return Model.Load( definition.ThirdPersonWorldModelPath );
	}

	private void UpdateThirdPersonItemTransform(
		LargeLadWeaponPresentationState state )
	{
		var bodyRenderer = cachedPlayer?.BodyRenderer;
		LargeLadUtilityPresentationDefinition utilityDefinition = null;
		var isDodgeball =
			LargeLadWeaponPresentationRules.IsDodgeballSelected( state ) &&
			LargeLadUtilityPresentationCatalog.TryGet(
				state.Selection.Utility,
				out utilityDefinition );
		var weaponDefinition = isDodgeball
			? null
			: LargeLadWeaponCatalog.Get( state.Weapon );
		var modelPath = isDodgeball
			? utilityDefinition.ThirdPersonWorldModelPath
			: weaponDefinition.ThirdPersonWorldModelPath;
		var modelPosition = isDodgeball
			? utilityDefinition.ThirdPersonModelPosition
			: weaponDefinition.ThirdPersonModelPosition;
		var modelRotation = isDodgeball
			? utilityDefinition.ThirdPersonModelRotation
			: weaponDefinition.ThirdPersonModelRotation;
		var modelScale = isDodgeball
			? utilityDefinition.ThirdPersonModelScale
			: weaponDefinition.ThirdPersonModelScale;

		if ( thirdPersonGripRoot is null ||
			!thirdPersonGripRoot.IsValid ||
			thirdPersonWeaponModelPivot is null ||
			!thirdPersonWeaponModelPivot.IsValid ||
			thirdPersonWeaponObject is null ||
			!thirdPersonWeaponObject.IsValid ||
			bodyRenderer is null )
		{
			SetObjectEnabled( thirdPersonWeaponObject, false );
			return;
		}

		var bindingKey =
			$"{presentationRevision}:{thirdPersonGripRoot.Id}:" +
			$"{bodyRenderer.GameObject.Id}:" +
			$"{modelPath}:{ThirdPersonGripBone}";
		if ( failedThirdPersonAttachmentBindingKey == bindingKey )
		{
			SetObjectEnabled( thirdPersonWeaponObject, false );
			return;
		}

		if ( !TryGetWorldAttachmentTransform(
			bodyRenderer,
			ThirdPersonGripBone,
			out var holdTransform ) )
		{
			failedThirdPersonAttachmentBindingKey = bindingKey;
			WarnMissingModelOnce(
				"third-person grip attachment",
				$"{modelPath}:{ThirdPersonGripBone}" );
			SetObjectEnabled( thirdPersonWeaponObject, false );
			return;
		}
		failedThirdPersonAttachmentBindingKey = null;

		var scale = System.MathF.Max(
			0.01f,
			modelScale );

		// This unparented root is the authored body-side socket. Fall back to the
		// final rendered Citizen hold_R bone only when a model has no attachment.
		thirdPersonGripRoot.WorldScale = Vector3.One;
		thirdPersonGripRoot.WorldPosition = holdTransform.Position;
		thirdPersonGripRoot.WorldRotation = holdTransform.Rotation;

		// Model origin correction belongs below the socket and has one source: the
		// selected definition. The renderer itself remains at a neutral transform.
		thirdPersonWeaponModelPivot.LocalPosition = modelPosition;
		thirdPersonWeaponModelPivot.LocalRotation = modelRotation.ToRotation();
		thirdPersonWeaponModelPivot.LocalScale =
			new Vector3( scale, scale, scale );
		thirdPersonWeaponObject.LocalPosition = Vector3.Zero;
		thirdPersonWeaponObject.LocalRotation = Rotation.Identity;
		thirdPersonWeaponObject.LocalScale = Vector3.One;

		DrawThirdPersonGripDebug( holdTransform );
	}

	private static bool TryGetWorldAttachmentTransform(
		SkinnedModelRenderer renderer,
		string attachmentName,
		out Transform transform )
	{
		var attachment = renderer.GetAttachment(
			attachmentName,
			worldSpace: true );
		if ( attachment.HasValue )
		{
			transform = attachment.Value;
			return true;
		}

		return renderer.TryGetBoneTransform(
			attachmentName,
			out transform );
	}

	private void DrawThirdPersonGripDebug( Transform holdTransform )
	{
		if ( !ShowThirdPersonGripDebug )
			return;

		var origin = holdTransform.Position;
		var axisLength = ThirdPersonGripDebugAxisLength;
		DebugOverlay.Line(
			origin,
			origin + holdTransform.Rotation.Forward * axisLength,
			new Color( 1.0f, 0.1f, 0.1f ) );
		DebugOverlay.Line(
			origin,
			origin + holdTransform.Rotation.Right * axisLength,
			new Color( 0.1f, 1.0f, 0.1f ) );
		DebugOverlay.Line(
			origin,
			origin + holdTransform.Rotation.Up * axisLength,
			new Color( 0.1f, 0.35f, 1.0f ) );

		// Nested wire spheres make coincident origins visible without moving any
		// transform: hold_R (white), grip root (orange), pivot (yellow), and model
		// renderer/model origin (cyan).
		DebugOverlay.Sphere(
			new Sphere( origin, 1.6f ),
			Color.White );
		DebugOverlay.Sphere(
			new Sphere( thirdPersonGripRoot.WorldPosition, 1.2f ),
			new Color( 1.0f, 0.4f, 0.05f ) );
		DebugOverlay.Sphere(
			new Sphere( thirdPersonWeaponModelPivot.WorldPosition, 0.8f ),
			new Color( 1.0f, 0.9f, 0.05f ) );
		DebugOverlay.Sphere(
			new Sphere( thirdPersonWeaponObject.WorldPosition, 0.4f ),
			Color.Cyan );
	}

	private static bool ShouldCreateThirdPersonItem(
		LargeLadWeaponPresentationState state )
	{
		return LargeLadWeaponPresentationRules.IsDodgeballSelected( state ) ||
			LargeLadWeaponCatalog.IsFirearm( state.Weapon ) ||
			(state.Role == LargeLadRole.SkinnyKid &&
				state.Weapon == LargeLadWeaponId.Melee);
	}

	private static LargeLadWeaponDefinition GetAnimationDefinition(
		LargeLadWeaponPresentationState state )
	{
		// The dodgeball uses the human arms' punching graph for movement and a
		// short throw gesture, but remains a utility in inventory and gameplay.
		return LargeLadWeaponPresentationRules.IsDodgeballSelected( state )
			? LargeLadWeaponCatalog.Get( LargeLadWeaponId.Melee )
			: LargeLadWeaponCatalog.Get( state.Weapon );
	}

	private void ApplyThirdPersonPose(
		LargeLadWeaponPresentationState state,
		LargeLadWeaponPresentationView view )
	{
		var renderer = cachedPlayer?.BodyRenderer;
		if ( renderer is null )
			return;

		if ( hasRecentThirdPersonDodgeballAttack &&
			timeSinceThirdPersonDodgeballAttack >=
				System.MathF.Max( 0.0f, DodgeballAttackPoseDuration ) )
		{
			hasRecentThirdPersonDodgeballAttack = false;
		}

		var isDodgeball = LargeLadWeaponPresentationRules
			.IsDodgeballSelected( state ) ||
			(view == LargeLadWeaponPresentationView.ThirdPerson &&
				hasRecentThirdPersonDodgeballAttack);
		var isBareFists = state.Weapon == LargeLadWeaponId.Melee &&
			state.Role != LargeLadRole.SkinnyKid;
		var isCrowbar = state.Weapon == LargeLadWeaponId.Melee &&
			state.Role == LargeLadRole.SkinnyKid && !isDodgeball;
		var isPistol = state.Weapon == LargeLadWeaponId.Pistol;
		var isSmg = state.Weapon == LargeLadWeaponId.Smg;

		var definition = LargeLadWeaponCatalog.Get( state.Weapon );
		var holdType = ToCitizenHoldType( definition.ThirdPersonHoldType );
		var handedness = definition.Grip ==
			LargeLadWeaponGrip.RightHandedOneHanded
			? CitizenAnimationHelper.Hand.Right
			: CitizenAnimationHelper.Hand.Both;
		var holdPose = 0.0f;
		var handPose = 0.0f;
		var attackVariant = 0.0f;

		if ( view == LargeLadWeaponPresentationView.Hidden )
		{
			holdType = CitizenAnimationHelper.HoldTypes.None;
			handedness = CitizenAnimationHelper.Hand.Both;
		}
		else if ( isDodgeball )
		{
			holdType = DodgeballHoldType;
			handedness = DodgeballHandedness;
			holdPose = DodgeballHoldPose;
			handPose = DodgeballHandPose;
			attackVariant = DodgeballThirdPersonAttackVariant;
		}
		else if ( isBareFists )
		{
			holdType = FistsHoldType;
			handedness = FistsHandedness;
			holdPose = FistsHoldPose;
			handPose = FistsHandPose;
			attackVariant = FistsThirdPersonAttackVariant;
		}
		else if ( isCrowbar )
		{
			holdType = CrowbarHoldType;
			handedness = CrowbarHandedness;
			holdPose = CrowbarHoldPose;
			handPose = CrowbarHandPose;
			attackVariant = CrowbarAttackVariant;
		}
		else if ( isPistol )
		{
			holdType = PistolHoldType;
			handedness = PistolHandedness;
			holdPose = PistolHoldPose;
			handPose = PistolHandPose;
			attackVariant = PistolAttackVariant;
		}
		else if ( isSmg )
		{
			holdType = SmgHoldType;
			handedness = SmgHandedness;
			holdPose = SmgHoldPose;
			handPose = SmgHandPose;
			attackVariant = SmgAttackVariant;
		}

		renderer.Set( "holdtype", (int)holdType );
		renderer.Set( "holdtype_handedness", (int)handedness );
		renderer.Set( "holdtype_pose", holdPose );
		renderer.Set( "holdtype_pose_hand", handPose );
		renderer.Set( "holdtype_attack", attackVariant );
	}

	private static CitizenAnimationHelper.HoldTypes ToCitizenHoldType(
		LargeLadThirdPersonHoldType holdType )
	{
		return holdType switch
		{
			LargeLadThirdPersonHoldType.Pistol =>
				CitizenAnimationHelper.HoldTypes.Pistol,
			LargeLadThirdPersonHoldType.Rifle =>
				CitizenAnimationHelper.HoldTypes.Rifle,
			LargeLadThirdPersonHoldType.HoldItem =>
				CitizenAnimationHelper.HoldTypes.HoldItem,
			LargeLadThirdPersonHoldType.Swing =>
				CitizenAnimationHelper.HoldTypes.Swing,
			_ => CitizenAnimationHelper.HoldTypes.None
		};
	}

	private void PresentDraw(
		LargeLadWeaponDefinition definition,
		LargeLadWeaponPresentationView view,
		bool playAudio )
	{
		if ( view == LargeLadWeaponPresentationView.Hidden )
			return;

		cachedPlayer?.BodyRenderer?.Set( "b_deploy", true );
		PlayFirstPersonAnimation(
			definition,
			definition.DrawAnimation,
			looping: false,
			FirstPersonTransient.Draw );

		if ( playAudio )
		{
			PlayDefinitionSound(
				definition.DrawSoundPath,
				definition.DrawSoundPackageIdent,
				view );
		}
	}

	private void PresentReloadStarted(
		LargeLadWeaponDefinition definition,
		LargeLadWeaponPresentationView view,
		bool playAudio )
	{
		if ( view == LargeLadWeaponPresentationView.Hidden ||
			!LargeLadWeaponCatalog.IsFirearm( definition.Id ) )
		{
			return;
		}

		// The first-person weapon graph owns its authored reload sound events.
		// Driving the hidden Citizen graph as well produced a second event source.
		if ( view == LargeLadWeaponPresentationView.ThirdPerson )
			cachedPlayer?.BodyRenderer?.Set( "b_reload", true );
		PlayFirstPersonAnimation(
			definition,
			definition.ReloadAnimation,
			looping: false,
			FirstPersonTransient.Reload );

		// Third person keeps the catalog fallback. First person normally re-emits
		// authored graph events from the moving viewmodel; an inspector override
		// suppresses those events and supplies exactly one chosen reload sound.
		if ( playAudio && view == LargeLadWeaponPresentationView.ThirdPerson )
		{
			PlayDefinitionSound(
				definition.ReloadSoundPath,
				definition.ReloadSoundPackageIdent,
				view );
		}
		else if ( playAudio &&
			view == LargeLadWeaponPresentationView.FirstPerson )
		{
			var reloadOverride = GetReloadSoundOverride( definition.Id );
			if ( reloadOverride is not null )
			{
				PlayPresentationSound( reloadOverride, view );
			}
		}
	}

	private void PresentFire(
		LargeLadWeaponDefinition definition,
		LargeLadWeaponPresentationView view )
	{
		TriggerThirdPersonAttack(
			ThirdPersonAttackKind.Current,
			previousState );
		if ( view == LargeLadWeaponPresentationView.FirstPerson )
		{
			// Accepted shots temporarily win over locomotion. Automatic fire
			// refreshes the window so sprint returns after the final shot rather
			// than dropping the weapon between rounds.
			hasRecentFirstPersonShot = true;
			timeSinceFirstPersonShot = 0.0f;
			// Clear sprint before pulsing the attack. Waiting for the next animation
			// update let the graph begin the shot from its lowered sprint branch,
			// which read as a small hop instead of a decisive raise-to-fire snap.
			TrySetFirstPersonParameter( "b_sprint", false );
		}
		PlayFirstPersonAnimation(
			definition,
			definition.FireAnimation,
			looping: false,
			FirstPersonTransient.Fire );
		PlayDefinitionSound(
			definition.FireSoundPath,
			definition.FireSoundPackageIdent,
			view );
		SpawnMuzzleEffect( definition, view );
	}

	private void PresentIdle( LargeLadWeaponDefinition definition )
	{
		PlayFirstPersonAnimation(
			definition,
			definition.IdleAnimation,
			looping: true,
			FirstPersonTransient.None );
	}

	private void RestoreCurrentFirstPersonAnimation(
		LargeLadWeaponPresentationState state,
		LargeLadWeaponDefinition definition )
	{
		if ( state.IsReloading )
		{
			PresentReloadStarted(
				definition,
				LargeLadWeaponPresentationView.FirstPerson,
				playAudio: false );
		}
		else
		{
			PresentIdle( definition );
		}
	}

	private void ConsumeAuthoritativeFireSignals(
		LargeLadWeaponPresentationState state,
		LargeLadWeaponPresentationView view,
		LargeLadWeaponDefinition definition )
	{
		var combat = cachedPlayer?.PrototypeWeapon;
		if ( combat is null )
			return;

		var shotSequence = combat.PresentationShotSequence;
		if ( LargeLadWeaponPresentationRules.ShouldPresentAcceptedShot(
			state,
			lastPresentedShotSequence,
			shotSequence,
			combat.PresentationShotWeapon ) )
		{
			PresentFire( definition, view );
		}

		if ( shotSequence > lastPresentedShotSequence )
			lastPresentedShotSequence = shotSequence;

		var emptySequence = combat.PresentationEmptySequence;
		if ( LargeLadWeaponPresentationRules.ShouldPresentEmptyFire(
			state,
			lastPresentedEmptySequence,
			emptySequence,
			combat.PresentationEmptyWeapon ) )
		{
			PlayFirstPersonAnimation(
				definition,
				definition.DryFireAnimation,
				looping: false,
				FirstPersonTransient.Fire );
			PlayDefinitionSound(
				definition.EmptySoundPath,
				definition.EmptySoundPackageIdent,
				LargeLadWeaponPresentationView.FirstPerson );
		}

		if ( emptySequence > lastPresentedEmptySequence )
			lastPresentedEmptySequence = emptySequence;
	}

	private bool PlayFirstPersonAnimation(
		LargeLadWeaponDefinition definition,
		string animation,
		bool looping,
		FirstPersonTransient transient )
	{
		var renderer = firstPersonWeaponRenderer;
		var model = renderer?.Model;
		if ( renderer is null || model is null ||
			string.IsNullOrWhiteSpace( animation ) )
		{
			return false;
		}

		if ( definition.FirstPersonUsesAnimGraph )
		{
			renderer.UseAnimGraph = true;
			firstPersonTransient = transient;
			firstPersonTransientUsesSequence = false;

			// These viewmodels deploy when their graph initializes and return to
			// idle by themselves. Only action triggers need to be pulsed.
			if ( transient == FirstPersonTransient.None )
			{
				return true;
			}

			if ( TrySetFirstPersonParameter( animation, true ) )
				return true;

			// Deploy is optional on the standard graphs; initializing the graph
			// already selects their authored draw/idle path.
			if ( transient == FirstPersonTransient.Draw )
				return true;

			WarnMissingModelOnce(
				"first-person animation parameter",
				$"{definition.FirstPersonModelPath}:{animation}" );
			return false;
		}

		var sequence = FindAnimationSequence( model, animation );

		if ( string.IsNullOrWhiteSpace( sequence ) )
		{
			WarnMissingModelOnce(
				"first-person animation sequence",
				$"{definition.FirstPersonModelPath}:{animation}" );
			return false;
		}

		renderer.UseAnimGraph = false;
		renderer.Sequence.Name = sequence;
		renderer.Sequence.Time = 0.0f;
		renderer.Sequence.Looping = looping;
		renderer.PlaybackRate = 1.0f;
		firstPersonTransient = transient;
		firstPersonTransientUsesSequence = transient !=
			FirstPersonTransient.None;
		return true;
	}

	private static string FindAnimationSequence(
		Model model,
		string animation )
	{
		if ( model is null || string.IsNullOrWhiteSpace( animation ) )
			return null;

		return model.AnimationNames.FirstOrDefault(
			candidate => string.Equals(
				candidate,
				animation,
				System.StringComparison.OrdinalIgnoreCase ) ||
				candidate.EndsWith(
					$"@{animation}",
					System.StringComparison.OrdinalIgnoreCase ) );
	}

	private void TickFirstPersonAnimation(
		LargeLadWeaponPresentationState state,
		LargeLadWeaponDefinition definition )
	{
		UpdateFirstPersonAnimationParameters( state, definition );

		if ( !firstPersonTransientUsesSequence ||
			firstPersonWeaponRenderer is null ||
			state.IsReloading ||
			firstPersonTransient == FirstPersonTransient.Reload ||
			!firstPersonWeaponRenderer.Sequence.IsFinished )
		{
			return;
		}

		PresentIdle( definition );
	}

	private void UpdateFirstPersonAnimationParameters(
		LargeLadWeaponPresentationState state,
		LargeLadWeaponDefinition definition )
	{
		if ( firstPersonWeaponRenderer is null ||
			!definition.FirstPersonUsesAnimGraph )
		{
			return;
		}

		var controller = cachedController;
		var root = firstPersonRoot;
		if ( controller is null || root is null || !root.IsValid )
			return;

		var grounded = !controller.IsAirborne;
		var velocity = controller.Velocity;
		var localVelocity = root.WorldRotation.Inverse * velocity;
		var horizontalSpeed = System.MathF.Sqrt(
			velocity.x * velocity.x + velocity.y * velocity.y );
		var normalizedSpeed = System.Math.Clamp(
			horizontalSpeed / ViewmodelFullMoveSpeed,
			0.0f,
			1.0f );
		var altMoveDown =
			!string.IsNullOrWhiteSpace( controller.AltMoveButton ) &&
			Input.Down( controller.AltMoveButton );
		var wantsRun = controller.RunByDefault
			? !altMoveDown
			: altMoveDown;
		var suppressSprintPose = hasRecentFirstPersonShot &&
			timeSinceFirstPersonShot <
				System.MathF.Max( 0.0f, SprintFirePoseDuration );
		if ( hasRecentFirstPersonShot && !suppressSprintPose )
			hasRecentFirstPersonShot = false;

		TrySetFirstPersonParameter( "skeleton", definition.FirstPersonSkeleton );
		TrySetFirstPersonParameter( "b_grounded", grounded );
		TrySetFirstPersonParameter(
			"b_sprint",
			grounded && wantsRun && horizontalSpeed > 10.0f &&
				!suppressSprintPose );
		TrySetFirstPersonParameter( "move_bob", normalizedSpeed );
		TrySetFirstPersonParameter(
			"move_x",
			System.Math.Clamp(
				localVelocity.x / ViewmodelFullMoveSpeed,
				-1.0f,
				1.0f ) );
		TrySetFirstPersonParameter(
			"move_y",
			System.Math.Clamp(
				localVelocity.y / ViewmodelFullMoveSpeed,
				-1.0f,
				1.0f ) );
		TrySetFirstPersonParameter(
			"move_z",
			System.Math.Clamp(
				localVelocity.z / ViewmodelFullMoveSpeed,
				-1.0f,
				1.0f ) );
		TrySetFirstPersonParameter( "attack_hold", Input.Down( "Attack1" ) );
		TrySetFirstPersonParameter( "b_reload", state.IsReloading );
		var forceCockingReload = state.IsReloading &&
			(state.Weapon == LargeLadWeaponId.Pistol
				? ForcePistolCockingReload
				: state.Weapon == LargeLadWeaponId.Smg &&
					ForceSmgCockingReload);
		TrySetFirstPersonParameter(
			definition.EmptyAnimation,
			LargeLadWeaponCatalog.IsFirearm( state.Weapon ) &&
				(cachedPlayer?.NativeInventory?.ActiveFirearm?.Clip1 <= 0 ||
					forceCockingReload) );
		var isTwoHanded = definition.FirstPersonTwoHanded;
		if ( LargeLadWeaponPresentationRules.IsDodgeballSelected( state ) &&
			LargeLadUtilityPresentationCatalog.TryGet(
				state.Selection.Utility,
				out var utilityDefinition ) )
		{
			isTwoHanded = utilityDefinition.FirstPersonTwoHanded;
		}
		TrySetFirstPersonParameter( "b_twohanded", isTwoHanded );

		if ( hasFirstPersonGroundState &&
			wasFirstPersonGrounded &&
			!grounded && velocity.z > 0.0f )
		{
			TrySetFirstPersonParameter( "b_jump", true );
		}

		hasFirstPersonGroundState = true;
		wasFirstPersonGrounded = grounded;
	}

	private bool TrySetFirstPersonParameter( string name, bool value )
	{
		if ( !HasFirstPersonParameter( name ) )
			return false;

		firstPersonWeaponRenderer.Set( name, value );
		return true;
	}

	private bool TrySetFirstPersonParameter( string name, int value )
	{
		if ( !HasFirstPersonParameter( name ) )
			return false;

		firstPersonWeaponRenderer.Set( name, value );
		return true;
	}

	private bool TrySetFirstPersonParameter( string name, float value )
	{
		if ( !HasFirstPersonParameter( name ) )
			return false;

		firstPersonWeaponRenderer.Set( name, value );
		return true;
	}

	private bool HasFirstPersonParameter( string name )
	{
		if ( string.IsNullOrWhiteSpace( name ) ||
			firstPersonWeaponRenderer is null ||
			!firstPersonWeaponRenderer.UseAnimGraph )
		{
			return false;
		}
		if ( supportedFirstPersonParameters.Contains( name ) )
			return true;
		if ( unsupportedFirstPersonParameters.Contains( name ) )
			return false;

		var graph = firstPersonWeaponRenderer.AnimationGraph;
		var supported = graph is not null && !graph.IsError &&
			graph.TryGetParameterIndex( name, out _ );
		if ( supported )
			supportedFirstPersonParameters.Add( name );
		else
			unsupportedFirstPersonParameters.Add( name );

		return supported;
	}

	private void ApplyFirstPersonAttackVariant(
		LargeLadWeaponPresentationState state )
	{
		if ( LargeLadWeaponPresentationRules.IsDodgeballSelected( state ) )
		{
			TrySetFirstPersonParameter(
				"holdtype_attack",
				DodgeballAttackVariant );
		}
		else if ( state.Weapon == LargeLadWeaponId.Melee &&
			state.Role != LargeLadRole.SkinnyKid )
		{
			TrySetFirstPersonParameter(
				"holdtype_attack",
				FistsAttackVariant );
		}
	}

	private void TriggerThirdPersonAttack(
		ThirdPersonAttackKind kind,
		LargeLadWeaponPresentationState state )
	{
		var renderer = cachedPlayer?.BodyRenderer;
		if ( renderer is null )
			return;

		if ( kind == ThirdPersonAttackKind.Dodgeball )
		{
			hasRecentThirdPersonDodgeballAttack = true;
			timeSinceThirdPersonDodgeballAttack = 0.0f;
			renderer.Set( "holdtype", (int)DodgeballHoldType );
			renderer.Set(
				"holdtype_handedness",
				(int)DodgeballHandedness );
			renderer.Set( "holdtype_pose", DodgeballHoldPose );
			renderer.Set( "holdtype_pose_hand", DodgeballHandPose );
			renderer.Set(
				"holdtype_attack",
				DodgeballThirdPersonAttackVariant );
		}
		else if ( kind == ThirdPersonAttackKind.Melee )
		{
			renderer.Set(
				"holdtype_attack",
				state.Role == LargeLadRole.SkinnyKid
					? CrowbarAttackVariant
					: FistsThirdPersonAttackVariant );
		}

		renderer.Set( "b_attack", true );
	}

	private void PlayDefinitionSound(
		string soundPath,
		string soundPackageIdent,
		LargeLadWeaponPresentationView view )
	{
		var soundKey = !string.IsNullOrWhiteSpace( soundPackageIdent )
			? soundPackageIdent
			: soundPath;
		var bindingKey = $"{presentationRevision}:{soundKey}";
		if ( string.IsNullOrWhiteSpace( soundKey ) ||
			view == LargeLadWeaponPresentationView.Hidden ||
			failedSoundBindingKeys.Contains( bindingKey ) )
		{
			return;
		}

		var sound = LoadDefinitionSound( soundPath, soundPackageIdent );
		if ( sound is null )
		{
			// Do not retry on every shot. A presentation identity change bumps the
			// revision and permits one fresh attempt in case a package mounted late.
			failedSoundBindingKeys.Add( bindingKey );
			WarnMissingModelOnce( "presentation sound", soundKey );
			return;
		}
		failedSoundBindingKeys.Remove( bindingKey );

		PlayPresentationSound( sound, view );
	}

	private SoundEvent GetReloadSoundOverride( LargeLadWeaponId weapon )
	{
		return weapon switch
		{
			LargeLadWeaponId.Pistol => PistolReloadSoundOverride,
			LargeLadWeaponId.Smg => SmgReloadSoundOverride,
			_ => null
		};
	}

	private void PlayPresentationSound(
		SoundEvent sound,
		LargeLadWeaponPresentationView view )
	{
		if ( sound is null ||
			view == LargeLadWeaponPresentationView.Hidden )
		{
			return;
		}

		var source = view == LargeLadWeaponPresentationView.FirstPerson
			? firstPersonWeaponObject ?? boundCameraObject
			: thirdPersonWeaponObject ?? GameObject;
		if ( source is null || !source.IsValid )
			source = GameObject;

		// Play through the source object so spatial effects follow the moving
		// muzzle/player instead of remaining at the shot's original world point.
		var handle = source.PlaySound( sound, Vector3.Zero );
		if ( handle is not null )
		{
			// A viewmodel sound is listener-relative. Removing spatial blend also
			// prevents motion Doppler/panning from distorting the owner's shot.
			if ( view == LargeLadWeaponPresentationView.FirstPerson )
			{
				handle.SpacialBlend = 0.0f;
				handle.DistanceAttenuation = false;
				handle.OcclusionEnabled = false;
				handle.ReverbEnabled = false;
			}
			activeSounds.Add( handle );
		}
	}

	private static SoundEvent LoadDefinitionSound(
		string soundPath,
		string packageIdent )
	{
		// Direct literal calls make these presentation-only sound dependencies
		// part of the game package instead of relying on another installed game.
		if ( packageIdent == "vidya/pistol-shoot" )
		{
			var mounted = LargeLadWeaponPresentationAssets.GetSound(
				"vidya/pistol-shoot" );
			if ( mounted is not null )
				return mounted;
			return Cloud.SoundEvent( "vidya/pistol-shoot" );
		}
		if ( packageIdent == "vidya/smg-shoot" )
		{
			var mounted = LargeLadWeaponPresentationAssets.GetSound(
				"vidya/smg-shoot" );
			if ( mounted is not null )
				return mounted;
			return Cloud.SoundEvent( "vidya/smg-shoot" );
		}
		if ( packageIdent == "drakefruit/pistol_reload" )
		{
			var mounted = LargeLadWeaponPresentationAssets.GetSound(
				"drakefruit/pistol_reload" );
			if ( mounted is not null )
				return mounted;
			return Cloud.SoundEvent( "drakefruit/pistol_reload" );
		}
		if ( packageIdent == "drakefruit/rifle_reload" )
		{
			var mounted = LargeLadWeaponPresentationAssets.GetSound(
				"drakefruit/rifle_reload" );
			if ( mounted is not null )
				return mounted;
			return Cloud.SoundEvent( "drakefruit/rifle_reload" );
		}
		if ( packageIdent == "hzgame/hzuiclickbuttontinyrattle" )
		{
			var mounted = LargeLadWeaponPresentationAssets.GetSound(
				"hzgame/hzuiclickbuttontinyrattle" );
			if ( mounted is not null )
				return mounted;

			return Cloud.SoundEvent( "hzgame/hzuiclickbuttontinyrattle" );
		}

		return string.IsNullOrWhiteSpace( soundPath )
			? null
			: ResourceLibrary.Get<SoundEvent>( soundPath );
	}

	private void SpawnMuzzleEffect(
		LargeLadWeaponDefinition definition,
		LargeLadWeaponPresentationView view )
	{
		if ( string.IsNullOrWhiteSpace( definition.MuzzleEffectPrefabPath ) ||
			string.IsNullOrWhiteSpace( definition.MuzzleAttachment ) )
		{
			return;
		}

		ModelRenderer renderer = view ==
			LargeLadWeaponPresentationView.FirstPerson
			? firstPersonWeaponRenderer
			: thirdPersonWeaponRenderer;
		var modelPath = view == LargeLadWeaponPresentationView.FirstPerson
			? definition.FirstPersonModelPath
			: definition.ThirdPersonWorldModelPath;
		var bindingKey =
			$"{presentationRevision}:{view}:{renderer?.GameObject?.Id}:" +
			$"{modelPath}:{definition.MuzzleAttachment}:" +
			definition.MuzzleEffectPrefabPath;
		if ( failedMuzzleBindingKey == bindingKey )
			return;

		var attachment = renderer?.GetAttachmentObject(
			definition.MuzzleAttachment );
		if ( attachment is null || !attachment.IsValid )
		{
			failedMuzzleBindingKey = bindingKey;
			WarnMissingModelOnce(
				"muzzle attachment",
				$"{modelPath}:" +
					definition.MuzzleAttachment );
			return;
		}

		DestroyPresentationEffects();
		var transform = attachment.WorldTransform;
		var effect = GameObject.Clone(
			definition.MuzzleEffectPrefabPath,
			new CloneConfig
			{
				Transform = transform,
				StartEnabled = true
			} );

		if ( effect is null || !effect.IsValid )
		{
			failedMuzzleBindingKey = bindingKey;
			WarnMissingModelOnce(
				"muzzle effect prefab",
				definition.MuzzleEffectPrefabPath );
			return;
		}
		failedMuzzleBindingKey = null;

		effect.NetworkMode = NetworkMode.Never;
		var effectScale = System.MathF.Max(
			0.01f,
			definition.MuzzleEffectScale );
		effect.WorldScale = effect.WorldScale * effectScale;
		// The flash is transient but must continue following the animated muzzle
		// for every frame of its lifetime.
		effect.SetParent( attachment, keepWorldPosition: true );

		if ( view == LargeLadWeaponPresentationView.FirstPerson )
		{
			foreach ( var effectRenderer in effect.Components.GetAll<Renderer>(
				FindMode.EverythingInSelfAndDescendants ) )
			{
				ConfigureFirstPersonRenderer( effectRenderer );
			}
		}

		activeEffects.Add( effect );
	}

	private void InterruptPresentation()
	{
		presentationRevision++;
		StopPresentationSounds();
		DestroyPresentationEffects();
		firstPersonTransient = FirstPersonTransient.None;
		firstPersonTransientUsesSequence = false;
		hasRecentFirstPersonShot = false;
	}

	private void CleanupFinishedPresentationObjects()
	{
		for ( var index = activeSounds.Count - 1; index >= 0; index-- )
		{
			if ( activeSounds[index] is null ||
				!activeSounds[index].IsPlaying )
			{
				activeSounds.RemoveAt( index );
			}
		}

		for ( var index = activeEffects.Count - 1; index >= 0; index-- )
		{
			if ( activeEffects[index] is null ||
				!activeEffects[index].IsValid )
			{
				activeEffects.RemoveAt( index );
			}
		}
	}

	private void StopPresentationSounds()
	{
		foreach ( var handle in activeSounds )
		{
			if ( handle is not null && handle.IsPlaying )
				handle.Fadeout = 0.05f;
		}

		activeSounds.Clear();
	}

	private void DestroyPresentationEffects()
	{
		foreach ( var effect in activeEffects )
		{
			if ( effect is not null && effect.IsValid )
				effect.Destroy();
		}

		activeEffects.Clear();
	}

	private void DestroyFirstPersonViewmodel()
	{
		DestroyFirstPersonUtility();

		if ( firstPersonRoot is not null && firstPersonRoot.IsValid )
			firstPersonRoot.Destroy();

		firstPersonRoot = null;
		firstPersonWeaponObject = null;
		firstPersonArmsObject = null;
		firstPersonWeaponRenderer = null;
		firstPersonArmsRenderer = null;
		boundCameraObject = null;
		appliedFirstPersonWeaponPath = null;
		appliedFirstPersonArmsPath = null;
		firstPersonWeaponUsesArmsModel = false;
		hasFirstPersonGroundState = false;
		wasFirstPersonGrounded = false;
		firstPersonTransient = FirstPersonTransient.None;
		firstPersonTransientUsesSequence = false;
		hasRecentFirstPersonShot = false;
		configuredFirstPersonAudioSceneModel = null;
		lastFirstPersonAnimationSoundName = null;
		supportedFirstPersonParameters.Clear();
		unsupportedFirstPersonParameters.Clear();
	}

	private void DestroyFirstPersonUtility()
	{
		if ( firstPersonUtilityObject is not null &&
			firstPersonUtilityObject.IsValid )
		{
			firstPersonUtilityObject.Destroy();
		}

		firstPersonUtilityObject = null;
		firstPersonUtilityRenderer = null;
		appliedFirstPersonUtilityModelPath = null;
	}

	private void DestroyThirdPersonWeapon()
	{
		if ( thirdPersonGripRoot is not null &&
			thirdPersonGripRoot.IsValid )
		{
			thirdPersonGripRoot.Destroy();
		}

		thirdPersonGripRoot = null;
		thirdPersonWeaponModelPivot = null;
		thirdPersonWeaponObject = null;
		thirdPersonWeaponRenderer = null;
		appliedThirdPersonModelPath = null;
	}

	private void ResetPresentation( bool restoreBody )
	{
		InterruptPresentation();
		DestroyFirstPersonViewmodel();
		DestroyThirdPersonWeapon();
		hasRecentThirdPersonDodgeballAttack = false;
		hasPreviousState = false;

		if ( restoreBody )
		{
			SetBodyVisible( true );
			cachedPlayer?.BodyRenderer?.Set(
				"holdtype",
				(int)CitizenAnimationHelper.HoldTypes.None );
		}
	}

	private void SetBodyVisible( bool visible )
	{
		var bodyObject = cachedPlayer?.BodyRenderer?.GameObject;
		if ( bodyObject is null || !bodyObject.IsValid )
			return;

		foreach ( var renderer in bodyObject.Components.GetAll<Renderer>(
			FindMode.EverythingInSelfAndDescendants ) )
		{
			renderer.RenderOptions.Game = visible;
		}
	}

	private static void SetObjectEnabled(
		GameObject gameObject,
		bool enabled )
	{
		if ( gameObject is not null && gameObject.IsValid )
			gameObject.Enabled = enabled;
	}

	private void WarnMissingModelOnce( string usage, string path )
	{
		var key = $"{usage}:{path}";
		if ( !warnedMissingModels.Add( key ) )
			return;

		Log.Warning(
			$"{GameObject.Name}: unable to load {usage} '{path}'." );
	}

	private LargeLadGameManager GetGameManager()
	{
		if ( cachedGameManager is not null &&
			cachedGameManager.IsValid &&
			cachedGameManager.Enabled &&
			cachedGameManager.Scene == Scene &&
			cachedGameManager.HasSceneGameplayOwnership )
		{
			return cachedGameManager;
		}

		cachedGameManager = LargeLadGameManager.FindForScene( Scene );
		return cachedGameManager;
	}

	private void ResolveCachedReferences()
	{
		if ( cachedPlayer is null ||
			!cachedPlayer.IsValid ||
			cachedPlayer.GameObject != GameObject )
		{
			cachedPlayer = Components.Get<LargeLadPlayer>();
		}

		if ( cachedController is null ||
			!cachedController.IsValid ||
			cachedController.GameObject != GameObject )
		{
			cachedController = Components.Get<PlayerController>();
		}

		if ( cachedLocalPlayerSetup is null ||
			!cachedLocalPlayerSetup.IsValid ||
			cachedLocalPlayerSetup.GameObject != GameObject )
		{
			cachedLocalPlayerSetup = Components.Get<LocalPlayerSetup>();
		}

		GetGameManager();
	}
}

/// <summary>
/// Process-local cache for the cloud packages used by weapon presentation.
/// Loading is shared by every player component on this peer and is awaited by
/// OnLoad, so a joining client cannot permanently bind an error model merely
/// because its package mount completed one frame late.
/// </summary>
internal static class LargeLadWeaponPresentationAssets
{
	private static readonly Dictionary<string, Model> models = new();
	private static readonly Dictionary<string, SoundEvent> sounds = new();
	private static Task loadTask;

	public static Task EnsureLoadedAsync()
	{
		loadTask ??= LoadAllAsync();
		return loadTask;
	}

	public static Model GetModel( string packageIdent )
	{
		return models.TryGetValue( packageIdent, out var model )
			? model
			: null;
	}

	public static SoundEvent GetSound( string packageIdent )
	{
		return sounds.TryGetValue( packageIdent, out var sound )
			? sound
			: null;
	}

	private static async Task LoadAllAsync()
	{
		// Start every package request before awaiting any of them. Avoid
		// Task.WhenAll's ReadOnlySpan overload, which is not on the s&box
		// runtime whitelist even though ordinary dotnet builds accept it.
		var packageLoads = new Task[]
		{
			LoadModelAsync( "facepunch/v_first_person_arms_human" ),
			LoadModelAsync( "facepunch/v_crowbar" ),
			LoadModelAsync( "facepunch/v_usp" ),
			LoadModelAsync( "facepunch/v_mp5" ),
			LoadModelAsync( "facepunch/w_crowbar" ),
			LoadModelAsync( "facepunch/w_usp" ),
			LoadModelAsync( "facepunch/w_mp5" ),
			LoadSoundAsync( "vidya/pistol-shoot" ),
			LoadSoundAsync( "vidya/smg-shoot" ),
			LoadSoundAsync( "drakefruit/pistol_reload" ),
			LoadSoundAsync( "drakefruit/rifle_reload" ),
			LoadSoundAsync( "hzgame/hzuiclickbuttontinyrattle" )
		};

		foreach ( var packageLoad in packageLoads )
			await packageLoad;
	}

	private static async Task LoadModelAsync( string packageIdent )
	{
		try
		{
			var model = await Cloud.Load<Model>( packageIdent );
			if ( model is not null && !model.IsError )
				models[packageIdent] = model;
		}
		catch ( System.Exception exception )
		{
			Log.Warning(
				$"Unable to mount weapon presentation model package " +
				$"'{packageIdent}': {exception.Message}" );
		}
	}

	private static async Task LoadSoundAsync( string packageIdent )
	{
		try
		{
			var sound = await Cloud.Load<SoundEvent>( packageIdent );
			if ( sound is not null )
				sounds[packageIdent] = sound;
		}
		catch ( System.Exception exception )
		{
			Log.Warning(
				$"Unable to mount weapon presentation sound package " +
				$"'{packageIdent}': {exception.Message}" );
		}
	}
}
