using Sandbox;

public enum LargeLadGroundSlamPropBehavior
{
	Move,
	Unanchor,
	Break
}

public enum LargeLadGroundSlamHostRequestResult
{
	Replay,
	Rejected,
	Accepted
}

public enum LargeLadGroundSlamWindupResult
{
	Pending,
	Cancelled,
	Impact
}

public readonly struct LargeLadGroundSlamHostDecision
{
	public LargeLadGroundSlamHostDecision(
		LargeLadGroundSlamHostRequestResult result,
		float cooldownRemaining,
		float cooldownEndTime )
	{
		Result = result;
		CooldownRemaining = System.MathF.Max(
			0.0f,
			cooldownRemaining );
		CooldownEndTime = cooldownEndTime;
	}

	public LargeLadGroundSlamHostRequestResult Result { get; }
	public float CooldownRemaining { get; }
	public float CooldownEndTime { get; }
	public bool IsNewRequest =>
		Result != LargeLadGroundSlamHostRequestResult.Replay;
	public bool Accepted =>
		Result == LargeLadGroundSlamHostRequestResult.Accepted;
}

/// <summary>
/// Deterministic host request sequencing, cadence, and windup state. The
/// runtime component supplies authoritative eligibility and timing inputs;
/// this object makes acceptance and replay behavior independently testable.
/// </summary>
public sealed class LargeLadGroundSlamHostState
{
	public int LastRequestSequence { get; private set; }
	public int ActiveSequence { get; private set; }
	public bool IsWindingUp { get; private set; }
	public bool HasCadence { get; private set; }
	public float NextActivationTime { get; private set; }

	public LargeLadGroundSlamHostDecision EvaluateRequest(
		int sequence,
		bool canActivate,
		float now,
		float cooldown,
		float cadenceTolerance )
	{
		if ( sequence <= LastRequestSequence )
		{
			return new LargeLadGroundSlamHostDecision(
				LargeLadGroundSlamHostRequestResult.Replay,
				GetCooldownRemaining( now ),
				HasCadence ? NextActivationTime : 0.0f );
		}

		LastRequestSequence = sequence;
		var cooldownRemaining = GetCooldownRemaining( now );
		var cadenceActive = HasCadence &&
			now + System.MathF.Max( 0.0f, cadenceTolerance ) <
				NextActivationTime;

		if ( !canActivate || cadenceActive )
		{
			return new LargeLadGroundSlamHostDecision(
				LargeLadGroundSlamHostRequestResult.Rejected,
				cooldownRemaining,
				HasCadence ? NextActivationTime : 0.0f );
		}

		var safeCooldown = System.MathF.Max( 0.01f, cooldown );
		NextActivationTime = HasCadence
			? System.MathF.Max( now, NextActivationTime ) + safeCooldown
			: now + safeCooldown;
		HasCadence = true;
		ActiveSequence = sequence;
		IsWindingUp = true;

		return new LargeLadGroundSlamHostDecision(
			LargeLadGroundSlamHostRequestResult.Accepted,
			GetCooldownRemaining( now ),
			NextActivationTime );
	}

	public LargeLadGroundSlamWindupResult ResolveWindup(
		bool canComplete,
		bool reachedImpactTime )
	{
		if ( !IsWindingUp )
			return LargeLadGroundSlamWindupResult.Pending;

		if ( !canComplete )
		{
			IsWindingUp = false;
			return LargeLadGroundSlamWindupResult.Cancelled;
		}

		if ( !reachedImpactTime )
			return LargeLadGroundSlamWindupResult.Pending;

		IsWindingUp = false;
		return LargeLadGroundSlamWindupResult.Impact;
	}

	public bool CancelWindup()
	{
		if ( !IsWindingUp )
			return false;

		IsWindingUp = false;
		return true;
	}

	public float GetCooldownRemaining( float now )
	{
		return HasCadence
			? System.MathF.Max( 0.0f, NextActivationTime - now )
			: 0.0f;
	}
}

/// <summary>
/// Owner-side mirror of host acceptance timing. A rejection without an
/// authoritative cadence never starts a local cooldown. Cancelling
/// presentation deliberately leaves accepted cadence intact while suppressing
/// its later ready notification.
/// </summary>
public sealed class LargeLadGroundSlamOwnerState
{
	public int LastResultSequence { get; private set; }
	public int AcceptedSequence { get; private set; }
	public bool HasAuthoritativeCooldown { get; private set; }
	public bool HasCooldownPresentation { get; private set; }
	public bool CooldownReadyPresentationPending { get; private set; }
	public float CooldownEndTime { get; private set; }

	public bool ApplyHostResult(
		int sequence,
		bool accepted,
		float authoritativeCooldownEndTime,
		float now )
	{
		if ( sequence <= LastResultSequence )
			return false;

		LastResultSequence = sequence;
		HasAuthoritativeCooldown =
			authoritativeCooldownEndTime > now;
		CooldownEndTime = HasAuthoritativeCooldown
			? authoritativeCooldownEndTime
			: 0.0f;

		if ( accepted )
		{
			AcceptedSequence = sequence;
			HasCooldownPresentation = true;
			CooldownReadyPresentationPending = true;
		}

		return true;
	}

	public float GetCooldownRemaining( float now )
	{
		if ( !HasAuthoritativeCooldown )
			return 0.0f;

		var remaining = System.MathF.Max( 0.0f, CooldownEndTime - now );

		if ( remaining <= 0.0f )
			HasAuthoritativeCooldown = false;

		return remaining;
	}

	public bool TryTakeCooldownReadyPresentation( float now )
	{
		if ( !CooldownReadyPresentationPending ||
			GetCooldownRemaining( now ) > 0.0f )
		{
			return false;
		}

		CooldownReadyPresentationPending = false;
		HasCooldownPresentation = false;
		return true;
	}

	public void CancelPresentation()
	{
		HasCooldownPresentation = false;
		CooldownReadyPresentationPending = false;
	}

	public void Reset()
	{
		LastResultSequence = 0;
		AcceptedSequence = 0;
		HasAuthoritativeCooldown = false;
		HasCooldownPresentation = false;
		CooldownReadyPresentationPending = false;
		CooldownEndTime = 0.0f;
	}
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

	public static float GetFeedbackScale( float distance, float radius )
	{
		if ( !float.IsFinite( distance ) ||
			!float.IsFinite( radius ) ||
			distance < 0.0f ||
			radius <= 0.0f ||
			distance >= radius )
		{
			return 0.0f;
		}

		var normalizedDistance = System.Math.Clamp(
			distance / radius,
			0.0f,
			1.0f );

		// Ease the falloff so close impacts stay weighty while the effect still
		// reaches exactly zero at the configured feedback boundary.
		var linear = 1.0f - normalizedDistance;
		return linear * linear * (3.0f - 2.0f * linear);
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
