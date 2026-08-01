using Sandbox;

/// <summary>
/// Local-only presentation consumer for Ground Slam. It owns animation hooks,
/// sound, transient particles, camera shake, and owner HUD timing; it never
/// queries or mutates gameplay targets.
/// </summary>
public sealed class LargeLadGroundSlamPresentationController : Component
{
	[Property, RequireComponent]
	public LargeLadGroundSlam GroundSlam { get; set; }

	[Property, Group( "Animation" ), Title( "Windup Parameter" )]
	public string WindupAnimationParameter { get; set; } = "b_attack";

	[Property, Group( "Animation" ), Title( "Impact Parameter" )]
	public string ImpactAnimationParameter { get; set; } = "b_attack";

	[Property, Group( "Audio" )]
	public SoundEvent WindupSound { get; set; }

	[Property, Group( "Audio" )]
	public SoundEvent ImpactSound { get; set; }

	[Property, Group( "Audio" ), Title( "Owner Cooldown Ready Sound" )]
	public SoundEvent CooldownReadySound { get; set; }

	[Property, Group( "Effects" ), Title( "Optional Windup Effect" )]
	public PrefabFile WindupEffectPrefab { get; set; }

	[Property, Group( "Effects" ), Title( "Windup Effect Scale" )]
	public float WindupEffectScale { get; set; } = 0.5f;

	[Property, Group( "Effects" ), Title( "Impact Dust/Debris Effect" )]
	public PrefabFile ImpactEffectPrefab { get; set; }

	[Property, Group( "Effects" ), Title( "Impact Effect Scale" )]
	public float ImpactEffectScale { get; set; } = 2.0f;

	[Property, Group( "Camera Shake" ), Title( "Maximum Amplitude" )]
	public float ShakeAmplitude { get; set; } = 8.0f;

	[Property, Group( "Camera Shake" )]
	public float ShakeFrequency { get; set; } = 32.0f;

	[Property, Group( "Camera Shake" )]
	public float ShakeDuration { get; set; } = 0.35f;

	[Property, Group( "Owner Feedback" ), Title( "Ready HUD Duration" )]
	public float ReadyHudDuration { get; set; } = 1.0f;

	public bool HasCooldownHud { get; private set; }
	public bool HasReadyHud =>
		hasReadyHud && timeSinceReadyHud < ReadyHudDuration;
	public float CooldownRemaining => HasCooldownHud
		? System.MathF.Max( 0.0f, cooldownEndTime - Time.Now )
		: 0.0f;

	private LargeLadGroundSlam subscribedSlam;
	private LargeLadPlayer cachedPlayer;
	private SoundHandle windupSoundHandle;
	private GameObject activeWindupEffect;
	private GameObject activeImpactEffect;
	private CameraEffectSystem.BaseEffect activeCameraShake;
	private int activeWindupSequence;
	private int lastWindupSequence;
	private int lastImpactSequence;
	private int lastCameraSequence;
	private int lastCancelledSequence;
	private int cooldownSequence;
	private float cooldownEndTime;
	private bool hasReadyHud;
	private TimeSince timeSinceReadyHud;

	protected override void OnAwake()
	{
		ResolveReferences();
		Subscribe();
	}

	protected override void OnEnabled()
	{
		base.OnEnabled();
		ResolveReferences();
		Subscribe();
	}

	protected override void OnStart()
	{
		ResolveReferences();
		Subscribe();
	}

	protected override void OnUpdate()
	{
		ResolveReferences();
		Subscribe();

		if ( HasCooldownHud && CooldownRemaining <= 0.0f )
			HasCooldownHud = false;

		if ( hasReadyHud && !HasReadyHud )
			hasReadyHud = false;

		if ( HasActivePresentation() && !CanRetainPresentation() )
			CancelActivePresentation();
	}

	protected override void OnDisabled()
	{
		Unsubscribe();
		CancelActivePresentation();
		base.OnDisabled();
	}

	protected override void OnDestroy()
	{
		Unsubscribe();
		CancelActivePresentation();
		base.OnDestroy();
	}

	protected override void OnValidate()
	{
		ValidateNonNegative( nameof( WindupEffectScale ), WindupEffectScale );
		ValidateNonNegative( nameof( ImpactEffectScale ), ImpactEffectScale );
		ValidateNonNegative( nameof( ShakeAmplitude ), ShakeAmplitude );
		ValidateNonNegative( nameof( ShakeFrequency ), ShakeFrequency );
		ValidateNonNegative( nameof( ShakeDuration ), ShakeDuration );
		ValidateNonNegative( nameof( ReadyHudDuration ), ReadyHudDuration );
	}

