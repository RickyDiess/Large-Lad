using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox;

[TestClass]
public sealed class LargeLadGroundSlamRulesTests
{
	[TestMethod]
	public void PlayerEligibility_AffectsLivingVisibleSkinnyKidsAndMinionsOnly()
	{
		Assert.IsTrue( LargeLadGroundSlamRules.CanAffectPlayer(
			LargeLadRole.SkinnyKid,
			isLivingAndActive: true,
			isObstructed: false,
			distance: 180.0f,
			radius: 180.0f ) );
		Assert.IsTrue( LargeLadGroundSlamRules.CanAffectPlayer(
			LargeLadRole.Minion,
			isLivingAndActive: true,
			isObstructed: false,
			distance: 40.0f,
			radius: 180.0f ) );
		Assert.IsFalse( LargeLadGroundSlamRules.CanAffectPlayer(
			LargeLadRole.LargeLad,
			isLivingAndActive: true,
			isObstructed: false,
			distance: 40.0f,
			radius: 180.0f ) );
	}

	[TestMethod]
	public void PlayerEligibility_RejectsObstructionDeathAndOutOfRange()
	{
		Assert.IsFalse( LargeLadGroundSlamRules.CanAffectPlayer(
			LargeLadRole.SkinnyKid,
			isLivingAndActive: true,
			isObstructed: true,
			distance: 20.0f,
			radius: 180.0f ) );
		Assert.IsFalse( LargeLadGroundSlamRules.CanAffectPlayer(
			LargeLadRole.SkinnyKid,
			isLivingAndActive: false,
			isObstructed: false,
			distance: 20.0f,
			radius: 180.0f ) );
		Assert.IsFalse( LargeLadGroundSlamRules.CanAffectPlayer(
			LargeLadRole.SkinnyKid,
			isLivingAndActive: true,
			isObstructed: false,
			distance: 180.01f,
			radius: 180.0f ) );
	}

	[TestMethod]
	public void PlayerEffect_StaggersOnlySkinnyKids()
	{
		Assert.IsTrue( LargeLadGroundSlamRules.ShouldStaggerPlayer(
			LargeLadRole.SkinnyKid ) );
		Assert.IsFalse( LargeLadGroundSlamRules.ShouldStaggerPlayer(
			LargeLadRole.Minion ) );
	}

	[TestMethod]
	public void RadialImpulse_IsUpwardDirectionalAndBounded()
	{
		var impulse = LargeLadGroundSlamRules.GetRadialImpulse(
			Vector3.Zero,
			new Vector3( 10.0f, 0.0f, -100.0f ),
			float.PositiveInfinity,
			LargeLadGroundSlamRules.MaximumUpwardImpulse + 1.0f,
			usePositiveXWhenCoincident: false );

		Assert.AreEqual( 0.0f, impulse.x, 0.001f );
		Assert.AreEqual( 0.0f, impulse.y, 0.001f );
		Assert.AreEqual(
			LargeLadGroundSlamRules.MaximumUpwardImpulse,
			impulse.z,
			0.001f );

		var directional = LargeLadGroundSlamRules.GetRadialImpulse(
			Vector3.Zero,
			new Vector3( 10.0f, 0.0f, 500.0f ),
			100.0f,
			50.0f,
			usePositiveXWhenCoincident: false );
		Assert.AreEqual( 100.0f, directional.x, 0.001f );
		Assert.AreEqual( 50.0f, directional.z, 0.001f );
	}

	[TestMethod]
	public void ReactiveProps_RequireOptInAndRejectProtectedObjects()
	{
		Assert.IsFalse( LargeLadGroundSlamRules.CanReactivePropReact(
			isExplicitlyDesignated: false,
			isCriticalGameplayObject: false,
			isAuthoritativeBlocker: false,
			LargeLadGroundSlamPropBehavior.Move,
			hasUsableRigidbody: true ) );
		Assert.IsFalse( LargeLadGroundSlamRules.CanReactivePropReact(
			isExplicitlyDesignated: true,
			isCriticalGameplayObject: true,
			isAuthoritativeBlocker: false,
			LargeLadGroundSlamPropBehavior.Break,
			hasUsableRigidbody: true ) );
		Assert.IsFalse( LargeLadGroundSlamRules.CanReactivePropReact(
			isExplicitlyDesignated: true,
			isCriticalGameplayObject: false,
			isAuthoritativeBlocker: true,
			LargeLadGroundSlamPropBehavior.Unanchor,
			hasUsableRigidbody: true ) );
	}

	[TestMethod]
	public void ReactiveProps_MoveAndUnanchorNeedPhysicsButBreakDoesNot()
	{
		Assert.IsFalse( LargeLadGroundSlamRules.CanReactivePropReact(
			isExplicitlyDesignated: true,
			isCriticalGameplayObject: false,
			isAuthoritativeBlocker: false,
			LargeLadGroundSlamPropBehavior.Move,
			hasUsableRigidbody: false ) );
		Assert.IsTrue( LargeLadGroundSlamRules.CanReactivePropReact(
			isExplicitlyDesignated: true,
			isCriticalGameplayObject: false,
			isAuthoritativeBlocker: false,
			LargeLadGroundSlamPropBehavior.Break,
			hasUsableRigidbody: false ) );
	}
}
