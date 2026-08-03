using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public sealed class LargeLadSkinnyKidRegenerationRulesTests
{
	[TestMethod]
	public void Defaults_UseFiveSecondDelayAndFixedSeventyFivePercentCap()
	{
		Assert.AreEqual(
			5.0f,
			LargeLadSkinnyKidSurvivabilityRules.DefaultRegenerationDelay );
		Assert.AreEqual(
			75.0f,
			LargeLadSkinnyKidSurvivabilityRules.GetRegenerationCap(
				maximumHealth: 100.0f ) );
	}

	[TestMethod]
	public void BeforeDelay_NoHealthIsRestored()
	{
		Assert.AreEqual(
			40.0f,
			Regenerate(
				currentHealth: 40.0f,
				secondsSinceLastDamage: 4.999f,
				deltaTime: 0.1f,
				rate: 10.0f ) );
	}

	[TestMethod]
	public void CrossingDelay_UsesOnlyPostDelayPartOfFrame()
	{
		Assert.AreEqual(
			40.5f,
			Regenerate(
				currentHealth: 40.0f,
				secondsSinceLastDamage: 5.05f,
				deltaTime: 0.1f,
				rate: 10.0f ),
			0.0001f );
	}

	[TestMethod]
	public void AfterDelay_HealthRestoresGraduallyAtConfiguredRate()
	{
		Assert.AreEqual(
			41.0f,
			Regenerate(
				currentHealth: 40.0f,
				secondsSinceLastDamage: 6.0f,
				deltaTime: 0.1f,
				rate: 10.0f ),
			0.0001f );
	}

	[TestMethod]
	public void Regeneration_StopsExactlyAtSeventyFivePercentOfCurrentMaximum()
	{
		Assert.AreEqual(
			150.0f,
			LargeLadSkinnyKidSurvivabilityRules.GetRegeneratedHealth(
				LargeLadRole.SkinnyKid,
				isLiving: true,
				isRoundActive: true,
				currentHealth: 149.5f,
				maximumHealth: 200.0f,
				secondsSinceLastDamage: 6.0f,
				deltaTime: 1.0f,
				regenerationDelay: 5.0f,
				regenerationRate: 20.0f ) );
	}

	[DataTestMethod]
	[DataRow( LargeLadRole.LargeLad, true, true )]
	[DataRow( LargeLadRole.Minion, true, true )]
	[DataRow( LargeLadRole.SkinnyKid, false, true )]
	[DataRow( LargeLadRole.SkinnyKid, true, false )]
	public void Regeneration_RejectsOtherRolesDeathAndInactiveRounds(
		LargeLadRole role,
		bool isLiving,
		bool isRoundActive )
	{
		Assert.AreEqual(
			40.0f,
			LargeLadSkinnyKidSurvivabilityRules.GetRegeneratedHealth(
				role,
				isLiving,
				isRoundActive,
				currentHealth: 40.0f,
				maximumHealth: 100.0f,
				secondsSinceLastDamage: 10.0f,
				deltaTime: 1.0f,
				regenerationDelay: 5.0f,
				regenerationRate: 10.0f ) );
	}

	private static float Regenerate(
		float currentHealth,
		float secondsSinceLastDamage,
		float deltaTime,
		float rate )
	{
		return LargeLadSkinnyKidSurvivabilityRules.GetRegeneratedHealth(
			LargeLadRole.SkinnyKid,
			isLiving: true,
			isRoundActive: true,
			currentHealth,
			maximumHealth: 100.0f,
			secondsSinceLastDamage,
			deltaTime,
			regenerationDelay: 5.0f,
			regenerationRate: rate );
	}
}

[TestClass]
public sealed class LargeLadLastSkinnyKidRulesTests
{
	[DataTestMethod]
	[DataRow( LargeLadDamageType.Firearm )]
	[DataRow( LargeLadDamageType.Melee )]
	[DataRow( LargeLadDamageType.Environment )]
	public void OrdinaryIncomingDamage_IsReducedByExactlyFiftyPercent(
		LargeLadDamageType damageType )
	{
		Assert.AreEqual(
			40.0f,
			LargeLadSkinnyKidSurvivabilityRules
				.ApplyLastSkinnyKidDamageReduction(
					LargeLadRole.SkinnyKid,
					isLastSkinnyKid: true,
					damageType,
					isExplicitExecution: false,
					ordinaryIncomingDamage: 80.0f ) );
	}

