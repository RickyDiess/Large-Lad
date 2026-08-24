using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public sealed class LargeLadRoleSelectionTests
{
	[DataTestMethod]
	[DataRow( LargeLadRolePreference.NoPreference )]
	[DataRow( LargeLadRolePreference.PreferLargeLad )]
	[DataRow( LargeLadRolePreference.PreferSkinnyKid )]
	public void DefinedPreferenceValues_AreAccepted(
		LargeLadRolePreference requested )
	{
		Assert.IsTrue(
			LargeLadRoleSelectionRules.TryAcceptPreference(
				LargeLadRolePreference.NoPreference,
				requested,
				out var accepted ) );
		Assert.AreEqual( requested, accepted );
	}

	[TestMethod]
	public void InvalidPreference_IsRejectedAndRestoresCurrentValue()
	{
		Assert.IsFalse(
			LargeLadRoleSelectionRules.TryAcceptPreference(
				LargeLadRolePreference.PreferSkinnyKid,
				(LargeLadRolePreference)999,
				out var accepted ) );
		Assert.AreEqual(
			LargeLadRolePreference.PreferSkinnyKid,
			accepted );
	}

	[TestMethod]
	public void Volunteer_IsPreferredOverNeutral()
	{
		Assert.AreEqual(
			"volunteer",
			Select(
				Candidate( "neutral" ),
				Candidate(
					"volunteer",
					LargeLadRolePreference.PreferLargeLad ) ) );
	}

	[TestMethod]
	public void Neutral_IsPreferredOverSkinnyPreference()
	{
		Assert.AreEqual(
			"neutral",
			Select(
				Candidate(
					"skinny",
					LargeLadRolePreference.PreferSkinnyKid ),
				Candidate( "neutral" ) ) );
	}

	[TestMethod]
	public void SkinnyPreference_IsUsedWhenNecessary()
	{
		Assert.AreEqual(
			"skinny",
			Select( Candidate(
				"skinny",
				LargeLadRolePreference.PreferSkinnyKid ) ) );
	}

	[TestMethod]
	public void PreviousLargeLad_IsExcludedWhenAnotherEligiblePlayerExists()
	{
		Assert.AreEqual(
			"neutral",
			Select(
				Candidate(
					"previous-volunteer",
					LargeLadRolePreference.PreferLargeLad,
					wasPreviousLargeLad: true,
					hasBeenLargeLad: true,
					lastSelectionOrdinal: 3 ),
				Candidate( "neutral" ) ) );
	}

	[TestMethod]
	public void PreviousLargeLad_MayRepeatWhenOnlyEligibleCandidate()
	{
		Assert.AreEqual(
			"previous",
			Select( Candidate(
				"previous",
				LargeLadRolePreference.PreferLargeLad,
				wasPreviousLargeLad: true,
				hasBeenLargeLad: true,
				lastSelectionOrdinal: 3 ) ) );
	}

	[TestMethod]
	public void LongestWaitingCandidate_WinsWithinPreferenceTier()
	{
		Assert.AreEqual(
			"waited-longer",
			Select(
				Candidate(
					"recent",
					LargeLadRolePreference.PreferLargeLad,
					hasBeenLargeLad: true,
					lastSelectionOrdinal: 8 ),
				Candidate(
					"waited-longer",
					LargeLadRolePreference.PreferLargeLad,
					hasBeenLargeLad: true,
					lastSelectionOrdinal: 2 ) ) );
	}

	[TestMethod]
	public void NeverSelectedPlayer_OutranksRecentlySelectedPlayer()
	{
		Assert.AreEqual(
			"never-selected",
			Select(
				Candidate(
					"recent",
					LargeLadRolePreference.NoPreference,
					hasBeenLargeLad: true,
					lastSelectionOrdinal: 1 ),
				Candidate( "never-selected" ) ) );
	}

	[TestMethod]
	public void GenuineTie_ChoosesAnyTiedCandidateButNeverLowerTier()
	{
		var tiedA = Candidate(
			"a",
			LargeLadRolePreference.PreferLargeLad );
		var tiedB = Candidate(
			"b",
			LargeLadRolePreference.PreferLargeLad );
		var lowerPriority = Candidate( "neutral" );

		Assert.AreEqual( "a", Select( 0, tiedA, tiedB, lowerPriority ) );
		Assert.AreEqual( "b", Select( 1, tiedA, tiedB, lowerPriority ) );
		Assert.AreNotEqual(
			"neutral",
			Select( 2, tiedA, tiedB, lowerPriority ) );
	}

	[TestMethod]
	public void NewlyJoinedIneligiblePlayer_IsExcluded()
	{
		Assert.AreEqual(
			"eligible-neutral",
			Select(
				Candidate(
					"new-volunteer",
					LargeLadRolePreference.PreferLargeLad,
					isEligible: false ),
				Candidate( "eligible-neutral" ) ) );
	}

	[TestMethod]
	public void FirstSessionBootstrap_PermitsInitialRound()
	{
		var history = default(LargeLadRoleSelectionHistory);
		var session = default(LargeLadRoleSelectionSessionState);

		Assert.IsTrue(
			LargeLadRoleSelectionRules.IsEligibleForSelection(
				history,
				session,
				isInCapturedBootstrapRoster: false ) );
		Assert.AreEqual(
			"initial",
			Select( Candidate(
				"initial",
				isEligible: false,
				isBootstrapEligible: true ) ) );
	}

	[TestMethod]
	public void BootstrapException_DoesNotMakeLaterJoinerEligible()
	{
		Assert.IsTrue(
			LargeLadRoleSelectionRules.TryCommitSuccessfulRoundStart(
				default,
				"initial-player",
				spawnAllocationSucceeded: true,
				out var session,
				out _ ) );

		Assert.IsTrue(
			LargeLadRoleSelectionRules.IsEligibleForSelection(
				default,
				session,
				isInCapturedBootstrapRoster: true ) );
		Assert.IsFalse(
			LargeLadRoleSelectionRules.IsEligibleForSelection(
				default,
				session,
				isInCapturedBootstrapRoster: false ) );
	}

	[TestMethod]
	public void PresentAtStartAndConnectedAtCompletion_EarnsEligibility()
	{
		var history =
			LargeLadRoleSelectionRules.CommitFullRoundCompletion(
				default,
				wasPresentAtSuccessfulStart: true,
				isConnectedAtCompletion: true,
				roundCompletedSuccessfully: true );

		Assert.IsTrue( history.HasCompletedFullRound );
	}

	[TestMethod]
	public void MidRoundJoin_DoesNotEarnEligibility()
	{
		var history =
			LargeLadRoleSelectionRules.CommitFullRoundCompletion(
				default,
				wasPresentAtSuccessfulStart: false,
				isConnectedAtCompletion: true,
				roundCompletedSuccessfully: true );

		Assert.IsFalse( history.HasCompletedFullRound );
	}

	[TestMethod]
	public void DisconnectBeforeCompletion_DoesNotEarnEligibility()
	{
		var history =
			LargeLadRoleSelectionRules.CommitFullRoundCompletion(
				default,
				wasPresentAtSuccessfulStart: true,
				isConnectedAtCompletion: false,
				roundCompletedSuccessfully: true );

		Assert.IsFalse( history.HasCompletedFullRound );
	}

	[TestMethod]
	public void AbortedRound_DoesNotEarnEligibility()
	{
		var history =
			LargeLadRoleSelectionRules.CommitFullRoundCompletion(
				default,
				wasPresentAtSuccessfulStart: true,
				isConnectedAtCompletion: true,
				roundCompletedSuccessfully: false );

		Assert.IsFalse( history.HasCompletedFullRound );
	}

	[TestMethod]
	public void FailedSpawnAllocation_DoesNotUpdateFairness()
	{
		var initialSession = default(LargeLadRoleSelectionSessionState);
		var initialHistory = default(LargeLadRoleSelectionHistory);

		Assert.IsFalse(
			LargeLadRoleSelectionRules.TryCommitSuccessfulRoundStart(
				initialSession,
				"selected",
				spawnAllocationSucceeded: false,
				out var unchangedSession,
				out var ordinal ) );
		Assert.AreEqual( 0, ordinal );
		Assert.AreEqual(
			initialSession.SuccessfulRoundOrdinal,
			unchangedSession.SuccessfulRoundOrdinal );
		Assert.IsFalse( initialHistory.HasBeenLargeLad );
	}

	[TestMethod]
	public void SuccessfulRoundStartup_UpdatesFairnessExactlyOnce()
	{
		Assert.IsTrue(
			LargeLadRoleSelectionRules.TryCommitSuccessfulRoundStart(
				default,
				"selected",
				spawnAllocationSucceeded: true,
				out var committedSession,
				out var ordinal ) );
		var history =
			LargeLadRoleSelectionRules.CommitLargeLadSelection(
				default,
				ordinal );

		Assert.IsFalse(
			LargeLadRoleSelectionRules.TryCommitSuccessfulRoundStart(
				committedSession,
				"selected",
				spawnAllocationSucceeded: true,
				out var duplicateSession,
				out var duplicateOrdinal ) );
		var duplicateHistory =
			LargeLadRoleSelectionRules.CommitLargeLadSelection(
				history,
				ordinal );

		Assert.AreEqual( 1, committedSession.SuccessfulRoundOrdinal );
		Assert.AreEqual( 1, history.LastLargeLadSelectionOrdinal );
		Assert.AreEqual( 0, duplicateOrdinal );
		Assert.AreEqual(
			committedSession.SuccessfulRoundOrdinal,
			duplicateSession.SuccessfulRoundOrdinal );
		Assert.AreEqual(
			history.LastLargeLadSelectionOrdinal,
			duplicateHistory.LastLargeLadSelectionOrdinal );
	}

	[TestMethod]
	public void RosterReordering_DoesNotChangeNonTiedSelection()
	{
		var longerWaiting = Candidate(
			"longer",
			LargeLadRolePreference.PreferLargeLad,
			hasBeenLargeLad: true,
			lastSelectionOrdinal: 2 );
		var recent = Candidate(
			"recent",
			LargeLadRolePreference.PreferLargeLad,
			hasBeenLargeLad: true,
			lastSelectionOrdinal: 7 );

		Assert.AreEqual( "longer", Select( longerWaiting, recent ) );
		Assert.AreEqual( "longer", Select( recent, longerWaiting ) );
	}

	[TestMethod]
	public void PreferenceChange_DoesNotRetroactivelyChangeCurrentRole()
	{
		var committedLargeLadIdentity = Select(
			Candidate(
				"current-large-lad",
				LargeLadRolePreference.PreferLargeLad ),
			Candidate( "other" ) );

		var nextRoundSelection = Select(
			Candidate(
				"current-large-lad",
				LargeLadRolePreference.PreferSkinnyKid ),
			Candidate(
				"other",
				LargeLadRolePreference.PreferLargeLad ) );

		Assert.AreEqual( "current-large-lad", committedLargeLadIdentity );
		Assert.AreEqual( "other", nextRoundSelection );
	}

	[TestMethod]
	public void MapTransitionReset_PreservesPersistentEligibilityAndFairness()
	{
		Assert.IsTrue(
			LargeLadRoleSelectionRules.TryCommitSuccessfulRoundStart(
				default,
				"selected",
				spawnAllocationSucceeded: true,
				out var committedSession,
				out var ordinal ) );
		committedSession = LargeLadRoleSelectionRules.MarkRoundCompleted(
			committedSession,
			roundCompletedSuccessfully: true );
		var history = LargeLadRoleSelectionRules.CommitFullRoundCompletion(
			LargeLadRoleSelectionRules.CommitLargeLadSelection(
				default,
				ordinal ),
			wasPresentAtSuccessfulStart: true,
			isConnectedAtCompletion: true,
			roundCompletedSuccessfully: true );

		var afterTransition =
			LargeLadRoleSelectionRules.AbortForMapTransition(
				committedSession );

		Assert.AreEqual(
			committedSession.SuccessfulRoundOrdinal,
			afterTransition.SuccessfulRoundOrdinal );
		Assert.AreEqual(
			committedSession.PreviousLargeLadIdentity,
			afterTransition.PreviousLargeLadIdentity );
		Assert.AreEqual(
			committedSession.HasCompletedEligibilityRound,
			afterTransition.HasCompletedEligibilityRound );
		Assert.AreEqual(
			committedSession.HasCapturedBootstrapRoster,
			afterTransition.HasCapturedBootstrapRoster );
		Assert.IsFalse( afterTransition.HasCommittedCurrentRoundStart );
		Assert.IsTrue( history.HasCompletedFullRound );
		Assert.IsTrue( history.HasBeenLargeLad );
		Assert.AreEqual( ordinal, history.LastLargeLadSelectionOrdinal );
	}

	private static LargeLadRoleSelectionCandidate Candidate(
		string identity,
		LargeLadRolePreference preference =
			LargeLadRolePreference.NoPreference,
		bool isEligible = true,
		bool isBootstrapEligible = false,
		bool wasPreviousLargeLad = false,
		bool hasBeenLargeLad = false,
		long lastSelectionOrdinal = 0 )
	{
		return new LargeLadRoleSelectionCandidate(
			identity,
			preference,
			isEligible,
			isBootstrapEligible,
			wasPreviousLargeLad,
			hasBeenLargeLad,
			lastSelectionOrdinal );
	}

	private static string Select(
		params LargeLadRoleSelectionCandidate[] candidates )
	{
		return Select( 0, candidates );
	}

	private static string Select(
		int tieBreakValue,
		params LargeLadRoleSelectionCandidate[] candidates )
	{
		var selected =
			LargeLadRoleSelectionRules.SelectLargeLadCandidate(
				candidates,
				tieBreakValue );
		Assert.IsTrue( selected.HasValue );
		return selected.Value.SessionIdentity;
	}
}
