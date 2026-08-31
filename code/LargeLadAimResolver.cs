using Sandbox;

public enum LargeLadAimValidationFailure
{
	None,
	MissingContext,
	NonFiniteVector,
	InvalidLength,
	OutOfRange,
	Misaligned
}

/// <summary>
/// The complete result of resolving camera intent into a shot from the
/// authoritative player eye. The host result intentionally has no view trace.
/// </summary>
public struct LargeLadAimResolution
{
	public bool IsValid { get; init; }
	public Ray ViewRay { get; init; }
	public bool HasViewTrace { get; init; }
	public SceneTraceResult ViewTrace { get; init; }
	public Vector3 DesiredAimPoint { get; init; }
	public Vector3 ShotOrigin { get; init; }
	public Vector3 ShotDirection { get; init; }
	public SceneTraceResult ShotTrace { get; init; }
	public Vector3 ActualImpactPoint { get; init; }
	public bool IsObstructed { get; init; }
}

/// <summary>
/// Shared, narrowly scoped two-stage firearm aim calculation for local HUD
/// prediction and authoritative host traces.
/// </summary>
public static class LargeLadAimResolver
{
	// PlayerController.CameraOffset is currently about 195 units. This bound
	// permits the composed third-person view while rejecting remote aim points.
	public const float MaximumViewOriginOffset = 256.0f;
	public const float MinimumAimAlignmentDot = 0.35f;

	private const float MinimumAimDistance = 1.0f;
	private const float MatchingImpactTolerance = 1.0f;
	private const float HostObstructionTolerance = 4.0f;

	public static bool TryResolveLocal(
		Scene scene,
		CameraComponent camera,
		PlayerController controller,
		GameObject shooter,
		float range,
		out LargeLadAimResolution resolution )
	{
		resolution = default;

		if ( scene is null || camera is null || controller is null ||
			shooter is null || !IsValidRange( range ) )
		{
			return false;
		}

		var viewRay = camera.View.ForwardRay;

		if ( !IsFinite( viewRay.Position ) || !IsDirection( viewRay.Forward ) )
			return false;

		var shotOrigin = controller.EyePosition;
		var viewOffset = viewRay.Position - shotOrigin;

		if ( !IsFinite( shotOrigin ) || !IsFiniteLengthSquared( viewOffset.LengthSquared ) ||
			viewOffset.LengthSquared > MaximumViewOriginOffset * MaximumViewOriginOffset )
		{
			return false;
		}

		var viewTrace = scene.Trace
			.Ray( viewRay, range )
			.UseHitboxes( true )
			.WithoutTags(
				LargeLadGameplayRules.MinionPassageTag,
				LargeLadGameplayRules.PlayerMovementCollisionTag )
			.IgnoreGameObjectHierarchy( shooter )
			.Run();
		var desiredAimPoint = viewTrace.EndPosition;

		if ( !TryBuildShotDirection(
			controller,
			desiredAimPoint,
			range,
			out var shotDirection,
			out _ ) )
		{
			resolution = new LargeLadAimResolution
			{
				ViewRay = viewRay,
				HasViewTrace = true,
				ViewTrace = viewTrace,
				DesiredAimPoint = desiredAimPoint,
				ShotOrigin = shotOrigin
			};
			return false;
		}

		var shotTrace = RunShotTrace(
			scene,
			shooter,
			shotOrigin,
			shotDirection,
			range );
		var changedImpact = viewTrace.Hit != shotTrace.Hit ||
			(viewTrace.Hit && shotTrace.Hit &&
				viewTrace.EndPosition.DistanceSquared( shotTrace.EndPosition ) >
					MatchingImpactTolerance * MatchingImpactTolerance);

		resolution = new LargeLadAimResolution
		{
			IsValid = true,
			ViewRay = viewRay,
			HasViewTrace = true,
			ViewTrace = viewTrace,
			DesiredAimPoint = desiredAimPoint,
			ShotOrigin = shotOrigin,
			ShotDirection = shotDirection,
			ShotTrace = shotTrace,
			ActualImpactPoint = shotTrace.EndPosition,
			IsObstructed = changedImpact
		};
		return true;
	}

