using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// One bounded hostile contribution retained only on the host. The attacker
/// role and relationship are captured when damage legitimately applies so a
/// later role change cannot rewrite the original combat fact.
/// </summary>
public readonly record struct LargeLadDamageContribution(
	string VictimSessionIdentity,
	LargeLadRole VictimRole,
	string AttackerSessionIdentity,
	LargeLadRole AttackerRole,
	int RoundSequenceId,
	float LastAppliedAt,
	float TotalAppliedDamage,
	LargeLadDamageType LastDamageType,
	LargeLadWeaponId LastSourceWeapon );

/// <summary>
/// The one record produced after a player death has committed. Every Stage 5
/// consumer receives these facts rather than trying to reconstruct a kill from
/// weapon callbacks or synchronized health later.
/// </summary>
public sealed class LargeLadDeathRecord
{
	public int EventSequenceId { get; init; }
	public int RoundSequenceId { get; init; }
	public LargeLadPlayer Victim { get; init; }
	public string VictimSessionIdentity { get; init; }
	public string VictimDisplayName { get; init; }
	public LargeLadRole VictimRole { get; init; }
	public LargeLadPlayer CreditedKiller { get; init; }
	public string CreditedKillerSessionIdentity { get; init; }
	public string CreditedKillerDisplayName { get; init; }
	public LargeLadRole CreditedKillerRole { get; init; }
	public LargeLadKillfeedCause KillfeedCause { get; init; }
	public LargeLadWeaponId SourceWeapon { get; init; }
	public LargeLadHitRegion HitRegion { get; init; }
	public LargeLadDamageType DamageType { get; init; }
	public bool WasEatExecution { get; init; }
	public bool WasEnvironmentalInfluenceKill { get; init; }
	public bool ConvertedToMinion { get; init; }
	public IReadOnlyList<string> AssistantSessionIdentities { get; init; } =
		Array.Empty<string>();

	public bool HasCreditedKiller =>
		!string.IsNullOrWhiteSpace( CreditedKillerSessionIdentity );

	public bool IsFirearmHeadshot =>
		!WasEnvironmentalInfluenceKill &&
		DamageType == LargeLadDamageType.Firearm &&
		HitRegion == LargeLadHitRegion.Head &&
		LargeLadWeaponCatalog.IsFirearm( SourceWeapon );
}

/// <summary>
/// Local-only presentation snapshot received from the host's committed death.
/// It is intentionally not synchronized state and expires after a few seconds.
/// </summary>
public sealed class LargeLadKillfeedEntry
{
	public int EventSequenceId { get; init; }
	public string KillerDisplayName { get; init; }
	public string VictimDisplayName { get; init; }
	public string CauseLabel { get; init; }
	public LargeLadKillfeedCause Cause { get; init; }
	public bool WasEnvironmentalInfluenceKill { get; init; }
	public float ExpiresAt { get; init; }

	public bool HasCreditedKiller =>
		!string.IsNullOrWhiteSpace( KillerDisplayName );
}

/// <summary>
/// Pure role, recency, direct-credit, and assist rules shared by runtime and
/// deterministic tests.
/// </summary>
public static class LargeLadCombatAttributionRules
{
	public const float DefaultInfluenceWindow = 7.0f;

	public static bool AreOpposingRoles(
		LargeLadRole attackerRole,
		LargeLadRole victimRole )
	{
		var attackerIsSkinny = attackerRole == LargeLadRole.SkinnyKid;
		var victimIsSkinny = victimRole == LargeLadRole.SkinnyKid;
		var attackerIsHunter =
			attackerRole is LargeLadRole.LargeLad or LargeLadRole.Minion;
		var victimIsHunter =
			victimRole is LargeLadRole.LargeLad or LargeLadRole.Minion;

		return (attackerIsSkinny && victimIsHunter) ||
			(attackerIsHunter && victimIsSkinny);
	}

	public static bool IsValidContribution(
		string victimSessionIdentity,
		LargeLadRole victimRole,
		string attackerSessionIdentity,
		LargeLadRole attackerRole,
		float appliedDamage )
	{
		return !string.IsNullOrWhiteSpace( victimSessionIdentity ) &&
			!string.IsNullOrWhiteSpace( attackerSessionIdentity ) &&
			!string.Equals(
				victimSessionIdentity,
				attackerSessionIdentity,
				StringComparison.Ordinal ) &&
			float.IsFinite( appliedDamage ) &&
			appliedDamage > 0.0f &&
			AreOpposingRoles( attackerRole, victimRole );
	}

