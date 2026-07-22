using Sandbox;
using System.Linq;

public enum LargeLadRoundPhase
{
	WaitingForPlayers,
	HeadStart,
	Playing,
	RoundOver
}

public enum LargeLadWinner
{
	None,
	SkinnyKids,
	LargeLadTeam
}

public sealed class LargeLadRoundManager : Component
{
	[Property]
	public int MinimumPlayers { get; set; } = 2;

	[Property, Title( "Round Start Padding" )]
	public float PlayerReadyDelay { get; set; } = 0.5f;

	[Property]
	public float HeadStartDuration { get; set; } = 10.0f;

	[Property]
	public float RoundDuration { get; set; } = 60.0f;

	[Property, Title( "Between-Round Padding" )]
	public float IntermissionDuration { get; set; } = 5.0f;

	[Property]
	public float ConversionDistance { get; set; } = 48.0f;

	[Property]
	public float LargeLadRespawnDelay { get; set; } = 5.0f;

	[Property]
	public float PlayerRespawnDelay { get; set; } = 5.0f;

	[Property, Title( "Skinny Kid Spawn" )]
	public GameObject RunnerSpawn { get; set; }

	[Property]
	public GameObject LargeLadSpawn { get; set; }

	[Sync( SyncFlags.FromHost ), Change( nameof( OnPhaseChanged ) )]
	public LargeLadRoundPhase Phase { get; private set; } =
		LargeLadRoundPhase.WaitingForPlayers;

	[Sync( SyncFlags.FromHost )]
	public float PhaseTimeRemaining { get; private set; }

	[Sync( SyncFlags.FromHost )]
	public LargeLadWinner Winner { get; private set; } = LargeLadWinner.None;

	private int nextLargeLadIndex;
	private int waitingPlayerCount = -1;
	private float playerReadyTimeRemaining;

	protected override void OnUpdate()
	{
		if ( !Networking.IsHost )
			return;

		var players = Scene
			.GetAllComponents<LargeLadPlayer>()
			.ToList();

		switch ( Phase )
		{
			case LargeLadRoundPhase.WaitingForPlayers:
				UpdateInactiveRespawns( players );

				if ( players.Count < MinimumPlayers )
				{
					waitingPlayerCount = players.Count;
					playerReadyTimeRemaining = PlayerReadyDelay;
					break;
				}

				if ( waitingPlayerCount != players.Count )
				{
					waitingPlayerCount = players.Count;
					playerReadyTimeRemaining = PlayerReadyDelay;
					break;
				}

				playerReadyTimeRemaining -= Time.Delta;

				if ( playerReadyTimeRemaining <= 0.0f )
				{
					StartTestRound( players );
				}
				break;

			case LargeLadRoundPhase.HeadStart:
				AssignLateJoinersAsMinions( players );

				if ( EndRoundIfTeamIsMissing( players ) )
					break;

				if ( TickPhaseTimer() )
				{
					BeginPlaying( players );
				}
				break;

			case LargeLadRoundPhase.Playing:
				AssignLateJoinersAsMinions( players );

				if ( EndRoundIfTeamIsMissing( players ) )
					break;

				UpdateLargeLadRespawn( players );
				UpdatePlayerRespawns( players );
				ConvertTouchedSkinnyKids( players );

				if ( EndRoundIfTeamIsMissing( players ) )
					break;

				if ( TickPhaseTimer() )
				{
					EndRound( LargeLadWinner.SkinnyKids );
				}
				break;

			case LargeLadRoundPhase.RoundOver:
				UpdateInactiveRespawns( players );

				if ( TickPhaseTimer() )
				{
					FinishIntermission( players );
				}
				break;
		}
	}

	private void StartTestRound( List<LargeLadPlayer> players )
	{
		foreach ( var player in players )
		{
			player.Role = LargeLadRole.SkinnyKid;
		}

		Winner = LargeLadWinner.None;

		var largeLad = players[nextLargeLadIndex % players.Count];
		largeLad.Role = LargeLadRole.LargeLad;
		nextLargeLadIndex = ( nextLargeLadIndex + 1 ) % players.Count;

		foreach ( var player in players )
		{
			player.Health?.ResetForCurrentRole();
		}

		var skinnyKidIndex = 0;

		foreach ( var player in players )
		{
			var isLargeLad = player.Role == LargeLadRole.LargeLad;
			player.MovementLocked = isLargeLad;

			if ( isLargeLad )
			{
				TeleportPlayer( player, LargeLadSpawn );
			}
			else
			{
				TeleportPlayer( player, RunnerSpawn, skinnyKidIndex );
				skinnyKidIndex++;
			}
		}

		PhaseTimeRemaining = HeadStartDuration;
		Phase = LargeLadRoundPhase.HeadStart;

		Log.Info( $"Large Lad test round started with {players.Count} players and a {HeadStartDuration:0.#}-second head start." );
	}

