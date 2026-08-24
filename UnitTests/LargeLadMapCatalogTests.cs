using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

[TestClass]
public sealed class LargeLadMapCatalogTests
{
	private const string LocalMapIdentifier = "scenes/valid_map.scene";

	[TestMethod]
	public void CurrentContractVersion_IsAccepted()
	{
		var manifest = CreateValidLocalManifest();

		Assert.IsTrue( TryResolveCommunity(
			manifest,
			out var descriptor,
			out var issues ) );
		Assert.AreEqual( 0, issues.Count );
		Assert.AreEqual(
			LargeLadMapContract.CurrentVersion,
			descriptor.ContractVersion );
	}

	[TestMethod]
	public void LocalManifestValidation_DoesNotRequirePublishedPackageIdent()
	{
		var manifest = CreateValidLocalManifest();
		manifest.PublishedPackageIdent = "";

		var issues =
			LargeLadMapCatalog.GetLocalManifestValidationIssues( manifest );

		Assert.AreEqual( 0, issues.Count );
	}

	[TestMethod]
	public void LocalManifestValidation_UsesRuntimeIdentityAndContractRules()
	{
		var manifest = CreateValidLocalManifest();
		manifest.StableMapId = "Bad Map Id";
		manifest.ContractVersion = LargeLadMapContract.CurrentVersion + 1;

		var issues =
			LargeLadMapCatalog.GetLocalManifestValidationIssues( manifest );

		Assert.IsTrue( issues.Any( issue =>
			issue.Contains( "stable lowercase map id" ) ) );
		Assert.IsTrue( issues.Any( issue =>
			issue.Contains( "map-contract version" ) &&
			issue.Contains( LargeLadMapContract.CurrentVersion.ToString() ) ) );
	}

	[TestMethod]
	public void UnsupportedContractVersion_IsRejectedWithBothVersions()
	{
		var manifest = CreateValidLocalManifest();
		manifest.ContractVersion = LargeLadMapContract.CurrentVersion + 1;

		Assert.IsFalse( TryResolveCommunity(
			manifest,
			out _,
			out var issues ) );
		var issue = issues.Single( candidate =>
			candidate.Contains( "map-contract version" ) );
		StringAssert.Contains(
			issue,
			manifest.ContractVersion.ToString() );
		StringAssert.Contains(
			issue,
			LargeLadMapContract.CurrentVersion.ToString() );
		StringAssert.Contains( issue, "Update the manifest" );
	}

	[TestMethod]
	public void MissingIdentityAndPresentationMetadata_AreRejectedClearly()
	{
		var manifest = CreateValidLocalManifest();
		manifest.StableMapId = "";
		manifest.DisplayName = "";
		manifest.MapperCredit = "";
		manifest.PresentationAsset = "";

		Assert.IsFalse( TryResolveCommunity(
			manifest,
			out _,
			out var issues ) );
		Assert.IsTrue( issues.Any( issue => issue.Contains( "stable lowercase map id" ) ) );
		Assert.IsTrue( issues.Any( issue => issue.Contains( "display name" ) ) );
		Assert.IsTrue( issues.Any( issue => issue.Contains( "mapper/author credit" ) ) );
		Assert.IsTrue( issues.Any( issue => issue.Contains( "presentation asset" ) ) );
	}

	[TestMethod]
	public void MissingManifest_IsRejectedClearly()
	{
		Assert.IsFalse( LargeLadMapCatalog.TryResolveCommunity(
			manifest: null,
			mapInstanceIdentifier: "org.community_map",
			packageMetadata: null,
			out _,
			out var issues ) );
		Assert.IsTrue( issues.Any( issue =>
			issue.Contains( "missing its required Large Lad map manifest" ) ) );
	}

	[TestMethod]
	public void MapSceneCannotBeUsedAsItsOwnThumbnail()
	{
		var manifest = CreateValidLocalManifest();
		manifest.PresentationAsset = LocalMapIdentifier;

		Assert.IsFalse( TryResolveCommunity(
			manifest,
			out _,
			out var issues ) );
		Assert.IsTrue( issues.Any( issue =>
			issue.Contains( "Select a texture or image" ) &&
			issue.Contains( "recursive content dependency" ) ) );
	}

	[DataTestMethod]
	[DataRow( "docs/map_thumbnail.txt" )]
	[DataRow( "../outside/map_thumbnail.png" )]
	[DataRow( "C:\\outside\\map_thumbnail.png" )]
	[DataRow( "https://example.com/map_thumbnail.png" )]
	public void LocalThumbnailMustUseAProjectRelativeTextureOrImagePath(
		string presentationAsset )
	{
		var manifest = CreateValidLocalManifest();
		manifest.PresentationAsset = presentationAsset;

		var issues =
			LargeLadMapCatalog.GetLocalManifestValidationIssues( manifest );

		Assert.IsTrue( issues.Any( issue =>
			issue.Contains( "project-relative texture or image path" ) &&
			issue.Contains( presentationAsset ) ) );
	}

