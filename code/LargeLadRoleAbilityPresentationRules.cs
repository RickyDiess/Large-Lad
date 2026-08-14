public enum LargeLadRoleAbilityPresentationKind
{
	None,
	RoleMelee,
	Dodgeball
}

public enum LargeLadRoleAbilityPresentationView
{
	Hidden,
	FirstPerson,
	ThirdPerson
}

/// <summary>
/// Engine-independent state for presentation that does not belong to a native
/// BaseCombatWeapon. Native Skinny Kid weapons deliberately resolve to None.
/// </summary>
public readonly struct LargeLadRoleAbilityPresentationState
{
	public LargeLadRole Role { get; init; }
	public LargeLadRoundPhase RoundPhase { get; init; }
	public LargeLadInventorySelection Selection { get; init; }
	public bool IsDead { get; init; }
	public bool IsLocalOwner { get; init; }
	public bool HasOwnedCamera { get; init; }
	public bool IsThirdPersonCamera { get; init; }
}

public static class LargeLadRoleAbilityPresentationRules
{
	public static LargeLadRoleAbilityPresentationKind ResolveKind(
		LargeLadRoleAbilityPresentationState state )
	{
		if ( state.Role is LargeLadRole.LargeLad or LargeLadRole.Minion )
			return LargeLadRoleAbilityPresentationKind.RoleMelee;

		if ( state.Role == LargeLadRole.SkinnyKid &&
			state.Selection.Kind == LargeLadInventorySelectionKind.Utility &&
			state.Selection.Utility == LargeLadUtilityId.Dodgeball &&
			state.Selection.UtilityInstanceId > 0 )
		{
			return LargeLadRoleAbilityPresentationKind.Dodgeball;
		}

		return LargeLadRoleAbilityPresentationKind.None;
	}

	public static LargeLadRoleAbilityPresentationView ResolveView(
		LargeLadRoleAbilityPresentationState state )
	{
		if ( !CanPresent( state ) )
			return LargeLadRoleAbilityPresentationView.Hidden;

		return state.IsLocalOwner &&
			state.HasOwnedCamera &&
			!state.IsThirdPersonCamera
			? LargeLadRoleAbilityPresentationView.FirstPerson
			: LargeLadRoleAbilityPresentationView.ThirdPerson;
	}

	private static bool CanPresent(
		LargeLadRoleAbilityPresentationState state )
	{
		if ( state.IsDead )
			return false;

		return ResolveKind( state ) switch
		{
			LargeLadRoleAbilityPresentationKind.RoleMelee =>
				state.RoundPhase == LargeLadRoundPhase.Playing,
			LargeLadRoleAbilityPresentationKind.Dodgeball =>
				state.RoundPhase is LargeLadRoundPhase.HeadStart or
					LargeLadRoundPhase.Playing,
			_ => false
		};
	}
}
