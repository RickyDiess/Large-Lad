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

		// Send the point the owner aimed at, not a client-selected target object.
		// The host replays the shot from the authoritative player eye position.
		RequestFire( trace.EndPosition );
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	private void RequestFire( Vector3 claimedAimPoint )
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

		var controller = Components.Get<PlayerController>();
		var start = controller?.EyePosition ?? GameObject.WorldPosition + Vector3.Up * 64.0f;
		var towardAimPoint = claimedAimPoint - start;

		if ( towardAimPoint.LengthSquared < 0.001f )
			return;

		var traceDistance = System.MathF.Min( towardAimPoint.Length, definition.Range );
		var targetPosition = start + towardAimPoint.Normal * traceDistance;
		var trace = Scene.Trace
			.Ray( start, targetPosition )
			.UseHitboxes( true )
			.IgnoreGameObjectHierarchy( GameObject )
			.Run();

		var targetPlayer = trace.GameObject?.Components.Get<LargeLadPlayer>(
			FindMode.EverythingInSelfAndAncestors );
		var barricade = LargeLadBarricade.FindFor( Scene, trace.GameObject );

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
			if ( targetPlayer.Role is not (LargeLadRole.LargeLad or LargeLadRole.Minion) ||
				targetPlayer.Health?.IsDead != false )
			{
				return;
			}

			targetPlayer.Health.TryApplyDamage( damage, out var applied );
			Log.Info( $"{GameObject.Name} hit {targetPlayer.GameObject.Name} for {applied.AppliedDamage:0.#} damage." );
			return;
		}

		if ( barricade is not null && barricade.TryApplyDamage( damage, out var structuralDamage ) )
		{
			Log.Info( $"{GameObject.Name} damaged {barricade.GameObject.Name} for {structuralDamage.AppliedDamage:0.#}." );
		}
	}
}
