using System.Collections.Generic;

/// <summary>
/// Side-effect-free inventory decisions shared by the host component and unit
/// tests. Runtime networking and world-object creation remain in components.
/// </summary>
public static class LargeLadInventoryRules
{
	public static bool CanUseInventory(
		LargeLadRole role,
		bool isDead )
	{
		return role == LargeLadRole.SkinnyKid && !isDead;
	}

	public static bool CanUseFirearmInventory(
		LargeLadRole role,
		bool isDead )
	{
		return CanUseInventory( role, isDead );
	}

	public static bool CanProcessOwnerRequest(
		bool isHost,
		bool ownerRequest,
		LargeLadRole role,
		bool isDead )
	{
		return isHost &&
			ownerRequest &&
			CanUseInventory( role, isDead );
	}

	public static bool CanCollectCore(
		bool isHost,
		LargeLadRole role,
		bool isDead,
		IList<LargeLadWeaponState> coreWeapons,
		LargeLadWeaponId weapon )
	{
		return isHost &&
			CanUseFirearmInventory( role, isDead ) &&
			LargeLadWeaponCatalog.IsFirearm( weapon ) &&
			FindCoreWeapon( coreWeapons, weapon ) < 0;
	}

	public static bool IsValidCoreState( LargeLadWeaponState state )
	{
		return state.IsOwned &&
			state.AmmunitionMode ==
				LargeLadAmmunitionMode.InfiniteReserve &&
			state.ExclusiveInstanceId == 0 &&
			state.Magazine >= 0 &&
			state.Reserve == 0;
	}

	public static bool IsValidExclusiveState( LargeLadWeaponState state )
	{
		return state.IsOwned &&
			state.AmmunitionMode ==
				LargeLadAmmunitionMode.FiniteReserve &&
			state.ExclusiveInstanceId > 0 &&
			state.Magazine >= 0 &&
			state.Reserve >= 0;
	}

	public static int FindCoreWeapon(
		IList<LargeLadWeaponState> coreWeapons,
		LargeLadWeaponId weapon )
	{
		if ( coreWeapons is null )
			return -1;

		for ( var index = 0; index < coreWeapons.Count; index++ )
		{
			if ( coreWeapons[index].Weapon == weapon )
				return index;
		}

		return -1;
	}

	public static bool TryAddCoreWeapon(
		IList<LargeLadWeaponState> coreWeapons,
		LargeLadWeaponId weapon )
	{
		if ( coreWeapons is null ||
			!LargeLadWeaponCatalog.IsFirearm( weapon ) ||
			FindCoreWeapon( coreWeapons, weapon ) >= 0 )
		{
			return false;
		}

		var state = LargeLadWeaponState.CreateCore( weapon );
		var order = LargeLadWeaponCatalog.GetCatalogOrder( weapon );
		var insertAt = coreWeapons.Count;

		for ( var index = 0; index < coreWeapons.Count; index++ )
		{
			if ( LargeLadWeaponCatalog.GetCatalogOrder(
				coreWeapons[index].Weapon ) > order )
			{
				insertAt = index;
				break;
			}
		}

		coreWeapons.Insert( insertAt, state );
		return true;
	}

	public static bool CanAcceptExclusive(
		LargeLadRole role,
		bool isDead,
		bool alreadyHasExclusive,
		bool pickupAvailable,
		LargeLadWeaponState state )
	{
		return CanUseFirearmInventory( role, isDead ) &&
			!alreadyHasExclusive &&
			pickupAvailable &&
			IsValidExclusiveState( state );
	}

	public static bool CanSelectFirearm(
		bool isHost,
		bool ownerRequest,
		LargeLadRole role,
		bool isDead,
		LargeLadWeaponState state )
	{
		return CanProcessOwnerRequest(
				isHost,
				ownerRequest,
				role,
				isDead ) &&
			(IsValidCoreState( state ) ||
				IsValidExclusiveState( state ));
	}

	public static bool CanDropExclusive(
		bool isHost,
		bool ownerRequest,
		LargeLadRole role,
		bool isDead,
		LargeLadWeaponState state,
		LargeLadInventorySelection activeSelection )
	{
		return CanProcessOwnerRequest(
				isHost,
				ownerRequest,
				role,
				isDead ) &&
			IsValidExclusiveState( state ) &&
			activeSelection == FirearmSelectionFor( state );
	}

