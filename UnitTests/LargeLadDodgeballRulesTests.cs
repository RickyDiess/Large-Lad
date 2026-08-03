using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox;

[TestClass]
public sealed class LargeLadDodgeballLifecycleRulesTests
{
	[TestMethod]
	public void Throw_AtomicallyMovesOneCarriedInstanceIntoWorld()
	{
		var firstCarrier = new object();
		var secondCarrier = new object();
		var instance = new LargeLadUtilityInstance( 4101 );

		Assert.IsTrue(
			instance.TryCollectFromOrigin( firstCarrier, out var state ) );
		Assert.IsTrue(
			instance.TryThrow( firstCarrier, state, out var throwSequence ) );
		Assert.IsTrue( throwSequence > 0 );
		Assert.AreEqual( throwSequence, instance.ActiveThrowSequence );
		Assert.AreEqual( LargeLadUtilityLocation.Thrown, instance.Location );
		Assert.IsNull( instance.Carrier );

		Assert.IsFalse(
			instance.TryThrow( firstCarrier, state, out _ ),
			"The carried reservation was consumed by the first throw." );
		Assert.IsFalse(
			instance.TryDrop( firstCarrier, state ),
			"A stale manual-drop request cannot create another world state." );

		Assert.IsTrue(
			instance.TryCollectDropped( secondCarrier, out var transferred ) );
		Assert.AreEqual( state, transferred );
		Assert.AreSame( secondCarrier, instance.Carrier );
		Assert.AreEqual( LargeLadUtilityLocation.Carried, instance.Location );
		Assert.AreEqual( 0, instance.ActiveThrowSequence );
	}

	[TestMethod]
	public void Reset_InvalidatesStaleThrowWithoutReusingItsToken()
	{
		var carrier = new object();
		var instance = new LargeLadUtilityInstance( 4102 );
		instance.TryCollectFromOrigin( carrier, out var state );
		instance.TryThrow( carrier, state, out var staleThrow );

		instance.ResetForRound();

		Assert.AreEqual(
			LargeLadUtilityLocation.OriginAvailable,
			instance.Location );
		Assert.AreEqual( 0, instance.ActiveThrowSequence );
		Assert.IsFalse( instance.TrySettleThrow( staleThrow ) );

		instance.TryCollectFromOrigin( carrier, out state );
		instance.TryThrow( carrier, state, out var newThrow );
		Assert.IsTrue( newThrow > staleThrow );
		Assert.IsFalse( instance.TrySettleThrow( staleThrow ) );
		Assert.AreEqual( LargeLadUtilityLocation.Thrown, instance.Location );
		Assert.IsTrue( instance.TrySettleThrow( newThrow ) );
		Assert.AreEqual( LargeLadUtilityLocation.Dropped, instance.Location );
	}

	[TestMethod]
	public void ImpactGate_ConsumesOnlyFirstCallbackForActiveThrow()
	{
		var gate = new LargeLadDodgeballImpactGate();
		gate.BeginThrow( 17 );

		Assert.IsFalse( gate.TryConsume( 16 ), "Stale throw callback." );
		Assert.IsTrue( gate.TryConsume( 17 ) );
		Assert.IsFalse( gate.TryConsume( 17 ), "Repeated physics callback." );

		gate.BeginThrow( 18 );
		Assert.IsFalse( gate.TryConsume( 17 ), "Prior throw after rethrow." );
		Assert.IsTrue( gate.TryConsume( 18 ) );
	}

	[TestMethod]
	public void PickupCooldown_UsesInclusiveAuthoritativeBoundary()
	{
		Assert.IsFalse(
			LargeLadDodgeballRules.CanPickup(
				LargeLadUtilityLocation.Thrown,
				available: true,
				now: 9.999f,
				pickupUnlockTime: 10.0f ) );
		Assert.IsTrue(
			LargeLadDodgeballRules.CanPickup(
				LargeLadUtilityLocation.Thrown,
				available: true,
				now: 10.0f,
				pickupUnlockTime: 10.0f ) );
		Assert.IsFalse(
			LargeLadDodgeballRules.CanPickup(
				LargeLadUtilityLocation.Carried,
				available: true,
				now: 11.0f,
				pickupUnlockTime: 10.0f ) );
		Assert.IsFalse(
			LargeLadDodgeballRules.CanPickup(
				LargeLadUtilityLocation.Dropped,
				available: false,
				now: 11.0f,
				pickupUnlockTime: 10.0f ) );
	}
}

