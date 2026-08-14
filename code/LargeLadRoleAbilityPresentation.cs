using Sandbox;
using Sandbox.Citizen;
using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>
/// Presentation for player abilities that do not have native weapon models.
/// Native Skinny Kid melee and firearms never enter this component. Bare-fist
/// role attacks remain custom, and the dodgeball stays here until its inventory
/// item gains a native viewmodel/worldmodel presentation of its own.
/// </summary>
public sealed class LargeLadRoleAbilityPresentation : Component
{
	private const float ViewmodelFullMoveSpeed = 250.0f;
	private const string ThirdPersonGripBone = "hold_R";
	private const string HumanArmsModelPath =
		"models/first_person/v_first_person_arms_human.vmdl";
	private const string HumanArmsPackageIdent =
		"facepunch/v_first_person_arms_human";
	private const string BareArmsAnimationGraphPath =
		"models/first_person/v_first_person_arms_punching.vanmgrph";

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

	private readonly HashSet<string> warnedMissingAssets = new();
	private readonly HashSet<string> supportedFirstPersonParameters = new();
	private readonly HashSet<string> unsupportedFirstPersonParameters = new();

	private LargeLadPlayer cachedPlayer;
	private PlayerController cachedController;
	private LocalPlayerSetup cachedLocalPlayerSetup;
	private LargeLadGameManager cachedGameManager;

	private GameObject firstPersonRoot;
	private GameObject firstPersonArmsObject;
	private GameObject firstPersonDodgeballObject;
	private SkinnedModelRenderer firstPersonArmsRenderer;
	private ModelRenderer firstPersonDodgeballRenderer;
	private GameObject boundCameraObject;
	private LargeLadRoleAbilityPresentationKind firstPersonKind;
	private string failedFirstPersonBindingKey;
	private string failedFirstPersonDodgeballBindingKey;

	private GameObject thirdPersonDodgeballGripRoot;
	private GameObject thirdPersonDodgeballModelPivot;
	private GameObject thirdPersonDodgeballObject;
	private ModelRenderer thirdPersonDodgeballRenderer;
	private string appliedThirdPersonDodgeballModelPath;
	private string failedThirdPersonDodgeballBindingKey;
	private string failedThirdPersonAttachmentBindingKey;

	private LargeLadRoleAbilityPresentationState currentState;
	private LargeLadRoleAbilityPresentationKind currentKind;
	private LargeLadRoleAbilityPresentationView currentView;
	private bool hasCurrentState;
	private bool ownsThirdPersonPose;
	private bool hasRecentThirdPersonDodgeballAttack;
	private TimeSince timeSinceThirdPersonDodgeballAttack;
	private int presentationRevision;
	private bool nativePresentationSuppressed;
	private bool hasFirstPersonGroundState;
	private bool wasFirstPersonGrounded;

	protected override void OnAwake()
	{
		ResolveCachedReferences();
	}

	protected override async Task OnLoad()
	{
		await LargeLadPresentationAssets.EnsureLoadedAsync();
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
		ResetPresentation( restoreBody: true, clearPose: true );
		base.OnDisabled();
	}

	protected override void OnDestroy()
	{
		ResetPresentation( restoreBody: true, clearPose: true );
		base.OnDestroy();
	}

