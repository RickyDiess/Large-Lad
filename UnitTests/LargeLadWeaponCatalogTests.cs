using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

[TestClass]
public sealed class LargeLadWeaponCatalogLookupTests
{
	[TestMethod]
	public void Lookup_ResolvesEveryAuthoredFirearm()
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
	}

	[TestMethod]
	public void Lookup_InvalidIdUsesExplicitTryFailureAndSafeFallback()
	{
		var invalid = (LargeLadWeaponId)999;

		Assert.IsFalse(
			LargeLadWeaponCatalog.TryGetFirearm( invalid, out var definition ) );
		Assert.IsNull( definition );
		Assert.AreEqual(
			LargeLadWeaponId.None,
			LargeLadWeaponCatalog.Get( invalid ).Id );
	}

	[TestMethod]
	public void ExistingSerializedNamesKeepStableMetadataAndNativeRoutes()
	{
		Assert.IsTrue(
			System.Enum.TryParse<LargeLadWeaponId>(
				"Pistol",
				out var pistolId ) );
		Assert.IsTrue(
			System.Enum.TryParse<LargeLadWeaponId>(
				"Smg",
				out var smgId ) );

		var pistol = LargeLadWeaponCatalog.Get( pistolId );
		var smg = LargeLadWeaponCatalog.Get( smgId );
		Assert.AreEqual( "Pistol", pistol.DisplayName );
		Assert.AreEqual( "SMG", smg.DisplayName );
		Assert.AreEqual(
			"prefabs/gameplay/native_pistol.prefab",
			pistol.NativePrefabPath );
		Assert.AreEqual(
			"prefabs/gameplay/native_smg.prefab",
			smg.NativePrefabPath );
	}
}

[TestClass]
public sealed class LargeLadWeaponDefinitionMetadataTests
{
	[TestMethod]
	public void CataloguedFirearms_HaveCompleteMetadata()
	{
		var firearmIdCount = System.Enum
			.GetValues<LargeLadWeaponId>()
			.Count( id =>
				id is not (LargeLadWeaponId.None or LargeLadWeaponId.Melee) );
		Assert.AreEqual(
			firearmIdCount,
			LargeLadWeaponCatalog.FirearmDefinitions.Count );
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
		}
	}

	[TestMethod]
	public void IncompleteDefinition_ReportsOnlyMissingMetadata()
	{
		var definition = new LargeLadWeaponDefinition
		{
			Id = LargeLadWeaponId.Pistol
		};

		var warnings = definition.GetValidationWarnings();

		Assert.IsTrue(
			warnings.Any( warning => warning.Contains( "Display name" ) ) );
		Assert.IsTrue(
			warnings.Any( warning => warning.Contains( "Crosshair" ) ) );
		Assert.IsTrue(
			warnings.Any( warning => warning.Contains( "Native prefab" ) ) );
		Assert.AreEqual( 3, warnings.Count );
	}

	[TestMethod]
	public void InvalidDefinitionId_ProducesAnIdentityWarning()
	{
		var definition = new LargeLadWeaponDefinition
		{
			Id = (LargeLadWeaponId)999,
			DisplayName = "Invalid",
			Crosshair = LargeLadCrosshairStyle.FourSegment,
			NativePrefabPath = "prefabs/gameplay/invalid.prefab"
		};

		Assert.IsTrue(
			definition.GetValidationWarnings().Any(
				warning => warning.Contains( "firearm weapon id" ) ) );
	}

	[TestMethod]
	public void Definition_ContainsOnlyLargeLadMetadataAndPrefabRouting()
	{
		var propertyNames = typeof( LargeLadWeaponDefinition )
			.GetProperties()
			.Select( property => property.Name )
			.ToArray();

		CollectionAssert.AreEquivalent(
			new[]
			{
				"Id",
				"DisplayName",
				"Crosshair",
				"AccentColor",
				"NativePrefabPath"
			},
			propertyNames );
	}

	[TestMethod]
	public void PickupOwnershipPolicy_IsPerMapperAuthoredInstance()
	{
		Assert.IsNull(
			typeof( LargeLadWeaponDefinition ).GetProperty( "PickupPolicy" ) );
		Assert.IsNotNull(
			typeof( LargeLadWeaponPickup ).GetProperty( "PickupPolicy" ) );
	}
}

[TestClass]
public sealed class LargeLadRemainingPresentationDefinitionTests
{
	[TestMethod]
	public void DodgeballPresentation_RemainsASeparateHumanArmsUtility()
	{
		Assert.IsTrue( LargeLadUtilityPresentationCatalog.TryGet(
			LargeLadUtilityId.Dodgeball,
			out var dodgeball ) );
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
			Vector3.Zero,
			dodgeball.ThirdPersonModelPosition );
	}
}
