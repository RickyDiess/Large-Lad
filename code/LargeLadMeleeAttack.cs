using Sandbox;
using System.Linq;

public sealed class LargeLadMeleeAttack : Component
{
	[Property]
	public float LargeLadRange { get; set; } = 100.0f;

	[Property]
	public float MinionRange { get; set; } = 80.0f;

	[Property]
	public float LargeLadCooldown { get; set; } = 0.85f;

	[Property]
	public float MinionCooldown { get; set; } = 0.65f;

	[Property]
	public float MinionDamage { get; set; } = 25.0f;

	[Property, Title( "Minimum Facing Dot" )]
	public float MinimumFacingDot { get; set; } = 0.35f;

	private TimeSince timeSinceLocalSwing;
	private TimeSince timeSinceValidatedSwing;

	protected override void OnUpdate()
	{
		if ( IsProxy || !Input.Pressed( "Attack1" ) )
			return;

		var attacker = Components.Get<LargeLadPlayer>();
		var controller = Components.Get<PlayerController>();

		if ( !CanAttack( attacker, controller ) )
			return;

		var cooldown = GetCooldown( attacker.Role );

		if ( timeSinceLocalSwing < cooldown )
			return;

		timeSinceLocalSwing = 0.0f;

		var target = FindBestTarget( attacker, controller );

		if ( target is null )
		{
			Log.Info( $"{attacker.GameObject.Name} swung and missed." );
			return;
		}

		RequestMeleeAttack( target.GameObject );
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	public void RequestMeleeAttack( GameObject targetObject )
	{
		if ( !Networking.IsHost )
			return;

		var attacker = Components.Get<LargeLadPlayer>();
		var controller = Components.Get<PlayerController>();

		if ( !CanAttack( attacker, controller ) )
			return;

		var cooldown = GetCooldown( attacker.Role );

		if ( timeSinceValidatedSwing < cooldown )
			return;

		timeSinceValidatedSwing = 0.0f;

		var target = targetObject?.Components.Get<LargeLadPlayer>();

		if ( !IsValidTarget( target ) ||
			!IsWithinReachAndFacing( attacker, target, controller ) ||
			!HasLineOfSight( attacker, target, controller ) )
		{
			Log.Info( $"{attacker.GameObject.Name} swung and missed." );
			return;
		}

		var round = GetRoundManager();

		if ( attacker.Role == LargeLadRole.LargeLad )
		{
			round.BeginPlayerRespawn(
				target,
				LargeLadRole.Minion,
				useRagdoll: false );

			Log.Info(
				$"{attacker.GameObject.Name} ate {target.GameObject.Name}. " +
				$"They will respawn as a Minion in {round.PlayerRespawnDelay:0.#} seconds." );
			return;
		}

		var killed = target.Health.TakeDamage( MinionDamage );

		if ( !killed )
		{
			Log.Info(
				$"{attacker.GameObject.Name} hit {target.GameObject.Name} for " +
				$"{MinionDamage:0.#} damage. " +
				$"{target.Health.CurrentHealth:0.#}/{target.Health.MaximumHealth:0.#} health remains." );
			return;
		}

		// A lethal Minion hit uses the ordinary ragdoll path before the
		// Skinny Kid joins the Lad team.
		round.BeginPlayerRespawn(
			target,
			LargeLadRole.Minion,
			useRagdoll: true );

		Log.Info(
			$"{attacker.GameObject.Name} killed {target.GameObject.Name}. " +
			$"They will respawn as a Minion in {round.PlayerRespawnDelay:0.#} seconds." );
	}

	private bool CanAttack( LargeLadPlayer attacker, PlayerController controller )
	{
		if ( attacker is null || controller is null ||
			attacker.Role is not (LargeLadRole.LargeLad or LargeLadRole.Minion) ||
			attacker.EquippedWeapon != LargeLadWeaponType.Melee ||
			attacker.Health?.IsDead == true ||
			attacker.MovementLocked )
		{
			return false;
		}

		return GetRoundManager()?.Phase == LargeLadRoundPhase.Playing;
	}

	private LargeLadPlayer FindBestTarget(
		LargeLadPlayer attacker,
		PlayerController controller )
	{
		return Scene
			.GetAllComponents<LargeLadPlayer>()
			.Where( IsValidTarget )
			.Where( target => IsWithinReachAndFacing( attacker, target, controller ) )
			.Where( target => HasLineOfSight( attacker, target, controller ) )
			.OrderBy( target => attacker.GameObject.WorldPosition.DistanceSquared(
				target.GameObject.WorldPosition ) )
			.FirstOrDefault();
	}

	private bool IsWithinReachAndFacing(
		LargeLadPlayer attacker,
		LargeLadPlayer target,
		PlayerController controller )
	{
		var range = GetRange( attacker.Role );
		var start = controller.EyePosition;
		var targetPosition = target.GameObject.WorldPosition + Vector3.Up * 36.0f;
		var toTarget = targetPosition - start;

		if ( toTarget.LengthSquared > range * range )
			return false;

		if ( toTarget.LengthSquared <= 0.001f )
			return true;

		var forward = controller.EyeTransform.Rotation.Forward;
		return Vector3.Dot( forward, toTarget.Normal ) >= MinimumFacingDot;
	}

	private bool HasLineOfSight(
		LargeLadPlayer attacker,
		LargeLadPlayer target,
		PlayerController controller )
	{
		var start = controller.EyePosition;
		var end = target.GameObject.WorldPosition + Vector3.Up * 36.0f;
		var trace = Scene.Trace
			.Ray( start, end )
			.UseHitboxes( true )
			.IgnoreGameObjectHierarchy( attacker.GameObject )
			.Run();

		var hitPlayer = trace.GameObject?.Components.Get<LargeLadPlayer>(
			FindMode.EverythingInSelfAndAncestors );

		return hitPlayer == target;
	}

	private static bool IsValidTarget( LargeLadPlayer target )
	{
		return target?.Role == LargeLadRole.SkinnyKid &&
			target.Health?.IsDead != true;
	}

	private float GetRange( LargeLadRole role )
	{
		return role == LargeLadRole.LargeLad
			? LargeLadRange
			: MinionRange;
	}

	private float GetCooldown( LargeLadRole role )
	{
		return role == LargeLadRole.LargeLad
			? LargeLadCooldown
			: MinionCooldown;
	}

	private LargeLadRoundManager GetRoundManager()
	{
		return Scene
			.GetAllComponents<LargeLadRoundManager>()
			.FirstOrDefault();
	}
}
