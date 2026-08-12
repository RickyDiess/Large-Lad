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

/// <summary>
/// Owns the complete Large Lad game lifecycle and map contract. The bootstrap
/// keeps spawn generation in LargeLadSpawnAllocator as its one focused helper.
/// </summary>
public sealed class LargeLadGameManager : Component
{
	public const int MinimumSupportedPlayerCount = 2;
	public const int TargetPlayerCount = 32;
	private const float BarricadeAnnouncementDuration = 3.5f;
	private const float LastSkinnyKidAnnouncementDuration = 4.0f;
	private readonly LargeLadRoundBalanceSettings defaultRoundBalanceSettings =
		new();

	[Property]
	public int MinimumPlayers { get; set; } = MinimumSupportedPlayerCount;

	[Property, Title( "Round Start Padding" )]
	public float PlayerReadyDelay { get; set; } = 0.5f;

	[Property]
	public float HeadStartDuration { get; set; } = 10.0f;

	[Property]
	public float SurvivalDuration { get; set; } = 60.0f;

	[Property, Title( "Between-Round Padding" )]
	public float IntermissionDuration { get; set; } = 5.0f;

	[Property]
	public float LargeLadRespawnDelay { get; set; } = 5.0f;

	[Property]
	public float PlayerRespawnDelay { get; set; } = 5.0f;

	[Property, Group( "Skinny Kid Survivability" ),
		Title( "Regeneration Delay" )]
	public float SkinnyKidRegenerationDelay { get; set; } =
		LargeLadSkinnyKidSurvivabilityRules.DefaultRegenerationDelay;

	[Property, Group( "Skinny Kid Survivability" ),
		Title( "Regeneration Rate (Health Per Second)" )]
	public float SkinnyKidRegenerationRate { get; set; } =
		LargeLadSkinnyKidSurvivabilityRules.DefaultRegenerationRate;

	[Property, Group( "Hunter Movement Escalation" ),
		Title( "Ramp Start (Normalized Round Time)" )]
	public float HunterMovementRampStartNormalizedTime { get; set; } =
		LargeLadHunterMovementEscalationRules
			.DefaultRampStartNormalizedTime;

	[Property, Group( "Hunter Movement Escalation" ),
		Title( "Ramp End (Normalized Round Time)" )]
	public float HunterMovementRampEndNormalizedTime { get; set; } =
		LargeLadHunterMovementEscalationRules
			.DefaultRampEndNormalizedTime;

	[Property, Group( "Hunter Movement Escalation" ),
		Title( "Large Lad Maximum Multiplier" )]
	public float LargeLadMovementMaximumMultiplier { get; set; } =
		LargeLadHunterMovementEscalationRules
			.DefaultLargeLadMaximumMultiplier;

	[Property, Group( "Hunter Movement Escalation" ),
		Title( "Minion Maximum Multiplier" )]
	public float MinionMovementMaximumMultiplier { get; set; } =
		LargeLadHunterMovementEscalationRules
			.DefaultMinionMaximumMultiplier;

	[Property, Group( "Round Balance" )]
	public LargeLadRoundBalanceSettings RoundBalanceSettings { get; set; }

	private LargeLadRoundBalanceSettings EffectiveRoundBalanceSettings =>
		RoundBalanceSettings ?? defaultRoundBalanceSettings;

	[Property]
	public NetworkHelper NetworkHelper { get; set; }

	[Property]
	public LargeLadSpawnAllocator SpawnAllocator { get; set; }

	[Property, Group( "Debug Logging" ), Title( "Melee" )]
	public bool EnableMeleeDebugLogging { get; set; } = false;

	[Property, Group( "Debug Logging" ), Title( "Pickups and Round Resets" )]
	public bool EnablePickupAndRoundResetDebugLogging { get; set; } = false;

	[Property, Group( "Debug Logging" ), Title( "Kill Volumes" )]
	public bool EnableKillVolumeDebugLogging { get; set; } = false;

	[Property, Group( "Debug Logging" ), Title( "Player Lifecycle" )]
	public bool EnablePlayerLifecycleDebugLogging { get; set; } = false;

	[Property, Group( "Debug Logging" ), Title( "Successful Map Validation" )]
	public bool EnableMapValidationDebugLogging { get; set; } = false;

	[Sync( SyncFlags.FromHost ), Change( nameof( OnPhaseChanged ) )]
	public LargeLadRoundPhase Phase { get; private set; } =
		LargeLadRoundPhase.WaitingForPlayers;

	[Sync( SyncFlags.FromHost )]
	public float PhaseEndTime { get; private set; }

	/// <summary>
	/// Replicated host timestamp for the beginning of the survival interval.
	/// Together with PhaseEndTime, this is the complete escalation state a
	/// movement owner or optional non-directional presentation cue consumes.
	/// </summary>
	[Sync( SyncFlags.FromHost )]
	public float SurvivalRoundStartTime { get; private set; }

	[Sync( SyncFlags.FromHost )]
	public LargeLadWinner Winner { get; private set; } = LargeLadWinner.None;

	[Sync( SyncFlags.FromHost )]
	public bool HasSelectedBalanceBand { get; private set; }

	[Sync( SyncFlags.FromHost )]
	public LargeLadBalanceBand SelectedBalanceBand { get; private set; } =
		LargeLadBalanceBand.Medium;

	[Sync( SyncFlags.FromHost )]
	public int SkinnyKidCountAtRoundStart { get; private set; }

	public bool HasBarricadeDestructionAnnouncement =>
		!string.IsNullOrWhiteSpace( barricadeDestructionAnnouncement ) &&
		timeSinceBarricadeDestructionAnnouncement <
			BarricadeAnnouncementDuration;

	public string BarricadeDestructionAnnouncement =>
		HasBarricadeDestructionAnnouncement
			? barricadeDestructionAnnouncement
			: null;

	/// <summary>
	/// The complete gameplay interval in which role survivability rules apply.
	/// </summary>
	public bool IsRoundActive =>
		Phase is LargeLadRoundPhase.HeadStart or LargeLadRoundPhase.Playing;

	public bool HasLastSkinnyKidAnnouncement =>
		IsRoundActive &&
		!string.IsNullOrWhiteSpace( lastSkinnyKidAnnouncement ) &&
		timeSinceLastSkinnyKidAnnouncement <
			LastSkinnyKidAnnouncementDuration;

	public string LastSkinnyKidAnnouncement =>
		HasLastSkinnyKidAnnouncement
			? lastSkinnyKidAnnouncement
			: null;

	/// <summary>
	/// Host-only lethal attribution hook for scoring and kill-feed systems.
	/// The damage envelope retains the attacker, weapon, shot sequence, hit
	/// region, and stable killfeed cause.
	/// </summary>
	public event System.Action<
		LargeLadPlayer,
		LargeLadDamageContext> AuthoritativePlayerKilled;

	public float PhaseTimeRemaining =>
		Phase == LargeLadRoundPhase.WaitingForPlayers
			? 0.0f
			: LargeLadGameplayRules.GetTimerTimeRemaining(
				PhaseEndTime,
				Time.Now );

	/// <summary>
	/// Elapsed survival-round time normalized from the replicated host interval.
	/// It is exactly zero outside Playing, including every between-round phase.
	/// </summary>
	public float NormalizedElapsedSurvivalRoundTime =>
		LargeLadHunterMovementEscalationRules
			.GetNormalizedElapsedSurvivalRoundTime(
				Phase == LargeLadRoundPhase.Playing,
				SurvivalRoundStartTime,
				PhaseEndTime,
				Time.Now );