	protected override void OnUpdate()
	{
		ResolveCachedReferences();

		if ( cachedPlayer?.NativeInventory?
			.HasNativeCombatPresentationControl == true )
		{
			if ( !nativePresentationSuppressed )
			{
				ResetPresentation(
					restoreBody: true,
					clearPose: false );
			}

			nativePresentationSuppressed = true;
			return;
		}

		nativePresentationSuppressed = false;
		var state = CaptureState( out var ownedCamera );
		var kind = LargeLadRoleAbilityPresentationRules.ResolveKind( state );
		var view = LargeLadRoleAbilityPresentationRules.ResolveView( state );
		var cameraChanged = view ==
			LargeLadRoleAbilityPresentationView.FirstPerson &&
			boundCameraObject != ownedCamera?.GameObject;
		var identityChanged = !hasCurrentState ||
			currentKind != kind ||
			currentView != view ||
			currentState.Selection != state.Selection;

		if ( identityChanged || cameraChanged )
		{
			presentationRevision++;
			DestroyFirstPersonPresentation();
			DestroyThirdPersonDodgeball();
		}

		if ( hasRecentThirdPersonDodgeballAttack &&
			timeSinceThirdPersonDodgeballAttack >=
				System.MathF.Max( 0.0f, DodgeballAttackPoseDuration ) )
		{
			hasRecentThirdPersonDodgeballAttack = false;
		}

		UpdateVisibilityAndModels( state, kind, view, ownedCamera );
		ApplyThirdPersonPose( kind, view );

		if ( view == LargeLadRoleAbilityPresentationView.FirstPerson )
			UpdateFirstPersonAnimationParameters( state, kind );

		currentState = state;
		currentKind = kind;
		currentView = view;
		hasCurrentState = true;
	}

	protected override void OnPreRender()
	{
		if ( nativePresentationSuppressed || !hasCurrentState )
			return;

		if ( currentView ==
			LargeLadRoleAbilityPresentationView.FirstPerson )
		{
			UpdateFirstPersonTransform( currentKind );
			UpdateFirstPersonDodgeballTransform( currentState, currentKind );
		}
		else if ( currentView ==
			LargeLadRoleAbilityPresentationView.ThirdPerson &&
			currentKind == LargeLadRoleAbilityPresentationKind.Dodgeball )
		{
			UpdateThirdPersonDodgeballTransform( currentState );
		}
	}

	/// <summary>
	/// Immediate owner-side feedback for role melee. Native Skinny Kid melee
	/// never reaches this method because LargeLadMeleeWeapon owns its gesture.
	/// </summary>
	internal void TriggerPredictedSwing()
	{
		var state = CaptureState( out _ );
		var kind = LargeLadRoleAbilityPresentationRules.ResolveKind( state );
		var view = LargeLadRoleAbilityPresentationRules.ResolveView( state );

		if ( kind != LargeLadRoleAbilityPresentationKind.RoleMelee ||
			view == LargeLadRoleAbilityPresentationView.Hidden )
		{
			return;
		}

		if ( view == LargeLadRoleAbilityPresentationView.FirstPerson )
			TriggerFirstPersonAttack( FistsAttackVariant );
		else
			TriggerThirdPersonMeleeAttack();
	}

	internal void BroadcastSwing()
	{
		BroadcastMeleeSwingAnimation();
	}

	internal void TriggerPredictedUtilityUse()
	{
		var state = CaptureState( out _ );
		var kind = LargeLadRoleAbilityPresentationRules.ResolveKind( state );
		var view = LargeLadRoleAbilityPresentationRules.ResolveView( state );

		if ( kind != LargeLadRoleAbilityPresentationKind.Dodgeball ||
			view == LargeLadRoleAbilityPresentationView.Hidden )
		{
			return;
		}

		if ( view == LargeLadRoleAbilityPresentationView.FirstPerson )
			TriggerFirstPersonAttack( DodgeballAttackVariant );
		else
			TriggerThirdPersonDodgeballAttack();
	}

	internal void BroadcastUtilityUse()
	{
		BroadcastUtilityUseAnimation();
	}

	[Rpc.Broadcast]
	private void BroadcastMeleeSwingAnimation()
	{
		if ( !IsProxy )
			return;

		var state = CaptureState( out _ );
		if ( LargeLadRoleAbilityPresentationRules.ResolveKind( state ) ==
				LargeLadRoleAbilityPresentationKind.RoleMelee &&
			LargeLadRoleAbilityPresentationRules.ResolveView( state ) ==
				LargeLadRoleAbilityPresentationView.ThirdPerson )
		{
			TriggerThirdPersonMeleeAttack();
		}
	}

