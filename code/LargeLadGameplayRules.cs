/// <summary>
/// Deterministic gameplay decisions shared by the runtime and unit tests.
/// Keep engine and networking side effects in their owning components.
/// </summary>
public readonly struct LargeLadDeathPlan
{
	public LargeLadDeathPlan(
		LargeLadRole resultingRole,
		float respawnDelay,
		bool useRagdoll,
		LargeLadSpawnGroup spawnGroup )
	{
		ResultingRole = resultingRole;
		RespawnDelay = respawnDelay;
		UseRagdoll = useRagdoll;
		SpawnGroup = spawnGroup;
	}

	public LargeLadRole ResultingRole { get; }
	public float RespawnDelay { get; }
	public bool UseRagdoll { get; }
	public LargeLadSpawnGroup SpawnGroup { get; }
}

public readonly struct LargeLadSoftSeparationResult
{
	public LargeLadSoftSeparationResult(
		Vector3 velocity,
		Vector3 appliedCorrection )
	{
		Velocity = velocity;
		AppliedCorrection = appliedCorrection;
	}

	public Vector3 Velocity { get; }
	public Vector3 AppliedCorrection { get; }
}

public static class LargeLadGameplayRules
{
	public const string PlayerBodyTag = "player";
	public const string HunterBodyTag = "large_lad_hunter_body";
	public const string SoftPlayerBodyTag = "large_lad_soft_player_body";
	public const float SoftPlayerSeparationRadius = 28.0f;
	public const float SoftPlayerSeparationHeight = 72.0f;
	public const float SoftPlayerBaseMaximumSeparationSpeed = 42.0f;
	public const float SoftPlayerCenterStrengthMultiplier = 1.5f;
	public const float SoftPlayerMaximumSeparationSpeed =
		SoftPlayerBaseMaximumSeparationSpeed *
		SoftPlayerCenterStrengthMultiplier;
	public const float SoftPlayerResponseRate = 8.0f;

	public static bool IsHunterRole( LargeLadRole role )
	{
		return role is LargeLadRole.LargeLad or LargeLadRole.Minion;
	}

	public static string GetPlayerBodyCollisionTag( LargeLadRole role )
	{
		return IsHunterRole( role )
			? HunterBodyTag
			: SoftPlayerBodyTag;
	}

	/// <summary>
	/// Mirrors the project collision matrix for deterministic rule tests.
	/// Hunters and soft players remain fully solid against the opposing group.
	/// Same-group contact is filtered; non-hunters receive the narrow manual
	/// soft response calculated below.
	/// </summary>
	public static bool HasSolidPlayerCollision(
		LargeLadRole left,
		LargeLadRole right )
	{
		return IsHunterRole( left ) != IsHunterRole( right );
	}

	public static bool UsesSoftPlayerCollision(
		LargeLadRole left,
		LargeLadRole right )
	{
		return !IsHunterRole( left ) && !IsHunterRole( right );
	}

	/// <summary>
	/// Returns a capped horizontal separation target for one soft player.
	/// This has no vertical component; callers combine every nearby player's
	/// target before applying the shared cap below.
	/// </summary>
	public static Vector3 GetSoftPlayerSeparationVelocity(
		Vector3 playerPosition,
		Vector3 otherPosition,
		bool usePositiveXWhenCoincident )
	{
		var offset = playerPosition - otherPosition;

		if ( System.MathF.Abs( offset.z ) >=
			SoftPlayerSeparationHeight )
		{
			return Vector3.Zero;
		}

		offset.z = 0.0f;

		var distanceSquared = offset.LengthSquared;
		var radiusSquared =
			SoftPlayerSeparationRadius * SoftPlayerSeparationRadius;

		if ( distanceSquared >= radiusSquared )
			return Vector3.Zero;

		Vector3 direction;
		float distance;

		if ( distanceSquared > 0.0001f )
		{
			distance = System.MathF.Sqrt( distanceSquared );
			direction = offset / distance;
		}
		else
		{
			distance = 0.0f;
			direction = new Vector3(
				usePositiveXWhenCoincident ? 1.0f : -1.0f,
				0.0f,
				0.0f );
		}

		var strength = 1.0f -
			distance / SoftPlayerSeparationRadius;
		var centerMultiplier = 1.0f +
			(SoftPlayerCenterStrengthMultiplier - 1.0f) * strength;
		return direction *
			(SoftPlayerBaseMaximumSeparationSpeed *
				strength *
				centerMultiplier);
	}

