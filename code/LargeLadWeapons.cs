using Sandbox;
using System.Collections.Generic;

public enum LargeLadWeaponId
{
	None = 0,
	Melee = 1,
	Pistol = 2,
	Smg = 3,
	// Value 4 is intentionally retired. These IDs are serialized and networked.
	Shotgun = 5,
	Rifle = 6
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

/// <summary>
/// Stable Large Lad identity and UI metadata for a native weapon. The native
/// BaseCombatWeapon prefab owns mechanics, sounds, models, effects, animations,
/// and inventory slot configuration. Pickup ownership policy deliberately
/// lives on each map-authored pickup so instances can use different policies.
/// </summary>
public sealed class LargeLadWeaponDefinition
{
	public LargeLadWeaponId Id { get; init; }
	public string DisplayName { get; init; }
	public LargeLadCrosshairStyle Crosshair { get; init; }
	public Color AccentColor { get; init; }

	/// <summary>
	/// Routing metadata used to instantiate the one authoritative native prefab.
	/// Core and Exclusive instances both clone this prefab.
	/// </summary>
	public string NativePrefabPath { get; init; }

	public IReadOnlyList<string> GetValidationWarnings()
	{
		return LargeLadWeaponCatalog.GetValidationWarnings( this );
	}
}

public static class LargeLadWeaponCatalog
{
	private static readonly LargeLadWeaponDefinition Unarmed = new()
	{
		Id = LargeLadWeaponId.None,
		DisplayName = "Unarmed",
		Crosshair = LargeLadCrosshairStyle.None,
		AccentColor = Color.Gray
	};

	private static readonly LargeLadWeaponDefinition Melee = new()
	{
		Id = LargeLadWeaponId.Melee,
		DisplayName = "Melee",
		Crosshair = LargeLadCrosshairStyle.Dot,
		AccentColor = Color.Red
	};

	private static LargeLadWeaponDefinition Pistol =
		CreatePistolDefinition();

	private static LargeLadWeaponDefinition CreatePistolDefinition() => new()
	{
		Id = LargeLadWeaponId.Pistol,
		DisplayName = "Pistol",
		Crosshair = LargeLadCrosshairStyle.FourSegment,
		AccentColor = new Color( 0.25f, 0.85f, 1.0f ),
		NativePrefabPath = "prefabs/gameplay/native_pistol.prefab"
	};

	private static LargeLadWeaponDefinition Smg =
		CreateSmgDefinition();

	private static LargeLadWeaponDefinition CreateSmgDefinition() => new()
	{
		Id = LargeLadWeaponId.Smg,
		DisplayName = "SMG",
		Crosshair = LargeLadCrosshairStyle.FourSegment,
		AccentColor = new Color( 1.0f, 0.78f, 0.18f ),
		NativePrefabPath = "prefabs/gameplay/native_smg.prefab"
	};

	private static LargeLadWeaponDefinition Shotgun =
		CreateShotgunDefinition();

	private static LargeLadWeaponDefinition CreateShotgunDefinition() => new()
	{
		Id = LargeLadWeaponId.Shotgun,
		DisplayName = "Shotgun",
		Crosshair = LargeLadCrosshairStyle.FourSegment,
		AccentColor = new Color( 1.0f, 0.56f, 0.16f ),
		NativePrefabPath = "prefabs/gameplay/native_shotgun.prefab"
	};

	private static LargeLadWeaponDefinition Rifle =
		CreateRifleDefinition();

	private static LargeLadWeaponDefinition CreateRifleDefinition() => new()
	{
		Id = LargeLadWeaponId.Rifle,
		DisplayName = "Rifle",
		Crosshair = LargeLadCrosshairStyle.FourSegment,
		AccentColor = new Color( 0.48f, 0.82f, 0.28f ),
		NativePrefabPath = "prefabs/gameplay/native_rifle.prefab"
	};

	private static LargeLadWeaponDefinition[] Firearms =
		CreateFirearmList();

	public static IReadOnlyList<LargeLadWeaponDefinition> FirearmDefinitions =>
		GetCurrentFirearms();

	public static LargeLadWeaponDefinition Get( LargeLadWeaponId id )
	{
		if ( id == LargeLadWeaponId.None )
			return Unarmed;

		if ( id == LargeLadWeaponId.Melee )
			return Melee;

		return TryGetFirearm( id, out var definition )
			? definition
			: Unarmed;
	}