	/// <summary>
	/// Timer-only role modifier derived from the host's replicated survival
	/// interval. No roster, map, objective, or conversion state is consulted.
	/// </summary>
	public float GetHunterMovementEscalationMultiplier(
		LargeLadRole role )
	{
		return LargeLadHunterMovementEscalationRules
			.GetMovementMultiplier(
				role,
				NormalizedElapsedSurvivalRoundTime,
				HunterMovementRampStartNormalizedTime,
				HunterMovementRampEndNormalizedTime,
				LargeLadMovementMaximumMultiplier,
				MinionMovementMaximumMultiplier );
	}

	/// <summary>
	/// Returns the current round's band factor composed with an optional future
	/// map-specific Large Lad health factor. Before the first successful round,
	/// balance remains neutral.
	/// </summary>
	public float GetLargeLadMaximumHealthMultiplier(
		float mapSpecificMultiplier = 1.0f )
	{
		var bandMultiplier = 1.0f;

		if ( HasSelectedBalanceBand &&
			EffectiveRoundBalanceSettings.TryGetMultipliers(
				SelectedBalanceBand,
				out var multipliers ) )
		{
			bandMultiplier = multipliers.LargeLadMaximumHealth;
		}

		return LargeLadRoundBalanceRules.ComposeHealthMultipliers(
			bandMultiplier,
			mapSpecificMultiplier );
	}

	/// <summary>
	/// Returns the current round's band factor composed with an optional future
	/// map-specific SkinnyProgression barricade health factor.
	/// </summary>
	public float GetSkinnyProgressionBarricadeMaximumHealthMultiplier(
		float mapSpecificMultiplier = 1.0f )
	{
		var bandMultiplier = 1.0f;

		if ( HasSelectedBalanceBand &&
			EffectiveRoundBalanceSettings.TryGetMultipliers(
				SelectedBalanceBand,
				out var multipliers ) )
		{
			bandMultiplier =
				multipliers.SkinnyProgressionBarricadeMaximumHealth;
		}

		return LargeLadRoundBalanceRules.ComposeHealthMultipliers(
			bandMultiplier,
			mapSpecificMultiplier );
	}

	internal bool PublishBarricadeDestructionAnnouncement(
		bool announcementEnabled,
		LargeLadBarricadeMode mode,
		string mapperDisplayName )
	{
		if ( !Networking.IsHost )
			return false;

		var message =
			LargeLadBarricadeStageRules.CreateDestructionAnnouncement(
				announcementEnabled,
				mode,
				mapperDisplayName );

		if ( string.IsNullOrWhiteSpace( message ) )
			return false;

		ReceiveBarricadeDestructionAnnouncement( message );
		BroadcastBarricadeDestructionAnnouncement( message );
		return true;
	}

	[Rpc.Broadcast]
	private void BroadcastBarricadeDestructionAnnouncement( string message )
	{
		// The host applied it before issuing the one broadcast.
		if ( Networking.IsHost )
			return;

		ReceiveBarricadeDestructionAnnouncement( message );
	}

	private void ReceiveBarricadeDestructionAnnouncement( string message )
	{
		barricadeDestructionAnnouncement = message;
		timeSinceBarricadeDestructionAnnouncement = 0.0f;
	}

	private void PublishLastSkinnyKidAnnouncement()
	{
		const string message = "LAST SKINNY KID";
		ReceiveLastSkinnyKidAnnouncement( message );
		BroadcastLastSkinnyKidAnnouncement( message );
	}

	[Rpc.Broadcast]
	private void BroadcastLastSkinnyKidAnnouncement( string message )
	{
		// The host applied it before issuing the one broadcast.
		if ( Networking.IsHost )
			return;

		ReceiveLastSkinnyKidAnnouncement( message );
	}

	private void ReceiveLastSkinnyKidAnnouncement( string message )
	{
		lastSkinnyKidAnnouncement = message;
		timeSinceLastSkinnyKidAnnouncement = 0.0f;
	}

	private int nextLargeLadIndex;
	private int waitingPlayerCount = -1;
	private float playerReadyTimeRemaining;
	private bool spawnFailureReported;
	private bool hasStarted;
	private bool isHydratingRegistrations;
	private bool hasSceneGameplayOwnership;
	private Scene registeredScene;
	private LargeLadPlayer currentLargeLad;
	private string barricadeDestructionAnnouncement;
	private TimeSince timeSinceBarricadeDestructionAnnouncement;
	private string lastSkinnyKidAnnouncement;
	private TimeSince timeSinceLastSkinnyKidAnnouncement;
	private int previousEffectiveLivingSkinnyKidCount;
	private bool hasAnnouncedLastSkinnyKidThisRound;
	private readonly List<LargeLadPlayer> activePlayers = new();
	private readonly HashSet<LargeLadPlayer> registeredPlayers = new();
	private readonly Dictionary<LargeLadRole, List<LargeLadPlayer>> playersByRole =
		new()
		{
			[LargeLadRole.Unassigned] = new(),
			[LargeLadRole.SkinnyKid] = new(),
			[LargeLadRole.LargeLad] = new(),
			[LargeLadRole.Minion] = new()
		};
	private readonly List<ILargeLadRoundResettable> roundResettables = new();
	private readonly HashSet<ILargeLadRoundResettable> registeredRoundResettables =
		new();
	private readonly List<LargeLadBarricade> activeBarricades = new();
	private readonly List<LargeLadEatSmashable> activeEatSmashables = new();
	private readonly List<LargeLadMinionPassage> activeMinionPassages = new();
	private readonly List<LargeLadGroundSlamReactiveProp>
		activeGroundSlamReactiveProps = new();
	private readonly List<string> validatedBlockingBootstrapIssues = new();
	private readonly HashSet<LargeLadPlayer> lobbyPlacedPlayers = new();
	private readonly HashSet<(
		LargeLadPlayer Player,
		LargeLadSpawnGroup Group)> reportedSpawnAllocationFailures = new();

	/// <summary>
	/// Enabled LargeLadPlayer components registered in this manager's scene.
	/// Invalid registrations are pruned without searching the scene.
	/// </summary>
	public IReadOnlyList<LargeLadPlayer> ActivePlayers
	{
		get
		{
			PruneInvalidRegistrations();
			return activePlayers;
		}
	}

	/// <summary>
	/// The registered player whose current role is Large Lad.
	/// </summary>
	public LargeLadPlayer CurrentLargeLad
	{
		get
		{
			PruneInvalidRegistrations();
			return currentLargeLad;
		}
	}

	/// <summary>
	/// Returns the active players currently indexed under the requested role.
	/// </summary>
	public IReadOnlyList<LargeLadPlayer> GetPlayersByRole( LargeLadRole role )
	{
		PruneInvalidRegistrations();
		return playersByRole.TryGetValue( role, out var players )
			? players
			: System.Array.Empty<LargeLadPlayer>();
	}

	/// <summary>
	/// Enabled round-reset participants registered in this manager's scene.
	/// </summary>
	public IReadOnlyList<ILargeLadRoundResettable> RoundResettables
	{
		get
		{
			PruneInvalidRegistrations();
			return roundResettables;
		}
	}

	/// <summary>
	/// Enabled barricades already participating in this scene's round-reset
	/// registry. Combat targeting reads this index instead of enumerating the
	/// scene for every swing.
	/// </summary>
	public IReadOnlyList<LargeLadBarricade> ActiveBarricades
	{
		get
		{
			PruneInvalidRegistrations();
			return activeBarricades;
		}
	}