	public static bool CanReload(
		bool isHost,
		bool ownerRequest,
		LargeLadRole role,
		bool isDead,
		bool isAlreadyReloading,
		LargeLadWeaponState state )
	{
		if ( !CanSelectFirearm(
			isHost,
			ownerRequest,
			role,
			isDead,
			state ) ||
			isAlreadyReloading )
		{
			return false;
		}

		var magazineSize =
			LargeLadWeaponCatalog.Get( state.Weapon ).MagazineSize;

		if ( state.Magazine >= magazineSize )
			return false;

		return state.HasInfiniteReserve || state.Reserve > 0;
	}

	public static LargeLadWeaponState CompleteReload(
		LargeLadWeaponState state )
	{
		var magazineSize =
			LargeLadWeaponCatalog.Get( state.Weapon ).MagazineSize;
		var needed = System.Math.Max( 0, magazineSize - state.Magazine );

		if ( state.HasInfiniteReserve )
		{
			state.Magazine += needed;
			state.Reserve = 0;
			return state;
		}

		var loaded = System.Math.Min( needed, state.Reserve );
		state.Magazine += loaded;
		state.Reserve -= loaded;
		return state;
	}

	public static bool TryConsumeShot( ref LargeLadWeaponState state )
	{
		if ((!IsValidCoreState( state ) &&
			!IsValidExclusiveState( state )) ||
			state.Magazine <= 0 )
		{
			return false;
		}

		state.Magazine--;
		return true;
	}

	public static int GetFirearmSelectionIndex(
		IList<LargeLadWeaponState> coreWeapons,
		LargeLadWeaponState exclusiveWeapon,
		LargeLadInventorySelection selection )
	{
		if ( selection.Kind ==
			LargeLadInventorySelectionKind.CoreFirearm )
			return FindCoreWeapon( coreWeapons, selection.Weapon );

		if ( selection.Kind ==
				LargeLadInventorySelectionKind.ExclusiveFirearm &&
			IsValidExclusiveState( exclusiveWeapon ) &&
			selection.Weapon == exclusiveWeapon.Weapon &&
			selection.ExclusiveInstanceId ==
				exclusiveWeapon.ExclusiveInstanceId )
		{
			return coreWeapons?.Count ?? 0;
		}

		return -1;
	}

	public static bool TryGetFirearmAt(
		IList<LargeLadWeaponState> coreWeapons,
		LargeLadWeaponState exclusiveWeapon,
		int index,
		out LargeLadWeaponState state )
	{
		state = default;

		if ( coreWeapons is null || index < 0 )
			return false;

		if ( index < coreWeapons.Count )
		{
			state = coreWeapons[index];
			return IsValidCoreState( state );
		}

		if ( index == coreWeapons.Count &&
			IsValidExclusiveState( exclusiveWeapon ) )
		{
			state = exclusiveWeapon;
			return true;
		}

		return false;
	}

	public static int GetCycledIndex(
		int currentIndex,
		int selectionCount,
		int direction )
	{
		if ( selectionCount <= 0 || direction == 0 )
			return -1;

		if ( currentIndex < 0 || currentIndex >= selectionCount )
			return direction > 0 ? 0 : selectionCount - 1;

		var candidate = (currentIndex + direction) % selectionCount;
		return candidate < 0 ? candidate + selectionCount : candidate;
	}

	public static LargeLadInventorySelection GetFirearmFallback(
		IList<LargeLadWeaponState> coreWeapons,
		LargeLadWeaponId lastSelectedCoreWeapon )
	{
		var remembered = FindCoreWeapon(
			coreWeapons,
			lastSelectedCoreWeapon );

		if ( remembered >= 0 &&
			IsValidCoreState( coreWeapons[remembered] ) )
		{
			return LargeLadInventorySelection.ForCoreFirearm(
				coreWeapons[remembered].Weapon );
		}

		return LargeLadInventorySelection.ForRoleMelee();
	}

