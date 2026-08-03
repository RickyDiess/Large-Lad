using Sandbox;
using System.Collections.Generic;

public enum LargeLadEatTargetKind
{
	SkinnyKid,
	LargeLadBarricade,
	EatSmashable
}

public readonly struct LargeLadEatTargetCandidate
{
	public LargeLadEatTargetCandidate(
		int id,
		LargeLadEatTargetKind kind,
		Vector3 position,
		bool isEligible = true,
		bool isObstructed = false,
		bool isClaimed = false )
	{
		Id = id;
		Kind = kind;
		Position = position;
		IsEligible = isEligible;
		IsObstructed = isObstructed;
		IsClaimed = isClaimed;
	}

	public int Id { get; }
	public LargeLadEatTargetKind Kind { get; }
	public Vector3 Position { get; }
	public bool IsEligible { get; }
	public bool IsObstructed { get; }
	public bool IsClaimed { get; }
}

public enum LargeLadEatStatePhase
{
	Inactive,
	Eating
}

public enum LargeLadEatStateTransition
{
	None,
	Cancel,
	Complete
}

/// <summary>
/// The one explicit Eat transaction. Runtime participant references are kept
/// by the attack component, while all timing and one-shot gates live here so
/// completion, execution, and cleanup cannot drift into independent timers.
/// </summary>
public sealed class LargeLadEatState
{
	public LargeLadEatStatePhase Phase { get; private set; }
	public int Sequence { get; private set; }
	public float CompletionTime { get; private set; }
	public float NextPresentationTime { get; private set; }
	public int PresentationPulseCount { get; private set; }
	public bool ExecutionCommitted { get; private set; }
	public bool CleanupCommitted { get; private set; }

	public bool IsActive => Phase == LargeLadEatStatePhase.Eating;

	public bool TryBegin(
		int sequence,
		float now,
		float duration,
		float presentationInterval )
	{
		if ( IsActive || sequence <= 0 )
			return false;

		Phase = LargeLadEatStatePhase.Eating;
		Sequence = sequence;
		CompletionTime = LargeLadGameplayRules.GetTimerDeadline(
			now,
			duration );
		NextPresentationTime = LargeLadGameplayRules.GetTimerDeadline(
			now,
			presentationInterval );
		PresentationPulseCount = 0;
		ExecutionCommitted = false;
		CleanupCommitted = false;
		return true;
	}

	public LargeLadEatStateTransition GetTransition(
		float now,
		bool participantsRemainValid )
	{
		if ( !IsActive )
			return LargeLadEatStateTransition.None;

		if ( !participantsRemainValid )
			return LargeLadEatStateTransition.Cancel;

		return LargeLadGameplayRules.HasTimerReachedDeadline(
			CompletionTime,
			now )
			? LargeLadEatStateTransition.Complete
			: LargeLadEatStateTransition.None;
	}

	public bool TryTakePresentationPulse(
		float now,
		float presentationInterval,
		out int pulseIndex )
	{
		pulseIndex = -1;

		if ( !IsActive ||
			LargeLadGameplayRules.HasTimerReachedDeadline(
				CompletionTime,
				now ) ||
			!LargeLadGameplayRules.HasTimerReachedDeadline(
				NextPresentationTime,
				now ) )
		{
			return false;
		}

		pulseIndex = PresentationPulseCount++;
		NextPresentationTime = LargeLadGameplayRules.GetTimerDeadline(
			NextPresentationTime,
			presentationInterval );
		return true;
	}

	public bool TryCommitExecution()
	{
		if ( !IsActive || ExecutionCommitted || CleanupCommitted )
			return false;

		ExecutionCommitted = true;
		return true;
	}

	public bool TryCommitCleanup()
	{
		if ( !IsActive || CleanupCommitted )
			return false;

		CleanupCommitted = true;
		Phase = LargeLadEatStatePhase.Inactive;
		return true;
	}
}

/// <summary>
/// Pure Eat targeting and balance decisions shared by runtime and tests.
/// </summary>
public static class LargeLadEatRules
{
	/// <summary>
	/// Gives a committed Eat victim exclusive ownership against ordinary damage.
	/// Eat damage is accepted only through LargeLadHealth.TryExecuteEat, while an
	/// authorized environmental execution can still end the victim immediately.
	/// </summary>
	public static float FilterDamageForEatCommit(
		LargeLadEatParticipation participation,
		LargeLadDamageType damageType,
		float requestedDamage,
		bool isAuthorizedExecution )
	{
		if ( damageType is LargeLadDamageType.Eat or
			LargeLadDamageType.Environment && isAuthorizedExecution )
		{
			return requestedDamage;
		}

		if ( damageType == LargeLadDamageType.Eat )
			return 0.0f;

		return participation == LargeLadEatParticipation.Victim
			? 0.0f
			: requestedDamage;
	}

