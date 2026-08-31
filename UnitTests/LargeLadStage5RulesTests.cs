using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;

[TestClass]
public sealed class LargeLadCombatAttributionRulesTests
{
	[DataTestMethod]
	[DataRow( LargeLadDamageType.Firearm )]
	[DataRow( LargeLadDamageType.Melee )]
	[DataRow( LargeLadDamageType.Eat )]
	[DataRow( LargeLadDamageType.Dodgeball )]
	public void DirectHostileDamage_ReceivesKillCredit(
		LargeLadDamageType damageType )
	{
		var victimRole = damageType == LargeLadDamageType.Eat
			? LargeLadRole.SkinnyKid
			: LargeLadRole.Minion;
		var attackerRole = damageType == LargeLadDamageType.Eat
			? LargeLadRole.LargeLad
			: LargeLadRole.SkinnyKid;

		Assert.IsTrue(
			LargeLadCombatAttributionRules.IsDirectKillCreditEligible(
				"victim",
				victimRole,
				"attacker",
				attackerRole,
				damageType,
				25.0f ) );
	}

	[TestMethod]
	public void PureEnvironment_HasNoDirectPlayerCredit()
	{
		Assert.IsFalse(
			LargeLadCombatAttributionRules.IsDirectKillCreditEligible(
				"victim",
				LargeLadRole.LargeLad,
				"attacker",
				LargeLadRole.SkinnyKid,
				LargeLadDamageType.Environment,
				100.0f ) );
	}

	[TestMethod]
	public void RecentEnvironmentCredit_UsesMostRecentContributor()
	{
		var contributions = new[]
		{
			Contribution( "victim", "older", 2.0f ),
			Contribution( "victim", "newer", 6.5f )
		};

		var killer = LargeLadCombatAttributionRules
			.ResolveEnvironmentalKiller(
				contributions,
				roundSequenceId: 4,
				now: 7.0f );

		Assert.IsTrue( killer.HasValue );
		Assert.AreEqual( "newer", killer.Value.AttackerSessionIdentity );
	}

	[TestMethod]
	public void StaleEnvironmentInfluence_IsRejected()
	{
		var killer = LargeLadCombatAttributionRules
			.ResolveEnvironmentalKiller(
				new[] { Contribution( "victim", "attacker", 1.0f ) },
				roundSequenceId: 4,
				now: 8.01f );

		Assert.IsFalse( killer.HasValue );
	}

	[DataTestMethod]
	[DataRow( "same", LargeLadRole.SkinnyKid, "same", LargeLadRole.Minion )]
	[DataRow( "victim", LargeLadRole.SkinnyKid, "friend", LargeLadRole.SkinnyKid )]
	[DataRow( "victim", LargeLadRole.LargeLad, "friend", LargeLadRole.Minion )]
	public void SelfAndFriendlyInfluence_IsRejected(
		string victim,
		LargeLadRole victimRole,
		string attacker,
		LargeLadRole attackerRole )
	{
		Assert.IsFalse(
			LargeLadCombatAttributionRules.IsValidContribution(
				victim,
				victimRole,
				attacker,
				attackerRole,
				10.0f ) );
	}

	[TestMethod]
	public void MultipleHitsFromOneAttacker_ProduceOneAssist()
	{
		var store = new LargeLadRecentDamageStore();
		store.Record( Contribution( "victim", "assistant", 2.0f, 10.0f ) );
		store.Record( Contribution( "victim", "assistant", 5.0f, 15.0f ) );
		var contributions = store.Consume( "victim", 4, 6.0f );
		var assistants = LargeLadCombatAttributionRules
			.ResolveAssistantIdentities(
				contributions,
				"victim",
				"killer",
				4,
				6.0f );

		Assert.AreEqual( 1, contributions.Count );
		Assert.AreEqual( 25.0f, contributions[0].TotalAppliedDamage );
		CollectionAssert.AreEqual( new[] { "assistant" }, assistants.ToArray() );
	}

	[TestMethod]
	public void AssistResolution_ExcludesKillerAndIncludesEachOtherAttackerOnce()
	{
		var contributions = new[]
		{
			Contribution( "victim", "killer", 5.0f ),
			Contribution( "victim", "assist-b", 4.0f ),
			Contribution( "victim", "assist-a", 3.0f )
		};

		var assistants = LargeLadCombatAttributionRules
			.ResolveAssistantIdentities(
				contributions,
				"victim",
				"killer",
				4,
				6.0f );

		CollectionAssert.AreEqual(
			new[] { "assist-a", "assist-b" },
			assistants.ToArray() );
	}

