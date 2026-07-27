using Sandbox;
using Sandbox.Citizen;
using System.Linq;

public enum LargeLadMeleeResult
{
	Miss,
	PlayerHit,
	BarricadeHit
}

/// <summary>
/// Shared owner-input, host-validated melee system for every playable role.
/// The owner only asks to swing; the host chooses and validates the target.
/// </summary>
public sealed class LargeLadMeleeSystem : Component
{
	private const float HostCadenceTolerance = 0.025f;
	private const float ConfirmedHitmarkerDuration = 0.14f;

	[Property, Group( "Targeting" ), Title( "Swing Trace Radius" )]
	public float SwingTraceRadius { get; set; } = 18.0f;

	[Property, Group( "Targeting" ), Title( "Aim Assist Facing Dot" )]
	public float MinimumFacingDot { get; set; } = 0.55f;

	[Property, Group( "Melee Model" )]
	public Model MeleeModel { get; set; } =
		Model.Load( "models/citizen_props/crowbar01.vmdl" );

	[Property, Group( "Melee Model" ), Title( "Skinny Kid Hold Bone" )]
	public string MeleeModelBone { get; set; } = "hold_R";

	[Property, Group( "Melee Model" ), Title( "Hold-relative Position" )]
	public Vector3 MeleeModelPosition { get; set; } = Vector3.Zero;

	[Property, Group( "Melee Model" ), Title( "Hold-relative Angles" )]
	public Angles MeleeModelAngles { get; set; } =
		new( 0.0f, 0.0f, 0.0f );

	[Property, Group( "Melee Model" ), Title( "Model-space Grip Point" )]
	public Vector3 MeleeModelGripPosition { get; set; } = Vector3.Zero;

	[Property, Group( "Melee Model" )]
	public float MeleeModelScale { get; set; } = 0.25f;

	[Property, Group( "First Person" ), Title( "Show Melee Arms" )]
	public bool ShowFirstPersonArms { get; set; } = true;

	[Property, Group( "First Person" ), Title( "Camera-relative Adjustment" )]
	public Vector3 FirstPersonArmsPosition { get; set; } = Vector3.Zero;

	[Property, Group( "First Person" )]
	public Angles FirstPersonArmsAngles { get; set; } = Angles.Zero;

	private TimeSince timeSinceLocalSwing;
	private TimeSince timeSinceConfirmedHit;
	private int nextOwnerSwingSequence;
	private int lastHostSwingSequence;
	private int lastOwnerResultSequence;
	private bool hasHostSwingSchedule;
	private float nextHostSwingTime;
	private bool hasConfirmedHit;
	private GameObject meleeModelPivotObject;
	private GameObject meleeModelObject;
	private ModelRenderer meleeModelRenderer;
	private Model appliedMeleeModel;
	private GameObject firstPersonArmsObject;
	private SkinnedModelRenderer firstPersonArmsRenderer;
	private Model appliedFirstPersonArmsModel;

	public bool HasConfirmedHitmarker =>
		hasConfirmedHit && timeSinceConfirmedHit < ConfirmedHitmarkerDuration;

	public LargeLadMeleeResult LastAttackResult { get; private set; } =
		LargeLadMeleeResult.Miss;

	protected override void OnUpdate()
	{
		UpdateMeleePose();
		UpdateFirstPersonArms();
		UpdateMeleeModel();

		if ( IsProxy || !Input.Down( "Attack1" ) )
			return;

		var attacker = Components.Get<LargeLadPlayer>();
		var controller = Components.Get<PlayerController>();

		if ( !CanAttack( attacker, controller ) )
			return;

		if ( !attacker.TryGetRoleProfile( attacker.Role, out var profile ) )
			return;

		if ( timeSinceLocalSwing < profile.MeleeCooldown )
			return;

		timeSinceLocalSwing = 0.0f;
		TriggerMeleeSwingAnimation();
		nextOwnerSwingSequence++;
		RequestMeleeAttack( nextOwnerSwingSequence );
	}

	protected override void OnDestroy()
	{
		DestroyMeleeModel();
		DestroyFirstPersonArms();
	}

