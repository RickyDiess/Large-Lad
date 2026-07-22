using Sandbox;
using System.Collections.Generic;
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
	public float LargeLadRespawnDelay { get; set; } = 5.0f;

	[Property]
	public float PlayerRespawnDelay { get; set; } = 5.0f;

	[Property]
	public LargeLadMapDefinition MapDefinition { get; private set; }

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

	protected override void OnStart()
	{
		MapDefinition ??= Scene
			.GetAllComponents<LargeLadMapDefinition>()
			.FirstOrDefault();

		if ( MapDefinition is not null )
		{
			UseMapDefinition( MapDefinition );
		}
		else
		{
			Log.Warning( "Round manager has no LargeLadMapDefinition." );
		}
	}

	public void UseMapDefinition( LargeLadMapDefinition definition )
	{
		if ( definition is null )
			return;

		MapDefinition = definition;
		HeadStartDuration = definition.HeadStartDuration;
		RoundDuration = definition.SurvivalDuration;
		IntermissionDuration = definition.IntermissionDuration;
	}

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
					StartRound( players );
				break;

			case LargeLadRoundPhase.HeadStart:
				AssignLateJoinersAsMinions( players );

				if ( EndRoundIfTeamIsMissing( players ) )
					break;

				if ( TickPhaseTimer() )
					BeginPlaying( players );
				break;

			case LargeLadRoundPhase.Playing:
				AssignLateJoinersAsMinions( players );

				if ( EndRoundIfTeamIsMissing( players ) )
					break;

				UpdateLargeLadRespawn( players );
				UpdatePlayerRespawns( players );

				if ( EndRoundIfTeamIsMissing( players ) )
					break;

				if ( TickPhaseTimer() )
					EndRound( LargeLadWinner.SkinnyKids );
				break;

			case LargeLadRoundPhase.RoundOver:
				UpdateInactiveRespawns( players );

				if ( TickPhaseTimer() )
					FinishIntermission( players );
				break;
		}
	}

	private void StartRound( List<LargeLadPlayer> players )
	{
		if ( MapDefinition is null )
		{
			Log.Warning( "Cannot start a round without a map definition." );
			return;
		}

		// Player-held exclusive items are cleared before their authored pickup
		// returns, so a reset can never create a second copy.
		foreach ( var player in players )
			player.Inventory?.ClearForRoundReset();

		ResetMapState();

		foreach ( var player in players )
			player.Role = LargeLadRole.SkinnyKid;

		Winner = LargeLadWinner.None;

		var largeLad = players[nextLargeLadIndex % players.Count];
		largeLad.Role = LargeLadRole.LargeLad;
		nextLargeLadIndex = (nextLargeLadIndex + 1) % players.Count;

		foreach ( var player in players )
			player.Health?.ResetForCurrentRole();

		var skinnyKidIndex = 0;

		foreach ( var player in players )
		{
			var isLargeLad = player.Role == LargeLadRole.LargeLad;
			player.MovementLocked = isLargeLad;

			if ( isLargeLad )
			{
				TeleportPlayer( player, LargeLadSpawnGroup.Hunter, 0 );
			}
			else
			{
				TeleportPlayer( player, LargeLadSpawnGroup.SkinnyKid, skinnyKidIndex );
				skinnyKidIndex++;
			}
		}

		PhaseTimeRemaining = HeadStartDuration;
		Phase = LargeLadRoundPhase.HeadStart;
		Log.Info( $"Round started with {players.Count} players and a {HeadStartDuration:0.#}-second head start." );
	}

	private void BeginPlaying( List<LargeLadPlayer> players )
	{
		foreach ( var player in players )
			player.MovementLocked = false;

		PhaseTimeRemaining = RoundDuration;
		Phase = LargeLadRoundPhase.Playing;
		Log.Info( $"Head start finished. Skinny Kids must survive {RoundDuration:0.#} seconds." );
	}

	public void EndRound( LargeLadWinner winner )
	{
		if ( !Networking.IsHost ||
			(Phase != LargeLadRoundPhase.HeadStart && Phase != LargeLadRoundPhase.Playing) )
		{
			return;
		}

		Winner = winner;
		PhaseTimeRemaining = IntermissionDuration;
		Phase = LargeLadRoundPhase.RoundOver;

		var players = Scene.GetAllComponents<LargeLadPlayer>().ToList();

		for ( var i = 0; i < players.Count; i++ )
		{
			var player = players[i];

			if ( player.Health?.IsDead == true )
				continue;

			player.Role = LargeLadRole.Unassigned;
			player.MovementLocked = false;
			TeleportPlayer( player, LargeLadSpawnGroup.Lobby, i );
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
			StartRound( players );
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
			TeleportPlayer( player, LargeLadSpawnGroup.Hunter, hunterIndex );
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
		var largeLad = players.FirstOrDefault( player => player.Role == LargeLadRole.LargeLad );
		var health = largeLad?.Health;

		if ( largeLad is null || health is null )
			return;

		if ( !health.IsDead && health.CurrentHealth <= 0.0f )
		{
			largeLad.Inventory?.HandleDeath( largeLad.GameObject.WorldPosition );
			largeLad.MovementLocked = true;
			health.BeginRespawnCountdown( LargeLadRespawnDelay, true );
			Log.Info( $"The Large Lad was killed and will respawn in {LargeLadRespawnDelay:0.#} seconds." );
			return;
		}

		if ( !health.IsDead || !health.TickRespawnCountdown() )
			return;

		TeleportPlayer( largeLad, LargeLadSpawnGroup.Hunter, 0 );
		health.ResetForCurrentRole();
		largeLad.Inventory?.PrepareForRole( LargeLadRole.LargeLad );
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

		player.Inventory?.HandleDeath( player.GameObject.WorldPosition );

		if ( player.Role == LargeLadRole.SkinnyKid )
			respawnRole = LargeLadRole.Minion;

		player.Role = respawnRole;
		player.MovementLocked = true;
		player.Health.BeginRespawnCountdown( PlayerRespawnDelay, useRagdoll );
	}

	public void HandleKillVolumeDeath( LargeLadPlayer player )
	{
		if ( !Networking.IsHost || player?.Health is null || player.Health.IsDead ||
			Phase is not (LargeLadRoundPhase.HeadStart or LargeLadRoundPhase.Playing) )
		{
			return;
		}

		if ( player.Role == LargeLadRole.LargeLad )
		{
			var damage = new LargeLadDamageContext
			{
				AttackerRole = LargeLadRole.Unassigned,
				SourceWeapon = LargeLadWeaponId.None,
				DamageType = LargeLadDamageType.Environment,
				BaseDamage = 1000000.0f
			};
			player.Health.TryApplyDamage( damage, out _ );
			return;
		}

		BeginPlayerRespawn( player, player.Role, useRagdoll: false );
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

			var group = player.Role == LargeLadRole.Minion
				? LargeLadSpawnGroup.Hunter
				: LargeLadSpawnGroup.SkinnyKid;
			var spawnIndex = group == LargeLadSpawnGroup.Hunter
				? 1 + players.Where( other => other.Role == LargeLadRole.Minion ).ToList().IndexOf( player )
				: players.Where( other => other.Role == LargeLadRole.SkinnyKid ).ToList().IndexOf( player );

			TeleportPlayer( player, group, spawnIndex );
			health.ResetForCurrentRole();
			player.Inventory?.PrepareForRole( player.Role );
			player.MovementLocked = false;
			Log.Info( $"{player.GameObject.Name} respawned as {GetRoleName( player.Role )}." );
		}
	}

	private void UpdateInactiveRespawns( List<LargeLadPlayer> players )
	{
		for ( var i = 0; i < players.Count; i++ )
		{
			var player = players[i];
			var health = player.Health;

			if ( health is null || !health.IsDead || !health.TickRespawnCountdown() )
				continue;

			player.Role = LargeLadRole.Unassigned;
			TeleportPlayer( player, LargeLadSpawnGroup.Lobby, i );
			health.ResetForCurrentRole();
			player.Inventory?.PrepareForRole( LargeLadRole.Unassigned );
			player.MovementLocked = false;
			Log.Info( $"{player.GameObject.Name} respawned in the waiting area." );
		}
	}

	private void ResetMapState()
	{
		// Barricades keep their authoritative state on a networked child. Query
		// them explicitly so they cannot be skipped because their authored mesh
		// component was disabled when the previous round destroyed them.
		foreach ( var barricade in Scene.GetAllComponents<LargeLadBarricade>() )
		{
			barricade.ResetForRound();
		}

		// Core pickups are never globally consumed, but each keeps a host-side
		// per-player collection set for the current round. Reset these concrete
		// component types explicitly so every Skinny Kid can collect them again.
		foreach ( var pickup in Scene.GetAllComponents<LargeLadWeaponPickup>() )
		{
			pickup.ResetForRound();
		}

		foreach ( var ammoPickup in Scene.GetAllComponents<LargeLadAmmoPickup>() )
		{
			ammoPickup.ResetForRound();
		}

		foreach ( var component in Scene.GetAllComponents<Component>() )
		{
			if ( component is LargeLadBarricade or
				LargeLadWeaponPickup or LargeLadAmmoPickup )
				continue;

			if ( component is ILargeLadRoundResettable resettable )
				resettable.ResetForRound();
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

	private void TeleportPlayer(
		LargeLadPlayer player,
		LargeLadSpawnGroup group,
		int spawnIndex )
	{
		var spawn = MapDefinition?.GetSpawn( group, spawnIndex );

		if ( spawn is null )
		{
			Log.Warning( $"No explicit {group} spawn is available for {player.GameObject.Name}." );
			return;
		}

		player.TeleportTo( spawn.WorldPosition, spawn.WorldRotation );
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