	public static bool TryGet(
		LargeLadWeaponId id,
		out LargeLadWeaponDefinition definition )
	{
		if ( id == LargeLadWeaponId.None )
		{
			definition = Unarmed;
			return true;
		}

		if ( id == LargeLadWeaponId.Melee )
		{
			definition = Melee;
			return true;
		}

		return TryGetFirearm( id, out definition );
	}

	public static bool TryGetFirearm(
		LargeLadWeaponId id,
		out LargeLadWeaponDefinition definition )
	{
		foreach ( var candidate in GetCurrentFirearms() )
		{
			if ( candidate.Id != id )
				continue;

			definition = candidate;
			return true;
		}

		definition = null;
		return false;
	}

	public static bool IsFirearm( LargeLadWeaponId id )
	{
		return TryGetFirearm( id, out _ );
	}

	public static IReadOnlyList<string> GetCatalogValidationWarnings()
	{
		var warnings = new List<string>();
		var seen = new HashSet<LargeLadWeaponId>();

		foreach ( var definition in GetCurrentFirearms() )
		{
			if ( definition is not null && !seen.Add( definition.Id ) )
			{
				warnings.Add(
					$"Weapon catalog has more than one definition for " +
					$"'{definition.Id}'." );
			}

			var name = definition?.Id.ToString() ?? "<null>";
			foreach ( var warning in GetValidationWarnings( definition ) )
				warnings.Add( $"Weapon definition '{name}': {warning}" );
		}

		foreach ( var id in System.Enum.GetValues<LargeLadWeaponId>() )
		{
			if ( id is LargeLadWeaponId.None or LargeLadWeaponId.Melee )
				continue;

			if ( !seen.Contains( id ) )
			{
				warnings.Add(
					$"Weapon id '{id}' has no firearm definition in the catalog." );
			}
		}

		return warnings;
	}

	private static LargeLadWeaponDefinition[] GetCurrentFirearms()
	{
		// s&box preserves static field values during hotload. Rebuild catalog
		// definitions when serialized enum identities changed between compiles.
		if ( Pistol?.Id != LargeLadWeaponId.Pistol )
			Pistol = CreatePistolDefinition();

		if ( Smg?.Id != LargeLadWeaponId.Smg )
			Smg = CreateSmgDefinition();

		if ( Shotgun?.Id != LargeLadWeaponId.Shotgun )
			Shotgun = CreateShotgunDefinition();

		if ( Rifle?.Id != LargeLadWeaponId.Rifle )
			Rifle = CreateRifleDefinition();

		if ( Firearms is null ||
			Firearms.Length != 4 ||
			!object.ReferenceEquals( Firearms[0], Pistol ) ||
			!object.ReferenceEquals( Firearms[1], Smg ) ||
			!object.ReferenceEquals( Firearms[2], Shotgun ) ||
			!object.ReferenceEquals( Firearms[3], Rifle ) )
		{
			Firearms = CreateFirearmList();
		}

		return Firearms;
	}

	private static LargeLadWeaponDefinition[] CreateFirearmList()
	{
		return new[]
		{
			Pistol,
			Smg,
			Shotgun,
			Rifle
		};
	}

	public static IReadOnlyList<string> GetValidationWarnings(
		LargeLadWeaponDefinition definition )
	{
		var warnings = new List<string>();

		if ( definition is null )
		{
			warnings.Add( "Definition is missing." );
			return warnings;
		}

		if ( !System.Enum.IsDefined(
			typeof( LargeLadWeaponId ),
			definition.Id ) ||
			definition.Id is LargeLadWeaponId.None or LargeLadWeaponId.Melee )
		{
			warnings.Add( "A firearm needs a firearm weapon id." );
		}

		RequireText( warnings, definition.DisplayName, "Display name" );

		if ( !System.Enum.IsDefined(
			typeof( LargeLadCrosshairStyle ),
			definition.Crosshair ) ||
			definition.Crosshair == LargeLadCrosshairStyle.None )
		{
			warnings.Add( "Crosshair must be a supported visible style." );
		}

		RequireText( warnings, definition.NativePrefabPath, "Native prefab path" );

		return warnings;
	}

	private static void RequireText(
		ICollection<string> warnings,
		string value,
		string name )
	{
		if ( string.IsNullOrWhiteSpace( value ) )
			warnings.Add( $"{name} is required." );
	}

}
