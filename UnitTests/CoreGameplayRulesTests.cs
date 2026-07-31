using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;

[TestClass]
public sealed class TimerRulesTests
{
	[TestMethod]
	public void Deadline_UsesClampedDuration()
	{
		Assert.AreEqual(
			15.0f,
			LargeLadGameplayRules.GetTimerDeadline(
				now: 10.0f,
				duration: 5.0f ) );
		Assert.AreEqual(
			10.0f,
			LargeLadGameplayRules.GetTimerDeadline(
				now: 10.0f,
				duration: 0.0f ) );
		Assert.AreEqual(
			10.0f,
			LargeLadGameplayRules.GetTimerDeadline(
				now: 10.0f,
				duration: -5.0f ) );
	}

	[TestMethod]
	public void JoiningClient_CalculatesCurrentRemainingTimeFromDeadline()
	{
		Assert.AreEqual(
			6.25f,
			LargeLadGameplayRules.GetTimerTimeRemaining(
				deadline: 20.0f,
				now: 13.75f ) );
		Assert.AreEqual(
			0.0f,
			LargeLadGameplayRules.GetTimerTimeRemaining(
				deadline: 20.0f,
				now: 21.0f ) );
	}

	[TestMethod]
	public void Completion_BeginsAtAuthoritativeDeadline()
	{
		Assert.IsFalse(
			LargeLadGameplayRules.HasTimerReachedDeadline(
				deadline: 20.0f,
				now: 19.999f ) );
		Assert.IsTrue(
			LargeLadGameplayRules.HasTimerReachedDeadline(
				deadline: 20.0f,
				now: 20.0f ) );
		Assert.IsTrue(
			LargeLadGameplayRules.HasTimerReachedDeadline(
				deadline: 20.0f,
				now: 21.0f ) );
	}
}

[TestClass]
public sealed class RoundRulesTests
{
	[TestMethod]
	public void RoundPhases_AllowOnlyRuntimeTransitions()
	{
		var phases = new[]
		{
			LargeLadRoundPhase.WaitingForPlayers,
			LargeLadRoundPhase.HeadStart,
			LargeLadRoundPhase.Playing,
			LargeLadRoundPhase.RoundOver
		};

		var allowed = new[]
		{
			(LargeLadRoundPhase.WaitingForPlayers,
				LargeLadRoundPhase.HeadStart),
			(LargeLadRoundPhase.HeadStart,
				LargeLadRoundPhase.Playing),
			(LargeLadRoundPhase.HeadStart,
				LargeLadRoundPhase.RoundOver),
			(LargeLadRoundPhase.Playing,
				LargeLadRoundPhase.RoundOver),
			(LargeLadRoundPhase.RoundOver,
				LargeLadRoundPhase.WaitingForPlayers),
			(LargeLadRoundPhase.RoundOver,
				LargeLadRoundPhase.HeadStart)
		};

		foreach ( var current in phases )
		{
			foreach ( var next in phases )
			{
				var expected = System.Array.Exists(
					allowed,
					transition =>
						transition.Item1 == current &&
						transition.Item2 == next );

				Assert.AreEqual(
					expected,
					LargeLadGameplayRules.CanTransitionRoundPhase(
						current,
						next ),
					$"{current} -> {next}" );
			}
		}
	}

	[TestMethod]
	public void MinimumPlayers_StartsAtConfiguredBoundary()
	{
		Assert.IsFalse(
			LargeLadGameplayRules.HasMinimumPlayers( 1, 2 ) );
		Assert.IsTrue(
			LargeLadGameplayRules.HasMinimumPlayers( 2, 2 ) );
		Assert.IsTrue(
			LargeLadGameplayRules.HasMinimumPlayers( 3, 2 ) );
	}

	[DataTestMethod]
	[DataRow( 2 )]
	[DataRow( 16 )]
	[DataRow( 31 )]
	[DataRow( 32 )]
	public void SupportedRoundSize_IncludesContractBoundaries(
		int playerCount )
	{
		Assert.IsTrue(
			LargeLadGameplayRules.IsSupportedRoundPlayerCount(
				playerCount ) );
	}

	[TestMethod]
	public void SupportedRoundSize_RejectsCountsOutsideContract()
	{
		Assert.IsFalse(
			LargeLadGameplayRules.IsSupportedRoundPlayerCount( 1 ) );
		Assert.IsFalse(
			LargeLadGameplayRules.IsSupportedRoundPlayerCount( 33 ) );
	}

	[TestMethod]
	public void MissingLargeLad_AwardsSkinnyKids()
	{
		Assert.AreEqual(
			LargeLadWinner.SkinnyKids,
			LargeLadGameplayRules.DetermineWinnerWhenTeamIsMissing(
				hasLargeLad: false,
				hasSkinnyKid: true ) );
	}

	[TestMethod]
	public void MissingSkinnyKids_AwardsLargeLadTeam()
	{
		Assert.AreEqual(
			LargeLadWinner.LargeLadTeam,
			LargeLadGameplayRules.DetermineWinnerWhenTeamIsMissing(
				hasLargeLad: true,
				hasSkinnyKid: false ) );
	}

	[TestMethod]
	public void BothTeamsPresent_DoesNotChooseWinner()
	{
		Assert.AreEqual(
			LargeLadWinner.None,
			LargeLadGameplayRules.DetermineWinnerWhenTeamIsMissing(
				hasLargeLad: true,
				hasSkinnyKid: true ) );
	}

	[TestMethod]
	public void NoPlayers_PreservesSkinnyKidWinnerPrecedence()
	{
		Assert.AreEqual(
			LargeLadWinner.SkinnyKids,
			LargeLadGameplayRules.DetermineWinnerWhenTeamIsMissing(
				hasLargeLad: false,
				hasSkinnyKid: false ) );
	}

	[TestMethod]
	public void PendingRespawnRole_CountsForWinnerChecks()
	{
		Assert.AreEqual(
			LargeLadRole.Minion,
			LargeLadGameplayRules.GetEffectiveRoundRole(
				LargeLadRole.SkinnyKid,
				LargeLadRole.Minion ) );

		Assert.AreEqual(
			LargeLadRole.LargeLad,
			LargeLadGameplayRules.GetEffectiveRoundRole(
				LargeLadRole.LargeLad,
				LargeLadRole.Unassigned ) );
	}
}

[TestClass]
public sealed class RoundBalanceRulesTests
{
	[DataTestMethod]
	[DataRow( 1, (int)LargeLadBalanceBand.Small )]
	[DataRow( 3, (int)LargeLadBalanceBand.Small )]
	[DataRow( 4, (int)LargeLadBalanceBand.Medium )]
	[DataRow( 7, (int)LargeLadBalanceBand.Medium )]
	[DataRow( 8, (int)LargeLadBalanceBand.Large )]
	[DataRow( 15, (int)LargeLadBalanceBand.Large )]
	[DataRow( 16, (int)LargeLadBalanceBand.VeryLarge )]
	[DataRow( 23, (int)LargeLadBalanceBand.VeryLarge )]
	[DataRow( 24, (int)LargeLadBalanceBand.Full )]
	[DataRow( 31, (int)LargeLadBalanceBand.Full )]
	public void BandSelection_CoversEveryInclusiveBoundary(
		int skinnyKidCount,
		int expectedBand )
	{
		Assert.AreEqual(
			(LargeLadBalanceBand)expectedBand,
			LargeLadRoundBalanceRules.GetBand( skinnyKidCount ) );
	}

