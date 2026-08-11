public enum LargeLadWeaponPresentationView
{
	Hidden,
	FirstPerson,
	ThirdPerson
}

[System.Flags]
public enum LargeLadWeaponPresentationAction
{
	None = 0,
	Interrupt = 1 << 0,
	Rebuild = 1 << 1,
	Draw = 1 << 2,
	StartReload = 1 << 3,
	FinishReload = 1 << 4
}

/// <summary>
/// Engine-independent input used to decide which local presentation, if any,
/// may represent the synchronized player and inventory state.
/// </summary>
public readonly struct LargeLadWeaponPresentationState
{
	public LargeLadRole Role { get; init; }
	public LargeLadRoundPhase RoundPhase { get; init; }
	public LargeLadWeaponId Weapon { get; init; }
	public LargeLadInventorySelection Selection { get; init; }
	public bool IsDead { get; init; }
	public bool IsLocalOwner { get; init; }
	public bool HasOwnedCamera { get; init; }
	public bool IsThirdPersonCamera { get; init; }
	public bool IsReloading { get; init; }
}

/// <summary>
/// Pure presentation policy. Combat and inventory never call these rules to
/// decide whether an action is valid; renderers only consume the resulting
/// view and transition decisions.
/// </summary>
public static class LargeLadWeaponPresentationRules
{
	public static LargeLadWeaponId ResolvePresentedWeapon(
		LargeLadRole role,
		LargeLadInventorySelection selection )
	{
		if ( role is LargeLadRole.LargeLad or LargeLadRole.Minion )
			return LargeLadWeaponId.Melee;

		if ( role != LargeLadRole.SkinnyKid )
			return LargeLadWeaponId.None;

		return selection.Kind switch
		{
			LargeLadInventorySelectionKind.RoleAbility =>
				LargeLadWeaponId.Melee,
			LargeLadInventorySelectionKind.CoreFirearm or
			LargeLadInventorySelectionKind.ExclusiveFirearm
				when LargeLadWeaponCatalog.IsFirearm( selection.Weapon ) =>
				selection.Weapon,
			_ => LargeLadWeaponId.None
		};
	}

	public static LargeLadWeaponPresentationView ResolveView(
		LargeLadWeaponPresentationState state )
	{
		if ( !CanPresentEquippedWeapon( state ) )
			return LargeLadWeaponPresentationView.Hidden;

		return state.IsLocalOwner &&
			state.HasOwnedCamera &&
			!state.IsThirdPersonCamera
			? LargeLadWeaponPresentationView.FirstPerson
			: LargeLadWeaponPresentationView.ThirdPerson;
	}

	public static bool IsDodgeballSelected(
		LargeLadWeaponPresentationState state )
	{
		return state.Role == LargeLadRole.SkinnyKid &&
			state.Selection.Kind == LargeLadInventorySelectionKind.Utility &&
			state.Selection.Utility == LargeLadUtilityId.Dodgeball &&
			state.Selection.UtilityInstanceId > 0;
	}

	public static LargeLadWeaponPresentationAction ResolveTransition(
		LargeLadWeaponPresentationState previous,
		LargeLadWeaponPresentationState current,
		bool cameraChanged = false )
	{
		var previousView = ResolveView( previous );
		var currentView = ResolveView( current );
		var action = LargeLadWeaponPresentationAction.None;
		var identityChanged = previous.Role != current.Role ||
			previous.Weapon != current.Weapon ||
			previous.Selection != current.Selection ||
			previousView != currentView;

		if ( previousView != LargeLadWeaponPresentationView.Hidden &&
			(currentView == LargeLadWeaponPresentationView.Hidden ||
				identityChanged) )
		{
			action |= LargeLadWeaponPresentationAction.Interrupt;
		}

		if ( currentView == LargeLadWeaponPresentationView.Hidden )
			return action;

		if ( identityChanged ||
			(cameraChanged &&
				currentView == LargeLadWeaponPresentationView.FirstPerson) )
		{
			action |= LargeLadWeaponPresentationAction.Rebuild;
		}

		if ( identityChanged )
			action |= LargeLadWeaponPresentationAction.Draw;

		if ( current.IsReloading &&
			(!previous.IsReloading || identityChanged) )
		{
			action |= LargeLadWeaponPresentationAction.StartReload;
		}
		else if ( previous.IsReloading &&
			!current.IsReloading &&
			!identityChanged )
		{
			action |= LargeLadWeaponPresentationAction.FinishReload;
		}

		return action;
	}

	public static bool ShouldPresentAcceptedShot(
		LargeLadWeaponPresentationState state,
		int lastPresentedSequence,
		int authoritativeSequence,
		LargeLadWeaponId authoritativeWeapon )
	{
		return authoritativeSequence > lastPresentedSequence &&
			authoritativeWeapon == state.Weapon &&
			LargeLadWeaponCatalog.IsFirearm( authoritativeWeapon ) &&
			ResolveView( state ) != LargeLadWeaponPresentationView.Hidden;
	}

	public static bool ShouldPresentEmptyFire(
		LargeLadWeaponPresentationState state,
		int lastPresentedSequence,
		int authoritativeSequence,
		LargeLadWeaponId authoritativeWeapon )
	{
		return state.IsLocalOwner &&
			authoritativeSequence > lastPresentedSequence &&
			authoritativeWeapon == state.Weapon &&
			LargeLadWeaponCatalog.IsFirearm( authoritativeWeapon ) &&
			ResolveView( state ) ==
				LargeLadWeaponPresentationView.FirstPerson;
	}

	private static bool CanPresentEquippedWeapon(
		LargeLadWeaponPresentationState state )
	{
		var isDodgeball = IsDodgeballSelected( state );

		if ( state.IsDead ||
			state.Role == LargeLadRole.Unassigned ||
			(!isDodgeball &&
				(state.Weapon == LargeLadWeaponId.None ||
					!LargeLadWeaponCatalog.TryGet( state.Weapon, out _ ))) )
		{
			return false;
		}

		if ( state.Role == LargeLadRole.SkinnyKid )
		{
			return state.RoundPhase is LargeLadRoundPhase.HeadStart or
				LargeLadRoundPhase.Playing;
		}

		return state.Role is LargeLadRole.LargeLad or LargeLadRole.Minion &&
			state.Weapon == LargeLadWeaponId.Melee &&
			state.RoundPhase == LargeLadRoundPhase.Playing;
	}
}
