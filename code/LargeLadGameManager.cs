using Sandbox;
using System;
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
	private const float KillfeedEntryDuration = 6.5f;
	private const int MaximumKillfeedEntries = 6;
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

	/// <summary>
	/// The descriptor admitted by the current loaded-map preparation boundary.
	/// Null outside a successfully prepared map, including every transition.
	/// </summary>
	public LargeLadMapDescriptor CurrentMapDescriptor => activeMapDescriptor;

	public float EffectiveSurvivalDuration =>
		activeMapDescriptor?.Balance.ResolveSurvivalDuration(
			SurvivalDuration ) ?? SurvivalDuration;

	[Property]
	public NetworkHelper NetworkHelper { get; set; }

	[Property]
	public LargeLadSpawnAllocator SpawnAllocator { get; set; }

	[Property]
	public LargeLadSessionCoordinator SessionCoordinator { get; set; }

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
		SessionCoordinator?.CanAdvanceRoundFlow == true &&
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
	/// Host-only notification emitted after all built-in consumers have received
	/// the one committed death record.
	/// </summary>
	public event System.Action<LargeLadDeathRecord>
		AuthoritativeDeathCommitted;

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
		var mapBalance = activeMapDescriptor?.Balance;
		return LargeLadHunterMovementEscalationRules
			.GetMovementMultiplier(
				role,
				NormalizedElapsedSurvivalRoundTime,
				HunterMovementRampStartNormalizedTime,
				HunterMovementRampEndNormalizedTime,
				LargeLadMapCatalog.ComposeHunterMaximumMultiplier(
					LargeLadMovementMaximumMultiplier,
					mapBalance ),
				LargeLadMapCatalog.ComposeHunterMaximumMultiplier(
					MinionMovementMaximumMultiplier,
					mapBalance ) );
	}

	/// <summary>
	/// Returns the current round's band factor composed with the validated active
	/// map's Large Lad health factor. Before the first successful round, the band
	/// remains neutral.
	/// </summary>
	public float GetLargeLadMaximumHealthMultiplier()
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
			activeMapDescriptor?.Balance
				.ResolveLargeLadMaximumHealthMultiplier() ?? 1.0f );
	}

	/// <summary>
	/// Returns the current round's band factor composed with the validated active
	/// map's SkinnyProgression barricade health factor.
	/// </summary>
	public float GetSkinnyProgressionBarricadeMaximumHealthMultiplier()
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
			activeMapDescriptor?.Balance
				.ResolveSkinnyProgressionBarricadeMultiplier() ?? 1.0f );
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

	internal void HandleAuthoritativeBarricadeDestruction(
		LargeLadBarricade barricade,
		LargeLadDamageContext finalDamage )
	{
		if ( !Networking.IsHost ||
			!OwnsSceneGameplay() ||
			!IsRoundActive ||
			barricade is null ||
			!barricade.IsValid ||
			barricade.Scene != Scene ||
			!barricade.IsDestroyed ||
			finalDamage.AppliedDamage <= 0.0f ||
			!LargeLadGameplayRules.CanDamageBarricade(
				barricade.Mode,
				finalDamage.AttackerRole,
				finalDamage.DamageType ) ||
			!TryResolveRegisteredAttacker(
				victim: null,
				finalDamage.Attacker,
				out var attacker,
				out _ ) )
		{
			return;
		}

		attacker.SubmitCareerStats(
			LargeLadCareerStatRules.GetBarricadeDestructionDeltas(
				barricade.Mode,
				finalDamage.AttackerRole,
				isFinalAuthoritativeDestruction: true ) );
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

	private LargeLadRoleSelectionSessionState roleSelectionSessionState;
	private int waitingPlayerCount = -1;
	private float playerReadyTimeRemaining;
	private bool spawnFailureReported;
	private bool hasStarted;
	private bool isHydratingRegistrations;
	private bool hasSceneGameplayOwnership;
	private Scene registeredScene;
	private LargeLadPlayer currentLargeLad;
	private LargeLadMapDescriptor activeMapDescriptor;
	private string barricadeDestructionAnnouncement;
	private TimeSince timeSinceBarricadeDestructionAnnouncement;
	private string lastSkinnyKidAnnouncement;
	private TimeSince timeSinceLastSkinnyKidAnnouncement;
	private TimeSince timeSinceContributionPrune;
	private int previousEffectiveLivingSkinnyKidCount;
	private bool hasAnnouncedLastSkinnyKidThisRound;
	private int activeRoundSequenceId;
	private int nextDeathEventSequenceId;
	private int lastReceivedKillfeedEventSequenceId;
	private int committedLargeLadDeathsThisRound;
	private string activeRoundLargeLadIdentity;
	private readonly List<LargeLadPlayer> activePlayers = new();
	private readonly HashSet<LargeLadPlayer> registeredPlayers = new();
	private readonly HashSet<string> firstRoundBootstrapRosterIdentities =
		new( StringComparer.Ordinal );
	private readonly HashSet<string> activeRoundStarterIdentities =
		new( StringComparer.Ordinal );
	private readonly Dictionary<string, LargeLadRole>
		activeRoundStartingRoles = new( StringComparer.Ordinal );
	private readonly HashSet<string> lastSkinnyKidIdentitiesThisRound =
		new( StringComparer.Ordinal );
	private readonly LargeLadRecentDamageStore recentDamageContributions = new();
	private readonly LargeLadRoundOutcomeCommitGate roundOutcomeCommitGate = new();
	private readonly List<LargeLadKillfeedEntry> localKillfeedEntries = new();
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
	private readonly List<string> validatedBlockingMapIssues = new();
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
	/// The small local presentation queue populated by reliable host death RPCs.
	/// It is neither synchronized state nor replayed to late joiners.
	/// </summary>
	public IReadOnlyList<LargeLadKillfeedEntry> KillfeedEntries
	{
		get
		{
			PruneExpiredKillfeedEntries();
			return localKillfeedEntries;
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

		if ( LargeLadBootstrapPlacement.DisableIfEmbeddedMapContent( this ) )
			return;

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
		if ( LargeLadBootstrapPlacement.DisableIfEmbeddedMapContent( this ) )
			return;

		AttachToSceneRegistry();
		ResolveBootstrapReferences();
	}

	protected override void OnStart()
	{
		if ( LargeLadBootstrapPlacement.IsEmbeddedMapContent( this ) )
			return;

		hasStarted = true;
		ResolveBootstrapReferences();

		if ( !OwnsSceneGameplay() )
			return;

		// MapInstance owns asynchronous startup; its callback prepares the map.
		if ( SessionCoordinator?.IsMapReady == true )
		{
			SpawnAllocator?.ConfigureNetworkHelper( NetworkHelper );
			ValidateGameSession( logResults: true );
		}
	}

	protected override void OnValidate()
	{
		ResolveBootstrapReferences();
		ValidateGameSession( logResults: true );
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
		return GetBlockingRoundSpawnIssues( requireReadyMap: true );
	}

	private IReadOnlyList<string> GetBlockingRoundSpawnIssues(
		bool requireReadyMap )
	{
		var issues = new List<string>();
		ResolveBootstrapReferences();
		issues.AddRange(
			LargeLadSceneRegistry.GetRuntimeBootstrapIssues( Scene, this ) );

		if ( requireReadyMap && SessionCoordinator?.IsMapReady != true )
		{
			issues.Add(
				"No valid MapInstance map is ready for the persistent session." );
		}

		foreach ( var issue in validatedBlockingBootstrapIssues )
		{
			if ( !issues.Contains( issue ) )
				issues.Add( issue );
		}

		foreach ( var issue in validatedBlockingMapIssues )
		{
			if ( !issues.Contains( issue ) )
				issues.Add( issue );
		}

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

	/// <summary>
	/// Validates the persistent game/session owner only. Mapper-authored content
	/// is validated by LargeLadMapValidator from its Map Profile and admission.
	/// </summary>
	public IReadOnlyList<string> ValidateGameSession( bool logResults )
	{
		var issues = new List<string>();
		var blockingBootstrapIssues =
			LargeLadSceneRegistry.GetBlockingBootstrapIssues( Scene, this )
				.ToList();
		validatedBlockingBootstrapIssues.Clear();
		validatedBlockingBootstrapIssues.AddRange( blockingBootstrapIssues );
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
				$"Minimum players cannot exceed the {TargetPlayerCount}-player contract." );
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
			.IsValidMaximumMultiplier( LargeLadMovementMaximumMultiplier ) )
		{
			issues.Add(
				"Large Lad movement escalation maximum must be finite and at least one." );
		}

		if ( !LargeLadHunterMovementEscalationRules
			.IsValidMaximumMultiplier( MinionMovementMaximumMultiplier ) )
		{
			issues.Add(
				"Minion movement escalation maximum must be finite and at least one." );
		}

		issues.AddRange(
			EffectiveRoundBalanceSettings.GetValidationWarnings()
				.Select( issue => $"Round balance: {issue}" ) );
		issues.AddRange(
			LargeLadWeaponCatalog.GetCatalogValidationWarnings()
				.Select( issue => $"Weapon catalog: {issue}" ) );
		issues.AddRange(
			LargeLadBarricade.GetProjectValidationWarnings()
				.Select( issue => $"Barricade project setup: {issue}" ) );
		issues.AddRange(
			LargeLadMinionPassage.GetProjectValidationWarnings()
				.Select( issue => $"Minion passage project setup: {issue}" ) );

		if ( logResults )
		{
			if ( issues.Count == 0 )
			{
				if ( EnableMapValidationDebugLogging )
				{
					Log.Info(
						$"[Debug/Game Validation] Persistent gameplay bootstrap " +
						$"'{GameObject.Name}' passes game/session validation." );
				}
			}
			else
			{
				foreach ( var issue in issues )
				{
					if ( blockingBootstrapIssues.Contains( issue ) )
					{
						// The registry owns the one fail-closed bootstrap diagnostic.
						continue;
					}

					Log.Warning( $"Game/session validation: {issue}" );
				}
			}
		}

		return issues;
	}

	protected override void OnUpdate()
	{
		PruneExpiredKillfeedEntries();

		if ( !Networking.IsHost || !OwnsSceneGameplay() )
			return;

		if ( timeSinceContributionPrune >= 1.0f )
		{
			timeSinceContributionPrune = 0.0f;
			recentDamageContributions.Prune(
				activeRoundSequenceId,
				Time.Now );
		}

		// Readiness is callback-authored by the session coordinator. While the
		// map is absent, loading, unloading, or invalid, no round state advances.
		if ( SessionCoordinator?.CanAdvanceRoundFlow != true )
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

		if ( !TryBuildRoleSelectionCandidates(
			players,
			out var playersBySessionIdentity,
			out var roleSelectionCandidates ) )
		{
			return false;
		}

		var selectedCandidate =
			LargeLadRoleSelectionRules.SelectLargeLadCandidate(
				roleSelectionCandidates,
				Game.Random.Int( 0, int.MaxValue - 1 ) );

		if ( selectedCandidate is not { } candidate ||
			!playersBySessionIdentity.TryGetValue(
				candidate.SessionIdentity,
				out var largeLad ) )
		{
			Log.Error(
				"Round start rejected before changing gameplay state: no " +
				"eligible Large Lad candidate exists. Newly connected players " +
				"must complete a full successful round before selection." );
			return false;
		}

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

		var captureBootstrapRoster =
			!roleSelectionSessionState.HasCapturedBootstrapRoster;

		if ( !LargeLadRoleSelectionRules.TryCommitSuccessfulRoundStart(
			roleSelectionSessionState,
			candidate.SessionIdentity,
			spawnAllocationSucceeded: true,
			out var committedSelectionState,
			out var selectionOrdinal ) )
		{
			Log.Error(
				"Round start aborted before changing gameplay state: the " +
				"Large Lad fairness transaction was already committed or " +
				"contained an invalid selected identity." );
			return false;
		}

		CommitRoundBalanceState( skinnyKidPlayers.Count );
		roleSelectionSessionState = committedSelectionState;
		largeLad.CommitLargeLadSelection( selectionOrdinal );

		if ( captureBootstrapRoster )
		{
			firstRoundBootstrapRosterIdentities.Clear();
			firstRoundBootstrapRosterIdentities.UnionWith(
				playersBySessionIdentity.Keys );
		}

		activeRoundStarterIdentities.Clear();
		activeRoundStarterIdentities.UnionWith(
			playersBySessionIdentity.Keys );
		activeRoundStartingRoles.Clear();

		foreach ( var rosterEntry in playersBySessionIdentity )
		{
			activeRoundStartingRoles[rosterEntry.Key] =
				rosterEntry.Value == largeLad
					? LargeLadRole.LargeLad
					: LargeLadRole.SkinnyKid;
		}

		activeRoundSequenceId++;
		activeRoundLargeLadIdentity = candidate.SessionIdentity;
		committedLargeLadDeathsThisRound = 0;
		recentDamageContributions.Clear();
		roundOutcomeCommitGate.ResetForSuccessfulRoundStart();

		// Player-held exclusive and utility items are cleared before their
		// authored pickups reset, so a reset can never create a second copy.
		foreach ( var player in players )
		{
			player.NativeInventory?.ClearForRoundReset();
		}

		ResetMapState();
		ResetSurvivalRoundTiming();
		ResetLastSkinnyKidState();
		Winner = LargeLadWinner.None;
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
			$"{HeadStartDuration:0.#}-second head start. Large Lad fairness " +
			$"ordinal {selectionOrdinal} committed." );
		return true;
	}

	private bool TryBuildRoleSelectionCandidates(
		IReadOnlyList<LargeLadPlayer> players,
		out Dictionary<string, LargeLadPlayer> playersBySessionIdentity,
		out List<LargeLadRoleSelectionCandidate> candidates )
	{
		playersBySessionIdentity = new Dictionary<string, LargeLadPlayer>(
			StringComparer.Ordinal );
		candidates = new List<LargeLadRoleSelectionCandidate>( players.Count );

		foreach ( var player in players )
		{
			var identity = player.GetRoleSelectionSessionIdentity();

			if ( string.IsNullOrWhiteSpace( identity ) ||
				!playersBySessionIdentity.TryAdd( identity, player ) )
			{
				Log.Error(
					"Round start rejected before changing gameplay state: every " +
					"connected player needs one unique session identity." );
				return false;
			}

			var history = player.GetRoleSelectionHistory();
			var isBootstrapEligible =
				LargeLadRoleSelectionRules.IsBootstrapEligible(
					history,
					roleSelectionSessionState,
					firstRoundBootstrapRosterIdentities.Contains( identity ) );
			var wasPreviousLargeLad = string.Equals(
				identity,
				roleSelectionSessionState.PreviousLargeLadIdentity,
				StringComparison.Ordinal );

			candidates.Add( player.BuildRoleSelectionCandidate(
				history.HasCompletedFullRound,
				isBootstrapEligible,
				wasPreviousLargeLad ) );
		}

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
		Log.Info(
			$"Head start finished. Skinny Kids must survive " +
			$"{EffectiveSurvivalDuration:0.#} seconds." );
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
		var players = GetActivePlayerSnapshot();
		var roundCompletedSuccessfully = winner != LargeLadWinner.None;
		CommitRoundCareerStats(
			winner,
			players,
			roundCompletedSuccessfully );
		ResetSurvivalRoundTiming();
		ResetLastSkinnyKidState();
		SetPhaseDeadline( IntermissionDuration );
		SetPhase( LargeLadRoundPhase.RoundOver );

		foreach ( var player in players )
		{
			var identity = player.GetRoleSelectionSessionIdentity();
			player.CommitFullRoundCompletion(
				activeRoundStarterIdentities.Contains( identity ),
				isConnectedAtCompletion: true,
				roundCompletedSuccessfully );
		}

		roleSelectionSessionState =
			LargeLadRoleSelectionRules.MarkRoundCompleted(
				roleSelectionSessionState,
				roundCompletedSuccessfully );
		activeRoundStarterIdentities.Clear();
		activeRoundStartingRoles.Clear();
		activeRoundLargeLadIdentity = null;

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
		SessionCoordinator?.NotifyRoundCompleted( this, winner );
	}

	private void CommitRoundCareerStats(
		LargeLadWinner winner,
		IReadOnlyList<LargeLadPlayer> players,
		bool roundCompletedSuccessfully )
	{
		if ( !roundOutcomeCommitGate.TryCommit(
			roundCompletedSuccessfully ) )
		{
			return;
		}

		foreach ( var player in players ?? Array.Empty<LargeLadPlayer>() )
		{
			if ( player is null ||
				!player.IsValid ||
				!player.Enabled ||
				player.Scene != Scene )
			{
				continue;
			}

			var identity = player.GetRoleSelectionSessionIdentity();
			var wasStarter = activeRoundStartingRoles.TryGetValue(
				identity,
				out var startingRole );
			var isLiving = player.Health is not null &&
				!player.Health.IsDead &&
				player.Health.CurrentHealth > 0.0f;
			var participant = new LargeLadRoundParticipantOutcome(
				identity,
				wasStarter,
				IsConnectedAtCompletion: true,
				startingRole,
				GetEffectiveRoundRole( player ),
				isLiving,
				string.Equals(
					identity,
					activeRoundLargeLadIdentity,
					StringComparison.Ordinal ),
				lastSkinnyKidIdentitiesThisRound.Contains( identity ) );

			player.SubmitCareerStats(
				LargeLadCareerStatRules.GetRoundOutcomeDeltas(
					winner,
					roundCompletedSuccessfully,
					committedLargeLadDeathsThisRound,
					participant ) );
		}
	}

	private void FinishIntermission( List<LargeLadPlayer> players )
	{
		roleSelectionSessionState =
			LargeLadRoleSelectionRules.PrepareNextRound(
				roleSelectionSessionState );
		activeRoundStarterIdentities.Clear();
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
			var lastSkinnyKid = activePlayers.FirstOrDefault( player =>
				registeredPlayers.Contains( player ) &&
				IsEffectiveLivingSkinnyKid( player ) );
			var identity = lastSkinnyKid?
				.GetRoleSelectionSessionIdentity();

			if ( !string.IsNullOrWhiteSpace( identity ) )
				lastSkinnyKidIdentitiesThisRound.Add( identity );

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
		lastSkinnyKidIdentitiesThisRound.Clear();
		lastSkinnyKidAnnouncement = null;
		timeSinceLastSkinnyKidAnnouncement = 0.0f;
	}

	private void EvaluateWinnerAfterLifecycleChange()
	{
		if ( !Networking.IsHost ||
			!IsRoundActive )
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
			!IsRoundActive ||
			player?.Health is null ||
			!registeredPlayers.Contains( player ) ||
			player.Scene != Scene )
		{
			return false;
		}

		var victimRole = player.Role;
		var plan = LargeLadGameplayRules.ResolveDeathPlan(
			victimRole,
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
		player.NativeInventory?.HandleDeath(
			player.GameObject.WorldPosition );
		player.SetPendingRespawnRole( plan.ResultingRole );
		player.MovementLocked = true;

		var convertedToMinion =
			victimRole == LargeLadRole.SkinnyKid &&
			plan.ResultingRole == LargeLadRole.Minion;
		var conversion = convertedToMinion
			? " and will convert to a Minion"
			: string.Empty;

		if ( EnablePlayerLifecycleDebugLogging )
		{
			Log.Info(
				$"[Debug/Player Lifecycle] {player.GameObject.Name} died " +
				$"from {damage.KillfeedCause}{conversion}; respawn in " +
				$"{plan.RespawnDelay:0.#} seconds." );
		}

		CommitAuthoritativeDeath(
			player,
			victimRole,
			damage,
			convertedToMinion );
		RefreshLastSkinnyKidState();
		EvaluateWinnerAfterLifecycleChange();
		return true;
	}

	/// <summary>
	/// Receives only applied, nonlethal health transactions. A lethal hit is
	/// represented directly by CommitAuthoritativeDeath, while a rejected lethal
	/// is rolled back by LargeLadHealth and never reaches this method.
	/// </summary>
	internal void RecordAppliedPlayerDamage(
		LargeLadPlayer victim,
		LargeLadDamageContext damage )
	{
		if ( !Networking.IsHost ||
			!OwnsSceneGameplay() ||
			!IsRoundActive ||
			victim is null ||
			!registeredPlayers.Contains( victim ) ||
			!TryResolveRegisteredAttacker(
				victim,
				damage.Attacker,
				out _,
				out var attackerIdentity ) )
		{
			return;
		}

		var victimIdentity = victim.GetRoleSelectionSessionIdentity();
		if ( !LargeLadCombatAttributionRules.IsValidContribution(
			victimIdentity,
			victim.Role,
			attackerIdentity,
			damage.AttackerRole,
			damage.AppliedDamage ) )
		{
			return;
		}

		recentDamageContributions.Record(
			new LargeLadDamageContribution(
				victimIdentity,
				victim.Role,
				attackerIdentity,
				damage.AttackerRole,
				activeRoundSequenceId,
				Time.Now,
				damage.AppliedDamage,
				damage.DamageType,
				damage.SourceWeapon ) );
	}

	private void CommitAuthoritativeDeath(
		LargeLadPlayer victim,
		LargeLadRole victimRole,
		LargeLadDamageContext damage,
		bool convertedToMinion )
	{
		var now = Time.Now;
		var victimIdentity = victim.GetRoleSelectionSessionIdentity();
		var contributions = recentDamageContributions.Consume(
			victimIdentity,
			activeRoundSequenceId,
			now );
		var validContributions = contributions
			.Where( contribution => TryFindRegisteredPlayerBySessionIdentity(
				contribution.AttackerSessionIdentity,
				out _ ) )
			.ToArray();
		LargeLadPlayer creditedKiller = null;
		var creditedKillerIdentity = string.Empty;
		var creditedKillerRole = LargeLadRole.Unassigned;
		var inheritedEnvironmentCredit = false;

		if ( damage.DamageType != LargeLadDamageType.Environment &&
			TryResolveRegisteredAttacker(
				victim,
				damage.Attacker,
				out var directAttacker,
				out var directAttackerIdentity ) &&
			LargeLadCombatAttributionRules.IsDirectKillCreditEligible(
				victimIdentity,
				victimRole,
				directAttackerIdentity,
				damage.AttackerRole,
				damage.DamageType,
				damage.AppliedDamage ) )
		{
			creditedKiller = directAttacker;
			creditedKillerIdentity = directAttackerIdentity;
			creditedKillerRole = damage.AttackerRole;
		}
		else if ( damage.DamageType == LargeLadDamageType.Environment &&
			LargeLadCombatAttributionRules.ResolveEnvironmentalKiller(
				validContributions,
				activeRoundSequenceId,
				now ) is { } inheritedContribution &&
			TryFindRegisteredPlayerBySessionIdentity(
				inheritedContribution.AttackerSessionIdentity,
				out var inheritedKiller ) )
		{
			// The most recent valid hostile contributor receives general/role
			// credit, but the lethal cause remains visibly environmental.
			creditedKiller = inheritedKiller;
			creditedKillerIdentity =
				inheritedContribution.AttackerSessionIdentity;
			creditedKillerRole = inheritedContribution.AttackerRole;
			inheritedEnvironmentCredit = true;
		}

		var assistantIdentities =
			LargeLadCombatAttributionRules.ResolveAssistantIdentities(
				validContributions,
				victimIdentity,
				creditedKillerIdentity,
				activeRoundSequenceId,
				now )
			.Where( identity => TryFindRegisteredPlayerBySessionIdentity(
				identity,
				out _ ) )
			.ToArray();
		var record = new LargeLadDeathRecord
		{
			EventSequenceId = ++nextDeathEventSequenceId,
			RoundSequenceId = activeRoundSequenceId,
			Victim = victim,
			VictimSessionIdentity = victimIdentity,
			VictimDisplayName = GetPlayerDisplayName( victim ),
			VictimRole = victimRole,
			CreditedKiller = creditedKiller,
			CreditedKillerSessionIdentity = creditedKillerIdentity,
			CreditedKillerDisplayName = creditedKiller is null
				? null
				: GetPlayerDisplayName( creditedKiller ),
			CreditedKillerRole = creditedKillerRole,
			KillfeedCause = damage.KillfeedCause,
			SourceWeapon = inheritedEnvironmentCredit
				? LargeLadWeaponId.None
				: damage.SourceWeapon,
			HitRegion = inheritedEnvironmentCredit
				? LargeLadHitRegion.None
				: damage.HitRegion,
			DamageType = damage.DamageType,
			WasEatExecution =
				damage.DamageType == LargeLadDamageType.Eat &&
				damage.IsExecution,
			WasEnvironmentalInfluenceKill = inheritedEnvironmentCredit,
			ConvertedToMinion = convertedToMinion,
			AssistantSessionIdentities = assistantIdentities
		};

		if ( victimRole == LargeLadRole.LargeLad )
			committedLargeLadDeathsThisRound++;

		victim.CommitSessionDeath();
		victim.SubmitCareerStats(
			LargeLadCareerStatRules.GetVictimDeltas( record ) );

		if ( creditedKiller is not null )
		{
			creditedKiller.CommitSessionKill();
			creditedKiller.SubmitCareerStats(
				LargeLadCareerStatRules.GetKillerDeltas( record ) );
		}

		foreach ( var assistantIdentity in assistantIdentities )
		{
			if ( !TryFindRegisteredPlayerBySessionIdentity(
				assistantIdentity,
				out var assistant ) )
			{
				continue;
			}

			assistant.CommitSessionAssist();
			assistant.SubmitCareerStats(
				LargeLadCareerStatRules.GetAssistantDeltas() );
		}

		PublishKillfeedEntry( record );
		AuthoritativeDeathCommitted?.Invoke( record );
	}

	private bool TryResolveRegisteredAttacker(
		LargeLadPlayer victim,
		GameObject attackerObject,
		out LargeLadPlayer attacker,
		out string attackerIdentity )
	{
		attacker = null;
		attackerIdentity = string.Empty;

		if ( attackerObject is null || !attackerObject.IsValid )
			return false;

		attacker = attackerObject.Components.Get<LargeLadPlayer>(
			FindMode.EverythingInSelfAndAncestors );

		if ( attacker is null ||
			!attacker.IsValid ||
			!attacker.Enabled ||
			attacker == victim ||
			attacker.Scene != Scene ||
			!registeredPlayers.Contains( attacker ) )
		{
			return false;
		}

		attackerIdentity = attacker.GetRoleSelectionSessionIdentity();
		return !string.IsNullOrWhiteSpace( attackerIdentity );
	}

	private bool TryFindRegisteredPlayerBySessionIdentity(
		string sessionIdentity,
		out LargeLadPlayer player )
	{
		player = null;

		if ( string.IsNullOrWhiteSpace( sessionIdentity ) )
			return false;

		player = activePlayers.FirstOrDefault( candidate =>
			candidate is not null &&
			candidate.IsValid &&
			candidate.Enabled &&
			candidate.Scene == Scene &&
			registeredPlayers.Contains( candidate ) &&
			string.Equals(
				candidate.GetRoleSelectionSessionIdentity(),
				sessionIdentity,
				StringComparison.Ordinal ) );
		return player is not null;
	}

	private static string GetPlayerDisplayName( LargeLadPlayer player )
	{
		if ( player is null )
			return "Unknown Player";

		var connection = player.Network.Owner ??
			Connection.Find( player.Network.OwnerId );
		return string.IsNullOrWhiteSpace( connection?.DisplayName )
			? player.GameObject.Name
			: connection.DisplayName;
	}

	private void PublishKillfeedEntry( LargeLadDeathRecord record )
	{
		var causeLabel = LargeLadKillfeedPresentationRules.GetCauseLabel(
			record.KillfeedCause );
		ReceiveKillfeedEntry(
			record.EventSequenceId,
			record.CreditedKillerDisplayName,
			record.VictimDisplayName,
			causeLabel,
			record.KillfeedCause,
			record.WasEnvironmentalInfluenceKill );
		BroadcastKillfeedEntry(
			record.EventSequenceId,
			record.CreditedKillerDisplayName,
			record.VictimDisplayName,
			causeLabel,
			record.KillfeedCause,
			record.WasEnvironmentalInfluenceKill );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	private void BroadcastKillfeedEntry(
		int eventSequenceId,
		string killerDisplayName,
		string victimDisplayName,
		string causeLabel,
		LargeLadKillfeedCause cause,
		bool wasEnvironmentalInfluenceKill )
	{
		if ( Networking.IsHost )
			return;

		ReceiveKillfeedEntry(
			eventSequenceId,
			killerDisplayName,
			victimDisplayName,
			causeLabel,
			cause,
			wasEnvironmentalInfluenceKill );
	}

	private void ReceiveKillfeedEntry(
		int eventSequenceId,
		string killerDisplayName,
		string victimDisplayName,
		string causeLabel,
		LargeLadKillfeedCause cause,
		bool wasEnvironmentalInfluenceKill )
	{
		if ( eventSequenceId <= lastReceivedKillfeedEventSequenceId )
			return;

		lastReceivedKillfeedEventSequenceId = eventSequenceId;
		localKillfeedEntries.Add( new LargeLadKillfeedEntry
		{
			EventSequenceId = eventSequenceId,
			KillerDisplayName = killerDisplayName,
			VictimDisplayName = string.IsNullOrWhiteSpace( victimDisplayName )
				? "Unknown Player"
				: victimDisplayName,
			CauseLabel = string.IsNullOrWhiteSpace( causeLabel )
				? "DEFEATED"
				: causeLabel,
			Cause = cause,
			WasEnvironmentalInfluenceKill =
				wasEnvironmentalInfluenceKill,
			ExpiresAt = Time.Now + KillfeedEntryDuration
		} );

		while ( localKillfeedEntries.Count > MaximumKillfeedEntries )
			localKillfeedEntries.RemoveAt( 0 );
	}

	private void PruneExpiredKillfeedEntries()
	{
		var now = Time.Now;
		localKillfeedEntries.RemoveAll( entry => entry.ExpiresAt <= now );
	}

	private void ClearLocalKillfeed()
	{
		localKillfeedEntries.Clear();
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	private void BroadcastClearKillfeed()
	{
		if ( Networking.IsHost )
			return;

		ClearLocalKillfeed();
	}

	public void RequestEnvironmentalDeath( LargeLadPlayer player )
	{
		if ( !Networking.IsHost ||
			!OwnsSceneGameplay() ||
			!IsRoundActive ||
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

		if ( !IsUsableBootstrapComponent( SessionCoordinator ) )
			SessionCoordinator = Components.Get<LargeLadSessionCoordinator>();

		SpawnAllocator?.ConfigureGameManager( this );
	}

	/// <summary>
	/// Runs focused map admission after MapInstance's loaded callback, then gives
	/// the admitted projection to the runtime allocator. The coordinator owns the
	/// resulting ready/not-ready transaction state.
	/// </summary>
	internal bool PrepareLoadedMap(
		LargeLadSessionCoordinator coordinator,
		LargeLadMapDescriptor descriptor )
	{
		ResolveBootstrapReferences();

		if ( !Networking.IsHost ||
			!OwnsSceneGameplay() ||
			coordinator is null ||
			coordinator != SessionCoordinator ||
			descriptor is null ||
			descriptor != coordinator.CurrentMapDescriptor )
		{
			return false;
		}

		activeMapDescriptor = descriptor;
		validatedBlockingMapIssues.Clear();
		var mapContentHost = coordinator.MapInstance?.GameObject;
		var mapValidation = LargeLadMapValidator.ValidateLoadedContent(
			mapContentHost );
		validatedBlockingMapIssues.AddRange(
			mapValidation.Issues
				.Where( issue => issue.IsBlocking )
				.Select( issue => issue.ToString() ) );

		SpawnAllocator?.InvalidateCandidateCache();
		SpawnAllocator?.ApplyCandidateProjection(
			mapValidation.SpawnProjection );
		SpawnAllocator?.ConfigureNetworkHelper( NetworkHelper );
		mapValidation.LogMapperSummary();

		if ( GetBlockingRoundSpawnIssues( requireReadyMap: false ).Count > 0 )
		{
			activeMapDescriptor = null;
			return false;
		}

		if ( TryPlacePlayersInLoadedMapLobby() )
			return true;

		activeMapDescriptor = null;
		return false;
	}

	/// <summary>
	/// Stops the active round transaction before MapInstance begins unloading.
	/// Players and session infrastructure persist, but inventories are detached
	/// from departing pickup sources and every player is held until a replacement
	/// map has validated and supplied Lobby positions.
	/// </summary>
	internal void HandleMapTransition(
		LargeLadSessionCoordinator coordinator )
	{
		ResolveBootstrapReferences();

		if ( !Networking.IsHost ||
			coordinator is null ||
			coordinator != SessionCoordinator )
		{
			return;
		}

		activeMapDescriptor = null;
		var players = GetActivePlayerSnapshot();
		roleSelectionSessionState =
			LargeLadRoleSelectionRules.AbortForMapTransition(
				roleSelectionSessionState );
		activeRoundStarterIdentities.Clear();
		activeRoundStartingRoles.Clear();
		activeRoundLargeLadIdentity = null;
		committedLargeLadDeathsThisRound = 0;
		recentDamageContributions.Clear();
		roundOutcomeCommitGate.Abort();
		ClearLocalKillfeed();

		if ( Game.IsPlaying )
			BroadcastClearKillfeed();

		foreach ( var player in players )
		{
			player.CancelEatParticipationForLifecycle();
			player.Health?.ClearPassiveRegenerationState();
			player.NativeInventory?.HandleMapTransition( Scene );
			player.SetPendingRespawnRole( LargeLadRole.Unassigned );
			player.MovementLocked = true;
		}

		PhaseEndTime = 0.0f;
		ResetSurvivalRoundTiming();
		ResetLastSkinnyKidState();
		Winner = LargeLadWinner.None;
		barricadeDestructionAnnouncement = null;
		timeSinceBarricadeDestructionAnnouncement = 0.0f;
		waitingPlayerCount = players.Count;
		playerReadyTimeRemaining = PlayerReadyDelay;
		spawnFailureReported = false;
		lobbyPlacedPlayers.Clear();
		reportedSpawnAllocationFailures.Clear();
		validatedBlockingBootstrapIssues.Clear();
		validatedBlockingMapIssues.Clear();

		// A map/session boundary is not a gameplay phase transition, so it
		// deliberately bypasses the ordinary round transition table.
		Phase = LargeLadRoundPhase.WaitingForPlayers;

		SpawnAllocator?.InvalidateCandidateCache();
		SpawnAllocator?.RefreshNetworkHelperLobbyPoints();

		Log.Info(
			$"Large Lad map-transition cleanup completed for " +
			$"{players.Count} persistent player(s)." );
	}

	private bool TryPlacePlayersInLoadedMapLobby()
	{
		var players = GetActivePlayerSnapshot();

		if ( players.Count == 0 )
			return true;

		var allocations = AllocateSpawnBatch(
			LargeLadSpawnGroup.Lobby,
			players );

		if ( !LargeLadSpawnRules.HasCompleteBatchAllocation(
			players,
			allocations ) )
		{
			foreach ( var player in players )
				player.MovementLocked = true;

			Log.Error(
				"Loaded-map Lobby placement failed before readiness: received " +
				$"{allocations?.Count ?? 0}/{players.Count} required positions." );
			return false;
		}

		lobbyPlacedPlayers.Clear();

		foreach ( var player in players )
		{
			player.RespawnAs(
				LargeLadRole.Unassigned,
				allocations[player] );
			lobbyPlacedPlayers.Add( player );
		}

		waitingPlayerCount = players.Count;
		playerReadyTimeRemaining = PlayerReadyDelay;
		return true;
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

		// NetworkHelper can create players while the persistent shell has no map.
		// Freeze them at creation instead of allowing their owning peer to simulate
		// gravity in the void while the host is choosing or loading content.
		if ( Networking.IsHost &&
			SessionCoordinator?.CanAdvanceRoundFlow != true )
		{
			player.MovementLocked = true;
		}

		if ( !isHydratingRegistrations )
		{
			RefreshLastSkinnyKidState();
			EvaluateWinnerAfterLifecycleChange();
		}
	}

	internal bool IsRegisteredPlayer( LargeLadPlayer player )
	{
		PruneInvalidRegistrations();
		return player is not null && registeredPlayers.Contains( player );
	}

	internal void UnregisterPlayer( LargeLadPlayer player )
	{
		if ( player is null || !registeredPlayers.Remove( player ) )
			return;

		if ( Networking.IsHost )
		{
			player.CancelEatParticipationForLifecycle();
			player.NativeInventory?.HandleDisconnect();

			var identity = player.GetRoleSelectionSessionIdentity();
			firstRoundBootstrapRosterIdentities.Remove( identity );
			activeRoundStarterIdentities.Remove( identity );
			activeRoundStartingRoles.Remove( identity );
			lastSkinnyKidIdentitiesThisRound.Remove( identity );
			recentDamageContributions.RemovePlayer( identity );
			roleSelectionSessionState =
				LargeLadRoleSelectionRules.ForgetDisconnectedPlayer(
					roleSelectionSessionState,
					identity );
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
			EffectiveSurvivalDuration );
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