	/// <summary>
	/// Explicitly authored non-barricade targets eligible for Eat's structural
	/// fallback. Ordinary physics props never enter this registry.
	/// </summary>
	public IReadOnlyList<LargeLadEatSmashable> ActiveEatSmashables
	{
		get
		{
			PruneInvalidRegistrations();
			return activeEatSmashables;
		}
	}

	/// <summary>
	/// Enabled Minion passages already participating in round reset. Melee aim
	/// assist uses this lifecycle index.
	/// </summary>
	public IReadOnlyList<LargeLadMinionPassage> ActiveMinionPassages
	{
		get
		{
			PruneInvalidRegistrations();
			return activeMinionPassages;
		}
	}

	/// <summary>
	/// Explicit mapper opt-ins eligible for Ground Slam. Generic rigidbodies,
	/// pickups, blockers, and other gameplay objects never enter this index.
	/// </summary>
	public IReadOnlyList<LargeLadGroundSlamReactiveProp>
		ActiveGroundSlamReactiveProps
	{
		get
		{
			PruneInvalidRegistrations();
			return activeGroundSlamReactiveProps;
		}
	}

	internal bool HasSceneGameplayOwnership =>
		hasSceneGameplayOwnership;

	/// <summary>
	/// Finds the registered manager for one explicit scene. This intentionally
	/// does not expose a process-wide current manager or singleton accessor.
	/// </summary>
	public static LargeLadGameManager FindForScene( Scene scene )
	{
		return LargeLadSceneRegistry.FindManager( scene );
	}

	protected override void OnEnabled()
	{
		base.OnEnabled();
		AttachToSceneRegistry();
	}

	protected override void OnDisabled()
	{
		DetachFromSceneRegistry();
		base.OnDisabled();
	}

	protected override void OnDestroy()
	{
		DetachFromSceneRegistry();
		base.OnDestroy();
	}

	protected override void OnAwake()
	{
		AttachToSceneRegistry();
		ResolveBootstrapReferences();
	}

	protected override void OnStart()
	{
		hasStarted = true;
		ResolveBootstrapReferences();

		if ( !OwnsSceneGameplay() )
			return;

		SpawnAllocator?.ConfigureNetworkHelper( NetworkHelper );
		ValidateMap( logResults: true, validateGeometry: true );
	}

	protected override void OnValidate()
	{
		ResolveBootstrapReferences();

		// The bootstrap prefab is also compiled in isolation, where map-authored
		// spawns intentionally do not exist. Full validation still always runs
		// when a playable scene starts.
		if ( SpawnAllocator is null ||
			SpawnAllocator.AuthoredTeamSpawns.Count == 0 )
			return;

		ValidateMap( logResults: true, validateGeometry: false );
	}

	/// <summary>
	/// Reprojects authored spawn candidates and validates those exact cached
	/// positions against the full map contract.
	/// </summary>
	[Button]
	public void RebuildAndValidateSpawnCandidates()
	{
		ResolveBootstrapReferences();

		if ( !OwnsSceneGameplay() )
			return;

		SpawnAllocator?.RebuildCandidatesAndRefreshLobbyPoints();
		ValidateMap( logResults: true, validateGeometry: true );
	}

	public IReadOnlyDictionary<LargeLadPlayer, LargeLadSpawnLocation> AllocateSpawnBatch(
		LargeLadSpawnGroup group,
		IReadOnlyList<LargeLadPlayer> players )
	{
		if ( !OwnsSceneGameplay() )
			return new Dictionary<LargeLadPlayer, LargeLadSpawnLocation>();

		return SpawnAllocator?.AllocateBatch( group, players ) ??
			new Dictionary<LargeLadPlayer, LargeLadSpawnLocation>();
	}

	public IReadOnlyDictionary<LargeLadPlayer, LargeLadSpawnLocation> AllocateSpawnBatch(
		LargeLadSpawnGroup group,
		IReadOnlyList<LargeLadPlayer> players,
		IReadOnlyCollection<LargeLadPlayer> additionallyRelocatingPlayers,
		IReadOnlyList<Vector3> projectedOccupiedPositions )
	{
		if ( !OwnsSceneGameplay() )
			return new Dictionary<LargeLadPlayer, LargeLadSpawnLocation>();

		return SpawnAllocator?.AllocateBatch(
			group,
			players,
			additionallyRelocatingPlayers,
			projectedOccupiedPositions ) ??
			new Dictionary<LargeLadPlayer, LargeLadSpawnLocation>();
	}

	public bool TryAllocateSpawn(
		LargeLadSpawnGroup group,
		LargeLadPlayer player,
		out LargeLadSpawnLocation location )
	{
		if ( OwnsSceneGameplay() && SpawnAllocator is not null )
			return SpawnAllocator.TryAllocate( group, player, out location );

		location = default;
		return false;
	}

	/// <summary>
	/// Returns bootstrap and spawn-contract failures that make a complete round
	/// unsafe. Other map-contract warnings do not block round flow.
	/// </summary>
	public IReadOnlyList<string> GetBlockingRoundSpawnIssues()
	{
		var issues = new List<string>();
		ResolveBootstrapReferences();
		issues.AddRange(
			LargeLadSceneRegistry.GetRuntimeBootstrapIssues( Scene, this ) );

		foreach ( var issue in validatedBlockingBootstrapIssues )
		{
			if ( !issues.Contains( issue ) )
				issues.Add( issue );
		}

		if ( SpawnAllocator is null )
			return issues;

		ValidateSpawnGroup(
			issues,
			LargeLadSpawnGroup.Lobby,
			LargeLadSpawnRules.GetRequiredCapacity(
				LargeLadSpawnGroup.Lobby,
				TargetPlayerCount ),
			validateGeometry: true );
		ValidateSpawnGroup(
			issues,
			LargeLadSpawnGroup.SkinnyKid,
			LargeLadSpawnRules.GetRequiredCapacity(
				LargeLadSpawnGroup.SkinnyKid,
				TargetPlayerCount ),
			validateGeometry: true );
		ValidateSpawnGroup(
			issues,
			LargeLadSpawnGroup.Hunter,
			LargeLadSpawnRules.GetRequiredCapacity(
				LargeLadSpawnGroup.Hunter,
				TargetPlayerCount ),
			validateGeometry: true );
		return issues;
	}

	public bool CanSafelyStartRound( bool logFailures )
	{
		// The registry owns bootstrap diagnostics and logs them once per invalid
		// scene state. A non-owner must never emit a second error or start work.
		if ( !OwnsSceneGameplay() )
			return false;

		var issues = GetBlockingRoundSpawnIssues();

		if ( issues.Count == 0 )
			return true;

		if ( logFailures )
		{
			foreach ( var issue in issues )
				Log.Error( $"Round start blocked by map contract: {issue}" );
		}

		return false;
	}

