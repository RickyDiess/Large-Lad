using Sandbox;

public enum LargeLadDodgeballHitOutcome
{
	None,
	MinionKill,
	LargeLadHit
}

public readonly struct LargeLadDodgeballImpactDecision
{
	public LargeLadDodgeballImpactDecision(
		bool consumesThrow,
		LargeLadDodgeballHitOutcome outcome )
	{
		ConsumesThrow = consumesThrow;
		Outcome = outcome;
	}

	public bool ConsumesThrow { get; }
	public LargeLadDodgeballHitOutcome Outcome { get; }
	public bool AppliesCombatEffect =>
		Outcome != LargeLadDodgeballHitOutcome.None;
}

/// <summary>
/// Pure dodgeball decisions shared by the authoritative runtime and focused
/// lifecycle tests. A throw becomes harmless on its first non-self contact.
/// </summary>
public static class LargeLadDodgeballRules
{
	public const string CollisionTag = "large_lad_dodgeball";
	public const float MaximumThrowSpeed = 2400.0f;
	public const float MaximumLinearSpeed = 2800.0f;
	public const float MaximumAngularSpeed = 3600.0f;
	public const float MaximumLargeLadDamage = 5.0f;
	public const float MaximumHorizontalKnockbackImpulse = 300000.0f;
	public const float MaximumUpwardKnockbackImpulse = 150000.0f;

	public static bool CanPickup(
		LargeLadUtilityLocation location,
		bool available,
		float now,
		float pickupUnlockTime )
	{
		return available &&
			location is (LargeLadUtilityLocation.OriginAvailable or
				LargeLadUtilityLocation.Dropped or
				LargeLadUtilityLocation.Thrown) &&
			float.IsFinite( now ) &&
			float.IsFinite( pickupUnlockTime ) &&
			now >= pickupUnlockTime;
	}

	public static LargeLadDodgeballImpactDecision ResolveImpact(
		LargeLadUtilityLocation location,
		bool impactAlreadyConsumed,
		bool hitThrowerDuringGrace,
		LargeLadRole throwerRole,
		LargeLadRole targetRole,
		bool targetIsLiving,
		float impactSpeed,
		float minimumCombatSpeed )
	{
		if ( location != LargeLadUtilityLocation.Thrown ||
			impactAlreadyConsumed )
		{
			return default;
		}

		if ( hitThrowerDuringGrace )
			return default;

		var fastEnough = float.IsFinite( impactSpeed ) &&
			impactSpeed >= System.MathF.Max( 0.0f, minimumCombatSpeed );
		var validThrower = throwerRole == LargeLadRole.SkinnyKid;

		if ( validThrower && fastEnough && targetIsLiving )
		{
			if ( targetRole == LargeLadRole.Minion )
			{
				return new LargeLadDodgeballImpactDecision(
					consumesThrow: true,
					LargeLadDodgeballHitOutcome.MinionKill );
			}

			if ( targetRole == LargeLadRole.LargeLad )
			{
				return new LargeLadDodgeballImpactDecision(
					consumesThrow: true,
					LargeLadDodgeballHitOutcome.LargeLadHit );
			}
		}

		// World, friendly, dead-player, low-speed, and otherwise invalid contacts
		// still make this a dead ball, but never create a damage event.
		return new LargeLadDodgeballImpactDecision(
			consumesThrow: true,
			LargeLadDodgeballHitOutcome.None );
	}

	public static float GetMinionKillDamage( float currentHealth )
	{
		return SafeNonNegative( currentHealth );
	}

	public static float GetLargeLadDamage( float configuredDamage )
	{
		return ClampFinite(
			configuredDamage,
			0.0f,
			MaximumLargeLadDamage );
	}

	public static Vector3 GetThrowVelocity(
		Vector3 direction,
		float configuredSpeed,
		Vector3 inheritedVelocity )
	{
		var safeDirection = direction.LengthSquared > 0.0001f
			? direction.Normal
			: Vector3.Forward;
		var speed = ClampFinite(
			configuredSpeed,
			0.0f,
			MaximumThrowSpeed );
		return ClampVelocity(
			safeDirection * speed + inheritedVelocity,
			MaximumLinearSpeed );
	}

	public static Vector3 GetLargeLadKnockbackImpulse(
		Vector3 awayFromThrower,
		Vector3 fallbackDirection,
		float configuredHorizontalImpulse,
		float configuredUpwardImpulse )
	{
		var horizontalDirection = awayFromThrower;
		horizontalDirection.z = 0.0f;

		if ( horizontalDirection.LengthSquared <= 0.0001f )
		{
			horizontalDirection = fallbackDirection;
			horizontalDirection.z = 0.0f;
		}

		horizontalDirection = horizontalDirection.LengthSquared > 0.0001f
			? horizontalDirection.Normal
			: Vector3.Forward;

		return horizontalDirection * ClampFinite(
				configuredHorizontalImpulse,
				0.0f,
				MaximumHorizontalKnockbackImpulse ) +
			Vector3.Up * ClampFinite(
				configuredUpwardImpulse,
				0.0f,
				MaximumUpwardKnockbackImpulse );
	}

	public static bool HasSolidPlayerCollision( LargeLadRole role )
	{
		return role is LargeLadRole.LargeLad or LargeLadRole.Minion;
	}

	public static Vector3 ClampVelocity(
		Vector3 velocity,
		float configuredMaximumSpeed )
	{
		var maximumSpeed = ClampFinite(
			configuredMaximumSpeed,
			0.0f,
			MaximumLinearSpeed );
		var maximumSpeedSquared = maximumSpeed * maximumSpeed;

		if ( maximumSpeed <= 0.0f )
			return Vector3.Zero;

		return velocity.LengthSquared <= maximumSpeedSquared
			? velocity
			: velocity.Normal * maximumSpeed;
	}

	public static float ClampAngularSpeed(
		float speed,
		float configuredMaximumSpeed )
	{
		var maximumSpeed = ClampFinite(
			configuredMaximumSpeed,
			0.0f,
			MaximumAngularSpeed );
		return ClampFinite( speed, 0.0f, maximumSpeed );
	}

	private static float SafeNonNegative( float value )
	{
		return float.IsFinite( value )
			? System.MathF.Max( 0.0f, value )
			: 0.0f;
	}

	private static float ClampFinite(
		float value,
		float minimum,
		float maximum )
	{
		return float.IsFinite( value )
			? System.Math.Clamp( value, minimum, maximum )
			: minimum;
	}
}

/// <summary>
/// Exactly-once impact gate for a particular authoritative throw token.
/// Stale callbacks from a prior throw or reset can never consume a newer throw.
/// </summary>
public sealed class LargeLadDodgeballImpactGate
{
	public int ActiveThrowSequence { get; private set; }
	public bool ImpactConsumed { get; private set; }

	public void BeginThrow( int throwSequence )
	{
		ActiveThrowSequence = System.Math.Max( 0, throwSequence );
		ImpactConsumed = false;
	}

	public bool TryConsume( int throwSequence )
	{
		if ( throwSequence <= 0 ||
			throwSequence != ActiveThrowSequence ||
			ImpactConsumed )
		{
			return false;
		}

		ImpactConsumed = true;
		return true;
	}

	public void Reset()
	{
		ActiveThrowSequence = 0;
		ImpactConsumed = false;
	}
}
