using Sandbox;

public enum LargeLadGroundSlamPresentationPhase
{
	Windup,
	Impact,
	CameraAudioFeedback,
	CooldownStarted,
	CooldownReady,
	Cancelled
}

public readonly struct LargeLadGroundSlamPresentation
{
	public LargeLadGroundSlamPresentation(
		LargeLadGroundSlamPresentationPhase phase,
		int sequence,
		Vector3 origin,
		float radius,
		float duration,
		float strength )
	{
		Phase = phase;
		Sequence = sequence;
		Origin = origin;
		Radius = radius;
		Duration = duration;
		Strength = strength;
	}

	public LargeLadGroundSlamPresentationPhase Phase { get; }
	public int Sequence { get; }
	public Vector3 Origin { get; }
	public float Radius { get; }
	public float Duration { get; }
	public float Strength { get; }
}

/// <summary>
/// Large Lad's host-authoritative secondary attack. Clients request an
/// activation and receive presentation only; the host performs every range,
/// eligibility, and obstruction decision at the end of the windup.
/// </summary>
[Description(
	"Large Lad's secondary, zero-damage crowd-control attack. Skinny Kids are " +
	"impulsed and staggered; Minions receive friendly impulse without damage " +
	"or stagger; only explicitly authored props can react." )]
public sealed class LargeLadGroundSlam : Component
{
	private const float HostCadenceTolerance = 0.025f;
	private const float ObstructionTolerance = 2.0f;

	[Property, Group( "Timing" )]
	public float Cooldown { get; set; } = 5.0f;

	[Property, Group( "Timing" )]
	public float Windup { get; set; } = 0.45f;

	[Property, Group( "Targeting" ), Title( "Radial Range" )]
	public float Radius { get; set; } = 180.0f;

	[Property, Group( "Targeting" ), Title( "Trace Height" )]
	[Description(
		"Raises the radial visibility trace above the floor. Player and prop " +
		"effects still stop at walls, floors, and other blocking geometry." )]
	public float TraceHeight { get; set; } = 36.0f;

	[Property, Group( "Skinny Kid Effect" ), Title( "Horizontal Impulse" )]
	public float SkinnyKidHorizontalImpulse { get; set; } = 70000.0f;

	[Property, Group( "Skinny Kid Effect" ), Title( "Upward Impulse" )]
	public float SkinnyKidUpwardImpulse { get; set; } = 125000.0f;

	[Property, Group( "Skinny Kid Effect" ), Title( "Stagger Duration" )]
	public float SkinnyKidStaggerDuration { get; set; } = 0.65f;

	[Property, Group( "Minion Friendly Impulse" ), Title( "Horizontal Impulse" )]
	[Description(
		"Minions take no Ground Slam damage or stagger. This explicit physics " +
		"impulse may still move them." )]
	public float MinionHorizontalImpulse { get; set; } = 50000.0f;

	[Property, Group( "Minion Friendly Impulse" ), Title( "Upward Impulse" )]
	public float MinionUpwardImpulse { get; set; } = 75000.0f;

	[Property, Group( "Presentation" ), Title( "Feedback Radius" )]
	public float FeedbackRadius { get; set; } = 700.0f;

	[Property, Group( "Presentation" ), Title( "Feedback Strength" )]
	public float FeedbackStrength { get; set; } = 1.0f;

	[Property, Group( "Diagnostics" )]
	[Description(
		"Logs accepted activations, host impact selection totals, and owner-side " +
		"velocity application. Enable manually for focused multiplayer diagnosis; " +
		"leave disabled for normal play." )]
	public bool EnableDebugLogging { get; set; } = false;

	[Sync( SyncFlags.FromHost )]
	public bool IsWindingUp { get; private set; }

	/// <summary>
	/// Raised on each peer after a replicated presentation message. Camera,
	/// audio, particles, and HUD code may subscribe, but this event carries no
	/// hit authority and cannot add gameplay targets.
	/// </summary>
	public event System.Action<LargeLadGroundSlamPresentation> Presentation;