	public static Vector3 ClampSoftPlayerSeparationVelocity(
		Vector3 combinedSeparationVelocity )
	{
		combinedSeparationVelocity.z = 0.0f;

		var maximumSpeedSquared =
			SoftPlayerMaximumSeparationSpeed *
			SoftPlayerMaximumSeparationSpeed;

		if ( combinedSeparationVelocity.LengthSquared <=
			maximumSpeedSquared )
		{
			return combinedSeparationVelocity;
		}

		return combinedSeparationVelocity.Normal *
			SoftPlayerMaximumSeparationSpeed;
	}

	/// <summary>
	/// Replaces the correction applied on the preceding physics tick with one
	/// bounded horizontal correction. Subtracting only the tracked correction
	/// leaves movement and gameplay impulses intact. A zero target removes the
	/// correction immediately once overlap ends.
	/// </summary>
	public static LargeLadSoftSeparationResult ResolveSoftPlayerSeparation(
		Vector3 currentVelocity,
		Vector3 previousAppliedCorrection,
		Vector3 combinedTargetVelocity,
		float deltaTime )
	{
		var previousCorrection =
			ClampSoftPlayerSeparationVelocity(
				previousAppliedCorrection );
		var targetCorrection =
			ClampSoftPlayerSeparationVelocity(
				combinedTargetVelocity );

		Vector3 nextCorrection;

		if ( targetCorrection.LengthSquared <= 0.0001f )
		{
			nextCorrection = Vector3.Zero;
		}
		else
		{
			var response = System.MathF.Min(
				1.0f,
				SoftPlayerResponseRate *
					System.MathF.Max( 0.0f, deltaTime ) );
			nextCorrection = previousCorrection +
				(targetCorrection - previousCorrection) * response;
			nextCorrection =
				ClampSoftPlayerSeparationVelocity(
					nextCorrection );
		}

		var correctedVelocity =
			currentVelocity -
			previousCorrection +
			nextCorrection;

		// Both corrections are horizontal, but retain this assignment as an
		// explicit invariant for jumps, falls, knockback, and future slam forces.
		correctedVelocity.z = currentVelocity.z;

		return new LargeLadSoftSeparationResult(
			correctedVelocity,
			nextCorrection );
	}

	public static Vector3 RemoveSoftPlayerSeparation(
		Vector3 currentVelocity,
		Vector3 previousAppliedCorrection )
	{
		var correctedVelocity =
			currentVelocity -
			ClampSoftPlayerSeparationVelocity(
				previousAppliedCorrection );
		correctedVelocity.z = currentVelocity.z;
		return correctedVelocity;
	}

	public static bool IsSupportedRoundPlayerCount( int playerCount )
	{
		return playerCount >=
				LargeLadGameManager.MinimumSupportedPlayerCount &&
			playerCount <= LargeLadGameManager.TargetPlayerCount;
	}

	public static float GetTimerDeadline(
		float now,
		float duration )
	{
		return now + System.MathF.Max( 0.0f, duration );
	}

	public static float GetTimerTimeRemaining(
		float deadline,
		float now )
	{
		return System.MathF.Max( 0.0f, deadline - now );
	}

	public static bool HasTimerReachedDeadline(
		float deadline,
		float now )
	{
		return now >= deadline;
	}

