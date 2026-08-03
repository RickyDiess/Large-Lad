using Sandbox;
using System.Collections.Generic;

public enum LargeLadShotResult
{
	AcceptedMiss,
	PlayerHit,
	PlayerHeadshot,
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
	private const float FireDebugTraceDuration = 2.0f;

	private TimeSince timeSinceLocalShot;
	private TimeSince timeSinceConfirmedHit;
	private int nextOwnerShotSequence;
	private int lastOwnerResultSequence;
	private bool hasHostShotSchedule;
	private float nextHostShotTime;
	private bool hasConfirmedHit;
	private readonly LargeLadFirearmShotRequestGate hostShotRequestGate = new();
	private LargeLadPlayer cachedPlayer;
	private PlayerController cachedController;
	private LargeLadGameManager cachedGameManager;

	[Property, Title( "Firearm Debug" )]
	public bool EnableFireDebug { get; set; }

	[Property, Group( "Feedback" ), Title( "Headshot Confirmation Sound" )]
	public SoundEvent HeadshotConfirmationSound { get; set; }

	public bool HasConfirmedHitmarker =>
		hasConfirmedHit && timeSinceConfirmedHit < ConfirmedHitmarkerDuration;

	public LargeLadShotResult LastShotResult { get; private set; } =
		LargeLadShotResult.AcceptedMiss;

	protected override void OnAwake()
	{
		ResolveCachedReferences();
	}

	protected override void OnStart()
	{
		ResolveCachedReferences();
	}

