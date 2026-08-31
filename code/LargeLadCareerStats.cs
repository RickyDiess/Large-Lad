using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Exact published-package stat identifiers for Large Lad v1. Gameplay routes
/// only these values to an owning player's local s&box Stats service context.
/// </summary>
public static class LargeLadStatIds
{
	public const string RoundsPlayed = "rounds_played";
	public const string SkinnyRoundsPlayed = "skinny_rounds_played";
	public const string LargeLadRoundsPlayed = "large_lad_rounds_played";
	public const string SkinnyKidWins = "skinny_kid_wins";
	public const string LargeLadWins = "large_lad_wins";
	public const string MinionWins = "minion_wins";
	public const string LastSkinnyKidSurvivals = "last_skinny_kid_survivals";
	public const string PerfectLargeLadWins = "perfect_large_lad_wins";
	public const string Kills = "kills";
	public const string Assists = "assists";
	public const string Deaths = "deaths";
	public const string HeadshotKills = "headshot_kills";
	public const string SkinnyKidsEaten = "skinny_kids_eaten";
	public const string LargeLadKills = "large_lad_kills";
	public const string MinionKills = "minion_kills";
	public const string SkinnyKidDeaths = "skinny_kid_deaths";
	public const string LargeLadDeaths = "large_lad_deaths";
	public const string MinionDeaths = "minion_deaths";
	public const string Conversions = "conversions";
	public const string PistolKills = "pistol_kills";
	public const string SmgKills = "smg_kills";
	public const string ShotgunKills = "shotgun_kills";
	public const string RifleKills = "rifle_kills";
	public const string MeleeKills = "melee_kills";
	public const string DodgeballKills = "dodgeball_kills";
	public const string BarricadesDestroyed = "barricades_destroyed";
	public const string ShortcutsDestroyed = "shortcuts_destroyed";

	private static readonly string[] Identifiers =
	{
		RoundsPlayed,
		SkinnyRoundsPlayed,
		LargeLadRoundsPlayed,
		SkinnyKidWins,
		LargeLadWins,
		MinionWins,
		LastSkinnyKidSurvivals,
		PerfectLargeLadWins,
		Kills,
		Assists,
		Deaths,
		HeadshotKills,
		SkinnyKidsEaten,
		LargeLadKills,
		MinionKills,
		SkinnyKidDeaths,
		LargeLadDeaths,
		MinionDeaths,
		Conversions,
		PistolKills,
		SmgKills,
		ShotgunKills,
		RifleKills,
		MeleeKills,
		DodgeballKills,
		BarricadesDestroyed,
		ShortcutsDestroyed
	};

	private static readonly HashSet<string> KnownIdentifiers =
		new( Identifiers, StringComparer.Ordinal );

	public static IReadOnlyList<string> All => Identifiers;

	public static bool IsKnown( string identifier )
	{
		return !string.IsNullOrWhiteSpace( identifier ) &&
			KnownIdentifiers.Contains( identifier );
	}
}

public readonly record struct LargeLadStatDelta(
	string Identifier,
	int Amount = 1 );

public readonly record struct LargeLadRoundParticipantOutcome(
	string SessionIdentity,
	bool WasStarter,
	bool IsConnectedAtCompletion,
	LargeLadRole StartingRole,
	LargeLadRole EndingRole,
	bool IsLivingAtCompletion,
	bool IsCommittedLargeLad,
	bool BecameLastSkinnyKid );

/// <summary>
/// One defensive latch around successful round-outcome side effects. Aborted
/// rounds never call TryCommit, and reset only occurs after a successful start.
/// </summary>
public sealed class LargeLadRoundOutcomeCommitGate
{
	public bool HasCommitted { get; private set; }

	public bool TryCommit( bool roundCompletedSuccessfully )
	{
		if ( !roundCompletedSuccessfully || HasCommitted )
			return false;

		HasCommitted = true;
		return true;
	}

