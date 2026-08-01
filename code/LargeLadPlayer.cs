using Sandbox;

public enum LargeLadRole
{
	Unassigned,
	SkinnyKid,
	LargeLad,
	Minion
}

public enum LargeLadEatParticipation
{
	None,
	Attacker,
	Victim
}

public sealed class LargeLadPlayer : Component, IScenePhysicsEvents
{
	private const int TeleportSettleFrames = 2;
	private const float KillVolumeTeleportGrace = 0.5f;

	private Scene registeredScene;
	private Vector3 pendingTeleportPosition;
	private Rotation pendingTeleportRotation;
	private int pendingTeleportFrames;
	private TimeSince timeSinceAuthoritativeTeleport;
	private bool hasAuthoritativeTeleport;
	private Vector3 pendingSoftSeparationDisplacement;
	private LargeLadEatAttack eatClaimOwner;
	private float groundSlamStaggerEndsAt;

	[Property, RequireComponent]
	public LargeLadHealth Health { get; set; }

	[Property, RequireComponent]
	public LargeLadPrototypeWeapon PrototypeWeapon { get; set; }

	[Property, RequireComponent]
	public LargeLadInventory Inventory { get; set; }

	[Property, RequireComponent]
	public LargeLadMeleeCombat MeleeCombat { get; set; }

	[Property, RequireComponent]
	public LargeLadMeleePresentation MeleePresentation { get; set; }

	[Property, RequireComponent]
	public LargeLadEatAttack EatAttack { get; set; }

	[Property, RequireComponent]
	public LargeLadGroundSlam GroundSlam { get; set; }

	[Property, RequireComponent]
	public LargeLadGroundSlamPresentationController GroundSlamPresentation
	{
		get;
		set;
	}

	[Property]
	public LargeLadRoleProfiles RoleProfiles { get; set; }

	[Sync( SyncFlags.FromHost ), Change( nameof( OnRoleChanged ) )]
	public LargeLadRole Role { get; private set; } = LargeLadRole.Unassigned;

	[Sync( SyncFlags.FromHost )]
	public LargeLadRole PendingRespawnRole { get; private set; } =
		LargeLadRole.Unassigned;

	public LargeLadWeaponId EquippedWeapon =>
		Role switch
		{
			LargeLadRole.LargeLad or LargeLadRole.Minion =>
				LargeLadWeaponId.Melee,
			LargeLadRole.SkinnyKid =>
				Inventory?.EquippedWeapon ?? LargeLadWeaponId.None,
			_ => LargeLadWeaponId.None
		};

	[Sync( SyncFlags.FromHost ), Change( nameof( OnMovementLockedChanged ) )]
	public bool MovementLocked { get; set; }

	[Sync( SyncFlags.FromHost ), Change( nameof( OnEatMovementMultiplierChanged ) )]
	public float EatMovementMultiplier { get; private set; } = 1.0f;

	[Sync( SyncFlags.FromHost )]
	public LargeLadEatParticipation EatParticipation { get; private set; }

	public bool IsEatBusy =>
		EatParticipation != LargeLadEatParticipation.None;

	public bool IsGroundSlamBusy => GroundSlam?.IsWindingUp == true;

	[Property, Hide]
	[Sync( SyncFlags.FromHost ),
		Change( nameof( OnGroundSlamStaggeredChanged ) )]
	public bool IsGroundSlamStaggered { get; private set; }

	[Property]
	public SkinnedModelRenderer BodyRenderer { get; set; }

	protected override void OnEnabled()
	{
		base.OnEnabled();
		ApplyRoleCollision( Role );
		RegisterWithGameManager();
	}

	protected override void OnDisabled()
	{
		CancelEatParticipationForLifecycle();
		CancelGroundSlamStagger();
		pendingSoftSeparationDisplacement = Vector3.Zero;
		UnregisterFromGameManager();
		base.OnDisabled();
	}