	[TestMethod]
	public void EatExecutionDamage_BypassesLastSkinnyKidProtection()
	{
		Assert.AreEqual(
			80.0f,
			LargeLadSkinnyKidSurvivabilityRules
				.ApplyLastSkinnyKidDamageReduction(
					LargeLadRole.SkinnyKid,
					isLastSkinnyKid: true,
					LargeLadDamageType.Eat,
					isExplicitExecution: true,
					ordinaryIncomingDamage: 80.0f ) );
	}

	[TestMethod]
	public void OrdinaryEnvironmentalDamage_IsReducedForLastSkinnyKid()
	{
		var damage = LargeLadDamageRules.ResolveIncomingDamage(
			LargeLadRole.SkinnyKid,
			isLiving: true,
			isLastSkinnyKid: true,
			LargeLadWeaponId.None,
			LargeLadDamageType.Environment,
			LargeLadHitRegion.None,
			requestsExecution: false,
			currentHealth: 100.0f,
			baseDamage: 80.0f,
			incomingDamageMultiplier: 1.0f );

		Assert.AreEqual( 40.0f, damage );
	}

	[DataTestMethod]
	[DataRow( true )]
	[DataRow( false )]
	public void KillVolumeExecution_OutsideEatConsumesCurrentHealthWithEnvironmentalCauseOnce(
		bool isLastSkinnyKid )
	{
		var context = new LargeLadDamageContext
		{
			DamageType = LargeLadDamageType.Environment,
			IsExecution = true,
			BaseDamage = 1.0f
		};
		var health = 73.0f;
		var hasReportedLethalTransition = false;
		var lethalTransitions = 0;

		void ApplyExecution()
		{
			var resolvedDamage = LargeLadDamageRules.ResolveIncomingDamage(
				LargeLadRole.SkinnyKid,
				isLiving: health > 0.0f,
				isLastSkinnyKid,
				context.SourceWeapon,
				context.DamageType,
				context.HitRegion,
				context.IsExecution,
				health,
				context.BaseDamage,
				incomingDamageMultiplier: 0.5f );
			var damage = LargeLadEatRules.FilterDamageForEatCommit(
				LargeLadEatParticipation.None,
				context.DamageType,
				resolvedDamage,
				isAuthorizedEatExecution: false );
			var previousHealth = health;
			health = System.MathF.Max( 0.0f, health - damage );

			if ( !LargeLadGameplayRules.IsNewLethalTransition(
				previousHealth,
				health,
				hasReportedLethalTransition ) )
			{
				return;
			}

			hasReportedLethalTransition = true;
			lethalTransitions++;
		}

		ApplyExecution();
		ApplyExecution();

		Assert.IsTrue( context.IsExplicitExecution );
		Assert.AreEqual( LargeLadKillfeedCause.Environment, context.KillfeedCause );
		Assert.AreEqual( 0.0f, health );
		Assert.AreEqual( 1, lethalTransitions );
	}

	[TestMethod]
	public void NonLastSkinnyKid_KeepsOrdinaryEnvironmentalDamage()
	{
		var ordinaryDamage = LargeLadDamageRules.ResolveIncomingDamage(
			LargeLadRole.SkinnyKid,
			isLiving: true,
			isLastSkinnyKid: false,
			LargeLadWeaponId.None,
			LargeLadDamageType.Environment,
			LargeLadHitRegion.None,
			requestsExecution: false,
			currentHealth: 100.0f,
			baseDamage: 80.0f,
			incomingDamageMultiplier: 1.0f );
		var executionDamage = LargeLadDamageRules.ResolveIncomingDamage(
			LargeLadRole.SkinnyKid,
			isLiving: true,
			isLastSkinnyKid: false,
			LargeLadWeaponId.None,
			LargeLadDamageType.Environment,
			LargeLadHitRegion.None,
			requestsExecution: true,
			currentHealth: 100.0f,
			baseDamage: 1.0f,
			incomingDamageMultiplier: 1.0f );

		Assert.AreEqual( 80.0f, ordinaryDamage );
		Assert.AreEqual( 100.0f, executionDamage );
	}

