using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// A player's private preference for the next Large Lad assignment. Values are
/// deliberately explicit because untrusted RPC payloads are validated against
/// this enum before they can enter host state.
/// </summary>
public enum LargeLadRolePreference
{
	NoPreference = 0,
	PreferLargeLad = 1,
	PreferSkinnyKid = 2
}

/// <summary>
/// Host-private fairness state which belongs to one persistent player object.
/// It lasts for that connection/session only and is never synchronized.
/// </summary>
public readonly struct LargeLadRoleSelectionHistory
{
	public LargeLadRoleSelectionHistory(
		bool hasCompletedFullRound,
		bool hasBeenLargeLad,
		long lastLargeLadSelectionOrdinal )
	{
		HasCompletedFullRound = hasCompletedFullRound;
		HasBeenLargeLad = hasBeenLargeLad;
		LastLargeLadSelectionOrdinal = lastLargeLadSelectionOrdinal;
	}

	public bool HasCompletedFullRound { get; }
	public bool HasBeenLargeLad { get; }
	public long LastLargeLadSelectionOrdinal { get; }
}

/// <summary>
/// Persistent host-session state for selection. Active-round participation is
/// tracked separately because it is deliberately discarded by an aborted map
/// transition, while every field here survives one.
/// </summary>
public readonly struct LargeLadRoleSelectionSessionState
{
	public LargeLadRoleSelectionSessionState(
		long successfulRoundOrdinal,
		string previousLargeLadIdentity,
		bool hasCommittedCurrentRoundStart,
		bool hasCompletedEligibilityRound,
		bool hasCapturedBootstrapRoster )
	{
		SuccessfulRoundOrdinal = successfulRoundOrdinal;
		PreviousLargeLadIdentity = previousLargeLadIdentity ?? string.Empty;
		HasCommittedCurrentRoundStart = hasCommittedCurrentRoundStart;
		HasCompletedEligibilityRound = hasCompletedEligibilityRound;
		HasCapturedBootstrapRoster = hasCapturedBootstrapRoster;
	}

	public long SuccessfulRoundOrdinal { get; }
	public string PreviousLargeLadIdentity { get; }
	public bool HasCommittedCurrentRoundStart { get; }
	public bool HasCompletedEligibilityRound { get; }
	public bool HasCapturedBootstrapRoster { get; }
}

/// <summary>
/// One normalized, immutable host candidate. Stable identity, eligibility,
/// previous-selection state, preference, and fairness history are all explicit
/// so selection never depends on roster position or registration order.
/// </summary>
public readonly struct LargeLadRoleSelectionCandidate
{
	public LargeLadRoleSelectionCandidate(
		string sessionIdentity,
		LargeLadRolePreference preference,
		bool isOrdinarilyEligible,
		bool isBootstrapEligible,
		bool wasPreviousLargeLad,
		bool hasBeenLargeLad,
		long lastLargeLadSelectionOrdinal )
	{
		SessionIdentity = sessionIdentity?.Trim() ?? string.Empty;
		Preference = LargeLadRoleSelectionRules.IsValidPreference( preference )
			? preference
			: LargeLadRolePreference.NoPreference;
		IsOrdinarilyEligible = isOrdinarilyEligible;
		IsBootstrapEligible = isBootstrapEligible;
		WasPreviousLargeLad = wasPreviousLargeLad;
		HasBeenLargeLad = hasBeenLargeLad;
		LastLargeLadSelectionOrdinal = lastLargeLadSelectionOrdinal;
	}

	public string SessionIdentity { get; }
	public LargeLadRolePreference Preference { get; }
	public bool IsOrdinarilyEligible { get; }
	public bool IsBootstrapEligible { get; }
	public bool IsEligible => IsOrdinarilyEligible || IsBootstrapEligible;
	public bool WasPreviousLargeLad { get; }
	public bool HasBeenLargeLad { get; }
	public long LastLargeLadSelectionOrdinal { get; }
}

/// <summary>
/// Pure host policy for preference validation, full-round eligibility, and fair
/// Large Lad selection. Runtime state lives on the persistent player/manager.
/// </summary>
public static class LargeLadRoleSelectionRules
{
	public static bool IsValidPreference( LargeLadRolePreference preference )
	{
		return preference is
			LargeLadRolePreference.NoPreference or
			LargeLadRolePreference.PreferLargeLad or
			LargeLadRolePreference.PreferSkinnyKid;
	}

