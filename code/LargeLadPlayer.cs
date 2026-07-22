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
	[Sync( SyncFlags.FromHost ), Change( nameof( OnRoleChanged ) )]
	public LargeLadRole Role { get; set; } = LargeLadRole.Unassigned;

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
		ApplyRole( Role );
		ApplyMovementLock( MovementLocked );
	}

	private void OnRoleChanged( LargeLadRole oldRole, LargeLadRole newRole )
	{
		ApplyRole( newRole );
		Log.Info( $"{GameObject.Name} changed role from {oldRole} to {newRole}." );
	}

	private void OnMovementLockedChanged( bool oldValue, bool newValue )
	{
		ApplyMovementLock( newValue );
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

	private void ApplyMovementLock( bool isLocked )
	{
		if ( IsProxy )
			return;

		var controller = Components.Get<PlayerController>();

		if ( controller is null )
			return;

		controller.UseInputControls = !isLocked;

		if ( isLocked && controller.Body is not null )
		{
			controller.Body.Velocity = Vector3.Zero;
		}
	}

	[Rpc.Owner( NetFlags.HostOnly )]
	public void TeleportTo( Vector3 worldPosition, Rotation worldRotation )
	{
		GameObject.WorldPosition = worldPosition;
		GameObject.WorldRotation = worldRotation;

		var controller = Components.Get<PlayerController>();

		if ( controller?.Body is not null )
		{
			controller.Body.Velocity = Vector3.Zero;
		}

		GameObject.Network.ClearInterpolation();
	}
}
