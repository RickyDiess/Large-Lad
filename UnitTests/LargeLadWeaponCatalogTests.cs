using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

[TestClass]
public sealed class LargeLadWeaponCatalogLookupTests
{
	[TestMethod]
	public void Lookup_ResolvesEveryExistingAuthoredFirearm()
	{
		Assert.IsTrue(
			LargeLadWeaponCatalog.TryGetFirearm(
				LargeLadWeaponId.Pistol,
				out var pistol ) );
		Assert.AreEqual( LargeLadWeaponId.Pistol, pistol.Id );
		Assert.AreSame(
			pistol,
			LargeLadWeaponCatalog.Get( LargeLadWeaponId.Pistol ) );

		Assert.IsTrue(
			LargeLadWeaponCatalog.TryGetFirearm(
				LargeLadWeaponId.Smg,
				out var smg ) );
		Assert.AreEqual( LargeLadWeaponId.Smg, smg.Id );
		Assert.AreSame(
			smg,
			LargeLadWeaponCatalog.Get( LargeLadWeaponId.Smg ) );
		Assert.AreEqual( 0, LargeLadWeaponCatalog.GetCatalogOrder( pistol.Id ) );
		Assert.AreEqual( 1, LargeLadWeaponCatalog.GetCatalogOrder( smg.Id ) );
	}

	[TestMethod]
	public void Lookup_InvalidIdUsesExplicitTryFailureAndSafeLegacyFallback()
	{
		var invalid = (LargeLadWeaponId)999;

		Assert.IsFalse(
			LargeLadWeaponCatalog.TryGetFirearm( invalid, out var definition ) );
		Assert.IsNull( definition );
		Assert.AreEqual(
			LargeLadWeaponId.None,
			LargeLadWeaponCatalog.Get( invalid ).Id );
		Assert.AreEqual( -1, LargeLadWeaponCatalog.GetCatalogOrder( invalid ) );
	}

	[TestMethod]
	public void ExistingSerializedWeaponNamesStillParseToCatalogDefinitions()
	{
		Assert.IsTrue(
			System.Enum.TryParse<LargeLadWeaponId>(
				"Pistol",
				out var pistolId ) );
		Assert.IsTrue(
			System.Enum.TryParse<LargeLadWeaponId>(
				"Smg",
				out var smgId ) );
		Assert.AreEqual( "Pistol", LargeLadWeaponCatalog.Get( pistolId ).DisplayName );
		Assert.AreEqual( "SMG", LargeLadWeaponCatalog.Get( smgId ).DisplayName );
	}
}

[TestClass]
public sealed class LargeLadWeaponDefinitionValidationTests
{
	[TestMethod]
	public void CataloguedFirearms_AreComplete()
	{
		Assert.AreEqual( 2, LargeLadWeaponCatalog.FirearmDefinitions.Count );
		Assert.AreEqual(
			0,
			LargeLadWeaponCatalog.GetCatalogValidationWarnings().Count,
			string.Join(
				System.Environment.NewLine,
				LargeLadWeaponCatalog.GetCatalogValidationWarnings() ) );

		foreach ( var definition in
			LargeLadWeaponCatalog.FirearmDefinitions )
		{
			Assert.AreEqual(
				0,
				definition.GetValidationWarnings().Count,
				$"{definition.Id}: " +
				string.Join( "; ", definition.GetValidationWarnings() ) );
			Assert.AreEqual(
				definition.ThirdPersonWorldModelPath,
				definition.WorldModelPath,
				"The compatibility world-model accessor must resolve the canonical field." );
		}
	}

	[TestMethod]
	public void IncompleteDefinition_ProducesUsefulFieldWarnings()
	{
		var definition = new LargeLadWeaponDefinition
		{
			Id = LargeLadWeaponId.Pistol,
			Archetype = LargeLadFirearmArchetype.SemiAutomatic
		};

		var warnings = definition.GetValidationWarnings();

		Assert.IsTrue(
			warnings.Any( warning => warning.Contains( "Display name" ) ) );
		Assert.IsTrue(
			warnings.Any( warning => warning.Contains( "Damage" ) ) );
		Assert.IsTrue(
			warnings.Any( warning => warning.Contains( "First-person model" ) ) );
		Assert.IsTrue(
			warnings.Any( warning => warning.Contains( "Fire sound" ) ) );
		Assert.IsTrue(
			warnings.Any( warning => warning.Contains( "Muzzle attachment" ) ) );
	}

	[TestMethod]
	public void InvalidDefinitionId_ProducesAnIdentityWarning()
	{
		var definition = new LargeLadWeaponDefinition
		{
			Id = (LargeLadWeaponId)999,
			Archetype = LargeLadFirearmArchetype.Automatic,
			PelletCount = 1
		};

		Assert.IsTrue(
			definition.GetValidationWarnings().Any(
				warning => warning.Contains( "firearm weapon id" ) ) );
	}

