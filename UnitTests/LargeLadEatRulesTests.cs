using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;

[TestClass]
public sealed class LargeLadEatTargetingTests
{
	private static readonly Vector3 AttackerPosition = Vector3.Zero;
	private static readonly Vector3 AttackerForward = new( 1.0f, 0.0f, 0.0f );

	[TestMethod]
	public void TargetSelection_ChoosesNearestValidLivingSkinnyKid()
	{
		var candidates = new List<LargeLadEatTargetCandidate>
		{
			new( 20, LargeLadEatTargetKind.SkinnyKid,
				new Vector3( 45.0f, 0.0f, 0.0f ) ),
			new( 10, LargeLadEatTargetKind.SkinnyKid,
				new Vector3( 28.0f, 0.0f, 0.0f ) ),
			new( 5, LargeLadEatTargetKind.SkinnyKid,
				new Vector3( 20.0f, 0.0f, 0.0f ),
				isEligible: false )
		};

		Assert.IsTrue( TrySelect( candidates, out var selected ) );
		Assert.AreEqual( 10, selected.Id );
	}

	[TestMethod]
	public void EquidistantTargets_UseStableCandidateIdTieBreak()
	{
		var candidates = new List<LargeLadEatTargetCandidate>
		{
			new( 9, LargeLadEatTargetKind.SkinnyKid,
				new Vector3( 30.0f, 4.0f, 0.0f ) ),
			new( 3, LargeLadEatTargetKind.SkinnyKid,
				new Vector3( 30.0f, -4.0f, 0.0f ) )
		};

		Assert.IsTrue( TrySelect( candidates, out var selected ) );
		Assert.AreEqual( 3, selected.Id );
	}

	[TestMethod]
	public void ObstructionRejection_SkipsOccludedSkinnyKid()
	{
		var candidates = new List<LargeLadEatTargetCandidate>
		{
			new( 1, LargeLadEatTargetKind.SkinnyKid,
				new Vector3( 20.0f, 0.0f, 0.0f ),
				isObstructed: true ),
			new( 2, LargeLadEatTargetKind.SkinnyKid,
				new Vector3( 35.0f, 0.0f, 0.0f ) )
		};

		Assert.IsTrue( TrySelect( candidates, out var selected ) );
		Assert.AreEqual( 2, selected.Id );
	}

	[TestMethod]
	public void PlayerBeforePropPriority_WinsEvenWhenBreakableIsCloser()
	{
		var candidates = new List<LargeLadEatTargetCandidate>
		{
			new( 1, LargeLadEatTargetKind.LargeLadBarricade,
				new Vector3( 12.0f, 0.0f, 0.0f ) ),
			new( 2, LargeLadEatTargetKind.EatSmashable,
				new Vector3( 16.0f, 0.0f, 0.0f ) ),
			new( 3, LargeLadEatTargetKind.SkinnyKid,
				new Vector3( 42.0f, 0.0f, 0.0f ) )
		};

		Assert.IsTrue( TrySelect( candidates, out var selected ) );
		Assert.AreEqual( LargeLadEatTargetKind.SkinnyKid, selected.Kind );
		Assert.AreEqual( 3, selected.Id );
	}

	[TestMethod]
	public void ClaimedVictim_IsNotSelectedByAnotherEat()
	{
		var candidates = new List<LargeLadEatTargetCandidate>
		{
			new( 1, LargeLadEatTargetKind.SkinnyKid,
				new Vector3( 22.0f, 0.0f, 0.0f ),
				isClaimed: true ),
			new( 2, LargeLadEatTargetKind.LargeLadBarricade,
				new Vector3( 25.0f, 0.0f, 0.0f ) )
		};

		Assert.IsTrue( TrySelect( candidates, out var selected ) );
		Assert.AreEqual( LargeLadEatTargetKind.LargeLadBarricade, selected.Kind );
	}

	private static bool TrySelect(
		IReadOnlyList<LargeLadEatTargetCandidate> candidates,
		out LargeLadEatTargetCandidate selected )
	{
		return LargeLadEatRules.TrySelectTarget(
			AttackerPosition,
			AttackerForward,
			forwardOffset: 48.0f,
			searchRadius: 50.0f,
			minimumFacingDot: 0.1f,
			candidates,
			out selected );
	}
}

[TestClass]
public sealed class LargeLadEatStateTests
{
	[TestMethod]
	public void Cancellation_InvalidParticipantCancelsBeforeDeadline()
	{
		var state = BeginState();

		Assert.AreEqual(
			LargeLadEatStateTransition.Cancel,
			state.GetTransition(
				now: 0.1f,
				participantsRemainValid: false ) );
		Assert.IsTrue( state.TryCommitCleanup() );
		Assert.IsFalse( state.IsActive );
	}

	[TestMethod]
	public void Completion_RemainsPendingUntilDurationThenCommitsOnce()
	{
		var state = BeginState();

		Assert.AreEqual(
			LargeLadEatStateTransition.None,
			state.GetTransition(
				now: 0.299f,
				participantsRemainValid: true ) );
		Assert.AreEqual(
			LargeLadEatStateTransition.Complete,
			state.GetTransition(
				now: 0.3f,
				participantsRemainValid: true ) );
		Assert.IsTrue( state.TryCommitExecution() );
		Assert.IsFalse( state.TryCommitExecution() );
	}

	[TestMethod]
	public void DuplicateEventPrevention_ExecutionAndCleanupAreIdempotent()
	{
		var state = BeginState();
		var lethalEvents = 0;
		var healingEvents = 0;
		var cleanupEvents = 0;

		for ( var attempt = 0; attempt < 3; attempt++ )
		{
			if ( state.TryCommitExecution() )
			{
				lethalEvents++;
				healingEvents++;
			}

			if ( state.TryCommitCleanup() )
				cleanupEvents++;
		}

		Assert.AreEqual( 1, lethalEvents );
		Assert.AreEqual( 1, healingEvents );
		Assert.AreEqual( 1, cleanupEvents );
		Assert.AreEqual(
			LargeLadEatStateTransition.None,
			state.GetTransition( 1.0f, participantsRemainValid: true ) );
	}

	private static LargeLadEatState BeginState()
	{
		var state = new LargeLadEatState();
		Assert.IsTrue( state.TryBegin(
			sequence: 1,
			now: 0.0f,
			duration: 0.3f,
			presentationInterval: 0.1f ) );
		return state;
	}
}

[TestClass]
public sealed class LargeLadEatHealingTests
{
	[TestMethod]
	public void MissingHealthHealing_UsesTenPercentOfMissingAndClamps()
	{
		Assert.AreEqual(
			55.0f,
			LargeLadEatRules.GetHealedHealth(
				currentHealth: 50.0f,
				maximumHealth: 100.0f,
				missingHealthFraction: 0.1f ),
			0.0001f );
		Assert.AreEqual(
			100.0f,
			LargeLadEatRules.GetHealedHealth(
				currentHealth: 150.0f,
				maximumHealth: 100.0f,
				missingHealthFraction: 0.1f ),
			0.0001f );
	}
}
