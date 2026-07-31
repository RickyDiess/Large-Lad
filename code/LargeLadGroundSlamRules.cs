using Sandbox;

public enum LargeLadGroundSlamPropBehavior
{
	Move,
	Unanchor,
	Break
}

/// <summary>
/// Pure Ground Slam decisions shared by runtime combat and unit tests.
/// Networking, traces, physics, and presentation remain in their components.
/// </summary>
public static class LargeLadGroundSlamRules
{
	public const float MaximumHorizontalImpulse = 250000.0f;
	public const float MaximumUpwardImpulse = 250000.0f;

	public static bool CanAffectPlayer(
		LargeLadRole role,
		bool isLivingAndActive,
		bool isObstructed,
		float distance,
		float radius )
	{
		return role is LargeLadRole.SkinnyKid or LargeLadRole.Minion &&
			isLivingAndActive &&
			!isObstructed &&
			float.IsFinite( distance ) &&
			float.IsFinite( radius ) &&
			radius > 0.0f &&
			distance <= radius;
	}

	public static bool ShouldStaggerPlayer( LargeLadRole role )
	{
		return role == LargeLadRole.SkinnyKid;
	}

	public static bool CanReactivePropReact(
		bool isExplicitlyDesignated,
		bool isCriticalGameplayObject,
		bool isAuthoritativeBlocker,
		LargeLadGroundSlamPropBehavior behavior,
		bool hasUsableRigidbody )
	{
		if ( !isExplicitlyDesignated ||
			isCriticalGameplayObject ||
			isAuthoritativeBlocker ||
			!System.Enum.IsDefined( typeof( LargeLadGroundSlamPropBehavior ), behavior ) )
		{
			return false;
		}

		return behavior == LargeLadGroundSlamPropBehavior.Break ||
			hasUsableRigidbody;
	}

	public static float ClampHorizontalImpulse( float impulse )
	{
		return ClampFinite(
			impulse,
			MaximumHorizontalImpulse );
	}

	public static float ClampUpwardImpulse( float impulse )
	{
		return ClampFinite(
			impulse,
			MaximumUpwardImpulse );
	}

	public static Vector3 GetRadialImpulse(
		Vector3 origin,
		Vector3 target,
		float horizontalImpulse,
		float upwardImpulse,
		bool usePositiveXWhenCoincident )
	{
		var offset = target - origin;
		offset.z = 0.0f;
		var direction = offset.LengthSquared > 0.0001f
			? offset.Normal
			: new Vector3(
				usePositiveXWhenCoincident ? 1.0f : -1.0f,
				0.0f,
				0.0f );

		return direction * ClampHorizontalImpulse( horizontalImpulse ) +
			Vector3.Up * ClampUpwardImpulse( upwardImpulse );
	}

	private static float ClampFinite( float value, float maximum )
	{
		if ( !float.IsFinite( value ) )
			return 0.0f;

		return System.Math.Clamp( value, 0.0f, maximum );
	}
}
