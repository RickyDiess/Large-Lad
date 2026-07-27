using Sandbox;

public enum LargeLadRole
{
	Unassigned,
	SkinnyKid,
	LargeLad,
	Minion
}

public sealed class LargeLadPlayer : Component
{
	private const int TeleportSettleFrames = 2;
	private const float KillVolumeTeleportGrace = 0.5f;

	private Scene registeredScene;
	private Vector3 pendingTeleportPosition;
	private Rotation pendingTeleportRotation;
	private int pendingTeleportFrames;
	private TimeSince timeSinceAuthoritativeTeleport;
	private bool hasAuthoritativeTeleport;

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

	[Property]
	public LargeLadRoleProfiles RoleProfiles { get; set; }

	[Sync( SyncFlags.FromHost ), Change( nameof( OnRoleChanged ) )]
	public LargeLadRole Role { get; private set; } = LargeLadRole.Unassigned;

	[Sync( SyncFlags.FromHost )]
	public LargeLadRole PendingRespawnRole { get; private set; } =
		LargeLadRole.Unassigned;

	public LargeLadWeaponId EquippedWeapon =>
		Inventory?.EquippedWeapon ?? LargeLadWeaponId.None;

	[Sync( SyncFlags.FromHost ), Change( nameof( OnMovementLockedChanged ) )]
	public bool MovementLocked { get; set; }

	[Property]
	public SkinnedModelRenderer BodyRenderer { get; set; }

	protected override void OnEnabled()
	{
		base.OnEnabled();
		RegisterWithGameManager();
	}

	protected override void OnDisabled()
	{
		UnregisterFromGameManager();
		base.OnDisabled();
	}

	protected override void OnDestroy()
	{
		UnregisterFromGameManager();
		base.OnDestroy();
	}

	protected override void OnStart()
	{
		LogRoleProfileWarnings();
		ApplyRoleProfile( Role );
		RefreshMovementState();
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
		}
	}

	protected override void OnValidate()
	{
		LogRoleProfileWarnings();
	}

	private void OnRoleChanged( LargeLadRole oldRole, LargeLadRole newRole )
	{
		// The synchronized role is sufficient to update presentation on every
		// observer and movement settings on whichever peer owns this player.
		ApplyRoleProfile( newRole );
		LargeLadSceneRegistry.NotifyPlayerRoleChanged(
			registeredScene,
			this,
			oldRole,
			newRole );
		Log.Info( $"{GameObject.Name} changed role from {oldRole} to {newRole}." );
	}

	private void OnMovementLockedChanged( bool oldValue, bool newValue )
	{
		RefreshMovementState();
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

		controller.WalkSpeed = profile.WalkSpeed;
		controller.RunSpeed = profile.RunSpeed;
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
		if ( IsProxy )
			return;

		var controller = Components.Get<PlayerController>();

		if ( controller is null )
			return;

		var isLocked = MovementLocked || Health?.IsDead == true;
		controller.UseInputControls = !isLocked;

		if ( controller.Body is null )
			return;

		if ( isLocked || pendingTeleportFrames > 0 )
		{
			StopMovement();
			controller.Body.MotionEnabled = false;
			return;
		}

		controller.Body.MotionEnabled = true;
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
