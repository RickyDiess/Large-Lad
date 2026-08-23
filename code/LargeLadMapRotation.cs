using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;

public enum LargeLadMapFlowState
{
	WaitingForInitialMapSelection,
	Loading,
	Playing,
	Voting,
	Transitioning,
	Recovering,
	Failed
}

/// <summary>
/// Pure, deterministic map-rotation and plurality-vote policy. Runtime loading,
/// networking, and presentation stay in the persistent session coordinator.
/// </summary>
public static class LargeLadMapRotationRules
{
	public static int UpdateCompletedRoundCount(
		int currentCount,
		bool roundSuccessfullyCompleted )
	{
		return roundSuccessfullyCompleted
			? Math.Max( 0, currentCount ) + 1
			: Math.Max( 0, currentCount );
	}

	public static bool ShouldOpenVote(
		int completedRoundCount,
		int roundsPerMap )
	{
		return completedRoundCount >= Math.Max( 1, roundsPerMap );
	}

	public static int UpdateCountForMapReadiness(
		int currentCount,
		bool replacementMapBecameReady )
	{
		return replacementMapBecameReady
			? 0
			: Math.Max( 0, currentCount );
	}

	public static bool ShouldHoldLocalViewAndMovement(
		LargeLadMapFlowState flowState,
		bool localSelectedMapIsLoaded )
	{
		return flowState != LargeLadMapFlowState.Playing ||
			!localSelectedMapIsLoaded;
	}

	public static string GetMapIdentity( LargeLadMapDescriptor descriptor )
	{
		return descriptor?.StableMapId?.Trim() ?? string.Empty;
	}

	public static IReadOnlyList<LargeLadMapDescriptor> SelectVoteCandidates(
		IEnumerable<LargeLadMapDescriptor> knownDescriptors,
		LargeLadMapDescriptor currentMap )
	{
		var candidates = (knownDescriptors ?? [])
			.Where( IsUsableDescriptor )
			.GroupBy( GetMapIdentity, StringComparer.OrdinalIgnoreCase )
			.Select( group => group.First() )
			.ToList();
		var currentIdentity = GetMapIdentity( currentMap );

		if ( candidates.Count > 1 &&
			!string.IsNullOrWhiteSpace( currentIdentity ) )
		{
			candidates.RemoveAll( candidate => string.Equals(
				GetMapIdentity( candidate ),
				currentIdentity,
				StringComparison.OrdinalIgnoreCase ) );
		}

		return candidates;
	}

	public static bool TryCastVote(
		ISet<string> eligibleVoters,
		IDictionary<string, string> submittedVotes,
		IEnumerable<LargeLadMapDescriptor> candidates,
		string voterIdentity,
		string candidateIdentity )
	{
		if ( eligibleVoters is null ||
			submittedVotes is null ||
			string.IsNullOrWhiteSpace( voterIdentity ) ||
			string.IsNullOrWhiteSpace( candidateIdentity ) ||
			!eligibleVoters.Contains( voterIdentity ) ||
			submittedVotes.ContainsKey( voterIdentity ) )
		{
			return false;
		}

		var canonicalCandidateIdentity = (candidates ?? [])
			.Where( IsUsableDescriptor )
			.Select( GetMapIdentity )
			.FirstOrDefault( identity => string.Equals(
				identity,
				candidateIdentity.Trim(),
				StringComparison.OrdinalIgnoreCase ) );

		if ( string.IsNullOrWhiteSpace( canonicalCandidateIdentity ) )
			return false;

		submittedVotes.Add( voterIdentity, canonicalCandidateIdentity );
		return true;
	}

	public static bool HaveAllConnectedEligibleVotersSubmitted(
		ISet<string> eligibleVoters,
		ISet<string> connectedVoters,
		IReadOnlyDictionary<string, string> submittedVotes )
	{
		if ( eligibleVoters is null || connectedVoters is null )
			return true;

		return eligibleVoters
			.Where( connectedVoters.Contains )
			.All( voter => submittedVotes?.ContainsKey( voter ) == true );
	}

