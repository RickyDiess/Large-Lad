/// <summary>
/// Pure timer-only decisions for late-round Hunter movement escalation.
/// Runtime callers provide the replicated survival-round timing and continue
/// to own all engine and networking side effects.
/// </summary>
public static class LargeLadHunterMovementEscalationRules
{
	public const float DefaultRampStartNormalizedTime = 0.6f;
	public const float DefaultRampEndNormalizedTime = 1.0f;
	public const float DefaultLargeLadMaximumMultiplier = 1.10f;
	public const float DefaultMinionMaximumMultiplier = 1.15f;

	/// <summary>
	/// Converts the host-authored survival interval into a clamped 0..1 value.
	/// Non-playing and invalid intervals are deliberately neutral so timing can
	/// be cleared completely between rounds.
	/// </summary>
	public static float GetNormalizedElapsedSurvivalRoundTime(
		bool isSurvivalRoundActive,
		float survivalRoundStartTime,
		float survivalRoundEndTime,
		float now )
	{
		if ( !isSurvivalRoundActive ||
			!float.IsFinite( survivalRoundStartTime ) ||
			!float.IsFinite( survivalRoundEndTime ) ||
			!float.IsFinite( now ) ||
			survivalRoundEndTime <= survivalRoundStartTime )
		{
			return 0.0f;
		}

		var normalized =
			(now - survivalRoundStartTime) /
			(survivalRoundEndTime - survivalRoundStartTime);
		return System.Math.Clamp( normalized, 0.0f, 1.0f );
	}

	/// <summary>
	/// Smoothstep ramp: exactly neutral before the configured interval and at
	/// full escalation after it, with no velocity discontinuity at either edge.
	/// </summary>
	public static float GetRampProgress(
		float normalizedElapsedSurvivalRoundTime,
		float rampStartNormalizedTime,
		float rampEndNormalizedTime )
	{
		if ( !float.IsFinite( normalizedElapsedSurvivalRoundTime ) ||
			!IsValidRampInterval(
				rampStartNormalizedTime,
				rampEndNormalizedTime ) )
		{
			return 0.0f;
		}

		var linearProgress = System.Math.Clamp(
			(normalizedElapsedSurvivalRoundTime -
				rampStartNormalizedTime) /
			(rampEndNormalizedTime - rampStartNormalizedTime),
			0.0f,
			1.0f );
		return linearProgress * linearProgress *
			(3.0f - 2.0f * linearProgress);
	}

	public static float GetMovementMultiplier(
		LargeLadRole role,
		float normalizedElapsedSurvivalRoundTime,
		float rampStartNormalizedTime,
		float rampEndNormalizedTime,
		float largeLadMaximumMultiplier,
		float minionMaximumMultiplier )
	{
		var maximumMultiplier = role switch
		{
			LargeLadRole.LargeLad => largeLadMaximumMultiplier,
			LargeLadRole.Minion => minionMaximumMultiplier,
			_ => 1.0f
		};

		if ( !LargeLadGameplayRules.IsHunterRole( role ) )
			return 1.0f;

		maximumMultiplier = NormalizeMaximumMultiplier(
			maximumMultiplier );
		var progress = GetRampProgress(
			normalizedElapsedSurvivalRoundTime,
			rampStartNormalizedTime,
			rampEndNormalizedTime );
		return 1.0f + (maximumMultiplier - 1.0f) * progress;
	}

	public static bool IsValidRampInterval(
		float rampStartNormalizedTime,
		float rampEndNormalizedTime )
	{
		return float.IsFinite( rampStartNormalizedTime ) &&
			float.IsFinite( rampEndNormalizedTime ) &&
			rampStartNormalizedTime >= 0.0f &&
			rampEndNormalizedTime <= 1.0f &&
			rampStartNormalizedTime < rampEndNormalizedTime;
	}

	public static bool IsValidMaximumMultiplier( float multiplier )
	{
		return float.IsFinite( multiplier ) && multiplier >= 1.0f;
	}

	private static float NormalizeMaximumMultiplier( float multiplier )
	{
		return IsValidMaximumMultiplier( multiplier )
			? multiplier
			: 1.0f;
	}
}
