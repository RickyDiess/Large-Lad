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
		Vector3 displacement )
	{
		Velocity = velocity;
		Displacement = displacement;
	}

	public Vector3 Velocity { get; }
	public Vector3 Displacement { get; }
}

public static class LargeLadGameplayRules
{
	public const string PlayerBodyTag = "player";
	public const string HunterBodyTag = "large_lad_hunter_body";
	public const string SoftPlayerBodyTag = "large_lad_soft_player_body";
	public const string MinionBodyTag = "large_lad_minion_body";
	public const string MinionPassageTag = "large_lad_minion_passage";
	public const float PlayerBodyRadius = 16.0f;
	public const float PlayerBodyHeight = 72.0f;
	public const float SoftPlayerSeparationRadius = 28.0f;
	public const float SoftPlayerSeparationHeight = 72.0f;
	public const float SoftPlayerBaseMaximumSeparationSpeed = 42.0f;
	public const float SoftPlayerCenterStrengthMultiplier = 1.5f;
	public const float SoftPlayerMaximumSeparationSpeed =
		SoftPlayerBaseMaximumSeparationSpeed *
		SoftPlayerCenterStrengthMultiplier;

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
	/// Minions retain the shared Hunter tag for player contact and receive this
	/// additional tag solely for focused Minion-passage filtering.
	/// </summary>
	public static string GetSupplementaryRoleCollisionTag(
		LargeLadRole role )
	{
		return role == LargeLadRole.Minion
			? MinionBodyTag
			: null;
	}

	public static bool CanTraverseMinionPassage( LargeLadRole role )
	{
		return role == LargeLadRole.Minion;
	}

	public static bool CanTraverseMinionPassage(
		LargeLadRole role,
		bool coverEnabled,
		bool coverDestroyed )
	{
		return IsMinionPassageOpen( coverEnabled, coverDestroyed ) &&
			CanTraverseMinionPassage( role );
	}

	/// <summary>
	/// Mirrors the passage collision-matrix exception. Ordinary physics bodies
	/// have no Minion body tag and therefore remain solid against the gate.
	/// </summary>
	public static bool HasMinionPassageCollisionException( string bodyTag )
	{
		return bodyTag == MinionBodyTag;
	}

	public static bool CanDamageMinionPassageCover(
		LargeLadRole attackerRole,
		LargeLadDamageType damageType )
	{
		return attackerRole == LargeLadRole.Minion &&
			damageType == LargeLadDamageType.Melee;
	}

	public static bool IsMinionPassageOpen(
		bool coverEnabled,
		bool coverDestroyed )
	{
		return !coverEnabled || coverDestroyed;
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
	/// Calculates one traced horizontal displacement for the current physics
	/// step. Rigidbody velocity is returned unchanged, preserving controller
	/// movement and gameplay impulses without carrying artificial velocity.
	/// </summary>
	public static LargeLadSoftSeparationResult ResolveSoftPlayerSeparation(
		Vector3 currentVelocity,
		Vector3 combinedTargetVelocity,
		float deltaTime )
	{
		var targetCorrection =
			ClampSoftPlayerSeparationVelocity(
				combinedTargetVelocity );
		var displacement = targetCorrection *
			System.MathF.Max( 0.0f, deltaTime );
		displacement.z = 0.0f;

		return new LargeLadSoftSeparationResult(
			currentVelocity,
			displacement );
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
		return (mode, attackerRole, damageType) switch
		{
			(
				LargeLadBarricadeMode.SkinnyProgression,
				LargeLadRole.SkinnyKid,
				LargeLadDamageType.Melee
			) => true,
			(
				LargeLadBarricadeMode.LadShortcut,
				LargeLadRole.LargeLad,
				LargeLadDamageType.Melee
			) => true,
			_ => false
		};
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