	public IReadOnlyList<string> ValidateMap(
		bool logResults,
		bool validateGeometry = false )
	{
		var issues = new List<string>();
		var blockingBootstrapIssues =
			LargeLadSceneRegistry.GetBlockingBootstrapIssues( Scene, this )
				.ToList();
		validatedBlockingBootstrapIssues.Clear();
		validatedBlockingBootstrapIssues.AddRange( blockingBootstrapIssues );
		var blockingSpawnIssues = new List<string>();
		ResolveBootstrapReferences();
		issues.AddRange( blockingBootstrapIssues );

		if ( MinimumPlayers < MinimumSupportedPlayerCount )
		{
			issues.Add(
				$"Minimum players must be at least " +
				$"{MinimumSupportedPlayerCount}." );
		}

		if ( MinimumPlayers > TargetPlayerCount )
		{
			issues.Add(
				$"Minimum players cannot exceed the {TargetPlayerCount}-player map contract." );
		}

		if ( PlayerReadyDelay < 0.0f )
			issues.Add( "Player-ready delay cannot be negative." );

		if ( HeadStartDuration < 0.0f )
			issues.Add( "Head-start duration cannot be negative." );

		if ( SurvivalDuration <= 0.0f )
			issues.Add( "Survival duration must be greater than zero." );

		if ( IntermissionDuration < 0.0f )
			issues.Add( "Intermission duration cannot be negative." );

		if ( LargeLadRespawnDelay < 0.0f )
			issues.Add( "Large Lad respawn delay cannot be negative." );

		if ( PlayerRespawnDelay < 0.0f )
			issues.Add( "Player respawn delay cannot be negative." );

		if ( !LargeLadSkinnyKidSurvivabilityRules
			.IsValidRegenerationDelay( SkinnyKidRegenerationDelay ) )
		{
			issues.Add(
				"Skinny Kid regeneration delay must be finite and non-negative." );
		}

		if ( !LargeLadSkinnyKidSurvivabilityRules
			.IsValidRegenerationRate( SkinnyKidRegenerationRate ) )
		{
			issues.Add(
				"Skinny Kid regeneration rate must be finite and non-negative." );
		}

		if ( !LargeLadHunterMovementEscalationRules.IsValidRampInterval(
			HunterMovementRampStartNormalizedTime,
			HunterMovementRampEndNormalizedTime ) )
		{
			issues.Add(
				"Hunter movement escalation needs normalized ramp start/end " +
				"values from zero through one, with start before end." );
		}

		if ( !LargeLadHunterMovementEscalationRules
			.IsValidMaximumMultiplier(
				LargeLadMovementMaximumMultiplier ) )
		{
			issues.Add(
				"Large Lad movement escalation maximum must be finite and " +
				"at least one." );
		}

		if ( !LargeLadHunterMovementEscalationRules
			.IsValidMaximumMultiplier(
				MinionMovementMaximumMultiplier ) )
		{
			issues.Add(
				"Minion movement escalation maximum must be finite and at " +
				"least one." );
		}

		issues.AddRange(
			EffectiveRoundBalanceSettings.GetValidationWarnings() );
		issues.AddRange(
			LargeLadWeaponCatalog.GetCatalogValidationWarnings() );

		ValidateSpawnGroup(
			blockingSpawnIssues,
			LargeLadSpawnGroup.Lobby,
			LargeLadSpawnRules.GetRequiredCapacity(
				LargeLadSpawnGroup.Lobby,
				TargetPlayerCount ),
			validateGeometry );
		ValidateSpawnGroup(
			blockingSpawnIssues,
			LargeLadSpawnGroup.SkinnyKid,
			LargeLadSpawnRules.GetRequiredCapacity(
				LargeLadSpawnGroup.SkinnyKid,
				TargetPlayerCount ),
			validateGeometry );
		ValidateSpawnGroup(
			blockingSpawnIssues,
			LargeLadSpawnGroup.Hunter,
			LargeLadSpawnRules.GetRequiredCapacity(
				LargeLadSpawnGroup.Hunter,
				TargetPlayerCount ),
			validateGeometry );
		issues.AddRange( blockingSpawnIssues );

		// This is an explicit editor/startup map-contract audit. Runtime combat,
		// HUD, and round reset use their lifecycle registries instead.
		var exclusivePickupNamesById =
			new Dictionary<int, string>();

		foreach ( var pickup in
			Scene?.GetAllComponents<LargeLadWeaponPickup>() ??
			Enumerable.Empty<LargeLadWeaponPickup>() )
		{
			if ( !LargeLadWeaponCatalog.IsFirearm( pickup.Weapon ) )
				issues.Add( $"Weapon pickup '{pickup.GameObject.Name}' has no valid firearm." );

			if ( !System.Enum.IsDefined(
				typeof( LargeLadPickupPolicy ),
				pickup.PickupPolicy ) )
			{
				issues.Add(
					$"Weapon pickup '{pickup.GameObject.Name}' has no valid " +
					"per-instance pickup policy." );
			}

			if ( pickup.PickupPolicy == LargeLadPickupPolicy.Exclusive &&
				pickup.GameObject.NetworkMode != NetworkMode.Object )
			{
				issues.Add(
					$"Exclusive weapon pickup '{pickup.GameObject.Name}' " +
					"must use Network Mode Object so availability replicates." );
			}

			if ( pickup.PickupPolicy ==
				LargeLadPickupPolicy.Exclusive &&
				Networking.IsHost )
			{
				pickup.EnsureExclusiveIdentityForHost();
				var instanceId = pickup.ExclusiveInstanceId;

				if ( instanceId <= 0 )
				{
					issues.Add(
						$"Exclusive weapon pickup '{pickup.GameObject.Name}' " +
						"could not establish a stable instance identity." );
				}
				else if ( exclusivePickupNamesById.TryGetValue(
					instanceId,
					out var existingName ) )
				{
					issues.Add(
						$"Exclusive weapon pickups '{existingName}' and " +
						$"'{pickup.GameObject.Name}' have the same runtime " +
						$"instance id {instanceId}." );
				}
				else
				{
					exclusivePickupNamesById.Add(
						instanceId,
						pickup.GameObject.Name );
				}
			}

			if ( pickup.PickupCollider is null )
				issues.Add( $"Weapon pickup '{pickup.GameObject.Name}' needs a trigger collider." );

			if ( pickup.PickupRenderer is null )
				issues.Add( $"Weapon pickup '{pickup.GameObject.Name}' needs visible scene geometry." );
		}

		var utilityPickupNamesById = new Dictionary<int, string>();

		foreach ( var pickup in
			Scene?.GetAllComponents<LargeLadDodgeballPickup>() ??
			Enumerable.Empty<LargeLadDodgeballPickup>() )
		{
			if ( pickup.GameObject.NetworkMode != NetworkMode.Object )
			{
				issues.Add(
					$"Dodgeball utility pickup '{pickup.GameObject.Name}' must " +
					"use Network Mode Object so its single physical instance " +
					"replicates." );
			}

			if ( Networking.IsHost )
			{
				pickup.EnsureUtilityIdentityForHost();
				var instanceId = pickup.UtilityInstanceId;

				if ( instanceId <= 0 )
				{
					issues.Add(
						$"Dodgeball utility pickup '{pickup.GameObject.Name}' " +
						"could not establish a stable instance identity." );
				}
				else if ( utilityPickupNamesById.TryGetValue(
					instanceId,
					out var existingName ) )
				{
					issues.Add(
						$"Dodgeball utility pickups '{existingName}' and " +
						$"'{pickup.GameObject.Name}' have the same runtime " +
						$"instance id {instanceId}." );
				}
				else
				{
					utilityPickupNamesById.Add(
						instanceId,
						pickup.GameObject.Name );
				}
			}

			if ( pickup.PickupCollider is null )
			{
				issues.Add(
					$"Dodgeball utility pickup '{pickup.GameObject.Name}' " +
					"needs a separate pickup trigger." );
			}

			if ( pickup.BallCollider is null ||
				pickup.BallCollider.IsTrigger )
			{
				issues.Add(
					$"Dodgeball utility pickup '{pickup.GameObject.Name}' " +
					"needs a solid ball collider." );
			}

			if ( pickup.BallRigidbody is null )
			{
				issues.Add(
					$"Dodgeball utility pickup '{pickup.GameObject.Name}' " +
					"needs a Rigidbody for authoritative bounded physics." );
			}

			if ( !pickup.GameObject.Tags.Has(
				LargeLadDodgeballRules.CollisionTag ) )
			{
				issues.Add(
					$"Dodgeball utility pickup '{pickup.GameObject.Name}' needs " +
					$"the '{LargeLadDodgeballRules.CollisionTag}' collision tag so " +
					"Minion vent openings remain solid to it." );
			}

			if ( pickup.PickupRenderer is null )
			{
				issues.Add(
					$"Dodgeball utility pickup '{pickup.GameObject.Name}' " +
					"needs visible scene geometry." );
			}
		}

		foreach ( var barricade in
			Scene?.GetAllComponents<LargeLadBarricade>() ??
			Enumerable.Empty<LargeLadBarricade>() )
		{
			if ( !barricade.HasCollision )
			{
				issues.Add(
					$"Barricade '{barricade.GameObject.Name}' needs an " +
					"authoritative blocking collider." );
			}

			if ( barricade.GameObject.NetworkMode != NetworkMode.Object )
			{
				issues.Add(
					$"Barricade '{barricade.GameObject.Name}' must use Network Mode Object." );
			}

			foreach ( var warning in barricade.GetValidationWarnings() )
			{
				issues.Add(
					$"Barricade '{barricade.GameObject.Name}': {warning}" );
			}
		}

		foreach ( var passage in
			Scene?.GetAllComponents<LargeLadMinionPassage>() ??
			Enumerable.Empty<LargeLadMinionPassage>() )
		{
			foreach ( var warning in passage.GetValidationWarnings() )
			{
				issues.Add(
					$"Minion passage '{passage.GameObject.Name}': {warning}" );
			}
		}

		foreach ( var reactiveProp in
			Scene?.GetAllComponents<LargeLadGroundSlamReactiveProp>() ??
			Enumerable.Empty<LargeLadGroundSlamReactiveProp>() )
		{
			foreach ( var warning in reactiveProp.GetValidationWarnings() )
			{
				issues.Add(
					$"Ground Slam prop '{reactiveProp.GameObject.Name}': {warning}" );
			}
		}

		foreach ( var killVolume in
			Scene?.GetAllComponents<LargeLadKillVolume>() ??
			Enumerable.Empty<LargeLadKillVolume>() )
		{
			if ( killVolume.TriggerCollider is null )
				issues.Add( $"Kill volume '{killVolume.GameObject.Name}' needs a trigger collider." );
		}

		if ( logResults )
		{
			if ( issues.Count == 0 )
			{
				if ( EnableMapValidationDebugLogging )
				{
					Log.Info(
						$"[Debug/Map Validation] Scene containing " +
						$"'{GameObject.Name}' passes the Large Lad map contract." );
				}
			}
			else
			{
				foreach ( var issue in issues )
				{
					if ( blockingBootstrapIssues.Contains( issue ) )
					{
						// The scene registry owns the single fail-closed bootstrap
						// diagnostic so duplicate managers cannot each report it.
						continue;
					}
					else if ( blockingSpawnIssues.Contains( issue ) )
					{
						Log.Error(
							$"Map contract blocks round start: {issue}" );
					}
					else
					{
						Log.Warning( $"Map contract: {issue}" );
					}
				}
			}
		}

		return issues;
	}