	public void ResetForSuccessfulRoundStart()
	{
		HasCommitted = false;
	}

	public void Abort()
	{
		HasCommitted = false;
	}
}

/// <summary>
/// Pure "which counters were earned?" rules. Backend calls remain in the
/// owning player component and are deliberately absent from this class.
/// </summary>
public static class LargeLadCareerStatRules
{
	public static bool CanSubmitInLocalServiceContext(
		bool isDedicatedServer,
		bool isOwnedByLocalPlayer,
		string identifier,
		int amount )
	{
		return !isDedicatedServer &&
			isOwnedByLocalPlayer &&
			amount > 0 &&
			LargeLadStatIds.IsKnown( identifier );
	}

	public static IReadOnlyList<LargeLadStatDelta> GetKillerDeltas(
		LargeLadDeathRecord death )
	{
		if ( death?.HasCreditedKiller != true )
			return Array.Empty<LargeLadStatDelta>();

		var deltas = new List<LargeLadStatDelta>
		{
			new( LargeLadStatIds.Kills )
		};

		if ( death.CreditedKillerRole == LargeLadRole.LargeLad )
			deltas.Add( new( LargeLadStatIds.LargeLadKills ) );
		else if ( death.CreditedKillerRole == LargeLadRole.Minion )
			deltas.Add( new( LargeLadStatIds.MinionKills ) );

		if ( death.WasEatExecution &&
			death.CreditedKillerRole == LargeLadRole.LargeLad &&
			death.VictimRole == LargeLadRole.SkinnyKid )
		{
			deltas.Add( new( LargeLadStatIds.SkinnyKidsEaten ) );
		}

		if ( death.IsFirearmHeadshot )
			deltas.Add( new( LargeLadStatIds.HeadshotKills ) );

		if ( death.WasEnvironmentalInfluenceKill ||
			death.DamageType == LargeLadDamageType.Environment )
		{
			return deltas;
		}

		if ( death.DamageType == LargeLadDamageType.Firearm )
		{
			if ( death.SourceWeapon == LargeLadWeaponId.Pistol )
				deltas.Add( new( LargeLadStatIds.PistolKills ) );
			else if ( death.SourceWeapon == LargeLadWeaponId.Smg )
				deltas.Add( new( LargeLadStatIds.SmgKills ) );
			else if ( death.SourceWeapon == LargeLadWeaponId.Shotgun )
				deltas.Add( new( LargeLadStatIds.ShotgunKills ) );
			else if ( death.SourceWeapon == LargeLadWeaponId.Rifle )
				deltas.Add( new( LargeLadStatIds.RifleKills ) );
		}
		else if ( death.DamageType == LargeLadDamageType.Melee )
		{
			deltas.Add( new( LargeLadStatIds.MeleeKills ) );
		}
		else if ( death.DamageType == LargeLadDamageType.Dodgeball )
		{
			deltas.Add( new( LargeLadStatIds.DodgeballKills ) );
		}

		return deltas;
	}

	public static IReadOnlyList<LargeLadStatDelta> GetVictimDeltas(
		LargeLadDeathRecord death )
	{
		if ( death is null )
			return Array.Empty<LargeLadStatDelta>();

		var deltas = new List<LargeLadStatDelta>
		{
			new( LargeLadStatIds.Deaths )
		};

		switch ( death.VictimRole )
		{
			case LargeLadRole.SkinnyKid:
				deltas.Add( new( LargeLadStatIds.SkinnyKidDeaths ) );
				break;
			case LargeLadRole.LargeLad:
				deltas.Add( new( LargeLadStatIds.LargeLadDeaths ) );
				break;
			case LargeLadRole.Minion:
				deltas.Add( new( LargeLadStatIds.MinionDeaths ) );
				break;
		}

		if ( death.ConvertedToMinion )
			deltas.Add( new( LargeLadStatIds.Conversions ) );

		return deltas;
	}

