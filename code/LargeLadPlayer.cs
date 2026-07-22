using Sandbox;

public enum LargeLadRole
{
	Unassigned,
	SkinnyKid,
	LargeLad,
	Minion
}

public enum LargeLadWeaponType
{
	None,
	Melee,
	PrototypeGun
}

public sealed class LargeLadPlayer : Component
{
	private const int TeleportSettleFrames = 2;

	private Vector3 pendingTeleportPosition;
	private Rotation pendingTeleportRotation;
	private int pendingTeleportFrames;

	[Property, RequireComponent]
	public LargeLadHealth Health { get; set; }

	[Property, RequireComponent]
	public LargeLadPrototypeWeapon PrototypeWeapon { get; set; }

	[Property, RequireComponent]
	public LargeLadMeleeAttack MeleeAttack { get; set; }

	[Sync( SyncFlags.FromHost ), Change( nameof( OnRoleChanged ) )]
	public LargeLadRole Role { get; set; } = LargeLadRole.Unassigned;

	[Sync( SyncFlags.FromHost )]
	public LargeLadWeaponType EquippedWeapon { get; private set; } =
		LargeLadWeaponType.None;

	[Sync( SyncFlags.FromHost ), Change( nameof( OnMovementLockedChanged ) )]
	public bool MovementLocked { get; set; }

	[Property]
	public SkinnedModelRenderer BodyRenderer { get; set; }

	[Property, Title( "Skinny Kid Tint" )]
	public Color RunnerTint { get; set; } = Color.White;

	[Property]
	public Color LargeLadTint { get; set; } = new( 1.0f, 0.25f, 0.05f );

	[Property]
	public Color MinionTint { get; set; } = new( 0.55f, 0.15f, 0.75f );

	[Property, Title( "Skinny Kid Walk Speed" )]
	public float RunnerWalkSpeed { get; set; } = 110.0f;

	[Property, Title( "Skinny Kid Run Speed" )]
	public float RunnerRunSpeed { get; set; } = 320.0f;

	[Property]
	public float LargeLadWalkSpeed { get; set; } = 85.0f;

	[Property]
	public float LargeLadRunSpeed { get; set; } = 230.0f;

	[Property]
	public float MinionWalkSpeed { get; set; } = 110.0f;

	[Property]
	public float MinionRunSpeed { get; set; } = 300.0f;

	protected override void OnStart()
	{
		if ( Networking.IsHost )
		{
			EquipDefaultWeaponForRole( Role );
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

	private void OnRoleChanged( LargeLadRole oldRole, LargeLadRole newRole )
	{
		if ( Networking.IsHost )
		{
			EquipDefaultWeaponForRole( newRole );
		}

		ApplyRole( newRole );
		Log.Info( $"{GameObject.Name} changed role from {oldRole} to {newRole}." );
	}

	public void EquipWeapon( LargeLadWeaponType weapon )
	{
		if ( !Networking.IsHost )
			return;

		EquippedWeapon = weapon;
	}

	private void EquipDefaultWeaponForRole( LargeLadRole role )
	{
		EquipWeapon( role switch
		{
			LargeLadRole.SkinnyKid => LargeLadWeaponType.PrototypeGun,
			LargeLadRole.LargeLad => LargeLadWeaponType.Melee,
			LargeLadRole.Minion => LargeLadWeaponType.Melee,
			_ => LargeLadWeaponType.None
		} );
	}

	private void OnMovementLockedChanged( bool oldValue, bool newValue )
	{
		RefreshMovementState();
	}

	private void ApplyRole( LargeLadRole role )
	{
		if ( BodyRenderer is not null )
		{
			BodyRenderer.Tint = role switch
			{
				LargeLadRole.LargeLad => LargeLadTint,
				LargeLadRole.Minion => MinionTint,
				_ => RunnerTint
			};
		}

		if ( !IsProxy )
		{
			ApplyLocalMovementSettings( role );
		}

	}

	private void ApplyLocalMovementSettings( LargeLadRole role )
	{
		var controller = Components.Get<PlayerController>();

		if ( controller is null )
			return;

		( controller.WalkSpeed, controller.RunSpeed ) = role switch
		{
			LargeLadRole.LargeLad => ( LargeLadWalkSpeed, LargeLadRunSpeed ),
			LargeLadRole.Minion => ( MinionWalkSpeed, MinionRunSpeed ),
			_ => ( RunnerWalkSpeed, RunnerRunSpeed )
		};
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