	protected override void OnDestroy()
	{
		CancelEatParticipationForLifecycle();
		CancelGroundSlamStagger();
		UnregisterFromGameManager();
		base.OnDestroy();
	}

	protected override void OnStart()
	{
		LogRoleProfileWarnings();
		ApplyRoleProfile( Role );
		RefreshMovementState();
	}

	protected override void OnUpdate()
	{
		if ( Networking.IsHost &&
			IsGroundSlamStaggered &&
			Time.Now >= groundSlamStaggerEndsAt )
		{
			IsGroundSlamStaggered = false;
		}
	}

	protected override void OnRefresh()
	{
		base.OnRefresh();
		ApplyRoleCollision( Role );
	}

	protected override void OnFixedUpdate()
	{
		if ( IsProxy )
			return;

		if ( pendingTeleportFrames > 0 )
		{
			ApplyPendingTeleport();
			pendingTeleportFrames--;

			if ( pendingTeleportFrames == 0 )
			{
				RefreshMovementState();
			}

			return;
		}

		if ( MovementLocked || Health?.IsDead == true )
		{
			StopMovement();
			return;
		}

		if ( IsGroundSlamStaggered )
		{
			SuppressMovementForGroundSlam();
			return;
		}

		if ( EatParticipation == LargeLadEatParticipation.Victim )
			DampenMovementForEat();

	}

	void IScenePhysicsEvents.PostPhysicsStep()
	{
		if ( IsProxy ||
			pendingTeleportFrames > 0 ||
			MovementLocked ||
			Health?.IsDead == true )
		{
			pendingSoftSeparationDisplacement = Vector3.Zero;
			return;
		}

		// Normal physics has finished. Apply the planar displacement captured
		// before simulation so every pair uses the same physics-step snapshot.
		ApplySoftPlayerSeparation(
			pendingSoftSeparationDisplacement );
		pendingSoftSeparationDisplacement = Vector3.Zero;
	}

	void IScenePhysicsEvents.PrePhysicsStep()
	{
		pendingSoftSeparationDisplacement = Vector3.Zero;

		if ( IsProxy ||
			pendingTeleportFrames > 0 ||
			MovementLocked ||
			Health?.IsDead == true )
		{
			return;
		}

		pendingSoftSeparationDisplacement =
			CalculateSoftPlayerSeparation();
	}

	protected override void OnValidate()
	{
		LogRoleProfileWarnings();
	}

	private void OnRoleChanged( LargeLadRole oldRole, LargeLadRole newRole )
	{
		if ( Networking.IsHost && oldRole != newRole )
		{
			CancelEatParticipationForLifecycle();
			CancelGroundSlamStagger();
		}

		// The synchronized role is sufficient to update presentation on every
		// observer and movement settings on whichever peer owns this player.
		ApplyRoleProfile( newRole );
		LargeLadSceneRegistry.NotifyPlayerRoleChanged(
			registeredScene,
			this,
			oldRole,
			newRole );

		if ( LargeLadGameManager.FindForScene( Scene )?
			.EnablePlayerLifecycleDebugLogging == true )
		{
			Log.Info(
				$"[Debug/Player Lifecycle] {GameObject.Name} changed role " +
				$"from {oldRole} to {newRole}." );
		}
	}

	private void OnMovementLockedChanged( bool oldValue, bool newValue )
	{
		RefreshMovementState();
	}

	private void OnGroundSlamStaggeredChanged(
		bool oldValue,
		bool newValue )
	{
		RefreshMovementState();

		if ( !IsProxy && GroundSlam?.EnableDebugLogging == true )
		{
			Log.Info(
				$"[Debug/Ground Slam] {GameObject.Name} owner stagger state " +
				$"changed from {oldValue} to {newValue}." );
		}
	}

	private void OnEatMovementMultiplierChanged(
		float oldValue,
		float newValue )
	{
		if ( TryGetRoleProfile( Role, out var profile ) && !IsProxy )
			ApplyLocalMovementSettings( profile );
	}

