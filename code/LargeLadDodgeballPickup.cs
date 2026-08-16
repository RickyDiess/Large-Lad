using Sandbox;

public enum LargeLadDodgeballPresentationPhase
{
	Throw,
	Impact,
	Pickup,
	MinionKill,
	LargeLadHit
}

public readonly struct LargeLadDodgeballPresentation
{
	public LargeLadDodgeballPresentation(
		LargeLadDodgeballPresentationPhase phase,
		int sequence,
		Vector3 position,
		Vector3 normal,
		LargeLadRole targetRole )
	{
		Phase = phase;
		Sequence = sequence;
		Position = position;
		Normal = normal;
		TargetRole = targetRole;
	}

	public LargeLadDodgeballPresentationPhase Phase { get; }
	public int Sequence { get; }
	public Vector3 Position { get; }
	public Vector3 Normal { get; }
	public LargeLadRole TargetRole { get; }
}

/// <summary>
/// One authored, networked physical dodgeball. Carrying reserves and hides this
/// same object; throw, manual drop, death, disconnect, transfer, and reset never
/// create a runtime copy.
/// </summary>
public sealed class LargeLadDodgeballPickup :
	LargeLadRoundResettableComponent,
	Component.ICollisionListener,
	Component.INetworkSpawn
{
	[Property, Group( "Setup" )]
	public Renderer PickupRenderer { get; set; }

	[Property, Group( "Setup" ), Title( "Solid Ball Collider" )]
	public Collider BallCollider { get; set; }

	[Property, Group( "Setup" ), Title( "Pickup Trigger" )]
	public Collider PickupCollider { get; set; }

	[Property, Group( "Setup" )]
	public Rigidbody BallRigidbody { get; set; }

	[Property, Group( "Throw" )]
	public float ThrowSpeed { get; set; } = 1350.0f;

	[Property, Group( "Throw" )]
	public float InheritedCarrierVelocityScale { get; set; } = 0.35f;

	[Property, Group( "Throw" )]
	public float MinimumCombatImpactSpeed { get; set; } = 350.0f;

	[Property, Group( "Throw" )]
	public float MaximumFlightDuration { get; set; } = 5.0f;

	[Property, Group( "Throw" )]
	public float PickupCooldown { get; set; } = 0.35f;

	[Property, Group( "Throw" )]
	public float ThrowerCollisionGrace { get; set; } = 0.2f;

	[Property, Group( "Throw" )]
	public float ThrowerClearanceDistance { get; set; } = 54.0f;

	[Property, Group( "Throw" )]
	public float LaunchForwardOffset { get; set; } = 32.0f;

	[Property, Group( "Throw" )]
	public float BallRadius { get; set; } = 33.5f;

	[Property, Group( "Throw" )]
	public Vector3 BallCenterOffset { get; set; } = Vector3.Zero;

	[Property, Group( "Combat" )]
	public float LargeLadDamage { get; set; } = 0.0f;

	[Property, Group( "Combat" )]
	public float LargeLadHorizontalKnockbackImpulse { get; set; } =
		180000.0f;

	[Property, Group( "Combat" )]
	public float LargeLadUpwardKnockbackImpulse { get; set; } =
		45000.0f;

	[Property, Group( "Physics" )]
	public float MaximumLinearSpeed { get; set; } = 2400.0f;

	[Property, Group( "Physics" )]
	public float MaximumAngularSpeed { get; set; } = 2400.0f;

	[Property, Group( "Presentation" )]
	public float MinimumImpactPresentationSpeed { get; set; } = 120.0f;

	[Property, Group( "Diagnostics" )]
	public bool EnableDebugLogging { get; set; }

	[Sync( SyncFlags.FromHost ), Change( nameof( OnAvailableChanged ) )]
	public bool Available { get; private set; } = true;

	[Sync( SyncFlags.FromHost ), Change( nameof( OnPickupEnabledChanged ) )]
	public bool PickupEnabled { get; private set; } = true;

	[Sync( SyncFlags.FromHost )]
	public int UtilityInstanceId { get; private set; }

	[Sync( SyncFlags.FromHost )]
	public int ActiveThrowSequence { get; private set; }

	public event System.Action<LargeLadDodgeballPresentation> Presentation;

	private readonly LargeLadDodgeballImpactGate impactGate = new();
	private Transform authoredTransform;
	private bool hasAuthoredTransform;
	private LargeLadUtilityInstance utilityInstance;
	private LargeLadPlayer thrower;
	private LargeLadRole throwerRole;
	private Vector3 throwerPositionAtLaunch;
	private float pickupUnlockTime;
	private float flightExpireTime;
	private int nextPresentationSequence;
	private int lastLocalPresentationSequence;
	private bool clientAuthoredCopySuppressed;

	internal LargeLadUtilityLocation Location =>
		utilityInstance?.Location ?? LargeLadUtilityLocation.OriginAvailable;

	internal void SuppressClientAuthoredCopy()
	{
		if ( Networking.IsHost )
			return;

		clientAuthoredCopySuppressed = true;
		ResolveAuthoredParts();
		StopPhysicsMotion();
		ApplyAvailableState();
	}

	protected override void OnAwake()
	{
		ResolveAuthoredParts();
		ConfigurePhysics();
		ConfigureLocalSimulationAuthority();
	}

	protected override void OnStart()
	{
		ResolveAuthoredParts();
		ConfigurePhysics();
		authoredTransform = GameObject.WorldTransform;
		hasAuthoredTransform = true;

		if ( Networking.IsHost )
		{
			EnsureUtilityInstance();
			ConfigureNetworkAuthority();
		}

		ApplyAvailableState();
	}

	void Component.INetworkSpawn.OnNetworkSpawn( Connection owner )
	{
		ResolveAuthoredParts();

		if ( Networking.IsHost )
			ConfigureNetworkAuthority();

		// Stop proxy physics as part of network-spawn setup so the client presents
		// the host transform without simulating the same loose ball locally.
		ApplyAvailableState();
	}

	protected override void OnUpdate()
	{
		if ( !Networking.IsHost || !Available )
			return;

		if ( !PickupEnabled &&
			LargeLadDodgeballRules.CanPickup(
				utilityInstance?.Location ??
					LargeLadUtilityLocation.OriginAvailable,
				Available,
				Time.Now,
				pickupUnlockTime ) )
			SetPickupEnabled( true );

		if ( utilityInstance?.Location == LargeLadUtilityLocation.Thrown &&
			Time.Now >= flightExpireTime )
		{
			SettleActiveThrow();
		}
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost || !Available )
			return;

		if ( GameObject.Network.Active && GameObject.Network.IsProxy )
			GameObject.Network.DropOwnership();

		var body = BallRigidbody?.PhysicsBody;

		if ( body is null || body.BodyType != PhysicsBodyType.Dynamic )
			return;

		body.Velocity = LargeLadDodgeballRules.ClampVelocity(
			body.Velocity,
			MaximumLinearSpeed );

		var angularSpeed = body.AngularVelocity.Length;
		var clampedAngularSpeed =
			LargeLadDodgeballRules.ClampAngularSpeed(
				angularSpeed,
				MaximumAngularSpeed );

		if ( angularSpeed > clampedAngularSpeed && angularSpeed > 0.0001f )
		{
			body.AngularVelocity = body.AngularVelocity.Normal *
				clampedAngularSpeed;
		}
	}

	protected override void OnValidate()
	{
		ResolveAuthoredParts();

		if ( GameObject.NetworkMode != NetworkMode.Object )
		{
			Log.Warning(
				$"{GameObject.Name}: a dodgeball utility pickup must use " +
				"Network Mode Object." );
		}

		if ( BallCollider is null || BallCollider.IsTrigger )
			Log.Warning( $"{GameObject.Name}: dodgeball needs a solid ball collider." );

		if ( PickupCollider is null || !PickupCollider.IsTrigger )
			Log.Warning( $"{GameObject.Name}: dodgeball needs a separate pickup trigger." );

		if ( BallRigidbody is null )
			Log.Warning( $"{GameObject.Name}: dodgeball needs a Rigidbody." );

		ValidateConfiguration();
	}

	protected override void OnDestroy()
	{
		if ( Networking.IsHost &&
			utilityInstance?.Location == LargeLadUtilityLocation.Carried &&
			utilityInstance.Carrier is LargeLadNativeInventory carrier )
		{
			carrier.HandleUtilitySourceDestroyed( this );
		}

		base.OnDestroy();
	}

	void Component.ICollisionListener.OnCollisionStart( Collision collision )
	{
		if ( !Networking.IsHost || !Available )
			return;

		var impactVelocity = BallRigidbody?.PhysicsBody?.Velocity ??
			collision.Contact.Speed;
		var impactSpeed = System.MathF.Max(
			impactVelocity.Length,
			System.MathF.Abs( collision.Contact.NormalSpeed ) );
		var target = collision.Other.GameObject?.Components.Get<LargeLadPlayer>(
			FindMode.EverythingInSelfAndAncestors );
		var hitThrowerDuringGrace = IsProtectedThrowerContact( target );
		var decision = LargeLadDodgeballRules.ResolveImpact(
			utilityInstance?.Location ?? LargeLadUtilityLocation.OriginAvailable,
			impactGate.ImpactConsumed,
			hitThrowerDuringGrace,
			throwerRole,
			target?.Role ?? LargeLadRole.Unassigned,
			target?.Health?.IsDead == false &&
				target.Health.CurrentHealth > 0.0f,
			impactSpeed,
			MinimumCombatImpactSpeed );

		if ( hitThrowerDuringGrace )
		{
			ReassertOutwardThrowVelocity( impactVelocity );
			return;
		}

		if ( impactSpeed >= System.MathF.Max(
			0.0f,
			MinimumImpactPresentationSpeed ) )
		{
			BroadcastPresentation(
				LargeLadDodgeballPresentationPhase.Impact,
				collision.Contact.Point,
				collision.Contact.Normal,
				target?.Role ?? LargeLadRole.Unassigned );
		}

		if ( !decision.ConsumesThrow ||
			!impactGate.TryConsume( ActiveThrowSequence ) )
		{
			return;
		}

		var attackerObject = thrower is not null && thrower.IsValid
			? thrower.GameObject
			: null;
		var activeThrow = ActiveThrowSequence;
		utilityInstance?.TrySettleThrow( activeThrow );
		ActiveThrowSequence = 0;

		switch ( decision.Outcome )
		{
			case LargeLadDodgeballHitOutcome.MinionKill:
				ApplyMinionKill(
					target,
					attackerObject,
					activeThrow,
					collision.Contact );
				break;
			case LargeLadDodgeballHitOutcome.LargeLadHit:
				ApplyLargeLadHit(
					target,
					attackerObject,
					activeThrow,
					impactVelocity,
					collision.Contact );
				break;
		}

		thrower = null;
		throwerRole = LargeLadRole.Unassigned;
		throwerPositionAtLaunch = Vector3.Zero;
	}

	void Component.ICollisionListener.OnCollisionUpdate( Collision collision )
	{
	}

	void Component.ICollisionListener.OnCollisionStop( CollisionStop collision )
	{
	}

	internal void TryCollectFromTrigger( Collider other )
	{
		if ( !Networking.IsHost || !Available || !PickupEnabled )
			return;

		var player = other?.GameObject?.Components.Get<LargeLadPlayer>(
			FindMode.EverythingInSelfAndAncestors );

		if ( player is null ||
			!LargeLadUtilityRules.CanUseUtility(
				player.Role,
				player.Health?.IsDead != false ) )
		{
			return;
		}

		var inventory = player.NativeInventory;

		if ( inventory is null )
			return;

		if ( inventory.HasUtility )
		{
			inventory.NotifyUtilitySlotFull();
			return;
		}

		TryCollect( inventory );
	}

	internal void EnsureUtilityIdentityForHost()
	{
		if ( Networking.IsHost )
			EnsureUtilityInstance();
	}

	internal bool TryThrowFromCarrier(
		LargeLadNativeInventory carrier,
		LargeLadUtilityState state,
		Vector3 eyePosition,
		Vector3 direction,
		Vector3 inheritedVelocity )
	{
		if ( !Networking.IsHost ||
			carrier is null ||
			!carrier.IsValid ||
			!LargeLadUtilityRules.IsValidState( state ) )
		{
			return false;
		}

		EnsureUtilityInstance();
		direction = direction.LengthSquared > 0.0001f
			? direction.Normal
			: carrier.GameObject.WorldRotation.Forward;
		var launchCenter = eyePosition + direction *
			System.MathF.Max( BallRadius * 2.0f, LaunchForwardOffset );
		var clearance = Scene.Trace
			.Sphere(
				System.MathF.Max( 1.0f, BallRadius ),
				eyePosition,
				launchCenter )
			.IgnoreGameObjectHierarchy( carrier.GameObject )
			.Run();

		if ( clearance.Hit || clearance.StartedSolid ||
			utilityInstance is null ||
			!utilityInstance.TryThrow(
				carrier,
				state,
				out var throwSequence ) )
		{
			return false;
		}

		ReclaimHostSimulation();
		thrower = carrier.Components.Get<LargeLadPlayer>();
		throwerRole = thrower?.Role ?? LargeLadRole.Unassigned;
		throwerPositionAtLaunch = thrower?.GameObject.WorldPosition ??
			carrier.GameObject.WorldPosition;
		ActiveThrowSequence = throwSequence;
		impactGate.BeginThrow( throwSequence );
		pickupUnlockTime = Time.Now +
			System.MathF.Max( 0.0f, PickupCooldown );
		flightExpireTime = Time.Now +
			System.MathF.Max( 0.0f, MaximumFlightDuration );
		GameObject.WorldPosition = launchCenter - BallCenterOffset;
		GameObject.Network.ClearInterpolation();
		SetWorldState( available: true, pickupEnabled: false );
		ActivatePhysics(
			LargeLadDodgeballRules.GetThrowVelocity(
				direction,
				ThrowSpeed,
				inheritedVelocity * System.Math.Clamp(
					InheritedCarrierVelocityScale,
					0.0f,
					1.0f ) ) );
		BroadcastPresentation(
			LargeLadDodgeballPresentationPhase.Throw,
			launchCenter,
			direction,
			LargeLadRole.Unassigned );
		DebugLog(
			$"{carrier.GameObject.Name} threw dodgeball #{state.InstanceId} " +
			$"as throw {throwSequence}." );
		return true;
	}

	internal bool TryDropFromCarrier(
		LargeLadNativeInventory carrier,
		LargeLadUtilityState state,
		Vector3 nearPosition,
		Vector3 forward )
	{
		if ( !Networking.IsHost ||
			carrier is null ||
			!carrier.IsValid ||
			!LargeLadUtilityRules.IsValidState( state ) )
		{
			return false;
		}

		EnsureUtilityInstance();

		if ( utilityInstance is null ||
			!LargeLadDropPlacement.TryFind(
				Scene,
				carrier.GameObject,
				nearPosition,
				forward,
				out var worldPosition ) ||
			!utilityInstance.TryDrop( carrier, state ) )
		{
			return false;
		}

		ReclaimHostSimulation();
		thrower = null;
		throwerRole = LargeLadRole.Unassigned;
		throwerPositionAtLaunch = Vector3.Zero;
		ActiveThrowSequence = 0;
		impactGate.Reset();
		pickupUnlockTime = Time.Now +
			System.MathF.Max( 0.0f, PickupCooldown );
		GameObject.WorldPosition = worldPosition;
		GameObject.Network.ClearInterpolation();
		SetWorldState( available: true, pickupEnabled: false );
		ActivatePhysics( Vector3.Zero );
		DebugLog(
			$"{carrier.GameObject.Name} dropped dodgeball " +
			$"#{state.InstanceId}." );
		return true;
	}

	internal bool ReturnCarrierToOrigin(
		LargeLadNativeInventory carrier,
		LargeLadUtilityState state )
	{
		if ( !Networking.IsHost )
			return false;

		EnsureUtilityInstance();

		if ( utilityInstance is null )
			return false;

		var returned = utilityInstance.ReturnCarrierToOrigin(
			carrier,
			state );

		if ( !returned )
			returned = utilityInstance.ForceReturnToOrigin( state );

		if ( !returned )
			return false;

		ReclaimHostSimulation();
		RestoreAuthoredTransform();
		ResetTransientThrowState();
		SetWorldState( available: true, pickupEnabled: true );
		return true;
	}

	internal void ReleaseCarrierForRoundReset(
		LargeLadNativeInventory carrier )
	{
		if ( !Networking.IsHost ||
			utilityInstance is null ||
			utilityInstance.Location != LargeLadUtilityLocation.Carried ||
			!ReferenceEquals( utilityInstance.Carrier, carrier ) )
		{
			return;
		}

		utilityInstance.ReturnCarrierToOrigin(
			carrier,
			utilityInstance.State );
	}

	public override void ResetForRound()
	{
		if ( !Networking.IsHost )
			return;

		EnsureUtilityInstance();
		utilityInstance?.ResetForRound();
		ReclaimHostSimulation();
		RestoreAuthoredTransform();
		ResetTransientThrowState();
		SetWorldState( available: true, pickupEnabled: true );
		DebugLog( $"Reset dodgeball pickup '{GameObject.Name}'." );
	}

	private void TryCollect( LargeLadNativeInventory inventory )
	{
		EnsureUtilityInstance();
		var pendingState = utilityInstance?.State ?? default;

		if ( utilityInstance is null ||
			!inventory.CanAcceptUtility(
				this,
				pendingState,
				pickupAvailable: Available && PickupEnabled ) )
		{
			return;
		}

		var wasInWorld = utilityInstance.Location is
			LargeLadUtilityLocation.Dropped or
			LargeLadUtilityLocation.Thrown;
		var collected = wasInWorld
			? utilityInstance.TryCollectDropped( inventory, out var state )
			: utilityInstance.TryCollectFromOrigin( inventory, out state );

		if ( !collected )
			return;

		if ( !inventory.TryGrantUtility( this, state ) )
		{
			if ( wasInWorld )
				utilityInstance.TryDrop( inventory, state );
			else
				utilityInstance.ReturnCarrierToOrigin( inventory, state );

			return;
		}

		ResetTransientThrowState();
		StopPhysicsMotion();
		SetWorldState( available: false, pickupEnabled: false );
		BroadcastPresentation(
			LargeLadDodgeballPresentationPhase.Pickup,
			GameObject.WorldPosition + BallCenterOffset,
			Vector3.Up,
			LargeLadRole.SkinnyKid );
		TransferOwnershipToCarrier( inventory );
		DebugLog(
			$"{inventory.GameObject.Name} collected dodgeball " +
			$"#{state.InstanceId} from '{GameObject.Name}'." );
	}

	private void ApplyMinionKill(
		LargeLadPlayer target,
		GameObject attacker,
		int throwSequence,
		PhysicsContact contact )
	{
		if ( target?.Role != LargeLadRole.Minion ||
			target.Health?.IsDead != false )
		{
			return;
		}

		var damage = new LargeLadDamageContext
		{
			Attacker = attacker,
			AttackerRole = throwerRole,
			SourceWeapon = LargeLadWeaponId.None,
			SourceShotSequence = throwSequence,
			DamageType = LargeLadDamageType.Dodgeball,
			BaseDamage = LargeLadDodgeballRules.GetMinionKillDamage(
				target.Health.CurrentHealth )
		};

		if ( target.Health.TryApplyDamage( damage, out _ ) )
		{
			BroadcastPresentation(
				LargeLadDodgeballPresentationPhase.MinionKill,
				contact.Point,
				contact.Normal,
				target.Role );
		}
	}

	private void ApplyLargeLadHit(
		LargeLadPlayer target,
		GameObject attacker,
		int throwSequence,
		Vector3 impactVelocity,
		PhysicsContact contact )
	{
		if ( target?.Role != LargeLadRole.LargeLad ||
			target.Health?.IsDead != false )
		{
			return;
		}

		var targetPosition = target.GameObject.WorldPosition;
		var throwerPosition = attacker is not null && attacker.IsValid
			? attacker.WorldPosition
			: throwerPositionAtLaunch;
		var awayFromThrower = targetPosition - throwerPosition;
		var knockback = LargeLadDodgeballRules
			.GetLargeLadKnockbackImpulse(
				awayFromThrower,
				impactVelocity,
				LargeLadHorizontalKnockbackImpulse,
				LargeLadUpwardKnockbackImpulse );
		target.ApplyDodgeballKnockback( knockback );

		var damage = new LargeLadDamageContext
		{
			Attacker = attacker,
			AttackerRole = throwerRole,
			SourceWeapon = LargeLadWeaponId.None,
			SourceShotSequence = throwSequence,
			DamageType = LargeLadDamageType.Dodgeball,
			BaseDamage = LargeLadDodgeballRules.GetLargeLadDamage(
				LargeLadDamage )
		};

		if ( damage.BaseDamage > 0.0f )
			target.Health.TryApplyDamage( damage, out _ );

		BroadcastPresentation(
			LargeLadDodgeballPresentationPhase.LargeLadHit,
			contact.Point,
			contact.Normal,
			target.Role );
	}

	private bool IsProtectedThrowerContact( LargeLadPlayer target )
	{
		if ( target is null || target != thrower )
			return false;

		return Time.Now < pickupUnlockTime -
			System.MathF.Max( 0.0f, PickupCooldown ) +
			System.MathF.Max( 0.0f, ThrowerCollisionGrace ) ||
			GameObject.WorldPosition.Distance(
				target.GameObject.WorldPosition ) <
				System.MathF.Max( 0.0f, ThrowerClearanceDistance );
	}

	private void ReassertOutwardThrowVelocity( Vector3 currentVelocity )
	{
		var body = BallRigidbody?.PhysicsBody;

		if ( body is null || thrower is null || !thrower.IsValid )
			return;

		var outward = GameObject.WorldPosition -
			thrower.GameObject.WorldPosition;

		if ( outward.LengthSquared <= 0.0001f )
			return;

		var speed = System.MathF.Max(
			currentVelocity.Length,
			System.MathF.Min(
				LargeLadDodgeballRules.MaximumThrowSpeed,
				System.MathF.Max( 0.0f, ThrowSpeed ) ) );
		body.Velocity = LargeLadDodgeballRules.ClampVelocity(
			outward.Normal * speed,
			MaximumLinearSpeed );
	}

	private void SettleActiveThrow()
	{
		var activeThrow = ActiveThrowSequence;

		if ( activeThrow <= 0 ||
			utilityInstance?.TrySettleThrow( activeThrow ) != true )
		{
			return;
		}

		ActiveThrowSequence = 0;
		impactGate.Reset();
		thrower = null;
		throwerRole = LargeLadRole.Unassigned;
		throwerPositionAtLaunch = Vector3.Zero;
	}

	private void EnsureUtilityInstance()
	{
		if ( utilityInstance is not null )
			return;

		var instanceId = CreateStableInstanceId();
		UtilityInstanceId = instanceId;
		utilityInstance = new LargeLadUtilityInstance( instanceId );
	}

	private int CreateStableInstanceId()
	{
		var hash = System.HashCode.Combine(
			GameObject.Id,
			Id,
			nameof( LargeLadUtilityId.Dodgeball ) ) & int.MaxValue;
		return hash == 0 ? 1 : hash;
	}

	private void ResolveAuthoredParts()
	{
		PickupRenderer ??= Components.Get<Renderer>(
			FindMode.EverythingInSelfAndDescendants );
		BallRigidbody ??= Components.Get<Rigidbody>(
			FindMode.EverythingInSelfAndDescendants );

		foreach ( var collider in Components.GetAll<Collider>(
			FindMode.EverythingInSelfAndDescendants ) )
		{
			if ( collider is null )
				continue;

			if ( collider.IsTrigger )
				PickupCollider ??= collider;
			else
				BallCollider ??= collider;
		}
	}

	private void ConfigurePhysics()
	{
		if ( PickupCollider is not null )
			PickupCollider.IsTrigger = true;

		if ( BallCollider is not null )
			BallCollider.IsTrigger = false;

		if ( BallRigidbody is not null )
		{
			BallRigidbody.EnableImpactDamage = false;
			BallRigidbody.CollisionEventsEnabled = true;
			BallRigidbody.EnhancedCcd = true;
		}

		GameObject.Tags.Add( LargeLadDodgeballRules.CollisionTag );
	}

	private void ConfigureNetworkAuthority()
	{
		if ( !GameObject.Network.Active )
			return;

		GameObject.Network.Interpolation = true;
		GameObject.Network.SetOrphanedMode( NetworkOrphaned.Host );
		GameObject.Network.SetOwnerTransfer( OwnerTransfer.Fixed );
	}

	private void ConfigureLocalSimulationAuthority()
	{
		if ( !GameObject.Network.Active || !GameObject.Network.IsProxy )
			return;

		// A proxy only presents the host's transform. Its colliders and body motion
		// must not simulate another copy of the host-authoritative loose ball.
		if ( BallCollider is not null )
			BallCollider.Enabled = false;

		if ( PickupCollider is not null )
			PickupCollider.Enabled = false;

		var body = BallRigidbody?.PhysicsBody;

		if ( body is not null )
		{
			body.ClearForces();
			body.ClearTorque();
			body.Velocity = Vector3.Zero;
			body.AngularVelocity = Vector3.Zero;
			body.MotionEnabled = false;
			body.Sleeping = true;
		}

		if ( BallRigidbody is not null )
			BallRigidbody.Enabled = false;
	}

	private void TransferOwnershipToCarrier(
		LargeLadNativeInventory carrier )
	{
		if ( !GameObject.Network.Active )
			return;

		var owner = carrier?.GameObject?.Network.Owner;

		if ( owner is not null )
			GameObject.Network.AssignOwnership( owner );
	}

	private void ReclaimHostSimulation()
	{
		if ( GameObject.Network.Active && GameObject.Network.IsProxy )
			GameObject.Network.DropOwnership();
	}

	private void ActivatePhysics( Vector3 velocity )
	{
		var body = BallRigidbody?.PhysicsBody;

		if ( body is null )
			return;

		body.BodyType = PhysicsBodyType.Dynamic;
		body.MotionEnabled = true;
		body.Sleeping = false;
		body.Velocity = LargeLadDodgeballRules.ClampVelocity(
			velocity,
			MaximumLinearSpeed );
		body.AngularVelocity = Vector3.Zero;
	}

	internal void ClampPhysicsVelocityNow()
	{
		var body = BallRigidbody?.PhysicsBody;

		if ( body is null )
			return;

		body.Velocity = LargeLadDodgeballRules.ClampVelocity(
			body.Velocity,
			MaximumLinearSpeed );
		var angularSpeed = body.AngularVelocity.Length;
		var clampedAngularSpeed =
			LargeLadDodgeballRules.ClampAngularSpeed(
				angularSpeed,
				MaximumAngularSpeed );

		if ( angularSpeed > clampedAngularSpeed && angularSpeed > 0.0001f )
		{
			body.AngularVelocity = body.AngularVelocity.Normal *
				clampedAngularSpeed;
		}
	}

	private void RestoreAuthoredTransform()
	{
		if ( !hasAuthoredTransform )
			return;

		GameObject.WorldTransform = authoredTransform;
		GameObject.Network.ClearInterpolation();
		StopPhysicsMotion();
	}

	private void StopPhysicsMotion()
	{
		var body = BallRigidbody?.PhysicsBody;

		if ( body is null )
			return;

		body.ClearForces();
		body.ClearTorque();
		body.Velocity = Vector3.Zero;
		body.AngularVelocity = Vector3.Zero;
	}

	private void ResetTransientThrowState()
	{
		ActiveThrowSequence = 0;
		impactGate.Reset();
		thrower = null;
		throwerRole = LargeLadRole.Unassigned;
		throwerPositionAtLaunch = Vector3.Zero;
		pickupUnlockTime = 0.0f;
		flightExpireTime = 0.0f;
	}

	private void SetWorldState( bool available, bool pickupEnabled )
	{
		Available = available;
		PickupEnabled = available && pickupEnabled;
		ApplyAvailableState();
	}

	private void SetPickupEnabled( bool enabled )
	{
		PickupEnabled = Available && enabled;
		ApplyAvailableState();
	}

	private void OnAvailableChanged( bool oldValue, bool newValue )
	{
		ApplyAvailableState();
	}

	private void OnPickupEnabledChanged( bool oldValue, bool newValue )
	{
		ApplyAvailableState();
	}

	private void ApplyAvailableState()
	{
		var presentLocally = Available && !clientAuthoredCopySuppressed;
		var simulateLocally = presentLocally &&
			( !GameObject.Network.Active || !GameObject.Network.IsProxy );

		if ( PickupRenderer is not null )
			PickupRenderer.Enabled = presentLocally;

		if ( BallCollider is not null )
			BallCollider.Enabled = simulateLocally;

		if ( BallRigidbody is not null )
			BallRigidbody.Enabled = simulateLocally;

		if ( PickupCollider is not null )
			PickupCollider.Enabled = simulateLocally && PickupEnabled;

		ConfigureLocalSimulationAuthority();
	}

	private void BroadcastPresentation(
		LargeLadDodgeballPresentationPhase phase,
		Vector3 position,
		Vector3 normal,
		LargeLadRole targetRole )
	{
		nextPresentationSequence++;
		var sequence = nextPresentationSequence;
		ReceivePresentation( phase, sequence, position, normal, targetRole );
		BroadcastPresentationRpc(
			phase,
			sequence,
			position,
			normal,
			targetRole );
	}

	[Rpc.Broadcast]
	private void BroadcastPresentationRpc(
		LargeLadDodgeballPresentationPhase phase,
		int sequence,
		Vector3 position,
		Vector3 normal,
		LargeLadRole targetRole )
	{
		if ( Networking.IsHost )
			return;

		ReceivePresentation( phase, sequence, position, normal, targetRole );
	}

	private void ReceivePresentation(
		LargeLadDodgeballPresentationPhase phase,
		int sequence,
		Vector3 position,
		Vector3 normal,
		LargeLadRole targetRole )
	{
		if ( sequence <= lastLocalPresentationSequence )
			return;

		lastLocalPresentationSequence = sequence;
		Presentation?.Invoke(
			new LargeLadDodgeballPresentation(
				phase,
				sequence,
				position,
				normal,
				targetRole ) );
	}

	private void ValidateConfiguration()
	{
		ValidateRange( nameof( ThrowSpeed ), ThrowSpeed, 0.0f,
			LargeLadDodgeballRules.MaximumThrowSpeed );
		ValidateRange( nameof( MaximumLinearSpeed ), MaximumLinearSpeed, 0.0f,
			LargeLadDodgeballRules.MaximumLinearSpeed );
		ValidateRange( nameof( MaximumAngularSpeed ), MaximumAngularSpeed, 0.0f,
			LargeLadDodgeballRules.MaximumAngularSpeed );
		ValidateRange( nameof( LargeLadDamage ), LargeLadDamage, 0.0f,
			LargeLadDodgeballRules.MaximumLargeLadDamage );
		ValidateRange(
			nameof( LargeLadHorizontalKnockbackImpulse ),
			LargeLadHorizontalKnockbackImpulse,
			0.0f,
			LargeLadDodgeballRules.MaximumHorizontalKnockbackImpulse );
		ValidateRange(
			nameof( LargeLadUpwardKnockbackImpulse ),
			LargeLadUpwardKnockbackImpulse,
			0.0f,
			LargeLadDodgeballRules.MaximumUpwardKnockbackImpulse );
	}

	private void ValidateRange(
		string propertyName,
		float value,
		float minimum,
		float maximum )
	{
		if ( !float.IsFinite( value ) || value < minimum || value > maximum )
		{
			Log.Warning(
				$"{GameObject.Name}: dodgeball {propertyName} must be " +
				$"{minimum} to {maximum}; runtime use is clamped." );
		}
	}

	private void DebugLog( string message )
	{
		if ( EnableDebugLogging ||
			LargeLadGameManager.FindForScene( Scene )?
				.EnablePickupAndRoundResetDebugLogging == true )
		{
			Log.Info( $"[Debug/Dodgeball] {message}" );
		}
	}

	protected override void DrawGizmos()
	{
		Gizmo.Draw.Color = LargeLadUtilityRules.DodgeballColor
			.WithAlpha( 0.7f );
		Gizmo.Draw.SolidSphere( BallCenterOffset, BallRadius );
	}
}

/// <summary>
/// Lives beside the authored trigger collider and forwards pickup attempts to
/// the single authoritative ball component on its parent network object.
/// </summary>
public sealed class LargeLadDodgeballPickupTrigger :
	Component,
	Component.ITriggerListener
{
	[Property]
	public LargeLadDodgeballPickup Dodgeball { get; set; }

	protected override void OnAwake()
	{
		ResolveDodgeball();
	}

	public void OnTriggerEnter( Collider other )
	{
		ResolveDodgeball();
		Dodgeball?.TryCollectFromTrigger( other );
	}

	public void OnTriggerExit( Collider other )
	{
	}

	private void ResolveDodgeball()
	{
		Dodgeball ??= Components.Get<LargeLadDodgeballPickup>(
			FindMode.EverythingInSelfAndAncestors );
	}
}
