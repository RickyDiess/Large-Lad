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

/// <summary>
/// The small set of authoritative firearm firing behaviors. This is data, not
/// a scriptable action graph: the firearm driver owns the implementation for
/// each explicit behavior.
/// </summary>
public enum LargeLadFirearmArchetype
{
	None,
	SemiAutomatic,
	Automatic,
	Shotgun
}

public enum LargeLadWeaponGrip
{
	None,
	RightHandedOneHanded,
	RightHandedTwoHanded
}

/// <summary>
/// Presentation pose used by the Citizen third-person animgraph. Keep this
/// separate from grip handedness: not every Citizen hold type supports the
/// handedness parameter.
/// </summary>
public enum LargeLadThirdPersonHoldType
{
	None,
	Pistol,
	Rifle,
	HoldItem
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
/// Immutable firearm behavior and presentation shared by inventories, firing,
/// pickups, HUD, and presentation components. Pickup ownership policy
/// deliberately lives on each map-authored weapon pickup so different
/// instances of the same weapon can use different policies.
/// </summary>
public sealed class LargeLadWeaponDefinition
{
	public LargeLadWeaponId Id { get; init; }
	public string DisplayName { get; init; }
	public LargeLadFirearmArchetype Archetype { get; init; }
	public float Damage { get; init; }
	public float FireInterval { get; init; }
	public float Range { get; init; }
	public int MagazineSize { get; init; }
	public int StartingReserve { get; init; }
	public float ReloadDuration { get; init; }
	public LargeLadCrosshairStyle Crosshair { get; init; }
	public Color PickupColor { get; init; }

	public string FirstPersonModelPath { get; init; }
	public string FirstPersonModelPackageIdent { get; init; }
	public string FirstPersonArmsModelPath { get; init; }
	public string FirstPersonArmsPackageIdent { get; init; }
	public bool FirstPersonModelIncludesArms { get; init; } = true;
	public Vector3 FirstPersonPositionOffset { get; init; }
	public Angles FirstPersonRotationOffset { get; init; }
	public float FirstPersonModelScale { get; init; } = 1.0f;
	public bool FirstPersonUsesAnimGraph { get; init; }
	public string FirstPersonAnimationGraphPath { get; init; }
	public int FirstPersonSkeleton { get; init; }
	public bool FirstPersonTwoHanded { get; init; }

	public string ThirdPersonWorldModelPath { get; init; }
	public string ThirdPersonWorldModelPackageIdent { get; init; }
	public string ThirdPersonAttachmentBone { get; init; }
	public Vector3 ThirdPersonPositionOffset { get; init; }
	public Angles ThirdPersonRotationOffset { get; init; }
	public Vector3 ThirdPersonGripOffset { get; init; }
	public float ThirdPersonModelScale { get; init; } = 1.0f;
	public LargeLadThirdPersonHoldType ThirdPersonHoldType { get; init; }
	public LargeLadWeaponGrip Grip { get; init; }

	public string DrawAnimation { get; init; }
	public string IdleAnimation { get; init; }
	public string FireAnimation { get; init; }
	public string ReloadAnimation { get; init; }
	public string DryFireAnimation { get; init; }
	public string EmptyAnimation { get; init; }

	public string FireSoundPath { get; init; }
	public string FireSoundPackageIdent { get; init; }
	public string ReloadSoundPath { get; init; }
	public string ReloadSoundPackageIdent { get; init; }
	public string EmptySoundPath { get; init; }
	public string EmptySoundPackageIdent { get; init; }
	public string DrawSoundPath { get; init; }
	public string DrawSoundPackageIdent { get; init; }

	public string MuzzleAttachment { get; init; }
	public string MuzzleEffectPrefabPath { get; init; }
	public float MuzzleEffectScale { get; init; } = 1.0f;
	public string ImpactEffectPrefabPath { get; init; }
	public float ImpactEffectScale { get; init; } = 1.0f;

	/// <summary>
	/// Authoritative shot-count/spread data. Presentation code may visualize the
	/// result, but must never use these values to decide what was hit.
	/// </summary>
	public int PelletCount { get; init; } = 1;
	public float PelletSpreadDegrees { get; init; }