	[TestMethod]
	public void SelectedBand_RemainsFixedForEveryMidRoundRosterChange()
	{
		var selected = LargeLadRoundBalanceRules.ResolveState(
			default,
			currentSkinnyKidCount: 3,
			roundSuccessfullyBeginning: true );

		// Disconnect, death, conversion, and late join can each change the live
		// roster, but none is a successful round-start boundary.
		var liveSkinnyKidCounts = new[] { 2, 2, 1, 24 };

		foreach ( var liveSkinnyKidCount in liveSkinnyKidCounts )
		{
			selected = LargeLadRoundBalanceRules.ResolveState(
				selected,
				liveSkinnyKidCount,
				roundSuccessfullyBeginning: false );

			Assert.IsTrue( selected.HasSelection );
			Assert.AreEqual(
				LargeLadBalanceBand.Small,
				selected.SelectedBand );
			Assert.AreEqual(
				3,
				selected.SkinnyKidCountAtRoundStart );
		}
	}

	[TestMethod]
	public void NextSuccessfulRound_ReplacesTheAuthoritativeBand()
	{
		var selected = LargeLadRoundBalanceRules.ResolveState(
			default,
			currentSkinnyKidCount: 3,
			roundSuccessfullyBeginning: true );

		selected = LargeLadRoundBalanceRules.ResolveState(
			selected,
			currentSkinnyKidCount: 24,
			roundSuccessfullyBeginning: true );

		Assert.AreEqual(
			LargeLadBalanceBand.Full,
			selected.SelectedBand );
		Assert.AreEqual( 24, selected.SkinnyKidCountAtRoundStart );
	}

	[TestMethod]
	public void MediumDefaults_AreTheNeutralDocumentedBaseline()
	{
		var settings = new LargeLadRoundBalanceSettings();

		Assert.IsTrue(
			settings.TryGetMultipliers(
				LargeLadBalanceBand.Medium,
				out var medium ) );
		Assert.AreEqual( 1.0f, medium.LargeLadMaximumHealth );
		Assert.AreEqual(
			1.0f,
			medium.SkinnyProgressionBarricadeMaximumHealth );
	}

	[TestMethod]
	public void HealthScaling_ComposesWithMapFactorWithoutCompounding()
	{
		const float authoredMaximumHealth = 500.0f;
		const float bandMultiplier = 1.2f;
		const float mapMultiplier = 1.1f;

		var firstReset = LargeLadRoundBalanceRules.GetScaledMaximumHealth(
			authoredMaximumHealth,
			bandMultiplier,
			mapMultiplier );
		var laterReset = LargeLadRoundBalanceRules.GetScaledMaximumHealth(
			authoredMaximumHealth,
			bandMultiplier,
			mapMultiplier );

		Assert.AreEqual( 660.0f, firstReset, 0.001f );
		Assert.AreEqual(
			firstReset,
			laterReset,
			"Every reset must derive from the authored baseline." );
	}
}

[TestClass]
public sealed class RespawnRulesTests
{
	[TestMethod]
	public void SkinnyKidDeath_ConvertsToMinion()
	{
		Assert.AreEqual(
			LargeLadRole.Minion,
			LargeLadGameplayRules.ResolveRespawnRole(
				LargeLadRole.SkinnyKid ) );
	}

	[TestMethod]
	public void LargeLadDeath_PreservesLargeLadRole()
	{
		Assert.AreEqual(
			LargeLadRole.LargeLad,
			LargeLadGameplayRules.ResolveRespawnRole(
				LargeLadRole.LargeLad ) );
	}

	[TestMethod]
	public void MinionDeath_RespawnsAsMinion()
	{
		Assert.AreEqual(
			LargeLadRole.Minion,
			LargeLadGameplayRules.ResolveRespawnRole(
				LargeLadRole.Minion ) );
	}

	[TestMethod]
	public void LethalTransition_IsReportedOnlyOnce()
	{
		Assert.IsTrue(
			LargeLadGameplayRules.IsNewLethalTransition(
				previousHealth: 1.0f,
				currentHealth: 0.0f,
				alreadyReported: false ) );
		Assert.IsFalse(
			LargeLadGameplayRules.IsNewLethalTransition(
				previousHealth: 1.0f,
				currentHealth: 0.0f,
				alreadyReported: true ),
			"The same lethal edge cannot be reported twice." );
		Assert.IsFalse(
			LargeLadGameplayRules.IsNewLethalTransition(
				previousHealth: 0.0f,
				currentHealth: 0.0f,
				alreadyReported: false ),
			"An already-dead player does not create a new lethal edge." );
		Assert.IsFalse(
			LargeLadGameplayRules.IsNewLethalTransition(
				previousHealth: 2.0f,
				currentHealth: 1.0f,
				alreadyReported: false ),
			"Nonlethal damage does not report a death." );
	}

	[TestMethod]
	public void LethalTransition_CommitsOnlyAfterManagerAcceptance()
	{
		Assert.IsTrue(
			LargeLadGameplayRules.CanCommitLethalTransition(
				previousHealth: 1.0f,
				currentHealth: 0.0f,
				alreadyReported: false,
				managerAccepted: true ),
			"An accepted first lethal edge commits." );
		Assert.IsFalse(
			LargeLadGameplayRules.CanCommitLethalTransition(
				previousHealth: 1.0f,
				currentHealth: 0.0f,
				alreadyReported: false,
				managerAccepted: false ),
			"A rejected handoff must remain uncommitted and retryable." );
		Assert.IsFalse(
			LargeLadGameplayRules.CanCommitLethalTransition(
				previousHealth: 1.0f,
				currentHealth: 0.0f,
				alreadyReported: true,
				managerAccepted: true ),
			"Manager acceptance cannot commit the same lethal edge twice." );
	}

	[TestMethod]
	public void RespawnSpawnGroup_IsReconstructableFromPendingRole()
	{
		Assert.AreEqual(
			LargeLadSpawnGroup.Hunter,
			LargeLadGameplayRules.GetSpawnGroupForRole(
				LargeLadRole.LargeLad ) );
		Assert.AreEqual(
			LargeLadSpawnGroup.Hunter,
			LargeLadGameplayRules.GetSpawnGroupForRole(
				LargeLadRole.Minion ) );
		Assert.AreEqual(
			LargeLadSpawnGroup.SkinnyKid,
			LargeLadGameplayRules.GetSpawnGroupForRole(
				LargeLadRole.SkinnyKid ) );
		Assert.AreEqual(
			LargeLadSpawnGroup.Lobby,
			LargeLadGameplayRules.GetSpawnGroupForRole(
				LargeLadRole.Unassigned ) );
	}

	[TestMethod]
	public void EveryDamageSource_UsesAuthoritativeRoleDelayAndSpawnPolicy()
	{
		foreach ( var damageType in
			System.Enum.GetValues<LargeLadDamageType>() )
		{
			var largeLad = LargeLadGameplayRules.ResolveDeathPlan(
				LargeLadRole.LargeLad,
				damageType,
				largeLadRespawnDelay: 7.0f,
				playerRespawnDelay: 3.0f );
			var skinnyKid = LargeLadGameplayRules.ResolveDeathPlan(
				LargeLadRole.SkinnyKid,
				damageType,
				largeLadRespawnDelay: 7.0f,
				playerRespawnDelay: 3.0f );
			var minion = LargeLadGameplayRules.ResolveDeathPlan(
				LargeLadRole.Minion,
				damageType,
				largeLadRespawnDelay: 7.0f,
				playerRespawnDelay: 3.0f );

			Assert.AreEqual( LargeLadRole.LargeLad, largeLad.ResultingRole );
			Assert.AreEqual( 7.0f, largeLad.RespawnDelay );
			Assert.AreEqual( LargeLadSpawnGroup.Hunter, largeLad.SpawnGroup );

			Assert.AreEqual( LargeLadRole.Minion, skinnyKid.ResultingRole );
			Assert.AreEqual( 3.0f, skinnyKid.RespawnDelay );
			Assert.AreEqual( LargeLadSpawnGroup.Hunter, skinnyKid.SpawnGroup );

			Assert.AreEqual( LargeLadRole.Minion, minion.ResultingRole );
			Assert.AreEqual( 3.0f, minion.RespawnDelay );
			Assert.AreEqual( LargeLadSpawnGroup.Hunter, minion.SpawnGroup );
		}
	}

