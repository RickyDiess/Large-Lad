using Sandbox;
using System.Linq;

public sealed partial class LargeLadFirearm
{
	[Property, Group( "World Presentation" )]
	public bool AimDrivenPresentation { get; set; }

	/// <summary>Carry adjustment from the animated right shoulder, in yaw space. Scales with the body.</summary>
	[Property, Group( "World Presentation" )]
	public Vector3 CarryPivot { get; set; } = Vector3.Zero;

	/// <summary>Right grip position relative to the pivot, in aim space.</summary>
	[Property, Group( "World Presentation" )]
	public Vector3 CarryGripOffset { get; set; } = new( 12, 0, -2 );

	[Property, Group( "World Presentation" )]
	public float CarryBodyYaw { get; set; } = -10;

	/// <summary>Fine adjustment in weapon space, preserving the authored support marker.</summary>
	[Property, Group( "World Presentation" )]
	public Vector3 SupportHandOffset { get; set; } = Vector3.Zero;

	[Property, Group( "World Presentation" )]
	public Angles SupportHandRotation { get; set; } = Angles.Zero;

	private const float NativePoseReturnDuration = 0.2f;
	private bool returningFromNativePose;
	private TimeSince timeSinceNativePose;
	private Transform nativeWeaponPose;
	private Transform nativeSupportHandPose;

	internal bool TryUpdateAimDrivenPresentation( SkinnedModelRenderer body,
		out Transform rightHand, out Transform leftHand )
	{
		rightHand = leftHand = default;
		if ( !LargeLadWeaponPresentationAim.TryGetRotation( Owner, out var aim, out var yaw ) ||
			WorldModel is null || !WorldModel.IsValid ||
			!TryGetLeftHandGrip( out _ ) )
			return false;

		if ( cachedRightHandGripWorldModel != WorldModel ||
			cachedRightHandGrip is null || !cachedRightHandGrip.IsValid )
		{
			cachedRightHandGripWorldModel = WorldModel;
			cachedRightHandGrip = WorldModel.GetAllObjects( true )
				.FirstOrDefault( x => x.Name == RightHandGripName );
		}

		var weaponRenderer = WorldModel.Components.Get<BaseWeaponModel>()?.Renderer;
		var muzzle = weaponRenderer?.Model?.Attachments.GetTransform( MuzzleAttachment );
		var holdBone = body.Model?.Bones.GetBone( HoldBone );
		var shoulderBone = body.Model?.Bones.GetBone( "arm_upper_R" );
		if ( cachedRightHandGrip is null || !cachedRightHandGrip.IsValid ||
			!muzzle.HasValue || holdBone?.Parent?.Name != "hand_R" ||
			shoulderBone is null || body.SceneModel is null ||
			body.Model.Bones.GetBone( "hand_L" ) is null )
			return false;

		// Read authored relationships in weapon space, never a previous animated
		// hand's world pose. The muzzle's model attachment defines the barrel axis.
		var rightInWeapon = WorldModel.WorldTransform.ToLocal( cachedRightHandGrip.WorldTransform );
		var muzzleInWeapon = WorldModel.WorldTransform.ToLocal( weaponRenderer.WorldTransform )
			.ToWorld( muzzle.Value );
		// Entering from native parenting (equip or reload): capture the source once
		// relative to the body (gun) and gun (support hand), so both follow movement.
		if ( WorldModel.Parent != Owner.GameObject )
		{
			nativeWeaponPose = body.WorldTransform.ToLocal( WorldModel.WorldTransform );
			nativeSupportHandPose = WorldModel.WorldTransform.ToLocal(
				body.WorldTransform.ToWorld( ReadModelSpacePose( body, "hand_L" ) ) );
			timeSinceNativePose = 0;
			returningFromNativePose = true;
		}
		var blend = returningFromNativePose
			? MathX.Clamp( timeSinceNativePose / NativePoseReturnDuration, 0, 1 ) : 1;
		returningFromNativePose &= blend < 1;
		blend = blend * blend * (3 - 2 * blend);
		body.SetLookDirection( "aim_body",
			Rotation.FromYaw( CarryBodyYaw * blend ) * aim.Forward, 1.0f );
		var scale = body.WorldScale;
		// The shoulder's position is upstream of arm IK. Read its local pose and
		// rebase it onto this frame's body transform, avoiding world-space lag.
		// Only the animation contribution is from the last evaluated pose.
		var shoulderPose = ReadModelSpacePose( body, "arm_upper_R" );
		var shoulder = body.WorldTransform.PointToWorld( shoulderPose.Position );
		var gripPosition = shoulder + yaw * (CarryPivot * scale) +
			aim * (CarryGripOffset * scale);
		var weaponRotation = aim * muzzleInWeapon.Rotation.Inverse;
		var weaponTransform = new Transform(
			gripPosition - weaponRotation * (rightInWeapon.Position * scale),
			weaponRotation, scale );

		// The native model still owns its lifetime. Only its presentation parent
		// changes: it must not be below either arm that these targets will solve.
		if ( blend < 1 )
			weaponTransform = global::Transform.Lerp(
				body.WorldTransform.ToWorld( nativeWeaponPose ), weaponTransform, blend, true );
		if ( WorldModel.Parent != Owner.GameObject )
			WorldModel.SetParent( Owner.GameObject, true );
		WorldModel.WorldTransform = weaponTransform;

		// Existing RightHandGrip markers use the native hold_R attachment frame.
		// Convert using the native pose's attachment-to-wrist offset; hold_R is
		// animated relative to the wrist and differs from the model bind pose.
		rightHand = cachedRightHandGrip.WorldTransform.ToWorld(
			body.SceneModel.GetBoneLocalTransform( HoldBone ).ToLocal( global::Transform.Zero ) );
		leftHand = cachedLeftHandGrip.WorldTransform;
		leftHand.Position += WorldModel.WorldRotation * (SupportHandOffset * scale);
		leftHand.Rotation *= SupportHandRotation.ToRotation();
		// Blend the support hand relative to the moving gun, not on a separate
		// body-space path that would make it trail the gun during the return.
		if ( blend < 1 )
			leftHand = WorldModel.WorldTransform.ToWorld( global::Transform.Lerp(
				nativeSupportHandPose, WorldModel.WorldTransform.ToLocal( leftHand ), blend, true ) );
		return true;
	}