	/// <summary>
	/// Compatibility name retained for existing pickup/drop consumers. New code
	/// should use ThirdPersonWorldModelPath.
	/// </summary>
	public string WorldModelPath => ThirdPersonWorldModelPath;
	public bool UsesAmmo => MagazineSize > 0;
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
		PickupColor = Color.Red,
		FirstPersonModelPath =
			"models/weapons/sbox_melee_crowbar/v_crowbar.vmdl",
		FirstPersonModelPackageIdent = "facepunch/v_crowbar",
		FirstPersonArmsModelPath =
			"models/first_person/v_first_person_arms_human.vmdl",
		FirstPersonArmsPackageIdent =
			"facepunch/v_first_person_arms_human",
		FirstPersonModelIncludesArms = false,
		FirstPersonPositionOffset = Vector3.Zero,
		FirstPersonRotationOffset = Angles.Zero,
		FirstPersonModelScale = 1.0f,
		FirstPersonUsesAnimGraph = true,
		FirstPersonAnimationGraphPath =
			"models/weapons/sbox_melee_crowbar/v_crowbar.vanmgrph",
		// Bare fists use the graph assigned to the dedicated human arms. Facepunch
		// weapon graphs use skeleton=0 for this rig.
		FirstPersonSkeleton = 0,
		FirstPersonTwoHanded = true,
		ThirdPersonWorldModelPath =
			"models/citizen_props/crowbar01.vmdl",
		ThirdPersonAttachmentBone = "hold_R",
		ThirdPersonPositionOffset = Vector3.Zero,
		ThirdPersonRotationOffset = Angles.Zero,
		// citizen_props/crowbar01 is centered on its shaft and extends along
		// local Z. Put the hand near the lower handle instead of its midpoint.
		ThirdPersonGripOffset = new Vector3( 0.0f, 0.0f, -32.0f ),
		// At one quarter scale the approximately 101-unit source prop is a
		// conventional two-foot crowbar. The former one-eighth scale was only
		// about one foot long and looked miniature beside the human model.
		ThirdPersonModelScale = 0.25f,
		// This is the catalog fallback. The player prefab exposes a serialized
		// Citizen hold type/handedness/attack variant specifically for crowbar
		// tuning without changing melee authority or weapon data.
		ThirdPersonHoldType = LargeLadThirdPersonHoldType.HoldItem,
		Grip = LargeLadWeaponGrip.RightHandedOneHanded,
		DrawAnimation = "b_deploy",
		IdleAnimation = "idle",
		FireAnimation = "b_attack",
		DryFireAnimation = "b_attack_dry"
	};

