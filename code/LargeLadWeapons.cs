using Sandbox;
using System.Collections.Generic;

public enum LargeLadWeaponId
{
	None,
	Melee,
	Pistol,
	Smg,
	Shotgun,
	Rifle
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

	private static readonly LargeLadWeaponDefinition Pistol = new()
	{
		Id = LargeLadWeaponId.Pistol,
		DisplayName = "Pistol",
		Crosshair = LargeLadCrosshairStyle.FourSegment,
		AccentColor = new Color( 0.25f, 0.85f, 1.0f ),
		NativePrefabPath = "prefabs/gameplay/native_pistol.prefab"
	};

	private static readonly LargeLadWeaponDefinition Smg = new()
	{
		Id = LargeLadWeaponId.Smg,
		DisplayName = "SMG",
		Crosshair = LargeLadCrosshairStyle.FourSegment,
		AccentColor = new Color( 1.0f, 0.78f, 0.18f ),
		NativePrefabPath = "prefabs/gameplay/native_smg.prefab"
	};

	private static readonly LargeLadWeaponDefinition Shotgun = new()
	{
		Id = LargeLadWeaponId.Shotgun,
		DisplayName = "Shotgun",
		Crosshair = LargeLadCrosshairStyle.FourSegment,
		AccentColor = new Color( 1.0f, 0.56f, 0.16f ),
		NativePrefabPath = "prefabs/gameplay/native_shotgun.prefab"
	};

	private static readonly LargeLadWeaponDefinition Rifle = new()
	{
		Id = LargeLadWeaponId.Rifle,
		DisplayName = "Rifle",
		Crosshair = LargeLadCrosshairStyle.FourSegment,
		AccentColor = new Color( 0.48f, 0.82f, 0.28f ),
		NativePrefabPath = "prefabs/gameplay/native_rifle.prefab"
	};

	private static readonly LargeLadWeaponDefinition[] Firearms =
	{
		Pistol,
		Smg,
		Shotgun,
		Rifle
	};

	public static IReadOnlyList<LargeLadWeaponDefinition> FirearmDefinitions =>
		Firearms;

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
		foreach ( var candidate in Firearms )
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

		foreach ( var definition in Firearms )
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