	[TestMethod]
	public void EnvironmentDeath_SkipsRagdollWhileCombatDeathsUseIt()
	{
		var firearm = LargeLadGameplayRules.ResolveDeathPlan(
			LargeLadRole.Minion,
			LargeLadDamageType.Firearm,
			largeLadRespawnDelay: 7.0f,
			playerRespawnDelay: 3.0f );
		var melee = LargeLadGameplayRules.ResolveDeathPlan(
			LargeLadRole.Minion,
			LargeLadDamageType.Melee,
			largeLadRespawnDelay: 7.0f,
			playerRespawnDelay: 3.0f );
		var environment = LargeLadGameplayRules.ResolveDeathPlan(
			LargeLadRole.Minion,
			LargeLadDamageType.Environment,
			largeLadRespawnDelay: 7.0f,
			playerRespawnDelay: 3.0f );

		Assert.IsTrue( firearm.UseRagdoll );
		Assert.IsTrue( melee.UseRagdoll );
		Assert.IsFalse( environment.UseRagdoll );
	}

	[TestMethod]
	public void SkinnyKidDeathPlan_ChangesWinnerEligibilityImmediately()
	{
		var death = LargeLadGameplayRules.ResolveDeathPlan(
			LargeLadRole.SkinnyKid,
			LargeLadDamageType.Firearm,
			largeLadRespawnDelay: 7.0f,
			playerRespawnDelay: 3.0f );
		var effectiveRole = LargeLadGameplayRules.GetEffectiveRoundRole(
			LargeLadRole.SkinnyKid,
			death.ResultingRole );

		Assert.AreEqual( LargeLadRole.Minion, effectiveRole );
		Assert.AreEqual(
			LargeLadWinner.LargeLadTeam,
			LargeLadGameplayRules.DetermineWinnerWhenTeamIsMissing(
				hasLargeLad: true,
				hasSkinnyKid:
					effectiveRole == LargeLadRole.SkinnyKid ) );
	}
}

[TestClass]
public sealed class PlayerCollisionRulesTests
{
	[DataTestMethod]
	[DataRow( LargeLadRole.LargeLad, LargeLadRole.LargeLad, false )]
	[DataRow( LargeLadRole.LargeLad, LargeLadRole.Minion, false )]
	[DataRow( LargeLadRole.Minion, LargeLadRole.LargeLad, false )]
	[DataRow( LargeLadRole.Minion, LargeLadRole.Minion, false )]
	[DataRow( LargeLadRole.LargeLad, LargeLadRole.SkinnyKid, true )]
	[DataRow( LargeLadRole.SkinnyKid, LargeLadRole.LargeLad, true )]
	[DataRow( LargeLadRole.Minion, LargeLadRole.SkinnyKid, true )]
	[DataRow( LargeLadRole.SkinnyKid, LargeLadRole.Minion, true )]
	[DataRow( LargeLadRole.SkinnyKid, LargeLadRole.SkinnyKid, false )]
	[DataRow( LargeLadRole.Unassigned, LargeLadRole.LargeLad, true )]
	[DataRow( LargeLadRole.Unassigned, LargeLadRole.Minion, true )]
	[DataRow( LargeLadRole.Unassigned, LargeLadRole.SkinnyKid, false )]
	[DataRow( LargeLadRole.Unassigned, LargeLadRole.Unassigned, false )]
	public void RolePair_UsesApprovedSolidCollisionRule(
		LargeLadRole left,
		LargeLadRole right,
		bool expectedSolidCollision )
	{
		Assert.AreEqual(
			expectedSolidCollision,
			LargeLadGameplayRules.HasSolidPlayerCollision( left, right ) );
	}

	[DataTestMethod]
	[DataRow( LargeLadRole.SkinnyKid, LargeLadRole.SkinnyKid, true )]
	[DataRow( LargeLadRole.Unassigned, LargeLadRole.SkinnyKid, true )]
	[DataRow( LargeLadRole.Unassigned, LargeLadRole.Unassigned, true )]
	[DataRow( LargeLadRole.LargeLad, LargeLadRole.SkinnyKid, false )]
	[DataRow( LargeLadRole.Minion, LargeLadRole.SkinnyKid, false )]
	[DataRow( LargeLadRole.LargeLad, LargeLadRole.Minion, false )]
	public void RolePair_UsesSoftCollisionOnlyBetweenNonHunters(
		LargeLadRole left,
		LargeLadRole right,
		bool expectedSoftCollision )
	{
		Assert.AreEqual(
			expectedSoftCollision,
			LargeLadGameplayRules.UsesSoftPlayerCollision( left, right ) );
	}

	[TestMethod]
	public void LobbyAndSkinnyKid_UseSoftPlayerBodyTag()
	{
		Assert.AreEqual(
			LargeLadGameplayRules.SoftPlayerBodyTag,
			LargeLadGameplayRules.GetPlayerBodyCollisionTag(
				LargeLadRole.Unassigned ) );
		Assert.AreEqual(
			LargeLadGameplayRules.SoftPlayerBodyTag,
			LargeLadGameplayRules.GetPlayerBodyCollisionTag(
				LargeLadRole.SkinnyKid ) );
	}

	[TestMethod]
	public void LargeLadAndMinions_ShareHunterBodyTag()
	{
		Assert.AreEqual(
			LargeLadGameplayRules.HunterBodyTag,
			LargeLadGameplayRules.GetPlayerBodyCollisionTag(
				LargeLadRole.LargeLad ) );
		Assert.AreEqual(
			LargeLadGameplayRules.HunterBodyTag,
			LargeLadGameplayRules.GetPlayerBodyCollisionTag(
				LargeLadRole.Minion ) );
	}

	[TestMethod]
	public void RuntimeRoleTags_CopyAsTwoDistinctEngineTags()
	{
		var configured = new Sandbox.TagSet();
		configured.Add( LargeLadGameplayRules.PlayerBodyTag );
		configured.Add( LargeLadGameplayRules.HunterBodyTag );
		var live = new Sandbox.TagSet();

		live.SetFrom( configured );

		Assert.IsTrue(
			live.Has( LargeLadGameplayRules.PlayerBodyTag ) );
		Assert.IsTrue(
			live.Has( LargeLadGameplayRules.HunterBodyTag ) );
		Assert.IsFalse(
			live.Has( LargeLadGameplayRules.SoftPlayerBodyTag ) );
	}

	[TestMethod]
	public void CollisionRule_IsSymmetricForEveryRolePair()
	{
		foreach ( var left in System.Enum.GetValues<LargeLadRole>() )
		{
			foreach ( var right in System.Enum.GetValues<LargeLadRole>() )
			{
				Assert.AreEqual(
					LargeLadGameplayRules.HasSolidPlayerCollision( left, right ),
					LargeLadGameplayRules.HasSolidPlayerCollision( right, left ),
					$"{left} versus {right}" );
			}
		}
	}

	[TestMethod]
	public void FullHunterRoster_CannotHardBlockItselfWhenOverlapping()
	{
		var hunters = new LargeLadRole[
			LargeLadGameManager.TargetPlayerCount];
		hunters[0] = LargeLadRole.LargeLad;

		for ( var index = 1; index < hunters.Length; index++ )
			hunters[index] = LargeLadRole.Minion;

		foreach ( var left in hunters )
		{
			foreach ( var right in hunters )
			{
				Assert.IsFalse(
					LargeLadGameplayRules.HasSolidPlayerCollision(
						left,
						right ) );
			}
		}
	}

