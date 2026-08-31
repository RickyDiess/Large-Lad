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
	public void ColliderFirst_SameTargetHeadHitboxStillClassifiesHead()
	{
		var candidates = new[]
		{
			new LargeLadFirearmHitboxCandidate(
				BelongsToSelectedTarget: true,
				HasHitbox: false,
				Distance: 100.0f,
				HitboxBoneName: null,
				HasHeadHitboxTag: false ),
			new LargeLadFirearmHitboxCandidate(
				BelongsToSelectedTarget: true,
				HasHitbox: true,
				Distance: 110.0f,
				HitboxBoneName: "head",
				HasHeadHitboxTag: false )
		};

		Assert.AreEqual(
			LargeLadHitRegion.Head,
			LargeLadFirearmHitRules.ResolveSelectedTargetHitRegion(
				candidates,
				maximumClassificationDistance: 150.0f ) );
	}

	[TestMethod]
	public void ColliderFirst_SameTargetBodyHitboxClassifiesBody()
	{
		var candidates = new[]
		{
			new LargeLadFirearmHitboxCandidate(
				BelongsToSelectedTarget: true,
				HasHitbox: false,
				Distance: 100.0f,
				HitboxBoneName: null,
				HasHeadHitboxTag: false ),
			new LargeLadFirearmHitboxCandidate(
				BelongsToSelectedTarget: true,
				HasHitbox: true,
				Distance: 110.0f,
				HitboxBoneName: "spine_2",
				HasHeadHitboxTag: false )
		};

		Assert.AreEqual(
			LargeLadHitRegion.Body,
			LargeLadFirearmHitRules.ResolveSelectedTargetHitRegion(
				candidates,
				maximumClassificationDistance: 150.0f ) );
	}

	[TestMethod]
	public void SameTargetBodyBeforeHead_ClassifiesBody()
	{
		var candidates = new[]
		{
			new LargeLadFirearmHitboxCandidate(
				BelongsToSelectedTarget: true,
				HasHitbox: true,
				Distance: 90.0f,
				HitboxBoneName: "spine_2",
				HasHeadHitboxTag: false ),
			new LargeLadFirearmHitboxCandidate(
				BelongsToSelectedTarget: true,
				HasHitbox: true,
				Distance: 110.0f,
				HitboxBoneName: "head",
				HasHeadHitboxTag: true )
		};

		Assert.AreEqual(
			LargeLadHitRegion.Body,
			LargeLadFirearmHitRules.ResolveSelectedTargetHitRegion(
				candidates,
				maximumClassificationDistance: 150.0f ) );
	}

	[TestMethod]
	public void SameTargetHeadBeforeBody_ClassifiesHead()
	{
		var candidates = new[]
		{
			new LargeLadFirearmHitboxCandidate(
				BelongsToSelectedTarget: true,
				HasHitbox: true,
				Distance: 90.0f,
				HitboxBoneName: "head",
				HasHeadHitboxTag: true ),
			new LargeLadFirearmHitboxCandidate(
				BelongsToSelectedTarget: true,
				HasHitbox: true,
				Distance: 110.0f,
				HitboxBoneName: "spine_2",
				HasHeadHitboxTag: false )
		};

		Assert.AreEqual(
			LargeLadHitRegion.Head,
			LargeLadFirearmHitRules.ResolveSelectedTargetHitRegion(
				candidates,
				maximumClassificationDistance: 150.0f ) );
	}

	[DataTestMethod]
	[DataRow( 70.0f )]
	[DataRow( 130.0f )]
	public void AlignedSecondPlayerHead_CannotPromoteSelectedVictim(
		float otherPlayerDistance )
	{
		var candidates = new[]
		{
			new LargeLadFirearmHitboxCandidate(
				BelongsToSelectedTarget: true,
				HasHitbox: true,
				Distance: 90.0f,
				HitboxBoneName: "spine_2",
				HasHeadHitboxTag: false ),
			new LargeLadFirearmHitboxCandidate(
				BelongsToSelectedTarget: false,
				HasHitbox: true,
				Distance: otherPlayerDistance,
				HitboxBoneName: "head",
				HasHeadHitboxTag: true )
		};

		Assert.AreEqual(
			LargeLadHitRegion.Body,
			LargeLadFirearmHitRules.ResolveSelectedTargetHitRegion(
				candidates,
				maximumClassificationDistance: 150.0f ) );
	}

	[TestMethod]
	public void UnsortedCandidates_SelectSmallestValidDistance()
	{
		var candidates = new[]
		{
			new LargeLadFirearmHitboxCandidate(
				BelongsToSelectedTarget: true,
				HasHitbox: true,
				Distance: 120.0f,
				HitboxBoneName: "head",
				HasHeadHitboxTag: true ),
			new LargeLadFirearmHitboxCandidate(
				BelongsToSelectedTarget: true,
				HasHitbox: false,
				Distance: 60.0f,
				HitboxBoneName: null,
				HasHeadHitboxTag: false ),
			new LargeLadFirearmHitboxCandidate(
				BelongsToSelectedTarget: true,
				HasHitbox: true,
				Distance: 80.0f,
				HitboxBoneName: "spine_2",
				HasHeadHitboxTag: false )
		};

		Assert.AreEqual(
			LargeLadHitRegion.Body,
			LargeLadFirearmHitRules.ResolveSelectedTargetHitRegion(
				candidates,
				maximumClassificationDistance: 150.0f ) );
	}

	[TestMethod]
	public void InvalidCandidateDistances_AreIgnored()
	{
		var candidates = new[]
		{
			new LargeLadFirearmHitboxCandidate(
				true, true, float.NaN, "head", true ),
			new LargeLadFirearmHitboxCandidate(
				true, true, float.PositiveInfinity, "head", true ),
			new LargeLadFirearmHitboxCandidate(
				true, true, -1.0f, "head", true ),
			new LargeLadFirearmHitboxCandidate(
				true, true, 90.0f, "spine_2", false )
		};

		Assert.AreEqual(
			LargeLadHitRegion.Body,
			LargeLadFirearmHitRules.ResolveSelectedTargetHitRegion(
				candidates,
				maximumClassificationDistance: 150.0f ) );
	}

	[TestMethod]
	public void SameTargetHeadBehindObstructionBoundary_ClassifiesBody()
	{
		var candidates = new[]
		{
			new LargeLadFirearmHitboxCandidate(
				BelongsToSelectedTarget: true,
				HasHitbox: true,
				Distance: 151.0f,
				HitboxBoneName: "head",
				HasHeadHitboxTag: true )
		};

		Assert.AreEqual(
			LargeLadHitRegion.Body,
			LargeLadFirearmHitRules.ResolveSelectedTargetHitRegion(
				candidates,
				maximumClassificationDistance: 150.0f ) );
	}

	[TestMethod]
	public void MissingValidHeadHitbox_DefaultsToBody()
	{
		Assert.AreEqual(
			LargeLadHitRegion.Body,
			LargeLadFirearmHitRules.ResolveSelectedTargetHitRegion(
				new LargeLadFirearmHitboxCandidate[0],
				maximumClassificationDistance: 150.0f ) );
	}

	[DataTestMethod]
	[DataRow( LargeLadWeaponId.Pistol )]
	[DataRow( LargeLadWeaponId.Smg )]
	[DataRow( LargeLadWeaponId.Shotgun )]
	[DataRow( LargeLadWeaponId.Rifle )]
	public void EveryCoreFirearmHeadshot_ConsumesAllCurrentMinionHealth(
		LargeLadWeaponId weapon )
	{
		var damage = LargeLadFirearmHitRules.ResolveIncomingDamage(
			LargeLadRole.Minion,
			isLiving: true,
			weapon,
			LargeLadDamageType.Firearm,
			LargeLadHitRegion.Head,
			currentHealth: 125.0f,
			ordinaryIncomingDamage: 25.0f );

		Assert.AreEqual( 125.0f, damage );
		Assert.AreEqual(
			LargeLadKillfeedCause.FirearmHeadshot,
			LargeLadFirearmHitRules.GetKillfeedCause(
				weapon,
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
	public void DuplicateShotRequest_CannotDamageFeedbackOrReportLethalTwice()
	{
		var gate = new LargeLadFirearmShotRequestGate();
		var health = 100.0f;
		var hasReportedLethalTransition = false;
		var appliedDamageEvents = 0;
		var feedbackResults = 0;
		var lethalEvents = 0;

		void ResolveHeadshot( int shotSequence )
		{
			if ( !gate.TryConsume( shotSequence ) )
				return;

			feedbackResults++;
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
		Assert.AreEqual( 1, feedbackResults );
		Assert.AreEqual( 1, lethalEvents );
		Assert.AreEqual( 0.0f, health );
	}

	[TestMethod]
	public void ShotgunPellets_ClassifyIndependentlyAndCommitAtMostOneDeath()
	{
		var gate = new LargeLadFirearmShotRequestGate();
		var health = 100.0f;
		var hasReportedLethalTransition = false;
		var classifiedRegions = new System.Collections.Generic.List<LargeLadHitRegion>();
		var appliedPellets = 0;
		var lethalEvents = 0;

		void ResolveShot( int sequence )
		{
			if ( !gate.TryConsume( sequence ) )
				return;

			foreach ( var hasHeadTag in new[] { false, true, false } )
			{
				var region = LargeLadFirearmHitRules.ClassifyHitRegion(
					hasHeadTag ? "head" : "spine_2",
					hasHeadTag );
				classifiedRegions.Add( region );

				if ( health <= 0.0f )
					continue;

				var previousHealth = health;
				var damage = LargeLadFirearmHitRules.ResolveIncomingDamage(
					LargeLadRole.Minion,
					isLiving: true,
					LargeLadWeaponId.Shotgun,
					LargeLadDamageType.Firearm,
					region,
					health,
					ordinaryIncomingDamage: 15.0f );
				health = System.MathF.Max( 0.0f, health - damage );
				appliedPellets++;

				if ( !LargeLadGameplayRules.IsNewLethalTransition(
					previousHealth,
					health,
					hasReportedLethalTransition ) )
				{
					continue;
				}

				hasReportedLethalTransition = true;
				lethalEvents++;
			}
		}

		ResolveShot( 52 );
		ResolveShot( 52 );

		CollectionAssert.AreEqual(
			new[]
			{
				LargeLadHitRegion.Body,
				LargeLadHitRegion.Head,
				LargeLadHitRegion.Body
			},
			classifiedRegions.ToArray() );
		Assert.AreEqual( 2, appliedPellets );
		Assert.AreEqual( 1, lethalEvents );
		Assert.AreEqual( 0.0f, health );
	}
}