	private static readonly LargeLadWeaponDefinition Pistol = new()
	{
		Id = LargeLadWeaponId.Pistol,
		DisplayName = "Pistol",
		Archetype = LargeLadFirearmArchetype.SemiAutomatic,
		Damage = 100.0f,
		FireInterval = 0.35f,
		Range = 1200.0f,
		MagazineSize = 8,
		StartingReserve = 32,
		ReloadDuration = 1.4f,
		Crosshair = LargeLadCrosshairStyle.FourSegment,
		PickupColor = new Color( 0.25f, 0.85f, 1.0f ),
		FirstPersonModelPath =
			"models/weapons/sbox_pistol_usp/v_usp.vmdl",
		FirstPersonModelPackageIdent = "facepunch/v_usp",
		FirstPersonArmsModelPath =
			"models/first_person/v_first_person_arms_human.vmdl",
		FirstPersonArmsPackageIdent =
			"facepunch/v_first_person_arms_human",
		FirstPersonModelIncludesArms = false,
		FirstPersonPositionOffset = Vector3.Zero,
		FirstPersonRotationOffset = Angles.Zero,
		FirstPersonModelScale = 1.0f,
		FirstPersonUsesAnimGraph = true,
		FirstPersonAnimationGraphPath =
			"models/weapons/sbox_pistol_usp/v_usp.vanmgrph",
		FirstPersonSkeleton = 0,
		FirstPersonTwoHanded = false,
		ThirdPersonWorldModelPath =
			"models/weapons/sbox_pistol_usp/w_usp.vmdl",
		ThirdPersonWorldModelPackageIdent = "facepunch/w_usp",
		ThirdPersonAttachmentBone = "hold_R",
		ThirdPersonPositionOffset = Vector3.Zero,
		ThirdPersonRotationOffset = Angles.Zero,
		// Facepunch world weapons are authored with their origin at the hold
		// transform. Keep model-space grip correction neutral unless an asset has
		// a measured, weapon-specific correction.
		ThirdPersonGripOffset = Vector3.Zero,
		ThirdPersonModelScale = 1.0f,
		ThirdPersonHoldType = LargeLadThirdPersonHoldType.Pistol,
		Grip = LargeLadWeaponGrip.RightHandedOneHanded,
		DrawAnimation = "b_deploy",
		IdleAnimation = "idle",
		FireAnimation = "b_attack",
		ReloadAnimation = "b_reload",
		DryFireAnimation = "b_attack_dry",
		EmptyAnimation = "b_empty",
		FireSoundPackageIdent = "vidya/pistol-shoot",
		ReloadSoundPackageIdent = "drakefruit/pistol_reload",
		EmptySoundPackageIdent = "hzgame/hzuiclickbuttontinyrattle",
		MuzzleAttachment = "muzzle",
		MuzzleEffectPrefabPath =
			"prefabs/effects/default_muzzleflash.prefab",
		MuzzleEffectScale = 0.25f,
		ImpactEffectPrefabPath = "prefabs/surface/default-bullet.prefab",
		ImpactEffectScale = 1.0f,
		PelletCount = 1,
		PelletSpreadDegrees = 0.0f
	};

	private static readonly LargeLadWeaponDefinition Smg = new()
	{
		Id = LargeLadWeaponId.Smg,
		DisplayName = "SMG",
		Archetype = LargeLadFirearmArchetype.Automatic,
		Damage = 25.0f,
		FireInterval = 0.09f,
		Range = 1000.0f,
		MagazineSize = 30,
		StartingReserve = 90,
		ReloadDuration = 2.0f,
		Crosshair = LargeLadCrosshairStyle.FourSegment,
		PickupColor = new Color( 1.0f, 0.78f, 0.18f ),
		FirstPersonModelPath =
			"models/weapons/sbox_smg_mp5/v_mp5.vmdl",
		FirstPersonModelPackageIdent = "facepunch/v_mp5",
		FirstPersonArmsModelPath =
			"models/first_person/v_first_person_arms_human.vmdl",
		FirstPersonArmsPackageIdent =
			"facepunch/v_first_person_arms_human",
		FirstPersonModelIncludesArms = false,
		FirstPersonPositionOffset = Vector3.Zero,
		FirstPersonRotationOffset = Angles.Zero,
		FirstPersonModelScale = 1.0f,
		FirstPersonUsesAnimGraph = true,
		FirstPersonAnimationGraphPath =
			"models/weapons/sbox_smg_mp5/v_mp5.vanmgrph",
		FirstPersonSkeleton = 0,
		FirstPersonTwoHanded = true,
		ThirdPersonWorldModelPath =
			"models/weapons/sbox_smg_mp5/w_mp5.vmdl",
		ThirdPersonWorldModelPackageIdent = "facepunch/w_mp5",
		ThirdPersonAttachmentBone = "hold_R",
		ThirdPersonPositionOffset = Vector3.Zero,
		ThirdPersonRotationOffset = Angles.Zero,
		ThirdPersonGripOffset = Vector3.Zero,
		ThirdPersonModelScale = 1.0f,
		ThirdPersonHoldType = LargeLadThirdPersonHoldType.Rifle,
		Grip = LargeLadWeaponGrip.RightHandedTwoHanded,
		DrawAnimation = "b_deploy",
		IdleAnimation = "idle",
		FireAnimation = "b_attack",
		ReloadAnimation = "b_reload",
		DryFireAnimation = "b_attack_dry",
		EmptyAnimation = "b_empty",
		FireSoundPackageIdent = "vidya/smg-shoot",
		ReloadSoundPackageIdent = "drakefruit/rifle_reload",
		EmptySoundPackageIdent = "hzgame/hzuiclickbuttontinyrattle",
		MuzzleAttachment = "muzzle",
		MuzzleEffectPrefabPath =
			"prefabs/effects/default_muzzleflash.prefab",
		MuzzleEffectScale = 0.25f,
		ImpactEffectPrefabPath = "prefabs/surface/default-bullet.prefab",
		ImpactEffectScale = 1.0f,
		PelletCount = 1,
		PelletSpreadDegrees = 0.0f
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

	public static int GetCatalogOrder( LargeLadWeaponId id )
	{
		for ( var index = 0; index < Firearms.Length; index++ )
		{
			if ( Firearms[index].Id == id )
				return index;
		}

		return -1;
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
			typeof( LargeLadFirearmArchetype ),
			definition.Archetype ) ||
			definition.Archetype == LargeLadFirearmArchetype.None )
		{
			warnings.Add( "Archetype must be a supported firearm behavior." );
		}