[TestClass]
public sealed class LargeLadDodgeballCombatRulesTests
{
	[TestMethod]
	public void DirectLivingHunterHits_SelectDedicatedOutcomes()
	{
		var minion = ResolveImpact( LargeLadRole.Minion );
		var largeLad = ResolveImpact( LargeLadRole.LargeLad );

		Assert.IsTrue( minion.ConsumesThrow );
		Assert.AreEqual(
			LargeLadDodgeballHitOutcome.MinionKill,
			minion.Outcome );
		Assert.IsTrue( largeLad.ConsumesThrow );
		Assert.AreEqual(
			LargeLadDodgeballHitOutcome.LargeLadHit,
			largeLad.Outcome );
	}

	[TestMethod]
	public void FriendlyLowSpeedAndInvalidHits_ConsumeWithoutCombatEffect()
	{
		var friendly = ResolveImpact( LargeLadRole.SkinnyKid );
		var lowSpeed = LargeLadDodgeballRules.ResolveImpact(
			LargeLadUtilityLocation.Thrown,
			impactAlreadyConsumed: false,
			hitThrowerDuringGrace: false,
			LargeLadRole.SkinnyKid,
			LargeLadRole.Minion,
			targetIsLiving: true,
			impactSpeed: 349.99f,
			minimumCombatSpeed: 350.0f );
		var invalidThrower = LargeLadDodgeballRules.ResolveImpact(
			LargeLadUtilityLocation.Thrown,
			impactAlreadyConsumed: false,
			hitThrowerDuringGrace: false,
			LargeLadRole.Minion,
			LargeLadRole.Minion,
			targetIsLiving: true,
			impactSpeed: 800.0f,
			minimumCombatSpeed: 350.0f );

		foreach ( var decision in new[] { friendly, lowSpeed, invalidThrower } )
		{
			Assert.IsTrue( decision.ConsumesThrow );
			Assert.IsFalse( decision.AppliesCombatEffect );
		}
	}

	[TestMethod]
	public void ProtectedSelfContact_DoesNotConsumeThrow()
	{
		var decision = LargeLadDodgeballRules.ResolveImpact(
			LargeLadUtilityLocation.Thrown,
			impactAlreadyConsumed: false,
			hitThrowerDuringGrace: true,
			LargeLadRole.SkinnyKid,
			LargeLadRole.SkinnyKid,
			targetIsLiving: true,
			impactSpeed: 1000.0f,
			minimumCombatSpeed: 350.0f );

		Assert.IsFalse( decision.ConsumesThrow );
		Assert.IsFalse( decision.AppliesCombatEffect );
	}

	[TestMethod]
	public void DuplicateOrSettledImpact_CannotSelectCombatEffect()
	{
		var duplicate = LargeLadDodgeballRules.ResolveImpact(
			LargeLadUtilityLocation.Thrown,
			impactAlreadyConsumed: true,
			hitThrowerDuringGrace: false,
			LargeLadRole.SkinnyKid,
			LargeLadRole.Minion,
			targetIsLiving: true,
			impactSpeed: 1000.0f,
			minimumCombatSpeed: 350.0f );
		var settled = LargeLadDodgeballRules.ResolveImpact(
			LargeLadUtilityLocation.Dropped,
			impactAlreadyConsumed: false,
			hitThrowerDuringGrace: false,
			LargeLadRole.SkinnyKid,
			LargeLadRole.Minion,
			targetIsLiving: true,
			impactSpeed: 1000.0f,
			minimumCombatSpeed: 350.0f );

		Assert.IsFalse( duplicate.ConsumesThrow );
		Assert.IsFalse( settled.ConsumesThrow );
	}