	public static bool TryAcceptPreference(
		LargeLadRolePreference current,
		LargeLadRolePreference requested,
		out LargeLadRolePreference accepted )
	{
		accepted = IsValidPreference( current )
			? current
			: LargeLadRolePreference.NoPreference;

		if ( !IsValidPreference( requested ) )
			return false;

		accepted = requested;
		return true;
	}

	/// <summary>
	/// Ordinary eligibility wins. Before the first qualifying round completes,
	/// the first successful round may bootstrap its original roster. Once that
	/// roster is captured, later joiners cannot enter through the exception.
	/// </summary>
	public static bool IsEligibleForSelection(
		LargeLadRoleSelectionHistory history,
		LargeLadRoleSelectionSessionState session,
		bool isInCapturedBootstrapRoster )
	{
		return history.HasCompletedFullRound ||
			IsBootstrapEligible(
				history,
				session,
				isInCapturedBootstrapRoster );
	}

	public static bool IsBootstrapEligible(
		LargeLadRoleSelectionHistory history,
		LargeLadRoleSelectionSessionState session,
		bool isInCapturedBootstrapRoster )
	{
		return !history.HasCompletedFullRound &&
			!session.HasCompletedEligibilityRound &&
			(!session.HasCapturedBootstrapRoster ||
				isInCapturedBootstrapRoster);
	}

	/// <summary>
	/// Selects by eligibility, immediate-repeat exclusion, preference tier, then
	/// longest wait. A supplied value chooses only among genuine tied finalists;
	/// runtime callers supply host randomness while tests inject deterministic
	/// values. Stable-identity ordering only maps the random value to finalists,
	/// so incidental roster ordering cannot affect the result.
	/// </summary>
	public static LargeLadRoleSelectionCandidate? SelectLargeLadCandidate(
		IReadOnlyList<LargeLadRoleSelectionCandidate> candidates,
		int tieBreakValue )
	{
		if ( candidates is null || candidates.Count == 0 )
			return null;

		var eligible = candidates
			.Where( candidate =>
				candidate.IsEligible &&
				!string.IsNullOrWhiteSpace( candidate.SessionIdentity ) )
			.GroupBy(
				candidate => candidate.SessionIdentity,
				StringComparer.Ordinal )
			.Select( group => group.First() )
			.ToList();

		if ( eligible.Count == 0 )
			return null;

		if ( eligible.Count > 1 &&
			eligible.Any( candidate => candidate.WasPreviousLargeLad ) )
		{
			eligible = eligible
				.Where( candidate => !candidate.WasPreviousLargeLad )
				.ToList();
		}

		var tiers = new[]
		{
			LargeLadRolePreference.PreferLargeLad,
			LargeLadRolePreference.NoPreference,
			LargeLadRolePreference.PreferSkinnyKid
		};
		List<LargeLadRoleSelectionCandidate> tierCandidates = null;

		foreach ( var tier in tiers )
		{
			tierCandidates = eligible
				.Where( candidate => candidate.Preference == tier )
				.ToList();

			if ( tierCandidates.Count > 0 )
				break;
		}

		if ( tierCandidates is null || tierCandidates.Count == 0 )
			return null;

		var neverSelected = tierCandidates
			.Where( candidate => !candidate.HasBeenLargeLad )
			.ToList();
		var longestWaitingOrdinal = tierCandidates.Min( candidate =>
			candidate.LastLargeLadSelectionOrdinal );
		var finalists = neverSelected.Count > 0
			? neverSelected
			: tierCandidates
				.Where( candidate =>
					candidate.LastLargeLadSelectionOrdinal ==
					longestWaitingOrdinal )
				.ToList();

		var stableFinalists = finalists
			.OrderBy(
				candidate => candidate.SessionIdentity,
				StringComparer.Ordinal )
			.ToList();
		var selectedIndex = (int)((uint)tieBreakValue % stableFinalists.Count);
		return stableFinalists[selectedIndex];
	}

