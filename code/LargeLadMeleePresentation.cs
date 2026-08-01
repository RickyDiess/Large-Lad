using Sandbox;
using Sandbox.Citizen;

/// <summary>
/// First-person role-arm presentation plus local and replicated ordinary melee
/// swings for Skinny Kids and Minions. Large Lad primary sequencing belongs to
/// Eat. Runtime arms, weapon models, grip transforms, and animation state live
/// here.
/// </summary>
public sealed class LargeLadMeleePresentation : Component
{
	[Property, Group( "Melee Model" )]
	public Model MeleeModel { get; set; } =
		Model.Load( "models/citizen_props/crowbar01.vmdl" );

	[Property, Group( "Melee Model" ), Title( "Skinny Kid Hold Bone" )]
	public string MeleeModelBone { get; set; } = "hold_R";

	[Property, Group( "Melee Model" ), Title( "Hold-relative Position" )]
	public Vector3 MeleeModelPosition { get; set; } = Vector3.Zero;

	[Property, Group( "Melee Model" ), Title( "Hold-relative Angles" )]
	public Angles MeleeModelAngles { get; set; } =
		new( 0.0f, 0.0f, 0.0f );

	[Property, Group( "Melee Model" ), Title( "Model-space Grip Point" )]
	public Vector3 MeleeModelGripPosition { get; set; } = Vector3.Zero;

	[Property, Group( "Melee Model" )]
	public float MeleeModelScale { get; set; } = 0.25f;

	[Property, Group( "First Person" ), Title( "Show Melee Arms" )]
	public bool ShowFirstPersonArms { get; set; } = true;

	[Property, Group( "First Person" ), Title( "Camera-relative Adjustment" )]
	public Vector3 FirstPersonArmsPosition { get; set; } = Vector3.Zero;

	[Property, Group( "First Person" )]
	public Angles FirstPersonArmsAngles { get; set; } = Angles.Zero;

	private GameObject meleeModelPivotObject;
	private GameObject meleeModelObject;
	private ModelRenderer meleeModelRenderer;
	private Model appliedMeleeModel;
	private GameObject firstPersonArmsObject;
	private SkinnedModelRenderer firstPersonArmsRenderer;
	private Model appliedFirstPersonArmsModel;
	private LargeLadPlayer cachedPlayer;
	private PlayerController cachedController;

	protected override void OnAwake()
	{
		ResolveCachedReferences();
	}

	protected override void OnStart()
	{
		ResolveCachedReferences();
	}

	protected override void OnUpdate()
	{
		UpdateMeleePose();
		UpdateFirstPersonArms();
		UpdateMeleeModel();
	}

	protected override void OnDestroy()
	{
		DestroyMeleeModel();
		DestroyFirstPersonArms();
	}

	internal void TriggerPredictedSwing()
	{
		TriggerMeleeSwingAnimation();
	}

	internal void BroadcastSwing()
	{
		BroadcastMeleeSwingAnimation();
	}