	[Rpc.Broadcast]
	private void BroadcastUtilityUseAnimation()
	{
		if ( !IsProxy )
			return;

		ResolveCachedReferences();

		// An accepted throw clears the utility item before this cosmetic RPC can
		// arrive. The short gesture therefore validates the living role, not the
		// already-consumed selection.
		if ( cachedPlayer?.Role == LargeLadRole.SkinnyKid &&
			cachedPlayer.Health?.IsDead == false )
		{
			TriggerThirdPersonDodgeballAttack();
		}
	}

	private LargeLadRoleAbilityPresentationState CaptureState(
		out CameraComponent ownedCamera )
	{
		var player = cachedPlayer;
		ownedCamera = GetOwnedCamera();

		return new LargeLadRoleAbilityPresentationState
		{
			Role = player?.Role ?? LargeLadRole.Unassigned,
			RoundPhase = GetGameManager()?.Phase ??
				LargeLadRoundPhase.WaitingForPlayers,
			Selection = player?.ActiveInventorySelection ??
				LargeLadInventorySelection.None,
			IsDead = player?.Health?.IsDead != false,
			IsLocalOwner = !IsProxy,
			HasOwnedCamera = ownedCamera is not null,
			IsThirdPersonCamera = cachedController?.ThirdPerson != false
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
		LargeLadRoleAbilityPresentationState state,
		LargeLadRoleAbilityPresentationKind kind,
		LargeLadRoleAbilityPresentationView view,
		CameraComponent ownedCamera )
	{
		SetBodyVisible(
			view != LargeLadRoleAbilityPresentationView.FirstPerson );

		if ( view == LargeLadRoleAbilityPresentationView.FirstPerson )
		{
			EnsureFirstPersonPresentation( state, kind, ownedCamera );
			SetObjectEnabled( firstPersonRoot, true );
			DestroyThirdPersonDodgeball();
			return;
		}

		DestroyFirstPersonPresentation();

		if ( view == LargeLadRoleAbilityPresentationView.ThirdPerson &&
			kind == LargeLadRoleAbilityPresentationKind.Dodgeball )
		{
			EnsureThirdPersonDodgeball( state );
			SetObjectEnabled( thirdPersonDodgeballObject, true );
			return;
		}

		DestroyThirdPersonDodgeball();
	}

	private void EnsureFirstPersonPresentation(
		LargeLadRoleAbilityPresentationState state,
		LargeLadRoleAbilityPresentationKind kind,
		CameraComponent camera )
	{
		if ( camera?.GameObject is null ||
			kind is not (LargeLadRoleAbilityPresentationKind.RoleMelee or
				LargeLadRoleAbilityPresentationKind.Dodgeball) )
		{
			DestroyFirstPersonPresentation();
			return;
		}

		var bindingKey =
			$"{presentationRevision}:{camera.GameObject.Id}:{kind}:" +
			HumanArmsModelPath;
		var mustRebuild = firstPersonRoot is null ||
			!firstPersonRoot.IsValid ||
			boundCameraObject != camera.GameObject ||
			firstPersonKind != kind;

		if ( mustRebuild )
		{
			if ( failedFirstPersonBindingKey == bindingKey )
				return;

			DestroyFirstPersonPresentation();
			if ( !CreateFirstPersonPresentation(
				camera.GameObject,
				kind ) )
			{
				failedFirstPersonBindingKey = bindingKey;
				return;
			}

			failedFirstPersonBindingKey = null;
		}

		if ( firstPersonRoot is null || !firstPersonRoot.IsValid )
			return;

		firstPersonRoot.LocalTransform = global::Transform.Zero;
		UpdateFirstPersonTransform( kind );
		EnsureFirstPersonDodgeball( state, kind );
		UpdateFirstPersonDodgeballTransform( state, kind );

		if ( firstPersonArmsRenderer is not null )
		{
			firstPersonArmsRenderer.Tint =
				cachedPlayer?.BodyRenderer?.Tint ?? Color.White;
		}
	}

	private bool CreateFirstPersonPresentation(
		GameObject cameraObject,
		LargeLadRoleAbilityPresentationKind kind )
	{
		var armsModel = LoadFirstPersonArmsModel();
		if ( armsModel is null || armsModel.IsError )
		{
			WarnMissingAssetOnce(
				"first-person role arms",
				HumanArmsModelPath );
			return false;
		}

		firstPersonRoot = new GameObject(
			cameraObject,
			true,
			"First Person Role Ability (Local)" )
		{
			NetworkMode = NetworkMode.Never
		};
		firstPersonArmsObject = new GameObject(
			firstPersonRoot,
			true,
			"First Person Role Arms" )
		{
			NetworkMode = NetworkMode.Never
		};
		firstPersonArmsRenderer = firstPersonArmsObject.Components
			.Create<SkinnedModelRenderer>();
		firstPersonArmsRenderer.Model = armsModel;

		var animationGraph = AnimationGraph.Load(
			BareArmsAnimationGraphPath );
		if ( animationGraph is not null && !animationGraph.IsError )
		{
			firstPersonArmsRenderer.AnimationGraph = animationGraph;
			firstPersonArmsRenderer.UseAnimGraph = true;
		}
		else
		{
			WarnMissingAssetOnce(
				"first-person role animation graph",
				BareArmsAnimationGraphPath );
			firstPersonArmsRenderer.UseAnimGraph = false;
		}

		firstPersonArmsRenderer.CreateAttachments = true;
		ConfigureFirstPersonRenderer( firstPersonArmsRenderer );
		boundCameraObject = cameraObject;
		firstPersonKind = kind;
		return true;
	}

	private static Model LoadFirstPersonArmsModel()
	{
		var mounted = LargeLadPresentationAssets.GetModel(
			HumanArmsPackageIdent );
		if ( mounted is not null && !mounted.IsError )
			return mounted;

		var pathModel = Model.Load( HumanArmsModelPath );
		if ( pathModel is not null && !pathModel.IsError )
			return pathModel;

		// Keep the package literal visible to the compiler for published clients.
		return Cloud.Model( "facepunch/v_first_person_arms_human" );
	}

	private void UpdateFirstPersonTransform(
		LargeLadRoleAbilityPresentationKind kind )
	{
		if ( firstPersonArmsObject is null ||
			!firstPersonArmsObject.IsValid )
		{
			return;
		}

		var position = kind == LargeLadRoleAbilityPresentationKind.Dodgeball
			? DodgeballPositionOffset
			: FistsPositionOffset;
		var scale = System.MathF.Max(
			0.01f,
			kind == LargeLadRoleAbilityPresentationKind.Dodgeball
				? DodgeballArmsScale
				: FistsModelScale );

		firstPersonArmsObject.LocalPosition = position;
		firstPersonArmsObject.LocalRotation = Rotation.Identity;
		firstPersonArmsObject.LocalScale = new Vector3( scale, scale, scale );
	}

	private void EnsureFirstPersonDodgeball(
		LargeLadRoleAbilityPresentationState state,
		LargeLadRoleAbilityPresentationKind kind )
	{
		if ( kind != LargeLadRoleAbilityPresentationKind.Dodgeball ||
			!LargeLadUtilityPresentationCatalog.TryGet(
				state.Selection.Utility,
				out var definition ) ||
			firstPersonRoot is null ||
			!firstPersonRoot.IsValid )
		{
			DestroyFirstPersonDodgeball();
			return;
		}

		var path = definition.FirstPersonHeldModelPath;
		var bindingKey = $"{presentationRevision}:{path}";
		if ( firstPersonDodgeballObject is not null &&
			firstPersonDodgeballObject.IsValid )
		{
			return;
		}

		if ( failedFirstPersonDodgeballBindingKey == bindingKey )
			return;

		var model = string.IsNullOrWhiteSpace( path )
			? null
			: Model.Load( path );
		if ( model is null || model.IsError )
		{
			WarnMissingAssetOnce( "first-person dodgeball model", path );
			failedFirstPersonDodgeballBindingKey = bindingKey;
			return;
		}

		firstPersonDodgeballObject = new GameObject(
			firstPersonRoot,
			true,
			"First Person Dodgeball" )
		{
			NetworkMode = NetworkMode.Never
		};
		firstPersonDodgeballRenderer = firstPersonDodgeballObject.Components
			.Create<ModelRenderer>();
		firstPersonDodgeballRenderer.Model = model;
		firstPersonDodgeballRenderer.Tint =
			LargeLadUtilityRules.DodgeballColor;
		ConfigureFirstPersonRenderer( firstPersonDodgeballRenderer );
		failedFirstPersonDodgeballBindingKey = null;
	}

	private void UpdateFirstPersonDodgeballTransform(
		LargeLadRoleAbilityPresentationState state,
		LargeLadRoleAbilityPresentationKind kind )
	{
		if ( kind != LargeLadRoleAbilityPresentationKind.Dodgeball ||
			!LargeLadUtilityPresentationCatalog.TryGet(
				state.Selection.Utility,
				out var definition ) ||
			firstPersonArmsRenderer is null ||
			firstPersonDodgeballObject is null ||
			!firstPersonDodgeballObject.IsValid )
		{
			return;
		}

		var bindingKey =
			$"{presentationRevision}:{definition.FirstPersonHeldModelPath}:" +
			definition.FirstPersonHeldAttachmentBone;
		if ( string.IsNullOrWhiteSpace(
				definition.FirstPersonHeldAttachmentBone ) ||
			!firstPersonArmsRenderer.TryGetBoneTransform(
				definition.FirstPersonHeldAttachmentBone,
				out var handTransform ) )
		{
			if ( failedFirstPersonDodgeballBindingKey != bindingKey )
			{
				failedFirstPersonDodgeballBindingKey = bindingKey;
				WarnMissingAssetOnce(
					"first-person dodgeball attachment bone",
					definition.FirstPersonHeldAttachmentBone );
			}
			SetObjectEnabled( firstPersonDodgeballObject, false );
			return;
		}

		failedFirstPersonDodgeballBindingKey = null;
		SetObjectEnabled( firstPersonDodgeballObject, true );
		var scale = System.MathF.Max(
			0.01f,
			definition.FirstPersonHeldModelScale );
		firstPersonDodgeballObject.WorldPosition = handTransform.PointToWorld(
			definition.FirstPersonHeldPositionOffset );
		firstPersonDodgeballObject.WorldRotation = handTransform.Rotation *
			definition.FirstPersonHeldRotationOffset.ToRotation();
		firstPersonDodgeballObject.WorldScale =
			new Vector3( scale, scale, scale );
	}

	private void EnsureThirdPersonDodgeball(
		LargeLadRoleAbilityPresentationState state )
	{
		if ( !LargeLadUtilityPresentationCatalog.TryGet(
			state.Selection.Utility,
			out var definition ) )
		{
			DestroyThirdPersonDodgeball();
			return;
		}

		var path = definition.ThirdPersonWorldModelPath;
		var bindingKey = $"{presentationRevision}:{path}";
		if ( thirdPersonDodgeballGripRoot is not null &&
			thirdPersonDodgeballGripRoot.IsValid &&
			thirdPersonDodgeballModelPivot is not null &&
			thirdPersonDodgeballModelPivot.IsValid &&
			thirdPersonDodgeballObject is not null &&
			thirdPersonDodgeballObject.IsValid &&
			appliedThirdPersonDodgeballModelPath == path )
		{
			return;
		}

		if ( failedThirdPersonDodgeballBindingKey == bindingKey )
			return;

		DestroyThirdPersonDodgeball();
		var model = string.IsNullOrWhiteSpace( path )
			? null
			: Model.Load( path );
		if ( model is null || model.IsError )
		{
			WarnMissingAssetOnce( "third-person dodgeball model", path );
			failedThirdPersonDodgeballBindingKey = bindingKey;
			return;
		}

		// The role-scaled body hierarchy cannot own a world-sized held ball. This
		// local root follows the rendered hand only for the non-native utility.
		thirdPersonDodgeballGripRoot = new GameObject(
			true,
			"Third Person Dodgeball Grip (Local)" )
		{
			NetworkMode = NetworkMode.Never
		};
		thirdPersonDodgeballModelPivot = new GameObject(
			thirdPersonDodgeballGripRoot,
			true,
			"Third Person Dodgeball Pivot" )
		{
			NetworkMode = NetworkMode.Never
		};
		thirdPersonDodgeballObject = new GameObject(
			thirdPersonDodgeballModelPivot,
			true,
			"Third Person Dodgeball" )
		{
			NetworkMode = NetworkMode.Never
		};
		thirdPersonDodgeballRenderer = thirdPersonDodgeballObject.Components
			.Create<ModelRenderer>();
		thirdPersonDodgeballRenderer.Model = model;
		thirdPersonDodgeballRenderer.Tint =
			LargeLadUtilityRules.DodgeballColor;
		thirdPersonDodgeballRenderer.RenderOptions.Game = true;
		thirdPersonDodgeballRenderer.RenderOptions.Overlay = false;
		appliedThirdPersonDodgeballModelPath = path;
		failedThirdPersonDodgeballBindingKey = null;
	}

	private void UpdateThirdPersonDodgeballTransform(
		LargeLadRoleAbilityPresentationState state )
	{
		var bodyRenderer = cachedPlayer?.BodyRenderer;
		if ( !LargeLadUtilityPresentationCatalog.TryGet(
				state.Selection.Utility,
				out var definition ) ||
			thirdPersonDodgeballGripRoot is null ||
			!thirdPersonDodgeballGripRoot.IsValid ||
			thirdPersonDodgeballModelPivot is null ||
			!thirdPersonDodgeballModelPivot.IsValid ||
			thirdPersonDodgeballObject is null ||
			!thirdPersonDodgeballObject.IsValid ||
			bodyRenderer is null )
		{
			SetObjectEnabled( thirdPersonDodgeballObject, false );
			return;
		}

		var bindingKey =
			$"{presentationRevision}:{bodyRenderer.GameObject.Id}:" +
			$"{definition.ThirdPersonWorldModelPath}:{ThirdPersonGripBone}";
		if ( failedThirdPersonAttachmentBindingKey == bindingKey )
		{
			SetObjectEnabled( thirdPersonDodgeballObject, false );
			return;
		}

		if ( !TryGetWorldAttachmentTransform(
			bodyRenderer,
			ThirdPersonGripBone,
			out var holdTransform ) )
		{
			failedThirdPersonAttachmentBindingKey = bindingKey;
			WarnMissingAssetOnce(
				"third-person dodgeball grip attachment",
				ThirdPersonGripBone );
			SetObjectEnabled( thirdPersonDodgeballObject, false );
			return;
		}

		failedThirdPersonAttachmentBindingKey = null;
		SetObjectEnabled( thirdPersonDodgeballObject, true );
		var scale = System.MathF.Max(
			0.01f,
			definition.ThirdPersonModelScale );

		thirdPersonDodgeballGripRoot.WorldScale = Vector3.One;
		thirdPersonDodgeballGripRoot.WorldPosition = holdTransform.Position;
		thirdPersonDodgeballGripRoot.WorldRotation = holdTransform.Rotation;
		thirdPersonDodgeballModelPivot.LocalPosition =
			definition.ThirdPersonModelPosition;
		thirdPersonDodgeballModelPivot.LocalRotation =
			definition.ThirdPersonModelRotation.ToRotation();
		thirdPersonDodgeballModelPivot.LocalScale =
			new Vector3( scale, scale, scale );
		thirdPersonDodgeballObject.LocalTransform = global::Transform.Zero;
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

		return renderer.TryGetBoneTransform( attachmentName, out transform );
	}

	private void ApplyThirdPersonPose(
		LargeLadRoleAbilityPresentationKind kind,
		LargeLadRoleAbilityPresentationView view )
	{
		var renderer = cachedPlayer?.BodyRenderer;
		if ( renderer is null )
			return;

		var useDodgeballPose =
			kind == LargeLadRoleAbilityPresentationKind.Dodgeball ||
			hasRecentThirdPersonDodgeballAttack;
		if ( useDodgeballPose )
		{
			SetThirdPersonPose(
				DodgeballHoldType,
				DodgeballHandedness,
				DodgeballHoldPose,
				DodgeballHandPose,
				DodgeballThirdPersonAttackVariant );
			ownsThirdPersonPose = true;
			return;
		}

		if ( kind == LargeLadRoleAbilityPresentationKind.RoleMelee &&
			view != LargeLadRoleAbilityPresentationView.Hidden )
		{
			SetThirdPersonPose(
				FistsHoldType,
				FistsHandedness,
				FistsHoldPose,
				FistsHandPose,
				FistsThirdPersonAttackVariant );
			ownsThirdPersonPose = true;
			return;
		}

		if ( ownsThirdPersonPose )
			ClearThirdPersonPose();
	}

	private void SetThirdPersonPose(
		CitizenAnimationHelper.HoldTypes holdType,
		CitizenAnimationHelper.Hand handedness,
		float holdPose,
		float handPose,
		float attackVariant )
	{
		var renderer = cachedPlayer?.BodyRenderer;
		renderer?.Set( "holdtype", (int)holdType );
		renderer?.Set( "holdtype_handedness", (int)handedness );
		renderer?.Set( "holdtype_pose", holdPose );
		renderer?.Set( "holdtype_pose_hand", handPose );
		renderer?.Set( "holdtype_attack", attackVariant );
	}

	private void ClearThirdPersonPose()
	{
		var renderer = cachedPlayer?.BodyRenderer;
		renderer?.Set(
			"holdtype",
			(int)CitizenAnimationHelper.HoldTypes.None );
		renderer?.Set(
			"holdtype_handedness",
			(int)CitizenAnimationHelper.Hand.Both );
		renderer?.Set( "holdtype_pose", 0.0f );
		renderer?.Set( "holdtype_pose_hand", 0.0f );
		renderer?.Set( "holdtype_attack", 0.0f );
		ownsThirdPersonPose = false;
	}

	private void TriggerFirstPersonAttack( float attackVariant )
	{
		TrySetFirstPersonParameter( "b_sprint", false );
		TrySetFirstPersonParameter( "holdtype_attack", attackVariant );
		TrySetFirstPersonParameter( "b_attack", true );
	}

	private void TriggerThirdPersonMeleeAttack()
	{
		SetThirdPersonPose(
			FistsHoldType,
			FistsHandedness,
			FistsHoldPose,
			FistsHandPose,
			FistsThirdPersonAttackVariant );
		ownsThirdPersonPose = true;
		cachedPlayer?.BodyRenderer?.Set( "b_attack", true );
	}

	private void TriggerThirdPersonDodgeballAttack()
	{
		hasRecentThirdPersonDodgeballAttack = true;
		timeSinceThirdPersonDodgeballAttack = 0.0f;
		SetThirdPersonPose(
			DodgeballHoldType,
			DodgeballHandedness,
			DodgeballHoldPose,
			DodgeballHandPose,
			DodgeballThirdPersonAttackVariant );
		ownsThirdPersonPose = true;
		cachedPlayer?.BodyRenderer?.Set( "b_attack", true );
	}

	private void UpdateFirstPersonAnimationParameters(
		LargeLadRoleAbilityPresentationState state,
		LargeLadRoleAbilityPresentationKind kind )
	{
		if ( firstPersonArmsRenderer is null ||
			!firstPersonArmsRenderer.UseAnimGraph )
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
		var twoHanded = kind ==
			LargeLadRoleAbilityPresentationKind.RoleMelee;

		if ( kind == LargeLadRoleAbilityPresentationKind.Dodgeball &&
			LargeLadUtilityPresentationCatalog.TryGet(
				state.Selection.Utility,
				out var utilityDefinition ) )
		{
			twoHanded = utilityDefinition.FirstPersonTwoHanded;
			TrySetFirstPersonParameter(
				"skeleton",
				utilityDefinition.FirstPersonSkeleton );
		}
		else
		{
			TrySetFirstPersonParameter( "skeleton", 0 );
		}

		TrySetFirstPersonParameter( "b_grounded", grounded );
		TrySetFirstPersonParameter(
			"b_sprint",
			grounded && wantsRun && horizontalSpeed > 10.0f );
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
		TrySetFirstPersonParameter( "b_twohanded", twoHanded );

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

		firstPersonArmsRenderer.Set( name, value );
		return true;
	}

	private bool TrySetFirstPersonParameter( string name, int value )
	{
		if ( !HasFirstPersonParameter( name ) )
			return false;

		firstPersonArmsRenderer.Set( name, value );
		return true;
	}

	private bool TrySetFirstPersonParameter( string name, float value )
	{
		if ( !HasFirstPersonParameter( name ) )
			return false;

		firstPersonArmsRenderer.Set( name, value );
		return true;
	}

	private bool HasFirstPersonParameter( string name )
	{
		if ( string.IsNullOrWhiteSpace( name ) ||
			firstPersonArmsRenderer is null ||
			!firstPersonArmsRenderer.UseAnimGraph )
		{
			return false;
		}
		if ( supportedFirstPersonParameters.Contains( name ) )
			return true;
		if ( unsupportedFirstPersonParameters.Contains( name ) )
			return false;

		var graph = firstPersonArmsRenderer.AnimationGraph;
		var supported = graph is not null && !graph.IsError &&
			graph.TryGetParameterIndex( name, out _ );
		if ( supported )
			supportedFirstPersonParameters.Add( name );
		else
			unsupportedFirstPersonParameters.Add( name );

		return supported;
	}

	private static void ConfigureFirstPersonRenderer( Renderer renderer )
	{
		renderer.RenderOptions.Game = true;
		renderer.RenderOptions.Overlay = true;
		renderer.RenderOptions.Bloom = false;
		renderer.RenderOptions.AfterUI = false;
	}

	private void DestroyFirstPersonPresentation()
	{
		DestroyFirstPersonDodgeball();

		if ( firstPersonRoot is not null && firstPersonRoot.IsValid )
			firstPersonRoot.Destroy();

		firstPersonRoot = null;
		firstPersonArmsObject = null;
		firstPersonArmsRenderer = null;
		boundCameraObject = null;
		firstPersonKind = LargeLadRoleAbilityPresentationKind.None;
		hasFirstPersonGroundState = false;
		wasFirstPersonGrounded = false;
		supportedFirstPersonParameters.Clear();
		unsupportedFirstPersonParameters.Clear();
	}

	private void DestroyFirstPersonDodgeball()
	{
		if ( firstPersonDodgeballObject is not null &&
			firstPersonDodgeballObject.IsValid )
		{
			firstPersonDodgeballObject.Destroy();
		}

		firstPersonDodgeballObject = null;
		firstPersonDodgeballRenderer = null;
	}

	private void DestroyThirdPersonDodgeball()
	{
		if ( thirdPersonDodgeballGripRoot is not null &&
			thirdPersonDodgeballGripRoot.IsValid )
		{
			thirdPersonDodgeballGripRoot.Destroy();
		}

		thirdPersonDodgeballGripRoot = null;
		thirdPersonDodgeballModelPivot = null;
		thirdPersonDodgeballObject = null;
		thirdPersonDodgeballRenderer = null;
		appliedThirdPersonDodgeballModelPath = null;
	}

	private void ResetPresentation(
		bool restoreBody,
		bool clearPose )
	{
		presentationRevision++;
		DestroyFirstPersonPresentation();
		DestroyThirdPersonDodgeball();
		hasRecentThirdPersonDodgeballAttack = false;
		hasCurrentState = false;

		if ( restoreBody )
			SetBodyVisible( true );
		if ( clearPose && ownsThirdPersonPose )
			ClearThirdPersonPose();
		else if ( !clearPose )
			ownsThirdPersonPose = false;
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

	private void WarnMissingAssetOnce( string usage, string path )
	{
		var key = $"{usage}:{path}";
		if ( !warnedMissingAssets.Add( key ) )
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
