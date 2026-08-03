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
	[Description(
		"Extra world-space padding around the editor-only opening-gate gizmo." )]
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
	private bool hasCapturedAuthoredState;
	private bool authoredOpeningColliderEnabled;
	private bool authoredOpeningHasPassageTag;
	private Transform authoredCoverTransform;
	private bool coverGibsCreated;
	private bool appliedCoverEnabled;
	private bool? appliedCoverDestroyed;
	private bool? appliedPassageOpen;
	private Collider capturedOpeningCollider;
	private GameObject capturedCoverRoot;

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
		if ( OpeningCollider is not null && OpeningCollider.IsValid )
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

	protected override void OnUpdate()
	{
		if ( ResolveReferences() && hasCapturedAuthoredState )
			CaptureAuthoredState( true );

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

		foreach ( var warning in GetValidationWarnings() )
		{
			Log.Warning(
				$"{GameObject.Name}: invalid Minion passage: {warning}" );
		}
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

	public IReadOnlyList<string> GetValidationWarnings()
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
				"replicates." );
		}

		ValidateCollisionRules( warnings );

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

		if ( collision.GetCollisionRule(
			LargeLadGameplayRules.MinionPassageTag,
			"solid" ) !=
			Sandbox.Physics.CollisionRules.Result.Collide )
		{
			warnings.Add(
				"project collision rules must keep ordinary solid physics " +
				"bodies against Minion passages." );
		}
	}

	private bool IsCoverTarget( GameObject target )
	{
		if ( target is null ||
			!EnableBreakableCover ||
			IsCoverDestroyed ||
			OpeningCollider is null ||
			!OpeningCollider.IsValid )
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

	private bool ResolveReferences()
	{
		var previousOpeningCollider = OpeningCollider;
		var previousCoverRoot = CoverRoot;

		if ( OpeningCollider is not null &&
			(!OpeningCollider.IsValid ||
				OpeningCollider.GameObject != GameObject) )
		{
			OpeningCollider = null;
		}

		OpeningCollider ??= Components.Get<Collider>(
			FindMode.EverythingInSelf );

		if ( CoverRoot is not null &&
			(!CoverRoot.IsValid ||
				CoverRoot.Parent != GameObject) )
		{
			CoverRoot = null;
		}

		if ( CoverRoot is null && EnableBreakableCover )
		{
			CoverRoot = GameObject.Children.FirstOrDefault( child =>
				child.Components.Get<MeshComponent>(
					FindMode.EverythingInSelfAndDescendants ) is not null ||
				child.Components.Get<Renderer>(
					FindMode.EverythingInSelfAndDescendants ) is not null );
		}

		if ( CoverRoot is not null )
		{
			if ( CoverProp is not null &&
				(!CoverProp.IsValid ||
					!CoverRoot.IsDescendant( CoverProp.GameObject ) &&
					CoverProp.GameObject != CoverRoot) )
			{
				CoverProp = null;
			}

			CoverProp ??= CoverRoot.Components.Get<Prop>(
				FindMode.EverythingInSelfAndDescendants );
		}
		else
		{
			CoverProp = null;
		}

		return !ReferenceEquals( previousOpeningCollider, OpeningCollider ) ||
			!ReferenceEquals( previousCoverRoot, CoverRoot );
	}

	private void CaptureAuthoredState( bool force = false )
	{
		if ( hasCapturedAuthoredState &&
			!force &&
			ReferenceEquals( capturedOpeningCollider, OpeningCollider ) &&
			ReferenceEquals( capturedCoverRoot, CoverRoot ) )
		{
			return;
		}

		authoredOpeningColliderEnabled =
			OpeningCollider?.Enabled == true;
		authoredOpeningHasPassageTag =
			OpeningCollider?.GameObject?.Tags.Has(
				LargeLadGameplayRules.MinionPassageTag ) == true;
		authoredCoverTransform = CoverRoot?.LocalTransform ?? default;
		capturedOpeningCollider = OpeningCollider;
		capturedCoverRoot = CoverRoot;
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

}