	[TestMethod]
	public void SoftCollision_IsHorizontalCappedAndFallsOffWithDistance()
	{
		var coincident =
			LargeLadGameplayRules.GetSoftPlayerSeparationVelocity(
				Vector3.Zero,
				Vector3.Zero,
				usePositiveXWhenCoincident: true );
		var halfway =
			LargeLadGameplayRules.GetSoftPlayerSeparationVelocity(
				new Vector3(
					LargeLadGameplayRules.SoftPlayerSeparationRadius * 0.5f,
					0.0f,
					12.0f ),
				Vector3.Zero,
				usePositiveXWhenCoincident: true );
		var outside =
			LargeLadGameplayRules.GetSoftPlayerSeparationVelocity(
				new Vector3(
					LargeLadGameplayRules.SoftPlayerSeparationRadius,
					0.0f,
					0.0f ),
				Vector3.Zero,
				usePositiveXWhenCoincident: true );

		Assert.AreEqual(
			LargeLadGameplayRules.SoftPlayerMaximumSeparationSpeed,
			coincident.x,
			0.001f );
		Assert.AreEqual( 0.0f, coincident.y, 0.001f );
		Assert.AreEqual( 0.0f, coincident.z, 0.001f );
		Assert.AreEqual(
			LargeLadGameplayRules.SoftPlayerBaseMaximumSeparationSpeed *
				0.5f *
				1.25f,
			halfway.x,
			0.001f );
		Assert.AreEqual( Vector3.Zero, outside );
	}

	[TestMethod]
	public void SoftCollision_IgnoresPlayersSeparatedVertically()
	{
		var result =
			LargeLadGameplayRules.GetSoftPlayerSeparationVelocity(
				new Vector3(
					0.0f,
					0.0f,
					LargeLadGameplayRules.SoftPlayerSeparationHeight ),
				Vector3.Zero,
				usePositiveXWhenCoincident: true );

		Assert.AreEqual( Vector3.Zero, result );
	}

	[TestMethod]
	public void SoftCollision_RepeatedCoincidentTicksRemainBounded()
	{
		const int fixedTicks = 120;
		const float fixedDelta = 1.0f / 60.0f;
		var leftVelocity = Vector3.Zero;
		var rightVelocity = Vector3.Zero;
		var leftCorrection = Vector3.Zero;
		var rightCorrection = Vector3.Zero;
		var leftTarget =
			LargeLadGameplayRules.GetSoftPlayerSeparationVelocity(
				Vector3.Zero,
				Vector3.Zero,
				usePositiveXWhenCoincident: true );
		var rightTarget =
			LargeLadGameplayRules.GetSoftPlayerSeparationVelocity(
				Vector3.Zero,
				Vector3.Zero,
				usePositiveXWhenCoincident: false );

		for ( var tick = 0; tick < fixedTicks; tick++ )
		{
			var leftResult =
				LargeLadGameplayRules.ResolveSoftPlayerSeparation(
					leftVelocity,
					leftTarget,
					fixedDelta );
			var rightResult =
				LargeLadGameplayRules.ResolveSoftPlayerSeparation(
					rightVelocity,
					rightTarget,
					fixedDelta );
			leftCorrection = leftResult.Displacement;
			rightCorrection = rightResult.Displacement;

			Assert.IsTrue(
				leftCorrection.Length <=
					LargeLadGameplayRules
						.SoftPlayerMaximumSeparationSpeed *
						fixedDelta + 0.001f,
				$"left correction exceeded the cap on tick {tick}" );
			Assert.IsTrue(
				rightCorrection.Length <=
					LargeLadGameplayRules
						.SoftPlayerMaximumSeparationSpeed *
						fixedDelta + 0.001f,
				$"right correction exceeded the cap on tick {tick}" );
			AssertVectorEqual(
				Vector3.Zero,
				leftResult.Velocity,
				0.001f );
			AssertVectorEqual(
				Vector3.Zero,
				rightResult.Velocity,
				0.001f );
			AssertVectorEqual(
				leftCorrection,
				-rightCorrection,
				0.001f );

			leftVelocity = leftResult.Velocity;
			rightVelocity = rightResult.Velocity;
			Assert.AreEqual( Vector3.Zero, leftVelocity );
			Assert.AreEqual( Vector3.Zero, rightVelocity );
		}

		Assert.IsTrue(
			leftCorrection.Length >
				0.0f );
		Assert.AreEqual(
			LargeLadGameplayRules.SoftPlayerMaximumSeparationSpeed *
				fixedDelta,
			leftCorrection.Length,
			0.001f );
	}

	[TestMethod]
	public void SoftCollision_CrowdTargetsCombineThenRemainCapped()
	{
		const int fixedTicks = 120;
		const float fixedDelta = 1.0f / 60.0f;
		var playerPosition = Vector3.Zero;
		var neighborPositions = new[]
		{
			new Vector3( 1.0f, 0.0f, 0.0f ),
			new Vector3( 3.0f, 1.0f, 0.0f ),
			new Vector3( 5.0f, -1.0f, 0.0f ),
			new Vector3( 7.0f, 0.5f, 0.0f )
		};
		var combinedTarget = Vector3.Zero;

		foreach ( var neighborPosition in neighborPositions )
		{
			combinedTarget +=
				LargeLadGameplayRules.GetSoftPlayerSeparationVelocity(
					playerPosition,
					neighborPosition,
					usePositiveXWhenCoincident: true );
		}

		Assert.IsTrue(
			combinedTarget.x < 0.0f,
			"neighbors on the right should combine into a leftward target" );
		Assert.IsTrue(
			combinedTarget.Length >
				LargeLadGameplayRules.SoftPlayerMaximumSeparationSpeed );

		var boundedTarget =
			LargeLadGameplayRules.ClampSoftPlayerSeparationVelocity(
				combinedTarget );
		Assert.AreEqual(
			LargeLadGameplayRules.SoftPlayerMaximumSeparationSpeed,
			boundedTarget.Length,
			0.001f );
		Assert.AreEqual( 0.0f, boundedTarget.z, 0.001f );

		var velocity = Vector3.Zero;
		var correction = Vector3.Zero;

		for ( var tick = 0; tick < fixedTicks; tick++ )
		{
			var result =
				LargeLadGameplayRules.ResolveSoftPlayerSeparation(
					velocity,
					combinedTarget,
					fixedDelta );
			correction = result.Displacement;

			Assert.IsTrue(
				correction.Length <=
					LargeLadGameplayRules
						.SoftPlayerMaximumSeparationSpeed *
						fixedDelta + 0.001f,
				$"crowd correction exceeded the cap on tick {tick}" );
			velocity = result.Velocity;
			AssertVectorEqual(
				Vector3.Zero,
				velocity,
				0.001f );
		}
	}

	[TestMethod]
	public void SoftCollision_RepeatedTicksPreserveExternalImpulse()
	{
		const float fixedDelta = 1.0f / 60.0f;
		var externalVelocity =
			new Vector3( 110.0f, -20.0f, -250.0f );
		var velocity = externalVelocity;
		var correction = Vector3.Zero;
		var target =
			LargeLadGameplayRules.GetSoftPlayerSeparationVelocity(
				Vector3.Zero,
				Vector3.Zero,
				usePositiveXWhenCoincident: true );

		for ( var tick = 0; tick < 120; tick++ )
		{
			if ( tick == 40 )
			{
				var weaponImpulse =
					new Vector3( 320.0f, -75.0f, 0.0f );
				velocity += weaponImpulse;
				externalVelocity += weaponImpulse;
			}

			var result =
				LargeLadGameplayRules.ResolveSoftPlayerSeparation(
					velocity,
					target,
					fixedDelta );
			correction = result.Displacement;

			AssertVectorEqual(
				externalVelocity,
				result.Velocity,
				0.001f );
			Assert.AreEqual(
				externalVelocity.z,
				result.Velocity.z,
				0.0f );
			velocity = result.Velocity;
		}
	}

