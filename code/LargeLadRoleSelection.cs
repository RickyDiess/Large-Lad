using System.Collections.Generic;

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
/// Deterministic policy for validating preferences and choosing the next Large
/// Lad. The caller owns player identity and persistence; these rules only rank
/// the already-authoritative preference values.
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
	/// Chooses from willing players first, neutral players second, and players
	/// who prefer Skinny Kid only when every connected player prefers it. Within
	/// each tier, the existing round-robin cursor prevents a fixed roster order
	/// from always favoring the same eligible player.
	/// </summary>
	public static int SelectLargeLadIndex(
		IReadOnlyList<LargeLadRolePreference> preferences,
		int nextSelectionIndex )
	{
		if ( preferences is null || preferences.Count == 0 )
			return -1;

		var startIndex = NormalizeIndex(
			nextSelectionIndex,
			preferences.Count );
		var tiers = new[]
		{
			LargeLadRolePreference.PreferLargeLad,
			LargeLadRolePreference.NoPreference,
			LargeLadRolePreference.PreferSkinnyKid
		};

		foreach ( var tier in tiers )
		{
			for ( var offset = 0; offset < preferences.Count; offset++ )
			{
				var candidateIndex =
					(startIndex + offset) % preferences.Count;

				if ( preferences[candidateIndex] == tier )
					return candidateIndex;
			}
		}

		// Invalid values cannot be accepted into host state. Retaining a
		// deterministic fallback here keeps this pure helper total if a caller
		// supplies corrupt test or hotload state.
		return startIndex;
	}

	public static int GetNextSelectionIndex(
		int selectedIndex,
		int playerCount )
	{
		return playerCount <= 0 || selectedIndex < 0
			? 0
			: (selectedIndex + 1) % playerCount;
	}

	private static int NormalizeIndex( int index, int count )
	{
		var normalized = index % count;
		return normalized < 0 ? normalized + count : normalized;
	}
}
