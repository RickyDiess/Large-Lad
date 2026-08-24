using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public sealed class LargeLadMapValidationRulesTests
{
	[TestMethod]
	public void IssueSeverity_PreservesBlockingAndAdvisoryMeaning()
	{
		var blocking = new LargeLadMapIssue(
			LargeLadMapIssueSeverity.BlockingError,
			"Lobby spawns",
			"missing" );
		var warning = new LargeLadMapIssue(
			LargeLadMapIssueSeverity.Warning,
			"Weapon Pickup 'Pistol'",
			"missing renderer" );

		Assert.IsTrue( blocking.IsBlocking );
		Assert.IsFalse( warning.IsBlocking );
		StringAssert.Contains( blocking.ToString(), "Lobby spawns" );
		StringAssert.Contains( warning.ToString(), "Pistol" );
	}

	[TestMethod]
	public void SpawnCapacity_NoArea_IsDistinctFromCapacityShortfall()
	{
		var missing = LargeLadSpawnRules.EvaluateGroupCapacity(
			LargeLadSpawnGroup.Lobby,
			LargeLadGameManager.TargetPlayerCount,
			[] );
		var shortfall = LargeLadSpawnRules.EvaluateGroupCapacity(
			LargeLadSpawnGroup.Lobby,
			LargeLadGameManager.TargetPlayerCount,
			[new LargeLadSpawnAreaCapacity( "Small Lobby", 16, 16 )] );

		Assert.AreEqual(
			LargeLadSpawnCapacityFailure.MissingArea,
			missing.Failure );
		Assert.AreEqual(
			LargeLadSpawnCapacityFailure.ConfiguredCapacityShortfall,
			shortfall.Failure );
		Assert.AreEqual( 0, missing.ConfiguredCapacity );
		Assert.AreEqual( 16, shortfall.ConfiguredCapacity );
	}

	[TestMethod]
	public void SpawnCapacity_GeometryShortfall_IsDistinctFromConfiguration()
	{
		var result = LargeLadSpawnRules.EvaluateGroupCapacity(
			LargeLadSpawnGroup.SkinnyKid,
			LargeLadGameManager.TargetPlayerCount,
			[
				new LargeLadSpawnAreaCapacity( "East", 16, 12 ),
				new LargeLadSpawnAreaCapacity( "West", 15, 10 )
			] );

		Assert.AreEqual( 31, result.RequiredCapacity );
		Assert.AreEqual( 31, result.ConfiguredCapacity );
		Assert.AreEqual( 22, result.ValidCapacity );
		Assert.AreEqual(
			LargeLadSpawnCapacityFailure.GeometryShortfall,
			result.Failure );
	}

	[TestMethod]
	public void SpawnCapacity_MultipleAreasCombineToMeetFullContract()
	{
		var result = LargeLadSpawnRules.EvaluateGroupCapacity(
			LargeLadSpawnGroup.Hunter,
			LargeLadGameManager.TargetPlayerCount,
			[
				new LargeLadSpawnAreaCapacity( "North", 12, 12 ),
				new LargeLadSpawnAreaCapacity( "South", 20, 20 )
			] );

		Assert.AreEqual( 32, result.ConfiguredCapacity );
		Assert.AreEqual( 32, result.ValidCapacity );
		Assert.AreEqual(
			LargeLadSpawnCapacityFailure.None,
			result.Failure );
	}

	[TestMethod]
	public void SpawnCapacity_ClampsAuthoredAndValidCountsToMapContract()
	{
		var result = LargeLadSpawnRules.EvaluateGroupCapacity(
			LargeLadSpawnGroup.Lobby,
			LargeLadGameManager.TargetPlayerCount,
			[new LargeLadSpawnAreaCapacity( "Lobby", 99, 99 )] );

		Assert.AreEqual( 32, result.ConfiguredCapacity );
		Assert.AreEqual( 32, result.ValidCapacity );
		Assert.AreEqual(
			LargeLadSpawnCapacityFailure.None,
			result.Failure );
	}
}
