/// <summary>
/// Deterministic gameplay decisions shared by the runtime and unit tests.
/// Keep engine and networking side effects in their owning components.
/// </summary>
public static class LargeLadGameplayRules
{
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
		LargeLadRole currentRole,
		LargeLadRole requestedRespawnRole )
	{
		return currentRole == LargeLadRole.SkinnyKid
			? LargeLadRole.Minion
			: requestedRespawnRole;
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

	public static int FindWeaponGrantSlot(
		LargeLadWeaponId weapon,
		LargeLadWeaponId slot1,
		LargeLadWeaponId slot2,
		LargeLadWeaponId slot3,
		LargeLadWeaponId slot4 )
	{
		if ( slot1 == weapon || slot2 == weapon ||
			slot3 == weapon || slot4 == weapon )
		{
			return 0;
		}

		if ( slot1 == LargeLadWeaponId.None )
			return 1;

		if ( slot2 == LargeLadWeaponId.None )
			return 2;

		if ( slot3 == LargeLadWeaponId.None )
			return 3;

		return slot4 == LargeLadWeaponId.None ? 4 : 0;
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
