using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox;
using System.Linq;

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

	[TestMethod]
	public void RejectedRequest_DoesNotStartLocalCooldown()
	{
		var host = new LargeLadGroundSlamHostState();
		var owner = new LargeLadGroundSlamOwnerState();
		var decision = host.EvaluateRequest(
			sequence: 1,
			canActivate: false,
			now: 10.0f,
			cooldown: 5.0f,
			cadenceTolerance: 0.025f );

		Assert.AreEqual(
			LargeLadGroundSlamHostRequestResult.Rejected,
			decision.Result );
		Assert.AreEqual( 0.0f, decision.CooldownRemaining, 0.001f );
		Assert.IsTrue( owner.ApplyHostResult(
			1,
			accepted: false,
			decision.CooldownEndTime,
			now: 20.0f ) );
		Assert.AreEqual( 0.0f, owner.GetCooldownRemaining( 20.0f ) );
		Assert.IsFalse( owner.HasCooldownPresentation );
		Assert.IsFalse( owner.CooldownReadyPresentationPending );
	}

	[TestMethod]
	public void AcceptedRequest_StartsSynchronizedOwnerCooldown()
	{
		var host = new LargeLadGroundSlamHostState();
		var owner = new LargeLadGroundSlamOwnerState();
		var decision = host.EvaluateRequest(
			sequence: 1,
			canActivate: true,
			now: 10.0f,
			cooldown: 5.0f,
			cadenceTolerance: 0.025f );

		Assert.IsTrue( decision.Accepted );
		Assert.AreEqual( 5.0f, decision.CooldownRemaining, 0.001f );
		Assert.IsTrue( owner.ApplyHostResult(
			1,
			accepted: true,
			decision.CooldownEndTime,
			now: 10.25f ) );
		Assert.AreEqual( 3.0f, owner.GetCooldownRemaining( 12.0f ), 0.001f );
		Assert.IsTrue( owner.HasCooldownPresentation );
		Assert.IsTrue( owner.CooldownReadyPresentationPending );
	}

	[TestMethod]
	public void SpamReplay_CannotCreateDuplicateImpacts()
	{
		var host = new LargeLadGroundSlamHostState();
		var accepted = host.EvaluateRequest(
			sequence: 1,
			canActivate: true,
			now: 10.0f,
			cooldown: 5.0f,
			cadenceTolerance: 0.025f );

		Assert.IsTrue( accepted.Accepted );
		Assert.AreEqual(
			LargeLadGroundSlamWindupResult.Impact,
			host.ResolveWindup(
				canComplete: true,
				reachedImpactTime: true ) );
		Assert.AreEqual(
			LargeLadGroundSlamWindupResult.Pending,
			host.ResolveWindup(
				canComplete: true,
				reachedImpactTime: true ) );

		var replay = host.EvaluateRequest(
			sequence: 1,
			canActivate: true,
			now: 11.0f,
			cooldown: 5.0f,
			cadenceTolerance: 0.025f );
		var spam = host.EvaluateRequest(
			sequence: 2,
			canActivate: true,
			now: 11.0f,
			cooldown: 5.0f,
			cadenceTolerance: 0.025f );

		Assert.AreEqual(
			LargeLadGroundSlamHostRequestResult.Replay,
			replay.Result );
		Assert.AreEqual(
			LargeLadGroundSlamHostRequestResult.Rejected,
			spam.Result );
		Assert.IsFalse( host.IsWindingUp );
	}

	[TestMethod]
	public void DeathDuringWindup_CancelsImpact()
	{
		AssertWindupCancellationProducesNoImpact();
	}

	[TestMethod]
	public void RoundEndDuringWindup_CancelsImpact()
	{
		AssertWindupCancellationProducesNoImpact();
	}

	[TestMethod]
	public void CancelledActivation_ProducesNoStaleCooldownReadyEvent()
	{
		var owner = new LargeLadGroundSlamOwnerState();
		owner.ApplyHostResult(
			sequence: 1,
			accepted: true,
			authoritativeCooldownEndTime: 15.0f,
			now: 10.0f );

		owner.CancelPresentation();

		Assert.AreEqual( 4.0f, owner.GetCooldownRemaining( 11.0f ), 0.001f );
		Assert.IsFalse( owner.TryTakeCooldownReadyPresentation( 15.0f ) );
		Assert.IsFalse( owner.HasCooldownPresentation );
		Assert.IsFalse( owner.CooldownReadyPresentationPending );
	}

	[TestMethod]
	public void PresentationContract_CarriesNoGameplayTargetReference()
	{
		var propertyNames = typeof( LargeLadGroundSlamPresentation )
			.GetProperties()
			.Select( property => property.Name )
			.ToArray();
		var propertyTypes = typeof( LargeLadGroundSlamPresentation )
			.GetProperties()
			.Select( property => property.PropertyType )
			.ToArray();

		CollectionAssert.AreEquivalent(
			new[]
			{
				"Phase",
				"Sequence",
				"Origin",
				"Radius",
				"Duration",
				"Strength"
			},
			propertyNames );
		Assert.IsFalse( propertyNames.Any( name =>
			name.Contains( "Target", System.StringComparison.OrdinalIgnoreCase ) ) );
		Assert.IsFalse( propertyTypes.Any( type =>
			type == typeof( GameObject ) ||
			type == typeof( LargeLadPlayer ) ||
			type.IsSubclassOf( typeof( Component ) ) ) );
	}

	[TestMethod]
	public void CameraFeedback_IsDistanceScaledAndBoundedByFeedbackRadius()
	{
		Assert.AreEqual(
			1.0f,
			LargeLadGroundSlamRules.GetFeedbackScale( 0.0f, 700.0f ),
			0.001f );
		Assert.IsTrue(
			LargeLadGroundSlamRules.GetFeedbackScale( 100.0f, 700.0f ) >
			LargeLadGroundSlamRules.GetFeedbackScale( 600.0f, 700.0f ) );
		Assert.AreEqual(
			0.0f,
			LargeLadGroundSlamRules.GetFeedbackScale( 700.0f, 700.0f ),
			0.001f );
		Assert.AreEqual(
			0.0f,
			LargeLadGroundSlamRules.GetFeedbackScale( 701.0f, 700.0f ),
			0.001f );
	}

	private static void AssertWindupCancellationProducesNoImpact()
	{
		var host = new LargeLadGroundSlamHostState();
		Assert.IsTrue( host.EvaluateRequest(
			sequence: 1,
			canActivate: true,
			now: 10.0f,
			cooldown: 5.0f,
			cadenceTolerance: 0.025f ).Accepted );

		Assert.AreEqual(
			LargeLadGroundSlamWindupResult.Cancelled,
			host.ResolveWindup(
				canComplete: false,
				reachedImpactTime: false ) );
		Assert.AreEqual(
			LargeLadGroundSlamWindupResult.Pending,
			host.ResolveWindup(
				canComplete: true,
				reachedImpactTime: true ) );
		Assert.AreEqual( 4.0f, host.GetCooldownRemaining( 11.0f ), 0.001f );
	}
}