	[TestMethod]
	public void OfficialStatus_ComesOnlyFromFirstPartyCatalogMembership()
	{
		var manifest = CreateValidLocalManifest();

		Assert.IsTrue( TryResolveCommunity(
			manifest,
			out var community,
			out _ ) );
		Assert.IsFalse( community.IsOfficiallyCurated );

		var catalog = new LargeLadOfficialMapCatalog
		{
			Entries =
			[
				new LargeLadOfficialMapEntry
				{
					MapInstanceIdentifier = LocalMapIdentifier,
					Manifest = manifest
				}
			]
		};

		Assert.IsTrue( LargeLadMapCatalog.TryResolveOfficial(
			catalog,
			manifest.StableMapId,
			packageMetadata: null,
			out var official,
			out _ ) );
		Assert.IsTrue( official.IsOfficiallyCurated );
	}

	[TestMethod]
	public void NoMapOverrides_UsesEveryGameDefault()
	{
		var manifest = CreateValidLocalManifest();

		Assert.IsTrue( TryResolveCommunity(
			manifest,
			out var descriptor,
			out _ ) );
		Assert.AreEqual(
			600.0f,
			descriptor.Balance.ResolveSurvivalDuration( 600.0f ) );
		Assert.AreEqual(
			1.0f,
			descriptor.Balance
				.ResolveSkinnyProgressionBarricadeMultiplier() );
		Assert.AreEqual(
			1.0f,
			descriptor.Balance.ResolveLargeLadMaximumHealthMultiplier() );
		Assert.AreEqual(
			1.0f,
			descriptor.Balance.ResolveHunterEscalationMultiplier() );
	}

	[TestMethod]
	public void ApprovedOverrides_ComposeWithExistingBalanceLayers()
	{
		var manifest = CreateValidLocalManifest();
		manifest.BalanceOverrides = new LargeLadMapBalanceOverrides
		{
			SurvivalDurationSeconds = 480.0f,
			SkinnyProgressionBarricadeMaximumHealthMultiplier = 1.2f,
			LargeLadMaximumHealthMultiplier = 0.9f,
			HunterEscalationMultiplier = 1.1f
		};

		Assert.IsTrue( TryResolveCommunity(
			manifest,
			out var descriptor,
			out _ ) );
		Assert.AreEqual(
			480.0f,
			descriptor.Balance.ResolveSurvivalDuration( 600.0f ) );
		Assert.AreEqual(
			1.56f,
			LargeLadRoundBalanceRules.ComposeHealthMultipliers(
				bandMultiplier: 1.3f,
				descriptor.Balance
					.ResolveSkinnyProgressionBarricadeMultiplier() ),
			0.0001f );
		Assert.AreEqual(
			1.17f,
			LargeLadRoundBalanceRules.ComposeHealthMultipliers(
				bandMultiplier: 1.3f,
				descriptor.Balance
					.ResolveLargeLadMaximumHealthMultiplier() ),
			0.0001f );
		Assert.AreEqual(
			1.265f,
			LargeLadMapCatalog.ComposeHunterMaximumMultiplier(
				gameMaximumMultiplier: 1.15f,
				descriptor.Balance ),
			0.0001f );
	}

	[TestMethod]
	public void RepeatedResolution_NeverCompoundsPreviousMapValues()
	{
		var manifest = CreateValidLocalManifest();
		manifest.BalanceOverrides.LargeLadMaximumHealthMultiplier = 0.9f;

		Assert.IsTrue( TryResolveCommunity(
			manifest,
			out var first,
			out _ ) );
		Assert.IsTrue( TryResolveCommunity(
			manifest,
			out var second,
			out _ ) );

		var firstResult = LargeLadRoundBalanceRules.ComposeHealthMultipliers(
			1.3f,
			first.Balance.ResolveLargeLadMaximumHealthMultiplier() );
		var secondResult = LargeLadRoundBalanceRules.ComposeHealthMultipliers(
			1.3f,
			second.Balance.ResolveLargeLadMaximumHealthMultiplier() );

		Assert.AreEqual( 1.17f, firstResult, 0.0001f );
		Assert.AreEqual( firstResult, secondResult, 0.0001f );
	}

	[TestMethod]
	public void RecommendedPlayerRange_UsesTheFull32PlayerContract()
	{
		var manifest = CreateValidLocalManifest();
		manifest.RecommendedMinimumPlayers = 2;
		manifest.RecommendedMaximumPlayers = 32;

		Assert.IsTrue( TryResolveCommunity(
			manifest,
			out var descriptor,
			out _ ) );
		Assert.AreEqual( 32, descriptor.RecommendedMaximumPlayers );
	}

	[DataTestMethod]
	[DataRow( 2, 33 )]
	[DataRow( 10, 5 )]
	[DataRow( 1, 32 )]
	public void MalformedRecommendedPlayerRanges_AreRejected(
		int minimum,
		int maximum )
	{
		var manifest = CreateValidLocalManifest();
		manifest.RecommendedMinimumPlayers = minimum;
		manifest.RecommendedMaximumPlayers = maximum;

		Assert.IsFalse( TryResolveCommunity(
			manifest,
			out _,
			out var issues ) );
		Assert.IsTrue( issues.Any( issue =>
			issue.Contains( "recommended player range" ) &&
			issue.Contains( "2-32 player contract" ) ) );
	}

