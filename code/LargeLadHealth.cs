using Sandbox;

public sealed class LargeLadHealth : Component, ILargeLadDamageable
{
	[Property]
	public bool CreateRagdollOnDeath { get; set; } = true;

	[Sync( SyncFlags.FromHost )]
	public float CurrentHealth { get; private set; }

	[Sync( SyncFlags.FromHost ), Change( nameof( OnUseRagdollForCurrentDeathChanged ) )]
	public bool UseRagdollForCurrentDeath { get; private set; } = true;

	[Sync( SyncFlags.FromHost ), Change( nameof( OnIsDeadChanged ) )]
	public bool IsDead { get; private set; }

	[Sync( SyncFlags.FromHost )]
	public float RespawnEndTime { get; private set; }

	private GameObject deathRagdoll;
	private bool hasReportedLethalTransition;
	private LargeLadPlayer cachedPlayer;
	private PlayerController cachedController;
	private LargeLadGameManager cachedGameManager;

	public float RespawnTimeRemaining =>
		IsDead
			? LargeLadGameplayRules.GetTimerTimeRemaining(
				RespawnEndTime,
				Time.Now )
			: 0.0f;

	public float MaximumHealth
	{
		get
		{
			var player = cachedPlayer;

			if ( player is null ||
				!player.TryGetRoleProfile( player.Role, out var profile ) )
			{
				return 0.0f;
			}

			if ( player.Role != LargeLadRole.LargeLad )
				return profile.MaximumHealth;

			var roundMultiplier =
				GetGameManager()?
					.GetLargeLadMaximumHealthMultiplier() ?? 1.0f;
			return LargeLadRoundBalanceRules.GetScaledMaximumHealth(
				profile.MaximumHealth,
				roundMultiplier );
		}
	}

	protected override void OnAwake()
	{
		ResolveCachedReferences();
	}

	protected override void OnStart()
	{
		ResolveCachedReferences();
		ApplyLifeState( IsDead );
	}

	protected override void OnDestroy()
	{
		RemoveDeathRagdoll();
	}

	internal void ResetForCurrentRole()
	{
		if ( !Networking.IsHost )
			return;

		CurrentHealth = MaximumHealth;
		RespawnEndTime = 0.0f;
		hasReportedLethalTransition = false;

		var wasDead = IsDead;
		IsDead = false;
		UseRagdollForCurrentDeath = true;

		// The synchronized change callback restores presentation when a dead
		// player becomes alive. Round/lobby spawns may already be alive, so they
		// need one explicit reapplication instead.
		if ( !wasDead )
		{
			ApplyLifeState( false );
		}
	}

	public bool TakeDamage( float amount )
	{
		return TakeDamage( amount, out _ );
	}

	public bool TakeDamage( float amount, out float appliedDamage )
	{
		var context = new LargeLadDamageContext
		{
			DamageType = LargeLadDamageType.Environment,
			BaseDamage = amount
		};

		var killed = TryApplyDamage( context, out var applied );
		appliedDamage = applied.AppliedDamage;
		return killed;
	}

	public bool TryApplyDamage(
		LargeLadDamageContext damage,
		out LargeLadDamageContext appliedDamage )
	{
		appliedDamage = damage.WithAppliedDamage( 0.0f );

		if ( !Networking.IsHost || IsDead || CurrentHealth <= 0.0f || damage.BaseDamage <= 0.0f )
			return false;

		var player = cachedPlayer;

		if ( player is null || player.Role == LargeLadRole.Unassigned )
			return false;

		if ( !player.TryGetRoleProfile( player.Role, out var profile ) )
			return false;

		var isEatExecution =
			damage.DamageType == LargeLadDamageType.Eat;

		if ( isEatExecution &&
			(player.Role != LargeLadRole.SkinnyKid ||
				damage.AttackerRole != LargeLadRole.LargeLad ||
				damage.Attacker is null) )
		{
			return false;
		}

		// Eat is an execution, not ordinary damage. It deliberately bypasses
		// incoming-damage modifiers (including any Last Skinny Kid reduction)
		// and crosses the lethal edge exactly once through the normal manager.
		var amount = isEatExecution
			? CurrentHealth
			: damage.BaseDamage * System.MathF.Max(
				0.0f,
				profile.IncomingDamageMultiplier );

		if ( amount <= 0.0f )
			return false;

		var previousHealth = CurrentHealth;
		CurrentHealth = System.MathF.Max( 0.0f, previousHealth - amount );
		appliedDamage = damage.WithAppliedDamage( amount );

		if ( LargeLadGameplayRules.IsNewLethalTransition(
			previousHealth,
			CurrentHealth,
			hasReportedLethalTransition ) )
		{
			var managerAccepted =
				TryReportLethalTransition( player, appliedDamage );

			if ( LargeLadGameplayRules.CanCommitLethalTransition(
				previousHealth,
				CurrentHealth,
				hasReportedLethalTransition,
				managerAccepted ) )
			{
				hasReportedLethalTransition = true;
				return true;
			}

			// The manager did not commit the death, so roll the damage
			// transaction back to a valid living state. A later damage event can
			// retry the same lethal edge after registry/bootstrap recovery.
			CurrentHealth = previousHealth;
			appliedDamage = damage.WithAppliedDamage( 0.0f );
		}

		return false;
	}