	public static IReadOnlyDictionary<string, int> CountConnectedVotes(
		IEnumerable<LargeLadMapDescriptor> candidates,
		IReadOnlyDictionary<string, string> submittedVotes,
		ISet<string> connectedVoters )
	{
		var totals = (candidates ?? [])
			.Where( IsUsableDescriptor )
			.GroupBy( GetMapIdentity, StringComparer.OrdinalIgnoreCase )
			.ToDictionary(
				group => group.First().StableMapId,
				_ => 0,
				StringComparer.OrdinalIgnoreCase );

		if ( submittedVotes is null || connectedVoters is null )
			return totals;

		foreach ( var vote in submittedVotes )
		{
			if ( connectedVoters.Contains( vote.Key ) &&
				totals.ContainsKey( vote.Value ) )
			{
				totals[vote.Value]++;
			}
		}

		return totals;
	}

	public static LargeLadMapDescriptor SelectWinner(
		IEnumerable<LargeLadMapDescriptor> candidates,
		IReadOnlyDictionary<string, string> submittedVotes,
		ISet<string> connectedVoters,
		int tieBreakIndex )
	{
		var candidateList = (candidates ?? [])
			.Where( IsUsableDescriptor )
			.GroupBy( GetMapIdentity, StringComparer.OrdinalIgnoreCase )
			.Select( group => group.First() )
			.ToList();

		if ( candidateList.Count == 0 )
			return null;

		var totals = CountConnectedVotes(
			candidateList,
			submittedVotes,
			connectedVoters );
		var highestTotal = totals.Values.DefaultIfEmpty( 0 ).Max();

		// With no submitted votes, use stable identity ordering. This is safe,
		// deterministic, and does not pretend client randomness chose a winner.
		if ( highestTotal <= 0 )
		{
			return candidateList.OrderBy(
				GetMapIdentity,
				StringComparer.OrdinalIgnoreCase ).First();
		}

		var tiedLeaders = candidateList
			.Where( candidate =>
				totals[GetMapIdentity( candidate )] == highestTotal )
			.OrderBy( GetMapIdentity, StringComparer.OrdinalIgnoreCase )
			.ToList();
		var selectedIndex = Math.Abs( tieBreakIndex % tiedLeaders.Count );
		return tiedLeaders[selectedIndex];
	}

	public static string SelectFallbackIdentifier(
		string previousKnownGoodIdentifier,
		string configuredIdentifier,
		IEnumerable<string> curatedOfficialIdentifiers,
		ISet<string> attemptedIdentifiers )
	{
		var attempted = attemptedIdentifiers is null
			? new HashSet<string>( StringComparer.OrdinalIgnoreCase )
			: new HashSet<string>(
				attemptedIdentifiers,
				StringComparer.OrdinalIgnoreCase );

		return new[] { previousKnownGoodIdentifier, configuredIdentifier }
			.Concat( curatedOfficialIdentifiers ?? [] )
			.Select( identifier => identifier?.Trim() )
			.Where( identifier => !string.IsNullOrWhiteSpace( identifier ) )
			.Distinct( StringComparer.OrdinalIgnoreCase )
			.FirstOrDefault( identifier => !attempted.Contains( identifier ) );
	}

	private static bool IsUsableDescriptor( LargeLadMapDescriptor descriptor )
	{
		return descriptor is not null &&
			!string.IsNullOrWhiteSpace( descriptor.StableMapId ) &&
			!string.IsNullOrWhiteSpace( descriptor.MapInstanceIdentifier );
	}
}

/// <summary>
/// Persistent, host-authoritative policy layered over the coordinator's sole
/// MapInstance lifecycle. It never loads content independently.
/// </summary>
public sealed partial class LargeLadSessionCoordinator
{
	private const char VoteFieldSeparator = '\n';

	[Property, Group( "Map Rotation" ), Title( "Rounds Per Map" )]
	public int RoundsPerMap { get; set; } = 3;

	[Property, Group( "Map Rotation" ), Title( "Vote Duration (Seconds)" )]
	public float VoteDuration { get; set; } = 20.0f;

	[Property, Group( "Map Rotation" ), Title( "Map Load Timeout (Seconds)" )]
	public float MapLoadTimeout { get; set; } = 30.0f;

	[Sync( SyncFlags.FromHost )]
	public LargeLadMapFlowState MapFlowState { get; private set; } =
		LargeLadMapFlowState.WaitingForInitialMapSelection;