	public static bool IsDirectKillCreditEligible(
		string victimSessionIdentity,
		LargeLadRole victimRole,
		string attackerSessionIdentity,
		LargeLadRole attackerRole,
		LargeLadDamageType damageType,
		float appliedDamage )
	{
		return damageType != LargeLadDamageType.Environment &&
			IsValidContribution(
				victimSessionIdentity,
				victimRole,
				attackerSessionIdentity,
				attackerRole,
				appliedDamage );
	}

	public static bool IsRecent(
		LargeLadDamageContribution contribution,
		int roundSequenceId,
		float now,
		float influenceWindow = DefaultInfluenceWindow )
	{
		var age = now - contribution.LastAppliedAt;
		return contribution.RoundSequenceId == roundSequenceId &&
			float.IsFinite( now ) &&
			float.IsFinite( contribution.LastAppliedAt ) &&
			float.IsFinite( influenceWindow ) &&
			influenceWindow >= 0.0f &&
			age >= 0.0f &&
			age <= influenceWindow &&
			IsValidContribution(
				contribution.VictimSessionIdentity,
				contribution.VictimRole,
				contribution.AttackerSessionIdentity,
				contribution.AttackerRole,
				contribution.TotalAppliedDamage );
	}

	/// <summary>
	/// Environmental credit deterministically goes to the most recent valid
	/// hostile contributor. Stable session identity breaks an exact-time tie.
	/// </summary>
	public static LargeLadDamageContribution? ResolveEnvironmentalKiller(
		IEnumerable<LargeLadDamageContribution> contributions,
		int roundSequenceId,
		float now,
		float influenceWindow = DefaultInfluenceWindow )
	{
		return (contributions ?? Enumerable.Empty<LargeLadDamageContribution>())
			.Where( contribution => IsRecent(
				contribution,
				roundSequenceId,
				now,
				influenceWindow ) )
			.OrderByDescending( contribution => contribution.LastAppliedAt )
			.ThenBy(
				contribution => contribution.AttackerSessionIdentity,
				StringComparer.Ordinal )
			.Select( contribution =>
				(LargeLadDamageContribution?)contribution )
			.FirstOrDefault();
	}

	public static IReadOnlyList<string> ResolveAssistantIdentities(
		IEnumerable<LargeLadDamageContribution> contributions,
		string victimSessionIdentity,
		string creditedKillerSessionIdentity,
		int roundSequenceId,
		float now,
		float influenceWindow = DefaultInfluenceWindow )
	{
		return (contributions ?? Enumerable.Empty<LargeLadDamageContribution>())
			.Where( contribution =>
				IsRecent(
					contribution,
					roundSequenceId,
					now,
					influenceWindow ) &&
				!string.Equals(
					contribution.AttackerSessionIdentity,
					victimSessionIdentity,
					StringComparison.Ordinal ) &&
				!string.Equals(
					contribution.AttackerSessionIdentity,
					creditedKillerSessionIdentity,
					StringComparison.Ordinal ) )
			.Select( contribution => contribution.AttackerSessionIdentity )
			.Distinct( StringComparer.Ordinal )
			.OrderBy( identity => identity, StringComparer.Ordinal )
			.ToArray();
	}
}

/// <summary>
/// Small host-only store. One entry per attacker/victim pair is aggregated and
/// a committed death consumes the victim bucket, preventing previous-life
/// assists or inherited credit.
/// </summary>
public sealed class LargeLadRecentDamageStore
{
	private readonly Dictionary<
		string,
		Dictionary<string, LargeLadDamageContribution>> contributionsByVictim =
		new( StringComparer.Ordinal );

	public int VictimBucketCount => contributionsByVictim.Count;