	[TestMethod]
	public void PickupOwnershipPolicy_IsNotPartOfGlobalDefinition()
	{
		Assert.IsNull(
			typeof( LargeLadWeaponDefinition ).GetProperty( "PickupPolicy" ) );
		Assert.IsNotNull(
			typeof( LargeLadWeaponPickup ).GetProperty( "PickupPolicy" ) );
	}

	[TestMethod]
	public void ShotgunArchetype_RequiresMultipleSpreadPellets()
	{
		var invalid = CreateCompleteDefinition(
			LargeLadFirearmArchetype.Shotgun,
			pelletCount: 1,
			pelletSpreadDegrees: 0.0f );
		var warnings = invalid.GetValidationWarnings();

		Assert.IsTrue(
			warnings.Any( warning => warning.Contains( "at least two pellets" ) ) );
		Assert.IsTrue(
			warnings.Any( warning => warning.Contains( "pellet spread" ) ) );

		var valid = CreateCompleteDefinition(
			LargeLadFirearmArchetype.Shotgun,
			pelletCount: 8,
			pelletSpreadDegrees: 4.5f );
		Assert.AreEqual(
			0,
			valid.GetValidationWarnings().Count,
			string.Join( "; ", valid.GetValidationWarnings() ) );
	}

	[TestMethod]
	public void NonShotgunArchetype_RejectsShotgunOnlyValues()
	{
		var definition = CreateCompleteDefinition(
			LargeLadFirearmArchetype.Automatic,
			pelletCount: 6,
			pelletSpreadDegrees: 3.0f );
		var warnings = definition.GetValidationWarnings();

		Assert.IsTrue(
			warnings.Any( warning => warning.Contains( "exactly one pellet" ) ) );
		Assert.IsTrue(
			warnings.Any( warning => warning.Contains( "cannot define pellet spread" ) ) );
	}

	private static LargeLadWeaponDefinition CreateCompleteDefinition(
		LargeLadFirearmArchetype archetype,
		int pelletCount,
		float pelletSpreadDegrees )
	{
		return new LargeLadWeaponDefinition
		{
			Id = LargeLadWeaponId.Pistol,
			DisplayName = "Validation Weapon",
			Archetype = archetype,
			Damage = 10.0f,
			FireInterval = 0.2f,
			Range = 800.0f,
			MagazineSize = 6,
			StartingReserve = 12,
			ReloadDuration = 1.0f,
			Crosshair = LargeLadCrosshairStyle.FourSegment,
			PickupColor = Color.White,
			FirstPersonModelPath = "models/test/view.vmdl",
			FirstPersonModelPackageIdent = "test/view",
			FirstPersonArmsModelPath = "models/test/arms.vmdl",
			FirstPersonArmsPackageIdent = "test/arms",
			FirstPersonModelIncludesArms = false,
			FirstPersonPositionOffset = Vector3.Zero,
			FirstPersonRotationOffset = Angles.Zero,
			FirstPersonModelScale = 1.0f,
			FirstPersonUsesAnimGraph = true,
			FirstPersonAnimationGraphPath = "models/test/view.vanmgrph",
			FirstPersonSkeleton = 1,
			ThirdPersonWorldModelPath = "models/test/world.vmdl",
			ThirdPersonWorldModelPackageIdent = "test/world",
			ThirdPersonAttachmentBone = "hold_R",
			ThirdPersonPositionOffset = Vector3.Zero,
			ThirdPersonRotationOffset = Angles.Zero,
			ThirdPersonGripOffset = Vector3.Zero,
			ThirdPersonModelScale = 1.0f,
			ThirdPersonHoldType = LargeLadThirdPersonHoldType.Rifle,
			Grip = LargeLadWeaponGrip.RightHandedTwoHanded,
			DrawAnimation = "draw",
			IdleAnimation = "idle",
			FireAnimation = "fire",
			ReloadAnimation = "reload",
			DryFireAnimation = "dry",
			EmptyAnimation = "empty",
			FireSoundPath = "sounds/test/fire.sound",
			ReloadSoundPath = "sounds/test/reload.sound",
			EmptySoundPath = "sounds/test/empty.sound",
			DrawSoundPath = "sounds/test/draw.sound",
			MuzzleAttachment = "muzzle",
			MuzzleEffectPrefabPath = "prefabs/test/muzzle.prefab",
			ImpactEffectPrefabPath = "prefabs/test/impact.prefab",
			ImpactEffectScale = 1.0f,
			PelletCount = pelletCount,
			PelletSpreadDegrees = pelletSpreadDegrees
		};
	}
}