	private void OnPresentation( LargeLadGroundSlamPresentation presentation )
	{
		switch ( presentation.Phase )
		{
			case LargeLadGroundSlamPresentationPhase.Windup:
				PresentWindup( presentation );
				break;
			case LargeLadGroundSlamPresentationPhase.Impact:
				PresentImpact( presentation );
				break;
			case LargeLadGroundSlamPresentationPhase.CameraAudioFeedback:
				PresentCameraShake( presentation );
				break;
			case LargeLadGroundSlamPresentationPhase.CooldownStarted:
				PresentCooldownStarted( presentation );
				break;
			case LargeLadGroundSlamPresentationPhase.CooldownReady:
				PresentCooldownReady( presentation );
				break;
			case LargeLadGroundSlamPresentationPhase.Cancelled:
				PresentCancellation( presentation.Sequence );
				break;
		}
	}

	private void PresentWindup( LargeLadGroundSlamPresentation presentation )
	{
		if ( presentation.Sequence <= lastCancelledSequence ||
			presentation.Sequence <= lastWindupSequence )
		{
			return;
		}

		StopWindupPresentation();
		lastWindupSequence = presentation.Sequence;
		activeWindupSequence = presentation.Sequence;
		TriggerAnimation( WindupAnimationParameter );

		if ( WindupSound is not null )
		{
			windupSoundHandle = Sound.Play(
				WindupSound,
				GetGroundEffectPosition( presentation.Origin ) );
		}

		activeWindupEffect = SpawnLocalEffect(
			WindupEffectPrefab,
			GetGroundEffectPosition( presentation.Origin ),
			WindupEffectScale );
	}

	private void PresentImpact( LargeLadGroundSlamPresentation presentation )
	{
		if ( presentation.Sequence <= lastCancelledSequence ||
			presentation.Sequence != activeWindupSequence ||
			presentation.Sequence <= lastImpactSequence )
		{
			return;
		}

		lastImpactSequence = presentation.Sequence;
		StopWindupPresentation();
		TriggerAnimation( ImpactAnimationParameter );
		var effectPosition = GetGroundEffectPosition( presentation.Origin );

		if ( ImpactSound is not null )
			Sound.Play( ImpactSound, effectPosition );

		DestroyEffect( ref activeImpactEffect );
		activeImpactEffect = SpawnLocalEffect(
			ImpactEffectPrefab,
			effectPosition,
			ImpactEffectScale );
	}

	private void PresentCameraShake(
		LargeLadGroundSlamPresentation presentation )
	{
		if ( presentation.Sequence != lastImpactSequence ||
			presentation.Sequence <= lastCameraSequence )
		{
			return;
		}

		lastCameraSequence = presentation.Sequence;
		var camera = Scene.Camera;

		if ( camera is null )
			return;

		var distance = camera.GameObject.WorldPosition.Distance(
			presentation.Origin );
		var scale = LargeLadGroundSlamRules.GetFeedbackScale(
			distance,
			presentation.Radius );
		var amplitude = System.MathF.Max( 0.0f, ShakeAmplitude ) *
			System.MathF.Max( 0.0f, presentation.Strength ) * scale;

		if ( amplitude <= 0.0f )
			return;

		activeCameraShake?.Stop();
		activeCameraShake = camera.AddShake(
			amplitude,
			System.MathF.Max( 0.0f, ShakeFrequency ),
			System.MathF.Max( 0.0f, ShakeDuration ) );
	}

	private void PresentCooldownStarted(
		LargeLadGroundSlamPresentation presentation )
	{
		if ( IsProxy ||
			presentation.Sequence <= lastCancelledSequence ||
			presentation.Sequence < cooldownSequence )
		{
			return;
		}

		cooldownSequence = presentation.Sequence;
		cooldownEndTime = Time.Now + presentation.Duration;
		HasCooldownHud = presentation.Duration > 0.0f;
		hasReadyHud = false;
	}

	private void PresentCooldownReady(
		LargeLadGroundSlamPresentation presentation )
	{
		if ( IsProxy ||
			presentation.Sequence != cooldownSequence ||
			presentation.Sequence <= lastCancelledSequence )
		{
			return;
		}

		HasCooldownHud = false;
		hasReadyHud = true;
		timeSinceReadyHud = 0.0f;

		if ( CooldownReadySound is not null )
			Sound.Play( CooldownReadySound, GameObject.WorldPosition );
	}

