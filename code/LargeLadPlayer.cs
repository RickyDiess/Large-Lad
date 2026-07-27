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
	public LargeLadMeleeSystem MeleeSystem { get; set; }

	[Property]
	public LargeLadRoleProfiles RoleProfiles { get; set; }

	[Sync( SyncFlags.FromHost ), Change( nameof( OnRoleChanged ) )]
	public LargeLadRole Role { get; set; } = LargeLadRole.Unassigned;

	[Sync( SyncFlags.FromHost )]
	public LargeLadRole PendingRespawnRole { get; private set; } =
		LargeLadRole.Unassigned;

	public LargeLadWeaponId EquippedWeapon =>
		Inventory?.EquippedWeapon ?? LargeLadWeaponId.None;

	[Sync( SyncFlags.FromHost ), Change( nameof( OnMovementLockedChanged ) )]
	public bool MovementLocked { get; set; }

	[Property]
	public SkinnedModelRenderer BodyRenderer { get; set; }

	protected override void OnStart()
	{
		LogRoleProfileWarnings();

		if ( Networking.IsHost )
		{
			Inventory?.PrepareForRole( Role );
		}

		ApplyRole( Role );
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
		if ( Networking.IsHost )
		{
			Inventory?.PrepareForRole( newRole );
		}

		ApplyRole( newRole );
		Log.Info( $"{GameObject.Name} changed role from {oldRole} to {newRole}." );
	}

	private void OnMovementLockedChanged( bool oldValue, bool newValue )
	{
		RefreshMovementState();
	}

	public void SetPendingRespawnRole( LargeLadRole role )
	{
		if ( !Networking.IsHost )
			return;

		PendingRespawnRole = role;
	}

	public LargeLadRole ApplyPendingRespawnRole()
	{
		if ( !Networking.IsHost )
			return Role;

		var role = PendingRespawnRole;
		PendingRespawnRole = LargeLadRole.Unassigned;

		if ( role != LargeLadRole.Unassigned )
		{
			Role = role;
		}

		return Role;
	}

	public void ClearPendingRespawnRole()
	{
		if ( Networking.IsHost )
		{
			PendingRespawnRole = LargeLadRole.Unassigned;
		}
	}

	public bool HasKillVolumeTeleportGrace =>
		Networking.IsHost &&
		hasAuthoritativeTeleport &&
		timeSinceAuthoritativeTeleport < KillVolumeTeleportGrace;

	public void BeginAuthoritativeTeleport()
	{
		if ( !Networking.IsHost )
			return;

		hasAuthoritativeTeleport = true;
		timeSinceAuthoritativeTeleport = 0.0f;
	}

	private void ApplyRole( LargeLadRole role )
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
	public void TeleportTo( Vector3 worldPosition, Rotation worldRotation )
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
}
