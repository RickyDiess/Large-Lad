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

public static class LargeLadGameplayRules
{
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