	[Sync( SyncFlags.FromHost )]
	public int CompletedRoundsOnCurrentMap { get; private set; }

	[Sync( SyncFlags.FromHost )]
	public float VoteEndsAt { get; private set; }

	[Sync( SyncFlags.FromHost ), Change( nameof( OnVoteSessionChanged ) )]
	public int VoteSessionId { get; private set; }

	[Sync( SyncFlags.FromHost )]
	public string ActiveVoteCandidateIds { get; private set; } = string.Empty;

	[Sync( SyncFlags.FromHost )]
	public string ActiveVoteCounts { get; private set; } = string.Empty;

	[Sync( SyncFlags.FromHost )]
	public string LastWinningMapId { get; private set; } = string.Empty;

	public string LocalSubmittedVoteId { get; private set; } = string.Empty;

	public bool CanAdvanceRoundFlow =>
		IsMapReady && MapFlowState == LargeLadMapFlowState.Playing;

	/// <summary>
	/// Local MapInstance readiness is deliberately separate from the replicated
	/// host readiness state. A slower client stays covered and movement-frozen
	/// until its own copy of the selected map has completed loading.
	/// </summary>
	public bool IsLocalSelectedMapLoaded
	{
		get
		{
			var selectedMap = CurrentMapName?.Trim() ?? string.Empty;
			return !string.IsNullOrWhiteSpace( selectedMap ) &&
				MapInstance is not null &&
				MapInstance.IsValid &&
				MapInstance.IsLoaded &&
				string.Equals(
					MapInstance.MapName?.Trim(),
					selectedMap,
					StringComparison.OrdinalIgnoreCase );
		}
	}

	public bool ShouldBlackoutLocalView =>
		LargeLadMapRotationRules.ShouldHoldLocalViewAndMovement(
			MapFlowState,
			IsLocalSelectedMapLoaded );

	internal bool ShouldHoldLocalPlayerForMap => ShouldBlackoutLocalView;

	public bool ShowInitialMapSelection =>
		MapFlowState == LargeLadMapFlowState.WaitingForInitialMapSelection &&
		Networking.IsHost &&
		!Application.IsDedicatedServer;

	public float VoteTimeRemaining =>
		MapFlowState == LargeLadMapFlowState.Voting
			? MathF.Max( 0.0f, VoteEndsAt - Time.Now )
			: 0.0f;

	private readonly HashSet<string> eligibleVoteConnections =
		new( StringComparer.OrdinalIgnoreCase );
	private readonly Dictionary<string, string> submittedMapVotes =
		new( StringComparer.OrdinalIgnoreCase );
	private readonly HashSet<string> attemptedTransitionMaps =
		new( StringComparer.OrdinalIgnoreCase );
	private List<LargeLadMapDescriptor> activeVoteDescriptors = new();
	private string lastKnownGoodMapIdentifier;
	private float mapLoadDeadline;
	private bool mapSelectionTransactionActive;
	private bool voteCompletionCommitted;

	protected override void OnUpdate()
	{
		if ( !Networking.IsHost )
			return;

		if ( MapState == LargeLadMapSessionState.Loading &&
			mapLoadDeadline > 0.0f &&
			Time.Now >= mapLoadDeadline )
		{
			mapLoadDeadline = 0.0f;
			SetMapState( LargeLadMapSessionState.Failed );
			HandleSelectedMapFailure(
				$"MapInstance did not complete within " +
				$"{MathF.Max( 1.0f, MapLoadTimeout ):0.#} seconds" );
			return;
		}

		if ( MapFlowState == LargeLadMapFlowState.Voting )
			UpdateActiveVote();
	}