	public static bool CanTransitionRoundPhase(
		LargeLadRoundPhase current,
		LargeLadRoundPhase next )
	{
		return (current, next) switch
		{
			(LargeLadRoundPhase.WaitingForPlayers,
				LargeLadRoundPhase.HeadStart) => true,
			(LargeLadRoundPhase.HeadStart,
				LargeLadRoundPhase.Playing) => true,
			(LargeLadRoundPhase.HeadStart,
				LargeLadRoundPhase.RoundOver) => true,
			(LargeLadRoundPhase.Playing,
				LargeLadRoundPhase.RoundOver) => true,
			(LargeLadRoundPhase.RoundOver,
				LargeLadRoundPhase.WaitingForPlayers) => true,
			(LargeLadRoundPhase.RoundOver,
				LargeLadRoundPhase.HeadStart) => true,
			_ => false
		};
	}

	public static bool HasMinimumPlayers(
		int playerCount,
		int minimumPlayers )
	{
		return playerCount >= minimumPlayers;
	}

	public static LargeLadWinner DetermineWinnerWhenTeamIsMissing(
		bool hasLargeLad,
		bool hasSkinnyKid )
	{
		if ( !hasLargeLad )
			return LargeLadWinner.SkinnyKids;

		if ( !hasSkinnyKid )
			return LargeLadWinner.LargeLadTeam;

		return LargeLadWinner.None;
	}

	public static LargeLadRole GetEffectiveRoundRole(
		LargeLadRole currentRole,
		LargeLadRole pendingRespawnRole )
	{
		return pendingRespawnRole != LargeLadRole.Unassigned
			? pendingRespawnRole
			: currentRole;
	}

	public static LargeLadRole ResolveRespawnRole(
		LargeLadRole currentRole )
	{
		return currentRole == LargeLadRole.SkinnyKid
			? LargeLadRole.Minion
			: currentRole;
	}

	public static LargeLadSpawnGroup GetSpawnGroupForRole(
		LargeLadRole role )
	{
		return role switch
		{
			LargeLadRole.LargeLad or LargeLadRole.Minion =>
				LargeLadSpawnGroup.Hunter,
			LargeLadRole.SkinnyKid => LargeLadSpawnGroup.SkinnyKid,
			_ => LargeLadSpawnGroup.Lobby
		};
	}

	public static LargeLadDeathPlan ResolveDeathPlan(
		LargeLadRole currentRole,
		LargeLadDamageType damageType,
		float largeLadRespawnDelay,
		float playerRespawnDelay )
	{
		var resultingRole = ResolveRespawnRole( currentRole );
		var respawnDelay = currentRole == LargeLadRole.LargeLad
			? largeLadRespawnDelay
			: playerRespawnDelay;
		var spawnGroup = GetSpawnGroupForRole( resultingRole );

		return new LargeLadDeathPlan(
			resultingRole,
			System.MathF.Max( 0.0f, respawnDelay ),
			damageType != LargeLadDamageType.Environment,
			spawnGroup );
	}

	public static bool IsNewLethalTransition(
		float previousHealth,
		float currentHealth,
		bool alreadyReported )
	{
		return !alreadyReported &&
			previousHealth > 0.0f &&
			currentHealth <= 0.0f;
	}

	public static bool CanCommitLethalTransition(
		float previousHealth,
		float currentHealth,
		bool alreadyReported,
		bool managerAccepted )
	{
		return managerAccepted &&
			IsNewLethalTransition(
				previousHealth,
				currentHealth,
				alreadyReported );
	}

	public static bool CanDamageBarricade(
		LargeLadBarricadeMode mode,
		LargeLadRole attackerRole,
		LargeLadDamageType damageType )
	{
		if ( mode == LargeLadBarricadeMode.SkinnyProgression )
		{
			return attackerRole == LargeLadRole.SkinnyKid &&
				damageType is LargeLadDamageType.Firearm or
					LargeLadDamageType.Melee;
		}

		return attackerRole == LargeLadRole.LargeLad &&
			damageType == LargeLadDamageType.Melee;
	}

	public static LargeLadRoleProfile SelectRoleProfile(
		LargeLadRole role,
		LargeLadRoleProfile skinnyKid,
		LargeLadRoleProfile largeLad,
		LargeLadRoleProfile minion )
	{
		return role switch
		{
			LargeLadRole.LargeLad => largeLad,
			LargeLadRole.Minion => minion,
			_ => skinnyKid
		};
	}
}
