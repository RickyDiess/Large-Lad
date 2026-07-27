using Sandbox;

public enum LargeLadMeleeResult
{
	Miss,
	PlayerHit,
	BarricadeHit
}

/// <summary>
/// Owner-input, host-authoritative melee combat for every playable role.
/// The owner only asks to swing; the host chooses and validates the target.
/// </summary>
public sealed class LargeLadMeleeCombat : Component
{
	private const float HostCadenceTolerance = 0.025f;
	private const float ConfirmedHitmarkerDuration = 0.14f;

	[Property, Group( "Targeting" ), Title( "Swing Trace Radius" )]
	public float SwingTraceRadius { get; set; } = 18.0f;

	[Property, Group( "Targeting" ), Title( "Aim Assist Facing Dot" )]
	public float MinimumFacingDot { get; set; } = 0.55f;

	private TimeSince timeSinceLocalSwing;
	private TimeSince timeSinceConfirmedHit;
	private int nextOwnerSwingSequence;
	private int lastHostSwingSequence;
	private int lastOwnerResultSequence;
	private bool hasHostSwingSchedule;
	private float nextHostSwingTime;
	private bool hasConfirmedHit;
	private LargeLadPlayer cachedAttacker;
	private PlayerController cachedController;
	private LargeLadGameManager cachedGameManager;

	public bool HasConfirmedHitmarker =>
		hasConfirmedHit && timeSinceConfirmedHit < ConfirmedHitmarkerDuration;

	public LargeLadMeleeResult LastAttackResult { get; private set; } =
		LargeLadMeleeResult.Miss;

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
		if ( IsProxy || !Input.Down( "Attack1" ) )
			return;

		var attacker = cachedAttacker;
		var controller = cachedController;

		if ( !CanAttack( attacker, controller ) )
			return;

		if ( !attacker.TryGetRoleProfile( attacker.Role, out var profile ) )
			return;

		if ( timeSinceLocalSwing < profile.MeleeCooldown )
			return;

