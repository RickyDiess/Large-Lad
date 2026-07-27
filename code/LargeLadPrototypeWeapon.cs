using Sandbox;

public enum LargeLadShotResult
{
	AcceptedMiss,
	PlayerHit,
	BarricadeHit
}

/// <summary>
/// Owner-aimed, host-validated firearm driver. The historical class name is
/// retained so existing player prefabs keep their serialized component.
/// </summary>
public sealed class LargeLadPrototypeWeapon : Component
{
	// Requests may arrive this early relative to the host schedule. Accepted
	// shots remain anchored to the existing schedule, so this tolerance absorbs
	// frame/network jitter without shortening the sustained fire interval.
	private const float HostCadenceTolerance = 0.025f;
	private const float ConfirmedHitmarkerDuration = 0.14f;

	private TimeSince timeSinceLocalShot;
	private TimeSince timeSinceConfirmedHit;
	private int nextOwnerShotSequence;
	private int lastHostShotSequence;
	private int lastOwnerResultSequence;
	private bool hasHostShotSchedule;
	private float nextHostShotTime;
	private bool hasConfirmedHit;

	[Property, Title( "Fire Debug Output" )]
	public bool EnableFireDebug { get; set; } = false;

	public bool HasConfirmedHitmarker =>
		hasConfirmedHit && timeSinceConfirmedHit < ConfirmedHitmarkerDuration;

	public LargeLadShotResult LastShotResult { get; private set; } =
		LargeLadShotResult.AcceptedMiss;

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

		var round = LargeLadGameManager.FindForScene( Scene );

		if ( round?.Phase != LargeLadRoundPhase.Playing )
			return;

		var camera = Scene.Camera;

		if ( !LargeLadAimResolver.TryResolveLocal(
			Scene,
			camera,
			controller,
			GameObject,
			definition.Range,
			out var aim ) )
		{
			DebugFire( "Local shot not sent: invalid aim." );
			return;
		}

		timeSinceLocalShot = 0.0f;
		nextOwnerShotSequence++;
		RequestFire( nextOwnerShotSequence, aim.DesiredAimPoint );
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	private void RequestFire( int ownerShotSequence, Vector3 desiredAimPoint )
	{
		if ( !Networking.IsHost )
			return;

		if ( ownerShotSequence <= lastHostShotSequence )
		{
			DebugFire(
				$"Shot {ownerShotSequence} rejected: duplicate or out-of-order sequence." );
			return;
		}

		// Consume every new sequence even when its payload is invalid so the same
		// malformed request cannot be replayed.
		lastHostShotSequence = ownerShotSequence;

		var attacker = Components.Get<LargeLadPlayer>();
		var inventory = attacker?.Inventory;
		var definition = inventory?.EquippedDefinition;
		var round = LargeLadGameManager.FindForScene( Scene );

		if ( attacker?.Role != LargeLadRole.SkinnyKid || inventory is null ||
			definition is null || !LargeLadWeaponCatalog.IsFirearm( inventory.EquippedWeapon ) ||
			attacker.Health?.IsDead == true || round?.Phase != LargeLadRoundPhase.Playing )
		{
			DebugFire( $"Shot {ownerShotSequence} rejected: firing state is not valid." );
			return;
		}

		var controller = Components.Get<PlayerController>();

		if ( !LargeLadAimResolver.TryResolveAuthoritative(
			Scene,
			controller,
			GameObject,
			definition.Range,
			desiredAimPoint,
			out var aim,
			out var aimFailure ) )
		{
			DebugFire(
				$"Shot {ownerShotSequence} rejected: invalid aim ({aimFailure})." );
			return;
		}

		if ( inventory.IsReloading || inventory.EquippedMagazine <= 0 )
		{
			DebugFire(
				$"Shot {ownerShotSequence} rejected: missing ammo or reload in progress." );

			if ( inventory.EquippedMagazine <= 0 )
			{
				inventory.BeginReload();
			}

			return;
		}

		var hostNow = Time.Now;

		if ( hasHostShotSchedule &&
			hostNow + HostCadenceTolerance < nextHostShotTime )
		{
			var remaining = nextHostShotTime - hostNow;
			DebugFire(
				$"Shot {ownerShotSequence} rejected: cadence ({remaining:0.000}s remaining)." );
			return;
		}

		if ( !inventory.TryConsumeShot( out definition ) )
		{
			DebugFire( $"Shot {ownerShotSequence} rejected: missing ammo." );
			inventory.BeginReload();
			return;
		}

		CommitHostCadence( definition, hostNow );

		if ( aim.IsObstructed )
		{
			DebugFire(
				$"Shot {ownerShotSequence}: obstruction before desired aim point." );
		}

		var trace = aim.ShotTrace;
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

		var result = LargeLadShotResult.AcceptedMiss;

		if ( targetPlayer is not null &&
			targetPlayer.Role is LargeLadRole.LargeLad or LargeLadRole.Minion &&
			targetPlayer.Health?.IsDead == false )
		{
			targetPlayer.Health.TryApplyDamage( damage, out var applied );

			if ( applied.AppliedDamage > 0.0f )
			{
				result = LargeLadShotResult.PlayerHit;
				DebugFire(
					$"Shot {ownerShotSequence}: confirmed player hit for " +
					$"{applied.AppliedDamage:0.#} damage." );
			}
		}
		else if ( barricade is not null &&
			barricade.TryApplyDamage( damage, out var structuralDamage ) )
		{
			result = LargeLadShotResult.BarricadeHit;
			DebugFire(
				$"Shot {ownerShotSequence}: confirmed barricade hit for " +
				$"{structuralDamage.AppliedDamage:0.#} damage." );
		}

		if ( result == LargeLadShotResult.AcceptedMiss )
		{
			DebugFire( $"Shot {ownerShotSequence}: accepted miss." );
		}

		ReceiveShotResult( ownerShotSequence, result );
	}

	private void CommitHostCadence(
		LargeLadWeaponDefinition definition,
		float hostNow )
	{
		if ( !hasHostShotSchedule )
		{
			hasHostShotSchedule = true;
			nextHostShotTime = hostNow + definition.FireInterval;
			return;
		}

		nextHostShotTime =
			System.MathF.Max( hostNow, nextHostShotTime ) +
			definition.FireInterval;
	}

	[Rpc.Owner( NetFlags.HostOnly )]
	private void ReceiveShotResult(
		int ownerShotSequence,
		LargeLadShotResult result )
	{
		if ( ownerShotSequence <= lastOwnerResultSequence )
			return;

		lastOwnerResultSequence = ownerShotSequence;
		LastShotResult = result;

		if ( result is not (LargeLadShotResult.PlayerHit or
			LargeLadShotResult.BarricadeHit) )
		{
			return;
		}

		hasConfirmedHit = true;
		timeSinceConfirmedHit = 0.0f;
	}

	private void DebugFire( string message )
	{
		if ( EnableFireDebug )
		{
			Log.Info( $"{GameObject.Name}: {message}" );
		}
	}
}