	[DataTestMethod]
	[DataRow( -300.0f )]
	[DataRow( 0.0f )]
	[DataRow( 300.0f )]
	[DataRow( 900.0f )]
	public void SoftCollision_RepeatedTicksNeverChangeVerticalVelocity(
		float verticalVelocity )
	{
		const float fixedDelta = 1.0f / 60.0f;
		var velocity = new Vector3( 25.0f, -10.0f, verticalVelocity );
		var correction = Vector3.Zero;
		var target =
			LargeLadGameplayRules.GetSoftPlayerSeparationVelocity(
				Vector3.Zero,
				Vector3.Zero,
				usePositiveXWhenCoincident: true );

		for ( var tick = 0; tick < 120; tick++ )
		{
			var result =
				LargeLadGameplayRules.ResolveSoftPlayerSeparation(
					velocity,
					target,
					fixedDelta );
			correction = result.Displacement;

			Assert.AreEqual(
				verticalVelocity,
				result.Velocity.z,
				0.0f,
				$"vertical velocity changed on tick {tick}" );
			velocity = result.Velocity;
		}
	}

	[TestMethod]
	public void SoftCollision_JumpAndUpwardSlamImpulseRemainUnchanged()
	{
		const float fixedDelta = 1.0f / 60.0f;
		var velocity = new Vector3( 15.0f, 5.0f, 300.0f );
		var expectedVerticalVelocity = velocity.z;
		var correction = Vector3.Zero;
		var target =
			LargeLadGameplayRules.GetSoftPlayerSeparationVelocity(
				Vector3.Zero,
				Vector3.Zero,
				usePositiveXWhenCoincident: true );

		for ( var tick = 0; tick < 120; tick++ )
		{
			if ( tick == 40 )
			{
				const float upwardSlamImpulse = 600.0f;
				velocity.z += upwardSlamImpulse;
				expectedVerticalVelocity += upwardSlamImpulse;
			}

			var result =
				LargeLadGameplayRules.ResolveSoftPlayerSeparation(
					velocity,
					target,
					fixedDelta );
			correction = result.Displacement;

			Assert.AreEqual(
				expectedVerticalVelocity,
				result.Velocity.z,
				0.0f,
				$"upward velocity changed on tick {tick}" );
			velocity = result.Velocity;
		}
	}

	[TestMethod]
	public void SoftCollision_OutsideRadiusAndVerticalCutoffAddNothing()
	{
		var currentVelocity = new Vector3( 80.0f, -15.0f, 300.0f );
		var outsideRadius =
			LargeLadGameplayRules.GetSoftPlayerSeparationVelocity(
				new Vector3(
					LargeLadGameplayRules.SoftPlayerSeparationRadius,
					0.0f,
					0.0f ),
				Vector3.Zero,
				usePositiveXWhenCoincident: true );
		var beyondVerticalCutoff =
			LargeLadGameplayRules.GetSoftPlayerSeparationVelocity(
				new Vector3(
					0.0f,
					0.0f,
					LargeLadGameplayRules.SoftPlayerSeparationHeight ),
				Vector3.Zero,
				usePositiveXWhenCoincident: true );
		var outsideResult =
			LargeLadGameplayRules.ResolveSoftPlayerSeparation(
				currentVelocity,
				outsideRadius,
				1.0f / 60.0f );
		var verticalResult =
			LargeLadGameplayRules.ResolveSoftPlayerSeparation(
				currentVelocity,
				beyondVerticalCutoff,
				1.0f / 60.0f );

		Assert.AreEqual( Vector3.Zero, outsideRadius );
		Assert.AreEqual( Vector3.Zero, beyondVerticalCutoff );
		AssertVectorEqual(
			currentVelocity,
			outsideResult.Velocity,
			0.0f );
		AssertVectorEqual(
			currentVelocity,
			verticalResult.Velocity,
			0.0f );
		Assert.AreEqual(
			Vector3.Zero,
			outsideResult.Displacement );
		Assert.AreEqual(
			Vector3.Zero,
			verticalResult.Displacement );
	}

	[TestMethod]
	public void SoftCollision_CorrectionEndsWhenOverlapStops()
	{
		const float fixedDelta = 1.0f / 60.0f;
		var externalVelocity =
			new Vector3( 95.0f, 12.0f, 275.0f );
		var velocity = externalVelocity;
		var correction = Vector3.Zero;
		var target =
			LargeLadGameplayRules.GetSoftPlayerSeparationVelocity(
				Vector3.Zero,
				Vector3.Zero,
				usePositiveXWhenCoincident: true );

		for ( var tick = 0; tick < 120; tick++ )
		{
			var result =
				LargeLadGameplayRules.ResolveSoftPlayerSeparation(
					velocity,
					target,
					fixedDelta );
			correction = result.Displacement;
			velocity = result.Velocity;
		}

		var separatedResult =
			LargeLadGameplayRules.ResolveSoftPlayerSeparation(
				velocity,
				Vector3.Zero,
				fixedDelta );

		Assert.AreEqual(
			Vector3.Zero,
			separatedResult.Displacement );
		AssertVectorEqual(
			externalVelocity,
			separatedResult.Velocity,
			0.001f );
	}

	[TestMethod]
	public void SoftCollision_CoincidentDirectionIsDeterministicAndOpposite()
	{
		var first =
			LargeLadGameplayRules.GetSoftPlayerSeparationVelocity(
				Vector3.Zero,
				Vector3.Zero,
				usePositiveXWhenCoincident: true );
		var firstRepeated =
			LargeLadGameplayRules.GetSoftPlayerSeparationVelocity(
				Vector3.Zero,
				Vector3.Zero,
				usePositiveXWhenCoincident: true );
		var second =
			LargeLadGameplayRules.GetSoftPlayerSeparationVelocity(
				Vector3.Zero,
				Vector3.Zero,
				usePositiveXWhenCoincident: false );

		AssertVectorEqual( first, firstRepeated, 0.0f );
		AssertVectorEqual( first, -second, 0.0f );
		Assert.AreEqual(
			LargeLadGameplayRules.SoftPlayerMaximumSeparationSpeed,
			first.Length,
			0.001f );
	}

	[TestMethod]
	public void SoftCollision_ChangingRadialDirectionHasNoTangentialHistory()
	{
		const int fixedTicks = 120;
		const float fixedDelta = 1.0f / 60.0f;
		var velocity = Vector3.Zero;

		for ( var tick = 0; tick < fixedTicks; tick++ )
		{
			var angle = tick * System.MathF.PI * 2.0f / fixedTicks;
			var target = new Vector3(
				System.MathF.Cos( angle ),
				System.MathF.Sin( angle ),
				0.0f ) *
				LargeLadGameplayRules.SoftPlayerMaximumSeparationSpeed;
			var result =
				LargeLadGameplayRules.ResolveSoftPlayerSeparation(
					velocity,
					target,
					fixedDelta );
			var correction = result.Displacement;
			var planarCross =
				correction.x * target.y -
				correction.y * target.x;

			Assert.AreEqual(
				0.0f,
				planarCross,
				0.001f,
				$"correction gained a tangential component on tick {tick}" );
			Assert.IsTrue(
				Vector3.Dot( correction, target ) > 0.0f );

			velocity = result.Velocity;
			AssertVectorEqual(
				Vector3.Zero,
				velocity,
				0.001f );
		}
	}

	private static void AssertVectorEqual(
		Vector3 expected,
		Vector3 actual,
		float tolerance )
	{
		Assert.AreEqual( expected.x, actual.x, tolerance );
		Assert.AreEqual( expected.y, actual.y, tolerance );
		Assert.AreEqual( expected.z, actual.z, tolerance );
	}
}

[TestClass]
public sealed class SpawnRulesTests
{
	[DataTestMethod]
	[DataRow( 2 )]
	[DataRow( 16 )]
	[DataRow( 31 )]
	[DataRow( 32 )]
	public void DeterministicLayout_IdenticalInputsProduceIdenticalOffsets(
		int desiredCount )
	{
		const float radius = LargeLadTeamSpawn.DefaultSpawnRadius;

		for ( var attempt = 0; attempt < desiredCount * 2; attempt++ )
		{
			var first = LargeLadSpawnRules.GetDeterministicLayoutOffset(
				attempt,
				desiredCount,
				radius );
			var second = LargeLadSpawnRules.GetDeterministicLayoutOffset(
				attempt,
				desiredCount,
				radius );

			Assert.AreEqual( first.x, second.x );
			Assert.AreEqual( first.y, second.y );
			Assert.AreEqual( first.z, second.z );
		}
	}

