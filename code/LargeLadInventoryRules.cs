using System.Collections.Generic;

/// <summary>
/// Side-effect-free inventory decisions shared by the host component and unit
/// tests. Runtime networking and world-object creation remain in components.
/// </summary>
public static class LargeLadInventoryRules
{
	public static bool CanUseFirearmInventory(
		LargeLadRole role,
		bool isDead )
	{
		return role == LargeLadRole.SkinnyKid && !isDead;
	}

	public static bool CanProcessOwnerRequest(
		bool isHost,
		bool ownerRequest,
		LargeLadRole role,
		bool isDead )
	{
		return isHost &&
			ownerRequest &&
			CanUseFirearmInventory( role, isDead );
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

	public static bool CanSelect(
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
		LargeLadWeaponSelection activeSelection )
	{
		return CanProcessOwnerRequest(
				isHost,
				ownerRequest,
				role,
				isDead ) &&
			IsValidExclusiveState( state ) &&
			activeSelection == SelectionFor( state );
	}

	public static bool CanReload(
		bool isHost,
		bool ownerRequest,
		LargeLadRole role,
		bool isDead,
		bool isAlreadyReloading,
		LargeLadWeaponState state )
	{
		if ( !CanSelect(
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

	public static int GetSelectionIndex(
		IList<LargeLadWeaponState> coreWeapons,
		LargeLadWeaponState exclusiveWeapon,
		LargeLadWeaponSelection selection )
	{
		if ( selection.Kind == LargeLadWeaponSelectionKind.Core )
			return FindCoreWeapon( coreWeapons, selection.Weapon );

		if ( selection.Kind == LargeLadWeaponSelectionKind.Exclusive &&
			IsValidExclusiveState( exclusiveWeapon ) &&
			selection.Weapon == exclusiveWeapon.Weapon &&
			selection.ExclusiveInstanceId ==
				exclusiveWeapon.ExclusiveInstanceId )
		{
			return coreWeapons?.Count ?? 0;
		}

		return -1;
	}

	public static bool TryGetWeaponAt(
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
		int weaponCount,
		int direction )
	{
		if ( weaponCount <= 0 || direction == 0 )
			return -1;

		if ( currentIndex < 0 || currentIndex >= weaponCount )
			return direction > 0 ? 0 : weaponCount - 1;

		var candidate = (currentIndex + direction) % weaponCount;
		return candidate < 0 ? candidate + weaponCount : candidate;
	}

	public static LargeLadWeaponSelection GetCoreFallback(
		IList<LargeLadWeaponState> coreWeapons,
		LargeLadWeaponId lastSelectedCoreWeapon )
	{
		var remembered = FindCoreWeapon(
			coreWeapons,
			lastSelectedCoreWeapon );

		if ( remembered >= 0 &&
			IsValidCoreState( coreWeapons[remembered] ) )
		{
			return LargeLadWeaponSelection.ForCore(
				coreWeapons[remembered].Weapon );
		}

		return LargeLadWeaponSelection.ForRoleMelee();
	}

	public static LargeLadWeaponSelection SelectionFor(
		LargeLadWeaponState state )
	{
		return IsValidExclusiveState( state )
			? LargeLadWeaponSelection.ForExclusive(
				state.Weapon,
				state.ExclusiveInstanceId )
			: IsValidCoreState( state )
				? LargeLadWeaponSelection.ForCore( state.Weapon )
				: LargeLadWeaponSelection.None;
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
