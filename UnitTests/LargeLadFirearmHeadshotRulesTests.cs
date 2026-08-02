using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public sealed class LargeLadFirearmHeadshotRulesTests
{
	[TestMethod]
	public void HeadHitboxClassification_UsesTagOrCitizenHeadBone()
	{
		Assert.AreEqual(
			LargeLadHitRegion.Head,
			LargeLadFirearmHitRules.ClassifyHitRegion(
				hitboxBoneName: null,
				hasHeadHitboxTag: true ) );
		Assert.AreEqual(
			LargeLadHitRegion.Head,
			LargeLadFirearmHitRules.ClassifyHitRegion(
				hitboxBoneName: "head",
				hasHeadHitboxTag: false ) );
		Assert.AreEqual(
			LargeLadHitRegion.Body,
			LargeLadFirearmHitRules.ClassifyHitRegion(
				hitboxBoneName: "spine_2",
				hasHeadHitboxTag: false ) );
	}

	[TestMethod]
	public void MinionFirearmHeadshot_ConsumesAllCurrentHealth()
	{
		var damage = LargeLadFirearmHitRules.ResolveIncomingDamage(
			LargeLadRole.Minion,
			isLiving: true,
			LargeLadWeaponId.Smg,
			LargeLadDamageType.Firearm,
			LargeLadHitRegion.Head,
			currentHealth: 125.0f,
			ordinaryIncomingDamage: 25.0f );

		Assert.AreEqual( 125.0f, damage );
		Assert.AreEqual(
			LargeLadKillfeedCause.FirearmHeadshot,
			LargeLadFirearmHitRules.GetKillfeedCause(
				LargeLadWeaponId.Smg,
				LargeLadDamageType.Firearm,
				LargeLadHitRegion.Head ) );
	}

	[TestMethod]
	public void MinionFirearmBodyShot_KeepsWeaponDefinedDamage()
	{
		var damage = LargeLadFirearmHitRules.ResolveIncomingDamage(
			LargeLadRole.Minion,
			isLiving: true,
			LargeLadWeaponId.Smg,
			LargeLadDamageType.Firearm,
			LargeLadHitRegion.Body,
			currentHealth: 125.0f,
			ordinaryIncomingDamage: 25.0f );

		Assert.AreEqual( 25.0f, damage );
		Assert.AreEqual(
			LargeLadKillfeedCause.Firearm,
			LargeLadFirearmHitRules.GetKillfeedCause(
				LargeLadWeaponId.Smg,
				LargeLadDamageType.Firearm,
				LargeLadHitRegion.Body ) );
	}

	[DataTestMethod]
	[DataRow( LargeLadRole.LargeLad )]
	[DataRow( LargeLadRole.SkinnyKid )]
	public void NonMinionFirearmHeadshot_KeepsOrdinaryDamage(
		LargeLadRole victimRole )
	{
		var damage = LargeLadFirearmHitRules.ResolveIncomingDamage(
			victimRole,
			isLiving: true,
			LargeLadWeaponId.Pistol,
			LargeLadDamageType.Firearm,
			LargeLadHitRegion.Head,
			currentHealth: 500.0f,
			ordinaryIncomingDamage: 100.0f );

		Assert.AreEqual( 100.0f, damage );
	}

	[TestMethod]
	public void NonFirearmHeadHit_DoesNotUseMinionHeadshotRule()
	{
		var damage = LargeLadFirearmHitRules.ResolveIncomingDamage(
			LargeLadRole.Minion,
			isLiving: true,
			LargeLadWeaponId.Melee,
			LargeLadDamageType.Melee,
			LargeLadHitRegion.Head,
			currentHealth: 125.0f,
			ordinaryIncomingDamage: 25.0f );

		Assert.AreEqual( 25.0f, damage );
		Assert.IsFalse(
			LargeLadFirearmHitRules.IsUniversalLethalMinionHeadshot(
				LargeLadRole.Minion,
				isLiving: true,
				LargeLadWeaponId.Melee,
				LargeLadDamageType.Melee,
				LargeLadHitRegion.Head ) );
	}

	[TestMethod]
	public void DuplicateShotRequest_CannotApplyDamageOrReportLethalTwice()
	{
		var gate = new LargeLadFirearmShotRequestGate();
		var health = 100.0f;
		var hasReportedLethalTransition = false;
		var appliedDamageEvents = 0;
		var lethalEvents = 0;

		void ResolveHeadshot( int shotSequence )
		{
			if ( !gate.TryConsume( shotSequence ) )
				return;

			appliedDamageEvents++;
			var previousHealth = health;
			var damage = LargeLadFirearmHitRules.ResolveIncomingDamage(
				LargeLadRole.Minion,
				isLiving: health > 0.0f,
				LargeLadWeaponId.Smg,
				LargeLadDamageType.Firearm,
				LargeLadHitRegion.Head,
				health,
				ordinaryIncomingDamage: 25.0f );
			health = System.MathF.Max( 0.0f, health - damage );

			if ( !LargeLadGameplayRules.IsNewLethalTransition(
				previousHealth,
				health,
				hasReportedLethalTransition ) )
			{
				return;
			}

			hasReportedLethalTransition = true;
			lethalEvents++;
		}

		ResolveHeadshot( 41 );
		ResolveHeadshot( 41 );

		Assert.AreEqual( 1, appliedDamageEvents );
		Assert.AreEqual( 1, lethalEvents );
		Assert.AreEqual( 0.0f, health );
	}
}
