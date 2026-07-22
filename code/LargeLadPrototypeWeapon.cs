using Sandbox;
using System.Linq;

/// <summary>
/// Owner-aimed, host-validated firearm driver. The historical class name is
/// retained so existing player prefabs keep their serialized component.
/// </summary>
public sealed class LargeLadPrototypeWeapon : Component
{
	private TimeSince timeSinceLocalShot;
	private TimeSince timeSinceValidatedShot;

	protected override void OnUpdate()
	{
		if ( IsProxy || !Input.Down( "Attack1" ) )
			return;

		var player = Components.Get<LargeLadPlayer>();
		var controller = Components.Get<PlayerController>();
		var inventory = player?.Inventory;
		var definition = inventory?.EquippedDefinition;

		if ( player is null || controller is null || inventory is null ||
			definition is null || player.Role != LargeLadRole.SkinnyKid ||
			!LargeLadWeaponCatalog.IsFirearm( inventory.EquippedWeapon ) ||
			player.Health?.IsDead == true || inventory.IsReloading ||
			timeSinceLocalShot < definition.FireInterval )
		{
			return;
		}

		var round = Scene
			.GetAllComponents<LargeLadRoundManager>()
			.FirstOrDefault();

		if ( round?.Phase != LargeLadRoundPhase.Playing )
			return;

		timeSinceLocalShot = 0.0f;

		var camera = Scene.Camera;
		var start = camera is not null ? camera.WorldPosition : controller.EyePosition;
		var forward = camera is not null
			? camera.WorldRotation.Forward
			: controller.EyeTransform.Rotation.Forward;
		var trace = Scene.Trace
			.Ray( start, start + forward * definition.Range )
			.UseHitboxes( true )
			.IgnoreGameObjectHierarchy( GameObject )
			.Run();

		RequestFire( trace.Hit ? trace.GameObject : null );
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	private void RequestFire( GameObject claimedTarget )
	{
		if ( !Networking.IsHost )
			return;

		var attacker = Components.Get<LargeLadPlayer>();
		var inventory = attacker?.Inventory;
		var definition = inventory?.EquippedDefinition;
		var round = Scene
			.GetAllComponents<LargeLadRoundManager>()
			.FirstOrDefault();

		if ( attacker?.Role != LargeLadRole.SkinnyKid || inventory is null ||
			definition is null || !LargeLadWeaponCatalog.IsFirearm( inventory.EquippedWeapon ) ||
			attacker.Health?.IsDead == true || round?.Phase != LargeLadRoundPhase.Playing ||
			timeSinceValidatedShot < definition.FireInterval )
		{
			return;
		}

		if ( !inventory.TryConsumeShot( out definition ) )
		{
			inventory.BeginReload();
			return;
		}

		timeSinceValidatedShot = 0.0f;

		if ( claimedTarget is null )
			return;

		var targetPlayer = claimedTarget.Components.Get<LargeLadPlayer>(
			FindMode.EverythingInSelfAndAncestors );
		var barricade = LargeLadBarricade.FindFor( Scene, claimedTarget );

		if ( targetPlayer is null && barricade is null )
			return;

		var resolvedTarget = targetPlayer?.GameObject ?? barricade.GameObject;

		if ( GameObject.WorldPosition.DistanceSquared( resolvedTarget.WorldPosition ) >
			definition.Range * definition.Range )
		{
			return;
		}

		var controller = Components.Get<PlayerController>();
		var start = controller?.EyePosition ?? GameObject.WorldPosition + Vector3.Up * 64.0f;
		var targetPosition = targetPlayer is not null
			? targetPlayer.GameObject.WorldPosition + Vector3.Up * 36.0f
			: barricade.GameObject.WorldPosition;
		var trace = Scene.Trace
			.Ray( start, targetPosition )
			.UseHitboxes( true )
			.IgnoreGameObjectHierarchy( GameObject )
			.Run();

		var validatedPlayer = trace.GameObject?.Components.Get<LargeLadPlayer>(
			FindMode.EverythingInSelfAndAncestors );
		var validatedBarricade = LargeLadBarricade.FindFor( Scene, trace.GameObject );

		var damage = new LargeLadDamageContext
		{
			Attacker = GameObject,
			AttackerRole = attacker.Role,
			SourceWeapon = inventory.EquippedWeapon,
			DamageType = LargeLadDamageType.Firearm,
			BaseDamage = definition.Damage
		};

		if ( targetPlayer is not null )
		{
			if ( validatedPlayer != targetPlayer ||
				targetPlayer.Role is not (LargeLadRole.LargeLad or LargeLadRole.Minion) ||
				targetPlayer.Health?.IsDead != false )
			{
				return;
			}

			targetPlayer.Health.TryApplyDamage( damage, out var applied );
			Log.Info( $"{GameObject.Name} hit {targetPlayer.GameObject.Name} for {applied.AppliedDamage:0.#} damage." );
			return;
		}

		if ( validatedBarricade == barricade &&
			barricade.TryApplyDamage( damage, out var structuralDamage ) )
		{
			Log.Info( $"{GameObject.Name} damaged {barricade.GameObject.Name} for {structuralDamage.AppliedDamage:0.#}." );
		}
	}
}
