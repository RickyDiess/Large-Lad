using Sandbox;
using System.Collections.Generic;

public enum LargeLadEatAttackResult
{
	Miss,
	EatStarted,
	BarricadeHit,
	SmashableHit
}

/// <summary>
/// Large Lad's only primary attack. Owners submit an activation sequence; the
/// host selects a target, owns one short Eat transaction, and commits one
/// lethal Eat damage event only after the transaction remains valid.
/// </summary>
[Description(
	"Large Lad's only primary attack. A valid Skinny Kid takes priority over " +
	"eligible Lad Shortcut or explicitly authored Eat-smashable fallbacks; a " +
	"successful committed execution heals a percentage of missing health." )]
public sealed class LargeLadEatAttack : Component
{
	private const float HostCadenceTolerance = 0.025f;
	private const float ObstructionTolerance = 2.0f;

	[Property, Group( "Timing" )]
	public float Cooldown { get; set; } = 1.0f;

	[Property, Group( "Timing" )]
	public float EatDuration { get; set; } = 0.3f;

	[Property, Group( "Targeting" )]
	public float ForwardOffset { get; set; } = 48.0f;

	[Property, Group( "Targeting" )]
	public float SearchRadius { get; set; } = 50.0f;

	[Property, Group( "Targeting" )]
	public float MinimumFacingDot { get; set; } = 0.1f;

	[Property, Group( "Victim" ), Title( "Movement Multiplier" )]
	public float VictimMovementMultiplier { get; set; } = 0.05f;

	[Property, Group( "Results" ), Title( "Breakable Damage" )]
	[Description(
		"Damage applied only when no valid Skinny Kid is caught and the selected " +
		"fallback is a Lad Shortcut or explicitly authored Eat smashable." )]
	public float BreakableDamage { get; set; } = 100.0f;

	[Property, Group( "Results" ), Title( "Missing-health Heal Fraction" )]
	[Description(
		"Percentage of the Large Lad's currently missing health restored once " +
		"after a committed Eat executes its victim successfully." )]
	public float MissingHealthHealFraction { get; set; } = 0.1f;

	[Property, Group( "Presentation" ), Title( "Attack Animation Parameter" )]
	public string AttackAnimationParameter { get; set; } = "b_attack";

	[Property, Group( "Presentation" ), Title( "Victim Animation Parameter" )]
	public string VictimAnimationParameter { get; set; } = "b_flinch";

	[Property, Group( "Presentation" )]
	public SoundEvent AttackSound { get; set; }

	[Property, Group( "Presentation" )]
	public SoundEvent VictimScream { get; set; }

	[Property, Group( "Presentation" )]
	public List<SoundEvent> FleshSounds { get; set; } = new();

	[Property, Group( "Presentation" )]
	public PrefabFile BloodEffectPrefab { get; set; }

	[Property, Group( "Presentation" ), Title( "Default Blood Effect Path" )]
	public string DefaultBloodEffectPath { get; set; } =
		"prefabs/surface/flesh_bullet.prefab";

	[Property, Group( "Presentation" ), Title( "Blood Decal Trace Distance" )]
	public float BloodDecalTraceDistance { get; set; } = 96.0f;

	[Property, Group( "Presentation" ), Title( "Blood Splatter Decals" )]
	public List<DecalDefinition> BloodSplatterDecals { get; set; } = new();

	// These generated textures are referenced indirectly by the stock decal
	// definitions. Keeping explicit resource references here ensures a joining
	// client receives them instead of resolving them from the host's local cache.
	[Property, Hide]
	public List<Texture> BloodSplatterTextureDependencies { get; set; } = new();

	[Property, Group( "Presentation" ), Title( "Blood Decal Scale" )]
	public float BloodDecalScale { get; set; } = 2.0f;

	[Property, Group( "Presentation" ), Title( "Blood/Flesh Interval" )]
	public float PresentationInterval { get; set; } = 0.1f;

	[Property, Group( "Presentation" )]
	public SoundEvent BreakableHitSound { get; set; }

	[Property, Group( "Presentation" )]
	public PrefabFile BreakableHitEffectPrefab { get; set; }