	[DataTestMethod]
	[DataRow( 2 )]
	[DataRow( 16 )]
	[DataRow( 31 )]
	[DataRow( 32 )]
	public void DeterministicLayout_DefaultAreaSeparatesEveryPosition(
		int desiredCount )
	{
		var locations = new List<LargeLadSpawnLocation>();

		for ( var index = 0; index < desiredCount; index++ )
		{
			var candidate = new LargeLadSpawnLocation(
				LargeLadSpawnRules.GetDeterministicLayoutOffset(
					index,
					desiredCount,
					LargeLadTeamSpawn.DefaultSpawnRadius ),
				default,
				LargeLadTeamSpawn.DefaultMinimumSeparation );

			foreach ( var existing in locations )
			{
				Assert.IsTrue(
					LargeLadSpawnRules.MeetsPairwiseSeparation(
						candidate,
						existing ),
					$"Position {index} overlaps an earlier position in the " +
					$"{desiredCount}-player default layout." );
			}

			locations.Add( candidate );
		}

		Assert.AreEqual( desiredCount, locations.Count );
	}

	[DataTestMethod]
	[DataRow( 2, 2, 1, 2 )]
	[DataRow( 16, 16, 15, 16 )]
	[DataRow( 31, 31, 30, 31 )]
	[DataRow( 32, 32, 31, 32 )]
	public void RequiredCapacity_ScalesWithSupportedRoundSize(
		int playerCount,
		int expectedLobby,
		int expectedSkinnyKid,
		int expectedHunter )
	{
		Assert.AreEqual(
			expectedLobby,
			LargeLadSpawnRules.GetRequiredCapacity(
				LargeLadSpawnGroup.Lobby,
				playerCount ) );
		Assert.AreEqual(
			expectedSkinnyKid,
			LargeLadSpawnRules.GetRequiredCapacity(
				LargeLadSpawnGroup.SkinnyKid,
				playerCount ) );
		Assert.AreEqual(
			expectedHunter,
			LargeLadSpawnRules.GetRequiredCapacity(
				LargeLadSpawnGroup.Hunter,
				playerCount ) );
	}

	[TestMethod]
	public void FullMapContract_Requires32Lobby31SkinnyKid32Hunter()
	{
		Assert.AreEqual( 2, LargeLadGameManager.MinimumSupportedPlayerCount );
		Assert.AreEqual( 32, LargeLadGameManager.TargetPlayerCount );
		Assert.AreEqual(
			32,
			LargeLadSpawnRules.GetRequiredCapacity(
				LargeLadSpawnGroup.Lobby,
				LargeLadGameManager.TargetPlayerCount ) );
		Assert.AreEqual(
			31,
			LargeLadSpawnRules.GetRequiredCapacity(
				LargeLadSpawnGroup.SkinnyKid,
				LargeLadGameManager.TargetPlayerCount ) );
		Assert.AreEqual(
			32,
			LargeLadSpawnRules.GetRequiredCapacity(
				LargeLadSpawnGroup.Hunter,
				LargeLadGameManager.TargetPlayerCount ) );
	}

	[DataTestMethod]
	[DataRow( 2 )]
	[DataRow( 16 )]
	[DataRow( 31 )]
	[DataRow( 32 )]
	public void UsableAuthoredCapacity_PreservesSupportedBoundaries(
		int authoredCapacity )
	{
		Assert.AreEqual(
			authoredCapacity,
			LargeLadSpawnRules.GetUsableAuthoredCapacity(
				authoredCapacity ) );
	}

	[TestMethod]
	public void UsableAuthoredCapacity_ClampsOutsideContract()
	{
		Assert.AreEqual(
			0,
			LargeLadSpawnRules.GetUsableAuthoredCapacity( -1 ) );
		Assert.AreEqual(
			32,
			LargeLadSpawnRules.GetUsableAuthoredCapacity( 33 ) );
	}

	[TestMethod]
	public void PairwiseSeparation_UsesLargerCandidateRequirement()
	{
		var relaxed = new LargeLadSpawnLocation(
			Vector3.Zero,
			default,
			32.0f );
		var strictTooClose = new LargeLadSpawnLocation(
			new Vector3( 64.0f, 0.0f, 0.0f ),
			default,
			80.0f );
		var strictClear = new LargeLadSpawnLocation(
			new Vector3( 80.0f, 0.0f, 0.0f ),
			default,
			80.0f );

		Assert.IsFalse(
			LargeLadSpawnRules.MeetsPairwiseSeparation(
				relaxed,
				strictTooClose ) );
		Assert.IsFalse(
			LargeLadSpawnRules.MeetsPairwiseSeparation(
				strictTooClose,
				relaxed ),
			"The comparison must be symmetric." );
		Assert.IsTrue(
			LargeLadSpawnRules.MeetsPairwiseSeparation(
				relaxed,
				strictClear ) );
	}

	[DataTestMethod]
	[DataRow( 2 )]
	[DataRow( 16 )]
	[DataRow( 31 )]
	[DataRow( 32 )]
	public void BatchAllocation_MustContainEveryRequestedPlayer(
		int playerCount )
	{
		var requested = new object[playerCount];
		var complete =
			new Dictionary<object, LargeLadSpawnLocation>();

		for ( var index = 0; index < playerCount; index++ )
		{
			requested[index] = new object();
			complete[requested[index]] = default;
		}

		var incomplete =
			new Dictionary<object, LargeLadSpawnLocation>( complete );
		incomplete.Remove( requested[playerCount - 1] );

		Assert.IsFalse(
			LargeLadSpawnRules.HasCompleteBatchAllocation(
				requested,
				incomplete ) );
		Assert.IsTrue(
			LargeLadSpawnRules.HasCompleteBatchAllocation(
				requested,
				complete ) );
	}
}

[TestClass]
public sealed class BarricadeRulesTests
{
	[TestMethod]
	public void EveryModeRoleAndDamageType_AllowsOnlyMatchingRoleMelee()
	{
		foreach ( var mode in
			System.Enum.GetValues<LargeLadBarricadeMode>() )
		{
			foreach ( var role in System.Enum.GetValues<LargeLadRole>() )
			{
				foreach ( var damageType in
					System.Enum.GetValues<LargeLadDamageType>() )
				{
					var expected =
						damageType == LargeLadDamageType.Melee &&
						(mode, role) is
							(
								LargeLadBarricadeMode.SkinnyProgression,
								LargeLadRole.SkinnyKid
							) or
							(
								LargeLadBarricadeMode.LadShortcut,
								LargeLadRole.LargeLad
							);

					Assert.AreEqual(
						expected,
						LargeLadGameplayRules.CanDamageBarricade(
							mode,
							role,
							damageType ),
						$"{mode}: {role} using {damageType}" );
				}
			}
		}
	}

	[TestMethod]
	public void UnknownRuleInputs_FailClosed()
	{
		Assert.IsFalse(
			LargeLadGameplayRules.CanDamageBarricade(
				(LargeLadBarricadeMode)(-1),
				LargeLadRole.LargeLad,
				LargeLadDamageType.Melee ) );
		Assert.IsFalse(
			LargeLadGameplayRules.CanDamageBarricade(
				LargeLadBarricadeMode.SkinnyProgression,
				(LargeLadRole)(-1),
				LargeLadDamageType.Melee ) );
		Assert.IsFalse(
			LargeLadGameplayRules.CanDamageBarricade(
				LargeLadBarricadeMode.SkinnyProgression,
				LargeLadRole.SkinnyKid,
				(LargeLadDamageType)(-1) ) );
	}
}

