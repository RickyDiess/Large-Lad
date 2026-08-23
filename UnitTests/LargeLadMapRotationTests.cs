using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;

[TestClass]
public sealed class LargeLadMapRotationTests
{
	[TestMethod]
	public void CompletedRounds_AdvanceOnlyAtSuccessfulCompletionBoundary()
	{
		Assert.AreEqual(
			2,
			LargeLadMapRotationRules.UpdateCompletedRoundCount(
				currentCount: 2,
				roundSuccessfullyCompleted: false ) );
		Assert.AreEqual(
			3,
			LargeLadMapRotationRules.UpdateCompletedRoundCount(
				currentCount: 2,
				roundSuccessfullyCompleted: true ) );
	}

	[TestMethod]
	public void ConfiguredRoundThreshold_OpensVoteOnlyWhenReached()
	{
		Assert.IsFalse(
			LargeLadMapRotationRules.ShouldOpenVote( 2, roundsPerMap: 3 ) );
		Assert.IsTrue(
			LargeLadMapRotationRules.ShouldOpenVote( 3, roundsPerMap: 3 ) );
	}

	[TestMethod]
	public void EligiblePlayer_CanCastExactlyOneVote()
	{
		var candidates = CreateCandidates( "test.alpha", "test.bravo" );
		var eligible = new HashSet<string> { "player-one" };
		var votes = new Dictionary<string, string>();

		Assert.IsTrue( LargeLadMapRotationRules.TryCastVote(
			eligible,
			votes,
			candidates,
			"player-one",
			"test.alpha" ) );
		Assert.IsFalse( LargeLadMapRotationRules.TryCastVote(
			eligible,
			votes,
			candidates,
			"player-one",
			"test.bravo" ) );
		Assert.AreEqual( "test.alpha", votes["player-one"] );
	}

	[TestMethod]
	public void CandidateOutsideActiveVote_IsRejected()
	{
		var candidates = CreateCandidates( "test.alpha" );
		var eligible = new HashSet<string> { "player-one" };
		var votes = new Dictionary<string, string>();

		Assert.IsFalse( LargeLadMapRotationRules.TryCastVote(
			eligible,
			votes,
			candidates,
			"player-one",
			"test.not_present" ) );
		Assert.AreEqual( 0, votes.Count );
	}

	[TestMethod]
	public void DisconnectedEligibleVoter_DoesNotBlockEarlyCompletion()
	{
		var eligible = new HashSet<string> { "connected", "left" };
		var connected = new HashSet<string> { "connected", "joined-late" };
		var votes = new Dictionary<string, string>
		{
			["connected"] = "test.alpha"
		};

		Assert.IsTrue(
			LargeLadMapRotationRules.HaveAllConnectedEligibleVotersSubmitted(
				eligible,
				connected,
				votes ) );
	}

	[TestMethod]
	public void PluralityWinner_IsSelected()
	{
		var candidates = CreateCandidates(
			"test.alpha",
			"test.bravo",
			"test.charlie" );
		var connected = new HashSet<string> { "a", "b", "c", "d" };
		var votes = new Dictionary<string, string>
		{
			["a"] = "test.bravo",
			["b"] = "test.bravo",
			["c"] = "test.alpha",
			["d"] = "test.charlie"
		};

		var winner = LargeLadMapRotationRules.SelectWinner(
			candidates,
			votes,
			connected,
			tieBreakIndex: 99 );

		Assert.AreEqual( "test.bravo", winner.StableMapId );
	}

	[TestMethod]
	public void TieBreak_ConsidersOnlyTiedTopCandidates()
	{
		var candidates = CreateCandidates(
			"test.alpha",
			"test.bravo",
			"test.charlie" );
		var connected = new HashSet<string> { "a", "b", "c", "d", "e" };
		var votes = new Dictionary<string, string>
		{
			["a"] = "test.alpha",
			["b"] = "test.alpha",
			["c"] = "test.bravo",
			["d"] = "test.bravo",
			["e"] = "test.charlie"
		};

		var firstLeader = LargeLadMapRotationRules.SelectWinner(
			candidates,
			votes,
			connected,
			tieBreakIndex: 0 );
		var secondLeader = LargeLadMapRotationRules.SelectWinner(
			candidates,
			votes,
			connected,
			tieBreakIndex: 1 );

		CollectionAssert.AreEquivalent(
			new[] { "test.alpha", "test.bravo" },
			new[] { firstLeader.StableMapId, secondLeader.StableMapId } );
		Assert.AreNotEqual( "test.charlie", firstLeader.StableMapId );
		Assert.AreNotEqual( "test.charlie", secondLeader.StableMapId );
	}

	[TestMethod]
	public void NoVoteOutcome_UsesDeterministicStableIdentityOrdering()
	{
		var candidates = CreateCandidates(
			"test.charlie",
			"test.alpha",
			"test.bravo" );
		var connected = new HashSet<string> { "a" };

		var first = LargeLadMapRotationRules.SelectWinner(
			candidates,
			new Dictionary<string, string>(),
			connected,
			tieBreakIndex: 0 );
		var second = LargeLadMapRotationRules.SelectWinner(
			candidates,
			new Dictionary<string, string>(),
			connected,
			tieBreakIndex: 12345 );

		Assert.AreEqual( "test.alpha", first.StableMapId );
		Assert.AreEqual( first.StableMapId, second.StableMapId );
	}