	private readonly LargeLadGroundSlamHostState hostState = new();
	private readonly LargeLadGroundSlamOwnerState ownerState = new();
	private int nextOwnerActivationSequence;
	private float impactTime;
	private int lastLocalPresentationSequence;
	private int lastLocalCancellationSequence;
	private LargeLadPlayer cachedAttacker;
	private PlayerController cachedController;
	private LargeLadGameManager cachedGameManager;

	protected override void OnAwake()
	{
		ResolveCachedReferences();
	}

	protected override void OnStart()
	{
		ResolveCachedReferences();
	}

	protected override void OnUpdate()
	{
		ResolveCachedReferences();

		if ( Networking.IsHost )
			TickAuthoritativeState();

		TickLocalPresentationState();

		if ( IsProxy ||
			!Input.Pressed( "Attack2" ) )
		{
			return;
		}

		var cooldownRemaining = ownerState.GetCooldownRemaining( Time.Now );

		if ( cooldownRemaining > 0.0f )
		{
			if ( EnableDebugLogging )
			{
				Log.Info(
					$"[Debug/Ground Slam] Owner input ignored during " +
					$"accepted authoritative cooldown " +
					$"({cooldownRemaining:0.###} seconds remaining)." );
			}

			return;
		}

		nextOwnerActivationSequence++;

		if ( EnableDebugLogging )
		{
			Log.Info(
				$"[Debug/Ground Slam] Owner requested sequence " +
				$"{nextOwnerActivationSequence} from {GameObject.Name}." );
		}

		// The owner reports input only. Role, phase, health, movement state,
		// authoritative cadence, range, obstruction, and hits are all validated
		// by RequestGroundSlam on the host.
		RequestGroundSlam( nextOwnerActivationSequence );
	}

	protected override void OnDisabled()
	{
		CancelForLifecycle( resetOwnerState: false );
		base.OnDisabled();
	}

	protected override void OnDestroy()
	{
		CancelForLifecycle( resetOwnerState: true );
		base.OnDestroy();
	}