	[TestMethod]
	public void StaleAndPreviousRoundContributions_DoNotAssist()
	{
		var contributions = new[]
		{
			Contribution( "victim", "stale", 1.0f ),
			Contribution( "victim", "old-round", 7.0f ) with
			{
				RoundSequenceId = 3
			}
		};

		Assert.AreEqual(
			0,
			LargeLadCombatAttributionRules.ResolveAssistantIdentities(
				contributions,
				"victim",
				"killer",
				4,
				8.01f ).Count );
	}

	[TestMethod]
	public void CommittedDeath_ConsumesHistoryBetweenLives()
	{
		var store = new LargeLadRecentDamageStore();
		store.Record( Contribution( "victim", "assistant", 5.0f ) );

		Assert.AreEqual( 1, store.Consume( "victim", 4, 6.0f ).Count );
		Assert.AreEqual(
			0,
			store.Consume( "victim", 4, 6.1f ).Count,
			"A second death/Eat query cannot reuse previous-life assistance." );
	}

	[TestMethod]
	public void RoundReset_ClearsEveryContribution()
	{
		var store = new LargeLadRecentDamageStore();
		store.Record( Contribution( "victim-a", "attacker", 5.0f ) );
		store.Record( Contribution( "victim-b", "attacker", 5.0f ) );

		store.Clear();

		Assert.AreEqual( 0, store.VictimBucketCount );
		Assert.AreEqual( 0, store.Consume( "victim-a", 4, 6.0f ).Count );
	}

	private static LargeLadDamageContribution Contribution(
		string victim,
		string attacker,
		float time,
		float damage = 10.0f )
	{
		return new LargeLadDamageContribution(
			victim,
			LargeLadRole.LargeLad,
			attacker,
			LargeLadRole.SkinnyKid,
			RoundSequenceId: 4,
			time,
			damage,
			LargeLadDamageType.Firearm,
			LargeLadWeaponId.Pistol );
	}
}

[TestClass]
public sealed class LargeLadKillfeedPresentationRulesTests
{
	[DataTestMethod]
	[DataRow( LargeLadKillfeedCause.Firearm, "SHOT" )]
	[DataRow( LargeLadKillfeedCause.FirearmHeadshot, "HEADSHOT" )]
	[DataRow( LargeLadKillfeedCause.Eat, "ATE" )]
	public void PlayerKillCause_UsesSentenceFriendlyVerb(
		LargeLadKillfeedCause cause,
		string expected )
	{
		Assert.AreEqual(
			expected,
			LargeLadKillfeedPresentationRules.GetCauseLabel( cause ) );
	}
}

[TestClass]
public sealed class LargeLadCareerStatRulesTests
{
	[TestMethod]
	public void V1Catalog_ContainsTheExactTwentySevenUniqueIdentifiers()
	{
		var expected = new[]
		{
			"rounds_played", "skinny_rounds_played",
			"large_lad_rounds_played", "skinny_kid_wins",
			"large_lad_wins", "minion_wins",
			"last_skinny_kid_survivals", "perfect_large_lad_wins",
			"kills", "assists", "deaths", "headshot_kills",
			"skinny_kids_eaten", "large_lad_kills", "minion_kills",
			"skinny_kid_deaths", "large_lad_deaths", "minion_deaths",
			"conversions", "pistol_kills", "smg_kills", "shotgun_kills",
			"rifle_kills", "melee_kills", "dodgeball_kills",
			"barricades_destroyed", "shortcuts_destroyed"
		};

		Assert.AreEqual( 27, LargeLadStatIds.All.Count );
		CollectionAssert.AreEquivalent( expected, LargeLadStatIds.All.ToArray() );
		Assert.AreEqual( 27, LargeLadStatIds.All.Distinct().Count() );
	}

	[TestMethod]
	public void LocalStatsDelivery_RequiresRealOwnerKnownIdentAndPositiveAmount()
	{
		Assert.IsTrue(
			LargeLadCareerStatRules.CanSubmitInLocalServiceContext(
				isDedicatedServer: false,
				isOwnedByLocalPlayer: true,
				LargeLadStatIds.Kills,
				amount: 1 ) );
		Assert.IsFalse(
			LargeLadCareerStatRules.CanSubmitInLocalServiceContext(
				isDedicatedServer: true,
				isOwnedByLocalPlayer: true,
				LargeLadStatIds.Kills,
				amount: 1 ),
			"A dedicated process is never a career-stat recipient." );
		Assert.IsFalse(
			LargeLadCareerStatRules.CanSubmitInLocalServiceContext(
				isDedicatedServer: false,
				isOwnedByLocalPlayer: false,
				LargeLadStatIds.Kills,
				amount: 1 ) );
		Assert.IsFalse(
			LargeLadCareerStatRules.CanSubmitInLocalServiceContext(
				isDedicatedServer: false,
				isOwnedByLocalPlayer: true,
				"client_claimed_stat",
				amount: 1 ) );
		Assert.IsFalse(
			LargeLadCareerStatRules.CanSubmitInLocalServiceContext(
				isDedicatedServer: false,
				isOwnedByLocalPlayer: true,
				LargeLadStatIds.Kills,
				amount: 0 ) );
	}

