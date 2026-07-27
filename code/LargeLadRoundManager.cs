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
	private bool spawnFailureReported;
	private readonly HashSet<LargeLadPlayer> lobbyPlacedPlayers = new();
	private readonly HashSet<(
		LargeLadPlayer Player,
		LargeLadSpawnGroup Group)> reportedSpawnAllocationFailures = new();

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
		spawnFailureReported = false;
	}

	protected override void OnUpdate()
	{
		if ( !Networking.IsHost )
			return;

		if ( MapDefinition is null )
		{
			MapDefinition = Scene
				.GetAllComponents<LargeLadMapDefinition>()
				.FirstOrDefault();

			if ( MapDefinition is null )
				return;

			UseMapDefinition( MapDefinition );
		}

		var players = Scene
			.GetAllComponents<LargeLadPlayer>()
			.ToList();
		lobbyPlacedPlayers.RemoveWhere( player =>
			player is null || !players.Contains( player ) || player.Role != LargeLadRole.Unassigned );
		reportedSpawnAllocationFailures.RemoveWhere( failure =>
			failure.Player is null || !players.Contains( failure.Player ) );

		switch ( Phase )
		{
			case LargeLadRoundPhase.WaitingForPlayers:
				PlaceUnassignedPlayersInLobby( players );
				UpdateInactiveRespawns( players );

				if ( !LargeLadGameplayRules.HasMinimumPlayers(
					players.Count,
					MinimumPlayers ) )
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
					if ( !StartRound( players ) )
						playerReadyTimeRemaining = PlayerReadyDelay;
				}
				break;

			case LargeLadRoundPhase.HeadStart:
				AssignLateJoinersAsMinions( players );

				if ( EndRoundIfTeamIsMissing( players ) )
					break;

				UpdateLargeLadRespawn( players );
				UpdatePlayerRespawns( players );

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
				PlaceUnassignedPlayersInLobby( players );
				UpdateInactiveRespawns( players );

				if ( TickPhaseTimer() )
					FinishIntermission( players );
				break;
		}
	}

	private bool StartRound( List<LargeLadPlayer> players )
	{
		if ( MapDefinition is null )
		{
			Log.Warning( "Cannot start a round without a map definition." );
			return false;
		}

		if ( !MapDefinition.CanSafelyStartRound(
			logFailures: !spawnFailureReported ) )
		{
			spawnFailureReported = true;
			return false;
		}

		var largeLad = players[nextLargeLadIndex % players.Count];
		var hunterPlayers = new List<LargeLadPlayer> { largeLad };
		var skinnyKidPlayers = players
			.Where( player => player != largeLad )
			.ToList();
		var hunterAllocations = MapDefinition.AllocateSpawnBatch(
			LargeLadSpawnGroup.Hunter,
			hunterPlayers );
		var projectedHunterPositions = hunterAllocations.Values
			.Select( allocation => allocation.Position )
			.ToList();
		var skinnyKidAllocations = MapDefinition.AllocateSpawnBatch(
			LargeLadSpawnGroup.SkinnyKid,
			skinnyKidPlayers,
			hunterPlayers,
			projectedHunterPositions );
		var hunterComplete = LargeLadSpawnRules.HasCompleteBatchAllocation(
			hunterPlayers,
			hunterAllocations );
		var skinnyKidComplete = LargeLadSpawnRules.HasCompleteBatchAllocation(
			skinnyKidPlayers,
			skinnyKidAllocations );

		if ( !hunterComplete || !skinnyKidComplete )
		{
			if ( !spawnFailureReported )
			{
				Log.Error(
					"Round start aborted before changing gameplay state: " +
					$"Hunter allocations {hunterAllocations.Count}/" +
					$"{hunterPlayers.Count}, Skinny Kid allocations " +
					$"{skinnyKidAllocations.Count}/{skinnyKidPlayers.Count}. " +
					"Fix the authored spawn areas and rebuild projected candidates." );
			}

			spawnFailureReported = true;
			return false;
		}

		// Player-held exclusive items are cleared before their authored pickup
		// returns, so a reset can never create a second copy.
		foreach ( var player in players )
			player.Inventory?.ClearForRoundReset();

		ResetMapState();

		foreach ( var player in players )
		{
			player.ClearPendingRespawnRole();
			player.Role = LargeLadRole.SkinnyKid;
		}

		Winner = LargeLadWinner.None;

		largeLad.Role = LargeLadRole.LargeLad;
		nextLargeLadIndex = (nextLargeLadIndex + 1) % players.Count;
		lobbyPlacedPlayers.Clear();

		foreach ( var player in players )
		{
			var isLargeLad = player.Role == LargeLadRole.LargeLad;
			player.MovementLocked = isLargeLad;
		}

		ApplyTeleportAllocations( hunterPlayers, hunterAllocations );
		ApplyTeleportAllocations( skinnyKidPlayers, skinnyKidAllocations );

		foreach ( var player in players )
			player.Health?.ResetForCurrentRole();

		spawnFailureReported = false;
		PhaseTimeRemaining = HeadStartDuration;
		SetPhase( LargeLadRoundPhase.HeadStart );
		Log.Info( $"Round started with {players.Count} players and a {HeadStartDuration:0.#}-second head start." );
		return true;
	}

	private void BeginPlaying( List<LargeLadPlayer> players )
	{
		foreach ( var player in players )
			player.MovementLocked = false;

		PhaseTimeRemaining = RoundDuration;
		SetPhase( LargeLadRoundPhase.Playing );
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
		SetPhase( LargeLadRoundPhase.RoundOver );

		var players = Scene.GetAllComponents<LargeLadPlayer>().ToList();
		var returningPlayers = players
			.Where( player => player.Health?.IsDead != true )
			.ToList();
		var lobbyAllocations = MapDefinition?.AllocateSpawnBatch(
			LargeLadSpawnGroup.Lobby,
			returningPlayers );

		if ( LargeLadSpawnRules.HasCompleteBatchAllocation(
			returningPlayers,
			lobbyAllocations ) )
		{
			foreach ( var player in returningPlayers )
			{
				player.ClearPendingRespawnRole();
				player.Role = LargeLadRole.Unassigned;
				player.Health?.ResetForCurrentRole();
				player.MovementLocked = false;
				lobbyPlacedPlayers.Add( player );
			}

			ApplyTeleportAllocations( returningPlayers, lobbyAllocations );
		}
		else
		{
			foreach ( var player in returningPlayers )
				player.MovementLocked = true;

			Log.Error(
				"Lobby return allocation failed at round end: received " +
				$"{lobbyAllocations?.Count ?? 0}/{returningPlayers.Count} " +
				"required positions. Player roles, health, and inventories " +
				"were retained and movement remains locked." );
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

		if ( LargeLadGameplayRules.HasMinimumPlayers(
			players.Count,
			MinimumPlayers ) )
		{
			if ( StartRound( players ) )
				return;

			SetPhase( LargeLadRoundPhase.WaitingForPlayers );
			waitingPlayerCount = players.Count;
			playerReadyTimeRemaining = PlayerReadyDelay;
			return;
		}

		SetPhase( LargeLadRoundPhase.WaitingForPlayers );
		Log.Info( "Waiting for enough players to start the next round." );
	}

	private void AssignLateJoinersAsMinions( List<LargeLadPlayer> players )
	{
		foreach ( var player in players.Where( player => player.Role == LargeLadRole.Unassigned ) )
		{
			player.SetPendingRespawnRole( LargeLadRole.Minion );
			player.MovementLocked = true;

			if ( !TryTeleportPlayer(
				player,
				LargeLadSpawnGroup.Hunter,
				"joining the active round as a Minion",
				"unassigned, pending, and movement-locked" ) )
			{
				continue;
			}

			player.ClearPendingRespawnRole();
			player.Role = LargeLadRole.Minion;
			player.Health?.ResetForCurrentRole();
			player.MovementLocked = false;
			Log.Info( $"{player.GameObject.Name} joined the active round as a Minion." );
		}
	}

	private bool EndRoundIfTeamIsMissing( List<LargeLadPlayer> players )
	{
		var winner = LargeLadGameplayRules.DetermineWinnerWhenTeamIsMissing(
			players.Any( player =>
				GetEffectiveRoundRole( player ) == LargeLadRole.LargeLad ),
			players.Any( player =>
				GetEffectiveRoundRole( player ) == LargeLadRole.SkinnyKid ) );

		if ( winner == LargeLadWinner.None )
			return false;

		EndRound( winner );
		return true;
	}

	private void UpdateLargeLadRespawn( List<LargeLadPlayer> players )
	{
		var largeLad = players.FirstOrDefault( player => player.Role == LargeLadRole.LargeLad );
		var health = largeLad?.Health;

		if ( largeLad is null || health is null )
			return;

		var respawnRole = LargeLadGameplayRules.ResolveRespawnRole(
			largeLad.Role,
			largeLad.Role );

		if ( !health.IsDead && health.HasPendingLethalDamage )
		{
			largeLad.Inventory?.HandleDeath( largeLad.GameObject.WorldPosition );
			largeLad.MovementLocked = true;
			health.BeginRespawnCountdown( LargeLadRespawnDelay, true );
			Log.Info( $"The Large Lad was killed and will respawn in {LargeLadRespawnDelay:0.#} seconds." );
			return;
		}

		if ( !health.IsDead || !health.TickRespawnCountdown() )
			return;

		if ( !TryTeleportPlayer(
			largeLad,
			LargeLadSpawnGroup.Hunter,
			"respawning the Large Lad",
			"dead and movement-locked" ) )
		{
			return;
		}

		health.ResetForCurrentRole();
		largeLad.Inventory?.PrepareForRole( respawnRole );
		largeLad.MovementLocked = Phase == LargeLadRoundPhase.HeadStart;
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

		respawnRole = LargeLadGameplayRules.ResolveRespawnRole(
			player.Role,
			respawnRole );

		player.SetPendingRespawnRole( respawnRole );
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

			if ( !health.IsDead && health.HasPendingLethalDamage )
			{
				BeginPlayerRespawn( player, player.Role, true );
				continue;
			}

			if ( !health.IsDead || !health.TickRespawnCountdown() )
				continue;

			var respawnRole =
				player.PendingRespawnRole != LargeLadRole.Unassigned
					? player.PendingRespawnRole
					: player.Role;
			var group = respawnRole == LargeLadRole.Minion
				? LargeLadSpawnGroup.Hunter
				: LargeLadSpawnGroup.SkinnyKid;

			if ( !TryTeleportPlayer(
				player,
				group,
				$"respawning as {GetRoleName( respawnRole )}",
				"dead with its pending role intact and movement locked" ) )
			{
				continue;
			}

			respawnRole = player.ApplyPendingRespawnRole();
			health.ResetForCurrentRole();
			player.Inventory?.PrepareForRole( respawnRole );
			player.MovementLocked = false;
			Log.Info( $"{player.GameObject.Name} respawned as {GetRoleName( respawnRole )}." );
		}
	}

	private void UpdateInactiveRespawns( List<LargeLadPlayer> players )
	{
		foreach ( var player in players )
		{
			var health = player.Health;

			if ( health is null || !health.IsDead || !health.TickRespawnCountdown() )
				continue;

			player.MovementLocked = true;

			if ( !TryTeleportPlayer(
				player,
				LargeLadSpawnGroup.Lobby,
				"respawning in the Lobby",
				"dead with its pending role intact and movement locked" ) )
			{
				continue;
			}

			player.ClearPendingRespawnRole();
			player.Role = LargeLadRole.Unassigned;
			health.ResetForCurrentRole();
			player.Inventory?.PrepareForRole( LargeLadRole.Unassigned );
			player.MovementLocked = false;
			lobbyPlacedPlayers.Add( player );
			Log.Info( $"{player.GameObject.Name} respawned in the waiting area." );
		}
	}

	private void PlaceUnassignedPlayersInLobby( IReadOnlyList<LargeLadPlayer> players )
	{
		foreach ( var player in players.Where( player =>
			player.Role == LargeLadRole.Unassigned &&
			player.Health?.IsDead != true &&
			!lobbyPlacedPlayers.Contains( player ) ) )
		{
			player.MovementLocked = true;

			if ( !TryTeleportPlayer(
				player,
				LargeLadSpawnGroup.Lobby,
				"entering the Lobby",
				"unassigned and movement-locked" ) )
			{
				continue;
			}

			player.MovementLocked = false;
			lobbyPlacedPlayers.Add( player );
		}
	}

	private void ResetMapState()
	{
		// Query destructibles explicitly so disabled renderers and colliders do
		// not prevent their gameplay components from participating in reset.
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

	private static void ApplyTeleportAllocations(
		IReadOnlyList<LargeLadPlayer> players,
		IReadOnlyDictionary<LargeLadPlayer, LargeLadSpawnLocation> allocations )
	{
		foreach ( var player in players )
		{
			var spawn = allocations[player];
			player.BeginAuthoritativeTeleport();
			player.TeleportTo( spawn.Position, spawn.Rotation );
		}
	}

	private bool TryTeleportPlayer(
		LargeLadPlayer player,
		LargeLadSpawnGroup group,
		string action,
		string retainedState )
	{
		if ( MapDefinition is null ||
			!MapDefinition.TryAllocateSpawn( group, player, out var spawn ) )
		{
			var failure = (Player: player, Group: group);

			if ( reportedSpawnAllocationFailures.Add( failure ) )
			{
				Log.Error(
					$"Spawn allocation failed for '{player.GameObject.Name}' " +
					$"while {action}: no safe cached {group} position is " +
					$"available. The player remains {retainedState}. Fix the " +
					"authored spawn area and rebuild projected candidates." );
			}

			return false;
		}

		reportedSpawnAllocationFailures.Remove( (player, group) );
		player.BeginAuthoritativeTeleport();
		player.TeleportTo( spawn.Position, spawn.Rotation );
		return true;
	}

	private void OnPhaseChanged( LargeLadRoundPhase oldPhase, LargeLadRoundPhase newPhase )
	{
		Log.Info( $"Round phase changed from {oldPhase} to {newPhase}." );
	}

	private void SetPhase( LargeLadRoundPhase nextPhase )
	{
		if ( !LargeLadGameplayRules.CanTransitionRoundPhase( Phase, nextPhase ) )
		{
			Log.Warning(
				$"Ignored invalid round phase transition from {Phase} to {nextPhase}." );
			return;
		}

		Phase = nextPhase;
	}

	private static LargeLadRole GetEffectiveRoundRole( LargeLadPlayer player )
	{
		return LargeLadGameplayRules.GetEffectiveRoundRole(
			player.Role,
			player.PendingRespawnRole );
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
