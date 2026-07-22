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
	Runners,
	LargeLadTeam
}

public sealed class LargeLadRoundManager : Component
{
	[Property]
	public int MinimumPlayers { get; set; } = 2;

	[Property]
	public float HeadStartDuration { get; set; } = 10.0f;

	[Property]
	public float RoundDuration { get; set; } = 60.0f;

	[Property]
	public float IntermissionDuration { get; set; } = 5.0f;

	[Property]
	public float ConversionDistance { get; set; } = 48.0f;

	[Property]
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
				if ( players.Count >= MinimumPlayers )
				{
					StartTestRound( players );
				}
				break;

			case LargeLadRoundPhase.HeadStart:
				AssignLateJoiners( players );

				if ( TickPhaseTimer() )
				{
					BeginPlaying( players );
				}
				break;

			case LargeLadRoundPhase.Playing:
				AssignLateJoiners( players );
				ConvertTouchedRunners( players );

				if ( players.All( player => player.Role != LargeLadRole.Runner ) )
				{
					EndRound( LargeLadWinner.LargeLadTeam );
					break;
				}

				if ( TickPhaseTimer() )
				{
					EndRound( LargeLadWinner.Runners );
				}
				break;

			case LargeLadRoundPhase.RoundOver:
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
			player.Role = LargeLadRole.Runner;
		}

		Winner = LargeLadWinner.None;

		var largeLad = players[nextLargeLadIndex % players.Count];
		largeLad.Role = LargeLadRole.LargeLad;
		nextLargeLadIndex = ( nextLargeLadIndex + 1 ) % players.Count;

		var runnerIndex = 0;

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
				TeleportPlayer( player, RunnerSpawn, runnerIndex );
				runnerIndex++;
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
		Log.Info( $"Head start finished. The Large Lad can move. Runners must survive {RoundDuration:0.#} seconds." );
	}

	public void EndRound( LargeLadWinner winner )
	{
		if ( !Networking.IsHost || Phase != LargeLadRoundPhase.Playing )
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
			players[i].Role = LargeLadRole.Unassigned;
			players[i].MovementLocked = false;
			TeleportPlayer( players[i], initialSpawn, i );
		}

		Log.Info( $"Round over. {winner} won. Next round in {IntermissionDuration:0.#} seconds." );
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

	private void AssignLateJoiners( List<LargeLadPlayer> players )
	{
		var runnerIndex = players.Count( player => player.Role == LargeLadRole.Runner );

		foreach ( var player in players.Where( player => player.Role == LargeLadRole.Unassigned ) )
		{
			player.Role = LargeLadRole.Runner;
			player.MovementLocked = false;
			TeleportPlayer( player, RunnerSpawn, runnerIndex );
			runnerIndex++;
		}
	}

	private void ConvertTouchedRunners( List<LargeLadPlayer> players )
	{
		var hunters = players
			.Where( player => player.Role is LargeLadRole.LargeLad or LargeLadRole.Minion )
			.ToList();

		var runners = players
			.Where( player => player.Role == LargeLadRole.Runner )
			.ToList();

		var conversionDistanceSquared = ConversionDistance * ConversionDistance;

		foreach ( var hunter in hunters )
		{
			foreach ( var runner in runners )
			{
				if ( runner.Role != LargeLadRole.Runner )
					continue;

				var distanceSquared = hunter.GameObject.WorldPosition
					.DistanceSquared( runner.GameObject.WorldPosition );

				if ( distanceSquared > conversionDistanceSquared )
					continue;

				runner.Role = LargeLadRole.Minion;
				runner.MovementLocked = false;

				Log.Info( $"{hunter.GameObject.Name} converted {runner.GameObject.Name} into a Minion." );
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

	private void TeleportPlayer( LargeLadPlayer player, GameObject spawn, int runnerIndex = 0 )
	{
		if ( spawn is null )
		{
			Log.Warning( $"No spawn has been assigned for {player.Role}." );
			return;
		}

		var spacingOffset = spawn.WorldRotation.Right * runnerIndex * 40.0f;
		player.TeleportTo( spawn.WorldPosition + spacingOffset, spawn.WorldRotation );
	}

	private void OnPhaseChanged( LargeLadRoundPhase oldPhase, LargeLadRoundPhase newPhase )
	{
		Log.Info( $"Round phase changed from {oldPhase} to {newPhase}." );
	}
}
