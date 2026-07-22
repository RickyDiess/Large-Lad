using Sandbox;

public sealed class LargeLadHealth : Component
{
	[Property]
	public bool CreateRagdollOnDeath { get; set; } = true;

	[Property, Title( "Skinny Kid Maximum Health" )]
	public float SkinnyKidMaximumHealth { get; set; } = 100.0f;

	[Property]
	public float LargeLadMaximumHealth { get; set; } = 500.0f;

	[Property]
	public float MinionMaximumHealth { get; set; } = 75.0f;

	[Sync( SyncFlags.FromHost )]
	public float CurrentHealth { get; private set; }

	[Sync( SyncFlags.FromHost ), Change( nameof( OnUseRagdollForCurrentDeathChanged ) )]
	public bool UseRagdollForCurrentDeath { get; private set; } = true;

	[Sync( SyncFlags.FromHost ), Change( nameof( OnIsDeadChanged ) )]
	public bool IsDead { get; private set; }

	[Sync( SyncFlags.FromHost )]
	public float RespawnTimeRemaining { get; private set; }

	private GameObject deathRagdoll;

	public float MaximumHealth
	{
		get
		{
			var player = Components.Get<LargeLadPlayer>();

			return player?.Role switch
			{
				LargeLadRole.LargeLad => LargeLadMaximumHealth,
				LargeLadRole.Minion => MinionMaximumHealth,
				_ => SkinnyKidMaximumHealth
			};
		}
	}

	protected override void OnStart()
	{
		ApplyLifeState( IsDead );
	}

	protected override void OnDestroy()
	{
		RemoveDeathRagdoll();
	}

	public void ResetForCurrentRole()
	{
		if ( !Networking.IsHost )
			return;

		CurrentHealth = MaximumHealth;
		RespawnTimeRemaining = 0.0f;
		UseRagdollForCurrentDeath = true;
		IsDead = false;
		ApplyLifeState( false );
	}

	public bool TakeDamage( float amount )
	{
		if ( !Networking.IsHost || IsDead || CurrentHealth <= 0.0f || amount <= 0.0f )
			return false;

		var player = Components.Get<LargeLadPlayer>();

		if ( player is null || player.Role == LargeLadRole.Unassigned )
			return false;

		CurrentHealth = System.MathF.Max( 0.0f, CurrentHealth - amount );
		return CurrentHealth <= 0.0f;
	}

	public void BeginRespawnCountdown( float duration, bool useRagdoll = true )
	{
		if ( !Networking.IsHost || IsDead )
			return;

		CurrentHealth = 0.0f;
		RespawnTimeRemaining = System.MathF.Max( 0.0f, duration );
		UseRagdollForCurrentDeath = useRagdoll;
		IsDead = true;
		ApplyLifeState( true );
	}

	public bool TickRespawnCountdown()
	{
		if ( !Networking.IsHost || !IsDead )
			return false;

		RespawnTimeRemaining = System.MathF.Max(
			0.0f,
			RespawnTimeRemaining - Time.Delta );

		return RespawnTimeRemaining <= 0.0f;
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
		var player = Components.Get<LargeLadPlayer>();
		var controller = Components.Get<PlayerController>();

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
}
