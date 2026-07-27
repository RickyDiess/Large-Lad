using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;

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
public sealed class SpawnRulesTests
{
	[TestMethod]
	public void DeterministicLayout_IdenticalInputsProduceIdenticalOffsets()
	{
		const int desiredCount = 16;
		const float radius = 160.0f;

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

	[TestMethod]
	public void BatchAllocation_MustContainEveryRequestedPlayer()
	{
		var firstPlayer = new object();
		var secondPlayer = new object();
		var requested = new[] { firstPlayer, secondPlayer };
		var incomplete = new Dictionary<object, LargeLadSpawnLocation>
		{
			[firstPlayer] = default
		};
		var complete = new Dictionary<object, LargeLadSpawnLocation>
		{
			[firstPlayer] = default,
			[secondPlayer] = default
		};

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
	public void SkinnyProgression_AllowsOnlySkinnyKidWeapons()
	{
		foreach ( var role in System.Enum.GetValues<LargeLadRole>() )
		{
			foreach ( var damageType in
				System.Enum.GetValues<LargeLadDamageType>() )
			{
				var expected =
					role == LargeLadRole.SkinnyKid &&
					damageType is LargeLadDamageType.Firearm or
						LargeLadDamageType.Melee;

				Assert.AreEqual(
					expected,
					LargeLadGameplayRules.CanDamageBarricade(
						LargeLadBarricadeMode.SkinnyProgression,
						role,
						damageType ),
					$"{role} using {damageType}" );
			}
		}
	}

	[TestMethod]
	public void LadShortcut_AllowsOnlyLargeLadMelee()
	{
		foreach ( var role in System.Enum.GetValues<LargeLadRole>() )
		{
			foreach ( var damageType in
				System.Enum.GetValues<LargeLadDamageType>() )
			{
				var expected =
					role == LargeLadRole.LargeLad &&
					damageType == LargeLadDamageType.Melee;

				Assert.AreEqual(
					expected,
					LargeLadGameplayRules.CanDamageBarricade(
						LargeLadBarricadeMode.LadShortcut,
						role,
						damageType ),
					$"{role} using {damageType}" );
			}
		}
	}
}

[TestClass]
public sealed class InventoryRulesTests
{
	[TestMethod]
	public void Grant_UsesFirstEmptySlot()
	{
		Assert.AreEqual(
			3,
			LargeLadGameplayRules.FindWeaponGrantSlot(
				LargeLadWeaponId.Smg,
				LargeLadWeaponId.Melee,
				LargeLadWeaponId.Pistol,
				LargeLadWeaponId.None,
				LargeLadWeaponId.None ) );
	}

	[TestMethod]
	public void Grant_RejectsDuplicateWeapon()
	{
		Assert.AreEqual(
			0,
			LargeLadGameplayRules.FindWeaponGrantSlot(
				LargeLadWeaponId.Pistol,
				LargeLadWeaponId.Melee,
				LargeLadWeaponId.Pistol,
				LargeLadWeaponId.None,
				LargeLadWeaponId.None ) );
	}

	[TestMethod]
	public void Grant_RejectsFullInventory()
	{
		Assert.AreEqual(
			0,
			LargeLadGameplayRules.FindWeaponGrantSlot(
				LargeLadWeaponId.Smg,
				LargeLadWeaponId.Melee,
				LargeLadWeaponId.Pistol,
				LargeLadWeaponId.Melee,
				LargeLadWeaponId.Pistol ) );
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