	internal bool TryBeginEatParticipation(
		LargeLadEatAttack owner,
		LargeLadEatParticipation participation,
		float movementMultiplier = 1.0f )
	{
		if ( !Networking.IsHost ||
			owner is null ||
			!owner.IsValid ||
			participation == LargeLadEatParticipation.None ||
			eatClaimOwner is not null ||
			EatParticipation != LargeLadEatParticipation.None )
		{
			return false;
		}

		if ( participation == LargeLadEatParticipation.Attacker &&
			Role != LargeLadRole.LargeLad )
		{
			return false;
		}

		if ( participation == LargeLadEatParticipation.Victim &&
			(Role != LargeLadRole.SkinnyKid ||
				Health?.IsDead != false ||
				Health.CurrentHealth <= 0.0f) )
		{
			return false;
		}

		eatClaimOwner = owner;
		EatParticipation = participation;
		EatMovementMultiplier = participation ==
			LargeLadEatParticipation.Victim
			? System.Math.Clamp( movementMultiplier, 0.0f, 1.0f )
			: 1.0f;
		Inventory?.CancelConflictingActionForEat();

		if ( TryGetRoleProfile( Role, out var profile ) && !IsProxy )
			ApplyLocalMovementSettings( profile );

		if ( participation == LargeLadEatParticipation.Victim )
			DampenMovementForEat();

		return true;
	}

	internal bool IsEatParticipationOwnedBy(
		LargeLadEatAttack owner,
		LargeLadEatParticipation participation )
	{
		return owner is not null &&
			eatClaimOwner == owner &&
			EatParticipation == participation;
	}

	internal void ReleaseEatParticipation( LargeLadEatAttack owner )
	{
		if ( !Networking.IsHost || owner is null || eatClaimOwner != owner )
			return;

		eatClaimOwner = null;
		EatParticipation = LargeLadEatParticipation.None;
		EatMovementMultiplier = 1.0f;

		if ( TryGetRoleProfile( Role, out var profile ) && !IsProxy )
			ApplyLocalMovementSettings( profile );
	}

	internal void CancelEatParticipationForLifecycle()
	{
		if ( !Networking.IsHost )
			return;

		var owner = eatClaimOwner;

		if ( owner is not null && owner.IsValid )
		{
			owner.CancelFromParticipantLifecycle( this );
			return;
		}

		// Defensive repair for an owner destroyed before its callback could run.
		eatClaimOwner = null;
		EatParticipation = LargeLadEatParticipation.None;
		EatMovementMultiplier = 1.0f;
	}

	internal void SetPendingRespawnRole( LargeLadRole role )
	{
		if ( !Networking.IsHost )
			return;

		PendingRespawnRole = role;
	}

	public bool HasKillVolumeTeleportGrace =>
		Networking.IsHost &&
		hasAuthoritativeTeleport &&
		timeSinceAuthoritativeTeleport < KillVolumeTeleportGrace;

	/// <summary>
	/// Applies one complete authoritative player spawn. This is the only host
	/// lifecycle boundary that assigns a role, rebuilds its loadout, restores
	/// life and presentation, and moves the player to a spawn location.
	/// </summary>
	public bool RespawnAs(
		LargeLadRole role,
		LargeLadSpawnLocation spawn,
		bool keepMovementLocked = false )
	{
		if ( !Networking.IsHost )
			return false;

		CancelEatParticipationForLifecycle();
		CancelGroundSlamStagger();

		// Hold the controller still while health restores the live collider and
		// visuals. Teleport settling keeps motion disabled after the final lock
		// value is applied below.
		MovementLocked = true;
		PendingRespawnRole = LargeLadRole.Unassigned;
		var roleChanged = Role != role;
		Role = role;

		// A synchronized property callback does not run for same-role respawns,
		// so only that case needs an explicit local and client reapplication.
		if ( !roleChanged )
		{
			ApplyRoleProfile( role );
			BroadcastSameRoleRespawnProfile( role );
		}

		Inventory?.PrepareForRole( role );
		Health?.ResetForCurrentRole();

		hasAuthoritativeTeleport = true;
		timeSinceAuthoritativeTeleport = 0.0f;
		ApplyRespawnTeleport( spawn.Position, spawn.Rotation );

		MovementLocked = keepMovementLocked;
		return true;
	}