		RequirePositiveFinite( warnings, definition.Damage, "Damage" );
		RequirePositiveFinite(
			warnings,
			definition.FireInterval,
			"Fire interval" );
		RequirePositiveFinite( warnings, definition.Range, "Range" );

		if ( definition.MagazineSize <= 0 )
			warnings.Add( "Magazine size must be greater than zero." );

		if ( definition.StartingReserve < 0 )
			warnings.Add( "Starting reserve cannot be negative." );

		RequirePositiveFinite(
			warnings,
			definition.ReloadDuration,
			"Reload duration" );

		if ( !System.Enum.IsDefined(
			typeof( LargeLadCrosshairStyle ),
			definition.Crosshair ) ||
			definition.Crosshair == LargeLadCrosshairStyle.None )
		{
			warnings.Add( "Crosshair must be a supported visible style." );
		}

		RequireText(
			warnings,
			definition.FirstPersonModelPath,
			"First-person model path" );
		RequireText(
			warnings,
			definition.FirstPersonModelPackageIdent,
			"First-person model package" );
		if ( !definition.FirstPersonModelIncludesArms )
		{
			RequireText(
				warnings,
				definition.FirstPersonArmsModelPath,
				"First-person arms model path" );
			RequireText(
				warnings,
				definition.FirstPersonArmsPackageIdent,
				"First-person arms package" );
		}
		if ( !definition.FirstPersonUsesAnimGraph )
			warnings.Add( "First-person firearm must use its animation graph." );
		RequireText(
			warnings,
			definition.FirstPersonAnimationGraphPath,
			"First-person animation graph path" );
		RequireFinite(
			warnings,
			definition.FirstPersonPositionOffset,
			"First-person position offset" );
		RequireFinite(
			warnings,
			definition.FirstPersonRotationOffset,
			"First-person rotation offset" );
		RequirePositiveFinite(
			warnings,
			definition.FirstPersonModelScale,
			"First-person model scale" );

		RequireText(
			warnings,
			definition.ThirdPersonWorldModelPath,
			"Third-person world model path" );
		RequireText(
			warnings,
			definition.ThirdPersonWorldModelPackageIdent,
			"Third-person world model package" );
		RequireText(
			warnings,
			definition.ThirdPersonAttachmentBone,
			"Third-person attachment bone" );
		RequireFinite(
			warnings,
			definition.ThirdPersonPositionOffset,
			"Third-person position offset" );
		RequireFinite(
			warnings,
			definition.ThirdPersonRotationOffset,
			"Third-person rotation offset" );
		RequireFinite(
			warnings,
			definition.ThirdPersonGripOffset,
			"Third-person grip offset" );
		RequirePositiveFinite(
			warnings,
			definition.ThirdPersonModelScale,
			"Third-person model scale" );