	private void UpdateMeleeModel()
	{
		var player = cachedPlayer;

		if ( player?.Role is LargeLadRole.LargeLad or
			LargeLadRole.Minion )
		{
			DestroyMeleeModel();
			return;
		}

		var shouldShow = MeleeModel is not null &&
			player?.Role == LargeLadRole.SkinnyKid &&
			player.EquippedWeapon == LargeLadWeaponId.Melee &&
			player.Health?.IsDead == false;

		if ( !shouldShow )
		{
			if ( meleeModelPivotObject is not null &&
				meleeModelPivotObject.IsValid )
			{
				meleeModelPivotObject.Enabled = false;
			}

			return;
		}

		EnsureMeleeModel();

		if ( meleeModelPivotObject is null ||
			!meleeModelPivotObject.IsValid ||
			meleeModelObject is null ||
			!meleeModelObject.IsValid )
		{
			return;
		}

		meleeModelPivotObject.Enabled = true;

		var controller = cachedController;
		var useFirstPersonGrip = !IsProxy &&
			controller?.ThirdPerson == false &&
			firstPersonArmsObject is not null &&
			firstPersonArmsObject.IsValid &&
			firstPersonArmsObject.Enabled &&
			firstPersonArmsRenderer is not null;
		var holdRenderer = useFirstPersonGrip
			? firstPersonArmsRenderer
			: player.BodyRenderer;

		if ( holdRenderer is null ||
			string.IsNullOrWhiteSpace( MeleeModelBone ) ||
			!holdRenderer.TryGetBoneTransform(
				MeleeModelBone,
				out var holdTransform ) )
		{
			meleeModelPivotObject.Enabled = false;
			return;
		}

		var position = holdTransform.PointToWorld( MeleeModelPosition );
		var rotation =
			holdTransform.Rotation * MeleeModelAngles.ToRotation();

		var scale = System.MathF.Max( 0.01f, MeleeModelScale );

		meleeModelRenderer.RenderOptions.Game = !useFirstPersonGrip;
		meleeModelRenderer.RenderOptions.Overlay = useFirstPersonGrip;

		// Keep the authored grip point locked to the hand at every scale.
		meleeModelPivotObject.WorldTransform = new Transform(
			position,
			rotation,
			Vector3.One );
		meleeModelObject.LocalTransform = new Transform(
			-MeleeModelGripPosition * scale,
			Rotation.Identity,
			new Vector3( scale ) );
	}

	private void UpdateFirstPersonArms()
	{
		var player = cachedPlayer;
		var controller = cachedController;
		var shouldShow = ShowFirstPersonArms &&
			!IsProxy &&
			controller?.ThirdPerson == false &&
			(player?.Role is LargeLadRole.SkinnyKid or
				LargeLadRole.LargeLad or LargeLadRole.Minion) &&
			player.EquippedWeapon == LargeLadWeaponId.Melee &&
			player.Health?.IsDead == false &&
			player.BodyRenderer is not null;

		if ( !shouldShow )
		{
			if ( firstPersonArmsObject is not null &&
				firstPersonArmsObject.IsValid )
			{
				firstPersonArmsObject.Enabled = false;
			}

			return;
		}

		EnsureFirstPersonArms( player );

		if ( firstPersonArmsObject is null ||
			!firstPersonArmsObject.IsValid ||
			firstPersonArmsRenderer is null )
		{
			return;
		}

		var bodyTransform = player.BodyRenderer.GameObject.WorldTransform;
		var eyeTransform = controller.EyeTransform;
		bodyTransform.Position +=
			eyeTransform.Rotation.Forward * FirstPersonArmsPosition.x +
			eyeTransform.Rotation.Right * FirstPersonArmsPosition.y +
			eyeTransform.Rotation.Up * FirstPersonArmsPosition.z;
		bodyTransform.Rotation *= FirstPersonArmsAngles.ToRotation();

		firstPersonArmsObject.Enabled = true;
		firstPersonArmsObject.WorldTransform = bodyTransform;
		firstPersonArmsRenderer.BoneMergeTarget = player.BodyRenderer;
		firstPersonArmsRenderer.Tint = player.BodyRenderer.Tint;
	}

	private void EnsureFirstPersonArms( LargeLadPlayer player )
	{
		var bodyRenderer = player?.BodyRenderer;
		var bodyModel = bodyRenderer?.Model;

		if ( bodyModel is null )
			return;

		if ( firstPersonArmsObject is null ||
			!firstPersonArmsObject.IsValid )
		{
			firstPersonArmsObject = new GameObject(
				GameObject,
				true,
				"First Person Melee Arms (Runtime)" )
			{
				NetworkMode = NetworkMode.Never
			};
			firstPersonArmsRenderer =
				firstPersonArmsObject.Components
					.Create<SkinnedModelRenderer>();
			appliedFirstPersonArmsModel = null;
		}

		if ( firstPersonArmsRenderer is null ||
			appliedFirstPersonArmsModel == bodyModel )
		{
			return;
		}

		firstPersonArmsRenderer.Model = bodyModel;
		firstPersonArmsRenderer.BoneMergeTarget = bodyRenderer;
		firstPersonArmsRenderer.SetBodyGroup( "Head", 5 );
		firstPersonArmsRenderer.SetBodyGroup( "Chest", 0 );
		firstPersonArmsRenderer.SetBodyGroup( "Legs", 1 );
		firstPersonArmsRenderer.SetBodyGroup( "Hands", 0 );
		firstPersonArmsRenderer.SetBodyGroup( "Feet", 1 );
		firstPersonArmsRenderer.RenderOptions.Game = false;
		firstPersonArmsRenderer.RenderOptions.Overlay = true;
		firstPersonArmsRenderer.RenderOptions.Bloom = false;
		firstPersonArmsRenderer.RenderOptions.AfterUI = false;
		appliedFirstPersonArmsModel = bodyModel;
	}

