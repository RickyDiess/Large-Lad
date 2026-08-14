using Sandbox;

public sealed class LargeLadHealth : Component, ILargeLadDamageable,
	Component.IDamageable
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
	private bool isApplyingAuthorizedEatExecution;
	private bool hasPendingPassiveRegeneration;
	private float lastDamageTime;
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

	protected override void OnUpdate()
	{
		if ( !Networking.IsHost || !hasPendingPassiveRegeneration )
			return;

		var player = cachedPlayer;
		var manager = GetGameManager();
		var maximumHealth = MaximumHealth;
		var cap = LargeLadSkinnyKidSurvivabilityRules
			.GetRegenerationCap( maximumHealth );
		var isRoundActive = manager?.Phase is
			LargeLadRoundPhase.HeadStart or LargeLadRoundPhase.Playing;

		if ( player is null ||
			player.Role != LargeLadRole.SkinnyKid ||
			IsDead ||
			CurrentHealth <= 0.0f ||
			!isRoundActive ||
			CurrentHealth >= cap )
		{
			ClearPassiveRegenerationState();
			return;
		}

		CurrentHealth = LargeLadSkinnyKidSurvivabilityRules
			.GetRegeneratedHealth(
				player.Role,
				isLiving: true,
				isRoundActive: true,
				CurrentHealth,
				maximumHealth,
				Time.Now - lastDamageTime,
				Time.Delta,
				manager.SkinnyKidRegenerationDelay,
				manager.SkinnyKidRegenerationRate );

		if ( CurrentHealth >= cap )
			ClearPassiveRegenerationState();
	}

	protected override void OnDisabled()
	{
		ClearPassiveRegenerationState();
		base.OnDisabled();
	}

	protected override void OnDestroy()
	{
		ClearPassiveRegenerationState();
		RemoveDeathRagdoll();
	}

	internal void ResetForCurrentRole()
	{
		if ( !Networking.IsHost )
			return;

		CurrentHealth = MaximumHealth;
		RespawnEndTime = 0.0f;
		hasReportedLethalTransition = false;
		ClearPassiveRegenerationState();

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

	/// <summary>
	/// Native weapon entry point. Damage remains host-authoritative and is
	/// translated into the existing Large Lad envelope before health changes.
	/// </summary>
	public void OnDamage( in DamageInfo damage )
	{
		if ( !Networking.IsHost || damage is null ||
			!float.IsFinite( damage.Damage ) || damage.Damage <= 0.0f )
		{
			return;
		}

		ResolveCachedReferences();
		var victim = cachedPlayer;
		var attacker = damage.Attacker?.Components.Get<LargeLadPlayer>(
			FindMode.EverythingInSelfAndAncestors );
		var weapon = damage.Weapon?.Components.Get<LargeLadFirearm>(
			FindMode.EverythingInSelfAndAncestors );

		if ( victim is null || attacker is null || weapon is null ||
			!LargeLadWeaponCatalog.IsFirearm( weapon.WeaponId ) ||
			GetGameManager()?.Phase != LargeLadRoundPhase.Playing ||
			!weapon.IsAuthoritativelyHeldBy( attacker ) ||
			!LargeLadNativeWeaponRules.IsValidPlayerTarget(
				attacker.Role,
				victim.Role,
				!IsDead && CurrentHealth > 0.0f ) )
		{
			return;
		}

		var hitRegion = LargeLadNativeWeaponRules.ClassifyNativeDamage(
			damage.Tags?.Has(
				LargeLadFirearmHitRules.HeadHitboxTag ) == true,
			damage.Hitbox?.Tags?.Has(
				LargeLadFirearmHitRules.HeadHitboxTag ) == true,
			damage.Hitbox?.Bone?.Name );
		var context = new LargeLadDamageContext
		{
			Attacker = attacker.GameObject,
			AttackerRole = attacker.Role,
			SourceWeapon = weapon.WeaponId,
			SourceShotSequence = weapon.LastAuthoritativeShotSequence,
			DamageType = LargeLadDamageType.Firearm,
			HitRegion = hitRegion,
			BaseDamage = damage.Damage
		};

		TryApplyDamage( context, out _ );
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

		if ( !Networking.IsHost || IsDead || CurrentHealth <= 0.0f )
			return false;

		var player = cachedPlayer;

		if ( player is null || player.Role == LargeLadRole.Unassigned )
			return false;

		if ( !player.TryGetRoleProfile( player.Role, out var profile ) )
			return false;

		var isEatExecution =
			damage.DamageType == LargeLadDamageType.Eat &&
			damage.IsExecution;

		if ( damage.DamageType == LargeLadDamageType.Eat &&
			(!isEatExecution ||
			(player.Role != LargeLadRole.SkinnyKid ||
				damage.AttackerRole != LargeLadRole.LargeLad ||
				damage.Attacker is null)) )
		{
			return false;
		}

		// Eat and environmental death are explicit executions. Both consume all
		// current health before ordinary incoming-damage modifiers, while their
		// original damage type remains intact for lifecycle and killfeed metadata.
		// A valid Minion firearm headshot similarly resolves to current health,
		// but remains firearm damage with its shot and hit-region attribution.
		var amount = LargeLadDamageRules.ResolveIncomingDamage(
			player.Role,
			isLiving: true,
			GetGameManager()?
				.IsLastEffectiveLivingSkinnyKid( player ) == true,
			damage.SourceWeapon,
			damage.DamageType,
			damage.HitRegion,
			damage.IsExecution,
			CurrentHealth,
			damage.BaseDamage,
			profile.IncomingDamageMultiplier );
		amount = LargeLadEatRules.FilterDamageForEatCommit(
			player.EatParticipation,
			damage.DamageType,
			amount,
			isApplyingAuthorizedEatExecution );

		if ( amount <= 0.0f )
			return false;

		RestartPassiveRegenerationDelay( player );

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
		LargeLadEatAttack owner,
		LargeLadPlayer attacker,
		out LargeLadDamageContext appliedDamage )
	{
		var execution = new LargeLadDamageContext
		{
			Attacker = attacker?.GameObject,
			AttackerRole = attacker?.Role ?? LargeLadRole.Unassigned,
			SourceWeapon = LargeLadWeaponId.Melee,
			DamageType = LargeLadDamageType.Eat,
			IsExecution = true,
			BaseDamage = CurrentHealth
		};

		appliedDamage = execution.WithAppliedDamage( 0.0f );
		var victim = cachedPlayer;

		if ( !Networking.IsHost ||
			owner is null ||
			!owner.IsValid ||
			!owner.IsEating ||
			victim is null ||
			attacker is null ||
			!victim.IsEatParticipationOwnedBy(
				owner,
				LargeLadEatParticipation.Victim ) ||
			!attacker.IsEatParticipationOwnedBy(
				owner,
				LargeLadEatParticipation.Attacker ) )
		{
			return false;
		}

		isApplyingAuthorizedEatExecution = true;

		try
		{
			return TryApplyDamage( execution, out appliedDamage );
		}
		finally
		{
			isApplyingAuthorizedEatExecution = false;
		}
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

		var death = new LargeLadDamageContext
		{
			AttackerRole = LargeLadRole.Unassigned,
			SourceWeapon = LargeLadWeaponId.None,
			DamageType = LargeLadDamageType.Environment,
			IsExecution = true,
			BaseDamage = CurrentHealth
		};

		return TryApplyDamage( death, out _ );
	}

	internal bool TryBeginDeath( float duration, bool useRagdoll )
	{
		if ( !Networking.IsHost || IsDead || CurrentHealth > 0.0f )
			return false;

		CurrentHealth = 0.0f;
		ClearPassiveRegenerationState();
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

	internal void ClearPassiveRegenerationState()
	{
		hasPendingPassiveRegeneration = false;
		lastDamageTime = 0.0f;
	}

	private void RestartPassiveRegenerationDelay( LargeLadPlayer player )
	{
		if ( player?.Role != LargeLadRole.SkinnyKid || IsDead )
		{
			ClearPassiveRegenerationState();
			return;
		}

		hasPendingPassiveRegeneration = true;
		lastDamageTime = Time.Now;
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
