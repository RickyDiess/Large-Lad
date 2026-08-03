using Sandbox;
using System.Collections.Generic;

public enum LargeLadWeaponId
{
	None,
	Melee,
	Pistol,
	Smg
}

public enum LargeLadCrosshairStyle
{
	None,
	Dot,
	FourSegment
}

public enum LargeLadPickupPolicy
{
	Core,
	Exclusive
}

public enum LargeLadAmmunitionMode
{
	InfiniteReserve,
	FiniteReserve
}

public enum LargeLadInventorySelectionKind
{
	None,
	RoleAbility,
	CoreFirearm,
	ExclusiveFirearm,
	Utility
}

/// <summary>
/// The selected entry in the Skinny Kid's deliberately small ordered
/// inventory. Role melee, firearms, and the one utility slot keep distinct
/// identities so utility selection never needs a fake weapon definition.
/// </summary>
public struct LargeLadInventorySelection :
	System.IEquatable<LargeLadInventorySelection>
{
	public LargeLadInventorySelectionKind Kind { get; set; }
	public LargeLadWeaponId Weapon { get; set; }
	public int ExclusiveInstanceId { get; set; }
	public LargeLadUtilityId Utility { get; set; }
	public int UtilityInstanceId { get; set; }

	public static LargeLadInventorySelection None => default;

	public static LargeLadInventorySelection ForCoreFirearm(
		LargeLadWeaponId weapon )
	{
		return new LargeLadInventorySelection
		{
			Kind = LargeLadInventorySelectionKind.CoreFirearm,
			Weapon = weapon
		};
	}

	public static LargeLadInventorySelection ForRoleMelee()
	{
		return new LargeLadInventorySelection
		{
			Kind = LargeLadInventorySelectionKind.RoleAbility,
			Weapon = LargeLadWeaponId.Melee
		};
	}

	public static LargeLadInventorySelection ForExclusiveFirearm(
		LargeLadWeaponId weapon,
		int instanceId )
	{
		return new LargeLadInventorySelection
		{
			Kind = LargeLadInventorySelectionKind.ExclusiveFirearm,
			Weapon = weapon,
			ExclusiveInstanceId = instanceId
		};
	}

	public static LargeLadInventorySelection ForUtility(
		LargeLadUtilityId utility,
		int instanceId )
	{
		return new LargeLadInventorySelection
		{
			Kind = LargeLadInventorySelectionKind.Utility,
			Utility = utility,
			UtilityInstanceId = instanceId
		};
	}

	public bool Equals( LargeLadInventorySelection other )
	{
		return Kind == other.Kind &&
			Weapon == other.Weapon &&
			ExclusiveInstanceId == other.ExclusiveInstanceId &&
			Utility == other.Utility &&
			UtilityInstanceId == other.UtilityInstanceId;
	}

	public override bool Equals( object obj )
	{
		return obj is LargeLadInventorySelection other && Equals( other );
	}

	public override int GetHashCode()
	{
		return System.HashCode.Combine(
			Kind,
			Weapon,
			ExclusiveInstanceId,
			Utility,
			UtilityInstanceId );
	}

	public static bool operator ==(
		LargeLadInventorySelection left,
		LargeLadInventorySelection right )
	{
		return left.Equals( right );
	}

	public static bool operator !=(
		LargeLadInventorySelection left,
		LargeLadInventorySelection right )
	{
		return !left.Equals( right );
	}
}

/// <summary>
/// One host-authored, synchronized firearm state. Core entries use explicit
/// infinite reserve and instance id zero. Exclusive entries use finite reserve
/// and retain their authored pickup's stable per-match instance id.
/// </summary>
public struct LargeLadWeaponState : System.IEquatable<LargeLadWeaponState>
{
	public LargeLadWeaponId Weapon { get; set; }
	public int Magazine { get; set; }
	public int Reserve { get; set; }
	public LargeLadAmmunitionMode AmmunitionMode { get; set; }
	public int ExclusiveInstanceId { get; set; }

	public bool IsOwned => LargeLadWeaponCatalog.IsFirearm( Weapon );
	public bool HasInfiniteReserve =>
		AmmunitionMode == LargeLadAmmunitionMode.InfiniteReserve;
	public bool IsExclusive => ExclusiveInstanceId > 0;

	public static LargeLadWeaponState CreateCore( LargeLadWeaponId weapon )
	{
		var definition = LargeLadWeaponCatalog.Get( weapon );
		return new LargeLadWeaponState
		{
			Weapon = weapon,
			Magazine = definition.MagazineSize,
			Reserve = 0,
			AmmunitionMode = LargeLadAmmunitionMode.InfiniteReserve,
			ExclusiveInstanceId = 0
		};
	}