	[Rpc.Broadcast]
	private void BroadcastSameRoleRespawnProfile( LargeLadRole role )
	{
		// RespawnAs already applied the authoritative copy. Observers need this
		// only when the synchronized role value itself did not change.
		if ( Networking.IsHost )
			return;

		ApplyRoleProfile( role );
	}

	private void ApplyRoleProfile( LargeLadRole role )
	{
		ApplyRoleCollision( role );

		if ( !TryGetRoleProfile( role, out var profile ) )
			return;

		if ( BodyRenderer is not null )
		{
			BodyRenderer.Tint = profile.BodyTint;

			// Scale only the rendered body hierarchy. Scaling the networked player
			// root would also distort movement, camera offsets, and physics.
			BodyRenderer.GameObject.LocalScale = profile.BodyVisualScale;
		}

		if ( !IsProxy )
		{
			ApplyLocalMovementSettings( profile );
		}

	}

	private void ApplyRoleCollision( LargeLadRole role )
	{
		var roleTag =
			LargeLadGameplayRules.GetPlayerBodyCollisionTag( role );
		var supplementaryRoleTag =
			LargeLadGameplayRules.GetSupplementaryRoleCollisionTag( role );

		// PlayerController movement traces use the root object's tags, while
		// rigid-body contact uses ColliderObject.Tags. Keep both in sync so a
		// filtered player cannot be mistaken for ground or a step.
		GameObject.Tags.Remove( LargeLadGameplayRules.HunterBodyTag );
		GameObject.Tags.Remove( LargeLadGameplayRules.SoftPlayerBodyTag );
		GameObject.Tags.Remove( LargeLadGameplayRules.MinionBodyTag );
		GameObject.Tags.Add( LargeLadGameplayRules.PlayerBodyTag );
		GameObject.Tags.Add( roleTag );

		if ( supplementaryRoleTag is not null )
			GameObject.Tags.Add( supplementaryRoleTag );

		var controller = Components.Get<PlayerController>();

		if ( controller is null )
			return;

		// PlayerController copies BodyCollisionTags onto ColliderObject when it
		// creates the body. Role changes happen after that initialization, so
		// update both the retained setting and the already-live collider tags.
		// This changes only contact filtering: the dynamic Rigidbody remains
		// available for explicit impulses such as Ground Slam.
		var tags = new TagSet();
		tags.Add( LargeLadGameplayRules.PlayerBodyTag );
		tags.Add( roleTag );

		if ( supplementaryRoleTag is not null )
			tags.Add( supplementaryRoleTag );

		controller.BodyCollisionTags = tags;
		controller.CameraCollisionIgnore ??= new TagSet();
		controller.CameraCollisionIgnore.Add(
			LargeLadGameplayRules.PlayerBodyTag );

		if ( controller.ColliderObject is not null &&
			controller.ColliderObject.IsValid )
		{
			controller.ColliderObject.Tags.SetFrom( tags );
		}
	}

	private Vector3 CalculateSoftPlayerSeparation()
	{
		var controller = Components.Get<PlayerController>();

		if ( controller?.Body is null )
			return Vector3.Zero;

		var targetVelocity = Vector3.Zero;

		if ( !LargeLadGameplayRules.IsHunterRole( Role ) )
		{
			var gameManager =
				LargeLadGameManager.FindForScene( Scene );

			if ( gameManager is not null )
			{
				foreach ( var other in gameManager.ActivePlayers )
				{
					if ( other == this ||
						LargeLadGameplayRules.IsHunterRole( other.Role ) ||
						other.Health?.IsDead == true )
					{
						continue;
					}

					targetVelocity +=
						LargeLadGameplayRules
							.GetSoftPlayerSeparationVelocity(
								GameObject.WorldPosition,
								other.GameObject.WorldPosition,
								GameObject.Id.CompareTo(
									other.GameObject.Id ) >= 0 );
				}
			}
		}

		var result =
			LargeLadGameplayRules.ResolveSoftPlayerSeparation(
				controller.Body.Velocity,
				targetVelocity,
				Time.Delta );
		return result.Displacement;
	}