	private void BeginPlaying( List<LargeLadPlayer> players )
	{
		foreach ( var player in players )
		{
			player.MovementLocked = false;
		}

		PhaseTimeRemaining = RoundDuration;
		Phase = LargeLadRoundPhase.Playing;
		Log.Info( $"Head start finished. The Large Lad can move. Skinny Kids must survive {RoundDuration:0.#} seconds." );
	}

	public void EndRound( LargeLadWinner winner )
	{
		if ( !Networking.IsHost ||
			(Phase != LargeLadRoundPhase.HeadStart && Phase != LargeLadRoundPhase.Playing) )
			return;

		Winner = winner;
		PhaseTimeRemaining = IntermissionDuration;
		Phase = LargeLadRoundPhase.RoundOver;

		var players = Scene
			.GetAllComponents<LargeLadPlayer>()
			.ToList();

		var initialSpawn = GetInitialSpawn();

		for ( var i = 0; i < players.Count; i++ )
		{
			var player = players[i];

			if ( player.Health?.IsDead == true )
				continue;

			player.Role = LargeLadRole.Unassigned;
			player.MovementLocked = false;
			TeleportPlayer( player, initialSpawn, i );
			player.Health?.ResetForCurrentRole();
		}

		var winnerName = winner == LargeLadWinner.SkinnyKids
			? "Skinny Kids"
			: "Large Lad team";

		Log.Info( $"Round over. {winnerName} won. Next round in {IntermissionDuration:0.#} seconds." );
	}

	private void FinishIntermission( List<LargeLadPlayer> players )
	{
		PhaseTimeRemaining = 0.0f;
		Winner = LargeLadWinner.None;

		if ( players.Count >= MinimumPlayers )
		{
			StartTestRound( players );
			return;
		}

		Phase = LargeLadRoundPhase.WaitingForPlayers;
		Log.Info( "Waiting for enough players to start the next round." );
	}

	private void AssignLateJoinersAsMinions( List<LargeLadPlayer> players )
	{
		var hunterIndex = players.Count( player =>
			player.Role is LargeLadRole.LargeLad or LargeLadRole.Minion );

		foreach ( var player in players.Where( player => player.Role == LargeLadRole.Unassigned ) )
		{
			player.Role = LargeLadRole.Minion;
			player.MovementLocked = false;
			player.Health?.ResetForCurrentRole();
			TeleportPlayer( player, LargeLadSpawn, hunterIndex );
			hunterIndex++;

			Log.Info( $"{player.GameObject.Name} joined the active round as a Minion." );
		}
	}

	private bool EndRoundIfTeamIsMissing( List<LargeLadPlayer> players )
	{
		if ( players.All( player => player.Role != LargeLadRole.LargeLad ) )
		{
			EndRound( LargeLadWinner.SkinnyKids );
			return true;
		}

		if ( players.All( player => player.Role != LargeLadRole.SkinnyKid ) )
		{
			EndRound( LargeLadWinner.LargeLadTeam );
			return true;
		}

		return false;
	}

	private void UpdateLargeLadRespawn( List<LargeLadPlayer> players )
	{
		var largeLad = players.FirstOrDefault( player =>
			player.Role == LargeLadRole.LargeLad );
		var health = largeLad?.Health;

		if ( largeLad is null || health is null )
			return;

		if ( !health.IsDead && health.CurrentHealth <= 0.0f )
		{
			largeLad.MovementLocked = true;
			health.BeginRespawnCountdown( LargeLadRespawnDelay, true );

			Log.Info(
				$"The Large Lad was killed and will respawn in {LargeLadRespawnDelay:0.#} seconds." );
			return;
		}

		if ( !health.IsDead || !health.TickRespawnCountdown() )
			return;

		TeleportPlayer( largeLad, LargeLadSpawn );
		health.ResetForCurrentRole();
		largeLad.MovementLocked = false;

		Log.Info( "The Large Lad respawned." );
	}

	public void BeginPlayerRespawn(
		LargeLadPlayer player,
		LargeLadRole respawnRole,
		bool useRagdoll = true )
	{
		if ( !Networking.IsHost || player?.Health is null || player.Health.IsDead )
			return;

		// Infection is unconditional: once a Skinny Kid dies, they join the
		// Lad team regardless of which weapon, player, or hazard killed them.
		if ( player.Role == LargeLadRole.SkinnyKid )
		{
			respawnRole = LargeLadRole.Minion;
		}

		player.Role = respawnRole;
		player.MovementLocked = true;
		player.Health.BeginRespawnCountdown( PlayerRespawnDelay, useRagdoll );
	}