[TestClass]
public sealed class LargeLadExistingFirearmCompatibilityTests
{
	[TestMethod]
	public void Pistol_PreservesBehaviorInventoryAndAmmunitionValues()
	{
		var definition = LargeLadWeaponCatalog.Get( LargeLadWeaponId.Pistol );

		Assert.AreEqual( LargeLadFirearmArchetype.SemiAutomatic, definition.Archetype );
		Assert.AreEqual( 100.0f, definition.Damage );
		Assert.AreEqual( 0.35f, definition.FireInterval );
		Assert.AreEqual( 1200.0f, definition.Range );
		Assert.AreEqual( 8, definition.MagazineSize );
		Assert.AreEqual( 32, definition.StartingReserve );
		Assert.AreEqual( 1.4f, definition.ReloadDuration );

		var core = LargeLadWeaponState.CreateCore( LargeLadWeaponId.Pistol );
		Assert.AreEqual( 8, core.Magazine );
		Assert.AreEqual( 0, core.Reserve );
		Assert.AreEqual(
			LargeLadAmmunitionMode.InfiniteReserve,
			core.AmmunitionMode );
	}

	[TestMethod]
	public void Smg_PreservesBehaviorInventoryAndAmmunitionValues()
	{
		var definition = LargeLadWeaponCatalog.Get( LargeLadWeaponId.Smg );

		Assert.AreEqual( LargeLadFirearmArchetype.Automatic, definition.Archetype );
		Assert.AreEqual( 25.0f, definition.Damage );
		Assert.AreEqual( 0.09f, definition.FireInterval );
		Assert.AreEqual( 1000.0f, definition.Range );
		Assert.AreEqual( 30, definition.MagazineSize );
		Assert.AreEqual( 90, definition.StartingReserve );
		Assert.AreEqual( 2.0f, definition.ReloadDuration );

		var core = LargeLadWeaponState.CreateCore( LargeLadWeaponId.Smg );
		Assert.AreEqual( 30, core.Magazine );
		Assert.AreEqual( 0, core.Reserve );
		Assert.AreEqual(
			LargeLadAmmunitionMode.InfiniteReserve,
			core.AmmunitionMode );
	}

	[TestMethod]
	public void ExistingFirearms_KeepFiniteExclusiveStartingAmmunition()
	{
		foreach ( var definition in
			LargeLadWeaponCatalog.FirearmDefinitions )
		{
			var state = LargeLadWeaponState.CreateExclusive(
				definition.Id,
				instanceId: 17,
				definition.MagazineSize,
				definition.StartingReserve );

			Assert.AreEqual( definition.MagazineSize, state.Magazine );
			Assert.AreEqual( definition.StartingReserve, state.Reserve );
			Assert.AreEqual(
				LargeLadAmmunitionMode.FiniteReserve,
				state.AmmunitionMode );
			Assert.AreEqual( 17, state.ExclusiveInstanceId );
		}
	}
}