	internal void InitializeMapFlow()
	{
		if ( !Networking.IsHost )
			return;

		if ( !string.IsNullOrWhiteSpace( CurrentMapName ) )
		{
			SetMapFlowState(
				IsMapReady
					? LargeLadMapFlowState.Playing
					: LargeLadMapFlowState.Loading );
			SelectMapName( CurrentMapName );
			return;
		}

		if ( !Application.IsDedicatedServer )
		{
			SetMapFlowState(
				LargeLadMapFlowState.WaitingForInitialMapSelection );

			if ( GetKnownMapDescriptors().Count == 0 )
			{
				SetMapFlowState( LargeLadMapFlowState.Failed );
				Log.Error(
					"The listening host cannot choose an initial map because the " +
					"official Large Lad catalog has no valid entries." );
			}

			return;
		}

		var launchMap = LaunchArguments.Map?.Trim();
		var initialMap = !string.IsNullOrWhiteSpace( launchMap )
			? launchMap
			: ResolveConfiguredOrCuratedStartupMap();

		if ( string.IsNullOrWhiteSpace( initialMap ) )
		{
			SetMapFlowState( LargeLadMapFlowState.Failed );
			Log.Error(
				"The dedicated Large Lad session has no launch map, configured " +
				"startup map, or valid curated official fallback." );
			return;
		}

		Log.Info(
			!string.IsNullOrWhiteSpace( launchMap )
				? $"Dedicated startup selected launch map '{initialMap}'."
				: $"Dedicated startup selected configured/catalog map '{initialMap}'." );
		BeginMapSelectionTransaction( initialMap, isRuntimeTransition: false );
	}

	public IReadOnlyList<LargeLadMapDescriptor> GetInitialMapCandidates()
	{
		return GetKnownMapDescriptors();
	}

	public IReadOnlyList<LargeLadMapDescriptor> GetActiveVoteCandidates()
	{
		var requestedIds = DecodeVoteFields( ActiveVoteCandidateIds );
		var knownById = GetKnownMapDescriptors()
			.Concat( CurrentMapDescriptor is null
				? []
				: new[] { CurrentMapDescriptor } )
			.Where( descriptor => descriptor is not null )
			.GroupBy(
				LargeLadMapRotationRules.GetMapIdentity,
				StringComparer.OrdinalIgnoreCase )
			.ToDictionary(
				group => group.Key,
				group => group.First(),
				StringComparer.OrdinalIgnoreCase );

		return requestedIds
			.Where( knownById.ContainsKey )
			.Select( id => knownById[id] )
			.ToList();
	}

	public int GetPublishedVoteTotal( string stableMapId )
	{
		var ids = DecodeVoteFields( ActiveVoteCandidateIds );
		var totals = DecodeVoteFields( ActiveVoteCounts );

		for ( var index = 0; index < ids.Count; index++ )
		{
			if ( string.Equals(
				ids[index],
				stableMapId,
				StringComparison.OrdinalIgnoreCase ) )
			{
				return index < totals.Count &&
					int.TryParse( totals[index], out var total )
						? Math.Max( 0, total )
						: 0;
			}
		}

		return 0;
	}

	public void SelectInitialMap( string stableMapId )
	{
		if ( !ShowInitialMapSelection )
			return;

		var descriptor = GetKnownMapDescriptors().FirstOrDefault( candidate =>
			string.Equals(
				LargeLadMapRotationRules.GetMapIdentity( candidate ),
				stableMapId,
				StringComparison.OrdinalIgnoreCase ) );

		if ( descriptor is null )
		{
			Log.Warning(
				$"Rejected initial map selection '{stableMapId}': it is not a " +
				"valid official-catalog descriptor." );
			return;
		}

		BeginMapSelectionTransaction(
			descriptor.MapInstanceIdentifier,
			isRuntimeTransition: false );
	}

	public void SubmitMapVote( string stableMapId )
	{
		if ( MapFlowState != LargeLadMapFlowState.Voting ||
			string.IsNullOrWhiteSpace( stableMapId ) )
		{
			return;
		}

		RequestMapVote( stableMapId.Trim() );
	}

	internal void NotifyRoundCompleted(
		LargeLadGameManager manager,
		LargeLadWinner winner )
	{
		if ( !Networking.IsHost ||
			manager is null ||
			manager != GameManager ||
			MapFlowState != LargeLadMapFlowState.Playing )
		{
			return;
		}

		CompletedRoundsOnCurrentMap =
			LargeLadMapRotationRules.UpdateCompletedRoundCount(
				CompletedRoundsOnCurrentMap,
				winner != LargeLadWinner.None );

		if ( LargeLadMapRotationRules.ShouldOpenVote(
			CompletedRoundsOnCurrentMap,
			RoundsPerMap ) )
		{
			BeginMapVote();
		}
	}