	private void UpdatePlayerRespawns( List<LargeLadPlayer> players )
	{
		foreach ( var player in players.Where( player =>
			player.Role != LargeLadRole.LargeLad &&
			player.Role != LargeLadRole.Unassigned ) )
		{
			var health = player.Health;

			if ( health is null )
				continue;

			if ( !health.IsDead && health.CurrentHealth <= 0.0f )
			{
				BeginPlayerRespawn( player, player.Role, true );
				continue;
			}

			if ( !health.IsDead || !health.TickRespawnCountdown() )
				continue;

			var spawn = player.Role == LargeLadRole.Minion
				? LargeLadSpawn
				: RunnerSpawn;
			var spawnIndex = players.Count( other =>
				other != player && other.Role == player.Role && other.Health?.IsDead != true );

			TeleportPlayer( player, spawn, spawnIndex );
			health.ResetForCurrentRole();
			player.MovementLocked = false;

			Log.Info( $"{player.GameObject.Name} respawned as {GetRoleName( player.Role )}." );
		}
	}

	private void UpdateInactiveRespawns( List<LargeLadPlayer> players )
	{
		var initialSpawn = GetInitialSpawn();

		for ( var i = 0; i < players.Count; i++ )
		{
			var player = players[i];
			var health = player.Health;

			if ( health is null || !health.IsDead || !health.TickRespawnCountdown() )
				continue;

			player.Role = LargeLadRole.Unassigned;
			TeleportPlayer( player, initialSpawn, i );
			health.ResetForCurrentRole();
			player.MovementLocked = false;

			Log.Info( $"{player.GameObject.Name} respawned in the waiting area." );
		}
	}

	private void ConvertTouchedSkinnyKids( List<LargeLadPlayer> players )
	{
		var hunters = players
			.Where( player =>
				(player.Role is LargeLadRole.LargeLad or LargeLadRole.Minion) &&
				player.Health?.IsDead != true )
			.ToList();

		var skinnyKids = players
			.Where( player =>
				player.Role == LargeLadRole.SkinnyKid &&
				player.Health?.IsDead != true )
			.ToList();

		var conversionDistanceSquared = ConversionDistance * ConversionDistance;

		foreach ( var hunter in hunters )
		{
			foreach ( var skinnyKid in skinnyKids )
			{
				if ( skinnyKid.Role != LargeLadRole.SkinnyKid )
					continue;

				var distanceSquared = hunter.GameObject.WorldPosition
					.DistanceSquared( skinnyKid.GameObject.WorldPosition );

				if ( distanceSquared > conversionDistanceSquared )
					continue;

				BeginPlayerRespawn( skinnyKid, LargeLadRole.Minion, false );

				Log.Info(
					$"{hunter.GameObject.Name} ate {skinnyKid.GameObject.Name}. " +
					$"They will respawn as a Minion in {PlayerRespawnDelay:0.#} seconds." );
				break;
			}
		}
	}

	private bool TickPhaseTimer()
	{
		PhaseTimeRemaining -= Time.Delta;

		if ( PhaseTimeRemaining > 0.0f )
			return false;

		PhaseTimeRemaining = 0.0f;
		return true;
	}

	private GameObject GetInitialSpawn()
	{
		var networkHelper = Scene
			.GetAllComponents<NetworkHelper>()
			.FirstOrDefault();

		var initialSpawn = networkHelper?.SpawnPoints?.FirstOrDefault();

		if ( initialSpawn is null )
		{
			Log.Warning( "NetworkHelper has no initial spawn configured." );
		}

		return initialSpawn;
	}

	private void TeleportPlayer( LargeLadPlayer player, GameObject spawn, int spawnIndex = 0 )
	{
		if ( spawn is null )
		{
			Log.Warning( $"No spawn has been assigned for {player.Role}." );
			return;
		}

		var spacingOffset = spawn.WorldRotation.Right * spawnIndex * 40.0f;
		player.TeleportTo( spawn.WorldPosition + spacingOffset, spawn.WorldRotation );
	}

	private void OnPhaseChanged( LargeLadRoundPhase oldPhase, LargeLadRoundPhase newPhase )
	{
		Log.Info( $"Round phase changed from {oldPhase} to {newPhase}." );
	}

	private static string GetRoleName( LargeLadRole role )
	{
		return role switch
		{
			LargeLadRole.SkinnyKid => "a Skinny Kid",
			LargeLadRole.Minion => "a Minion",
			LargeLadRole.LargeLad => "the Large Lad",
			_ => "unassigned"
		};
	}
}