	public static IReadOnlyList<LargeLadStatDelta> GetAssistantDeltas()
	{
		return new[] { new LargeLadStatDelta( LargeLadStatIds.Assists ) };
	}

	public static IReadOnlyList<LargeLadStatDelta> GetRoundOutcomeDeltas(
		LargeLadWinner winner,
		bool roundCompletedSuccessfully,
		int committedLargeLadDeaths,
		LargeLadRoundParticipantOutcome participant )
	{
		if ( !roundCompletedSuccessfully ||
			winner == LargeLadWinner.None ||
			!participant.WasStarter ||
			!participant.IsConnectedAtCompletion ||
			string.IsNullOrWhiteSpace( participant.SessionIdentity ) )
		{
			return Array.Empty<LargeLadStatDelta>();
		}

		var deltas = new List<LargeLadStatDelta>
		{
			new( LargeLadStatIds.RoundsPlayed )
		};

		if ( participant.StartingRole == LargeLadRole.SkinnyKid )
			deltas.Add( new( LargeLadStatIds.SkinnyRoundsPlayed ) );
		else if ( participant.IsCommittedLargeLad )
			deltas.Add( new( LargeLadStatIds.LargeLadRoundsPlayed ) );

		if ( winner == LargeLadWinner.SkinnyKids &&
			participant.EndingRole == LargeLadRole.SkinnyKid &&
			participant.IsLivingAtCompletion )
		{
			deltas.Add( new( LargeLadStatIds.SkinnyKidWins ) );

			if ( participant.BecameLastSkinnyKid )
				deltas.Add( new( LargeLadStatIds.LastSkinnyKidSurvivals ) );
		}
		else if ( winner == LargeLadWinner.LargeLadTeam )
		{
			if ( participant.IsCommittedLargeLad )
			{
				deltas.Add( new( LargeLadStatIds.LargeLadWins ) );

				if ( committedLargeLadDeaths == 0 )
					deltas.Add( new( LargeLadStatIds.PerfectLargeLadWins ) );
			}

			if ( !participant.IsCommittedLargeLad &&
				participant.EndingRole == LargeLadRole.Minion )
				deltas.Add( new( LargeLadStatIds.MinionWins ) );
		}

		return deltas;
	}

	public static IReadOnlyList<LargeLadStatDelta>
		GetBarricadeDestructionDeltas(
			LargeLadBarricadeMode mode,
			LargeLadRole attackerRole,
			bool isFinalAuthoritativeDestruction )
	{
		if ( !isFinalAuthoritativeDestruction )
			return Array.Empty<LargeLadStatDelta>();

		return (mode, attackerRole) switch
		{
			(LargeLadBarricadeMode.SkinnyProgression,
				LargeLadRole.SkinnyKid) =>
					new[]
					{
						new LargeLadStatDelta(
							LargeLadStatIds.BarricadesDestroyed )
					},
			(LargeLadBarricadeMode.LadShortcut,
				LargeLadRole.LargeLad) =>
					new[]
					{
						new LargeLadStatDelta(
							LargeLadStatIds.ShortcutsDestroyed )
					},
			_ => Array.Empty<LargeLadStatDelta>()
		};
	}

	public static IReadOnlyList<LargeLadStatDelta> Aggregate(
		IEnumerable<LargeLadStatDelta> deltas )
	{
		return (deltas ?? Enumerable.Empty<LargeLadStatDelta>())
			.Where( delta =>
				delta.Amount > 0 &&
				LargeLadStatIds.IsKnown( delta.Identifier ) )
			.GroupBy( delta => delta.Identifier, StringComparer.Ordinal )
			.Select( group => new LargeLadStatDelta(
				group.Key,
				group.Sum( delta => delta.Amount ) ) )
			.OrderBy( delta => delta.Identifier, StringComparer.Ordinal )
			.ToArray();
	}
}