	[TestMethod]
	public void OfficialAndCommunityDescriptors_UseIdenticalVoteResultRules()
	{
		var official = CreateDescriptor(
			"test.official",
			"Official Map",
			"scenes/official.scene",
			isOfficial: true );
		var community = CreateDescriptor(
			"test.community",
			"Community Map",
			"scenes/community.scene",
			isOfficial: false );
		var connected = new HashSet<string> { "a", "b" };
		var votes = new Dictionary<string, string>
		{
			["a"] = community.StableMapId,
			["b"] = community.StableMapId
		};

		var winner = LargeLadMapRotationRules.SelectWinner(
			new[] { official, community },
			votes,
			connected,
			tieBreakIndex: 0 );

		Assert.IsFalse( winner.IsOfficiallyCurated );
		Assert.AreEqual(
			community.MapInstanceIdentifier,
			winner.MapInstanceIdentifier );
	}

	[TestMethod]
	public void DuplicateDisplayNames_DoNotReplaceStableVoteIdentity()
	{
		var first = CreateDescriptor(
			"test.first",
			"Duplicate Name",
			"scenes/first.scene",
			isOfficial: false );
		var second = CreateDescriptor(
			"test.second",
			"Duplicate Name",
			"scenes/second.scene",
			isOfficial: false );
		var connected = new HashSet<string> { "player" };
		var votes = new Dictionary<string, string>
		{
			["player"] = second.StableMapId
		};

		var winner = LargeLadMapRotationRules.SelectWinner(
			new[] { first, second },
			votes,
			connected,
			tieBreakIndex: 0 );

		Assert.AreEqual( "test.second", winner.StableMapId );
		Assert.AreEqual( "scenes/second.scene", winner.MapInstanceIdentifier );
	}

	[TestMethod]
	public void FallbackSelection_NeverRepeatsAttemptedFailedMap()
	{
		var attempted = new HashSet<string>
		{
			"scenes/voted_bad.scene"
		};

		var firstFallback = LargeLadMapRotationRules.SelectFallbackIdentifier(
			previousKnownGoodIdentifier: "scenes/gym.scene",
			configuredIdentifier: "scenes/default.scene",
			curatedOfficialIdentifiers:
				new[] { "scenes/gym.scene", "scenes/other.scene" },
			attempted );
		attempted.Add( firstFallback );
		var secondFallback = LargeLadMapRotationRules.SelectFallbackIdentifier(
			"scenes/gym.scene",
			"scenes/default.scene",
			new[] { "scenes/gym.scene", "scenes/other.scene" },
			attempted );

		Assert.AreEqual( "scenes/gym.scene", firstFallback );
		Assert.AreEqual( "scenes/default.scene", secondFallback );
		Assert.AreNotEqual( "scenes/voted_bad.scene", firstFallback );
		Assert.AreNotEqual( firstFallback, secondFallback );
	}

	[TestMethod]
	public void SuccessfulReplacementReadiness_ResetsMapCounter()
	{
		Assert.AreEqual(
			0,
			LargeLadMapRotationRules.UpdateCountForMapReadiness(
				currentCount: 3,
				replacementMapBecameReady: true ) );
	}

	[TestMethod]
	public void FailedLoad_DoesNotResetCounterAsThoughMapBecameReady()
	{
		Assert.AreEqual(
			3,
			LargeLadMapRotationRules.UpdateCountForMapReadiness(
				currentCount: 3,
				replacementMapBecameReady: false ) );
	}

	[TestMethod]
	public void LocalViewAndMovement_StayHeldUntilPlayingMapIsLocallyLoaded()
	{
		Assert.IsTrue(
			LargeLadMapRotationRules.ShouldHoldLocalViewAndMovement(
				LargeLadMapFlowState.WaitingForInitialMapSelection,
				localSelectedMapIsLoaded: false ) );
		Assert.IsTrue(
			LargeLadMapRotationRules.ShouldHoldLocalViewAndMovement(
				LargeLadMapFlowState.Loading,
				localSelectedMapIsLoaded: true ) );
		Assert.IsTrue(
			LargeLadMapRotationRules.ShouldHoldLocalViewAndMovement(
				LargeLadMapFlowState.Playing,
				localSelectedMapIsLoaded: false ) );
		Assert.IsFalse(
			LargeLadMapRotationRules.ShouldHoldLocalViewAndMovement(
				LargeLadMapFlowState.Playing,
				localSelectedMapIsLoaded: true ) );
	}

	private static IReadOnlyList<LargeLadMapDescriptor> CreateCandidates(
		params string[] stableMapIds )
	{
		return stableMapIds
			.Select( stableMapId => CreateDescriptor(
				stableMapId,
				stableMapId,
				$"scenes/{stableMapId}.scene",
				isOfficial: false ) )
			.ToList();
	}

	private static LargeLadMapDescriptor CreateDescriptor(
		string stableMapId,
		string displayName,
		string mapIdentifier,
		bool isOfficial )
	{
		var manifest = new LargeLadMapManifest
		{
			StableMapId = stableMapId,
			ContractVersion = LargeLadMapContract.CurrentVersion,
			DisplayName = displayName,
			MapperCredit = "Test Mapper",
			PresentationAsset = "textures/test_map.vtex",
			RecommendedMinimumPlayers = 2,
			RecommendedMaximumPlayers = 32,
			BalanceOverrides = new LargeLadMapBalanceOverrides()
		};

		if ( isOfficial )
		{
			var catalog = new LargeLadOfficialMapCatalog
			{
				Entries =
				[
					new LargeLadOfficialMapEntry
					{
						MapInstanceIdentifier = mapIdentifier,
						Manifest = manifest
					}
				]
			};
			Assert.IsTrue( LargeLadMapCatalog.TryResolveOfficial(
				catalog,
				stableMapId,
				packageMetadata: null,
				out var officialDescriptor,
				out _ ) );
			return officialDescriptor;
		}

		Assert.IsTrue( LargeLadMapCatalog.TryResolveCommunity(
			manifest,
			mapIdentifier,
			packageMetadata: null,
			out var communityDescriptor,
			out _ ) );
		return communityDescriptor;
	}
}