	public bool Record(
		LargeLadDamageContribution contribution,
		float influenceWindow =
			LargeLadCombatAttributionRules.DefaultInfluenceWindow )
	{
		if ( !LargeLadCombatAttributionRules.IsValidContribution(
			contribution.VictimSessionIdentity,
			contribution.VictimRole,
			contribution.AttackerSessionIdentity,
			contribution.AttackerRole,
			contribution.TotalAppliedDamage ) ||
			!float.IsFinite( contribution.LastAppliedAt ) )
		{
			return false;
		}

		if ( !contributionsByVictim.TryGetValue(
			contribution.VictimSessionIdentity,
			out var victimContributions ) )
		{
			victimContributions = new Dictionary<
				string,
				LargeLadDamageContribution>( StringComparer.Ordinal );
			contributionsByVictim.Add(
				contribution.VictimSessionIdentity,
				victimContributions );
		}

		PruneBucket(
			victimContributions,
			contribution.RoundSequenceId,
			contribution.LastAppliedAt,
			influenceWindow );

		if ( victimContributions.TryGetValue(
			contribution.AttackerSessionIdentity,
			out var existing ) &&
			existing.RoundSequenceId == contribution.RoundSequenceId )
		{
			var totalDamage = existing.TotalAppliedDamage +
				contribution.TotalAppliedDamage;
			if ( !float.IsFinite( totalDamage ) )
				totalDamage = float.MaxValue;

			contribution = contribution with
			{
				TotalAppliedDamage = totalDamage
			};
		}

		victimContributions[contribution.AttackerSessionIdentity] =
			contribution;
		return true;
	}

	public IReadOnlyList<LargeLadDamageContribution> Consume(
		string victimSessionIdentity,
		int roundSequenceId,
		float now,
		float influenceWindow =
			LargeLadCombatAttributionRules.DefaultInfluenceWindow )
	{
		if ( string.IsNullOrWhiteSpace( victimSessionIdentity ) ||
			!contributionsByVictim.Remove(
				victimSessionIdentity,
				out var victimContributions ) )
		{
			return Array.Empty<LargeLadDamageContribution>();
		}

		return victimContributions.Values
			.Where( contribution =>
				LargeLadCombatAttributionRules.IsRecent(
					contribution,
					roundSequenceId,
					now,
					influenceWindow ) )
			.OrderByDescending( contribution => contribution.LastAppliedAt )
			.ThenBy(
				contribution => contribution.AttackerSessionIdentity,
				StringComparer.Ordinal )
			.ToArray();
	}

	public void RemovePlayer( string sessionIdentity )
	{
		if ( string.IsNullOrWhiteSpace( sessionIdentity ) )
			return;

		contributionsByVictim.Remove( sessionIdentity );

		foreach ( var victimContributions in contributionsByVictim.Values )
			victimContributions.Remove( sessionIdentity );

		foreach ( var emptyVictim in contributionsByVictim
			.Where( pair => pair.Value.Count == 0 )
			.Select( pair => pair.Key )
			.ToArray() )
		{
			contributionsByVictim.Remove( emptyVictim );
		}
	}

	public void Prune(
		int roundSequenceId,
		float now,
		float influenceWindow =
			LargeLadCombatAttributionRules.DefaultInfluenceWindow )
	{
		foreach ( var victimEntry in contributionsByVictim.ToArray() )
		{
			PruneBucket(
				victimEntry.Value,
				roundSequenceId,
				now,
				influenceWindow );

			if ( victimEntry.Value.Count == 0 )
				contributionsByVictim.Remove( victimEntry.Key );
		}
	}

	public void Clear()
	{
		contributionsByVictim.Clear();
	}

	private static void PruneBucket(
		Dictionary<string, LargeLadDamageContribution> contributions,
		int roundSequenceId,
		float now,
		float influenceWindow )
	{
		foreach ( var staleAttacker in contributions
			.Where( pair => !LargeLadCombatAttributionRules.IsRecent(
				pair.Value,
				roundSequenceId,
				now,
				influenceWindow ) )
			.Select( pair => pair.Key )
			.ToArray() )
		{
			contributions.Remove( staleAttacker );
		}
	}
}

public static class LargeLadKillfeedPresentationRules
{
	public static string GetCauseLabel(
		LargeLadKillfeedCause cause )
	{
		return cause switch
		{
			LargeLadKillfeedCause.FirearmHeadshot => "HEADSHOT",
			LargeLadKillfeedCause.Firearm => "SHOT",
			LargeLadKillfeedCause.Melee => "MELEE",
			LargeLadKillfeedCause.Eat => "ATE",
			LargeLadKillfeedCause.Environment => "ENVIRONMENT",
			LargeLadKillfeedCause.Dodgeball => "DODGEBALL",
			_ => "DEFEATED"
		};
	}
}