	internal bool TryExecuteEat(
		LargeLadPlayer attacker,
		out LargeLadDamageContext appliedDamage )
	{
		var execution = new LargeLadDamageContext
		{
			Attacker = attacker?.GameObject,
			AttackerRole = attacker?.Role ?? LargeLadRole.Unassigned,
			SourceWeapon = LargeLadWeaponId.Melee,
			DamageType = LargeLadDamageType.Eat,
			BaseDamage = CurrentHealth
		};

		return TryApplyDamage( execution, out appliedDamage );
	}

	internal bool TryHealMissingHealth(
		float missingHealthFraction,
		out float appliedHealing )
	{
		appliedHealing = 0.0f;

		if ( !Networking.IsHost || IsDead || CurrentHealth <= 0.0f )
			return false;

		var healedHealth = LargeLadEatRules.GetHealedHealth(
			CurrentHealth,
			MaximumHealth,
			missingHealthFraction );
		appliedHealing = System.MathF.Max(
			0.0f,
			healedHealth - CurrentHealth );
		CurrentHealth = healedHealth;
		return appliedHealing > 0.0f;
	}

	internal bool RequestEnvironmentalDeath()
	{
		if ( !Networking.IsHost || IsDead || CurrentHealth <= 0.0f )
			return false;

		var player = cachedPlayer;

		if ( player is null || player.Role == LargeLadRole.Unassigned )
			return false;

		var previousHealth = CurrentHealth;
		CurrentHealth = 0.0f;
		var death = new LargeLadDamageContext
		{
			AttackerRole = LargeLadRole.Unassigned,
			SourceWeapon = LargeLadWeaponId.None,
			DamageType = LargeLadDamageType.Environment,
			BaseDamage = previousHealth,
			AppliedDamage = previousHealth
		};

		if ( !LargeLadGameplayRules.IsNewLethalTransition(
			previousHealth,
			CurrentHealth,
			hasReportedLethalTransition ) )
		{
			return false;
		}

		var managerAccepted = TryReportLethalTransition( player, death );

		if ( LargeLadGameplayRules.CanCommitLethalTransition(
			previousHealth,
			CurrentHealth,
			hasReportedLethalTransition,
			managerAccepted ) )
		{
			hasReportedLethalTransition = true;
			return true;
		}

		CurrentHealth = previousHealth;
		return false;
	}

	internal bool TryBeginDeath( float duration, bool useRagdoll )
	{
		if ( !Networking.IsHost || IsDead || CurrentHealth > 0.0f )
			return false;

		CurrentHealth = 0.0f;
		RespawnEndTime = LargeLadGameplayRules.GetTimerDeadline(
			Time.Now,
			duration );
		UseRagdollForCurrentDeath = useRagdoll;
		IsDead = true;
		return true;
	}

	internal bool TickRespawnCountdown()
	{
		if ( !Networking.IsHost || !IsDead )
			return false;

		return LargeLadGameplayRules.HasTimerReachedDeadline(
			RespawnEndTime,
			Time.Now );
	}

	private bool TryReportLethalTransition(
		LargeLadPlayer player,
		LargeLadDamageContext damage )
	{
		var manager = GetGameManager();

		if ( manager is null ||
			!manager.HandlePlayerLethalTransition( player, damage ) )
		{
			return false;
		}

		return true;
	}

	private void OnIsDeadChanged( bool oldValue, bool newValue )
	{
		ApplyLifeState( newValue );
	}

	private void OnUseRagdollForCurrentDeathChanged( bool oldValue, bool newValue )
	{
		if ( IsDead )
		{
			ApplyLifeState( true );
		}
	}

	private void ApplyLifeState( bool isDead )
	{
		var player = cachedPlayer;
		var controller = cachedController;

		if ( isDead && CreateRagdollOnDeath && UseRagdollForCurrentDeath )
		{
			CreateDeathRagdoll( controller );
		}
		else
		{
			RemoveDeathRagdoll();
		}

		if ( player?.BodyRenderer is not null )
		{
			// The Dresser adds clothing beneath the body object. Disabling the
			// whole visual hierarchy prevents detached clothing from T-posing.
			player.BodyRenderer.Enabled = true;
			player.BodyRenderer.GameObject.Enabled = !isDead;
		}

		if ( controller?.ColliderObject is not null )
		{
			controller.ColliderObject.Enabled = !isDead;
		}

		if ( controller?.Body is not null )
		{
			controller.Body.ClearForces();
			controller.Body.Velocity = Vector3.Zero;
			controller.Body.AngularVelocity = Vector3.Zero;
		}

		player?.RefreshMovementState();
	}

	private void CreateDeathRagdoll( PlayerController controller )
	{
		if ( deathRagdoll is not null && deathRagdoll.IsValid )
			return;

		deathRagdoll = controller?.CreateRagdoll( $"{GameObject.Name} Ragdoll" );
	}

	private void RemoveDeathRagdoll()
	{
		if ( deathRagdoll is not null && deathRagdoll.IsValid )
		{
			deathRagdoll.Destroy();
		}

		deathRagdoll = null;
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
		if ( cachedPlayer is null ||
			!cachedPlayer.IsValid ||
			cachedPlayer.GameObject != GameObject )
		{
			cachedPlayer = Components.Get<LargeLadPlayer>();
		}

		if ( cachedController is null ||
			!cachedController.IsValid ||
			cachedController.GameObject != GameObject )
		{
			cachedController = Components.Get<PlayerController>();
		}

		GetGameManager();
	}
}