	protected override void OnUpdate()
	{
		if ( !Networking.IsHost || !OwnsSceneGameplay() )
			return;

		var players = GetActivePlayerSnapshot();
		lobbyPlacedPlayers.RemoveWhere( player =>
			!registeredPlayers.Contains( player ) ||
			player.Role != LargeLadRole.Unassigned );
		reportedSpawnAllocationFailures.RemoveWhere( failure =>
			!registeredPlayers.Contains( failure.Player ) );
		RefreshLastSkinnyKidState();

		switch ( Phase )
		{
			case LargeLadRoundPhase.WaitingForPlayers:
				PlaceUnassignedPlayersInLobby( players );
				UpdateRespawns( players );

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
				UpdateRespawns( players );

				if ( TickPhaseTimer() )
					BeginPlaying( players );
				break;

			case LargeLadRoundPhase.Playing:
				AssignLateJoinersAsMinions( players );
				UpdateRespawns( players );

				if ( TickPhaseTimer() )
					EndRound( LargeLadWinner.SkinnyKids );
				break;

			case LargeLadRoundPhase.RoundOver:
				PlaceUnassignedPlayersInLobby( players );
				UpdateRespawns( players );

				if ( TickPhaseTimer() )
					FinishIntermission( players );
				break;
		}
	}

