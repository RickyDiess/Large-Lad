using Sandbox;

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
	CorePerPlayer,
	GloballyExclusive
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
		PickupColor = new Color( 0.25f, 0.85f, 1.0f )
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
		PickupColor = new Color( 1.0f, 0.78f, 0.18f )
	};

	public static LargeLadWeaponDefinition Get( LargeLadWeaponId id )
	{
		return id switch
		{
			LargeLadWeaponId.Melee => Melee,
			LargeLadWeaponId.Pistol => Pistol,
			LargeLadWeaponId.Smg => Smg,
			_ => Unarmed
		};
	}

	public static bool IsFirearm( LargeLadWeaponId id )
	{
		return id is LargeLadWeaponId.Pistol or LargeLadWeaponId.Smg;
	}
}
