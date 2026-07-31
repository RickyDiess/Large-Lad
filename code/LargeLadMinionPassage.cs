using Sandbox;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// One round's focused cover-destruction edge. Reset rearms the event without
/// coupling the vent to the general barricade system.
/// </summary>
public sealed class LargeLadMinionPassageCoverGate
{
	public bool HasCommittedDestruction { get; private set; }

	public bool TryCommitDestruction()
	{
		if ( HasCommittedDestruction )
			return false;

		HasCommittedDestruction = true;
		return true;
	}

	public void ResetForRound()
	{
		HasCommittedDestruction = false;
	}
}

/// <summary>
/// A focused Minion-only vent opening. One persistent opening collider blocks
/// everyone while the optional cover is intact, then becomes Minion-only when
/// the cover is absent or destroyed. No second collision shell is required.
/// </summary>
[Description(
	"Host-authoritative Minion-only vent-opening gate with an optional focused " +
	"cover. One collider handles both the intact and Minion-only states." )]
public sealed class LargeLadMinionPassage :
	LargeLadRoundResettableComponent,
	ILargeLadDamageable
{
	public const float DefaultCoverHealth = 50.0f;
	public const float AutomaticExitClearance = 24.0f;

	[Property, Group( "Opening" ), Title( "Opening Collider" )]
	[Description(
		"Single solid collider on this GameObject, placed across the vent " +
		"opening with its thin axis along local Forward. " +
		"It blocks everyone while the optional cover is intact, then uses the " +
		"large_lad_minion_passage tag so only Minions can pass." )]
	public Collider OpeningCollider { get; set; }

	[Property, Group( "Optional Cover" ), Title( "Enable Breakable Cover" )]
	[Description(
		"When enabled, the intact cover blocks everyone until Minion melee " +
		"destroys it. The same opening collider then becomes Minion-only." )]
	public bool EnableBreakableCover { get; set; }

	[Property, Group( "Optional Cover" ), Title( "Base Maximum Health" )]
	public float BaseCoverHealth { get; set; } = DefaultCoverHealth;

	[Property, Group( "Optional Cover" ), Title( "Intact Cover Root" )]
	[Description(
		"Required when the cover is enabled. This must be the visual-only child " +
		"of this GameObject; it is hidden on destruction and restored each " +
		"round." )]
	public GameObject CoverRoot { get; set; }

	[Property, Group( "Optional Cover" )]
	[Description(
		"Optional presentation enabled after destruction and disabled on reset." )]
	public GameObject BrokenCoverVisual { get; set; }

	[Property, Group( "Optional Cover" )]
	[Description(
		"Optional authored Prop used to create model gibs on final destruction." )]
	public Prop CoverProp { get; set; }

	[Property, Group( "Optional Cover" )]
	public SoundEvent CoverHitSound { get; set; }

	[Property, Group( "Optional Cover" )]
	public SoundEvent CoverBreakSound { get; set; }

	[Property, Title( "Editor Gizmo Padding" )]
	public float GizmoPadding { get; set; } = 2.0f;

	[Sync( SyncFlags.FromHost )]
	public float CurrentCoverHealth { get; private set; }

	[Sync( SyncFlags.FromHost ),
		Change( nameof( OnCoverDestroyedChanged ) )]
	public bool IsCoverDestroyed { get; private set; }

	[Sync( SyncFlags.FromHost ),
		Change( nameof( OnPassageOpenChanged ) )]
	public bool IsPassageOpen { get; private set; }

	public event System.Action<
		LargeLadMinionPassage,
		LargeLadDamageContext> AuthoritativeCoverDamaged;

	public event System.Action<
		LargeLadMinionPassage> AuthoritativeCoverDestroyed;

	public bool HasActiveCover =>
		EnableBreakableCover &&
		!IsCoverDestroyed &&
		CurrentCoverHealth > 0.0f;

	private readonly LargeLadMinionPassageCoverGate destructionGate = new();
	private readonly Dictionary<LargeLadPlayer, bool> safetyHolds = new();
	private readonly HashSet<LargeLadPlayer> playersBeingEjected = new();
	private bool hasCapturedAuthoredState;
	private bool authoredOpeningColliderEnabled;
	private bool authoredOpeningHasPassageTag;
	private Transform authoredCoverTransform;
	private bool coverGibsCreated;
	private bool appliedCoverEnabled;
	private bool? appliedCoverDestroyed;
	private bool? appliedPassageOpen;

	public static LargeLadMinionPassage FindCoverFor( GameObject target )
	{
		var passage = target?.Components.Get<LargeLadMinionPassage>(
			FindMode.EverythingInSelfAndAncestors );
		return passage?.IsCoverTarget( target ) == true
			? passage
			: null;
	}

	public Vector3 GetClosestCoverWorldPoint( Vector3 worldPoint )
	{
		if ( OpeningCollider is not null )
			return OpeningCollider.FindClosestPoint( worldPoint );

		return CoverRoot?.WorldPosition ?? GameObject.WorldPosition;
	}

	protected override void OnAwake()
	{
		ResolveReferences();
	}

	protected override void OnStart()
	{
		ResolveReferences();
		CaptureAuthoredState();

		if ( Networking.IsHost )
			ResetForRound();
		else
			RefreshPresentation();
	}

	protected override void OnDisabled()
	{
		ReleaseAllSafetyHolds();
		base.OnDisabled();
	}

	protected override void OnDestroy()
	{
		ReleaseAllSafetyHolds();
		base.OnDestroy();
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost )
			return;

		playersBeingEjected.RemoveWhere(
			player =>
				player is null ||
				!player.IsValid ||
				!IsPlayerTouching( player ) );

		foreach ( var player in safetyHolds.Keys.ToList() )
		{
			if ( player is null || !player.IsValid )
			{
				safetyHolds.Remove( player );
				continue;
			}

			if ( !player.PassageSafetyHeld )
			{
				safetyHolds.Remove( player );
				continue;
			}

			if ( player.Health?.IsDead == true )
			{
				ReleaseSafetyHold( player );
				continue;
			}

			TryEjectPlayer( player, player.Role );
		}

		var manager = LargeLadGameManager.FindForScene( Scene );

		foreach ( var player in
			manager?.ActivePlayers ??
			System.Array.Empty<LargeLadPlayer>() )
		{
			if ( player is null ||
				LargeLadGameplayRules.CanTraverseMinionPassage(
					player.Role ) ||
				!IsPlayerEmbeddedInOpening( player ) )
			{
				continue;
			}

			TryEjectPlayer( player, player.Role );
		}
	}

	protected override void OnUpdate()
	{
		if ( appliedCoverEnabled == EnableBreakableCover &&
			appliedCoverDestroyed == IsCoverDestroyed &&
			appliedPassageOpen == IsPassageOpen )
		{
			return;
		}

		RefreshPresentation();
	}

	protected override void OnValidate()
	{
		ResolveReferences();

		foreach ( var warning in GetValidationWarnings(
			validateGeometry: false ) )
		{
			Log.Warning(
				$"{GameObject.Name}: invalid Minion passage: {warning}" );
		}
	}

	internal void PreparePlayerRoleCollisionChange(
		LargeLadPlayer player,
		LargeLadRole oldRole,
		LargeLadRole newRole )
	{
		if ( !Networking.IsHost ||
			player is null ||
			LargeLadGameplayRules.CanTraverseMinionPassage( newRole ) ||
			!IsPlayerTouching( player ) )
		{
			return;
		}

		TryEjectPlayer( player, newRole );
	}

	public bool TryApplyDamage(
		LargeLadDamageContext damage,
		out LargeLadDamageContext appliedDamage )
	{
		appliedDamage = damage.WithAppliedDamage( 0.0f );

		if ( !Networking.IsHost ||
			!HasActiveCover ||
			!LargeLadGameplayRules.CanDamageMinionPassageCover(
				damage.AttackerRole,
				damage.DamageType ) )
		{
			return false;
		}

		var amount = System.MathF.Max( 0.0f, damage.BaseDamage );

		if ( amount <= 0.0f )
			return false;

		CurrentCoverHealth =
			System.MathF.Max( 0.0f, CurrentCoverHealth - amount );
		appliedDamage = damage.WithAppliedDamage( amount );
		var destroyed = CurrentCoverHealth <= 0.0f;

		if ( destroyed )
		{
			IsCoverDestroyed = true;
			IsPassageOpen = true;
		}

		RefreshPresentation();
		ReceiveCoverImpact( destroyed );
		BroadcastCoverImpact( destroyed );
		AuthoritativeCoverDamaged?.Invoke( this, appliedDamage );

		if ( destroyed && destructionGate.TryCommitDestruction() )
			AuthoritativeCoverDestroyed?.Invoke( this );

		return true;
	}

	public override void ResetForRound()
	{
		if ( !Networking.IsHost )
			return;

		ResolveReferences();
		CaptureAuthoredState();

		// A reappearing cover must not be created around an occupant.
		if ( EnableBreakableCover )
		{
			foreach ( var player in GetTouchingPlayers() )
				TryEjectPlayer( player, player.Role );
		}

		destructionGate.ResetForRound();
		coverGibsCreated = false;
		CurrentCoverHealth = EnableBreakableCover
			? System.MathF.Max( 1.0f, BaseCoverHealth )
			: 0.0f;
		IsCoverDestroyed = false;
		IsPassageOpen =
			LargeLadGameplayRules.IsMinionPassageOpen(
				EnableBreakableCover,
				IsCoverDestroyed );
		RestoreCoverTransform();
		RefreshPresentation();
	}

	public IReadOnlyList<string> GetValidationWarnings(
		bool validateGeometry )
	{
		var warnings = new List<string>();

		if ( OpeningCollider is null )
		{
			warnings.Add(
				"add and assign one solid collider on this vent-opening " +
				"GameObject." );
		}
		else
		{
			if ( OpeningCollider.IsTrigger )
			{
				warnings.Add(
					"the opening collider must be solid, not a trigger." );
			}

			var openingStartsEnabled = hasCapturedAuthoredState
				? authoredOpeningColliderEnabled
				: OpeningCollider.Enabled;

			if ( !openingStartsEnabled )
			{
				warnings.Add(
					"the opening collider must start enabled." );
			}

			if ( !OpeningCollider.GameObject.IsStatic )
			{
				warnings.Add(
					"the opening collider must be static." );
			}

			if ( OpeningCollider.GameObject != GameObject )
			{
				warnings.Add(
					"the opening collider must be on the same GameObject as " +
					"LargeLadMinionPassage; do not create a blocker child." );
			}

			var openingHasAuthoredPassageTag = hasCapturedAuthoredState
				? authoredOpeningHasPassageTag
				: OpeningCollider.GameObject.Tags.Has(
					LargeLadGameplayRules.MinionPassageTag );
			var tagIsIntentionallyRemovedAtRuntime =
				Scene?.IsEditor == false &&
				EnableBreakableCover &&
				!IsPassageOpen;

			if ( !openingHasAuthoredPassageTag &&
				!tagIsIntentionallyRemovedAtRuntime )
			{
				warnings.Add(
					$"the opening collider needs the authored " +
					$"'{LargeLadGameplayRules.MinionPassageTag}' tag." );
			}

		}

		if ( GameObject.NetworkMode != NetworkMode.Object )
		{
			warnings.Add(
				"the passage root must use Network Mode Object so cover state " +
				"and safety holds replicate." );
		}

		ValidateCollisionRules( warnings );

		if ( validateGeometry &&
			TryGetAutomaticExitPositions(
				out var exitA,
				out var exitB ) )
		{
			ValidateAutomaticExit( warnings, "Side A", exitA );
			ValidateAutomaticExit( warnings, "Side B", exitB );
		}

		if ( !float.IsFinite( BaseCoverHealth ) ||
			BaseCoverHealth <= 0.0f )
		{
			warnings.Add( "cover health must be finite and positive." );
		}

		if ( EnableBreakableCover )
		{
			if ( CoverRoot is null )
				warnings.Add( "enabled cover needs an intact cover root." );

			if ( OpeningCollider is null )
			{
				warnings.Add(
					"enabled cover needs the shared solid opening collider." );
			}
		}

		if ( CoverRoot is not null &&
			!IsInPassageHierarchy( CoverRoot ) )
		{
			warnings.Add(
				"the intact cover root must remain in this passage hierarchy." );
		}

		if ( CoverRoot is not null &&
			CoverRoot.Parent != GameObject )
		{
			warnings.Add(
				"the intact cover root must be a direct child of the vent-opening " +
				"GameObject." );
		}

		if ( CoverRoot?.Components.Get<Collider>(
			FindMode.EverythingInSelfAndDescendants ) is not null )
		{
			warnings.Add(
				"the intact cover hierarchy should be visual-only and must not " +
				"add a second collider; the opening collider handles both " +
				"cover states." );
		}

		if ( BrokenCoverVisual is not null &&
			!IsInPassageHierarchy( BrokenCoverVisual ) )
		{
			warnings.Add(
				"the broken-cover visual must remain in this passage hierarchy." );
		}

		return warnings;
	}

	private void ValidateCollisionRules( List<string> warnings )
	{
		var collision = ProjectSettings.Collision;

		if ( collision is null )
			return;

		if ( collision.GetCollisionRule(
			LargeLadGameplayRules.MinionPassageTag,
			LargeLadGameplayRules.MinionBodyTag ) !=
			Sandbox.Physics.CollisionRules.Result.Ignore )
		{
			warnings.Add(
				"project collision rules must ignore Minion bodies against " +
				"the Minion-passage tag." );
		}

		if ( collision.GetCollisionRule(
			LargeLadGameplayRules.MinionPassageTag,
			LargeLadGameplayRules.HunterBodyTag ) !=
			Sandbox.Physics.CollisionRules.Result.Collide )
		{
			warnings.Add(
				"project collision rules must keep the Large Lad solid " +
				"against Minion passages." );
		}

		if ( collision.GetCollisionRule(
			LargeLadGameplayRules.MinionPassageTag,
			LargeLadGameplayRules.SoftPlayerBodyTag ) !=
			Sandbox.Physics.CollisionRules.Result.Collide )
		{
			warnings.Add(
				"project collision rules must keep Skinny Kids and lobby " +
				"players solid against Minion passages." );
		}
	}

	private void ValidateAutomaticExit(
		List<string> warnings,
		string label,
		Vector3 exit )
	{
		if ( !IsExitClear( exit ) )
		{
			warnings.Add(
				$"the automatic {label} exit does not have a clear 32-by-72 " +
				"player capsule. Move the vent-opening object or clear nearby " +
				"world geometry." );
		}
	}

	private bool TryEjectPlayer(
		LargeLadPlayer player,
		LargeLadRole targetRole )
	{
		if ( player is null ||
			!player.IsValid ||
			player.Health?.IsDead == true )
		{
			return false;
		}

		if ( playersBeingEjected.Contains( player ) &&
			!safetyHolds.ContainsKey( player ) )
		{
			return true;
		}

		if ( TryGetAutomaticExitPositions(
			out var exitA,
			out var exitB ) )
		{
			var preferred =
				LargeLadGameplayRules
					.GetPreferredMinionPassageExitIndex(
						player.GameObject.WorldPosition,
						exitA,
						exitB );
			var exits = preferred == 0
				? new[] { exitA, exitB }
				: new[] { exitB, exitA };

			foreach ( var exit in exits )
			{
				if ( !IsExitClear( exit ) )
					continue;

				if ( player.RelocateForPassage(
					exit,
					GameObject.WorldRotation ) )
				{
					playersBeingEjected.Add( player );
					ReleaseSafetyHold( player );
					return true;
				}
			}
		}

		var manager = LargeLadGameManager.FindForScene( Scene );

		if ( manager?.TryAllocateSpawn(
			LargeLadGameplayRules.GetSpawnGroupForRole( targetRole ),
			player,
			out var spawn ) == true &&
			player.RelocateForPassage( spawn.Position, spawn.Rotation ) )
		{
			playersBeingEjected.Add( player );
			ReleaseSafetyHold( player );
			return true;
		}

		if ( !safetyHolds.ContainsKey( player ) )
			safetyHolds.Add( player, player.MovementLocked );

		player.SetPassageSafetyHold( true );
		return false;
	}

	private bool IsPlayerTouching( LargeLadPlayer player )
	{
		if ( OpeningCollider is null ||
			player is null ||
			!player.IsValid )
		{
			return false;
		}

		var bounds = OpeningCollider.GetWorldBounds();
		var position = player.GameObject.WorldPosition;
		var radius = LargeLadGameplayRules.PlayerBodyRadius;
		var bodyMins = new Vector3(
			position.x - radius,
			position.y - radius,
			position.z );
		var bodyMaxs = new Vector3(
			position.x + radius,
			position.y + radius,
			position.z + LargeLadGameplayRules.PlayerBodyHeight );

		return BoundsOverlap(
			bounds.Mins,
			bounds.Maxs,
			bodyMins,
			bodyMaxs );
	}

	private bool IsPlayerEmbeddedInOpening( LargeLadPlayer player )
	{
		if ( OpeningCollider is null ||
			player is null ||
			!player.IsValid )
		{
			return false;
		}

		var bounds = OpeningCollider.GetWorldBounds();
		var position =
			player.GameObject.WorldPosition +
			Vector3.Up *
				(LargeLadGameplayRules.PlayerBodyHeight * 0.5f);

		return position.x >= bounds.Mins.x &&
			position.x <= bounds.Maxs.x &&
			position.y >= bounds.Mins.y &&
			position.y <= bounds.Maxs.y &&
			position.z >= bounds.Mins.z &&
			position.z <= bounds.Maxs.z;
	}

	private IReadOnlyList<LargeLadPlayer> GetTouchingPlayers()
	{
		if ( OpeningCollider is null )
			return System.Array.Empty<LargeLadPlayer>();

		var manager = LargeLadGameManager.FindForScene( Scene );
		var players =
			manager?.ActivePlayers ??
			Scene?.GetAllComponents<LargeLadPlayer>()?.ToList() ??
			new List<LargeLadPlayer>();

		return players
			.Where( IsPlayerTouching )
			.ToList();
	}

	private bool IsExitClear( Vector3 position )
	{
		if ( Scene is null )
			return false;

		var radius = LargeLadGameplayRules.PlayerBodyRadius;
		var capsule = new Capsule(
			position + Vector3.Up * radius,
			position +
				Vector3.Up *
					(LargeLadGameplayRules.PlayerBodyHeight - radius),
			radius - 0.5f );
		var clearance = Scene.Trace
			.Capsule( capsule )
			.WithoutTags( LargeLadGameplayRules.PlayerBodyTag )
			.Run();
		return !clearance.Hit && !clearance.StartedSolid;
	}

	private bool TryGetAutomaticExitPositions(
		out Vector3 exitA,
		out Vector3 exitB )
	{
		exitA = default;
		exitB = default;

		if ( OpeningCollider is null || !OpeningCollider.IsValid )
			return false;

		var bounds = OpeningCollider.GetWorldBounds();
		var forward = OpeningCollider.GameObject.WorldRotation.Forward;
		forward = new Vector3( forward.x, forward.y, 0.0f );

		if ( forward.LengthSquared <= 0.0001f )
			return false;

		forward = forward.Normal;
		var halfExtents = (bounds.Maxs - bounds.Mins) * 0.5f;
		var halfDepth =
			System.MathF.Abs( forward.x ) * halfExtents.x +
			System.MathF.Abs( forward.y ) * halfExtents.y;
		var origin = (bounds.Mins + bounds.Maxs) * 0.5f;
		origin.z = bounds.Mins.z;
		var offset =
			System.MathF.Max( 0.0f, halfDepth ) +
			AutomaticExitClearance;

		exitA = origin - forward * offset;
		exitB = origin + forward * offset;
		return true;
	}

	private static bool BoundsOverlap(
		Vector3 leftMins,
		Vector3 leftMaxs,
		Vector3 rightMins,
		Vector3 rightMaxs )
	{
		return leftMins.x <= rightMaxs.x &&
			leftMaxs.x >= rightMins.x &&
			leftMins.y <= rightMaxs.y &&
			leftMaxs.y >= rightMins.y &&
			leftMins.z <= rightMaxs.z &&
			leftMaxs.z >= rightMins.z;
	}

	private void ReleaseSafetyHold( LargeLadPlayer player )
	{
		if ( player is null ||
			!safetyHolds.Remove( player, out var previousMovementLock ) )
		{
			return;
		}

		if ( player.IsValid )
		{
			player.SetPassageSafetyHold(
				false,
				previousMovementLock );
		}
	}

	private void ReleaseAllSafetyHolds()
	{
		foreach ( var player in safetyHolds.Keys.ToList() )
			ReleaseSafetyHold( player );

		playersBeingEjected.Clear();
	}

	private bool IsCoverTarget( GameObject target )
	{
		if ( target is null ||
			!EnableBreakableCover ||
			IsCoverDestroyed ||
			OpeningCollider is null )
		{
			return false;
		}

		return target == OpeningCollider.GameObject ||
			(CoverRoot is not null &&
				(target == CoverRoot ||
					CoverRoot.IsDescendant( target )));
	}

	private bool IsInPassageHierarchy( GameObject target )
	{
		return target is not null &&
			(target == GameObject || GameObject.IsDescendant( target ));
	}

	private void ResolveReferences()
	{
		OpeningCollider ??= Components.Get<Collider>(
			FindMode.EverythingInSelf );

		if ( CoverRoot is not null )
		{
			CoverProp ??= CoverRoot.Components.Get<Prop>(
				FindMode.EverythingInSelfAndDescendants );
		}
	}

	private void CaptureAuthoredState()
	{
		if ( hasCapturedAuthoredState )
			return;

		authoredOpeningColliderEnabled =
			OpeningCollider?.Enabled == true;
		authoredOpeningHasPassageTag =
			OpeningCollider?.GameObject?.Tags.Has(
				LargeLadGameplayRules.MinionPassageTag ) == true;
		authoredCoverTransform = CoverRoot?.LocalTransform ?? default;
		hasCapturedAuthoredState = true;
	}

	private void RestoreCoverTransform()
	{
		if ( !hasCapturedAuthoredState ||
			CoverRoot is null ||
			!CoverRoot.IsValid )
		{
			return;
		}

		CoverRoot.LocalTransform = authoredCoverTransform;
	}

	private void RefreshPresentation()
	{
		if ( !hasCapturedAuthoredState )
			return;

		var intact =
			EnableBreakableCover &&
			!IsCoverDestroyed;

		if ( EnableBreakableCover &&
			IsCoverDestroyed &&
			Networking.IsHost &&
			!coverGibsCreated &&
			CoverProp is not null &&
			CoverProp.IsValid )
		{
			coverGibsCreated = true;
			CoverProp.NetworkCreateGibs( false );
		}

		if ( CoverRoot is not null && CoverRoot.IsValid )
		{
			CoverRoot.Enabled = intact;

			var coverMesh = CoverRoot.Components.Get<MeshComponent>(
				FindMode.EverythingInSelfAndDescendants );

			if ( coverMesh is not null && coverMesh.IsValid )
				coverMesh.Enabled = intact;

			foreach ( var renderer in CoverRoot.Components.GetAll<Renderer>(
				FindMode.EverythingInSelfAndDescendants ) )
			{
				if ( renderer is not null && renderer.IsValid )
					renderer.Enabled = intact;
			}
		}

		if ( OpeningCollider is not null && OpeningCollider.IsValid )
		{
			// The opening uses one physical collider in every state. An intact
			// cover removes the Minion-ignore tag so it blocks everybody. An
			// absent or destroyed cover restores that tag, leaving the same
			// collider as the permanent role gate.
			OpeningCollider.Enabled = true;

			if ( !intact )
			{
				OpeningCollider.GameObject.Tags.Add(
					LargeLadGameplayRules.MinionPassageTag );
			}
			else
			{
				OpeningCollider.GameObject.Tags.Remove(
					LargeLadGameplayRules.MinionPassageTag );
			}
		}

		if ( BrokenCoverVisual is not null &&
			BrokenCoverVisual.IsValid )
		{
			BrokenCoverVisual.Enabled =
				EnableBreakableCover &&
				IsCoverDestroyed;
		}

		appliedCoverEnabled = EnableBreakableCover;
		appliedCoverDestroyed = IsCoverDestroyed;
		appliedPassageOpen = IsPassageOpen;
	}

	private void OnCoverDestroyedChanged(
		bool oldValue,
		bool newValue )
	{
		if ( oldValue && !newValue )
			RestoreCoverTransform();

		RefreshPresentation();
	}

	private void OnPassageOpenChanged(
		bool oldValue,
		bool newValue )
	{
		RefreshPresentation();
	}

	private void ReceiveCoverImpact( bool destroyed )
	{
		var sound = destroyed
			? CoverBreakSound
			: CoverHitSound;

		if ( sound is null )
			return;

		var position =
			OpeningCollider?.GameObject?.WorldPosition ??
			CoverRoot?.WorldPosition ??
			GameObject.WorldPosition;
		Sound.Play( sound, position );
	}

	[Rpc.Broadcast]
	private void BroadcastCoverImpact( bool destroyed )
	{
		if ( Networking.IsHost )
			return;

		ReceiveCoverImpact( destroyed );
	}

	protected override void DrawGizmos()
	{
		ResolveReferences();
		var padding =
			new Vector3( System.MathF.Max( 0.0f, GizmoPadding ) );

		DrawColliderGizmo(
			OpeningCollider,
			new Color( 0.78f, 0.25f, 1.0f ),
			padding );
		DrawAutomaticSafetyGizmo();

		if ( TryGetAutomaticExitPositions(
			out var exitA,
			out var exitB ) )
		{
			DrawExitGizmo( exitA, "Side A" );
			DrawExitGizmo( exitB, "Side B" );
		}
	}

	private static void DrawColliderGizmo(
		Collider collider,
		Color color,
		Vector3 padding )
	{
		if ( collider is null )
			return;

		var bounds = collider.GetWorldBounds();
		var padded = new BBox(
			bounds.Mins - padding,
			bounds.Maxs + padding );
		Gizmo.Transform = global::Transform.Zero;
		Gizmo.Draw.IgnoreDepth = true;
		Gizmo.Draw.Color = color.WithAlpha( 0.12f );
		Gizmo.Draw.SolidBox( padded );
		Gizmo.Draw.Color = color.WithAlpha( 0.95f );
		Gizmo.Draw.LineThickness = 2.0f;
		Gizmo.Draw.LineBBox( padded );
		Gizmo.Draw.LineThickness = 1.0f;
		Gizmo.Draw.IgnoreDepth = false;
	}

	private void DrawAutomaticSafetyGizmo()
	{
		if ( OpeningCollider is null )
			return;

		var bounds = OpeningCollider.GetWorldBounds();
		var radius = LargeLadGameplayRules.PlayerBodyRadius;
		var padded = new BBox(
			bounds.Mins - new Vector3( radius, radius, 0.0f ),
			bounds.Maxs + new Vector3( radius, radius, 0.0f ) );
		Gizmo.Transform = global::Transform.Zero;
		Gizmo.Draw.IgnoreDepth = true;
		Gizmo.Draw.Color =
			new Color( 0.15f, 0.85f, 0.95f, 0.08f );
		Gizmo.Draw.SolidBox( padded );
		Gizmo.Draw.Color =
			new Color( 0.15f, 0.85f, 0.95f, 0.65f );
		Gizmo.Draw.LineBBox( padded );
		Gizmo.Draw.IgnoreDepth = false;
	}

	private void DrawExitGizmo( Vector3 exit, string label )
	{
		var radius = LargeLadGameplayRules.PlayerBodyRadius;
		var clear = IsExitClear( exit );
		var color = clear
			? new Color( 0.2f, 1.0f, 0.35f )
			: new Color( 1.0f, 0.2f, 0.12f );
		var bottom = exit + Vector3.Up * radius;
		var top = exit +
			Vector3.Up *
				(LargeLadGameplayRules.PlayerBodyHeight - radius);
		Gizmo.Transform = global::Transform.Zero;
		Gizmo.Draw.IgnoreDepth = true;
		Gizmo.Draw.Color = color.WithAlpha( 0.18f );
		Gizmo.Draw.SolidCapsule( bottom, top, radius, 8, 4 );
		Gizmo.Draw.Color = color;
		Gizmo.Draw.Text(
			label,
			new Transform(
				exit +
					Vector3.Up *
						(LargeLadGameplayRules.PlayerBodyHeight + 12.0f) ),
			"Inter",
			13.0f );
		Gizmo.Draw.IgnoreDepth = false;
	}
}