		timeSinceLocalSwing = 0.0f;
		attacker.MeleePresentation?.TriggerPredictedSwing();
		nextOwnerSwingSequence++;
		RequestMeleeAttack( nextOwnerSwingSequence );
	}

	protected override void OnValidate()
	{
		if ( SwingTraceRadius <= 0.0f )
			Log.Warning( $"{GameObject.Name}: swing trace radius must be positive." );

		if ( MinimumFacingDot < -1.0f || MinimumFacingDot > 1.0f )
			Log.Warning( $"{GameObject.Name}: aim-assist facing dot must be -1 to 1." );
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	private void RequestMeleeAttack( int ownerSwingSequence )
	{
		if ( !Networking.IsHost ||
			ownerSwingSequence <= lastHostSwingSequence )
		{
			return;
		}

		// Consume every new sequence before validating its payload/state so it
		// cannot be replayed later.
		lastHostSwingSequence = ownerSwingSequence;

		var attacker = cachedAttacker;
		var controller = cachedController;

		if ( !CanAttack( attacker, controller ) )
			return;

		if ( !attacker.TryGetRoleProfile( attacker.Role, out var profile ) )
			return;

		var hostNow = Time.Now;

		if ( hasHostSwingSchedule &&
			hostNow + HostCadenceTolerance < nextHostSwingTime )
		{
			return;
		}

		CommitHostCadence( profile.MeleeCooldown, hostNow );
		attacker.MeleePresentation?.BroadcastSwing();

		var target = FindMeleeTarget(
			attacker,
			controller,
			profile.MeleeRange,
			profile.MeleeAimAssist );
		var result = ResolveAttack( attacker, target, profile.MeleeDamage );
		ReceiveMeleeResult( ownerSwingSequence, result );
	}

	private LargeLadMeleeResult ResolveAttack(
		LargeLadPlayer attacker,
		MeleeTarget target,
		float damageAmount )
	{
		if ( target is null )
		{
			Log.Info( $"{attacker.GameObject.Name} swung and missed." );
			return LargeLadMeleeResult.Miss;
		}

		var damage = new LargeLadDamageContext
		{
			Attacker = GameObject,
			AttackerRole = attacker.Role,
			SourceWeapon = LargeLadWeaponId.Melee,
			DamageType = LargeLadDamageType.Melee,
			BaseDamage = damageAmount
		};

		if ( target.Barricade is not null )
		{
			if ( !target.Barricade.TryApplyDamage(
				damage,
				out var structuralDamage ) )
			{
				Log.Info(
					$"{attacker.GameObject.Name} struck " +
					$"{target.Barricade.AuthoredTarget.Name}, but could not damage it." );
				return LargeLadMeleeResult.Miss;
			}

			Log.Info(
				$"{attacker.GameObject.Name} damaged " +
				$"{target.Barricade.AuthoredTarget.Name} for " +
				$"{structuralDamage.AppliedDamage:0.#}." );
			return LargeLadMeleeResult.BarricadeHit;
		}

		var victim = target.Player;

		if ( victim is null )
			return LargeLadMeleeResult.Miss;

		var killed = victim.Health.TryApplyDamage(
			damage,
			out var appliedDamage );

		if ( !killed )
		{
			Log.Info(
				$"{attacker.GameObject.Name} hit {victim.GameObject.Name} for " +
				$"{appliedDamage.AppliedDamage:0.#} damage. " +
				$"{victim.Health.CurrentHealth:0.#}/" +
				$"{victim.Health.MaximumHealth:0.#} health remains." );
			return appliedDamage.AppliedDamage > 0.0f
				? LargeLadMeleeResult.PlayerHit
				: LargeLadMeleeResult.Miss;
		}

		Log.Info(
			$"{attacker.GameObject.Name} killed {victim.GameObject.Name}." );
		return LargeLadMeleeResult.PlayerHit;
	}

	private MeleeTarget FindMeleeTarget(
		LargeLadPlayer attacker,
		PlayerController controller,
		float range,
		bool useAimAssist )
	{
		var start = controller.EyePosition;
		var forward = controller.EyeTransform.Rotation.Forward;
		var trace = Scene.Trace
			.Ray( start, start + forward * range )
			.Radius( System.MathF.Max( 0.0f, SwingTraceRadius ) )
			.UseHitboxes( true )
			.IgnoreGameObjectHierarchy( attacker.GameObject )
			.Run();

		if ( trace.Hit )
		{
			var directPlayer = trace.GameObject?.Components.Get<LargeLadPlayer>(
				FindMode.EverythingInSelfAndAncestors );

			if ( IsValidPlayerTarget( attacker, directPlayer ) )
				return MeleeTarget.ForPlayer( directPlayer );

			var directBarricade =
				LargeLadBarricade.FindFor( trace.GameObject );

			if ( directBarricade is not null && !directBarricade.IsDestroyed )
				return MeleeTarget.ForBarricade( directBarricade );

			// World geometry, friendly players, and unrelated colliders block
			// aim assist from selecting something behind them.
			return null;
		}

		return useAimAssist
			? FindAimAssistedTarget( attacker, controller, range )
			: null;
	}

	private MeleeTarget FindAimAssistedTarget(
		LargeLadPlayer attacker,
		PlayerController controller,
		float range )
	{
		var start = controller.EyePosition;
		var forward = controller.EyeTransform.Rotation.Forward;
		MeleeTarget bestTarget = null;
		var bestScore = float.MaxValue;
		var gameManager = GetGameManager();

		foreach ( var player in
			gameManager?.ActivePlayers ??
			System.Array.Empty<LargeLadPlayer>() )
		{
			if ( !IsValidPlayerTarget( attacker, player ) )
				continue;

			var targetPosition =
				player.GameObject.WorldPosition + Vector3.Up * 48.0f;

			if ( !TryScoreTarget(
				start,
				forward,
				targetPosition,
				range,
				out var score ) ||
				!HasLineOfSightToPlayer( attacker, player, start, targetPosition ) )
			{
				continue;
			}

			if ( score < bestScore )
			{
				bestScore = score;
				bestTarget = MeleeTarget.ForPlayer( player );
			}
		}

		foreach ( var barricade in
			gameManager?.ActiveBarricades ??
			System.Array.Empty<LargeLadBarricade>() )
		{
			if ( barricade is null || barricade.IsDestroyed )
				continue;

			var targetPosition = barricade.GetClosestWorldPoint( start );

			if ( !TryScoreTarget(
				start,
				forward,
				targetPosition,
				range,
				out var score ) ||
				!HasLineOfSightToBarricade(
					attacker,
					barricade,
					start,
					targetPosition ) )
			{
				continue;
			}

			if ( score < bestScore )
			{
				bestScore = score;
				bestTarget = MeleeTarget.ForBarricade( barricade );
			}
		}

		return bestTarget;
	}

	private bool TryScoreTarget(
		Vector3 start,
		Vector3 forward,
		Vector3 targetPosition,
		float range,
		out float score )
	{
		score = float.MaxValue;
		var toTarget = targetPosition - start;
		var distanceSquared = toTarget.LengthSquared;

		if ( distanceSquared > range * range )
			return false;

		if ( distanceSquared <= 0.001f )
		{
			score = 0.0f;
			return true;
		}

		var facing = Vector3.Dot( forward, toTarget.Normal );

		if ( facing < MinimumFacingDot )
			return false;

		var distance = System.MathF.Sqrt( distanceSquared );

		// Centered targets win over off-axis targets, with distance breaking
		// close calls. This keeps assistance predictable in crowded fights.
		score = (1.0f - facing) * range * 2.0f + distance;
		return true;
	}

	private bool HasLineOfSightToPlayer(
		LargeLadPlayer attacker,
		LargeLadPlayer target,
		Vector3 start,
		Vector3 targetPosition )
	{
		var trace = Scene.Trace
			.Ray( start, targetPosition )
			.UseHitboxes( true )
			.IgnoreGameObjectHierarchy( attacker.GameObject )
			.Run();
		var hitPlayer = trace.GameObject?.Components.Get<LargeLadPlayer>(
			FindMode.EverythingInSelfAndAncestors );

		return hitPlayer == target;
	}

	private bool HasLineOfSightToBarricade(
		LargeLadPlayer attacker,
		LargeLadBarricade target,
		Vector3 start,
		Vector3 targetPosition )
	{
		var towardTarget = targetPosition - start;
		var traceEnd = towardTarget.LengthSquared > 0.001f
			? targetPosition + towardTarget.Normal * 4.0f
			: targetPosition;
		var trace = Scene.Trace
			.Ray( start, traceEnd )
			.UseHitboxes( true )
			.IgnoreGameObjectHierarchy( attacker.GameObject )
			.Run();

		return LargeLadBarricade.FindFor( trace.GameObject ) == target;
	}

	private bool CanAttack(
		LargeLadPlayer attacker,
		PlayerController controller )
	{
		if ( attacker is null || controller is null ||
			attacker.Role is not (LargeLadRole.SkinnyKid or
				LargeLadRole.LargeLad or LargeLadRole.Minion) ||
			attacker.EquippedWeapon != LargeLadWeaponId.Melee ||
			attacker.Health?.IsDead != false ||
			attacker.Health.CurrentHealth <= 0.0f ||
			attacker.MovementLocked )
		{
			return false;
		}

		var phase = GetGameManager()?.Phase;

		if ( phase == LargeLadRoundPhase.Playing )
			return true;

		// Skinny Kids can use their head start to break early progression
		// barricades. Hunters remain unable to attack until play begins.
		return phase == LargeLadRoundPhase.HeadStart &&
			attacker.Role == LargeLadRole.SkinnyKid;
	}

	private static bool IsValidPlayerTarget(
		LargeLadPlayer attacker,
		LargeLadPlayer target )
	{
		if ( attacker is null || target is null || target == attacker ||
			target.Health?.IsDead != false ||
			target.Health.CurrentHealth <= 0.0f )
		{
			return false;
		}

		return attacker.Role == LargeLadRole.SkinnyKid
			? target.Role is LargeLadRole.LargeLad or LargeLadRole.Minion
			: target.Role == LargeLadRole.SkinnyKid;
	}

	private void CommitHostCadence( float cooldown, float hostNow )
	{
		cooldown = System.MathF.Max( 0.01f, cooldown );

		if ( !hasHostSwingSchedule )
		{
			hasHostSwingSchedule = true;
			nextHostSwingTime = hostNow + cooldown;
			return;
		}

		nextHostSwingTime =
			System.MathF.Max( hostNow, nextHostSwingTime ) + cooldown;
	}

	[Rpc.Owner( NetFlags.HostOnly )]
	private void ReceiveMeleeResult(
		int ownerSwingSequence,
		LargeLadMeleeResult result )
	{
		if ( ownerSwingSequence <= lastOwnerResultSequence )
			return;

		lastOwnerResultSequence = ownerSwingSequence;
		LastAttackResult = result;

		if ( result is not (LargeLadMeleeResult.PlayerHit or
			LargeLadMeleeResult.BarricadeHit) )
		{
			return;
		}

		hasConfirmedHit = true;
		timeSinceConfirmedHit = 0.0f;
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

	private sealed class MeleeTarget
	{
		public LargeLadPlayer Player { get; private init; }
		public LargeLadBarricade Barricade { get; private init; }

		public static MeleeTarget ForPlayer( LargeLadPlayer player )
		{
			return new MeleeTarget { Player = player };
		}

		public static MeleeTarget ForBarricade(
			LargeLadBarricade barricade )
		{
			return new MeleeTarget { Barricade = barricade };
		}
	}
}
