/// <summary>
/// Deterministic Skinny Kid survivability rules shared by the authoritative
/// runtime and unit tests. Presentation and networking remain in their owning
/// components.
/// </summary>
public static class LargeLadSkinnyKidSurvivabilityRules
{
	public const float DefaultRegenerationDelay = 5.0f;
	public const float DefaultRegenerationRate = 5.0f;
	public const float RegenerationCapFraction = 0.75f;
	public const float LastSkinnyKidIncomingDamageMultiplier = 0.5f;

	public static bool IsValidRegenerationDelay( float delay )
	{
		return float.IsFinite( delay ) && delay >= 0.0f;
	}

	public static bool IsValidRegenerationRate( float rate )
	{
		return float.IsFinite( rate ) && rate >= 0.0f;
	}

	public static float GetRegenerationCap( float maximumHealth )
	{
		return System.MathF.Max( 0.0f, maximumHealth ) *
			RegenerationCapFraction;
	}

	/// <summary>
	/// Applies only the portion of the current frame that lies beyond the
	/// post-damage delay, then clamps to 75% of the current maximum health.
	/// </summary>
	public static float GetRegeneratedHealth(
		LargeLadRole role,
		bool isLiving,
		bool isRoundActive,
		float currentHealth,
		float maximumHealth,
		float secondsSinceLastDamage,
		float deltaTime,
		float regenerationDelay,
		float regenerationRate )
	{
		var safeCurrentHealth = System.MathF.Max( 0.0f, currentHealth );
		var cap = GetRegenerationCap( maximumHealth );

		if ( role != LargeLadRole.SkinnyKid ||
			!isLiving ||
			!isRoundActive ||
			safeCurrentHealth <= 0.0f ||
			safeCurrentHealth >= cap ||
			!IsValidRegenerationDelay( regenerationDelay ) ||
			!IsValidRegenerationRate( regenerationRate ) ||
			regenerationRate <= 0.0f )
		{
			return safeCurrentHealth;
		}

		var safeDeltaTime = System.MathF.Max( 0.0f, deltaTime );
		var elapsedRegenerationTime = System.MathF.Max(
			0.0f,
			secondsSinceLastDamage - regenerationDelay );
		var activeFrameTime = System.MathF.Min(
			safeDeltaTime,
			elapsedRegenerationTime );

		return System.MathF.Min(
			cap,
			safeCurrentHealth + regenerationRate * activeFrameTime );
	}

	public static bool IsEffectiveLivingSkinnyKid(
		LargeLadRole currentRole,
		LargeLadRole pendingRespawnRole,
		bool isDead,
		float currentHealth )
	{
		return !isDead &&
			currentHealth > 0.0f &&
			LargeLadGameplayRules.GetEffectiveRoundRole(
				currentRole,
				pendingRespawnRole ) == LargeLadRole.SkinnyKid;
	}

	public static bool ShouldAnnounceLastSkinnyKid(
		bool isRoundActive,
		int previousEffectiveLivingSkinnyKidCount,
		int currentEffectiveLivingSkinnyKidCount,
		bool hasAlreadyAnnouncedThisRound )
	{
		return isRoundActive &&
			!hasAlreadyAnnouncedThisRound &&
			previousEffectiveLivingSkinnyKidCount != 1 &&
			currentEffectiveLivingSkinnyKidCount == 1;
	}

	/// <summary>
	/// Last Skinny Kid protection composes after ordinary role-profile damage.
	/// Explicit Eat and environmental executions are deliberately returned
	/// unchanged.
	/// </summary>
	public static float ApplyLastSkinnyKidDamageReduction(
		LargeLadRole role,
		bool isLastSkinnyKid,
		LargeLadDamageType damageType,
		bool isExplicitExecution,
		float ordinaryIncomingDamage )
	{
		var safeDamage = System.MathF.Max( 0.0f, ordinaryIncomingDamage );
		var bypassesProtection = isExplicitExecution &&
			damageType is LargeLadDamageType.Eat or
				LargeLadDamageType.Environment;

		if ( role != LargeLadRole.SkinnyKid ||
			!isLastSkinnyKid ||
			bypassesProtection )
		{
			return safeDamage;
		}

		return safeDamage * LastSkinnyKidIncomingDamageMultiplier;
	}
}