	public static LargeLadWeaponState CreateExclusive(
		LargeLadWeaponId weapon,
		int instanceId,
		int magazine,
		int reserve )
	{
		return new LargeLadWeaponState
		{
			Weapon = weapon,
			Magazine = System.Math.Max( 0, magazine ),
			Reserve = System.Math.Max( 0, reserve ),
			AmmunitionMode = LargeLadAmmunitionMode.FiniteReserve,
			ExclusiveInstanceId = System.Math.Max( 0, instanceId )
		};
	}

	public bool Equals( LargeLadWeaponState other )
	{
		return Weapon == other.Weapon &&
			Magazine == other.Magazine &&
			Reserve == other.Reserve &&
			AmmunitionMode == other.AmmunitionMode &&
			ExclusiveInstanceId == other.ExclusiveInstanceId;
	}

	public override bool Equals( object obj )
	{
		return obj is LargeLadWeaponState other && Equals( other );
	}

	public override int GetHashCode()
	{
		return System.HashCode.Combine(
			Weapon,
			Magazine,
			Reserve,
			AmmunitionMode,
			ExclusiveInstanceId );
	}

	public static bool operator ==(
		LargeLadWeaponState left,
		LargeLadWeaponState right )
	{
		return left.Equals( right );
	}

	public static bool operator !=(
		LargeLadWeaponState left,
		LargeLadWeaponState right )
	{
		return !left.Equals( right );
	}
}

/// <summary>
/// Immutable weapon behavior shared by inventories, firing, and HUD. Pickup
/// ownership policy deliberately lives on each map-authored weapon pickup so
/// different instances of the same weapon can use different policies.
/// </summary>
public sealed class LargeLadWeaponDefinition
{
	public LargeLadWeaponId Id { get; init; }
	public string DisplayName { get; init; }
	public float Damage { get; init; }
	public float FireInterval { get; init; }
	public float Range { get; init; }
	public int MagazineSize { get; init; }
	public int StartingReserve { get; init; }
	public float ReloadDuration { get; init; }
	public LargeLadCrosshairStyle Crosshair { get; init; }
	public Color PickupColor { get; init; }
	public string WorldModelPath { get; init; }
	public bool UsesAmmo => MagazineSize > 0;
}

public static class LargeLadWeaponCatalog
{
	private static readonly LargeLadWeaponDefinition Unarmed = new()
	{
		Id = LargeLadWeaponId.None,
		DisplayName = "Unarmed",
		Crosshair = LargeLadCrosshairStyle.None,
		PickupColor = Color.Gray
	};

	private static readonly LargeLadWeaponDefinition Melee = new()
	{
		Id = LargeLadWeaponId.Melee,
		DisplayName = "Melee",
		Damage = 25.0f,
		FireInterval = 0.65f,
		Range = 100.0f,
		Crosshair = LargeLadCrosshairStyle.Dot,
		PickupColor = Color.Red
	};

	private static readonly LargeLadWeaponDefinition Pistol = new()
	{
		Id = LargeLadWeaponId.Pistol,
		DisplayName = "Pistol",
		Damage = 100.0f,
		FireInterval = 0.35f,
		Range = 1200.0f,
		MagazineSize = 8,
		StartingReserve = 32,
		ReloadDuration = 1.4f,
		Crosshair = LargeLadCrosshairStyle.FourSegment,
		PickupColor = new Color( 0.25f, 0.85f, 1.0f ),
		WorldModelPath =
			"models/weapons/sbox_pistol_usp/w_usp.vmdl"
	};

	private static readonly LargeLadWeaponDefinition Smg = new()
	{
		Id = LargeLadWeaponId.Smg,
		DisplayName = "SMG",
		Damage = 25.0f,
		FireInterval = 0.09f,
		Range = 1000.0f,
		MagazineSize = 30,
		StartingReserve = 90,
		ReloadDuration = 2.0f,
		Crosshair = LargeLadCrosshairStyle.FourSegment,
		PickupColor = new Color( 1.0f, 0.78f, 0.18f ),
		WorldModelPath =
			"models/weapons/sbox_smg_mp5/w_mp5.vmdl"
	};

	private static readonly LargeLadWeaponDefinition[] Firearms =
	{
		Pistol,
		Smg
	};

	public static IReadOnlyList<LargeLadWeaponDefinition> FirearmDefinitions =>
		Firearms;

	public static LargeLadWeaponDefinition Get( LargeLadWeaponId id )
	{
		if ( id == LargeLadWeaponId.Melee )
			return Melee;

		foreach ( var definition in Firearms )
		{
			if ( definition.Id == id )
				return definition;
		}

		return Unarmed;
	}

	public static bool IsFirearm( LargeLadWeaponId id )
	{
		return GetCatalogOrder( id ) >= 0;
	}

	public static int GetCatalogOrder( LargeLadWeaponId id )
	{
		for ( var index = 0; index < Firearms.Length; index++ )
		{
			if ( Firearms[index].Id == id )
				return index;
		}

		return -1;
	}
}
