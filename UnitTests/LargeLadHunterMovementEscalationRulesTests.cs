using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public sealed class HunterMovementEscalationRulesTests
{
	[TestMethod]
	public void NormalizedElapsedTime_UsesOnlyActiveSurvivalInterval()
	{
		Assert.AreEqual(
			0.0f,
			LargeLadHunterMovementEscalationRules
				.GetNormalizedElapsedSurvivalRoundTime(
					isSurvivalRoundActive: true,
					survivalRoundStartTime: 100.0f,
					survivalRoundEndTime: 200.0f,
					now: 90.0f ) );
		Assert.AreEqual(
			0.25f,
			LargeLadHunterMovementEscalationRules
				.GetNormalizedElapsedSurvivalRoundTime(
					isSurvivalRoundActive: true,
					survivalRoundStartTime: 100.0f,
					survivalRoundEndTime: 200.0f,
					now: 125.0f ) );
		Assert.AreEqual(
			1.0f,
			LargeLadHunterMovementEscalationRules
				.GetNormalizedElapsedSurvivalRoundTime(
					isSurvivalRoundActive: true,
					survivalRoundStartTime: 100.0f,
					survivalRoundEndTime: 200.0f,
					now: 250.0f ) );
	}

	[TestMethod]
	public void BeforeRamp_HuntersRemainAtNormalMovementSpeed()
	{
		Assert.AreEqual(
			1.0f,
			GetMultiplier( LargeLadRole.LargeLad, 0.39f ) );
		Assert.AreEqual(
			1.0f,
			GetMultiplier( LargeLadRole.Minion, 0.39f ) );
	}

	[TestMethod]
	public void DuringRamp_HuntersUseSmoothRoleSpecificInterpolation()
	{
		// The midpoint of smoothstep is exactly 0.5.
		Assert.AreEqual(
			1.05f,
			GetMultiplier( LargeLadRole.LargeLad, 0.6f ),
			0.0001f );
		Assert.AreEqual(
			1.075f,
			GetMultiplier( LargeLadRole.Minion, 0.6f ),
			0.0001f );
	}

	[TestMethod]
	public void AfterRamp_HuntersRemainAtConfiguredMaximums()
	{
		Assert.AreEqual(
			1.10f,
			GetMultiplier( LargeLadRole.LargeLad, 0.9f ),
			0.0001f );
		Assert.AreEqual(
			1.15f,
			GetMultiplier( LargeLadRole.Minion, 0.9f ),
			0.0001f );
	}

	[DataTestMethod]
	[DataRow( LargeLadRole.Unassigned )]
	[DataRow( LargeLadRole.SkinnyKid )]
	public void NonHunters_NeverReceiveEscalation( LargeLadRole role )
	{
		Assert.AreEqual( 1.0f, GetMultiplier( role, 1.0f ) );
	}

	[TestMethod]
	public void InactiveRound_ResetsNormalizedTimeAndMultiplierCompletely()
	{
		var normalized = LargeLadHunterMovementEscalationRules
			.GetNormalizedElapsedSurvivalRoundTime(
				isSurvivalRoundActive: false,
				survivalRoundStartTime: 100.0f,
				survivalRoundEndTime: 200.0f,
				now: 200.0f );

		Assert.AreEqual( 0.0f, normalized );
		Assert.AreEqual(
			1.0f,
			GetMultiplier( LargeLadRole.Minion, normalized ) );
	}

	[TestMethod]
	public void ProvisionalDefaults_MatchRequestedRoleMaximums()
	{
		Assert.AreEqual(
			1.10f,
			LargeLadHunterMovementEscalationRules
				.DefaultLargeLadMaximumMultiplier );
		Assert.AreEqual(
			1.15f,
			LargeLadHunterMovementEscalationRules
				.DefaultMinionMaximumMultiplier );
	}

	private static float GetMultiplier(
		LargeLadRole role,
		float normalizedElapsedSurvivalRoundTime )
	{
		return LargeLadHunterMovementEscalationRules.GetMovementMultiplier(
			role,
			normalizedElapsedSurvivalRoundTime,
			rampStartNormalizedTime: 0.4f,
			rampEndNormalizedTime: 0.8f,
			largeLadMaximumMultiplier: 1.10f,
			minionMaximumMultiplier: 1.15f );
	}
}