		if ( !System.Enum.IsDefined(
			typeof( LargeLadThirdPersonHoldType ),
			definition.ThirdPersonHoldType ) ||
			definition.ThirdPersonHoldType ==
				LargeLadThirdPersonHoldType.None )
		{
			warnings.Add(
				"Third-person hold type must describe the Citizen pose." );
		}

		if ( !System.Enum.IsDefined(
			typeof( LargeLadWeaponGrip ),
			definition.Grip ) ||
			definition.Grip == LargeLadWeaponGrip.None )
		{
			warnings.Add( "Grip must describe how the firearm is held." );
		}

		RequireText( warnings, definition.DrawAnimation, "Draw animation" );
		RequireText( warnings, definition.IdleAnimation, "Idle animation" );
		RequireText( warnings, definition.FireAnimation, "Fire animation" );
		RequireText( warnings, definition.ReloadAnimation, "Reload animation" );
		RequireText( warnings, definition.DryFireAnimation, "Dry-fire animation" );
		RequireText( warnings, definition.EmptyAnimation, "Empty animation" );
		RequireSoundReference(
			warnings,
			definition.FireSoundPath,
			definition.FireSoundPackageIdent,
			"Fire sound" );
		RequireSoundReference(
			warnings,
			definition.ReloadSoundPath,
			definition.ReloadSoundPackageIdent,
			"Reload sound" );
		RequireSoundReference(
			warnings,
			definition.EmptySoundPath,
			definition.EmptySoundPackageIdent,
			"Empty sound" );
		RequireText( warnings, definition.MuzzleAttachment, "Muzzle attachment" );
		RequireText(
			warnings,
			definition.MuzzleEffectPrefabPath,
			"Muzzle effect prefab path" );
		RequirePositiveFinite(
			warnings,
			definition.MuzzleEffectScale,
			"Muzzle effect scale" );
		RequireText(
			warnings,
			definition.ImpactEffectPrefabPath,
			"Impact effect prefab path" );
		RequirePositiveFinite(
			warnings,
			definition.ImpactEffectScale,
			"Impact effect scale" );

		if ( definition.Archetype == LargeLadFirearmArchetype.Shotgun )
		{
			if ( definition.PelletCount < 2 )
				warnings.Add( "Shotgun archetypes need at least two pellets." );

			RequirePositiveFinite(
				warnings,
				definition.PelletSpreadDegrees,
				"Shotgun pellet spread" );
		}
		else
		{
			if ( definition.PelletCount != 1 )
				warnings.Add( "Non-shotgun archetypes must fire exactly one pellet." );

			if ( !float.IsFinite( definition.PelletSpreadDegrees ) ||
				definition.PelletSpreadDegrees != 0.0f )
			{
				warnings.Add( "Non-shotgun archetypes cannot define pellet spread." );
			}
		}

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

	private static void RequireSoundReference(
		List<string> warnings,
		string path,
		string packageIdent,
		string label )
	{
		if ( string.IsNullOrWhiteSpace( path ) &&
			string.IsNullOrWhiteSpace( packageIdent ) )
		{
			warnings.Add( $"{label} needs an asset path or package." );
		}
	}

	private static void RequirePositiveFinite(
		ICollection<string> warnings,
		float value,
		string name )
	{
		if ( !float.IsFinite( value ) || value <= 0.0f )
			warnings.Add( $"{name} must be finite and greater than zero." );
	}

	private static void RequireFinite(
		ICollection<string> warnings,
		Vector3 value,
		string name )
	{
		if ( !float.IsFinite( value.x ) ||
			!float.IsFinite( value.y ) ||
			!float.IsFinite( value.z ) )
		{
			warnings.Add( $"{name} must contain only finite values." );
		}
	}

	private static void RequireFinite(
		ICollection<string> warnings,
		Angles value,
		string name )
	{
		if ( !float.IsFinite( value.pitch ) ||
			!float.IsFinite( value.yaw ) ||
			!float.IsFinite( value.roll ) )
		{
			warnings.Add( $"{name} must contain only finite values." );
		}
	}
}