	private bool StartRound( List<LargeLadPlayer> players )
	{
		if ( !LargeLadGameplayRules.IsSupportedRoundPlayerCount(
			players?.Count ?? 0 ) )
		{
			if ( !spawnFailureReported )
			{
				Log.Error(
					"Round start rejected before changing gameplay state: " +
					$"the roster has {players?.Count ?? 0} players, but Large " +
					$"Lad supports {MinimumSupportedPlayerCount} through " +
					$"{TargetPlayerCount} players." );
			}

			spawnFailureReported = true;
			return false;
		}

		if ( !CanSafelyStartRound(
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
		var hunterAllocations = AllocateSpawnBatch(
			LargeLadSpawnGroup.Hunter,
			hunterPlayers );
		var projectedHunterPositions = hunterAllocations.Values
			.Select( allocation => allocation.Position )
			.ToList();
		var skinnyKidAllocations = AllocateSpawnBatch(
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

		CommitRoundBalanceState( skinnyKidPlayers.Count );

		// Player-held exclusive and utility items are cleared before their
		// authored pickups reset, so a reset can never create a second copy.
		foreach ( var player in players )
			player.Inventory?.ClearForRoundReset();

		ResetMapState();
		ResetSurvivalRoundTiming();
		ResetLastSkinnyKidState();
		Winner = LargeLadWinner.None;
		nextLargeLadIndex = (nextLargeLadIndex + 1) % players.Count;
		lobbyPlacedPlayers.Clear();

		ApplyRespawnAllocations(
			hunterPlayers,
			hunterAllocations,
			LargeLadRole.LargeLad,
			keepMovementLocked: true );
		ApplyRespawnAllocations(
			skinnyKidPlayers,
			skinnyKidAllocations,
			LargeLadRole.SkinnyKid,
			keepMovementLocked: false );

		spawnFailureReported = false;
		SetPhaseDeadline( HeadStartDuration );
		SetPhase( LargeLadRoundPhase.HeadStart );
		RefreshLastSkinnyKidState();
		Log.Info(
			$"Round started with {players.Count} players, " +
			$"{skinnyKidPlayers.Count} Skinny Kids, the " +
			$"{SelectedBalanceBand} balance band, and a " +
			$"{HeadStartDuration:0.#}-second head start." );
		return true;
	}

	private void CommitRoundBalanceState( int skinnyKidCount )
	{
		var current = new LargeLadRoundBalanceState(
			HasSelectedBalanceBand,
			SelectedBalanceBand,
			SkinnyKidCountAtRoundStart );
		var selected = LargeLadRoundBalanceRules.ResolveState(
			current,
			skinnyKidCount,
			roundSuccessfullyBeginning: true );

		HasSelectedBalanceBand = selected.HasSelection;
		SelectedBalanceBand = selected.SelectedBand;
		SkinnyKidCountAtRoundStart =
			selected.SkinnyKidCountAtRoundStart;
	}

	private void BeginPlaying( List<LargeLadPlayer> players )
	{
		foreach ( var player in players )
			player.MovementLocked = false;

		BeginSurvivalRoundTiming();
		SetPhase( LargeLadRoundPhase.Playing );
		Log.Info( $"Head start finished. Skinny Kids must survive {SurvivalDuration:0.#} seconds." );
	}

	public void EndRound( LargeLadWinner winner )
	{
		if ( !Networking.IsHost ||
			!OwnsSceneGameplay() ||
			(Phase != LargeLadRoundPhase.HeadStart && Phase != LargeLadRoundPhase.Playing) )
		{
			return;
		}

		Winner = winner;
		ResetSurvivalRoundTiming();
		ResetLastSkinnyKidState();
		SetPhaseDeadline( IntermissionDuration );
		SetPhase( LargeLadRoundPhase.RoundOver );

		var players = GetActivePlayerSnapshot();

		foreach ( var player in players )
		{
			player.CancelEatParticipationForLifecycle();
			player.Health?.ClearPassiveRegenerationState();
		}

		var returningPlayers = players
			.Where( player => player.Health?.IsDead != true )
			.ToList();
		var lobbyAllocations = AllocateSpawnBatch(
			LargeLadSpawnGroup.Lobby,
			returningPlayers );

		if ( LargeLadSpawnRules.HasCompleteBatchAllocation(
			returningPlayers,
			lobbyAllocations ) )
		{
			foreach ( var player in returningPlayers )
			{
				player.RespawnAs(
					LargeLadRole.Unassigned,
					lobbyAllocations[player] );
				lobbyPlacedPlayers.Add( player );
			}
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
		PhaseEndTime = 0.0f;
		ResetSurvivalRoundTiming();
		ResetLastSkinnyKidState();
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

			if ( !TryAllocatePlayerSpawn(
				player,
				LargeLadSpawnGroup.Hunter,
				"joining the active round as a Minion",
				"unassigned, pending, and movement-locked",
				out var spawn ) )
			{
				continue;
			}

			player.RespawnAs( LargeLadRole.Minion, spawn );

			if ( EnablePlayerLifecycleDebugLogging )
			{
				Log.Info(
					$"[Debug/Player Lifecycle] {player.GameObject.Name} " +
					"joined the active round as a Minion." );
			}
		}
	}

	internal bool IsLastEffectiveLivingSkinnyKid( LargeLadPlayer player )
	{
		return Networking.IsHost &&
			IsRoundActive &&
			player is not null &&
			registeredPlayers.Contains( player ) &&
			IsEffectiveLivingSkinnyKid( player ) &&
			CountEffectiveLivingSkinnyKids() == 1;
	}

	private void RefreshLastSkinnyKidState()
	{
		if ( !Networking.IsHost || !OwnsSceneGameplay() )
			return;

		var currentCount = IsRoundActive
			? CountEffectiveLivingSkinnyKids()
			: 0;

		if ( LargeLadSkinnyKidSurvivabilityRules
			.ShouldAnnounceLastSkinnyKid(
				IsRoundActive,
				previousEffectiveLivingSkinnyKidCount,
				currentCount,
				hasAnnouncedLastSkinnyKidThisRound ) )
		{
			hasAnnouncedLastSkinnyKidThisRound = true;
			PublishLastSkinnyKidAnnouncement();
		}

		previousEffectiveLivingSkinnyKidCount = currentCount;
	}

	private int CountEffectiveLivingSkinnyKids()
	{
		return activePlayers.Count( player =>
			registeredPlayers.Contains( player ) &&
			IsEffectiveLivingSkinnyKid( player ) );
	}

	private static bool IsEffectiveLivingSkinnyKid(
		LargeLadPlayer player )
	{
		return player?.Health is not null &&
			LargeLadSkinnyKidSurvivabilityRules
				.IsEffectiveLivingSkinnyKid(
					player.Role,
					player.PendingRespawnRole,
					player.Health.IsDead,
					player.Health.CurrentHealth );
	}

	private void ResetLastSkinnyKidState()
	{
		previousEffectiveLivingSkinnyKidCount = 0;
		hasAnnouncedLastSkinnyKidThisRound = false;
		lastSkinnyKidAnnouncement = null;
		timeSinceLastSkinnyKidAnnouncement = 0.0f;
	}

	private void EvaluateWinnerAfterLifecycleChange()
	{
		if ( !Networking.IsHost ||
			Phase is not (LargeLadRoundPhase.HeadStart or LargeLadRoundPhase.Playing) )
		{
			return;
		}

		var winner = LargeLadGameplayRules.DetermineWinnerWhenTeamIsMissing(
			activePlayers.Any( player =>
				registeredPlayers.Contains( player ) &&
				GetEffectiveRoundRole( player ) == LargeLadRole.LargeLad ),
			activePlayers.Any( player =>
				registeredPlayers.Contains( player ) &&
				GetEffectiveRoundRole( player ) == LargeLadRole.SkinnyKid ) );

		if ( winner != LargeLadWinner.None )
			EndRound( winner );
	}

	internal bool HandlePlayerLethalTransition(
		LargeLadPlayer player,
		LargeLadDamageContext damage )
	{
		// TryBeginDeath is the final idempotency gate. All state required to
		// reconstruct the respawn remains synchronized on the player and health.
		if ( !Networking.IsHost ||
			!OwnsSceneGameplay() ||
			player?.Health is null ||
			!registeredPlayers.Contains( player ) ||
			player.Scene != Scene )
		{
			return false;
		}

		var plan = LargeLadGameplayRules.ResolveDeathPlan(
			player.Role,
			damage.DamageType,
			LargeLadRespawnDelay,
			PlayerRespawnDelay );

		if ( !player.Health.TryBeginDeath(
			plan.RespawnDelay,
			plan.UseRagdoll ) )
		{
			return false;
		}

		player.CancelEatParticipationForLifecycle();
		player.NativeInventory?.HandleDeath();
		player.Inventory?.HandleDeath( player.GameObject.WorldPosition );
		player.SetPendingRespawnRole( plan.ResultingRole );
		player.MovementLocked = true;

		var conversion = player.Role == LargeLadRole.SkinnyKid
			? " and will convert to a Minion"
			: string.Empty;

		if ( EnablePlayerLifecycleDebugLogging )
		{
			Log.Info(
				$"[Debug/Player Lifecycle] {player.GameObject.Name} died " +
				$"from {damage.KillfeedCause}{conversion}; respawn in " +
				$"{plan.RespawnDelay:0.#} seconds." );
		}

		AuthoritativePlayerKilled?.Invoke( player, damage );
		RefreshLastSkinnyKidState();
		EvaluateWinnerAfterLifecycleChange();
		return true;
	}

	public void RequestEnvironmentalDeath( LargeLadPlayer player )
	{
		if ( !Networking.IsHost ||
			!OwnsSceneGameplay() ||
			Phase is not (LargeLadRoundPhase.HeadStart or LargeLadRoundPhase.Playing) ||
			player?.Health is null ||
			!registeredPlayers.Contains( player ) ||
			player.Scene != Scene )
		{
			return;
		}

		player.Health.RequestEnvironmentalDeath();
	}

	private void UpdateRespawns( IReadOnlyList<LargeLadPlayer> players )
	{
		foreach ( var player in players )
		{
			var health = player.Health;

			if ( health is null ||
				!health.IsDead ||
				!health.TickRespawnCountdown() )
			{
				continue;
			}

			var roundIsActive =
				Phase is LargeLadRoundPhase.HeadStart or LargeLadRoundPhase.Playing;
			var respawnRole = roundIsActive
				? GetEffectiveRoundRole( player )
				: LargeLadRole.Unassigned;
			var spawnGroup = roundIsActive
				? LargeLadGameplayRules.GetSpawnGroupForRole( respawnRole )
				: LargeLadSpawnGroup.Lobby;

			if ( !TryAllocatePlayerSpawn(
				player,
				spawnGroup,
				$"respawning as {GetRoleName( respawnRole )}",
				"dead with its pending role intact and movement locked",
				out var spawn ) )
			{
				continue;
			}

			player.RespawnAs(
				respawnRole,
				spawn,
				keepMovementLocked:
					roundIsActive &&
					respawnRole == LargeLadRole.LargeLad &&
					Phase == LargeLadRoundPhase.HeadStart );

			if ( !roundIsActive )
				lobbyPlacedPlayers.Add( player );

			if ( EnablePlayerLifecycleDebugLogging )
			{
				Log.Info(
					$"[Debug/Player Lifecycle] {player.GameObject.Name} " +
					$"respawned as {GetRoleName( respawnRole )}." );
			}
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

			if ( !TryAllocatePlayerSpawn(
				player,
				LargeLadSpawnGroup.Lobby,
				"entering the Lobby",
				"unassigned and movement-locked",
				out var spawn ) )
			{
				continue;
			}

			player.RespawnAs( LargeLadRole.Unassigned, spawn );
			lobbyPlacedPlayers.Add( player );
		}
	}

	internal void ResolveBootstrapReferencesForRegistry()
	{
		ResolveBootstrapReferences();
	}

	private void ResolveBootstrapReferences()
	{
		if ( !IsUsableBootstrapComponent( NetworkHelper ) )
			NetworkHelper = Components.Get<NetworkHelper>();

		if ( !IsUsableBootstrapComponent( SpawnAllocator ) )
			SpawnAllocator = Components.Get<LargeLadSpawnAllocator>();

		SpawnAllocator?.ConfigureGameManager( this );
	}

	private void ValidateSpawnGroup(
		List<string> issues,
		LargeLadSpawnGroup group,
		int requiredCapacity,
		bool validateGeometry )
	{
		if ( SpawnAllocator is null )
		{
			issues.Add(
				$"{group} cannot be validated because no LargeLadSpawnAllocator " +
				"is available. Keep exactly one Large Lad Gameplay Bootstrap " +
				"in the scene." );
			return;
		}

		var spawns = SpawnAllocator.GetTeamSpawns( group );

		if ( spawns.Count == 0 )
		{
			issues.Add(
				$"{group} has no authored spawn object. Add a {group} Team Spawn " +
				"prefab and rebuild projected candidates." );
			return;
		}

		var capacity = SpawnAllocator.GetConfiguredCapacity( group );
		if ( capacity < requiredCapacity )
		{
			var authoredCapacities = string.Join(
				", ",
				spawns.Select( spawn =>
					$"'{spawn.GameObject.Name}' capacity {spawn.Capacity}" ) );
			issues.Add(
				$"{group} configured capacity is {capacity}/{requiredCapacity}. " +
				$"Authored spawn objects: {authoredCapacities}. Increase capacity " +
				"or add another authored spawn area." );
		}

		if ( !validateGeometry )
			return;

		var generated = SpawnAllocator.CountGeneratedCandidates( group );
		if ( generated >= requiredCapacity )
			return;

		var generatedDescription = generated == 0
			? "zero valid cached positions"
			: $"{generated}/{requiredCapacity} valid cached positions";
		issues.Add(
			$"{group} produced {generatedDescription}; {requiredCapacity} are " +
			"required for safe round flow. The round cannot start until the " +
			"authored areas are fixed and rebuilt." );

		foreach ( var spawn in spawns )
		{
			var spawnGenerated =
				SpawnAllocator.CountGeneratedCandidates( spawn );
			issues.Add(
				$"{group} authored spawn '{spawn.GameObject.Name}': configured " +
				$"capacity {spawn.Capacity}, generated {spawnGenerated} valid " +
				"cached positions. Move or resize the area above walkable floor, " +
				"clear nearby walls/ceilings, adjust MinimumSeparation if needed, " +
				"then use Rebuild Projected Candidates." );
		}
	}

	private void ResetMapState()
	{
		foreach ( var resettable in GetRoundResettableSnapshot() )
		{
			if ( IsActiveInManagerScene( resettable ) )
				resettable.ResetForRound();
			else
				UnregisterRoundResettable( resettable );
		}
	}

	internal void RegisterPlayer( LargeLadPlayer player )
	{
		if ( !IsActiveInManagerScene( player ) ||
			!registeredPlayers.Add( player ) )
		{
			return;
		}

		activePlayers.Add( player );
		IndexPlayerRole( player, player.Role );

		if ( !isHydratingRegistrations )
		{
			RefreshLastSkinnyKidState();
			EvaluateWinnerAfterLifecycleChange();
		}
	}

	internal void UnregisterPlayer( LargeLadPlayer player )
	{
		if ( player is null || !registeredPlayers.Remove( player ) )
			return;

		if ( Networking.IsHost )
		{
			player.CancelEatParticipationForLifecycle();
			player.Inventory?.HandleDisconnect();
		}

		activePlayers.Remove( player );

		foreach ( var rolePlayers in playersByRole.Values )
			rolePlayers.Remove( player );

		if ( currentLargeLad == player )
			currentLargeLad = FindIndexedLargeLad();

		lobbyPlacedPlayers.Remove( player );
		reportedSpawnAllocationFailures.RemoveWhere(
			failure => failure.Player == player );

		if ( !isHydratingRegistrations )
		{
			RefreshLastSkinnyKidState();
			EvaluateWinnerAfterLifecycleChange();
		}
	}

	internal void UpdatePlayerRole(
		LargeLadPlayer player,
		LargeLadRole oldRole,
		LargeLadRole newRole )
	{
		if ( player is null || !registeredPlayers.Contains( player ) )
			return;

		if ( playersByRole.TryGetValue( oldRole, out var oldRolePlayers ) )
			oldRolePlayers.Remove( player );

		IndexPlayerRole( player, newRole );

		if ( oldRole == LargeLadRole.LargeLad &&
			currentLargeLad == player &&
			newRole != LargeLadRole.LargeLad )
		{
			currentLargeLad = FindIndexedLargeLad();
		}

		if ( !isHydratingRegistrations )
		{
			RefreshLastSkinnyKidState();
			EvaluateWinnerAfterLifecycleChange();
		}
	}

	internal void RegisterRoundResettable(
		ILargeLadRoundResettable resettable )
	{
		if ( !IsActiveInManagerScene( resettable ) ||
			!registeredRoundResettables.Add( resettable ) )
		{
			return;
		}

		roundResettables.Add( resettable );

		if ( resettable is LargeLadBarricade barricade )
			activeBarricades.Add( barricade );

		if ( resettable is LargeLadEatSmashable eatSmashable )
			activeEatSmashables.Add( eatSmashable );

		if ( resettable is LargeLadMinionPassage passage )
			activeMinionPassages.Add( passage );

		if ( resettable is LargeLadGroundSlamReactiveProp reactiveProp )
			activeGroundSlamReactiveProps.Add( reactiveProp );
	}

	internal void UnregisterRoundResettable(
		ILargeLadRoundResettable resettable )
	{
		if ( resettable is null ||
			!registeredRoundResettables.Remove( resettable ) )
		{
			return;
		}

		roundResettables.Remove( resettable );

		if ( resettable is LargeLadBarricade barricade )
			activeBarricades.Remove( barricade );

		if ( resettable is LargeLadEatSmashable eatSmashable )
			activeEatSmashables.Remove( eatSmashable );

		if ( resettable is LargeLadMinionPassage passage )
			activeMinionPassages.Remove( passage );

		if ( resettable is LargeLadGroundSlamReactiveProp reactiveProp )
			activeGroundSlamReactiveProps.Remove( reactiveProp );
	}

	internal void AcquireSceneGameplayOwnership(
		IReadOnlyList<LargeLadPlayer> players,
		IReadOnlyList<ILargeLadRoundResettable> resettables )
	{
		ClearRegistrations();
		hasSceneGameplayOwnership = false;
		isHydratingRegistrations = true;

		try
		{
			foreach ( var player in players )
				RegisterPlayer( player );

			foreach ( var resettable in resettables )
				RegisterRoundResettable( resettable );

			ReconcilePlayerRoleIndexes();
		}
		finally
		{
			isHydratingRegistrations = false;
		}

		hasSceneGameplayOwnership = true;

		if ( hasStarted )
		{
			ResolveBootstrapReferences();
			SpawnAllocator?.ConfigureNetworkHelper( NetworkHelper );
		}

		// Hydration is one lifecycle transaction. A partially restored role
		// index must never be visible to the winner check.
		RefreshLastSkinnyKidState();
		EvaluateWinnerAfterLifecycleChange();
	}

	internal void ReleaseSceneGameplayOwnership()
	{
		hasSceneGameplayOwnership = false;
		ClearRegistrations();
	}

	private void AttachToSceneRegistry()
	{
		if ( registeredScene is not null && registeredScene != Scene )
			DetachFromSceneRegistry();

		registeredScene = Scene;
		LargeLadSceneRegistry.RegisterManager( registeredScene, this );
	}

	private void DetachFromSceneRegistry()
	{
		if ( registeredScene is not null )
		{
			LargeLadSceneRegistry.UnregisterManager(
				registeredScene,
				this );
			registeredScene = null;
		}

		hasSceneGameplayOwnership = false;
		ClearRegistrations();
	}

	private void ClearRegistrations()
	{
		activePlayers.Clear();
		registeredPlayers.Clear();

		foreach ( var rolePlayers in playersByRole.Values )
			rolePlayers.Clear();

		currentLargeLad = null;
		roundResettables.Clear();
		registeredRoundResettables.Clear();
		activeBarricades.Clear();
		activeEatSmashables.Clear();
		activeMinionPassages.Clear();
		activeGroundSlamReactiveProps.Clear();
		lobbyPlacedPlayers.Clear();
		reportedSpawnAllocationFailures.Clear();
	}

	private List<LargeLadPlayer> GetActivePlayerSnapshot()
	{
		PruneInvalidRegistrations();
		return activePlayers.ToList();
	}

	private List<ILargeLadRoundResettable> GetRoundResettableSnapshot()
	{
		PruneInvalidRegistrations();
		return roundResettables.ToList();
	}

	private void PruneInvalidRegistrations()
	{
		foreach ( var player in activePlayers
			.Where( player => !IsActiveInManagerScene( player ) )
			.ToList() )
		{
			UnregisterPlayer( player );
		}

		foreach ( var resettable in roundResettables
			.Where( resettable => !IsActiveInManagerScene( resettable ) )
			.ToList() )
		{
			UnregisterRoundResettable( resettable );
		}

		ReconcilePlayerRoleIndexes();
	}

	private void ReconcilePlayerRoleIndexes()
	{
		// Role change callbacks keep this index current during normal play.
		// Reconcile from the already-registered player list as a defensive path
		// for deserialization and network snapshot application.
		foreach ( var player in activePlayers )
		{
			foreach ( var roleEntry in playersByRole )
			{
				if ( roleEntry.Key != player.Role )
					roleEntry.Value.Remove( player );
			}

			IndexPlayerRole( player, player.Role );
		}

		currentLargeLad = FindIndexedLargeLad();
	}

	private bool OwnsSceneGameplay()
	{
		return hasSceneGameplayOwnership;
	}

	private void IndexPlayerRole(
		LargeLadPlayer player,
		LargeLadRole role )
	{
		if ( !playersByRole.TryGetValue( role, out var rolePlayers ) )
			return;

		if ( !rolePlayers.Contains( player ) )
			rolePlayers.Add( player );

		if ( role == LargeLadRole.LargeLad )
			currentLargeLad = player;
	}

	private LargeLadPlayer FindIndexedLargeLad()
	{
		return playersByRole[LargeLadRole.LargeLad]
			.FirstOrDefault( player => IsActiveInManagerScene( player ) );
	}

	private bool IsActiveInManagerScene( Component component )
	{
		return component is not null &&
			component.IsValid &&
			component.Enabled &&
			component.Scene == Scene;
	}

	private bool IsActiveInManagerScene(
		ILargeLadRoundResettable resettable )
	{
		return resettable is Component component &&
			IsActiveInManagerScene( component );
	}

	private bool IsUsableBootstrapComponent( Component component )
	{
		return component is not null &&
			component.IsValid &&
			component.Scene == Scene &&
			component.GameObject == GameObject;
	}

	private bool TickPhaseTimer()
	{
		return Networking.IsHost &&
			LargeLadGameplayRules.HasTimerReachedDeadline(
				PhaseEndTime,
				Time.Now );
	}

	private void SetPhaseDeadline( float duration )
	{
		PhaseEndTime = LargeLadGameplayRules.GetTimerDeadline(
			Time.Now,
			duration );
	}

	private void BeginSurvivalRoundTiming()
	{
		var hostNow = Time.Now;
		SurvivalRoundStartTime = hostNow;
		PhaseEndTime = LargeLadGameplayRules.GetTimerDeadline(
			hostNow,
			SurvivalDuration );
	}

	private void ResetSurvivalRoundTiming()
	{
		SurvivalRoundStartTime = 0.0f;
	}

	private static void ApplyRespawnAllocations(
		IReadOnlyList<LargeLadPlayer> players,
		IReadOnlyDictionary<LargeLadPlayer, LargeLadSpawnLocation> allocations,
		LargeLadRole role,
		bool keepMovementLocked )
	{
		// Allocation is manager policy; applying the spawn is player lifecycle.
		foreach ( var player in players )
		{
			player.RespawnAs(
				role,
				allocations[player],
				keepMovementLocked );
		}
	}

	private bool TryAllocatePlayerSpawn(
		LargeLadPlayer player,
		LargeLadSpawnGroup group,
		string action,
		string retainedState,
		out LargeLadSpawnLocation spawn )
	{
		if ( !TryAllocateSpawn( group, player, out spawn ) )
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
