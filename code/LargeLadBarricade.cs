using Sandbox;
using System.Collections.Generic;
using System.Linq;

public enum LargeLadBarricadeMode
{
	SkinnyProgression,
	LadShortcut
}

/// <summary>
/// The authoritative root for both simple and optional compound barricades.
/// Health, damage, blocker state, destruction, registration, and reset remain
/// here even when presentation is distributed across mapper-authored children.
/// </summary>
[Description(
	"Authoritative health, blocker, destruction, and round-reset controller. " +
	"Direct child pieces are frozen automatically; Prop pieces may create model " +
	"gibs, while renderer-only pieces simply disappear when broken." )]
public sealed class LargeLadBarricade : LargeLadRoundResettableComponent,
	ILargeLadDamageable
{
	private sealed class AuthoredObjectState
	{
		public GameObject Target { get; init; }
		public bool Enabled { get; init; }
		public Transform LocalTransform { get; init; }
		public bool IsStatic { get; init; }
		public Prop Prop { get; init; }
		public bool PropIsStatic { get; init; }
		public bool PropStartAsleep { get; init; }
		public Rigidbody Rigidbody { get; init; }
		public bool RigidbodyEnabled { get; init; }
		public bool HasPhysicsBodyState { get; init; }
		public PhysicsBodyType BodyType { get; init; }
		public bool MotionEnabled { get; init; }
		public bool Sleeping { get; init; }
	}

	private sealed class AuthoredComponentState
	{
		public Component Target { get; init; }
		public bool Enabled { get; init; }
	}

	[Property]
	[Description(
		"Skinny Progression accepts Skinny Kid melee and may opt into a final " +
		"announcement. Lad Shortcut accepts the Large Lad Eat structural " +
		"fallback and never announces." )]
	public LargeLadBarricadeMode Mode { get; set; }

	[Property, Title( "Base Maximum Health" )]
	[Description(
		"Authored maximum health before round-balance scaling. Simple and " +
		"compound barricades both use this one authoritative health pool." )]
	public float BaseMaximumHealth { get; set; } = 300.0f;

	[Property]
	[Description(
		"The single authoritative blocking collider. Keep it on this root; " +
		"ordinary stages cannot disable it." )]
	public Collider BarricadeCollider { get; set; }

	[Property, Group( "Compound Stages" )]
	[Description(
		"Optional health thresholds that break direct child GameObjects in " +
		"hierarchy order. Leave empty to break every child only at zero health." )]
	public List<LargeLadBarricadeStage> Stages { get; set; } = new();

	[Property, Group( "Staged Passage" ), Title( "Enable Early Passage" )]
	[Description(
		"Explicitly allows the blocker to open before zero health. Leave off " +
		"to guarantee collision remains until final destruction." )]
	public bool EnableStagedPassage { get; set; }

	[Property, Group( "Staged Passage" ),
		Title( "Remaining Health Fraction" )]
	[Description(
		"When Early Passage is enabled, opens the blocker at or below this " +
		"remaining-health fraction. Must be greater than 0 and less than 1." )]
	public float StagedPassageHealthFraction { get; set; } = -1.0f;

	[Property, Group( "Skinny Progression Announcement" ),
		Title( "Announce Destruction" )]
	[Description(
		"Opt-in player-facing destruction announcement. Defaults off and only " +
		"has an effect for Skinny Progression barricades." )]
	public bool AnnounceDestruction { get; set; } = false;

	[Property, Group( "Skinny Progression Announcement" ),
		Title( "Mapper Display Name" )]
	[ShowIf( nameof( AnnounceDestruction ), true )]
	[Description(
		"Required when Announce Destruction is enabled. This is the only text " +
		"replicated, for example 'Gymnasium Doors'. No location data is added." )]
	public string DisplayName { get; set; }

	[Property, Title( "Editor Gizmo Padding" )]
	[Description(
		"Extra world-space padding around the editor-only barricade bounds gizmo." )]
	public float GizmoPadding { get; set; } = 2.0f;

	[Sync( SyncFlags.FromHost )]
	public float CurrentHealth { get; private set; }

	[Sync( SyncFlags.FromHost ), Change( nameof( OnActiveStageCountChanged ) )]
	public int ActiveStageCount { get; private set; }

	[Sync( SyncFlags.FromHost ), Change( nameof( OnPassageOpenChanged ) )]
	public bool IsPassageOpen { get; private set; }

	[Sync( SyncFlags.FromHost ), Change( nameof( OnDestroyedChanged ) )]
	public bool IsDestroyed { get; private set; }

	/// <summary>
	/// Raised once on the host for each round's final destruction. A later
	/// Minion spawn-stage system can subscribe without this component knowing
	/// anything about spawning.
	/// </summary>
	public event System.Action<LargeLadBarricade> AuthoritativeDestroyed;

	/// <summary>
	/// Effective round maximum derived from the authored baseline. Only
	/// SkinnyProgression barricades receive the fixed player-count band.
	/// </summary>
	public float MaximumHealth
	{
		get
		{
			var roundMultiplier =
				Mode == LargeLadBarricadeMode.SkinnyProgression
					? LargeLadGameManager.FindForScene( Scene )?
						.GetSkinnyProgressionBarricadeMaximumHealthMultiplier() ??
						1.0f
					: 1.0f;
			return LargeLadRoundBalanceRules.GetScaledMaximumHealth(
				BaseMaximumHealth,
				roundMultiplier );
		}
	}

	public bool HasCollision =>
		BarricadeCollider is not null && BarricadeCollider.IsValid;

	public GameObject AuthoredTarget => GameObject;

	private readonly Dictionary<GameObject, AuthoredObjectState>
		authoredObjectStates = new();
	private readonly Dictionary<Component, AuthoredComponentState>
		authoredComponentStates = new();
	private readonly List<GameObject> authoredChildRoots = new();
	private readonly HashSet<GameObject> brokenChildRoots = new();
	private readonly LargeLadBarricadeDestructionGate destructionGate = new();
	private bool hasCapturedAuthoredState;
	private bool isResettingForRound;
	private int appliedStageCount;
	private bool? appliedDestroyedState;
	private bool? appliedPassageOpenState;

	public Vector3 GetClosestWorldPoint( Vector3 worldPoint )
	{
		if ( BarricadeCollider is not null && BarricadeCollider.IsValid )
			return BarricadeCollider.FindClosestPoint( worldPoint );

		var localPoint = GameObject.WorldTransform.PointToLocal( worldPoint );
		var closest = GameObject.GetLocalBounds().ClosestPoint( localPoint );
		return GameObject.WorldTransform.PointToWorld( closest );
	}

	public static LargeLadBarricade FindFor( GameObject target )
	{
		return target?.Components.Get<LargeLadBarricade>(
			FindMode.EverythingInSelfAndAncestors );
	}

	protected override void OnAwake()
	{
		ConfigureObject();
		ResolveAuthoredParts();
	}

	protected override void OnStart()
	{
		ConfigureObject();
		ResolveAuthoredParts();
		CaptureAuthoredState();
		FreezeAuthoredChildren();

		if ( Networking.IsHost && CurrentHealth <= 0.0f && !IsDestroyed )
		{
			CurrentHealth = System.MathF.Max( 1.0f, MaximumHealth );
			RefreshAuthoritativeStageState();
		}

		RefreshPresentation();
	}

	protected override void OnUpdate()
	{
		ResolveAuthoredParts();

		if ( hasCapturedAuthoredState && AuthoredStateNeedsRefresh() )
		{
			RecaptureAuthoredState();
			FreezeAuthoredChildren();
		}

		if ( !hasCapturedAuthoredState )
			return;

		// Synced state is enough to wake remote presentation callbacks, but the
		// host also owns the mapper-authored objects those callbacks manipulate.
		// A later network/procedural component update can therefore leave a child
		// disabled after the host's reset while these cached values still say the
		// intact state was applied. Audit the actual authored presentation before
		// accepting the cache so the host heals on the following update.
		if ( IsFullyIntact() && !IsAuthoredPresentationRestored() )
			RestoreIntactPresentation();

		if ( appliedStageCount == ActiveStageCount &&
			appliedDestroyedState == IsDestroyed &&
			appliedPassageOpenState == IsPassageOpen )
		{
			return;
		}

		RefreshPresentation();
	}

	protected override void OnValidate()
	{
		ConfigureObject();
		ResolveAuthoredParts();

		if ( BaseMaximumHealth <= 0.0f )
			Log.Warning( $"{GameObject.Name}: barricade health must be positive." );

		if ( BarricadeCollider is null )
		{
			Log.Warning(
				$"{GameObject.Name}: assign one authoritative blocking collider." );
		}

		foreach ( var warning in GetValidationWarnings() )
			Log.Warning( $"{GameObject.Name}: {warning}" );
	}

	public IReadOnlyList<string> GetValidationWarnings()
	{
		var warnings = new List<string>();
		var stages = Stages ?? new();
		var childRoots = GameObject.Children.ToList();
		warnings.AddRange(
			LargeLadBarricadeStageRules.GetConfigurationWarnings(
				stages,
				childRoots.Count ) );

		if ( EnableStagedPassage &&
			!LargeLadBarricadeStageRules.IsValidThreshold(
				StagedPassageHealthFraction ) )
		{
			warnings.Add(
				"Early passage is enabled but its remaining-health fraction " +
				"is missing or outside the exclusive 0-to-1 range." );
		}

		if ( AnnounceDestruction &&
			Mode == LargeLadBarricadeMode.SkinnyProgression &&
			string.IsNullOrWhiteSpace( DisplayName ) )
		{
			warnings.Add(
				"SkinnyProgression barricades need a mapper-authored display " +
				"name for their destruction announcement." );
		}

		if ( AnnounceDestruction &&
			Mode != LargeLadBarricadeMode.SkinnyProgression )
		{
			warnings.Add(
				"Destruction announcements are only available for " +
				"SkinnyProgression barricades." );
		}

		if ( BarricadeCollider is not null &&
			BarricadeCollider.GameObject != GameObject )
		{
			warnings.Add(
				"The authoritative blocking collider must remain on the " +
				"barricade root." );
		}

		return warnings;
	}

	private void ConfigureObject()
	{
		GameObject.NetworkMode = NetworkMode.Object;
		GameObject.IsStatic = true;
	}

	private void ResolveAuthoredParts()
	{
		if ( BarricadeCollider is not null &&
			(!BarricadeCollider.IsValid ||
				BarricadeCollider.GameObject != GameObject) )
		{
			BarricadeCollider = null;
		}

		var editableMesh = Components.Get<MeshComponent>(
			FindMode.EverythingInSelf );

		if ( editableMesh is not null )
			BarricadeCollider ??= editableMesh;

		BarricadeCollider ??= Components.Get<Collider>(
			FindMode.EverythingInSelf );
	}

	public bool TryApplyDamage(
		LargeLadDamageContext damage,
		out LargeLadDamageContext appliedDamage )
	{
		appliedDamage = damage.WithAppliedDamage( 0.0f );

		if ( !Networking.IsHost || IsDestroyed || CurrentHealth <= 0.0f )
			return false;

		if ( !LargeLadGameplayRules.CanDamageBarricade(
			Mode,
			damage.AttackerRole,
			damage.DamageType ) )
		{
			return false;
		}

		var amount = System.MathF.Max( 0.0f, damage.BaseDamage );

		if ( amount <= 0.0f )
			return false;

		CurrentHealth = System.MathF.Max( 0.0f, CurrentHealth - amount );
		appliedDamage = damage.WithAppliedDamage( amount );

		if ( CurrentHealth <= 0.0f )
			IsDestroyed = true;

		RefreshAuthoritativeStageState();
		RefreshPresentation();

		if ( IsDestroyed )
			CommitAuthoritativeDestruction();

		return true;
	}

	public override void ResetForRound()
	{
		if ( !Networking.IsHost )
			return;

		ResolveAuthoredParts();
		EnsureAuthoredStateCaptured();
		destructionGate.ResetForRound();
		isResettingForRound = true;

		try
		{
			CurrentHealth = System.MathF.Max( 1.0f, MaximumHealth );
			IsDestroyed = false;
			ActiveStageCount = 0;
			IsPassageOpen = false;
		}
		finally
		{
			isResettingForRound = false;
		}

		RestoreIntactPresentation();
		RefreshPresentation();

		if ( LargeLadGameManager.FindForScene( Scene )?
			.EnablePickupAndRoundResetDebugLogging == true )
		{
			Log.Info(
				$"[Debug/Round Reset] Reset barricade '{GameObject.Name}'." );
		}
	}

	private void RefreshAuthoritativeStageState()
	{
		if ( !Networking.IsHost )
			return;

		var orderedStages = GetOrderedStages();
		var thresholds = orderedStages
			.Select( stage => stage.RemainingHealthFraction )
			.ToList();

		ActiveStageCount =
			LargeLadBarricadeStageRules.GetActiveStageCount(
				CurrentHealth,
				MaximumHealth,
				thresholds );
		IsPassageOpen =
			LargeLadBarricadeStageRules.ShouldOpenPassage(
				IsDestroyed,
				EnableStagedPassage,
				StagedPassageHealthFraction,
				CurrentHealth,
				MaximumHealth );
	}

	private void OnActiveStageCountChanged( int oldValue, int newValue )
	{
		if ( hasCapturedAuthoredState && !isResettingForRound )
			RefreshPresentation();
	}

	private void OnPassageOpenChanged( bool oldValue, bool newValue )
	{
		if ( hasCapturedAuthoredState && !isResettingForRound )
			RefreshPresentation();
	}

	private void OnDestroyedChanged( bool oldValue, bool newValue )
	{
		if ( isResettingForRound )
			return;

		if ( oldValue && !newValue && hasCapturedAuthoredState )
			RestoreIntactPresentation();

		if ( hasCapturedAuthoredState )
			RefreshPresentation();

		if ( newValue && !oldValue )
		{
			CommitAuthoritativeDestruction();
		}
	}

	private void CommitAuthoritativeDestruction()
	{
		if ( !Networking.IsHost ||
			!IsDestroyed ||
			!destructionGate.TryCommitDestruction() )
		{
			return;
		}

		var manager = LargeLadGameManager.FindForScene( Scene );
		manager?.PublishBarricadeDestructionAnnouncement(
			AnnounceDestruction,
			Mode,
			DisplayName );
		AuthoritativeDestroyed?.Invoke( this );
	}

	private void RefreshPresentation()
	{
		if ( !hasCapturedAuthoredState )
			return;

		ApplyActiveStages();
		appliedDestroyedState = IsDestroyed;
		appliedPassageOpenState = IsPassageOpen;

		if ( BarricadeCollider is not null && BarricadeCollider.IsValid )
		{
			BarricadeCollider.Enabled =
				GetAuthoredEnabled( BarricadeCollider ) &&
				!IsPassageOpen &&
				!IsDestroyed;
		}

		if ( !IsDestroyed )
			return;

		DisableRootVisuals();

		BreakRemainingChildRoots();
	}

	private void DisableRootVisuals()
	{
		var editableMesh = Components.Get<MeshComponent>(
			FindMode.EverythingInSelf );

		if ( editableMesh is not null )
			editableMesh.Enabled = false;

		foreach ( var renderer in Components.GetAll<Renderer>(
			FindMode.EverythingInSelf ) )
		{
			if ( renderer is not null && renderer.IsValid )
				renderer.Enabled = false;
		}
	}

	private void ApplyActiveStages()
	{
		var orderedStages = GetOrderedStages();
		var targetCount = System.Math.Clamp(
			ActiveStageCount,
			0,
			orderedStages.Count );

		if ( targetCount < appliedStageCount )
		{
			RestoreAuthoredState();
			brokenChildRoots.Clear();
			FreezeAuthoredChildren();
			appliedStageCount = 0;
		}

		for ( var index = appliedStageCount; index < targetCount; index++ )
			ApplyStage( orderedStages[index] );

		appliedStageCount = targetCount;
	}

	private List<LargeLadBarricadeStage> GetOrderedStages()
	{
		return (Stages ?? new())
			.Where( stage =>
				stage is not null &&
				LargeLadBarricadeStageRules.IsValidThreshold(
					stage.RemainingHealthFraction ) )
			.OrderByDescending( stage => stage.RemainingHealthFraction )
			.ToList();
	}

	private void ApplyStage( LargeLadBarricadeStage stage )
	{
		BreakNextChildRoots( System.Math.Max( 0, stage.ChildObjectsToBreak ) );
	}

	private void BreakNextChildRoots( int count )
	{
		if ( count <= 0 )
			return;

		foreach ( var childRoot in authoredChildRoots )
		{
			if ( brokenChildRoots.Contains( childRoot ) )
				continue;

			BreakChildRoot( childRoot );
			count--;

			if ( count <= 0 )
				return;
		}
	}

	private void BreakRemainingChildRoots()
	{
		foreach ( var childRoot in authoredChildRoots )
		{
			if ( !brokenChildRoots.Contains( childRoot ) )
				BreakChildRoot( childRoot );
		}
	}

	private void BreakChildRoot( GameObject childRoot )
	{
		if ( childRoot is null ||
			!childRoot.IsValid ||
			!brokenChildRoots.Add( childRoot ) )
		{
			return;
		}

		if ( Networking.IsHost )
		{
			foreach ( var prop in childRoot.Components.GetAll<Prop>(
				FindMode.EverythingInSelfAndDescendants ) )
			{
				if ( prop is not null && prop.IsValid && prop.Enabled )
					prop.NetworkCreateGibs( false );
			}
		}

		if ( childRoot.IsValid )
			childRoot.Enabled = false;
	}

	private void FreezeAuthoredChildren()
	{
		if ( !hasCapturedAuthoredState )
			return;

		foreach ( var state in authoredObjectStates.Values )
		{
			if ( state.Target is null ||
				!state.Target.IsValid ||
				state.Target == GameObject )
			{
				continue;
			}

			state.Target.IsStatic = true;

			if ( state.Prop is not null && state.Prop.IsValid )
			{
				state.Prop.IsStatic = true;
				state.Prop.StartAsleep = true;
			}

			if ( state.Rigidbody is null ||
				!state.Rigidbody.IsValid ||
				!state.Rigidbody.Enabled )
			{
				continue;
			}

			var body = state.Rigidbody.PhysicsBody;

			if ( body is null )
				continue;

			body.MotionEnabled = false;
			body.Velocity = Vector3.Zero;
			body.AngularVelocity = Vector3.Zero;
			body.Sleeping = true;
		}
	}

	private void CaptureAuthoredState()
	{
		if ( hasCapturedAuthoredState )
			return;

		CaptureComponentState( BarricadeCollider );
		CaptureRootVisualStates();

		foreach ( var childRoot in GameObject.Children )
		{
			authoredChildRoots.Add( childRoot );
			CaptureChildHierarchy( childRoot );
		}

		hasCapturedAuthoredState = true;
	}

	private bool AuthoredStateNeedsRefresh()
	{
		if ( BarricadeCollider is not null &&
			(!BarricadeCollider.IsValid ||
				!authoredComponentStates.ContainsKey( BarricadeCollider )) )
		{
			return true;
		}

		var currentChildRoots = GameObject.Children.ToList();

		if ( authoredChildRoots.Count != currentChildRoots.Count )
			return true;

		for ( var index = 0; index < authoredChildRoots.Count; index++ )
		{
			var authoredChild = authoredChildRoots[index];
			var currentChild = currentChildRoots[index];

			if ( authoredChild is null ||
				!authoredChild.IsValid ||
				!ReferenceEquals( authoredChild, currentChild ) )
			{
				return true;
			}
		}

		return authoredComponentStates.Values.Any( state =>
			state.Target is null || !state.Target.IsValid );
	}

	private void RecaptureAuthoredState()
	{
		authoredObjectStates.Clear();
		authoredComponentStates.Clear();
		authoredChildRoots.Clear();
		brokenChildRoots.Clear();
		hasCapturedAuthoredState = false;
		appliedStageCount = 0;
		appliedDestroyedState = null;
		appliedPassageOpenState = null;
		CaptureAuthoredState();
	}

	private void CaptureRootVisualStates()
	{
		CaptureComponentState( Components.Get<MeshComponent>(
			FindMode.EverythingInSelf ) );

		foreach ( var renderer in Components.GetAll<Renderer>(
			FindMode.EverythingInSelf ) )
		{
			CaptureComponentState( renderer );
		}
	}

	private void EnsureAuthoredStateCaptured()
	{
		if ( !hasCapturedAuthoredState )
			CaptureAuthoredState();
	}

	private void CaptureChildHierarchy( GameObject target )
	{
		if ( target is null )
			return;

		CaptureObjectState( target );

		foreach ( var component in target.Components.GetAll(
			FindMode.EverythingInSelf ) )
		{
			CaptureComponentState( component );
		}

		foreach ( var child in target.Children )
			CaptureChildHierarchy( child );
	}

	private void CaptureObjectState( GameObject target )
	{
		if ( target is null || authoredObjectStates.ContainsKey( target ) )
			return;

		var rigidbody = target.Components.Get<Rigidbody>(
			FindMode.EverythingInSelf );
		var prop = target.Components.Get<Prop>(
			FindMode.EverythingInSelf );
		var body = rigidbody?.PhysicsBody;
		authoredObjectStates.Add(
			target,
			new AuthoredObjectState
			{
				Target = target,
				Enabled = target.Enabled,
				LocalTransform = target.LocalTransform,
				IsStatic = target.IsStatic,
				Prop = prop,
				PropIsStatic = prop?.IsStatic ?? false,
				PropStartAsleep = prop?.StartAsleep ?? false,
				Rigidbody = rigidbody,
				RigidbodyEnabled = rigidbody?.Enabled == true,
				HasPhysicsBodyState = body is not null,
				BodyType = body?.BodyType ?? default,
				MotionEnabled = body?.MotionEnabled ?? false,
				Sleeping = body?.Sleeping ?? true
			} );

		if ( rigidbody is not null )
			CaptureComponentState( rigidbody );

		if ( prop is not null )
			CaptureComponentState( prop );
	}

	private void CaptureComponentState( Component target )
	{
		if ( target is null ||
			target == this ||
			authoredComponentStates.ContainsKey( target ) )
		{
			return;
		}

		authoredComponentStates.Add(
			target,
			new AuthoredComponentState
			{
				Target = target,
				Enabled = target.Enabled
			} );
		CaptureObjectState( target.GameObject );
	}

	private void RestoreAuthoredState()
	{
		if ( !hasCapturedAuthoredState )
			return;

		foreach ( var state in authoredObjectStates.Values )
		{
			if ( state.Target is null || !state.Target.IsValid )
				continue;

			state.Target.Enabled = true;
			state.Target.IsStatic = state.IsStatic;
			state.Target.LocalTransform = state.LocalTransform;
			state.Target.Network.ClearInterpolation();

			if ( state.Prop is not null && state.Prop.IsValid )
			{
				state.Prop.IsStatic = state.PropIsStatic;
				state.Prop.StartAsleep = state.PropStartAsleep;
			}
		}

		foreach ( var state in authoredComponentStates.Values )
		{
			if ( state.Target is not null && state.Target.IsValid )
				state.Target.Enabled = state.Enabled;
		}

		foreach ( var state in authoredObjectStates.Values )
		{
			if ( state.Target is null || !state.Target.IsValid )
				continue;

			if ( state.Rigidbody is not null && state.Rigidbody.IsValid )
			{
				state.Rigidbody.Enabled = state.RigidbodyEnabled;
				var body = state.Rigidbody.PhysicsBody;

				if ( state.HasPhysicsBodyState && body is not null )
				{
					body.BodyType = state.BodyType;
					body.MotionEnabled = state.MotionEnabled;
					body.Velocity = Vector3.Zero;
					body.AngularVelocity = Vector3.Zero;
					body.Sleeping = state.Sleeping;
				}
			}

			state.Target.Enabled = state.Enabled;
		}
	}

	private bool IsFullyIntact()
	{
		return !IsDestroyed &&
			ActiveStageCount == 0 &&
			!IsPassageOpen;
	}

	private bool IsAuthoredPresentationRestored()
	{
		foreach ( var state in authoredObjectStates.Values )
		{
			if ( state.Target is not null &&
				state.Target.IsValid &&
				state.Target.Enabled != state.Enabled )
			{
				return false;
			}
		}

		foreach ( var state in authoredComponentStates.Values )
		{
			if ( state.Target is not null &&
				state.Target.IsValid &&
				state.Target.Enabled != state.Enabled )
			{
				return false;
			}
		}

		return true;
	}

	private void RestoreIntactPresentation()
	{
		RestoreAuthoredState();
		brokenChildRoots.Clear();
		FreezeAuthoredChildren();
		appliedStageCount = 0;
		appliedDestroyedState = null;
		appliedPassageOpenState = null;
	}

	private bool GetAuthoredEnabled( Component target )
	{
		return target is not null &&
			authoredComponentStates.TryGetValue( target, out var state )
				? state.Enabled
				: target?.Enabled == true;
	}

	protected override void DrawGizmos()
	{
		ResolveAuthoredParts();

		var padding = new Vector3( System.MathF.Max( 0.0f, GizmoPadding ) );
		var bounds = BarricadeCollider is not null && BarricadeCollider.IsValid
			? BarricadeCollider.GetWorldBounds()
			: GameObject.GetBounds();
		var padded = new BBox(
			bounds.Mins - padding,
			bounds.Maxs + padding );
		var color = Mode == LargeLadBarricadeMode.SkinnyProgression
			? new Color( 0.25f, 0.85f, 1.0f )
			: new Color( 1.0f, 0.22f, 0.08f );

		Gizmo.Transform = global::Transform.Zero;
		Gizmo.Draw.IgnoreDepth = true;
		Gizmo.Draw.Color = color.WithAlpha( 0.16f );
		Gizmo.Draw.SolidBox( padded );
		Gizmo.Draw.Color = color.WithAlpha( 0.95f );
		Gizmo.Draw.LineThickness = 2.0f;
		Gizmo.Draw.LineBBox( padded );
		Gizmo.Draw.LineThickness = 1.0f;
		Gizmo.Draw.IgnoreDepth = false;
	}
}