	private static Transform ReadModelSpacePose( SkinnedModelRenderer body, string boneName )
	{
		var bone = body.Model.Bones.GetBone( boneName );
		var pose = body.SceneModel.GetBoneLocalTransform( bone.Index );
		for ( var parent = bone.Parent; parent is not null; parent = parent.Parent )
			pose = body.SceneModel.GetBoneLocalTransform( parent.Index ).ToWorld( pose );
		return pose;
	}
}

/// <summary>
/// Presentation-only adapter for the current aiming prototype. Replace this
/// direction policy when gameplay aim changes; placement and grips need not change.
/// </summary>
internal static class LargeLadWeaponPresentationAim
{
	internal static bool TryGetRotation( PlayerController owner, out Rotation rotation, out Rotation yaw )
	{
		rotation = yaw = Rotation.Identity;
		if ( owner is null || !owner.IsValid )
			return false;

		// EyeTransform is computed from current controller input before UpdateBones,
		// on the owner and proxies. It does not depend on the animated head. Do not
		// use BaseCombatWeapon.AimRay here: a proxy can pick this peer's scene camera.
		var direction = owner.EyeTransform.Rotation.Forward;
		if ( !LargeLadAimResolver.IsFinite( direction ) || direction.LengthSquared < 0.001f ||
			!float.IsFinite( owner.EyeAngles.yaw ) )
			return false;
		// Keep the controller's orientation at vertical pitch; rebuilding it from
		// forward/up loses heading when those vectors become parallel.
		rotation = owner.EyeTransform.Rotation;
		yaw = Rotation.FromYaw( owner.EyeAngles.yaw );
		return true;
	}
}