	public static bool TryResolveAuthoritative(
		Scene scene,
		PlayerController controller,
		GameObject shooter,
		float range,
		Vector3 desiredAimPoint,
		out LargeLadAimResolution resolution,
		out LargeLadAimValidationFailure failure )
	{
		resolution = default;
		failure = LargeLadAimValidationFailure.None;

		if ( scene is null || controller is null || shooter is null ||
			!IsValidRange( range ) )
		{
			failure = LargeLadAimValidationFailure.MissingContext;
			return false;
		}

		if ( !TryBuildShotDirection(
			controller,
			desiredAimPoint,
			range,
			out var shotDirection,
			out failure ) )
		{
			return false;
		}

		var shotOrigin = controller.EyePosition;
		var desiredDistance = (desiredAimPoint - shotOrigin).Length;
		var shotTrace = RunShotTrace(
			scene,
			shooter,
			shotOrigin,
			shotDirection,
			range );
		var expectedDistance = System.MathF.Min( desiredDistance, range );
		var isObstructed = shotTrace.Hit &&
			shotTrace.Distance + HostObstructionTolerance < expectedDistance;

		resolution = new LargeLadAimResolution
		{
			IsValid = true,
			DesiredAimPoint = desiredAimPoint,
			ShotOrigin = shotOrigin,
			ShotDirection = shotDirection,
			ShotTrace = shotTrace,
			ActualImpactPoint = shotTrace.EndPosition,
			IsObstructed = isObstructed
		};
		return true;
	}

	public static bool IsFinite( Vector3 value )
	{
		return float.IsFinite( value.x ) &&
			float.IsFinite( value.y ) &&
			float.IsFinite( value.z );
	}

	private static bool TryBuildShotDirection(
		PlayerController controller,
		Vector3 desiredAimPoint,
		float range,
		out Vector3 shotDirection,
		out LargeLadAimValidationFailure failure )
	{
		shotDirection = default;
		failure = LargeLadAimValidationFailure.None;

		var shotOrigin = controller.EyePosition;

		if ( !IsFinite( shotOrigin ) || !IsFinite( desiredAimPoint ) )
		{
			failure = LargeLadAimValidationFailure.NonFiniteVector;
			return false;
		}

		var towardAimPoint = desiredAimPoint - shotOrigin;
		var distanceSquared = towardAimPoint.LengthSquared;

		if ( !IsFiniteLengthSquared( distanceSquared ) ||
			distanceSquared < MinimumAimDistance * MinimumAimDistance )
		{
			failure = LargeLadAimValidationFailure.InvalidLength;
			return false;
		}

		var maximumDistance = range + MaximumViewOriginOffset;

		if ( distanceSquared > maximumDistance * maximumDistance )
		{
			failure = LargeLadAimValidationFailure.OutOfRange;
			return false;
		}

		shotDirection = towardAimPoint.Normal;
		var eyeForward = controller.EyeTransform.Rotation.Forward;

		if ( !IsDirection( shotDirection ) || !IsDirection( eyeForward ) )
		{
			failure = LargeLadAimValidationFailure.InvalidLength;
			return false;
		}

		eyeForward = eyeForward.Normal;

		if ( Vector3.Dot( eyeForward, shotDirection ) < MinimumAimAlignmentDot )
		{
			failure = LargeLadAimValidationFailure.Misaligned;
			return false;
		}

		return true;
	}

	private static SceneTraceResult RunShotTrace(
		Scene scene,
		GameObject shooter,
		Vector3 shotOrigin,
		Vector3 shotDirection,
		float range )
	{
		return scene.Trace
			.Ray( shotOrigin, shotOrigin + shotDirection * range )
			.UseHitboxes( true )
			.WithoutTags(
				LargeLadGameplayRules.MinionPassageTag,
				LargeLadGameplayRules.PlayerMovementCollisionTag )
			.IgnoreGameObjectHierarchy( shooter )
			.Run();
	}

	private static bool IsDirection( Vector3 value )
	{
		if ( !IsFinite( value ) )
			return false;

		var lengthSquared = value.LengthSquared;
		return IsFiniteLengthSquared( lengthSquared ) && lengthSquared > 0.001f;
	}

	private static bool IsValidRange( float range )
	{
		return float.IsFinite( range ) && range >= MinimumAimDistance;
	}

	private static bool IsFiniteLengthSquared( float value )
	{
		return float.IsFinite( value ) && value >= 0.0f;
	}
}
