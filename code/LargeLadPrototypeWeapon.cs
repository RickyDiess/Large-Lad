using Sandbox;
using System.Linq;

public sealed class LargeLadPrototypeWeapon : Component
{
	[Property]
	public float Damage { get; set; } = 100.0f;

	[Property]
	public float Range { get; set; } = 1200.0f;

	[Property]
	public float Cooldown { get; set; } = 0.35f;

	private TimeSince timeSinceLastAttack;

	protected override void OnUpdate()
	{
		if ( IsProxy || !Input.Pressed( "Attack1" ) )
			return;

		var player = Components.Get<LargeLadPlayer>();
		var controller = Components.Get<PlayerController>();

		if ( player is null || controller is null ||
			player.Role != LargeLadRole.SkinnyKid ||
			player.Health?.IsDead == true )
		{
			return;
		}

		var round = Scene
			.GetAllComponents<LargeLadRoundManager>()
			.FirstOrDefault();

		if ( round?.Phase != LargeLadRoundPhase.Playing )
			return;

		var start = controller.EyePosition;
		var end = start + controller.EyeTransform.Rotation.Forward * Range;
		var trace = Scene.Trace
			.Ray( start, end )
			.UseHitboxes( true )
			.IgnoreGameObjectHierarchy( GameObject )
			.Run();

		Log.Info( trace.Hit
			? $"Prototype blaster hit {trace.GameObject?.Name ?? "the world"}."
			: "Prototype blaster missed." );

		if ( !trace.Hit || trace.GameObject is null )
			return;

		var target = trace.GameObject.Components.Get<LargeLadPlayer>(
			FindMode.EverythingInSelfAndAncestors );

		if ( target?.Role is not (LargeLadRole.LargeLad or LargeLadRole.Minion) )
			return;

		RequestDamage( target.GameObject );
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	public void RequestDamage( GameObject targetObject )
	{
		if ( !Networking.IsHost || timeSinceLastAttack < Cooldown )
			return;

		var attacker = Components.Get<LargeLadPlayer>();
		var target = targetObject?.Components.Get<LargeLadPlayer>();
		var round = Scene
			.GetAllComponents<LargeLadRoundManager>()
			.FirstOrDefault();

		if ( attacker?.Role != LargeLadRole.SkinnyKid ||
			attacker.Health?.IsDead == true ||
			target?.Role is not (LargeLadRole.LargeLad or LargeLadRole.Minion) ||
			target.Health is null ||
			target.Health.IsDead ||
			round?.Phase != LargeLadRoundPhase.Playing )
		{
			return;
		}

		if ( GameObject.WorldPosition.DistanceSquared( target.GameObject.WorldPosition ) >
			Range * Range )
		{
			return;
		}

		var controller = Components.Get<PlayerController>();
		var start = controller?.EyePosition ?? GameObject.WorldPosition + Vector3.Up * 64.0f;
		var end = target.GameObject.WorldPosition + Vector3.Up * 36.0f;
		var trace = Scene.Trace
			.Ray( start, end )
			.UseHitboxes( true )
			.IgnoreGameObjectHierarchy( GameObject )
			.Run();

		var validatedTarget = trace.GameObject?.Components.Get<LargeLadPlayer>(
			FindMode.EverythingInSelfAndAncestors );

		if ( validatedTarget != target )
			return;

		timeSinceLastAttack = 0.0f;
		target.Health.TakeDamage( Damage );

		var targetName = target.Role == LargeLadRole.LargeLad
			? "the Large Lad"
			: "a Minion";

		Log.Info(
			$"{attacker.GameObject.Name} hit {targetName} for {Damage:0.#} damage. " +
			$"{target.Health.CurrentHealth:0.#}/{target.Health.MaximumHealth:0.#} health remains." );
	}
}