	internal void NotifyMapLoadAttemptStarted( string mapIdentifier )
	{
		if ( !Networking.IsHost )
			return;

		var identifier = mapIdentifier?.Trim() ?? string.Empty;
		if ( !mapSelectionTransactionActive )
		{
			BeginMapSelectionTransactionState(
				identifier,
				isRuntimeTransition: IsMapReady );
		}

		attemptedTransitionMaps.Add( identifier );
		mapLoadDeadline = Time.Now + MathF.Max( 1.0f, MapLoadTimeout );
	}

	internal void HandleMapBecameReady( LargeLadMapDescriptor descriptor )
	{
		if ( !Networking.IsHost || descriptor is null )
			return;

		mapLoadDeadline = 0.0f;
		lastKnownGoodMapIdentifier = descriptor.MapInstanceIdentifier;
		CompletedRoundsOnCurrentMap =
			LargeLadMapRotationRules.UpdateCountForMapReadiness(
				CompletedRoundsOnCurrentMap,
				replacementMapBecameReady: true );
		mapSelectionTransactionActive = false;
		attemptedTransitionMaps.Clear();
		SetMapFlowState( LargeLadMapFlowState.Playing );
	}

	internal void HandleSelectedMapFailure( string reason )
	{
		if ( !Networking.IsHost )
			return;

		mapLoadDeadline = 0.0f;
		var failedMap = CurrentMapName?.Trim() ?? string.Empty;
		CurrentMapDescriptor = null;
		attemptedTransitionMaps.Add( failedMap );
		Log.Error(
			$"Large Lad map '{failedMap}' failed: " +
			$"{(string.IsNullOrWhiteSpace( reason ) ? "unknown failure" : reason)}." );

		var fallback = LargeLadMapRotationRules.SelectFallbackIdentifier(
			lastKnownGoodMapIdentifier,
			StartupMap,
			GetKnownMapDescriptors().Select( descriptor =>
				descriptor.MapInstanceIdentifier ),
			attemptedTransitionMaps );

		if ( string.IsNullOrWhiteSpace( fallback ) )
		{
			mapSelectionTransactionActive = false;
			SetMapFlowState( LargeLadMapFlowState.Failed );
			Log.Error(
				"Large Lad exhausted its bounded known-good map fallbacks. " +
				"Round flow remains closed; no failed map will be retried forever." );
			return;
		}

		mapSelectionTransactionActive = true;
		SetMapFlowState( LargeLadMapFlowState.Recovering );
		Log.Warning(
			$"Attempting Large Lad fallback map '{fallback}' after " +
			$"'{failedMap}' failed." );
		LoadMap( fallback );
	}

	private void BeginMapVote()
	{
		if ( !Networking.IsHost ||
			MapFlowState != LargeLadMapFlowState.Playing ||
			!IsMapReady )
		{
			return;
		}

		var knownDescriptors = GetKnownMapDescriptors().ToList();
		if ( CurrentMapDescriptor is not null &&
			knownDescriptors.All( descriptor => !string.Equals(
				LargeLadMapRotationRules.GetMapIdentity( descriptor ),
				LargeLadMapRotationRules.GetMapIdentity( CurrentMapDescriptor ),
				StringComparison.OrdinalIgnoreCase ) ) )
		{
			knownDescriptors.Add( CurrentMapDescriptor );
		}

		activeVoteDescriptors = LargeLadMapRotationRules
			.SelectVoteCandidates( knownDescriptors, CurrentMapDescriptor )
			.ToList();

		if ( activeVoteDescriptors.Count == 0 )
		{
			SetMapFlowState( LargeLadMapFlowState.Failed );
			Log.Error(
				"Map vote could not start because no valid normalized map " +
				"descriptors are available. Round flow remains closed." );
			return;
		}

		eligibleVoteConnections.Clear();
		foreach ( var connection in Connection.All.Where( connection =>
			connection is not null && connection.IsActive ) )
		{
			eligibleVoteConnections.Add( GetConnectionIdentity( connection ) );
		}

		submittedMapVotes.Clear();
		voteCompletionCommitted = false;
		VoteSessionId++;
		LocalSubmittedVoteId = string.Empty;
		ActiveVoteCandidateIds = EncodeVoteFields(
			activeVoteDescriptors.Select(
				LargeLadMapRotationRules.GetMapIdentity ) );
		VoteEndsAt = Time.Now + MathF.Max( 1.0f, VoteDuration );
		SetMapFlowState( LargeLadMapFlowState.Voting );
		PublishVoteTotals();
		Log.Info(
			$"Map vote {VoteSessionId} opened for " +
			$"{eligibleVoteConnections.Count} connected eligible player(s). " +
			"Players joining after this snapshot are spectators for this vote." );
	}