	[TestMethod]
	public void MinionDodgeballDamage_ConsumesCurrentHealthThroughModifiers()
	{
		var damage = LargeLadDamageRules.ResolveIncomingDamage(
			LargeLadRole.Minion,
			isLiving: true,
			isLastSkinnyKid: false,
			LargeLadWeaponId.None,
			LargeLadDamageType.Dodgeball,
			LargeLadHitRegion.None,
			requestsExecution: false,
			currentHealth: 83.0f,
			baseDamage: 0.0f,
			incomingDamageMultiplier: 0.01f );

		Assert.AreEqual( 83.0f, damage );
		Assert.AreEqual(
			LargeLadKillfeedCause.Dodgeball,
			LargeLadFirearmHitRules.GetKillfeedCause(
				LargeLadWeaponId.None,
				LargeLadDamageType.Dodgeball,
				LargeLadHitRegion.None ) );
	}

	[TestMethod]
	public void LargeLadDamageAndKnockback_AreAwayFromThrowerAndBounded()
	{
		Assert.AreEqual(
			0.0f,
			LargeLadDodgeballRules.GetLargeLadDamage( -10.0f ) );
		Assert.AreEqual(
			LargeLadDodgeballRules.MaximumLargeLadDamage,
			LargeLadDodgeballRules.GetLargeLadDamage( 100.0f ) );

		var awayFromThrower = new Vector3( -3.0f, 4.0f, 500.0f );
		var impulse = LargeLadDodgeballRules.GetLargeLadKnockbackImpulse(
			awayFromThrower,
			Vector3.Right,
			configuredHorizontalImpulse: float.MaxValue,
			configuredUpwardImpulse: float.MaxValue );
		var horizontalImpulse = new Vector3( impulse.x, impulse.y, 0.0f );
		var horizontalAwayFromThrower = new Vector3(
			awayFromThrower.x,
			awayFromThrower.y,
			0.0f );

		Assert.AreEqual(
			LargeLadDodgeballRules.MaximumHorizontalKnockbackImpulse,
			horizontalImpulse.Length,
			0.01f );
		Assert.AreEqual(
			1.0f,
			Vector3.Dot(
				horizontalImpulse.Normal,
				horizontalAwayFromThrower.Normal ),
			0.0001f );
		Assert.AreEqual(
			LargeLadDodgeballRules.MaximumUpwardKnockbackImpulse,
			impulse.z,
			0.01f );
	}

	[DataTestMethod]
	[DataRow( LargeLadRole.Unassigned, false )]
	[DataRow( LargeLadRole.SkinnyKid, false )]
	[DataRow( LargeLadRole.LargeLad, true )]
	[DataRow( LargeLadRole.Minion, true )]
	public void SolidBall_PlayerCollisionMatchesRole(
		LargeLadRole role,
		bool expectedCollision )
	{
		Assert.AreEqual(
			expectedCollision,
			LargeLadDodgeballRules.HasSolidPlayerCollision( role ) );
	}

	[TestMethod]
	public void PhysicsVelocity_IsClampedAndVentNeverUsesMinionException()
	{
		var velocity = LargeLadDodgeballRules.GetThrowVelocity(
			Vector3.Forward,
			configuredSpeed: float.MaxValue,
			inheritedVelocity: Vector3.Forward * 10000.0f );

		Assert.AreEqual(
			LargeLadDodgeballRules.MaximumLinearSpeed,
			velocity.Length,
			0.01f );
		Assert.IsFalse(
			LargeLadGameplayRules.HasMinionPassageCollisionException(
				LargeLadDodgeballRules.CollisionTag ) );
	}

	private static LargeLadDodgeballImpactDecision ResolveImpact(
		LargeLadRole targetRole )
	{
		return LargeLadDodgeballRules.ResolveImpact(
			LargeLadUtilityLocation.Thrown,
			impactAlreadyConsumed: false,
			hitThrowerDuringGrace: false,
			LargeLadRole.SkinnyKid,
			targetRole,
			targetIsLiving: true,
			impactSpeed: 800.0f,
			minimumCombatSpeed: 350.0f );
	}
}