	public static LargeLadInventorySelection FirearmSelectionFor(
		LargeLadWeaponState state )
	{
		return IsValidExclusiveState( state )
			? LargeLadInventorySelection.ForExclusiveFirearm(
				state.Weapon,
				state.ExclusiveInstanceId )
			: IsValidCoreState( state )
				? LargeLadInventorySelection.ForCoreFirearm( state.Weapon )
				: LargeLadInventorySelection.None;
	}

	public static int GetInventorySelectionCount(
		IList<LargeLadWeaponState> coreWeapons,
		LargeLadWeaponState exclusiveWeapon,
		LargeLadUtilityState utility )
	{
		return 1 +
			(coreWeapons?.Count ?? 0) +
			(IsValidExclusiveState( exclusiveWeapon ) ? 1 : 0) +
			(LargeLadUtilityRules.IsValidState( utility ) ? 1 : 0);
	}

	/// <summary>
	/// Returns the absolute ordered inventory position: role melee, catalog-
	/// ordered core firearms, exclusive firearm, then the utility slot.
	/// </summary>
	public static int GetInventorySelectionIndex(
		IList<LargeLadWeaponState> coreWeapons,
		LargeLadWeaponState exclusiveWeapon,
		LargeLadUtilityState utility,
		LargeLadInventorySelection selection )
	{
		if ( selection == LargeLadInventorySelection.ForRoleMelee() )
			return 0;

		var firearmIndex = GetFirearmSelectionIndex(
			coreWeapons,
			exclusiveWeapon,
			selection );

		if ( firearmIndex >= 0 )
			return firearmIndex + 1;

		if ( LargeLadUtilityRules.IsValidState( utility ) &&
			selection == LargeLadUtilityRules.SelectionFor( utility ) )
		{
			return 1 +
				(coreWeapons?.Count ?? 0) +
				(IsValidExclusiveState( exclusiveWeapon ) ? 1 : 0);
		}

		return -1;
	}

	public static bool TryGetInventorySelectionAt(
		IList<LargeLadWeaponState> coreWeapons,
		LargeLadWeaponState exclusiveWeapon,
		LargeLadUtilityState utility,
		int index,
		out LargeLadInventorySelection selection )
	{
		selection = LargeLadInventorySelection.None;

		if ( index < 0 )
			return false;

		if ( index == 0 )
		{
			selection = LargeLadInventorySelection.ForRoleMelee();
			return true;
		}

		var firearmIndex = index - 1;

		if ( TryGetFirearmAt(
			coreWeapons,
			exclusiveWeapon,
			firearmIndex,
			out var firearm ) )
		{
			selection = FirearmSelectionFor( firearm );
			return true;
		}

		var utilityIndex = 1 +
			(coreWeapons?.Count ?? 0) +
			(IsValidExclusiveState( exclusiveWeapon ) ? 1 : 0);

		if ( index == utilityIndex &&
			LargeLadUtilityRules.IsValidState( utility ) )
		{
			selection = LargeLadUtilityRules.SelectionFor( utility );
			return true;
		}

		return false;
	}

	public static bool TryGetFirearmForSelection(
		IList<LargeLadWeaponState> coreWeapons,
		LargeLadWeaponState exclusiveWeapon,
		LargeLadInventorySelection selection,
		out LargeLadWeaponState state )
	{
		var index = GetFirearmSelectionIndex(
			coreWeapons,
			exclusiveWeapon,
			selection );
		return TryGetFirearmAt(
			coreWeapons,
			exclusiveWeapon,
			index,
			out state );
	}

	public static LargeLadInventorySelection GetUtilityRemovalFallback(
		IList<LargeLadWeaponState> coreWeapons,
		LargeLadWeaponState exclusiveWeapon,
		LargeLadWeaponId lastSelectedCoreWeapon )
	{
		if ( IsValidExclusiveState( exclusiveWeapon ) )
			return FirearmSelectionFor( exclusiveWeapon );

		return GetFirearmFallback(
			coreWeapons,
			lastSelectedCoreWeapon );
	}
}

public enum LargeLadExclusiveLocation
{
	OriginAvailable,
	Carried,
	Dropped
}