	protected override void OnUpdate()
	{
		if ( IsProxy || !Input.Down( "Attack1" ) )
			return;

		var player = cachedPlayer;
		var controller = cachedController;
		var inventory = player?.Inventory;
		var definition = inventory?.EquippedDefinition;

		if ( player is null || controller is null || inventory is null ||
			definition is null || player.Role != LargeLadRole.SkinnyKid ||
			!LargeLadWeaponCatalog.IsFirearm( inventory.EquippedWeapon ) ||
			player.Health?.IsDead == true || player.IsEatBusy ||
			inventory.IsReloading ||
			timeSinceLocalShot < definition.FireInterval )
		{
			return;
		}

		var round = GetGameManager();

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

		if ( !hostShotRequestGate.TryConsume( ownerShotSequence ) )
		{
			DebugFire(
				$"Shot {ownerShotSequence} rejected: duplicate or out-of-order sequence." );
			return;
		}

		// Consume every new sequence even when its payload is invalid so the same
		// malformed request cannot be replayed.
		var attacker = cachedPlayer;
		var inventory = attacker?.Inventory;
		var definition = inventory?.EquippedDefinition;
		var round = GetGameManager();

		if ( attacker?.Role != LargeLadRole.SkinnyKid || inventory is null ||
			definition is null || !LargeLadWeaponCatalog.IsFirearm( inventory.EquippedWeapon ) ||
			attacker.Health?.IsDead == true || attacker.IsEatBusy ||
			round?.Phase != LargeLadRoundPhase.Playing )
		{
			DebugFire( $"Shot {ownerShotSequence} rejected: firing state is not valid." );
			return;
		}

		var controller = cachedController;

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
		var barricade = LargeLadBarricade.FindFor( trace.GameObject );
		var targetCanTakeFirearmDamage = targetPlayer is not null &&
			targetPlayer.Role is LargeLadRole.LargeLad or LargeLadRole.Minion &&
			targetPlayer.Health?.IsDead == false;
		var hitRegion = LargeLadHitRegion.None;
		var classificationDetails = "not run";
		var resolutionReason = aim.IsObstructed
			? "authoritative trace obstructed before camera-selected aim point"
			: "none";

		if ( targetCanTakeFirearmDamage )
		{
			hitRegion = ResolveSelectedTargetHitRegion(
				targetPlayer,
				aim.ShotOrigin,
				aim.ShotDirection,
				definition.Range,
				out classificationDetails,
				out var classificationReason );
			resolutionReason = aim.IsObstructed
				? $"{resolutionReason}; {classificationReason}"
				: classificationReason;
		}
		else if ( targetPlayer is not null )
		{
			resolutionReason = "first-hit player is not an eligible living victim";
		}
		else if ( trace.Hit )
		{
			resolutionReason = barricade is not null
				? "first authoritative hit selected a barricade"
				: "first authoritative hit was non-player geometry or object";
		}
		else
		{
			resolutionReason = "authoritative trace reached range without a hit";
		}

		var damage = new LargeLadDamageContext
		{
			Attacker = GameObject,
			AttackerRole = attacker.Role,
			SourceWeapon = inventory.EquippedWeapon,
			SourceShotSequence = ownerShotSequence,
			DamageType = LargeLadDamageType.Firearm,
			HitRegion = hitRegion,
			BaseDamage = definition.Damage
		};

		var result = LargeLadShotResult.AcceptedMiss;

		if ( targetCanTakeFirearmDamage )
		{
			targetPlayer.Health.TryApplyDamage( damage, out var applied );

			if ( applied.AppliedDamage > 0.0f )
			{
				result = applied.IsFirearmHeadshot
					? LargeLadShotResult.PlayerHeadshot
					: LargeLadShotResult.PlayerHit;
				DebugFire(
					$"Shot {ownerShotSequence}: confirmed " +
					$"{(applied.IsFirearmHeadshot ? "headshot" : "player hit")} for " +
					$"{applied.AppliedDamage:0.#} damage." );
			}
			else
			{
				resolutionReason += "; damage receiver applied zero damage";
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

		DebugFireResolution(
			ownerShotSequence,
			aim,
			targetPlayer,
			hitRegion,
			classificationDetails,
			resolutionReason );

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

	private LargeLadHitRegion ResolveSelectedTargetHitRegion(
		LargeLadPlayer selectedTarget,
		Vector3 shotOrigin,
		Vector3 shotDirection,
		float range,
		out string classificationDetails,
		out string reason )
	{
		var shotEnd = shotOrigin + shotDirection * range;
		var obstructionTrace = Scene.Trace
			.Ray( shotOrigin, shotEnd )
			.UseHitboxes( false )
			.WithoutTags( LargeLadGameplayRules.MinionPassageTag )
			.IgnoreGameObjectHierarchy( GameObject )
			.IgnoreGameObjectHierarchy( selectedTarget.GameObject )
			.Run();
		var maximumClassificationDistance = obstructionTrace.Hit
			? System.MathF.Min( range, obstructionTrace.Distance )
			: range;
		var hitboxTraceBuilder = Scene.Trace
			.Ray(
				shotOrigin,
				shotOrigin + shotDirection * maximumClassificationDistance )
			// This s&box build returns no animated-model hitboxes when the physics
			// world is disabled. RunAll keeps the movement collider result but also
			// exposes model hitboxes behind it; the deterministic rule below accepts
			// only an actual same-target head hitbox.
			.UseHitboxes( true )
			.IgnoreGameObjectHierarchy( GameObject );

		var hitboxTraces = hitboxTraceBuilder.RunAll();
		var candidates = new List<LargeLadFirearmHitboxCandidate>();
		var details = EnableFireDebug
			? new List<string>()
			: null;

		if ( EnableFireDebug )
		{
			Scene.DebugOverlay.Trace(
				obstructionTrace,
				FireDebugTraceDuration,
				false );
		}

		foreach ( var hitboxTrace in hitboxTraces )
		{
			if ( EnableFireDebug )
			{
				Scene.DebugOverlay.Trace(
					hitboxTrace,
					FireDebugTraceDuration,
					false );
			}

			var hitbox = hitboxTrace.Hitbox;
			var hitPlayer = hitboxTrace.GameObject?.Components
				.Get<LargeLadPlayer>( FindMode.EverythingInSelfAndAncestors );
			var belongsToSelectedTarget = hitPlayer == selectedTarget;
			var hasHeadTag = hitbox?.Tags?.Has(
				LargeLadFirearmHitRules.HeadHitboxTag ) == true;
			var boneName = hitbox?.Bone?.Name;

			candidates.Add( new LargeLadFirearmHitboxCandidate(
				belongsToSelectedTarget,
				hitbox is not null,
				hitboxTrace.Distance,
				boneName,
				hasHeadTag ) );

			if ( details is not null )
			{
				details.Add(
					$"object={GetObjectName( hitboxTrace.GameObject )}, " +
					$"sameTarget={belongsToSelectedTarget}, " +
					$"collider={hitboxTrace.Collider is not null}, " +
					$"hitboxQueryResult={hitbox is not null}, " +
					$"hitboxWrapper={hitboxTrace.Hitbox is not null}, " +
					$"distance={hitboxTrace.Distance:0.###}, " +
					$"boneIndex={hitboxTrace.Bone}, " +
					$"bone={boneName ?? "<none>"}, " +
					$"headTag={hasHeadTag}, " +
					$"hitboxTags={FormatTags( hitbox?.Tags )}" );
			}
		}

		classificationDetails = details?.Count > 0
			? string.Join( " | ", details )
			: EnableFireDebug
				? "no hitbox results"
				: "debug disabled";
		var region = LargeLadFirearmHitRules.ResolveSelectedTargetHitRegion(
			candidates,
			maximumClassificationDistance );

		reason = region == LargeLadHitRegion.Head
			? "same-target head hitbox confirmed"
			: obstructionTrace.Hit
				? $"no same-target head hitbox before " +
					$"{GetObjectName( obstructionTrace.GameObject )} obstruction"
				: "no same-target head hitbox confirmed";
		return region;
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
			LargeLadShotResult.PlayerHeadshot or
			LargeLadShotResult.BarricadeHit) )
		{
			return;
		}

		hasConfirmedHit = true;
		timeSinceConfirmedHit = 0.0f;

		if ( result == LargeLadShotResult.PlayerHeadshot &&
			HeadshotConfirmationSound is not null )
		{
			Sound.Play(
				HeadshotConfirmationSound,
				GameObject.WorldPosition );
		}
	}

	private void DebugFire( string message )
	{
		if ( EnableFireDebug )
		{
			Log.Info( $"[Debug/Firearm] {GameObject.Name}: {message}" );
		}
	}

	private void DebugFireResolution(
		int ownerShotSequence,
		LargeLadAimResolution aim,
		LargeLadPlayer selectedTarget,
		LargeLadHitRegion hitRegion,
		string classificationDetails,
		string reason )
	{
		if ( !EnableFireDebug )
			return;

		var firstTrace = aim.ShotTrace;
		var firstHitbox = firstTrace.Hitbox;
		var firstComponent = firstTrace.Component?.GetType().Name ?? "<none>";
		var firstBone = firstHitbox?.Bone?.Name ?? "<none>";
		var firstHasHeadTag = firstHitbox?.Tags?.Has(
			LargeLadFirearmHitRules.HeadHitboxTag ) == true;
		var firstTags = FormatTags( firstHitbox?.Tags );

		Log.Info(
			$"[Debug/Firearm] {GameObject.Name}: " +
			$"shotSequence={ownerShotSequence}; " +
			$"cameraAimPoint={aim.DesiredAimPoint}; " +
			$"eyeTraceStart={aim.ShotOrigin}; " +
			$"eyeTraceDirection={aim.ShotDirection}; " +
			$"firstHitObject={GetObjectName( firstTrace.GameObject )}; " +
			$"firstHitComponent={firstComponent}; " +
			$"containsCollider={firstTrace.Collider is not null}; " +
			$"containsHitbox={firstHitbox is not null}; " +
			$"firstBoneIndex={firstTrace.Bone}; " +
			$"firstHitboxBone={firstBone}; " +
			$"firstHasHeadTag={firstHasHeadTag}; " +
			$"firstHitboxTags={firstTags}; " +
			$"selectedTarget={GetObjectName( selectedTarget?.GameObject )}; " +
			$"finalHitRegion={hitRegion}; " +
			$"classificationHitboxes=[{classificationDetails}]; " +
			$"reason={reason}." );
		Scene.DebugOverlay.Trace(
			firstTrace,
			FireDebugTraceDuration,
			false );
	}

	private static string FormatTags( ITagSet tags )
	{
		return tags is null
			? "<none>"
			: string.Join( ",", tags );
	}

	private static string GetObjectName( GameObject gameObject )
	{
		return gameObject is null
			? "<none>"
			: gameObject.Name;
	}

	private LargeLadGameManager GetGameManager()
	{
		if ( cachedGameManager is not null &&
			cachedGameManager.IsValid &&
			cachedGameManager.Enabled &&
			cachedGameManager.Scene == Scene &&
			cachedGameManager.HasSceneGameplayOwnership )
		{
			return cachedGameManager;
		}

		cachedGameManager = LargeLadGameManager.FindForScene( Scene );
		return cachedGameManager;
	}

	private void ResolveCachedReferences()
	{
		if ( cachedPlayer is null ||
			!cachedPlayer.IsValid ||
			cachedPlayer.GameObject != GameObject )
		{
			cachedPlayer = Components.Get<LargeLadPlayer>();
		}

		if ( cachedController is null ||
			!cachedController.IsValid ||
			cachedController.GameObject != GameObject )
		{
			cachedController = Components.Get<PlayerController>();
		}

		GetGameManager();
	}
}