	private void DestroyFirstPersonArms()
	{
		if ( firstPersonArmsObject is not null &&
			firstPersonArmsObject.IsValid )
		{
			firstPersonArmsObject.Destroy();
		}

		firstPersonArmsObject = null;
		firstPersonArmsRenderer = null;
		appliedFirstPersonArmsModel = null;
	}

	private void UpdateMeleePose()
	{
		var player = cachedPlayer;

		if ( player?.BodyRenderer is null )
			return;

		var isMeleeReadied = player.EquippedWeapon ==
			LargeLadWeaponId.Melee &&
			player.Health?.IsDead == false;
		var holdType = player.Role switch
		{
			LargeLadRole.SkinnyKid when isMeleeReadied =>
				(int)CitizenAnimationHelper.HoldTypes.Swing,
			LargeLadRole.LargeLad when isMeleeReadied =>
				(int)CitizenAnimationHelper.HoldTypes.Punch,
			LargeLadRole.Minion when isMeleeReadied =>
				(int)CitizenAnimationHelper.HoldTypes.Punch,
			_ => (int)CitizenAnimationHelper.HoldTypes.None
		};

		player.BodyRenderer.Set( "holdtype", holdType );
	}

	private void TriggerMeleeSwingAnimation()
	{
		var player = cachedPlayer;
		player?.BodyRenderer?.Set( "b_attack", true );
	}

	[Rpc.Broadcast]
	private void BroadcastMeleeSwingAnimation()
	{
		// The owner already predicted this animation immediately on input.
		if ( !IsProxy )
			return;

		TriggerMeleeSwingAnimation();
	}

	private void EnsureMeleeModel()
	{
		if ( MeleeModel is null )
		{
			if ( meleeModelPivotObject is not null &&
				meleeModelPivotObject.IsValid )
			{
				meleeModelPivotObject.Enabled = false;
			}

			appliedMeleeModel = null;
			return;
		}

		if ( meleeModelPivotObject is null ||
			!meleeModelPivotObject.IsValid )
		{
			meleeModelPivotObject = new GameObject(
				GameObject,
				true,
				"Melee Grip (Runtime)" )
			{
				NetworkMode = NetworkMode.Never
			};
			meleeModelObject = null;
			meleeModelRenderer = null;
			appliedMeleeModel = null;
		}

		if ( meleeModelObject is null || !meleeModelObject.IsValid )
		{
			meleeModelObject = new GameObject(
				meleeModelPivotObject,
				true,
				"Melee Model (Runtime)" )
			{
				NetworkMode = NetworkMode.Never
			};
			meleeModelRenderer =
				meleeModelObject.Components.Create<ModelRenderer>();
			appliedMeleeModel = null;
		}

		if ( meleeModelRenderer is not null &&
			appliedMeleeModel != MeleeModel )
		{
			meleeModelRenderer.Model = MeleeModel;
			appliedMeleeModel = MeleeModel;
		}
	}

	private void DestroyMeleeModel()
	{
		if ( meleeModelPivotObject is not null &&
			meleeModelPivotObject.IsValid )
		{
			meleeModelPivotObject.Destroy();
		}
		else if ( meleeModelObject is not null &&
			meleeModelObject.IsValid )
		{
			meleeModelObject.Destroy();
		}

		meleeModelPivotObject = null;
		meleeModelObject = null;
		meleeModelRenderer = null;
		appliedMeleeModel = null;
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
	}
}