	private void ApplySoftPlayerSeparation( Vector3 displacement )
	{
		if ( displacement.LengthSquared <= 0.0001f )
			return;

		var controller = Components.Get<PlayerController>();

		if ( controller?.Body is null )
			return;

		var start = controller.Body.WorldPosition;
		var trace = controller.TraceBody(
			start,
			start + displacement );

		if ( !trace.StartedSolid )
			controller.Body.WorldPosition = trace.EndPosition;
	}

	public bool TryGetRoleProfile(
		LargeLadRole role,
		out LargeLadRoleProfile profile )
	{
		if ( RoleProfiles is not null )
			return RoleProfiles.TryGetProfile( role, out profile );

		profile = null;
		return false;
	}

	private void ApplyLocalMovementSettings( LargeLadRoleProfile profile )
	{
		var controller = Components.Get<PlayerController>();

		if ( controller is null )
			return;

		var eatMultiplier = System.Math.Clamp(
			EatMovementMultiplier,
			0.0f,
			1.0f );
		controller.WalkSpeed = profile.WalkSpeed * eatMultiplier;
		controller.RunSpeed = profile.RunSpeed * eatMultiplier;
	}

	private void LogRoleProfileWarnings()
	{
		if ( RoleProfiles is null )
		{
			Log.Warning(
				$"{GameObject.Name}: Large Lad role profiles are missing; " +
				"movement, health, visuals, and melee have no role configuration." );
			return;
		}

		foreach ( var warning in RoleProfiles.GetValidationWarnings() )
		{
			Log.Warning(
				$"{GameObject.Name}: invalid Large Lad role profiles: {warning}" );
		}
	}

	public void RefreshMovementState()
	{
		var controller = Components.Get<PlayerController>();

		if ( controller is null )
			return;

		if ( IsProxy )
			return;

		var isHardLocked =
			MovementLocked ||
			Health?.IsDead == true;
		controller.UseInputControls =
			!isHardLocked && !IsGroundSlamStaggered;

		if ( controller.Body is null )
			return;

		if ( isHardLocked || pendingTeleportFrames > 0 )
		{
			StopMovement();
			controller.Body.MotionEnabled = false;
			return;
		}

		controller.Body.MotionEnabled = true;
	}

	internal void ApplyGroundSlamEffect(
		Vector3 impulse,
		float staggerDuration )
	{
		if ( !Networking.IsHost ||
			Role is not (LargeLadRole.SkinnyKid or LargeLadRole.Minion) ||
			Health?.IsDead != false ||
			Health.CurrentHealth <= 0.0f )
		{
			return;
		}

		if ( Role == LargeLadRole.SkinnyKid && staggerDuration > 0.0f )
		{
			groundSlamStaggerEndsAt = System.MathF.Max(
				groundSlamStaggerEndsAt,
				Time.Now + staggerDuration );
			IsGroundSlamStaggered = true;
		}

		ReceiveGroundSlamImpulse( impulse, staggerDuration );
	}

	private void CancelGroundSlamStagger()
	{
		if ( !Networking.IsHost )
			return;

		groundSlamStaggerEndsAt = 0.0f;
		IsGroundSlamStaggered = false;
	}