	[TestMethod]
	public void EatExecution_ConsumesCurrentHealthAndKeepsEatCause()
	{
		var damage = LargeLadDamageRules.ResolveIncomingDamage(
			LargeLadRole.SkinnyKid,
			isLiving: true,
			isLastSkinnyKid: true,
			LargeLadWeaponId.Melee,
			LargeLadDamageType.Eat,
			LargeLadHitRegion.None,
			requestsExecution: true,
			currentHealth: 83.0f,
			baseDamage: 1.0f,
			incomingDamageMultiplier: 0.5f );

		Assert.AreEqual( 83.0f, damage );
		Assert.AreEqual(
			LargeLadKillfeedCause.Eat,
			LargeLadFirearmHitRules.GetKillfeedCause(
				LargeLadWeaponId.Melee,
				LargeLadDamageType.Eat,
				LargeLadHitRegion.None ) );
	}

	[DataTestMethod]
	[DataRow( LargeLadRole.SkinnyKid, false )]
	[DataRow( LargeLadRole.LargeLad, true )]
	[DataRow( LargeLadRole.Minion, true )]
	public void Protection_DoesNotAffectOtherPlayers(
		LargeLadRole role,
		bool isLastSkinnyKid )
	{
		Assert.AreEqual(
			80.0f,
			LargeLadSkinnyKidSurvivabilityRules
				.ApplyLastSkinnyKidDamageReduction(
					role,
					isLastSkinnyKid,
					LargeLadDamageType.Firearm,
					isExplicitExecution: false,
					ordinaryIncomingDamage: 80.0f ) );
	}

	[TestMethod]
	public void EffectiveLivingState_ExcludesDeathAndPendingConversion()
	{
		Assert.IsTrue(
			LargeLadSkinnyKidSurvivabilityRules
				.IsEffectiveLivingSkinnyKid(
					LargeLadRole.SkinnyKid,
					LargeLadRole.Unassigned,
					isDead: false,
					currentHealth: 1.0f ) );
		Assert.IsFalse(
			LargeLadSkinnyKidSurvivabilityRules
				.IsEffectiveLivingSkinnyKid(
					LargeLadRole.SkinnyKid,
					LargeLadRole.Unassigned,
					isDead: true,
					currentHealth: 0.0f ) );
		Assert.IsFalse(
			LargeLadSkinnyKidSurvivabilityRules
				.IsEffectiveLivingSkinnyKid(
					LargeLadRole.SkinnyKid,
					LargeLadRole.Minion,
					isDead: false,
					currentHealth: 100.0f ) );
	}

	[TestMethod]
	public void Announcement_FiresOnlyOnFirstTransitionToExactlyOne()
	{
		Assert.IsTrue(
			LargeLadSkinnyKidSurvivabilityRules
				.ShouldAnnounceLastSkinnyKid(
					isRoundActive: true,
					previousEffectiveLivingSkinnyKidCount: 2,
					currentEffectiveLivingSkinnyKidCount: 1,
					hasAlreadyAnnouncedThisRound: false ) );
		Assert.IsFalse(
			LargeLadSkinnyKidSurvivabilityRules
				.ShouldAnnounceLastSkinnyKid(
					isRoundActive: true,
					previousEffectiveLivingSkinnyKidCount: 1,
					currentEffectiveLivingSkinnyKidCount: 1,
					hasAlreadyAnnouncedThisRound: false ),
			"An unchanged death/respawn snapshot cannot announce twice." );
		Assert.IsFalse(
			LargeLadSkinnyKidSurvivabilityRules
				.ShouldAnnounceLastSkinnyKid(
					isRoundActive: true,
					previousEffectiveLivingSkinnyKidCount: 2,
					currentEffectiveLivingSkinnyKidCount: 1,
					hasAlreadyAnnouncedThisRound: true ),
			"The per-round latch prevents later lifecycle transitions from repeating it." );
	}
}