	protected override void OnValidate()
	{
		if ( SwingTraceRadius <= 0.0f )
			Log.Warning( $"{GameObject.Name}: swing trace radius must be positive." );

		if ( MinimumFacingDot < -1.0f || MinimumFacingDot > 1.0f )
			Log.Warning( $"{GameObject.Name}: aim-assist facing dot must be -1 to 1." );
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	private void RequestMeleeAttack( int ownerSwingSequence )
	{
		if ( !Networking.IsHost ||
			ownerSwingSequence <= lastHostSwingSequence )
		{
			return;
		}

		// Consume every new sequence before validating its payload/state so it
		// cannot be replayed later.
		lastHostSwingSequence = ownerSwingSequence;

		var attacker = Components.Get<LargeLadPlayer>();
		var controller = Components.Get<PlayerController>();

		if ( !CanAttack( attacker, controller ) )
			return;

		if ( !attacker.TryGetRoleProfile( attacker.Role, out var profile ) )
			return;

		var hostNow = Time.Now;

		if ( hasHostSwingSchedule &&
			hostNow + HostCadenceTolerance < nextHostSwingTime )
		{
			return;
		}

		CommitHostCadence( profile.MeleeCooldown, hostNow );
		BroadcastMeleeSwingAnimation();

		var target = FindMeleeTarget(
			attacker,
			controller,
			profile.MeleeRange,
			profile.MeleeAimAssist );
		var result = ResolveAttack( attacker, target, profile.MeleeDamage );
		ReceiveMeleeResult( ownerSwingSequence, result );
	}

	private LargeLadMeleeResult ResolveAttack(
		LargeLadPlayer attacker,
		MeleeTarget target,
		float damageAmount )
	{
		if ( target is null )
		{
			Log.Info( $"{attacker.GameObject.Name} swung and missed." );
			return LargeLadMeleeResult.Miss;
		}

		var damage = new LargeLadDamageContext
		{
			Attacker = GameObject,
			AttackerRole = attacker.Role,
			SourceWeapon = LargeLadWeaponId.Melee,
			DamageType = LargeLadDamageType.Melee,
			BaseDamage = damageAmount
		};

		if ( target.Barricade is not null )
		{
			if ( !target.Barricade.TryApplyDamage(
				damage,
				out var structuralDamage ) )
			{
				Log.Info(
					$"{attacker.GameObject.Name} struck " +
					$"{target.Barricade.AuthoredTarget.Name}, but could not damage it." );
				return LargeLadMeleeResult.Miss;
			}

			Log.Info(
				$"{attacker.GameObject.Name} damaged " +
				$"{target.Barricade.AuthoredTarget.Name} for " +
				$"{structuralDamage.AppliedDamage:0.#}." );
			return LargeLadMeleeResult.BarricadeHit;
		}

		var victim = target.Player;

		if ( victim is null )
			return LargeLadMeleeResult.Miss;

		var killed = victim.Health.TryApplyDamage(
			damage,
			out var appliedDamage );

		if ( !killed )
		{
			Log.Info(
				$"{attacker.GameObject.Name} hit {victim.GameObject.Name} for " +
				$"{appliedDamage.AppliedDamage:0.#} damage. " +
				$"{victim.Health.CurrentHealth:0.#}/" +
				$"{victim.Health.MaximumHealth:0.#} health remains." );
			return appliedDamage.AppliedDamage > 0.0f
				? LargeLadMeleeResult.PlayerHit
				: LargeLadMeleeResult.Miss;
		}

		Log.Info(
			$"{attacker.GameObject.Name} killed {victim.GameObject.Name}." );
		return LargeLadMeleeResult.PlayerHit;
	}

	private MeleeTarget FindMeleeTarget(
		LargeLadPlayer attacker,
		PlayerController controller,
		float range,
		bool useAimAssist )
	{
		var start = controller.EyePosition;
		var forward = controller.EyeTransform.Rotation.Forward;
		var trace = Scene.Trace
			.Ray( start, start + forward * range )
			.Radius( System.MathF.Max( 0.0f, SwingTraceRadius ) )
			.UseHitboxes( true )
			.IgnoreGameObjectHierarchy( attacker.GameObject )
			.Run();

		if ( trace.Hit )
		{
			var directPlayer = trace.GameObject?.Components.Get<LargeLadPlayer>(
				FindMode.EverythingInSelfAndAncestors );

			if ( IsValidPlayerTarget( attacker, directPlayer ) )
				return MeleeTarget.ForPlayer( directPlayer );

			var directBarricade =
				LargeLadBarricade.FindFor( Scene, trace.GameObject );

			if ( directBarricade is not null && !directBarricade.IsDestroyed )
				return MeleeTarget.ForBarricade( directBarricade );

			// World geometry, friendly players, and unrelated colliders block
			// aim assist from selecting something behind them.
			return null;
		}

		return useAimAssist
			? FindAimAssistedTarget( attacker, controller, range )
			: null;
	}

	private MeleeTarget FindAimAssistedTarget(
		LargeLadPlayer attacker,
		PlayerController controller,
		float range )
	{
		var start = controller.EyePosition;
		var forward = controller.EyeTransform.Rotation.Forward;
		MeleeTarget bestTarget = null;
		var bestScore = float.MaxValue;

		foreach ( var player in
			GetGameManager()?.ActivePlayers ??
			System.Array.Empty<LargeLadPlayer>() )
		{
			if ( !IsValidPlayerTarget( attacker, player ) )
				continue;

			var targetPosition =
				player.GameObject.WorldPosition + Vector3.Up * 48.0f;

			if ( !TryScoreTarget(
				start,
				forward,
				targetPosition,
				range,
				out var score ) ||
				!HasLineOfSightToPlayer( attacker, player, start, targetPosition ) )
			{
				continue;
			}

			if ( score < bestScore )
			{
				bestScore = score;
				bestTarget = MeleeTarget.ForPlayer( player );
			}
		}

		foreach ( var barricade in Scene.GetAllComponents<LargeLadBarricade>() )
		{
			if ( barricade is null || barricade.IsDestroyed )
				continue;

			var targetPosition = barricade.GetClosestWorldPoint( start );

			if ( !TryScoreTarget(
				start,
				forward,
				targetPosition,
				range,
				out var score ) ||
				!HasLineOfSightToBarricade(
					attacker,
					barricade,
					start,
					targetPosition ) )
			{
				continue;
			}

			if ( score < bestScore )
			{
				bestScore = score;
				bestTarget = MeleeTarget.ForBarricade( barricade );
			}
		}

		return bestTarget;
	}

	private bool TryScoreTarget(
		Vector3 start,
		Vector3 forward,
		Vector3 targetPosition,
		float range,
		out float score )
	{
		score = float.MaxValue;
		var toTarget = targetPosition - start;
		var distanceSquared = toTarget.LengthSquared;

		if ( distanceSquared > range * range )
			return false;

		if ( distanceSquared <= 0.001f )
		{
			score = 0.0f;
			return true;
		}

		var facing = Vector3.Dot( forward, toTarget.Normal );

		if ( facing < MinimumFacingDot )
			return false;

		var distance = System.MathF.Sqrt( distanceSquared );

		// Centered targets win over off-axis targets, with distance breaking
		// close calls. This keeps assistance predictable in crowded fights.
		score = (1.0f - facing) * range * 2.0f + distance;
		return true;
	}

	private bool HasLineOfSightToPlayer(
		LargeLadPlayer attacker,
		LargeLadPlayer target,
		Vector3 start,
		Vector3 targetPosition )
	{
		var trace = Scene.Trace
			.Ray( start, targetPosition )
			.UseHitboxes( true )
			.IgnoreGameObjectHierarchy( attacker.GameObject )
			.Run();
		var hitPlayer = trace.GameObject?.Components.Get<LargeLadPlayer>(
			FindMode.EverythingInSelfAndAncestors );

		return hitPlayer == target;
	}

	private bool HasLineOfSightToBarricade(
		LargeLadPlayer attacker,
		LargeLadBarricade target,
		Vector3 start,
		Vector3 targetPosition )
	{
		var towardTarget = targetPosition - start;
		var traceEnd = towardTarget.LengthSquared > 0.001f
			? targetPosition + towardTarget.Normal * 4.0f
			: targetPosition;
		var trace = Scene.Trace
			.Ray( start, traceEnd )
			.UseHitboxes( true )
			.IgnoreGameObjectHierarchy( attacker.GameObject )
			.Run();

		return LargeLadBarricade.FindFor( Scene, trace.GameObject ) == target;
	}

	private bool CanAttack(
		LargeLadPlayer attacker,
		PlayerController controller )
	{
		if ( attacker is null || controller is null ||
			attacker.Role is not (LargeLadRole.SkinnyKid or
				LargeLadRole.LargeLad or LargeLadRole.Minion) ||
			attacker.EquippedWeapon != LargeLadWeaponId.Melee ||
			attacker.Health?.IsDead != false ||
			attacker.Health.CurrentHealth <= 0.0f ||
			attacker.MovementLocked )
		{
			return false;
		}

		var phase = GetGameManager()?.Phase;

		if ( phase == LargeLadRoundPhase.Playing )
			return true;

		// Skinny Kids can use their head start to break early progression
		// barricades. Hunters remain unable to attack until play begins.
		return phase == LargeLadRoundPhase.HeadStart &&
			attacker.Role == LargeLadRole.SkinnyKid;
	}

	private static bool IsValidPlayerTarget(
		LargeLadPlayer attacker,
		LargeLadPlayer target )
	{
		if ( attacker is null || target is null || target == attacker ||
			target.Health?.IsDead != false ||
			target.Health.CurrentHealth <= 0.0f )
		{
			return false;
		}

		return attacker.Role == LargeLadRole.SkinnyKid
			? target.Role is LargeLadRole.LargeLad or LargeLadRole.Minion
			: target.Role == LargeLadRole.SkinnyKid;
	}

	private void CommitHostCadence( float cooldown, float hostNow )
	{
		cooldown = System.MathF.Max( 0.01f, cooldown );

		if ( !hasHostSwingSchedule )
		{
			hasHostSwingSchedule = true;
			nextHostSwingTime = hostNow + cooldown;
			return;
		}

		nextHostSwingTime =
			System.MathF.Max( hostNow, nextHostSwingTime ) + cooldown;
	}

	[Rpc.Owner( NetFlags.HostOnly )]
	private void ReceiveMeleeResult(
		int ownerSwingSequence,
		LargeLadMeleeResult result )
	{
		if ( ownerSwingSequence <= lastOwnerResultSequence )
			return;

		lastOwnerResultSequence = ownerSwingSequence;
		LastAttackResult = result;

		if ( result is not (LargeLadMeleeResult.PlayerHit or
			LargeLadMeleeResult.BarricadeHit) )
		{
			return;
		}

		hasConfirmedHit = true;
		timeSinceConfirmedHit = 0.0f;
	}

	private void UpdateMeleeModel()
	{
		var player = Components.Get<LargeLadPlayer>();

		if ( player?.Role is LargeLadRole.LargeLad or
			LargeLadRole.Minion )
		{
			DestroyMeleeModel();
			return;
		}

		var shouldShow = MeleeModel is not null &&
			player?.Role == LargeLadRole.SkinnyKid &&
			player.EquippedWeapon == LargeLadWeaponId.Melee &&
			player.Health?.IsDead == false;

		if ( !shouldShow )
		{
			if ( meleeModelPivotObject is not null &&
				meleeModelPivotObject.IsValid )
			{
				meleeModelPivotObject.Enabled = false;
			}

			return;
		}

		EnsureMeleeModel();

		if ( meleeModelPivotObject is null ||
			!meleeModelPivotObject.IsValid ||
			meleeModelObject is null ||
			!meleeModelObject.IsValid )
		{
			return;
		}

		meleeModelPivotObject.Enabled = true;

		var controller = Components.Get<PlayerController>();
		var useFirstPersonGrip = !IsProxy &&
			controller?.ThirdPerson == false &&
			firstPersonArmsObject is not null &&
			firstPersonArmsObject.IsValid &&
			firstPersonArmsObject.Enabled &&
			firstPersonArmsRenderer is not null;
		var holdRenderer = useFirstPersonGrip
			? firstPersonArmsRenderer
			: player.BodyRenderer;

		if ( holdRenderer is null ||
			string.IsNullOrWhiteSpace( MeleeModelBone ) ||
			!holdRenderer.TryGetBoneTransform(
				MeleeModelBone,
				out var holdTransform ) )
		{
			meleeModelPivotObject.Enabled = false;
			return;
		}

		var position = holdTransform.PointToWorld( MeleeModelPosition );
		var rotation =
			holdTransform.Rotation * MeleeModelAngles.ToRotation();

		var scale = System.MathF.Max( 0.01f, MeleeModelScale );

		meleeModelRenderer.RenderOptions.Game = !useFirstPersonGrip;
		meleeModelRenderer.RenderOptions.Overlay = useFirstPersonGrip;

		// Keep the authored grip point locked to the hand at every scale.
		meleeModelPivotObject.WorldTransform = new Transform(
			position,
			rotation,
			Vector3.One );
		meleeModelObject.LocalTransform = new Transform(
			-MeleeModelGripPosition * scale,
			Rotation.Identity,
			new Vector3( scale ) );
	}

	private void UpdateFirstPersonArms()
	{
		var player = Components.Get<LargeLadPlayer>();
		var controller = Components.Get<PlayerController>();
		var shouldShow = ShowFirstPersonArms &&
			!IsProxy &&
			controller?.ThirdPerson == false &&
			(player?.Role is LargeLadRole.SkinnyKid or
				LargeLadRole.LargeLad or LargeLadRole.Minion) &&
			player.EquippedWeapon == LargeLadWeaponId.Melee &&
			player.Health?.IsDead == false &&
			player.BodyRenderer is not null;

		if ( !shouldShow )
		{
			if ( firstPersonArmsObject is not null &&
				firstPersonArmsObject.IsValid )
			{
				firstPersonArmsObject.Enabled = false;
			}

			return;
		}

		EnsureFirstPersonArms( player );

		if ( firstPersonArmsObject is null ||
			!firstPersonArmsObject.IsValid ||
			firstPersonArmsRenderer is null )
		{
			return;
		}

		var bodyTransform = player.BodyRenderer.GameObject.WorldTransform;
		var eyeTransform = controller.EyeTransform;
		bodyTransform.Position +=
			eyeTransform.Rotation.Forward * FirstPersonArmsPosition.x +
			eyeTransform.Rotation.Right * FirstPersonArmsPosition.y +
			eyeTransform.Rotation.Up * FirstPersonArmsPosition.z;
		bodyTransform.Rotation *= FirstPersonArmsAngles.ToRotation();

		firstPersonArmsObject.Enabled = true;
		firstPersonArmsObject.WorldTransform = bodyTransform;
		firstPersonArmsRenderer.BoneMergeTarget = player.BodyRenderer;
		firstPersonArmsRenderer.Tint = player.BodyRenderer.Tint;
	}

	private void EnsureFirstPersonArms( LargeLadPlayer player )
	{
		var bodyRenderer = player?.BodyRenderer;
		var bodyModel = bodyRenderer?.Model;

		if ( bodyModel is null )
			return;

		if ( firstPersonArmsObject is null ||
			!firstPersonArmsObject.IsValid )
		{
			firstPersonArmsObject = new GameObject(
				GameObject,
				true,
				"First Person Melee Arms (Runtime)" )
			{
				NetworkMode = NetworkMode.Never
			};
			firstPersonArmsRenderer =
				firstPersonArmsObject.Components
					.Create<SkinnedModelRenderer>();
			appliedFirstPersonArmsModel = null;
		}

		if ( firstPersonArmsRenderer is null ||
			appliedFirstPersonArmsModel == bodyModel )
		{
			return;
		}

		firstPersonArmsRenderer.Model = bodyModel;
		firstPersonArmsRenderer.BoneMergeTarget = bodyRenderer;
		firstPersonArmsRenderer.SetBodyGroup( "Head", 5 );
		firstPersonArmsRenderer.SetBodyGroup( "Chest", 0 );
		firstPersonArmsRenderer.SetBodyGroup( "Legs", 1 );
		firstPersonArmsRenderer.SetBodyGroup( "Hands", 0 );
		firstPersonArmsRenderer.SetBodyGroup( "Feet", 1 );
		firstPersonArmsRenderer.RenderOptions.Game = false;
		firstPersonArmsRenderer.RenderOptions.Overlay = true;
		firstPersonArmsRenderer.RenderOptions.Bloom = false;
		firstPersonArmsRenderer.RenderOptions.AfterUI = false;
		appliedFirstPersonArmsModel = bodyModel;
	}

	private void DestroyFirstPersonArms()
	{
		if ( firstPersonArmsObject is not null &&
			firstPersonArmsObject.IsValid )
		{
			firstPersonArmsObject.Destroy();
		}

		firstPersonArmsObject = null;
		firstPersonArmsRenderer = null;
		appliedFirstPersonArmsModel = null;
	}

	private void UpdateMeleePose()
	{
		var player = Components.Get<LargeLadPlayer>();

		if ( player?.BodyRenderer is null )
			return;

		var isMeleeReadied = player.EquippedWeapon ==
			LargeLadWeaponId.Melee &&
			player.Health?.IsDead == false;
		var holdType = player.Role switch
		{
			LargeLadRole.SkinnyKid when isMeleeReadied =>
				(int)CitizenAnimationHelper.HoldTypes.Swing,
			LargeLadRole.LargeLad when isMeleeReadied =>
				(int)CitizenAnimationHelper.HoldTypes.Punch,
			LargeLadRole.Minion when isMeleeReadied =>
				(int)CitizenAnimationHelper.HoldTypes.Punch,
			_ => (int)CitizenAnimationHelper.HoldTypes.None
		};

		player.BodyRenderer.Set( "holdtype", holdType );
	}

	private void TriggerMeleeSwingAnimation()
	{
		var player = Components.Get<LargeLadPlayer>();
		player?.BodyRenderer?.Set( "b_attack", true );
	}

	[Rpc.Broadcast]
	private void BroadcastMeleeSwingAnimation()
	{
		// The owner already predicted this animation immediately on input.
		if ( !IsProxy )
			return;

		TriggerMeleeSwingAnimation();
	}

	private void EnsureMeleeModel()
	{
		if ( MeleeModel is null )
		{
			if ( meleeModelPivotObject is not null &&
				meleeModelPivotObject.IsValid )
			{
				meleeModelPivotObject.Enabled = false;
			}

			appliedMeleeModel = null;
			return;
		}

		if ( meleeModelPivotObject is null ||
			!meleeModelPivotObject.IsValid )
		{
			meleeModelPivotObject = new GameObject(
				GameObject,
				true,
				"Melee Grip (Runtime)" )
			{
				NetworkMode = NetworkMode.Never
			};
			meleeModelObject = null;
			meleeModelRenderer = null;
			appliedMeleeModel = null;
		}

		if ( meleeModelObject is null || !meleeModelObject.IsValid )
		{
			meleeModelObject = new GameObject(
				meleeModelPivotObject,
				true,
				"Melee Model (Runtime)" )
			{
				NetworkMode = NetworkMode.Never
			};
			meleeModelRenderer =
				meleeModelObject.Components.Create<ModelRenderer>();
			appliedMeleeModel = null;
		}

		if ( meleeModelRenderer is not null &&
			appliedMeleeModel != MeleeModel )
		{
			meleeModelRenderer.Model = MeleeModel;
			appliedMeleeModel = MeleeModel;
		}
	}

	private void DestroyMeleeModel()
	{
		if ( meleeModelPivotObject is not null &&
			meleeModelPivotObject.IsValid )
		{
			meleeModelPivotObject.Destroy();
		}
		else if ( meleeModelObject is not null &&
			meleeModelObject.IsValid )
		{
			meleeModelObject.Destroy();
		}

		meleeModelPivotObject = null;
		meleeModelObject = null;
		meleeModelRenderer = null;
		appliedMeleeModel = null;
	}

	private LargeLadGameManager GetGameManager()
	{
		return LargeLadGameManager.FindForScene( Scene );
	}

	private sealed class MeleeTarget
	{
		public LargeLadPlayer Player { get; private init; }
		public LargeLadBarricade Barricade { get; private init; }

		public static MeleeTarget ForPlayer( LargeLadPlayer player )
		{
			return new MeleeTarget { Player = player };
		}

		public static MeleeTarget ForBarricade(
			LargeLadBarricade barricade )
		{
			return new MeleeTarget { Barricade = barricade };
		}
	}
}