	public static bool TrySelectTarget(
		Vector3 attackerPosition,
		Vector3 attackerForward,
		float forwardOffset,
		float searchRadius,
		float minimumFacingDot,
		IReadOnlyList<LargeLadEatTargetCandidate> candidates,
		out LargeLadEatTargetCandidate selected )
	{
		selected = default;

		if ( candidates is null || searchRadius <= 0.0f )
			return false;

		var forward = attackerForward.LengthSquared > 0.0001f
			? attackerForward.Normal
			: Vector3.Forward;
		var searchCenter = attackerPosition +
			forward * System.MathF.Max( 0.0f, forwardOffset );
		var searchRadiusSquared = searchRadius * searchRadius;

		// A living, visible, unclaimed Skinny Kid always wins over structural
		// targets. This preserves the original player-first Eat behavior even
		// when a breakable is physically closer.
		if ( TrySelectNearestOfKind(
			attackerPosition,
			forward,
			searchCenter,
			searchRadiusSquared,
			minimumFacingDot,
			candidates,
			LargeLadEatTargetKind.SkinnyKid,
			out selected ) )
		{
			return true;
		}

		var found = false;
		var bestDistanceSquared = float.MaxValue;

		foreach ( var candidate in candidates )
		{
			if ( candidate.Kind == LargeLadEatTargetKind.SkinnyKid ||
				!CanSelectCandidate(
					attackerPosition,
					forward,
					searchCenter,
					searchRadiusSquared,
					minimumFacingDot,
					candidate,
					out var distanceSquared ) )
			{
				continue;
			}

			if ( !found ||
				distanceSquared < bestDistanceSquared ||
				(distanceSquared == bestDistanceSquared &&
					candidate.Id < selected.Id) )
			{
				found = true;
				bestDistanceSquared = distanceSquared;
				selected = candidate;
			}
		}

		return found;
	}

	public static float GetHealedHealth(
		float currentHealth,
		float maximumHealth,
		float missingHealthFraction )
	{
		var maximum = float.IsFinite( maximumHealth )
			? System.MathF.Max( 0.0f, maximumHealth )
			: 0.0f;
		var current = float.IsFinite( currentHealth )
			? System.Math.Clamp( currentHealth, 0.0f, maximum )
			: 0.0f;
		var fraction = float.IsFinite( missingHealthFraction )
			? System.Math.Clamp( missingHealthFraction, 0.0f, 1.0f )
			: 0.0f;
		var missing = maximum - current;
		return System.MathF.Min(
			maximum,
			current + missing * fraction );
	}

	private static bool TrySelectNearestOfKind(
		Vector3 attackerPosition,
		Vector3 forward,
		Vector3 searchCenter,
		float searchRadiusSquared,
		float minimumFacingDot,
		IReadOnlyList<LargeLadEatTargetCandidate> candidates,
		LargeLadEatTargetKind kind,
		out LargeLadEatTargetCandidate selected )
	{
		selected = default;
		var found = false;
		var bestDistanceSquared = float.MaxValue;

		foreach ( var candidate in candidates )
		{
			if ( candidate.Kind != kind ||
				!CanSelectCandidate(
					attackerPosition,
					forward,
					searchCenter,
					searchRadiusSquared,
					minimumFacingDot,
					candidate,
					out var distanceSquared ) )
			{
				continue;
			}

			if ( !found ||
				distanceSquared < bestDistanceSquared ||
				(distanceSquared == bestDistanceSquared &&
					candidate.Id < selected.Id) )
			{
				found = true;
				bestDistanceSquared = distanceSquared;
				selected = candidate;
			}
		}

		return found;
	}

	private static bool CanSelectCandidate(
		Vector3 attackerPosition,
		Vector3 forward,
		Vector3 searchCenter,
		float searchRadiusSquared,
		float minimumFacingDot,
		LargeLadEatTargetCandidate candidate,
		out float distanceSquared )
	{
		distanceSquared = float.MaxValue;

		if ( !candidate.IsEligible ||
			candidate.IsObstructed ||
			candidate.IsClaimed ||
			candidate.Position.DistanceSquared( searchCenter ) >
				searchRadiusSquared )
		{
			return false;
		}

		var toTarget = candidate.Position - attackerPosition;
		distanceSquared = toTarget.LengthSquared;

		if ( distanceSquared <= 0.0001f )
			return true;

		return Vector3.Dot( forward, toTarget.Normal ) >=
			System.Math.Clamp( minimumFacingDot, -1.0f, 1.0f );
	}
}