	protected override void OnValidate()
	{
		ValidatePositive( nameof( Cooldown ), Cooldown );
		ValidateNonNegative( nameof( Windup ), Windup );
		ValidatePositive( nameof( Radius ), Radius );
		ValidateNonNegative( nameof( TraceHeight ), TraceHeight );
		ValidateNonNegative(
			nameof( SkinnyKidStaggerDuration ),
			SkinnyKidStaggerDuration );
		ValidateBoundedImpulse(
			nameof( SkinnyKidHorizontalImpulse ),
			SkinnyKidHorizontalImpulse,
			LargeLadGroundSlamRules.MaximumHorizontalImpulse );
		ValidateBoundedImpulse(
			nameof( SkinnyKidUpwardImpulse ),
			SkinnyKidUpwardImpulse,
			LargeLadGroundSlamRules.MaximumUpwardImpulse );
		ValidateBoundedImpulse(
			nameof( MinionHorizontalImpulse ),
			MinionHorizontalImpulse,
			LargeLadGroundSlamRules.MaximumHorizontalImpulse );
		ValidateBoundedImpulse(
			nameof( MinionUpwardImpulse ),
			MinionUpwardImpulse,
			LargeLadGroundSlamRules.MaximumUpwardImpulse );
		ValidatePositive( nameof( FeedbackRadius ), FeedbackRadius );
		ValidateNonNegative( nameof( FeedbackStrength ), FeedbackStrength );
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	private void RequestGroundSlam( int ownerActivationSequence )
	{
		if ( !Networking.IsHost )
			return;

		ResolveCachedReferences();
		var hostNow = Time.Now;
		var decision = hostState.EvaluateRequest(
			ownerActivationSequence,
			CanActivate( cachedAttacker, cachedController ),
			hostNow,
			Cooldown,
			HostCadenceTolerance );

		if ( !decision.IsNewRequest )
			return;

		var origin = GetSlamOrigin( cachedAttacker );
		ReceiveHostActivationResult(
			ownerActivationSequence,
			decision.Accepted,
			decision.CooldownEndTime,
			origin );

		if ( !decision.Accepted )
			return;

		var windup = System.MathF.Max( 0.0f, Windup );
		impactTime = hostNow + windup;
		IsWindingUp = true;

		ReceiveWindupPresentation(
			hostState.ActiveSequence,
			origin,
			windup );
		BroadcastWindupPresentation(
			hostState.ActiveSequence,
			origin,
			windup );

		if ( EnableDebugLogging )
		{
			Log.Info(
				$"[Debug/Ground Slam] Host accepted sequence " +
				$"{hostState.ActiveSequence} " +
				$"from {GameObject.Name}; impact in {windup:0.###} seconds, " +
				$"radius {Radius:0.###}." );
		}
	}

	private void TickAuthoritativeState()
	{
		var now = Time.Now;
		var result = hostState.ResolveWindup(
			CanCompleteWindup( cachedAttacker ),
			now >= impactTime );

		switch ( result )
		{
			case LargeLadGroundSlamWindupResult.Cancelled:
				IsWindingUp = false;
				BroadcastCancellation( hostState.ActiveSequence );
				break;
			case LargeLadGroundSlamWindupResult.Impact:
				IsWindingUp = false;
				CompleteAuthoritativeSlam();
				break;
		}
	}

	private void CompleteAuthoritativeSlam()
	{
		if ( !Networking.IsHost || !CanCompleteWindup( cachedAttacker ) )
		{
			IsWindingUp = false;
			BroadcastCancellation( hostState.ActiveSequence );
			return;
		}

		var origin = GetSlamOrigin( cachedAttacker );
		var playerSelection = ApplyPlayerEffects( origin );
		var affectedPropCount = ApplyReactivePropEffects( origin );
		ReceiveImpactPresentation( hostState.ActiveSequence, origin );
		BroadcastImpactPresentation( hostState.ActiveSequence, origin );

		if ( EnableDebugLogging )
		{
			Log.Info(
				$"[Debug/Ground Slam] Host completed sequence " +
				$"{hostState.ActiveSequence} at {origin}: affected " +
				$"{playerSelection.AffectedPlayers} player(s) and " +
				$"{affectedPropCount} mapped prop(s); considered " +
				$"{playerSelection.RoleCandidates} Skinny Kid/Minion " +
				$"candidate(s), rejected " +
				$"{playerSelection.InvalidPlayers} invalid, " +
				$"{playerSelection.OutOfRangePlayers} out of range, and " +
				$"{playerSelection.ObstructedPlayers} obstructed." );
		}
	}

	private PlayerSelectionSummary ApplyPlayerEffects( Vector3 origin )
	{
		var manager = GetGameManager();
		var summary = new PlayerSelectionSummary();

		if ( manager is null )
			return summary;

		foreach ( var target in manager.ActivePlayers )
		{
			if ( target is null || target == cachedAttacker )
				continue;

			if ( target.Role is not
				(LargeLadRole.SkinnyKid or LargeLadRole.Minion) )
			{
				continue;
			}

			summary.RoleCandidates++;

			var targetPoint = target.GameObject.WorldPosition +
				Vector3.Up * System.MathF.Max( 0.0f, TraceHeight );
			var livingAndActive = target.IsValid &&
				target.Enabled &&
				target.Scene == Scene &&
				target.Health?.IsDead == false &&
				target.Health.CurrentHealth > 0.0f;
			var distance = origin.Distance( targetPoint );

			if ( !livingAndActive )
			{
				summary.InvalidPlayers++;
				continue;
			}

			if ( distance > System.MathF.Max( 0.0f, Radius ) )
			{
				summary.OutOfRangePlayers++;
				continue;
			}

			var obstructed = IsPlayerObstructed( origin, targetPoint );

			if ( obstructed )
			{
				summary.ObstructedPlayers++;
				continue;
			}

			if ( !LargeLadGroundSlamRules.CanAffectPlayer(
				target.Role,
				livingAndActive,
				obstructed,
				distance,
				Radius ) )
			{
				continue;
			}

			var horizontalImpulse = target.Role == LargeLadRole.Minion
				? MinionHorizontalImpulse
				: SkinnyKidHorizontalImpulse;
			var upwardImpulse = target.Role == LargeLadRole.Minion
				? MinionUpwardImpulse
				: SkinnyKidUpwardImpulse;
			var impulse = LargeLadGroundSlamRules.GetRadialImpulse(
				origin,
				targetPoint,
				horizontalImpulse,
				upwardImpulse,
				target.GameObject.Id.CompareTo( GameObject.Id ) >= 0 );
			var staggerDuration =
				LargeLadGroundSlamRules.ShouldStaggerPlayer( target.Role )
					? System.MathF.Max( 0.0f, SkinnyKidStaggerDuration )
					: 0.0f;
			target.ApplyGroundSlamEffect( impulse, staggerDuration );
			summary.AffectedPlayers++;
		}

		return summary;
	}

	private int ApplyReactivePropEffects( Vector3 origin )
	{
		var manager = GetGameManager();
		var affectedCount = 0;

		if ( manager is null )
			return affectedCount;

		foreach ( var reactiveProp in manager.ActiveGroundSlamReactiveProps )
		{
			if ( reactiveProp is null ||
				!reactiveProp.IsValid ||
				!reactiveProp.Enabled ||
				reactiveProp.Scene != Scene )
			{
				continue;
			}

			var targetPoint = reactiveProp.GetSlamEffectPoint();
			var distance = origin.Distance( targetPoint );

			if ( distance > System.MathF.Max( 0.0f, Radius ) ||
				IsReactivePropObstructed(
					origin,
					targetPoint,
					reactiveProp ) )
			{
				continue;
			}

			if ( reactiveProp.TryReactToGroundSlam( origin ) )
				affectedCount++;
		}

		return affectedCount;
	}

	private bool IsPlayerObstructed( Vector3 origin, Vector3 targetPoint )
	{
		var expectedDistance = origin.Distance( targetPoint );
		var trace = Scene.Trace
			.Ray( origin, targetPoint )
			.WithoutTags( LargeLadGameplayRules.PlayerBodyTag )
			.IgnoreGameObjectHierarchy( GameObject )
			.Run();

		return trace.Hit &&
			trace.Distance + ObstructionTolerance < expectedDistance;
	}

	private bool IsReactivePropObstructed(
		Vector3 origin,
		Vector3 targetPoint,
		LargeLadGroundSlamReactiveProp target )
	{
		var trace = Scene.Trace
			.Ray( origin, targetPoint )
			.WithoutTags( LargeLadGameplayRules.PlayerBodyTag )
			.IgnoreGameObjectHierarchy( GameObject )
			.Run();

		return trace.Hit &&
			LargeLadGroundSlamReactiveProp.FindFor( trace.GameObject ) != target;
	}

	private bool CanActivate(
		LargeLadPlayer attacker,
		PlayerController controller )
	{
		return attacker is not null &&
			controller is not null &&
			attacker.GroundSlam == this &&
			attacker.Role == LargeLadRole.LargeLad &&
			attacker.Health?.IsDead == false &&
			attacker.Health.CurrentHealth > 0.0f &&
			!attacker.MovementLocked &&
			!attacker.IsEatBusy &&
			!IsWindingUp &&
			GetGameManager()?.Phase == LargeLadRoundPhase.Playing;
	}

	private bool CanCompleteWindup( LargeLadPlayer attacker )
	{
		return attacker is not null &&
			attacker.IsValid &&
			attacker.Enabled &&
			attacker.Scene == Scene &&
			attacker.Role == LargeLadRole.LargeLad &&
			attacker.Health?.IsDead == false &&
			attacker.Health.CurrentHealth > 0.0f &&
			!attacker.IsEatBusy &&
			GetGameManager()?.Phase == LargeLadRoundPhase.Playing;
	}

	private Vector3 GetSlamOrigin( LargeLadPlayer attacker )
	{
		return (attacker?.GameObject.WorldPosition ?? GameObject.WorldPosition) +
			Vector3.Up * System.MathF.Max( 0.0f, TraceHeight );
	}

	private void TickLocalPresentationState()
	{
		if ( IsProxy )
			return;

		if ( ownerState.HasCooldownPresentation &&
			!CanRetainPresentation() )
		{
			CancelLocalPresentation( ownerState.AcceptedSequence );
			return;
		}

		if ( ownerState.TryTakeCooldownReadyPresentation( Time.Now ) )
		{
			ReceiveCooldownReadyPresentation(
				ownerState.AcceptedSequence,
				GetSlamOrigin( cachedAttacker ) );
		}
	}

	private bool CanRetainPresentation()
	{
		return cachedAttacker is not null &&
			cachedAttacker.IsValid &&
			cachedAttacker.Enabled &&
			cachedAttacker.Scene == Scene &&
			cachedAttacker.Role == LargeLadRole.LargeLad &&
			cachedAttacker.Health?.IsDead == false &&
			cachedAttacker.Health.CurrentHealth > 0.0f &&
			GetGameManager()?.Phase == LargeLadRoundPhase.Playing;
	}

	private void CancelForLifecycle( bool resetOwnerState )
	{
		var sequence = hostState.ActiveSequence > 0
			? hostState.ActiveSequence
			: ownerState.AcceptedSequence > 0
				? ownerState.AcceptedSequence
				: lastLocalPresentationSequence;

		if ( Networking.IsHost && hostState.CancelWindup() )
		{
			IsWindingUp = false;
			BroadcastCancellation( sequence );
		}
		else
		{
			CancelLocalPresentation( sequence );
		}

		if ( resetOwnerState )
			ownerState.Reset();
	}

	private void CancelLocalPresentation( int sequence )
	{
		ownerState.CancelPresentation();

		if ( sequence <= 0 || sequence <= lastLocalCancellationSequence )
			return;

		ReceiveCancellationPresentation(
			sequence,
			GetSlamOrigin( cachedAttacker ) );
	}

	private void BroadcastCancellation( int sequence )
	{
		if ( sequence <= 0 )
			return;

		var origin = GetSlamOrigin( cachedAttacker );
		ReceiveCancellationPresentation( sequence, origin );
		BroadcastCancellationPresentation( sequence, origin );
	}

	private void ResolveCachedReferences()
	{
		if ( cachedAttacker is null ||
			!cachedAttacker.IsValid ||
			cachedAttacker.GameObject != GameObject )
		{
			cachedAttacker = Components.Get<LargeLadPlayer>();
		}

		if ( cachedController is null ||
			!cachedController.IsValid ||
			cachedController.GameObject != GameObject )
		{
			cachedController = Components.Get<PlayerController>();
		}
	}

	private LargeLadGameManager GetGameManager()
	{
		if ( cachedGameManager is null ||
			!cachedGameManager.IsValid ||
			cachedGameManager.Scene != Scene )
		{
			cachedGameManager = LargeLadGameManager.FindForScene( Scene );
		}

		return cachedGameManager;
	}

	private void ReceiveWindupPresentation(
		int sequence,
		Vector3 origin,
		float duration )
	{
		if ( sequence <= lastLocalCancellationSequence )
			return;

		lastLocalPresentationSequence = System.Math.Max(
			lastLocalPresentationSequence,
			sequence );

		RaisePresentation(
			LargeLadGroundSlamPresentationPhase.Windup,
			sequence,
			origin,
			Radius,
			duration,
			FeedbackStrength );
	}

	[Rpc.Broadcast]
	private void BroadcastWindupPresentation(
		int sequence,
		Vector3 origin,
		float duration )
	{
		if ( Networking.IsHost )
			return;

		ReceiveWindupPresentation( sequence, origin, duration );
	}

	private void ReceiveImpactPresentation( int sequence, Vector3 origin )
	{
		if ( sequence <= lastLocalCancellationSequence )
			return;

		lastLocalPresentationSequence = System.Math.Max(
			lastLocalPresentationSequence,
			sequence );

		RaisePresentation(
			LargeLadGroundSlamPresentationPhase.Impact,
			sequence,
			origin,
			Radius,
			0.0f,
			FeedbackStrength );
		RaisePresentation(
			LargeLadGroundSlamPresentationPhase.CameraAudioFeedback,
			sequence,
			origin,
			FeedbackRadius,
			0.0f,
			FeedbackStrength );
	}

	[Rpc.Broadcast]
	private void BroadcastImpactPresentation( int sequence, Vector3 origin )
	{
		if ( Networking.IsHost )
			return;

		ReceiveImpactPresentation( sequence, origin );
	}

	[Rpc.Owner( NetFlags.HostOnly )]
	private void ReceiveHostActivationResult(
		int sequence,
		bool accepted,
		float authoritativeCooldownEndTime,
		Vector3 origin )
	{
		if ( !ownerState.ApplyHostResult(
			sequence,
			accepted,
			authoritativeCooldownEndTime,
			Time.Now ) )
		{
			return;
		}

		if ( !accepted )
			return;

		if ( sequence <= lastLocalCancellationSequence )
		{
			ownerState.CancelPresentation();
			return;
		}

		if ( !CanRetainPresentation() )
		{
			ownerState.CancelPresentation();
			return;
		}

		lastLocalPresentationSequence = System.Math.Max(
			lastLocalPresentationSequence,
			sequence );
		var cooldownRemaining = ownerState.GetCooldownRemaining( Time.Now );
		RaisePresentation(
			LargeLadGroundSlamPresentationPhase.CooldownStarted,
			sequence,
			origin,
			0.0f,
			cooldownRemaining,
			0.0f );
	}

	private void ReceiveCooldownReadyPresentation(
		int sequence,
		Vector3 origin )
	{
		RaisePresentation(
			LargeLadGroundSlamPresentationPhase.CooldownReady,
			sequence,
			origin,
			0.0f,
			0.0f,
			0.0f );
	}

	private void ReceiveCancellationPresentation(
		int sequence,
		Vector3 origin )
	{
		if ( sequence <= 0 || sequence <= lastLocalCancellationSequence )
			return;

		lastLocalCancellationSequence = sequence;
		ownerState.CancelPresentation();
		RaisePresentation(
			LargeLadGroundSlamPresentationPhase.Cancelled,
			sequence,
			origin,
			0.0f,
			0.0f,
			0.0f );
	}

	[Rpc.Broadcast]
	private void BroadcastCancellationPresentation(
		int sequence,
		Vector3 origin )
	{
		if ( Networking.IsHost )
			return;

		ReceiveCancellationPresentation( sequence, origin );
	}

	private void RaisePresentation(
		LargeLadGroundSlamPresentationPhase phase,
		int sequence,
		Vector3 origin,
		float radius,
		float duration,
		float strength )
	{
		Presentation?.Invoke(
			new LargeLadGroundSlamPresentation(
				phase,
				sequence,
				origin,
				System.MathF.Max( 0.0f, radius ),
				System.MathF.Max( 0.0f, duration ),
				System.MathF.Max( 0.0f, strength ) ) );
	}

	private void ValidatePositive( string name, float value )
	{
		if ( !float.IsFinite( value ) || value <= 0.0f )
			Log.Warning( $"{GameObject.Name}: Ground Slam {name} must be positive." );
	}

	private void ValidateNonNegative( string name, float value )
	{
		if ( !float.IsFinite( value ) || value < 0.0f )
		{
			Log.Warning(
				$"{GameObject.Name}: Ground Slam {name} cannot be negative." );
		}
	}

	private void ValidateBoundedImpulse(
		string name,
		float value,
		float maximum )
	{
		if ( !float.IsFinite( value ) || value < 0.0f || value > maximum )
		{
			Log.Warning(
				$"{GameObject.Name}: Ground Slam {name} must be 0 to " +
				$"{maximum}. Runtime use is clamped to that range." );
		}
	}

	private sealed class PlayerSelectionSummary
	{
		public int RoleCandidates { get; set; }
		public int InvalidPlayers { get; set; }
		public int OutOfRangePlayers { get; set; }
		public int ObstructedPlayers { get; set; }
		public int AffectedPlayers { get; set; }
	}
}