	[TestMethod]
	public void EatKill_EarnsGeneralRoleAndEatCountersOnly()
	{
		AssertDeltas(
			LargeLadCareerStatRules.GetKillerDeltas( Death(
				LargeLadRole.LargeLad,
				LargeLadRole.SkinnyKid,
				LargeLadDamageType.Eat,
				LargeLadWeaponId.Melee,
				LargeLadHitRegion.None,
				wasEatExecution: true ) ),
			LargeLadStatIds.Kills,
			LargeLadStatIds.LargeLadKills,
			LargeLadStatIds.SkinnyKidsEaten );
	}

	[TestMethod]
	public void PistolHeadshot_EarnsExactMethodAndHeadshotCounters()
	{
		AssertDeltas(
			LargeLadCareerStatRules.GetKillerDeltas( Death(
				LargeLadRole.SkinnyKid,
				LargeLadRole.Minion,
				LargeLadDamageType.Firearm,
				LargeLadWeaponId.Pistol,
				LargeLadHitRegion.Head ) ),
			LargeLadStatIds.Kills,
			LargeLadStatIds.PistolKills,
			LargeLadStatIds.HeadshotKills );
	}

	[DataTestMethod]
	[DataRow( LargeLadWeaponId.Pistol, "pistol_kills" )]
	[DataRow( LargeLadWeaponId.Smg, "smg_kills" )]
	[DataRow( LargeLadWeaponId.Shotgun, "shotgun_kills" )]
	[DataRow( LargeLadWeaponId.Rifle, "rifle_kills" )]
	public void EveryCoreFirearm_EarnsItsExactMethodCounter(
		LargeLadWeaponId weapon,
		string expectedMethodStat )
	{
		AssertDeltas(
			LargeLadCareerStatRules.GetKillerDeltas( Death(
				LargeLadRole.SkinnyKid,
				LargeLadRole.Minion,
				LargeLadDamageType.Firearm,
				weapon,
				LargeLadHitRegion.Body ) ),
			LargeLadStatIds.Kills,
			expectedMethodStat );
	}

	[TestMethod]
	public void MinionMelee_EarnsGeneralRoleAndMethodCounters()
	{
		AssertDeltas(
			LargeLadCareerStatRules.GetKillerDeltas( Death(
				LargeLadRole.Minion,
				LargeLadRole.SkinnyKid,
				LargeLadDamageType.Melee,
				LargeLadWeaponId.Melee,
				LargeLadHitRegion.None ) ),
			LargeLadStatIds.Kills,
			LargeLadStatIds.MinionKills,
			LargeLadStatIds.MeleeKills );
	}

	[TestMethod]
	public void InheritedEnvironmentKill_DoesNotReuseOldWeaponMethod()
	{
		AssertDeltas(
			LargeLadCareerStatRules.GetKillerDeltas( Death(
				LargeLadRole.SkinnyKid,
				LargeLadRole.LargeLad,
				LargeLadDamageType.Environment,
				LargeLadWeaponId.Pistol,
				LargeLadHitRegion.Head,
				wasEnvironmentalInfluenceKill: true ) ),
			LargeLadStatIds.Kills );
	}

	[TestMethod]
	public void PureEnvironmentDeath_EarnsNoKillerCounters()
	{
		var death = new LargeLadDeathRecord
		{
			VictimSessionIdentity = "victim",
			VictimRole = LargeLadRole.LargeLad,
			DamageType = LargeLadDamageType.Environment,
			KillfeedCause = LargeLadKillfeedCause.Environment
		};

		Assert.AreEqual(
			0,
			LargeLadCareerStatRules.GetKillerDeltas( death ).Count );
	}