/// <summary>
/// Host-only lifecycle for one authored exclusive instance. Its ammunition is
/// mutated only by firing/reload state handed back from the carrier, and full
/// ammunition is restored only by ResetForRound.
/// </summary>
public sealed class LargeLadExclusiveInstance
{
	private readonly int fullMagazine;
	private readonly int fullReserve;

	public LargeLadExclusiveInstance(
		int instanceId,
		LargeLadWeaponId weapon,
		int fullMagazine,
		int fullReserve )
	{
		InstanceId = instanceId;
		Weapon = weapon;
		this.fullMagazine = System.Math.Max( 0, fullMagazine );
		this.fullReserve = System.Math.Max( 0, fullReserve );
		ResetForRound();
	}

	public int InstanceId { get; }
	public LargeLadWeaponId Weapon { get; }
	public LargeLadExclusiveLocation Location { get; private set; }
	public object Carrier { get; private set; }
	public LargeLadWeaponState State { get; private set; }

	public bool TryCollectFromOrigin(
		object carrier,
		out LargeLadWeaponState state )
	{
		state = State;

		if ( carrier is null ||
			Location != LargeLadExclusiveLocation.OriginAvailable )
		{
			return false;
		}

		Carrier = carrier;
		Location = LargeLadExclusiveLocation.Carried;
		return true;
	}

	public bool TryDrop(
		object carrier,
		LargeLadWeaponState state )
	{
		if ( Location != LargeLadExclusiveLocation.Carried ||
			!ReferenceEquals( Carrier, carrier ) ||
			!MatchesIdentity( state ) )
		{
			return false;
		}

		State = Sanitize( state );
		Carrier = null;
		Location = LargeLadExclusiveLocation.Dropped;
		return true;
	}

	public bool TryCollectDropped(
		object carrier,
		out LargeLadWeaponState state )
	{
		state = State;

		if ( carrier is null ||
			Location != LargeLadExclusiveLocation.Dropped )
		{
			return false;
		}

		Carrier = carrier;
		Location = LargeLadExclusiveLocation.Carried;
		return true;
	}

	public bool RestoreDroppedAfterRejectedTransfer()
	{
		if ( Location != LargeLadExclusiveLocation.Carried )
			return false;

		Carrier = null;
		Location = LargeLadExclusiveLocation.Dropped;
		return true;
	}

	public bool ReturnCarrierToOrigin(
		object carrier,
		LargeLadWeaponState state )
	{
		if ( Location != LargeLadExclusiveLocation.Carried ||
			!ReferenceEquals( Carrier, carrier ) ||
			!MatchesIdentity( state ) )
		{
			return false;
		}

		State = Sanitize( state );
		Carrier = null;
		Location = LargeLadExclusiveLocation.OriginAvailable;
		return true;
	}

	public bool ReturnDroppedToOrigin()
	{
		if ( Location != LargeLadExclusiveLocation.Dropped )
			return false;

		Carrier = null;
		Location = LargeLadExclusiveLocation.OriginAvailable;
		return true;
	}

	public bool ForceReturnToOrigin( LargeLadWeaponState state )
	{
		if ( !MatchesIdentity( state ) )
			return false;

		State = Sanitize( state );
		Carrier = null;
		Location = LargeLadExclusiveLocation.OriginAvailable;
		return true;
	}

	public void ResetForRound()
	{
		State = LargeLadWeaponState.CreateExclusive(
			Weapon,
			InstanceId,
			fullMagazine,
			fullReserve );
		Carrier = null;
		Location = LargeLadExclusiveLocation.OriginAvailable;
	}

	private bool MatchesIdentity( LargeLadWeaponState state )
	{
		return state.Weapon == Weapon &&
			state.ExclusiveInstanceId == InstanceId &&
			LargeLadInventoryRules.IsValidExclusiveState( state );
	}

	private LargeLadWeaponState Sanitize( LargeLadWeaponState state )
	{
		state.Weapon = Weapon;
		state.ExclusiveInstanceId = InstanceId;
		state.AmmunitionMode = LargeLadAmmunitionMode.FiniteReserve;
		state.Magazine = System.Math.Max( 0, state.Magazine );
		state.Reserve = System.Math.Max( 0, state.Reserve );
		return state;
	}
}