[TestClass]
public sealed class InventoryRulesTests
{
	[TestMethod]
	public void CoreInventory_AddsMultipleDistinctWeaponsWithoutSlots()
	{
		var core = new List<LargeLadWeaponState>();

		Assert.IsTrue(
			LargeLadInventoryRules.TryAddCoreWeapon(
				core,
				LargeLadWeaponId.Pistol ) );
		Assert.IsTrue(
			LargeLadInventoryRules.TryAddCoreWeapon(
				core,
				LargeLadWeaponId.Smg ) );
		Assert.AreEqual( 2, core.Count );
		Assert.IsTrue(
			LargeLadInventoryRules.IsValidCoreState( core[0] ) );
		Assert.IsTrue(
			LargeLadInventoryRules.IsValidCoreState( core[1] ) );
	}

	[TestMethod]
	public void CoreInventory_RejectsDuplicateWeaponWithoutGrantingAmmo()
	{
		var core = new List<LargeLadWeaponState>();
		LargeLadInventoryRules.TryAddCoreWeapon(
			core,
			LargeLadWeaponId.Pistol );
		var before = core[0];

		Assert.IsFalse(
			LargeLadInventoryRules.TryAddCoreWeapon(
				core,
				LargeLadWeaponId.Pistol ) );
		Assert.AreEqual( 1, core.Count );
		Assert.AreEqual( before, core[0] );
		Assert.AreEqual(
			LargeLadAmmunitionMode.InfiniteReserve,
			core[0].AmmunitionMode );
		Assert.AreEqual(
			0,
			core[0].Reserve,
			"Infinite reserve is a mode, never a fake or grantable count." );
	}

	[TestMethod]
	public void CoreReload_FillsMagazineWithoutChangingReserve()
	{
		var state =
			LargeLadWeaponState.CreateCore( LargeLadWeaponId.Pistol );
		state.Magazine = 2;

		Assert.IsTrue(
			LargeLadInventoryRules.CanReload(
				isHost: true,
				ownerRequest: true,
				LargeLadRole.SkinnyKid,
				isDead: false,
				isAlreadyReloading: false,
				state ) );

		state = LargeLadInventoryRules.CompleteReload( state );
		Assert.AreEqual( 8, state.Magazine );
		Assert.AreEqual( 0, state.Reserve );
		Assert.IsTrue( state.HasInfiniteReserve );
	}

	[TestMethod]
	public void CoreSwitching_PreservesEachMagazine()
	{
		var core = new List<LargeLadWeaponState>();
		LargeLadInventoryRules.TryAddCoreWeapon(
			core,
			LargeLadWeaponId.Pistol );
		LargeLadInventoryRules.TryAddCoreWeapon(
			core,
			LargeLadWeaponId.Smg );
		var pistol = core[0];
		pistol.Magazine = 3;
		core[0] = pistol;

		var smgSelection =
			LargeLadWeaponSelection.ForCore( LargeLadWeaponId.Smg );
		var pistolSelection =
			LargeLadWeaponSelection.ForCore( LargeLadWeaponId.Pistol );

		Assert.AreEqual(
			1,
			LargeLadInventoryRules.GetSelectionIndex(
				core,
				default,
				smgSelection ) );
		Assert.AreEqual(
			0,
			LargeLadInventoryRules.GetSelectionIndex(
				core,
				default,
				pistolSelection ) );
		Assert.AreEqual( 3, core[0].Magazine );
	}

	[TestMethod]
	public void CorePickup_RemainsAvailableToDifferentPlayers()
	{
		var firstPlayer = new List<LargeLadWeaponState>();
		var secondPlayer = new List<LargeLadWeaponState>();

		Assert.IsTrue(
			LargeLadInventoryRules.TryAddCoreWeapon(
				firstPlayer,
				LargeLadWeaponId.Pistol ) );
		Assert.IsTrue(
			LargeLadInventoryRules.TryAddCoreWeapon(
				secondPlayer,
				LargeLadWeaponId.Pistol ) );
		Assert.AreEqual( 1, firstPlayer.Count );
		Assert.AreEqual( 1, secondPlayer.Count );
	}

	[TestMethod]
	public void ExclusiveInventory_AddsOneAndRejectsSecond()
	{
		var first = CreateExclusiveState( instanceId: 101 );
		var second = CreateExclusiveState( instanceId: 102 );

		Assert.IsTrue(
			LargeLadInventoryRules.CanAcceptExclusive(
				LargeLadRole.SkinnyKid,
				isDead: false,
				alreadyHasExclusive: false,
				pickupAvailable: true,
				first ) );
		Assert.IsFalse(
			LargeLadInventoryRules.CanAcceptExclusive(
				LargeLadRole.SkinnyKid,
				isDead: false,
				alreadyHasExclusive: true,
				pickupAvailable: true,
				second ) );
	}

	[TestMethod]
	public void ExclusiveInstance_IdentityAndAmmoSurviveDrop()
	{
		var carrier = new object();
		var instance = CreateExclusiveInstance( instanceId: 321 );
		Assert.IsTrue(
			instance.TryCollectFromOrigin( carrier, out var state ) );
		state.Magazine = 3;
		state.Reserve = 11;

		Assert.IsTrue( instance.TryDrop( carrier, state ) );
		Assert.AreEqual( 321, instance.InstanceId );
		Assert.AreEqual( 321, instance.State.ExclusiveInstanceId );
		Assert.AreEqual( 3, instance.State.Magazine );
		Assert.AreEqual( 11, instance.State.Reserve );
		Assert.AreEqual(
			LargeLadExclusiveLocation.Dropped,
			instance.Location );
	}

	[TestMethod]
	public void ExclusiveTransfer_DoesNotRefillAmmunition()
	{
		var firstCarrier = new object();
		var secondCarrier = new object();
		var instance = CreateExclusiveInstance( instanceId: 654 );
		instance.TryCollectFromOrigin( firstCarrier, out var state );
		state.Magazine = 2;
		state.Reserve = 5;
		instance.TryDrop( firstCarrier, state );

		Assert.IsTrue(
			instance.TryCollectDropped(
				secondCarrier,
				out var transferred ) );
		Assert.AreEqual( 2, transferred.Magazine );
		Assert.AreEqual( 5, transferred.Reserve );
		Assert.AreEqual( 654, transferred.ExclusiveInstanceId );
	}

	[TestMethod]
	public void ExclusiveFiniteReload_PreservesTotalAmmunition()
	{
		var state = CreateExclusiveState( instanceId: 777 );
		state.Magazine = 2;
		state.Reserve = 7;
		var total = state.Magazine + state.Reserve;

		state = LargeLadInventoryRules.CompleteReload( state );

		Assert.AreEqual( 8, state.Magazine );
		Assert.AreEqual( 1, state.Reserve );
		Assert.AreEqual( total, state.Magazine + state.Reserve );
	}

	[TestMethod]
	public void ExclusiveRoundReset_RestoresConfiguredFullAmmo()
	{
		var carrier = new object();
		var instance = CreateExclusiveInstance( instanceId: 888 );
		instance.TryCollectFromOrigin( carrier, out var state );
		state.Magazine = 1;
		state.Reserve = 0;
		instance.TryDrop( carrier, state );

		instance.ResetForRound();

		Assert.AreEqual(
			LargeLadExclusiveLocation.OriginAvailable,
			instance.Location );
		Assert.AreEqual( 8, instance.State.Magazine );
		Assert.AreEqual( 18, instance.State.Reserve );
	}

	[TestMethod]
	public void ExclusiveDeath_DropsWithRemainingAmmunition()
	{
		var carrier = new object();
		var instance = CreateExclusiveInstance( instanceId: 901 );
		instance.TryCollectFromOrigin( carrier, out var state );
		state.Magazine = 4;
		state.Reserve = 9;

		Assert.IsTrue( instance.TryDrop( carrier, state ) );
		Assert.AreEqual(
			LargeLadExclusiveLocation.Dropped,
			instance.Location );
		Assert.AreEqual( 4, instance.State.Magazine );
		Assert.AreEqual( 9, instance.State.Reserve );
	}