	[Rpc.Owner( NetFlags.HostOnly )]
	private void ReceiveGroundSlamImpulse(
		Vector3 impulse,
		float staggerDuration )
	{
		if ( Role is not (LargeLadRole.SkinnyKid or LargeLadRole.Minion) ||
			Health?.IsDead != false )
		{
			return;
		}

		var controller = Components.Get<PlayerController>();

		if ( controller?.Body is null )
			return;

		// PlayerController writes its managed velocity back to the Rigidbody as
		// part of movement simulation. Applying only a Rigidbody impulse here can
		// be overwritten by that write in the same frame. Convert the configured
		// physical impulse into a velocity delta and inject it through Jump, the
		// controller's supported method for adding directional velocity.
		var bodyMass = System.MathF.Max( 0.001f, controller.Body.Mass );
		var velocityDelta = impulse / bodyMass;

		controller.Body.MotionEnabled = true;
		controller.Body.Sleeping = false;

		if ( velocityDelta.z > 0.0f )
		{
			controller.PreventGrounding(
				System.MathF.Max(
					0.1f,
					System.MathF.Min( 0.25f, staggerDuration ) ) );
		}

		controller.Jump( velocityDelta );

		if ( GroundSlam?.EnableDebugLogging == true )
		{
			Log.Info(
				$"[Debug/Ground Slam] {GameObject.Name} owner received " +
				$"impulse {impulse} as velocity delta {velocityDelta}; " +
				$"new controller velocity is {controller.Velocity}." );
		}
	}

	[Rpc.Owner( NetFlags.HostOnly )]
	private void ApplyRespawnTeleport(
		Vector3 worldPosition,
		Rotation worldRotation )
	{
		pendingTeleportPosition = worldPosition;
		pendingTeleportRotation = worldRotation;
		pendingTeleportFrames = TeleportSettleFrames;

		var controller = Components.Get<PlayerController>();

		if ( controller?.Body is not null )
		{
			controller.Body.MotionEnabled = false;
		}

		ApplyPendingTeleport();
	}

	private void ApplyPendingTeleport()
	{
		StopMovement();

		GameObject.WorldPosition = pendingTeleportPosition;
		GameObject.WorldRotation = pendingTeleportRotation;
		GameObject.Network.ClearInterpolation();
	}

	private void StopMovement()
	{
		pendingSoftSeparationDisplacement = Vector3.Zero;

		var controller = Components.Get<PlayerController>();

		if ( controller is null )
			return;

		controller.WishVelocity = Vector3.Zero;

		if ( controller.Body is null )
			return;

		controller.Body.ClearForces();
		controller.Body.Velocity = Vector3.Zero;
		controller.Body.AngularVelocity = Vector3.Zero;
	}

	private void SuppressMovementForGroundSlam()
	{
		var controller = Components.Get<PlayerController>();

		if ( controller is null )
			return;

		// Suppress authored movement input without clearing forces or velocity.
		// The explicit slam impulse must remain free to lift the player.
		controller.UseInputControls = false;
		controller.WishVelocity = Vector3.Zero;

		if ( controller.Body is not null )
			controller.Body.MotionEnabled = true;
	}

	private void DampenMovementForEat()
	{
		var controller = Components.Get<PlayerController>();

		if ( controller is null )
			return;

		var multiplier = System.Math.Clamp(
			EatMovementMultiplier,
			0.0f,
			1.0f );
		controller.WishVelocity *= multiplier;

		if ( controller.Body is null )
			return;

		var velocity = controller.Body.Velocity;
		velocity.x *= multiplier;
		velocity.y *= multiplier;
		controller.Body.Velocity = velocity;
	}

	private void RegisterWithGameManager()
	{
		if ( registeredScene is not null && registeredScene != Scene )
		{
			LargeLadSceneRegistry.UnregisterPlayer( registeredScene, this );
		}

		registeredScene = Scene;
		LargeLadSceneRegistry.RegisterPlayer( registeredScene, this );
	}

	private void UnregisterFromGameManager()
	{
		if ( registeredScene is null )
			return;

		LargeLadSceneRegistry.UnregisterPlayer( registeredScene, this );
		registeredScene = null;
	}
}