	[TestMethod]
	public void SkinnyDeathConversion_EarnsDeathRoleAndConversionCounters()
	{
		var death = Death(
			LargeLadRole.LargeLad,
			LargeLadRole.SkinnyKid,
			LargeLadDamageType.Eat,
			LargeLadWeaponId.Melee,
			LargeLadHitRegion.None,
			wasEatExecution: true );
		death = new LargeLadDeathRecord
		{
			CreditedKillerSessionIdentity = death.CreditedKillerSessionIdentity,
			CreditedKillerRole = death.CreditedKillerRole,
			VictimRole = death.VictimRole,
			DamageType = death.DamageType,
			SourceWeapon = death.SourceWeapon,
			WasEatExecution = death.WasEatExecution,
			ConvertedToMinion = true
		};

		AssertDeltas(
			LargeLadCareerStatRules.GetVictimDeltas( death ),
			LargeLadStatIds.Deaths,
			LargeLadStatIds.SkinnyKidDeaths,
			LargeLadStatIds.Conversions );
	}

	[TestMethod]
	public void FullRoundParticipation_IsRequiredForOutcomeDeltas()
	{
		var lateJoiner = Participant(
			LargeLadRole.SkinnyKid,
			LargeLadRole.SkinnyKid,
			wasStarter: false,
			connected: true,
			living: true );
		var disconnectedStarter = Participant(
			LargeLadRole.SkinnyKid,
			LargeLadRole.SkinnyKid,
			wasStarter: true,
			connected: false,
			living: true );

		Assert.AreEqual( 0, RoundDeltas(
			LargeLadWinner.SkinnyKids,
			lateJoiner ).Count );
		Assert.AreEqual( 0, RoundDeltas(
			LargeLadWinner.SkinnyKids,
			disconnectedStarter ).Count );
	}

	[TestMethod]
	public void LargeLadParticipation_RequiresCommittedSelectionIdentity()
	{
		var uncommittedProspect = Participant(
			LargeLadRole.LargeLad,
			LargeLadRole.LargeLad,
			isCommittedLargeLad: false );

		Assert.IsFalse( RoundDeltas(
			LargeLadWinner.LargeLadTeam,
			uncommittedProspect ).Any( delta =>
				delta.Identifier ==
					LargeLadStatIds.LargeLadRoundsPlayed ) );
	}

	[TestMethod]
	public void SkinnySurvivorWin_UsesStartingAndEndingFacts()
	{
		AssertDeltas(
			RoundDeltas(
				LargeLadWinner.SkinnyKids,
				Participant(
					LargeLadRole.SkinnyKid,
					LargeLadRole.SkinnyKid,
					living: true ) ),
			LargeLadStatIds.RoundsPlayed,
			LargeLadStatIds.SkinnyRoundsPlayed,
			LargeLadStatIds.SkinnyKidWins );
	}

	[TestMethod]
	public void ConvertedStarterGetsMinionWinNotSkinnyWin()
	{
		AssertDeltas(
			RoundDeltas(
				LargeLadWinner.LargeLadTeam,
				Participant(
					LargeLadRole.SkinnyKid,
					LargeLadRole.Minion,
					living: true ) ),
			LargeLadStatIds.RoundsPlayed,
			LargeLadStatIds.SkinnyRoundsPlayed,
			LargeLadStatIds.MinionWins );
	}

	[TestMethod]
	public void LargeLadZeroDeathWin_IsPerfect()
	{
		AssertDeltas(
			RoundDeltas(
				LargeLadWinner.LargeLadTeam,
				Participant(
					LargeLadRole.LargeLad,
					LargeLadRole.LargeLad,
					living: true,
					isCommittedLargeLad: true ),
				largeLadDeaths: 0 ),
			LargeLadStatIds.RoundsPlayed,
			LargeLadStatIds.LargeLadRoundsPlayed,
			LargeLadStatIds.LargeLadWins,
			LargeLadStatIds.PerfectLargeLadWins );
	}

	[TestMethod]
	public void LargeLadDeathThenWin_IsNotPerfect()
	{
		AssertDeltas(
			RoundDeltas(
				LargeLadWinner.LargeLadTeam,
				Participant(
					LargeLadRole.LargeLad,
					LargeLadRole.LargeLad,
					living: true,
					isCommittedLargeLad: true ),
				largeLadDeaths: 1 ),
			LargeLadStatIds.RoundsPlayed,
			LargeLadStatIds.LargeLadRoundsPlayed,
			LargeLadStatIds.LargeLadWins );
	}

	[TestMethod]
	public void LastSkinnyKidMustStillBeLivingForSurvivalStat()
	{
		AssertDeltas(
			RoundDeltas(
				LargeLadWinner.SkinnyKids,
				Participant(
					LargeLadRole.SkinnyKid,
					LargeLadRole.SkinnyKid,
					living: true,
					becameLastSkinnyKid: true ) ),
			LargeLadStatIds.RoundsPlayed,
			LargeLadStatIds.SkinnyRoundsPlayed,
			LargeLadStatIds.SkinnyKidWins,
			LargeLadStatIds.LastSkinnyKidSurvivals );

		var laterDied = Participant(
			LargeLadRole.SkinnyKid,
			LargeLadRole.Minion,
			living: false,
			becameLastSkinnyKid: true );
		Assert.IsFalse( RoundDeltas(
			LargeLadWinner.LargeLadTeam,
			laterDied ).Any( delta =>
				delta.Identifier == LargeLadStatIds.LastSkinnyKidSurvivals ) );
	}