	[TestMethod]
	public void InvalidApprovedMultipliers_ProduceUsefulFailures()
	{
		var manifest = CreateValidLocalManifest();
		manifest.BalanceOverrides = new LargeLadMapBalanceOverrides
		{
			SkinnyProgressionBarricadeMaximumHealthMultiplier = -1.0f,
			LargeLadMaximumHealthMultiplier = float.NaN,
			HunterEscalationMultiplier = 0.5f
		};

		Assert.IsFalse( TryResolveCommunity(
			manifest,
			out _,
			out var issues ) );
		Assert.IsTrue( issues.Any( issue =>
			issue.Contains( "Skinny Progression barricade" ) ) );
		Assert.IsTrue( issues.Any( issue =>
			issue.Contains( "Large Lad maximum-health" ) ) );
		Assert.IsTrue( issues.Any( issue =>
			issue.Contains( "Hunter escalation" ) ) );
	}

	[TestMethod]
	public void OfficialAndCommunityEntries_UseTheSameDescriptorShape()
	{
		var manifest = CreateValidLocalManifest();
		var catalog = new LargeLadOfficialMapCatalog
		{
			Entries =
			[
				new LargeLadOfficialMapEntry
				{
					MapInstanceIdentifier = LocalMapIdentifier,
					Manifest = manifest
				}
			]
		};

		Assert.IsTrue( TryResolveCommunity(
			manifest,
			out var community,
			out _ ) );
		Assert.IsTrue( LargeLadMapCatalog.TryResolveOfficial(
			catalog,
			manifest.StableMapId,
			packageMetadata: null,
			out var official,
			out _ ) );

		Assert.AreEqual( community.GetType(), official.GetType() );
		Assert.AreEqual( community.StableMapId, official.StableMapId );
		Assert.AreEqual( community.DisplayName, official.DisplayName );
		Assert.AreEqual(
			community.MapInstanceIdentifier,
			official.MapInstanceIdentifier );
		Assert.AreEqual(
			community.Balance.ResolveSurvivalDuration( 600.0f ),
			official.Balance.ResolveSurvivalDuration( 600.0f ) );
	}

	[TestMethod]
	public void PublishedPackageMetadata_IsNormalizedWithoutDuplicatingAuthority()
	{
		var manifest = CreateValidPublishedManifest();
		manifest.Backstory = "Local fallback description";
		var packageMetadata = new LargeLadMapPackageMetadata
		{
			PackageIdent = manifest.PublishedPackageIdent,
			DisplayName = "Published Community Title",
			PublisherCredit = "Community Organization",
			PresentationAsset = "https://cdn.example/map-wide.jpg",
			Description = "Published package summary"
		};

		Assert.IsTrue( LargeLadMapCatalog.TryResolveCommunity(
			manifest,
			manifest.PublishedPackageIdent,
			packageMetadata,
			out var descriptor,
			out _ ) );
		Assert.AreEqual( "Published Community Title", descriptor.DisplayName );
		Assert.AreEqual(
			"https://cdn.example/map-wide.jpg",
			descriptor.PresentationAsset );
		Assert.AreEqual( "Published package summary", descriptor.Description );
	}

	private static bool TryResolveCommunity(
		LargeLadMapManifest manifest,
		out LargeLadMapDescriptor descriptor,
		out System.Collections.Generic.IReadOnlyList<string> issues )
	{
		return LargeLadMapCatalog.TryResolveCommunity(
			manifest,
			LocalMapIdentifier,
			packageMetadata: null,
			out descriptor,
			out issues );
	}

	private static LargeLadMapManifest CreateValidLocalManifest()
	{
		return new LargeLadMapManifest
		{
			StableMapId = "test.valid_map",
			ContractVersion = LargeLadMapContract.CurrentVersion,
			DisplayName = "Valid Map",
			MapperCredit = "Test Mapper",
			PresentationAsset = "textures/valid_map_thumbnail.vtex",
			RecommendedMinimumPlayers = 2,
			RecommendedMaximumPlayers = 32,
			BalanceOverrides = new LargeLadMapBalanceOverrides()
		};
	}

	private static LargeLadMapManifest CreateValidPublishedManifest()
	{
		return new LargeLadMapManifest
		{
			StableMapId = "community.published_map",
			ContractVersion = LargeLadMapContract.CurrentVersion,
			PublishedPackageIdent = "community.published_map",
			DisplayName = "Fallback Title",
			MapperCredit = "Community Mapper",
			PresentationAsset = "textures/fallback.png",
			RecommendedMinimumPlayers = 2,
			RecommendedMaximumPlayers = 32,
			BalanceOverrides = new LargeLadMapBalanceOverrides()
		};
	}
}