	[TestMethod]
	public void ExclusiveDisconnect_CanRecoverToOriginWithoutReservation()
	{
		var carrier = new object();
		var instance = CreateExclusiveInstance( instanceId: 902 );
		instance.TryCollectFromOrigin( carrier, out var state );
		state.Magazine = 5;
		state.Reserve = 6;

		Assert.IsTrue(
			instance.ReturnCarrierToOrigin( carrier, state ) );
		Assert.AreEqual(
			LargeLadExclusiveLocation.OriginAvailable,
			instance.Location );
		Assert.IsNull( instance.Carrier );
		Assert.AreEqual( 5, instance.State.Magazine );
		Assert.AreEqual( 6, instance.State.Reserve );
	}

	[TestMethod]
	public void DroppingExclusive_WithRememberedCore_SelectsIt()
	{
		var core = new List<LargeLadWeaponState>();
		LargeLadInventoryRules.TryAddCoreWeapon(
			core,
			LargeLadWeaponId.Pistol );
		LargeLadInventoryRules.TryAddCoreWeapon(
			core,
			LargeLadWeaponId.Smg );

		var fallback = LargeLadInventoryRules.GetCoreFallback(
			core,
			LargeLadWeaponId.Smg );

		Assert.AreEqual(
			LargeLadWeaponSelectionKind.Core,
			fallback.Kind );
		Assert.AreEqual( LargeLadWeaponId.Smg, fallback.Weapon );
	}

	[TestMethod]
	public void DroppingExclusive_WithoutCore_SelectsRoleMelee()
	{
		var fallback = LargeLadInventoryRules.GetCoreFallback(
			new List<LargeLadWeaponState>(),
			LargeLadWeaponId.Pistol );

		Assert.AreEqual(
			LargeLadWeaponSelection.ForRoleMelee(),
			fallback );
	}

	[TestMethod]
	public void DroppingExclusive_CannotLeaveLivingSkinnyKidUnselected()
	{
		var exclusive = CreateExclusiveState( instanceId: 905 );
		var exclusiveSelection =
			LargeLadInventoryRules.SelectionFor( exclusive );

		Assert.IsTrue(
			LargeLadInventoryRules.CanDropExclusive(
				isHost: true,
				ownerRequest: true,
				LargeLadRole.SkinnyKid,
				isDead: false,
				exclusive,
				exclusiveSelection ) );

		var fallback = LargeLadInventoryRules.GetCoreFallback(
			new List<LargeLadWeaponState>
			{
				LargeLadWeaponState.CreateCore(
					LargeLadWeaponId.Pistol )
			},
			LargeLadWeaponId.Smg );

		Assert.AreNotEqual(
			LargeLadWeaponSelectionKind.None,
			fallback.Kind );
		Assert.AreEqual(
			LargeLadWeaponSelection.ForRoleMelee(),
			fallback );
	}

	[TestMethod]
	public void Ordering_IsCatalogStableWithExclusiveLast()
	{
		var core = new List<LargeLadWeaponState>();
		LargeLadInventoryRules.TryAddCoreWeapon(
			core,
			LargeLadWeaponId.Smg );
		LargeLadInventoryRules.TryAddCoreWeapon(
			core,
			LargeLadWeaponId.Pistol );
		var exclusive = CreateExclusiveState( instanceId: 903 );

		Assert.AreEqual( LargeLadWeaponId.Pistol, core[0].Weapon );
		Assert.AreEqual( LargeLadWeaponId.Smg, core[1].Weapon );
		Assert.IsTrue(
			LargeLadInventoryRules.TryGetWeaponAt(
				core,
				exclusive,
				2,
				out var last ) );
		Assert.AreEqual( exclusive, last );
	}

	[TestMethod]
	public void RoleRestrictions_AllowOnlyLivingSkinnyKids()
	{
		foreach ( var role in System.Enum.GetValues<LargeLadRole>() )
		{
			Assert.AreEqual(
				role == LargeLadRole.SkinnyKid,
				LargeLadInventoryRules.CanUseFirearmInventory(
					role,
					isDead: false ),
				role.ToString() );
		}

		Assert.IsFalse(
			LargeLadInventoryRules.CanUseFirearmInventory(
				LargeLadRole.SkinnyKid,
				isDead: true ) );
	}

	[TestMethod]
	public void HostValidation_RejectsInvalidPickupReloadSelectionAndDrop()
	{
		var core = new List<LargeLadWeaponState>();
		var coreState =
			LargeLadWeaponState.CreateCore( LargeLadWeaponId.Pistol );
		coreState.Magazine = 1;
		var exclusive = CreateExclusiveState( instanceId: 904 );
		var exclusiveSelection =
			LargeLadInventoryRules.SelectionFor( exclusive );

		Assert.IsFalse(
			LargeLadInventoryRules.CanCollectCore(
				isHost: false,
				LargeLadRole.SkinnyKid,
				isDead: false,
				core,
				LargeLadWeaponId.Pistol ) );
		Assert.IsFalse(
			LargeLadInventoryRules.CanReload(
				isHost: true,
				ownerRequest: false,
				LargeLadRole.SkinnyKid,
				isDead: false,
				isAlreadyReloading: false,
				coreState ) );
		Assert.IsFalse(
			LargeLadInventoryRules.CanSelect(
				isHost: false,
				ownerRequest: true,
				LargeLadRole.SkinnyKid,
				isDead: false,
				coreState ) );
		Assert.IsFalse(
			LargeLadInventoryRules.CanDropExclusive(
				isHost: true,
				ownerRequest: false,
				LargeLadRole.SkinnyKid,
				isDead: false,
				exclusive,
				exclusiveSelection ) );
		Assert.IsFalse(
			LargeLadInventoryRules.CanDropExclusive(
				isHost: true,
				ownerRequest: true,
				LargeLadRole.SkinnyKid,
				isDead: false,
				exclusive,
				LargeLadWeaponSelection.ForCore(
					LargeLadWeaponId.Pistol ) ) );
	}

	private static LargeLadWeaponState CreateExclusiveState(
		int instanceId )
	{
		return LargeLadWeaponState.CreateExclusive(
			LargeLadWeaponId.Pistol,
			instanceId,
			magazine: 8,
			reserve: 18 );
	}

	private static LargeLadExclusiveInstance CreateExclusiveInstance(
		int instanceId )
	{
		return new LargeLadExclusiveInstance(
			instanceId,
			LargeLadWeaponId.Pistol,
			fullMagazine: 8,
			fullReserve: 18 );
	}
}

[TestClass]
public sealed class RoleProfileRulesTests
{
	[TestMethod]
	public void SelectRoleProfile_UsesEachRolesValues()
	{
		var skinnyKid = new LargeLadRoleProfile { WalkSpeed = 110.0f };
		var largeLad = new LargeLadRoleProfile { WalkSpeed = 85.0f };
		var minion = new LargeLadRoleProfile { WalkSpeed = 100.0f };

		Assert.AreSame(
			skinnyKid,
			LargeLadGameplayRules.SelectRoleProfile(
				LargeLadRole.SkinnyKid,
				skinnyKid,
				largeLad,
				minion ) );
		Assert.AreSame(
			largeLad,
			LargeLadGameplayRules.SelectRoleProfile(
				LargeLadRole.LargeLad,
				skinnyKid,
				largeLad,
				minion ) );
		Assert.AreSame(
			minion,
			LargeLadGameplayRules.SelectRoleProfile(
				LargeLadRole.Minion,
				skinnyKid,
				largeLad,
				minion ) );
		Assert.AreSame(
			skinnyKid,
			LargeLadGameplayRules.SelectRoleProfile(
				LargeLadRole.Unassigned,
				skinnyKid,
				largeLad,
				minion ),
			"Unassigned players use the Skinny Kid baseline." );

		Assert.AreEqual(
			85.0f,
			LargeLadGameplayRules.SelectRoleProfile(
				LargeLadRole.LargeLad,
				skinnyKid,
				largeLad,
				minion ).WalkSpeed );
	}
}
