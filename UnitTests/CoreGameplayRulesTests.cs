using Microsoft.VisualStudio.TestTools.UnitTesting;

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
				LargeLadRole.SkinnyKid,
				LargeLadRole.SkinnyKid ) );
	}

	[TestMethod]
	public void LargeLadDeath_PreservesLargeLadRole()
	{
		Assert.AreEqual(
			LargeLadRole.LargeLad,
			LargeLadGameplayRules.ResolveRespawnRole(
				LargeLadRole.LargeLad,
				LargeLadRole.LargeLad ) );
	}

	[TestMethod]
	public void MinionDeath_RespawnsAsMinion()
	{
		Assert.AreEqual(
			LargeLadRole.Minion,
			LargeLadGameplayRules.ResolveRespawnRole(
				LargeLadRole.Minion,
				LargeLadRole.Minion ) );
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