	private readonly LargeLadEatState eatState = new();
	private readonly List<GameObject> localRoundBloodDecals = new();
	private TimeSince timeSinceLocalActivation;
	private int nextOwnerActivationSequence;
	private int lastHostActivationSequence;
	private bool hasHostActivationSchedule;
	private float nextHostActivationTime;
	private LargeLadPlayer activeAttacker;
	private LargeLadPlayer activeVictim;
	private LargeLadPlayer cachedAttacker;
	private PlayerController cachedController;
	private LargeLadGameManager cachedGameManager;

	public bool IsEating => eatState.IsActive;
	public LargeLadEatAttackResult LastAttackResult { get; private set; } =
		LargeLadEatAttackResult.Miss;

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
		ClearLocalBloodDecalsOutsidePlayingRound();

		if ( Networking.IsHost )
			TickAuthoritativeEat();

		if ( IsProxy ||
			LargeLadLocalUiInput.ShouldSuppressGameplayInput ||
			!Input.Down( "Attack1" ) )
			return;

		var attacker = cachedAttacker;

		if ( !CanActivate( attacker, cachedController ) ||
			timeSinceLocalActivation < System.MathF.Max( 0.01f, Cooldown ) )
		{
			return;
		}

		timeSinceLocalActivation = 0.0f;
		nextOwnerActivationSequence++;
		TriggerAttackAnimation( attacker );
		RequestEatActivation( nextOwnerActivationSequence );
	}

	protected override void OnDisabled()
	{
		CancelAuthoritativeEat();
		ClearLocalBloodDecals();
		base.OnDisabled();
	}

	protected override void OnDestroy()
	{
		CancelAuthoritativeEat();
		ClearLocalBloodDecals();
		base.OnDestroy();
	}

	protected override void OnValidate()
	{
		ValidatePositive( nameof( Cooldown ), Cooldown );
		ValidatePositive( nameof( EatDuration ), EatDuration );
		ValidatePositive( nameof( SearchRadius ), SearchRadius );
		ValidatePositive( nameof( BreakableDamage ), BreakableDamage );
		ValidatePositive( nameof( PresentationInterval ), PresentationInterval );
		ValidatePositive(
			nameof( BloodDecalTraceDistance ),
			BloodDecalTraceDistance );
		ValidatePositive( nameof( BloodDecalScale ), BloodDecalScale );

		if ( ForwardOffset < 0.0f )
			Log.Warning( $"{GameObject.Name}: Eat forward offset cannot be negative." );

		if ( MinimumFacingDot < -1.0f || MinimumFacingDot > 1.0f )
			Log.Warning( $"{GameObject.Name}: Eat facing dot must be -1 to 1." );

		if ( VictimMovementMultiplier < 0.0f ||
			VictimMovementMultiplier > 1.0f )
		{
			Log.Warning(
				$"{GameObject.Name}: Eat victim movement multiplier must be 0 to 1." );
		}

		if ( MissingHealthHealFraction < 0.0f ||
			MissingHealthHealFraction > 1.0f )
		{
			Log.Warning(
				$"{GameObject.Name}: Eat healing fraction must be 0 to 1." );
		}
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	private void RequestEatActivation( int ownerActivationSequence )
	{
		if ( !Networking.IsHost ||
			ownerActivationSequence <= lastHostActivationSequence )
		{
			return;
		}

		// Consume the sequence before validation so malformed or stale requests
		// cannot be replayed after state changes.
		lastHostActivationSequence = ownerActivationSequence;
		var attacker = cachedAttacker;
		var controller = cachedController;

		if ( !CanActivate( attacker, controller ) )
			return;

		var hostNow = Time.Now;

		if ( hasHostActivationSchedule &&
			hostNow + HostCadenceTolerance < nextHostActivationTime )
		{
			return;
		}

		CommitHostCadence( hostNow );
		BroadcastAttackAnimation();

		var target = FindAuthoritativeTarget( attacker, controller );

		if ( target?.Player is not null )
		{
			LastAttackResult = BeginEat(
				attacker,
				target.Player,
				ownerActivationSequence,
				hostNow )
				? LargeLadEatAttackResult.EatStarted
				: LargeLadEatAttackResult.Miss;
			return;
		}

		if ( target?.Barricade is not null )
		{
			LastAttackResult = DamageBreakable( target.Barricade )
				? LargeLadEatAttackResult.BarricadeHit
				: LargeLadEatAttackResult.Miss;
			return;
		}

		if ( target?.EatSmashable is not null )
		{
			LastAttackResult = DamageBreakable( target.EatSmashable )
				? LargeLadEatAttackResult.SmashableHit
				: LargeLadEatAttackResult.Miss;
			return;
		}

		LastAttackResult = LargeLadEatAttackResult.Miss;
	}

	private bool BeginEat(
		LargeLadPlayer attacker,
		LargeLadPlayer victim,
		int sequence,
		float now )
	{
		if ( !eatState.TryBegin(
			sequence,
			now,
			EatDuration,
			PresentationInterval ) )
		{
			return false;
		}

		activeAttacker = attacker;
		activeVictim = victim;

		if ( !attacker.TryBeginEatParticipation(
			this,
			LargeLadEatParticipation.Attacker ) ||
			!victim.TryBeginEatParticipation(
				this,
				LargeLadEatParticipation.Victim,
				VictimMovementMultiplier ) )
		{
			CleanupEat();
			return false;
		}

		var blood = ResolveBloodPresentation(
			attacker,
			victim,
			presentationIndex: 0 );
		ReceiveEatStarted( victim.GameObject, blood );
		BroadcastEatStarted(
			victim.GameObject,
			blood.SprayPosition,
			blood.SprayNormal,
			blood.DecalPosition,
			blood.DecalNormal,
			blood.HasDecal,
			blood.DecalIndex );
		return true;
	}

	private void TickAuthoritativeEat()
	{
		if ( !eatState.IsActive )
			return;

		var now = Time.Now;
		var transition = eatState.GetTransition(
			now,
			AreActiveParticipantsValid() );

		if ( transition == LargeLadEatStateTransition.Cancel )
		{
			CleanupEat();
			return;
		}

		if ( transition == LargeLadEatStateTransition.Complete )
		{
			CompleteEat();
			return;
		}

		if ( eatState.TryTakePresentationPulse(
			now,
			PresentationInterval,
			out var pulseIndex ) )
		{
			var presentationIndex = pulseIndex + 1;
			var blood = ResolveBloodPresentation(
				activeAttacker,
				activeVictim,
				presentationIndex );
			ReceiveEatPulse( blood, presentationIndex );
			BroadcastEatPulse(
				blood.SprayPosition,
				blood.SprayNormal,
				blood.DecalPosition,
				blood.DecalNormal,
				blood.HasDecal,
				blood.DecalIndex,
				presentationIndex );
		}
	}

	private void CompleteEat()
	{
		if ( !AreActiveParticipantsValid() ||
			!eatState.TryCommitExecution() )
		{
			CleanupEat();
			return;
		}

		// Capture both references because the normal lethal pipeline may end the
		// round synchronously and invoke this component's lifecycle cleanup.
		var attacker = activeAttacker;
		var victim = activeVictim;
		var killed = victim?.Health?.TryExecuteEat(
			this,
			attacker,
			out _ ) == true;

		if ( killed )
		{
			attacker?.Health?.TryHealMissingHealth(
				MissingHealthHealFraction,
				out _ );
		}

		CleanupEat();
	}

	internal void CancelFromParticipantLifecycle( LargeLadPlayer participant )
	{
		if ( !Networking.IsHost ||
			!eatState.IsActive ||
			(participant != activeAttacker && participant != activeVictim) )
		{
			return;
		}

		CleanupEat();
	}

	private void CancelAuthoritativeEat()
	{
		if ( Networking.IsHost )
			CleanupEat();
	}

	private void CleanupEat()
	{
		if ( !eatState.TryCommitCleanup() )
			return;

		var attacker = activeAttacker;
		var victim = activeVictim;
		activeAttacker = null;
		activeVictim = null;
		victim?.ReleaseEatParticipation( this );
		attacker?.ReleaseEatParticipation( this );
	}

	private bool AreActiveParticipantsValid()
	{
		var manager = GetGameManager();
		var attacker = activeAttacker;
		var victim = activeVictim;

		return manager?.Phase == LargeLadRoundPhase.Playing &&
			attacker is not null &&
			attacker.IsValid &&
			attacker.Enabled &&
			attacker.Scene == Scene &&
			attacker.Role == LargeLadRole.LargeLad &&
			attacker.Health?.IsDead == false &&
			attacker.Health.CurrentHealth > 0.0f &&
			attacker.IsEatParticipationOwnedBy(
				this,
				LargeLadEatParticipation.Attacker ) &&
			victim is not null &&
			victim.IsValid &&
			victim.Enabled &&
			victim.Scene == Scene &&
			victim.Role == LargeLadRole.SkinnyKid &&
			victim.Health?.IsDead == false &&
			victim.Health.CurrentHealth > 0.0f &&
			victim.IsEatParticipationOwnedBy(
				this,
				LargeLadEatParticipation.Victim );
	}

	private RuntimeEatTarget FindAuthoritativeTarget(
		LargeLadPlayer attacker,
		PlayerController controller )
	{
		var manager = GetGameManager();

		if ( manager is null )
			return null;

		var attackerPosition = attacker.GameObject.WorldPosition;
		var forward = controller.EyeTransform.Rotation.Forward;
		var searchCenter = attackerPosition +
			forward.Normal * System.MathF.Max( 0.0f, ForwardOffset );
		var traceStart = controller.EyePosition;
		var candidates = new List<LargeLadEatTargetCandidate>();
		var runtimeTargets = new List<RuntimeEatTarget>();
		var nextCandidateId = 1;

		foreach ( var player in manager.ActivePlayers )
		{
			if ( player == attacker ||
				player?.Role != LargeLadRole.SkinnyKid )
			{
				continue;
			}

			var eligible = player.IsValid &&
				player.Enabled &&
				player.Scene == Scene &&
				player.Health?.IsDead == false &&
				player.Health.CurrentHealth > 0.0f;
			var obstructed = eligible && IsPlayerObstructed(
				attacker,
				player,
				traceStart );
			var candidate = new LargeLadEatTargetCandidate(
				nextCandidateId++,
				LargeLadEatTargetKind.SkinnyKid,
				player.GameObject.WorldPosition,
				eligible,
				obstructed,
				player.IsEatBusy );
			candidates.Add( candidate );
			runtimeTargets.Add( RuntimeEatTarget.ForPlayer(
				candidate.Id,
				player ) );
		}

		foreach ( var barricade in manager.ActiveBarricades )
		{
			if ( barricade is null ||
				barricade.Mode != LargeLadBarricadeMode.LadShortcut )
			{
				continue;
			}

			var eligible = barricade.IsValid &&
				barricade.Enabled &&
				barricade.Scene == Scene &&
				!barricade.IsDestroyed &&
				LargeLadGameplayRules.CanDamageBarricade(
					barricade.Mode,
					LargeLadRole.LargeLad,
					LargeLadDamageType.Melee );
			var position = barricade.GetClosestWorldPoint( searchCenter );
			var obstructed = eligible && IsBarricadeObstructed(
				attacker,
				barricade,
				traceStart,
				position );
			var candidate = new LargeLadEatTargetCandidate(
				nextCandidateId++,
				LargeLadEatTargetKind.LargeLadBarricade,
				position,
				eligible,
				obstructed );
			candidates.Add( candidate );
			runtimeTargets.Add( RuntimeEatTarget.ForBarricade(
				candidate.Id,
				barricade,
				position ) );
		}

		foreach ( var smashable in manager.ActiveEatSmashables )
		{
			if ( smashable is null )
				continue;

			var eligible = smashable.IsValid &&
				smashable.Enabled &&
				smashable.Scene == Scene &&
				!smashable.IsDestroyed;
			var position = smashable.GetClosestWorldPoint( searchCenter );
			var obstructed = eligible && IsSmashableObstructed(
				attacker,
				smashable,
				traceStart,
				position );
			var candidate = new LargeLadEatTargetCandidate(
				nextCandidateId++,
				LargeLadEatTargetKind.EatSmashable,
				position,
				eligible,
				obstructed );
			candidates.Add( candidate );
			runtimeTargets.Add( RuntimeEatTarget.ForSmashable(
				candidate.Id,
				smashable,
				position ) );
		}

		if ( !LargeLadEatRules.TrySelectTarget(
			attackerPosition,
			forward,
			ForwardOffset,
			SearchRadius,
			MinimumFacingDot,
			candidates,
			out var selected ) )
		{
			return null;
		}

		return runtimeTargets.Find( target => target.Id == selected.Id );
	}

	private bool IsPlayerObstructed(
		LargeLadPlayer attacker,
		LargeLadPlayer target,
		Vector3 traceStart )
	{
		var targetPosition = GetVictimPresentationPosition( target );
		var expectedDistance = traceStart.Distance( targetPosition );
		var trace = Scene.Trace
			.Ray( traceStart, targetPosition )
			.WithoutTags( LargeLadGameplayRules.PlayerBodyTag )
			.IgnoreGameObjectHierarchy( attacker.GameObject )
			.Run();

		return trace.Hit &&
			trace.Distance + ObstructionTolerance < expectedDistance;
	}

	private bool IsBarricadeObstructed(
		LargeLadPlayer attacker,
		LargeLadBarricade target,
		Vector3 traceStart,
		Vector3 targetPosition )
	{
		var trace = RunBreakableTrace( attacker, traceStart, targetPosition );
		return LargeLadBarricade.FindFor( trace.GameObject ) != target;
	}

	private bool IsSmashableObstructed(
		LargeLadPlayer attacker,
		LargeLadEatSmashable target,
		Vector3 traceStart,
		Vector3 targetPosition )
	{
		var trace = RunBreakableTrace( attacker, traceStart, targetPosition );
		return LargeLadEatSmashable.FindFor( trace.GameObject ) != target;
	}

	private SceneTraceResult RunBreakableTrace(
		LargeLadPlayer attacker,
		Vector3 traceStart,
		Vector3 targetPosition )
	{
		var towardTarget = targetPosition - traceStart;
		var traceEnd = towardTarget.LengthSquared > 0.001f
			? targetPosition + towardTarget.Normal * 4.0f
			: targetPosition;
		return Scene.Trace
			.Ray( traceStart, traceEnd )
			.WithoutTags( LargeLadGameplayRules.PlayerBodyTag )
			.IgnoreGameObjectHierarchy( attacker.GameObject )
			.Run();
	}

	private bool DamageBreakable( ILargeLadDamageable target )
	{
		var damage = new LargeLadDamageContext
		{
			Attacker = GameObject,
			AttackerRole = LargeLadRole.LargeLad,
			SourceWeapon = LargeLadWeaponId.Melee,
			DamageType = LargeLadDamageType.Melee,
			BaseDamage = System.MathF.Max( 0.0f, BreakableDamage )
		};

		if ( !target.TryApplyDamage( damage, out var applied ) ||
			applied.AppliedDamage <= 0.0f )
		{
			return false;
		}

		var position = target switch
		{
			LargeLadBarricade barricade =>
				barricade.GetClosestWorldPoint( GameObject.WorldPosition ),
			LargeLadEatSmashable smashable =>
				smashable.GetClosestWorldPoint( GameObject.WorldPosition ),
			_ => GameObject.WorldPosition
		};
		ReceiveBreakableHit( position );
		BroadcastBreakableHit( position );
		return true;
	}

	private bool CanActivate(
		LargeLadPlayer attacker,
		PlayerController controller )
	{
		return attacker is not null &&
			controller is not null &&
			attacker.Role == LargeLadRole.LargeLad &&
			attacker.Health?.IsDead == false &&
			attacker.Health.CurrentHealth > 0.0f &&
			!attacker.MovementLocked &&
			!attacker.IsEatBusy &&
			!attacker.IsGroundSlamBusy &&
			!eatState.IsActive &&
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

	private void TriggerAttackAnimation( LargeLadPlayer attacker )
	{
		if ( attacker?.BodyRenderer is null ||
			string.IsNullOrWhiteSpace( AttackAnimationParameter ) )
		{
			return;
		}

		attacker.BodyRenderer.Set( AttackAnimationParameter, true );
	}

	[Rpc.Broadcast]
	private void BroadcastAttackAnimation()
	{
		// The owner predicted it at input time. Proxies still need the host's
		// accepted activation for third-person presentation.
		if ( !IsProxy )
			return;

		TriggerAttackAnimation( cachedAttacker );
	}

	private void ReceiveEatStarted(
		GameObject victimObject,
		BloodPresentation blood )
	{
		if ( AttackSound is not null )
			Sound.Play( AttackSound, GameObject.WorldPosition );

		if ( VictimScream is not null )
			Sound.Play( VictimScream, blood.SprayPosition );

		var victim = victimObject?.Components.Get<LargeLadPlayer>();

		if ( victim?.BodyRenderer is not null &&
			!string.IsNullOrWhiteSpace( VictimAnimationParameter ) )
		{
			victim.BodyRenderer.Set( VictimAnimationParameter, true );
		}

		ReceiveEatPulse( blood, 0 );
	}

	[Rpc.Broadcast]
	private void BroadcastEatStarted(
		GameObject victimObject,
		Vector3 sprayPosition,
		Vector3 sprayNormal,
		Vector3 decalPosition,
		Vector3 decalNormal,
		bool hasDecal,
		int decalIndex )
	{
		if ( Networking.IsHost )
			return;

		ReceiveEatStarted(
			victimObject,
			new BloodPresentation(
				sprayPosition,
				sprayNormal,
				decalPosition,
				decalNormal,
				hasDecal,
				decalIndex ) );
	}

	private void ReceiveEatPulse(
		BloodPresentation blood,
		int pulseIndex )
	{
		if ( FleshSounds is not null && FleshSounds.Count > 0 )
		{
			var sound = FleshSounds[
				System.Math.Abs( pulseIndex ) % FleshSounds.Count];

			if ( sound is not null )
				Sound.Play( sound, blood.SprayPosition );
		}

		SpawnLocalBloodSpray(
			blood.SprayPosition,
			blood.SprayNormal );

		if ( blood.HasDecal )
		{
			SpawnLocalBloodDecal(
				blood.DecalPosition,
				blood.DecalNormal,
				blood.DecalIndex );
		}
	}

	[Rpc.Broadcast]
	private void BroadcastEatPulse(
		Vector3 sprayPosition,
		Vector3 sprayNormal,
		Vector3 decalPosition,
		Vector3 decalNormal,
		bool hasDecal,
		int decalIndex,
		int pulseIndex )
	{
		if ( Networking.IsHost )
			return;

		ReceiveEatPulse(
			new BloodPresentation(
				sprayPosition,
				sprayNormal,
				decalPosition,
				decalNormal,
				hasDecal,
				decalIndex ),
			pulseIndex );
	}

	private void ReceiveBreakableHit( Vector3 position )
	{
		if ( BreakableHitSound is not null )
			Sound.Play( BreakableHitSound, position );

		SpawnLocalEffect(
			BreakableHitEffectPrefab,
			fallbackPath: null,
			position,
			Vector3.Up );
	}

	[Rpc.Broadcast]
	private void BroadcastBreakableHit( Vector3 position )
	{
		if ( Networking.IsHost )
			return;

		ReceiveBreakableHit( position );
	}

	private void SpawnLocalBloodSpray(
		Vector3 position,
		Vector3 normal )
	{
		var effect = SpawnLocalEffect(
			BloodEffectPrefab,
			DefaultBloodEffectPath,
			position,
			normal );

		// The stock flesh impact's Decal child needs a real surface. The spray
		// instance lives on the player hit point, so keep its visible particles
		// and suppress only the unreliable mid-air projector.
		if ( effect is null || BloodEffectPrefab is not null )
			return;

		foreach ( var child in effect.Children )
		{
			if ( child.Components.Get<Decal>() is not null )
				child.Enabled = false;
		}
	}

	private void SpawnLocalBloodDecal(
		Vector3 position,
		Vector3 normal,
		int decalIndex )
	{
		var direction = normal.LengthSquared > 0.0001f
			? normal.Normal
			: Vector3.Up;
		var surfaceTrace = Scene.Trace
			.Ray(
				position + direction * 4.0f,
				position - direction * 12.0f )
			.WithoutTags( LargeLadGameplayRules.PlayerBodyTag )
			.Run();

		if ( surfaceTrace.Hit )
		{
			position = surfaceTrace.HitPosition;
			normal = surfaceTrace.Normal;
			direction = normal.LengthSquared > 0.0001f
				? normal.Normal
				: direction;
		}

		var decalDefinition = GetBloodSplatterDecal( decalIndex );

		if ( decalDefinition is null )
			return;

		var rotationUp = System.MathF.Abs(
			Vector3.Dot( direction, Vector3.Up ) ) > 0.99f
			? Vector3.Forward
			: Vector3.Up;
		var decalObject = new GameObject( false, "Eat Blood Splatter" )
		{
			NetworkMode = NetworkMode.Never,
			WorldPosition = position + direction * 0.2f,
			WorldRotation = Rotation.LookAt( -direction, rotationUp )
		};
		var decal = decalObject.Components.Create<Decal>();
		decal.Decals = [decalDefinition];
		decal.Depth = 8.0f;
		decal.LifeTime = 0.0f;
		decal.Scale = System.MathF.Max( 0.01f, BloodDecalScale );
		decal.Transient = false;
		decal.Rotation = (decalIndex * 97) % 360;
		decalObject.Enabled = true;
		localRoundBloodDecals.Add( decalObject );
	}

	private void ClearLocalBloodDecalsOutsidePlayingRound()
	{
		if ( localRoundBloodDecals.Count == 0 )
			return;

		var manager = GetGameManager();

		if ( manager is not null &&
			manager.Phase != LargeLadRoundPhase.Playing )
		{
			ClearLocalBloodDecals();
		}
	}

	private void ClearLocalBloodDecals()
	{
		foreach ( var decalObject in localRoundBloodDecals )
		{
			if ( decalObject is not null && decalObject.IsValid )
				decalObject.Destroy();
		}

		localRoundBloodDecals.Clear();
	}

	private DecalDefinition GetBloodSplatterDecal( int decalIndex )
	{
		if ( BloodSplatterDecals is null ||
			BloodSplatterDecals.Count == 0 )
		{
			return null;
		}

		var startIndex = System.Math.Abs( decalIndex ) %
			BloodSplatterDecals.Count;

		for ( var offset = 0;
			offset < BloodSplatterDecals.Count;
			offset++ )
		{
			var candidate = BloodSplatterDecals[
				(startIndex + offset) % BloodSplatterDecals.Count];

			if ( candidate is not null )
				return candidate;
		}

		return null;
	}

	private static GameObject SpawnLocalEffect(
		PrefabFile effectPrefab,
		string fallbackPath,
		Vector3 position,
		Vector3 normal )
	{
		if ( effectPrefab is null && string.IsNullOrWhiteSpace( fallbackPath ) )
			return null;

		var direction = normal.LengthSquared > 0.0001f
			? normal.Normal
			: Vector3.Forward;
		var rotationUp = System.MathF.Abs(
			Vector3.Dot( direction, Vector3.Up ) ) > 0.99f
			? Vector3.Forward
			: Vector3.Up;
		var rotation = Rotation.LookAt( direction, rotationUp );
		var config = new CloneConfig
		{
			Transform = new Transform( position, rotation ),
			StartEnabled = true
		};
		var effect = effectPrefab is not null
			? GameObject.Clone( effectPrefab, config )
			: GameObject.Clone( fallbackPath, config );

		if ( effect is null || !effect.IsValid )
			return null;

		effect.NetworkMode = NetworkMode.Never;
		return effect;
	}

	private BloodPresentation ResolveBloodPresentation(
		LargeLadPlayer attacker,
		LargeLadPlayer victim,
		int presentationIndex )
	{
		var targetPosition = GetVictimPresentationPosition( victim );
		var controller = attacker?.GameObject?.Components.Get<PlayerController>();
		var traceStart = controller?.EyePosition ??
			attacker?.GameObject?.WorldPosition ?? targetPosition;
		var toVictim = targetPosition - traceStart;
		var travelDirection = toVictim.LengthSquared > 0.0001f
			? toVictim.Normal
			: attacker?.GameObject?.WorldRotation.Forward ?? Vector3.Forward;
		var sprayPosition = targetPosition;
		var sprayNormal = -travelDirection;
		var victimTrace = Scene.Trace
			.Ray( traceStart, targetPosition + travelDirection * 8.0f )
			.UseHitboxes( true )
			.IgnoreGameObjectHierarchy( attacker?.GameObject )
			.Run();
		var hitPlayer = victimTrace.GameObject?.Components.Get<LargeLadPlayer>(
			FindMode.EverythingInSelfAndAncestors );

		if ( victimTrace.Hit && hitPlayer == victim )
		{
			sprayPosition = victimTrace.HitPosition;
			sprayNormal = victimTrace.Normal;
		}

		var distance = System.MathF.Max( 1.0f, BloodDecalTraceDistance );
		var decalStart = sprayPosition + travelDirection * 4.0f;
		var decalTrace = Scene.Trace
			.Ray( decalStart, decalStart + travelDirection * distance )
			.WithoutTags( LargeLadGameplayRules.PlayerBodyTag )
			.IgnoreGameObjectHierarchy( attacker?.GameObject )
			.IgnoreGameObjectHierarchy( victim?.GameObject )
			.Run();

		if ( !decalTrace.Hit )
		{
			var floorStart = targetPosition + Vector3.Up * 4.0f;
			decalTrace = Scene.Trace
				.Ray( floorStart, floorStart - Vector3.Up * distance )
				.WithoutTags( LargeLadGameplayRules.PlayerBodyTag )
				.IgnoreGameObjectHierarchy( attacker?.GameObject )
				.IgnoreGameObjectHierarchy( victim?.GameObject )
				.Run();
		}

		return new BloodPresentation(
			sprayPosition,
			sprayNormal,
			decalTrace.HitPosition,
			decalTrace.Normal,
			decalTrace.Hit,
			eatState.Sequence + presentationIndex );
	}

	private static Vector3 GetVictimPresentationPosition(
		LargeLadPlayer victim )
	{
		return victim?.GameObject is not null
			? victim.GameObject.WorldPosition + Vector3.Up * 40.0f
			: Vector3.Zero;
	}

	private LargeLadGameManager GetGameManager()
	{
		if ( cachedGameManager is not null &&
			cachedGameManager.IsValid &&
			cachedGameManager.Enabled &&
			cachedGameManager.Scene == Scene &&
			cachedGameManager.HasSceneGameplayOwnership )
		{
			return cachedGameManager;
		}

		cachedGameManager = LargeLadGameManager.FindForScene( Scene );
		return cachedGameManager;
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

		GetGameManager();
	}

	private void ValidatePositive( string propertyName, float value )
	{
		if ( !float.IsFinite( value ) || value <= 0.0f )
		{
			Log.Warning(
				$"{GameObject.Name}: Eat {propertyName} must be finite and positive." );
		}
	}

	private sealed class RuntimeEatTarget
	{
		public int Id { get; private init; }
		public LargeLadPlayer Player { get; private init; }
		public LargeLadBarricade Barricade { get; private init; }
		public LargeLadEatSmashable EatSmashable { get; private init; }
		public Vector3 Position { get; private init; }

		public static RuntimeEatTarget ForPlayer(
			int id,
			LargeLadPlayer player )
		{
			return new RuntimeEatTarget { Id = id, Player = player };
		}

		public static RuntimeEatTarget ForBarricade(
			int id,
			LargeLadBarricade barricade,
			Vector3 position )
		{
			return new RuntimeEatTarget
			{
				Id = id,
				Barricade = barricade,
				Position = position
			};
		}

		public static RuntimeEatTarget ForSmashable(
			int id,
			LargeLadEatSmashable smashable,
			Vector3 position )
		{
			return new RuntimeEatTarget
			{
				Id = id,
				EatSmashable = smashable,
				Position = position
			};
		}
	}

	private readonly struct BloodPresentation
	{
		public BloodPresentation(
			Vector3 sprayPosition,
			Vector3 sprayNormal,
			Vector3 decalPosition,
			Vector3 decalNormal,
			bool hasDecal,
			int decalIndex )
		{
			SprayPosition = sprayPosition;
			SprayNormal = sprayNormal;
			DecalPosition = decalPosition;
			DecalNormal = decalNormal;
			HasDecal = hasDecal;
			DecalIndex = decalIndex;
		}

		public Vector3 SprayPosition { get; }
		public Vector3 SprayNormal { get; }
		public Vector3 DecalPosition { get; }
		public Vector3 DecalNormal { get; }
		public bool HasDecal { get; }
		public int DecalIndex { get; }
	}
}