[TestClass]
public sealed class LargeLadWeaponPresentationDefinitionTests
{
	[TestMethod]
	public void PurposeBuiltArmsAndSkinnyKidMeleeModels_AreCatalogDriven()
	{
		var melee = LargeLadWeaponCatalog.Get( LargeLadWeaponId.Melee );
		var pistol = LargeLadWeaponCatalog.Get( LargeLadWeaponId.Pistol );
		var smg = LargeLadWeaponCatalog.Get( LargeLadWeaponId.Smg );

		Assert.AreEqual(
			"models/weapons/sbox_melee_crowbar/v_crowbar.vmdl",
			melee.FirstPersonModelPath );
		Assert.AreEqual(
			"models/weapons/sbox_pistol_usp/v_usp.vmdl",
			pistol.FirstPersonModelPath );
		Assert.AreEqual(
			"models/weapons/sbox_smg_mp5/v_mp5.vmdl",
			smg.FirstPersonModelPath );
		Assert.AreEqual(
			"models/citizen_props/crowbar01.vmdl",
			melee.ThirdPersonWorldModelPath );
		Assert.AreEqual( "hold_R", melee.ThirdPersonAttachmentBone );
		Assert.AreEqual( 0.25f, melee.ThirdPersonModelScale );
		Assert.AreEqual(
			new Vector3( 0.0f, 0.0f, -32.0f ),
			melee.ThirdPersonGripOffset );
		Assert.AreEqual( Vector3.Zero, pistol.ThirdPersonGripOffset );
		Assert.AreEqual( Vector3.Zero, smg.ThirdPersonGripOffset );
		Assert.IsFalse( melee.FirstPersonModelIncludesArms );
		Assert.IsFalse( pistol.FirstPersonModelIncludesArms );
		Assert.IsFalse( smg.FirstPersonModelIncludesArms );
		Assert.AreEqual(
			melee.FirstPersonArmsModelPath,
			smg.FirstPersonArmsModelPath );
		Assert.IsFalse(
			string.IsNullOrWhiteSpace( smg.FirstPersonArmsModelPath ) );
		Assert.AreEqual(
			"models/first_person/v_first_person_arms_human.vmdl",
			melee.FirstPersonArmsModelPath );
		Assert.AreEqual(
			"facepunch/v_crowbar",
			melee.FirstPersonModelPackageIdent );
		Assert.IsTrue( melee.FirstPersonUsesAnimGraph );
		Assert.AreEqual(
			"models/weapons/sbox_melee_crowbar/v_crowbar.vanmgrph",
			melee.FirstPersonAnimationGraphPath );
		Assert.AreEqual(
			"facepunch/v_first_person_arms_human",
			melee.FirstPersonArmsPackageIdent );
		Assert.AreEqual(
			melee.FirstPersonArmsPackageIdent,
			smg.FirstPersonArmsPackageIdent );
		Assert.AreEqual( "facepunch/v_usp", pistol.FirstPersonModelPackageIdent );
		Assert.AreEqual( "facepunch/v_mp5", smg.FirstPersonModelPackageIdent );
		Assert.AreEqual( 0, melee.FirstPersonSkeleton );
		Assert.AreEqual( 0, pistol.FirstPersonSkeleton );
		Assert.AreEqual( 0, smg.FirstPersonSkeleton );
		Assert.IsTrue( melee.FirstPersonTwoHanded );
		Assert.IsFalse( pistol.FirstPersonTwoHanded );
		Assert.IsTrue( smg.FirstPersonTwoHanded );
		Assert.AreEqual(
			LargeLadWeaponGrip.RightHandedOneHanded,
			melee.Grip );
		Assert.AreEqual(
			LargeLadThirdPersonHoldType.HoldItem,
			melee.ThirdPersonHoldType );
		Assert.AreEqual(
			LargeLadThirdPersonHoldType.Pistol,
			pistol.ThirdPersonHoldType );
		Assert.AreEqual(
			LargeLadThirdPersonHoldType.Rifle,
			smg.ThirdPersonHoldType );
		Assert.IsTrue( pistol.FirstPersonUsesAnimGraph );
		Assert.IsTrue( smg.FirstPersonUsesAnimGraph );
		Assert.AreEqual( "vidya/pistol-shoot", pistol.FireSoundPackageIdent );
		Assert.AreEqual( "vidya/smg-shoot", smg.FireSoundPackageIdent );
		Assert.AreEqual(
			"prefabs/effects/default_muzzleflash.prefab",
			pistol.MuzzleEffectPrefabPath );
		Assert.AreEqual(
			pistol.MuzzleEffectPrefabPath,
			smg.MuzzleEffectPrefabPath );
		Assert.AreEqual( 0.25f, pistol.MuzzleEffectScale );
		Assert.AreEqual( 0.25f, smg.MuzzleEffectScale );
	}

	[TestMethod]
	public void DodgeballPresentation_RemainsASeparateHumanArmsUtility()
	{
		Assert.IsTrue( LargeLadUtilityPresentationCatalog.TryGet(
			LargeLadUtilityId.Dodgeball,
			out var dodgeball ) );
		Assert.AreEqual(
			"models/first_person/v_first_person_arms_human.vmdl",
			dodgeball.FirstPersonArmsModelPath );
		Assert.AreEqual(
			"facepunch/v_first_person_arms_human",
			dodgeball.FirstPersonArmsPackageIdent );
		Assert.AreEqual( 0, dodgeball.FirstPersonSkeleton );
		Assert.IsFalse( dodgeball.FirstPersonTwoHanded );
		Assert.AreEqual(
			"models/dev/sphere.vmdl",
			dodgeball.FirstPersonHeldModelPath );
		Assert.AreEqual(
			"hand_R",
			dodgeball.FirstPersonHeldAttachmentBone );
		Assert.AreEqual(
			"models/dev/sphere.vmdl",
			dodgeball.ThirdPersonWorldModelPath );
		Assert.AreEqual(
			"hold_R",
			dodgeball.ThirdPersonAttachmentBone );
		Assert.AreEqual(
			LargeLadThirdPersonHoldType.HoldItem,
			dodgeball.ThirdPersonHoldType );
		Assert.AreEqual(
			LargeLadWeaponGrip.RightHandedOneHanded,
			dodgeball.ThirdPersonGrip );
		Assert.AreEqual(
			Vector3.Zero,
			dodgeball.ThirdPersonPositionOffset );
	}
}
