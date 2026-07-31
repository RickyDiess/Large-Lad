using Sandbox;

public enum LargeLadGroundSlamPresentationPhase
{
	Windup,
	Impact,
	CameraAudioFeedback,
	CooldownStarted,
	CooldownReady
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
	"Large Lad secondary Ground Slam. The host owns windup completion, " +
	"line-of-sight checks, player stagger, and explicitly mapped prop reactions." )]
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

	[Property, Group( "Presentation" ), Title( "Windup Animation Parameter" )]
	public string WindupAnimationParameter { get; set; } = "b_attack";

	[Property, Group( "Presentation" ), Title( "Impact Animation Parameter" )]
	public string ImpactAnimationParameter { get; set; } = "b_attack";

	[Property, Group( "Presentation" )]
	public SoundEvent WindupSound { get; set; }

	[Property, Group( "Presentation" )]
	public SoundEvent ImpactSound { get; set; }

	[Property, Group( "Presentation" ), Title( "Cooldown Ready Sound" )]
	public SoundEvent CooldownReadySound { get; set; }

	[Property, Group( "Presentation" ), Title( "Feedback Radius" )]
	public float FeedbackRadius { get; set; } = 700.0f;

	[Property, Group( "Presentation" ), Title( "Feedback Strength" )]
	public float FeedbackStrength { get; set; } = 1.0f;

	[Property, Group( "Diagnostics" )]
	[Description(
		"Logs accepted activations, host impact selection totals, and owner-side " +
		"velocity application. Useful for multiplayer map testing." )]
	public bool EnableDebugLogging { get; set; }

	[Sync( SyncFlags.FromHost )]
	public bool IsWindingUp { get; private set; }

	/// <summary>
	/// Raised on each peer after a replicated presentation message. Camera,
	/// audio, particles, and HUD code may subscribe, but this event carries no
	/// hit authority and cannot add gameplay targets.
	/// </summary>
	public event System.Action<LargeLadGroundSlamPresentation> Presentation;

	private TimeSince timeSinceLocalActivation;
	private int nextOwnerActivationSequence;
	private int lastHostActivationSequence;
	private int activeSequence;
	private float impactTime;
	private bool hasHostActivationSchedule;
	private float nextHostActivationTime;
	private bool cooldownReadyPresentationPending;
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

		if ( IsProxy ||
			!Input.Pressed( "Attack2" ) )
		{
			return;
		}

		var localCooldown = System.MathF.Max( 0.01f, Cooldown );

		if ( timeSinceLocalActivation < localCooldown )
		{
			if ( EnableDebugLogging )
			{
				Log.Info(
					$"[Debug/Ground Slam] Owner input ignored during local " +
					$"cooldown ({timeSinceLocalActivation.Relative:0.###}/" +
					$"{localCooldown:0.###} seconds)." );
			}

			return;
		}

		timeSinceLocalActivation = 0.0f;
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
		CancelAuthoritativeWindup();
		base.OnDisabled();
	}

	protected override void OnDestroy()
	{
		CancelAuthoritativeWindup();
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
		if ( !Networking.IsHost ||
			ownerActivationSequence <= lastHostActivationSequence )
		{
			return;
		}

		lastHostActivationSequence = ownerActivationSequence;
		ResolveCachedReferences();

		if ( !CanActivate( cachedAttacker, cachedController ) )
			return;

		var hostNow = Time.Now;

		if ( hasHostActivationSchedule &&
			hostNow + HostCadenceTolerance < nextHostActivationTime )
		{
			return;
		}

		CommitHostCadence( hostNow );
		activeSequence = ownerActivationSequence;
		impactTime = hostNow + System.MathF.Max( 0.0f, Windup );
		IsWindingUp = true;
		cooldownReadyPresentationPending = true;
		var origin = GetSlamOrigin( cachedAttacker );
		var windup = System.MathF.Max( 0.0f, Windup );
		var cooldownRemaining = System.MathF.Max(
			0.0f,
			nextHostActivationTime - hostNow );

		ReceiveWindupPresentation( activeSequence, origin, windup );
		BroadcastWindupPresentation( activeSequence, origin, windup );
		ReceiveCooldownStartedPresentation(
			activeSequence,
			origin,
			cooldownRemaining );
		BroadcastCooldownStartedPresentation(
			activeSequence,
			origin,
			cooldownRemaining );

		if ( EnableDebugLogging )
		{
			Log.Info(
				$"[Debug/Ground Slam] Host accepted sequence {activeSequence} " +
				$"from {GameObject.Name}; impact in {windup:0.###} seconds, " +
				$"radius {Radius:0.###}." );
		}
	}

	private void TickAuthoritativeState()
	{
		var now = Time.Now;

		if ( IsWindingUp )
		{
			if ( !CanCompleteWindup( cachedAttacker ) )
			{
				IsWindingUp = false;
			}
			else if ( now >= impactTime )
			{
				CompleteAuthoritativeSlam();
			}
		}

		if ( cooldownReadyPresentationPending &&
			hasHostActivationSchedule &&
			now >= nextHostActivationTime )
		{
			cooldownReadyPresentationPending = false;
			var origin = GetSlamOrigin( cachedAttacker );
			ReceiveCooldownReadyPresentation( activeSequence, origin );
			BroadcastCooldownReadyPresentation( activeSequence, origin );
		}
	}

	private void CompleteAuthoritativeSlam()
	{
		if ( !Networking.IsHost || !CanCompleteWindup( cachedAttacker ) )
		{
			IsWindingUp = false;
			return;
		}

		var origin = GetSlamOrigin( cachedAttacker );
		var playerSelection = ApplyPlayerEffects( origin );
		var affectedPropCount = ApplyReactivePropEffects( origin );
		IsWindingUp = false;
		ReceiveImpactPresentation( activeSequence, origin );
		BroadcastImpactPresentation( activeSequence, origin );

		if ( EnableDebugLogging )
		{
			Log.Info(
				$"[Debug/Ground Slam] Host completed sequence " +
				$"{activeSequence} at {origin}: affected " +
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

	private void CommitHostCadence( float hostNow )
	{
		var cooldown = System.MathF.Max( 0.01f, Cooldown );

		if ( !hasHostActivationSchedule )
		{
			hasHostActivationSchedule = true;
			nextHostActivationTime = hostNow + cooldown;
			return;
		}

		nextHostActivationTime =
			System.MathF.Max( hostNow, nextHostActivationTime ) + cooldown;
	}

	private Vector3 GetSlamOrigin( LargeLadPlayer attacker )
	{
		return (attacker?.GameObject.WorldPosition ?? GameObject.WorldPosition) +
			Vector3.Up * System.MathF.Max( 0.0f, TraceHeight );
	}

	private void CancelAuthoritativeWindup()
	{
		if ( Networking.IsHost )
			IsWindingUp = false;
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
		TriggerAnimation( WindupAnimationParameter );

		if ( WindupSound is not null )
			Sound.Play( WindupSound, origin );

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
		TriggerAnimation( ImpactAnimationParameter );

		if ( ImpactSound is not null )
			Sound.Play( ImpactSound, origin );

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

	private void ReceiveCooldownStartedPresentation(
		int sequence,
		Vector3 origin,
		float duration )
	{
		RaisePresentation(
			LargeLadGroundSlamPresentationPhase.CooldownStarted,
			sequence,
			origin,
			0.0f,
			duration,
			0.0f );
	}

	[Rpc.Broadcast]
	private void BroadcastCooldownStartedPresentation(
		int sequence,
		Vector3 origin,
		float duration )
	{
		if ( Networking.IsHost )
			return;

		ReceiveCooldownStartedPresentation( sequence, origin, duration );
	}

	private void ReceiveCooldownReadyPresentation(
		int sequence,
		Vector3 origin )
	{
		if ( !IsProxy && CooldownReadySound is not null )
			Sound.Play( CooldownReadySound, origin );

		RaisePresentation(
			LargeLadGroundSlamPresentationPhase.CooldownReady,
			sequence,
			origin,
			0.0f,
			0.0f,
			0.0f );
	}

	[Rpc.Broadcast]
	private void BroadcastCooldownReadyPresentation(
		int sequence,
		Vector3 origin )
	{
		if ( Networking.IsHost )
			return;

		ReceiveCooldownReadyPresentation( sequence, origin );
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

	private void TriggerAnimation( string parameter )
	{
		if ( cachedAttacker?.BodyRenderer is null ||
			string.IsNullOrWhiteSpace( parameter ) )
		{
			return;
		}

		cachedAttacker.BodyRenderer.Set( parameter, true );
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