	private void PresentCancellation( int sequence )
	{
		if ( sequence <= lastCancelledSequence )
			return;

		lastCancelledSequence = sequence;
		ClearPresentationState();
	}

	private void CancelActivePresentation()
	{
		lastCancelledSequence = System.Math.Max(
			lastCancelledSequence,
			System.Math.Max(
				lastImpactSequence,
				System.Math.Max(
					activeWindupSequence,
					cooldownSequence ) ) );
		ClearPresentationState();
	}

	private void StopWindupPresentation()
	{
		if ( windupSoundHandle is not null && windupSoundHandle.IsPlaying )
			windupSoundHandle.Fadeout = 0.05f;

		windupSoundHandle = null;
		DestroyEffect( ref activeWindupEffect );
		activeWindupSequence = 0;
	}

	private void ClearPresentationState()
	{
		StopWindupPresentation();
		DestroyEffect( ref activeImpactEffect );
		activeCameraShake?.Stop();
		activeCameraShake = null;
		HasCooldownHud = false;
		hasReadyHud = false;
		cooldownSequence = 0;
		cooldownEndTime = 0.0f;
	}

	private bool HasActivePresentation()
	{
		return activeWindupSequence > 0 ||
			(activeImpactEffect is not null && activeImpactEffect.IsValid) ||
			(activeCameraShake is not null && !activeCameraShake.IsDone) ||
			HasCooldownHud ||
			hasReadyHud;
	}

	private bool CanRetainPresentation()
	{
		var player = cachedPlayer;
		return player is not null &&
			player.IsValid &&
			player.Enabled &&
			player.Scene == Scene &&
			player.Role == LargeLadRole.LargeLad &&
			player.Health?.IsDead == false &&
			player.Health.CurrentHealth > 0.0f &&
			LargeLadGameManager.FindForScene( Scene )?.Phase ==
				LargeLadRoundPhase.Playing;
	}

	private Vector3 GetGroundEffectPosition( Vector3 presentationOrigin )
	{
		return presentationOrigin - Vector3.Up *
			System.MathF.Max( 0.0f, GroundSlam?.TraceHeight ?? 0.0f );
	}

	private static GameObject SpawnLocalEffect(
		PrefabFile prefab,
		Vector3 position,
		float scale )
	{
		if ( prefab is null || scale <= 0.0f )
			return null;

		var rotation = Rotation.LookAt( Vector3.Up, Vector3.Forward );
		var effect = GameObject.Clone(
			prefab,
			new CloneConfig
			{
				Transform = new Transform(
					position,
					rotation,
					new Vector3( scale ) ),
				StartEnabled = true
			} );

		if ( effect is null || !effect.IsValid )
			return null;

		effect.NetworkMode = NetworkMode.Never;
		return effect;
	}

	private static void DestroyEffect( ref GameObject effect )
	{
		if ( effect is not null && effect.IsValid )
			effect.Destroy();

		effect = null;
	}

	private void TriggerAnimation( string parameter )
	{
		if ( cachedPlayer?.BodyRenderer is null ||
			string.IsNullOrWhiteSpace( parameter ) )
		{
			return;
		}

		cachedPlayer.BodyRenderer.Set( parameter, true );
	}

	private void ResolveReferences()
	{
		if ( GroundSlam is null ||
			!GroundSlam.IsValid ||
			GroundSlam.GameObject != GameObject )
		{
			GroundSlam = Components.Get<LargeLadGroundSlam>();
		}

		if ( cachedPlayer is null ||
			!cachedPlayer.IsValid ||
			cachedPlayer.GameObject != GameObject )
		{
			cachedPlayer = Components.Get<LargeLadPlayer>();
		}
	}

	private void Subscribe()
	{
		if ( subscribedSlam == GroundSlam )
			return;

		Unsubscribe();

		if ( GroundSlam is null || !GroundSlam.IsValid )
			return;

		subscribedSlam = GroundSlam;
		subscribedSlam.Presentation += OnPresentation;
	}

	private void Unsubscribe()
	{
		if ( subscribedSlam is not null && subscribedSlam.IsValid )
			subscribedSlam.Presentation -= OnPresentation;

		subscribedSlam = null;
	}

	private void ValidateNonNegative( string name, float value )
	{
		if ( !float.IsFinite( value ) || value < 0.0f )
		{
			Log.Warning(
				$"{GameObject.Name}: Ground Slam presentation {name} " +
				"cannot be negative." );
		}
	}
}