	/// <summary>
	/// A failed spawn transaction or duplicate commit consumes no ordinal and
	/// changes no previous-player state.
	/// </summary>
	public static bool TryCommitSuccessfulRoundStart(
		LargeLadRoleSelectionSessionState current,
		string selectedIdentity,
		bool spawnAllocationSucceeded,
		out LargeLadRoleSelectionSessionState committed,
		out long selectionOrdinal )
	{
		committed = current;
		selectionOrdinal = 0;

		if ( !spawnAllocationSucceeded ||
			current.HasCommittedCurrentRoundStart ||
			string.IsNullOrWhiteSpace( selectedIdentity ) )
		{
			return false;
		}

		selectionOrdinal = checked(current.SuccessfulRoundOrdinal + 1);
		committed = new LargeLadRoleSelectionSessionState(
			selectionOrdinal,
			selectedIdentity,
			hasCommittedCurrentRoundStart: true,
			current.HasCompletedEligibilityRound,
			hasCapturedBootstrapRoster: true );
		return true;
	}

	public static LargeLadRoleSelectionHistory CommitLargeLadSelection(
		LargeLadRoleSelectionHistory current,
		long selectionOrdinal )
	{
		if ( selectionOrdinal <= 0 ||
			(current.HasBeenLargeLad &&
			current.LastLargeLadSelectionOrdinal >= selectionOrdinal) )
		{
			return current;
		}

		return new LargeLadRoleSelectionHistory(
			current.HasCompletedFullRound,
			hasBeenLargeLad: true,
			selectionOrdinal );
	}

	public static LargeLadRoleSelectionHistory CommitFullRoundCompletion(
		LargeLadRoleSelectionHistory current,
		bool wasPresentAtSuccessfulStart,
		bool isConnectedAtCompletion,
		bool roundCompletedSuccessfully )
	{
		if ( current.HasCompletedFullRound ||
			!wasPresentAtSuccessfulStart ||
			!isConnectedAtCompletion ||
			!roundCompletedSuccessfully )
		{
			return current;
		}

		return new LargeLadRoleSelectionHistory(
			hasCompletedFullRound: true,
			current.HasBeenLargeLad,
			current.LastLargeLadSelectionOrdinal );
	}

	public static LargeLadRoleSelectionSessionState MarkRoundCompleted(
		LargeLadRoleSelectionSessionState current,
		bool roundCompletedSuccessfully )
	{
		if ( !current.HasCommittedCurrentRoundStart ||
			!roundCompletedSuccessfully )
		{
			return current;
		}

		return new LargeLadRoleSelectionSessionState(
			current.SuccessfulRoundOrdinal,
			current.PreviousLargeLadIdentity,
			current.HasCommittedCurrentRoundStart,
			hasCompletedEligibilityRound: true,
			current.HasCapturedBootstrapRoster );
	}

	public static LargeLadRoleSelectionSessionState PrepareNextRound(
		LargeLadRoleSelectionSessionState current )
	{
		return ResetActiveRoundState( current );
	}

	public static LargeLadRoleSelectionSessionState AbortForMapTransition(
		LargeLadRoleSelectionSessionState current )
	{
		return ResetActiveRoundState( current );
	}

	public static LargeLadRoleSelectionSessionState ForgetDisconnectedPlayer(
		LargeLadRoleSelectionSessionState current,
		string disconnectedIdentity )
	{
		var previousIdentity = string.Equals(
			current.PreviousLargeLadIdentity,
			disconnectedIdentity,
			StringComparison.Ordinal )
			? string.Empty
			: current.PreviousLargeLadIdentity;

		return new LargeLadRoleSelectionSessionState(
			current.SuccessfulRoundOrdinal,
			previousIdentity,
			current.HasCommittedCurrentRoundStart,
			current.HasCompletedEligibilityRound,
			current.HasCapturedBootstrapRoster );
	}

	private static LargeLadRoleSelectionSessionState ResetActiveRoundState(
		LargeLadRoleSelectionSessionState current )
	{
		return new LargeLadRoleSelectionSessionState(
			current.SuccessfulRoundOrdinal,
			current.PreviousLargeLadIdentity,
			hasCommittedCurrentRoundStart: false,
			current.HasCompletedEligibilityRound,
			current.HasCapturedBootstrapRoster );
	}
}