	[Rpc.Host]
	private void RequestMapVote( string stableMapId )
	{
		if ( !Networking.IsHost ||
			MapFlowState != LargeLadMapFlowState.Voting ||
			voteCompletionCommitted )
		{
			return;
		}

		var caller = Rpc.Caller;
		if ( caller is null || !caller.IsActive )
			return;

		var voterIdentity = GetConnectionIdentity( caller );
		if ( !LargeLadMapRotationRules.TryCastVote(
			eligibleVoteConnections,
			submittedMapVotes,
			activeVoteDescriptors,
			voterIdentity,
			stableMapId ) )
		{
			return;
		}

		var canonicalVote = submittedMapVotes[voterIdentity];
		PublishVoteTotals();
		if ( Connection.Local is not null &&
			string.Equals(
				voterIdentity,
				GetConnectionIdentity( Connection.Local ),
				StringComparison.OrdinalIgnoreCase ) )
		{
			LocalSubmittedVoteId = canonicalVote;
		}
		ReceiveAcceptedVote( voterIdentity, canonicalVote, VoteSessionId );
		UpdateActiveVote();
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	private void ReceiveAcceptedVote(
		string voterIdentity,
		string stableMapId,
		int voteSessionId )
	{
		if ( voteSessionId != VoteSessionId || Connection.Local is null )
			return;

		if ( string.Equals(
			voterIdentity,
			GetConnectionIdentity( Connection.Local ),
			StringComparison.OrdinalIgnoreCase ) )
		{
			LocalSubmittedVoteId = stableMapId;
		}
	}

	private void UpdateActiveVote()
	{
		if ( MapFlowState != LargeLadMapFlowState.Voting ||
			voteCompletionCommitted )
		{
			return;
		}

		var connectedVoters = GetConnectedVoteIdentities();
		var disconnectedVotes = submittedMapVotes.Keys
			.Where( voter => !connectedVoters.Contains( voter ) )
			.ToList();

		foreach ( var disconnectedVote in disconnectedVotes )
			submittedMapVotes.Remove( disconnectedVote );

		if ( disconnectedVotes.Count > 0 )
			PublishVoteTotals();

		var everyConnectedEligibleVoterSubmitted =
			LargeLadMapRotationRules.HaveAllConnectedEligibleVotersSubmitted(
				eligibleVoteConnections,
				connectedVoters,
				submittedMapVotes );
		var timerExpired = Time.Now >= VoteEndsAt;

		if ( timerExpired || everyConnectedEligibleVoterSubmitted )
			CompleteMapVote( connectedVoters );
	}

	private void CompleteMapVote( ISet<string> connectedVoters )
	{
		if ( voteCompletionCommitted )
			return;

		voteCompletionCommitted = true;
		var winner = LargeLadMapRotationRules.SelectWinner(
			activeVoteDescriptors,
			submittedMapVotes,
			connectedVoters,
			Game.Random.Int( 0, int.MaxValue ) );

		if ( winner is null )
		{
			SetMapFlowState( LargeLadMapFlowState.Failed );
			Log.Error(
				"Map vote ended without a resolvable candidate. Round flow " +
				"remains closed." );
			return;
		}

		LastWinningMapId = LargeLadMapRotationRules.GetMapIdentity( winner );
		VoteEndsAt = 0.0f;
		SetMapFlowState( LargeLadMapFlowState.Transitioning );
		Log.Info(
			$"Map vote {VoteSessionId} selected '{winner.DisplayName}' " +
			$"({winner.MapInstanceIdentifier})." );

		var winningMapIdentifier = winner.MapInstanceIdentifier;
		activeVoteDescriptors.Clear();
		eligibleVoteConnections.Clear();
		submittedMapVotes.Clear();
		BeginMapSelectionTransaction(
			winningMapIdentifier,
			isRuntimeTransition: true );
	}

	private void BeginMapSelectionTransaction(
		string mapIdentifier,
		bool isRuntimeTransition )
	{
		var selectedIdentifier = mapIdentifier?.Trim() ?? string.Empty;
		if ( string.IsNullOrWhiteSpace( selectedIdentifier ) )
			return;

		BeginMapSelectionTransactionState(
			selectedIdentifier,
			isRuntimeTransition );

		if ( string.Equals(
			CurrentMapName,
			selectedIdentifier,
			StringComparison.OrdinalIgnoreCase ) &&
			MapState == LargeLadMapSessionState.Ready )
		{
			ReloadCurrentMap();
			return;
		}

		LoadMap( selectedIdentifier );
	}

	private void BeginMapSelectionTransactionState(
		string mapIdentifier,
		bool isRuntimeTransition )
	{
		mapSelectionTransactionActive = true;
		attemptedTransitionMaps.Clear();
		mapLoadDeadline = 0.0f;
		SetMapFlowState(
			isRuntimeTransition
				? LargeLadMapFlowState.Transitioning
				: LargeLadMapFlowState.Loading );
	}

	private string ResolveConfiguredOrCuratedStartupMap()
	{
		if ( !string.IsNullOrWhiteSpace( StartupMap ) )
			return StartupMap.Trim();

		return GetKnownMapDescriptors()
			.FirstOrDefault()?.MapInstanceIdentifier;
	}

	private IReadOnlyList<LargeLadMapDescriptor> GetKnownMapDescriptors()
	{
		var descriptors = new List<LargeLadMapDescriptor>();

		foreach ( var entry in OfficialMapCatalog?.Entries ?? [] )
		{
			if ( entry is null ||
				!LargeLadMapCatalog.TryResolveOfficial(
					OfficialMapCatalog,
					entry.MapInstanceIdentifier,
					packageMetadata: null,
					out var descriptor,
					out _ ) )
			{
				continue;
			}

			descriptors.Add( descriptor );
		}

		return descriptors
			.GroupBy(
				LargeLadMapRotationRules.GetMapIdentity,
				StringComparer.OrdinalIgnoreCase )
			.Select( group => group.First() )
			.ToList();
	}

	private void PublishVoteTotals()
	{
		var connectedVoters = GetConnectedVoteIdentities();
		var totals = LargeLadMapRotationRules.CountConnectedVotes(
			activeVoteDescriptors,
			submittedMapVotes,
			connectedVoters );
		ActiveVoteCounts = EncodeVoteFields(
			activeVoteDescriptors.Select( descriptor =>
				totals[LargeLadMapRotationRules.GetMapIdentity( descriptor )]
					.ToString() ) );
	}

	private HashSet<string> GetConnectedVoteIdentities()
	{
		return Connection.All
			.Where( connection => connection is not null && connection.IsActive )
			.Select( GetConnectionIdentity )
			.ToHashSet( StringComparer.OrdinalIgnoreCase );
	}

	private static string GetConnectionIdentity( Connection connection )
	{
		return connection?.Id.ToString( "N" ) ?? string.Empty;
	}

	private static string EncodeVoteFields( IEnumerable<string> fields )
	{
		return string.Join(
			VoteFieldSeparator,
			(fields ?? []).Select( field => field?.Trim() ?? string.Empty ) );
	}

	private static IReadOnlyList<string> DecodeVoteFields( string encoded )
	{
		return string.IsNullOrWhiteSpace( encoded )
			? []
			: encoded.Split(
				VoteFieldSeparator,
				StringSplitOptions.RemoveEmptyEntries |
				StringSplitOptions.TrimEntries );
	}

	private void OnVoteSessionChanged( int oldSessionId, int newSessionId )
	{
		LocalSubmittedVoteId = string.Empty;
	}

	private void SetMapFlowState( LargeLadMapFlowState newState )
	{
		if ( !Networking.IsHost || MapFlowState == newState )
			return;

		var oldState = MapFlowState;
		MapFlowState = newState;
		Log.Info(
			$"Large Lad map flow changed from {oldState} to {newState}." );
	}
}