	[TestMethod]
	public void AbortedRoundAndDuplicateCompletion_DoNotCommit()
	{
		var gate = new LargeLadRoundOutcomeCommitGate();

		Assert.IsFalse( gate.TryCommit( roundCompletedSuccessfully: false ) );
		Assert.IsTrue( gate.TryCommit( roundCompletedSuccessfully: true ) );
		Assert.IsFalse( gate.TryCommit( roundCompletedSuccessfully: true ) );
		gate.Abort();
		Assert.IsFalse( gate.HasCommitted );
	}

	[DataTestMethod]
	[DataRow(
		LargeLadBarricadeMode.SkinnyProgression,
		LargeLadRole.SkinnyKid,
		LargeLadStatIds.BarricadesDestroyed )]
	[DataRow(
		LargeLadBarricadeMode.LadShortcut,
		LargeLadRole.LargeLad,
		LargeLadStatIds.ShortcutsDestroyed )]
	public void FinalBarricadeDestruction_EarnsTheMatchingCounter(
		LargeLadBarricadeMode mode,
		LargeLadRole role,
		string expected )
	{
		AssertDeltas(
			LargeLadCareerStatRules.GetBarricadeDestructionDeltas(
				mode,
				role,
				isFinalAuthoritativeDestruction: true ),
			expected );
	}

	[TestMethod]
	public void NonFinalOrInvalidBarricadeAction_EarnsNothing()
	{
		Assert.AreEqual(
			0,
			LargeLadCareerStatRules.GetBarricadeDestructionDeltas(
				LargeLadBarricadeMode.SkinnyProgression,
				LargeLadRole.SkinnyKid,
				isFinalAuthoritativeDestruction: false ).Count );
		Assert.AreEqual(
			0,
			LargeLadCareerStatRules.GetBarricadeDestructionDeltas(
				LargeLadBarricadeMode.LadShortcut,
				LargeLadRole.Minion,
				isFinalAuthoritativeDestruction: true ).Count );
	}

	private static LargeLadDeathRecord Death(
		LargeLadRole killerRole,
		LargeLadRole victimRole,
		LargeLadDamageType damageType,
		LargeLadWeaponId sourceWeapon,
		LargeLadHitRegion hitRegion,
		bool wasEatExecution = false,
		bool wasEnvironmentalInfluenceKill = false )
	{
		return new LargeLadDeathRecord
		{
			CreditedKillerSessionIdentity = "killer",
			CreditedKillerRole = killerRole,
			VictimSessionIdentity = "victim",
			VictimRole = victimRole,
			DamageType = damageType,
			SourceWeapon = sourceWeapon,
			HitRegion = hitRegion,
			KillfeedCause = LargeLadFirearmHitRules.GetKillfeedCause(
				sourceWeapon,
				damageType,
				hitRegion ),
			WasEatExecution = wasEatExecution,
			WasEnvironmentalInfluenceKill =
				wasEnvironmentalInfluenceKill
		};
	}

	private static LargeLadRoundParticipantOutcome Participant(
		LargeLadRole startingRole,
		LargeLadRole endingRole,
		bool wasStarter = true,
		bool connected = true,
		bool living = false,
		bool isCommittedLargeLad = false,
		bool becameLastSkinnyKid = false )
	{
		return new LargeLadRoundParticipantOutcome(
			"player",
			wasStarter,
			connected,
			startingRole,
			endingRole,
			living,
			isCommittedLargeLad,
			becameLastSkinnyKid );
	}

	private static System.Collections.Generic.IReadOnlyList<LargeLadStatDelta>
		RoundDeltas(
			LargeLadWinner winner,
			LargeLadRoundParticipantOutcome participant,
			int largeLadDeaths = 0 )
	{
		return LargeLadCareerStatRules.GetRoundOutcomeDeltas(
			winner,
			roundCompletedSuccessfully: true,
			largeLadDeaths,
			participant );
	}

	private static void AssertDeltas(
		System.Collections.Generic.IEnumerable<LargeLadStatDelta> actual,
		params string[] expected )
	{
		CollectionAssert.AreEquivalent(
			expected,
			actual.Select( delta => delta.Identifier ).ToArray() );
	}
}
