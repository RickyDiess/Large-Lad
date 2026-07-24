using Sandbox;
using System.Collections.Generic;
using System.Linq;

public enum LargeLadBarricadeMode
{
	SkinnyProgression,
	LadShortcut
}

/// <summary>
/// The only networked part of a barricade. Hammer owns and loads the tied
/// brush locally on every client; this lightweight child carries just the
/// authoritative health and intact/destroyed state.
/// </summary>
public sealed class LargeLadBarricadeState : Component
{
	[Sync( SyncFlags.FromHost )]
	public float CurrentHealth { get; private set; }

	[Sync( SyncFlags.FromHost ), Change( nameof( OnDestroyedChanged ) )]
	public bool IsDestroyed { get; private set; }

	private bool? lastNotifiedDestroyedState;

	protected override void OnAwake()
	{
		GameObject.IsStatic = true;
		GameObject.NetworkMode = NetworkMode.Object;
	}

	protected override void OnStart()
	{
		NotifyBarricade( IsDestroyed, IsDestroyed );
	}

	protected override void OnUpdate()
	{
		// A network state child can arrive before its locally loaded Hammer
		// parent has finished streaming. Retry until the two halves are paired.
		if ( lastNotifiedDestroyedState != IsDestroyed )
			NotifyBarricade( lastNotifiedDestroyedState ?? IsDestroyed, IsDestroyed );
	}

	public void Initialize( float maximumHealth )
	{
		if ( !Networking.IsHost || IsDestroyed || CurrentHealth > 0.0f )
			return;

		CurrentHealth = System.MathF.Max( 1.0f, maximumHealth );
	}

	public bool TryApplyDamage( float amount )
	{
		if ( !Networking.IsHost || IsDestroyed || CurrentHealth <= 0.0f || amount <= 0.0f )
			return false;

		CurrentHealth = System.MathF.Max( 0.0f, CurrentHealth - amount );

		if ( CurrentHealth <= 0.0f )
			IsDestroyed = true;

		return true;
	}

	public void Reset( float maximumHealth )
	{
		if ( !Networking.IsHost )
			return;

		CurrentHealth = System.MathF.Max( 1.0f, maximumHealth );
		IsDestroyed = false;
	}

	private void OnDestroyedChanged( bool oldValue, bool newValue )
	{
		NotifyBarricade( oldValue, newValue );
	}

	private void NotifyBarricade( bool oldValue, bool newValue )
	{
		var barricade = GameObject.Parent?.Components.Get<LargeLadBarricade>(
			FindMode.EverythingInSelfAndAncestors );

		if ( barricade is null )
			return;

		lastNotifiedDestroyedState = newValue;
		barricade.HandleNetworkStateChanged( oldValue, newValue );
	}
}

public sealed class LargeLadBarricade : Component,
	ILargeLadDamageable,
	ILargeLadRoundResettable
{
	[Property]
	public LargeLadBarricadeMode Mode { get; set; }

	[Property]
	public float MaximumHealth { get; set; } = 300.0f;

	[Property, Title( "Lad Structural Damage Per Swing" )]
	public float LadStructuralDamage { get; set; } = 100.0f;

	public Component BarricadeRenderer { get; private set; }

	public Collider BarricadeCollider { get; private set; }

	public HammerMesh AuthoredHammerMesh { get; private set; }

	public bool HasVisibleGeometry =>
		(AuthoredHammerMesh is not null && AuthoredHammerMesh.UseRenderer) ||
		BarricadeRenderer is not null;

	public bool HasCollision =>
		(AuthoredHammerMesh is not null && AuthoredHammerMesh.UseCollision) ||
		BarricadeCollider is not null;

	public bool HasRedundantBoxCollider
	{
		get
		{
			ResolveAuthoredParts();

			return AuthoredHammerMesh is not null &&
				AuthoredTarget.Components.Get<BoxCollider>(
					FindMode.EverythingInSelf ) is not null;
		}
	}

	[Property, Title( "Local Cosmetic Debris" )]
	public List<GameObject> CosmeticDebris { get; set; } = new();

	[Property]
	public float CosmeticDebrisLifetime { get; set; } = 1.5f;

	[Property, Title( "Editor Gizmo Padding" )]
	public float GizmoPadding { get; set; } = 2.0f;

	public LargeLadBarricadeState NetworkState { get; private set; }

	public float CurrentHealth => NetworkState?.CurrentHealth ?? 0.0f;

	public bool IsDestroyed => NetworkState?.IsDestroyed == true;

	public bool HasNetworkState => NetworkState is not null;

	private bool? appliedDestroyedState;
	private bool warnedAboutMissingState;
	private const string DevVisualName = "Large Lad Barricade Dev Visual";

	public GameObject AuthoredTarget => GameObject;

	public Vector3 GetClosestWorldPoint( Vector3 worldPoint )
	{
		if ( BarricadeCollider is not null )
			return BarricadeCollider.FindClosestPoint( worldPoint );

		var authoredObject = AuthoredTarget;
		var localPoint = authoredObject.WorldTransform.PointToLocal( worldPoint );
		var closestLocalPoint = GetAuthoredLocalBounds().ClosestPoint( localPoint );
		return authoredObject.WorldTransform.PointToWorld( closestLocalPoint );
	}

	public static LargeLadBarricade FindFor( Scene scene, GameObject target )
	{
		if ( scene is null || target is null )
			return null;

		var direct = target.Components.Get<LargeLadBarricade>(
			FindMode.EverythingInSelfAndAncestors );

		if ( direct is not null )
			return direct;

		var targetCollider = target.Components.Get<Collider>(
			FindMode.EverythingInSelfAndAncestors );

		return scene
			.GetAllComponents<LargeLadBarricade>()
			.FirstOrDefault( barricade =>
				barricade.AuthoredTarget == target ||
				(targetCollider is not null && barricade.BarricadeCollider == targetCollider) );
	}

	protected override void OnAwake()
	{
		ResolveAuthoredParts();
		ResolveNetworkState();
		ConfigureAuthoredObject();
	}

	protected override void OnStart()
	{
		ResolveAuthoredParts();
		ResolveNetworkState();
		ConfigureAuthoredObject();
		CreateDevVisualIfNeeded();
		ResolveAuthoredParts();
		SetCosmeticDebrisEnabled( false );

		NetworkState?.Initialize( MaximumHealth );

		ApplyDestroyedState();
	}

	protected override void OnUpdate()
	{
		if ( NetworkState is null )
		{
			ResolveNetworkState();
			NetworkState?.Initialize( MaximumHealth );

			if ( NetworkState is null && !warnedAboutMissingState )
			{
				warnedAboutMissingState = true;
				Log.Error(
					$"Barricade '{GameObject.Name}' has no LargeLadBarricadeState child. " +
					"Repair or recreate it with the Large Lad Hammer menu." );
			}
		}

		// A tied HammerMesh can be attached after this component's Awake/Start
		// callbacks while the VMAP is streaming. Keep resolving until the brush
		// exists so the same tied GameObject becomes the renderer and collider.
		if ( !HasVisibleGeometry || !HasCollision )
		{
			ResolveAuthoredParts();
			ConfigureAuthoredObject();
			ApplyDestroyedState();
		}

		// The normal path is the Sync change callback. This also repairs the
		// authored pieces after a hotload or a late network snapshot.
		if ( appliedDestroyedState != IsDestroyed )
		{
			ResolveAuthoredParts();
			ApplyDestroyedState();
		}
	}

	protected override void OnValidate()
	{
		ResolveAuthoredParts();
		ConfigureAuthoredObject();

		if ( MaximumHealth <= 0.0f )
			Log.Warning( $"{GameObject.Name}: barricade health must be positive." );

		// Geometry may not have been attached yet while Hammer is streaming this
		// object. The deferred whole-map validator reports a real missing brush.
	}

	private void ResolveAuthoredParts()
	{
		var authoredObject = AuthoredTarget;

		AuthoredHammerMesh = authoredObject.Components.Get<HammerMesh>(
			FindMode.EverythingInSelf );
		BarricadeRenderer = null;
		BarricadeCollider = null;

		if ( AuthoredHammerMesh is not null )
		{
			BarricadeRenderer = AuthoredHammerMesh;
			BarricadeCollider = authoredObject.Components.Get<Collider>(
				FindMode.EverythingInSelf );
			return;
		}

		// A destroyed barricade disables its authored components. Use an
		// everything lookup so the host can still rediscover and re-enable them
		// at the next round reset.
		var editableMesh = authoredObject.Components.Get<MeshComponent>(
			FindMode.EverythingInSelf );

		if ( editableMesh is not null )
		{
			BarricadeRenderer = editableMesh;
			BarricadeCollider = editableMesh;
			return;
		}

		BarricadeRenderer = authoredObject.Components.Get<Renderer>(
			FindMode.EverythingInSelf );
		BarricadeRenderer ??= authoredObject.GetComponentInChildren<Renderer>( true );
		BarricadeCollider = authoredObject.Components.Get<Collider>(
			FindMode.EverythingInSelf );
		BarricadeRenderer ??= BarricadeCollider;
	}

	private void ResolveNetworkState()
	{
		NetworkState = AuthoredTarget.GetComponentInChildren<LargeLadBarricadeState>( true );
	}

	private void ConfigureHammerMesh()
	{
		if ( AuthoredHammerMesh is null )
			return;

		AuthoredHammerMesh.UseRenderer = true;
		AuthoredHammerMesh.UseCollision = true;
		AuthoredHammerMesh.IsTrigger = false;
		AuthoredHammerMesh.Static = true;
	}

	private void ConfigureAuthoredObject()
	{
		GameObject.IsStatic = true;
		// The compiled Hammer model is map-local content. Networking this parent
		// makes clients skip their locally generated copy and leaves them trying
		// to render a generated model resource that was never network-distributed.
		GameObject.NetworkMode = NetworkMode.Never;
		ConfigureHammerMesh();
	}

	private void CreateDevVisualIfNeeded()
	{
		if ( AuthoredHammerMesh is not null || BarricadeRenderer is not null || BarricadeCollider is null )
			return;

		var authoredObject = AuthoredTarget;
		var visual = authoredObject.Children.FirstOrDefault( child => child.Name == DevVisualName );

		if ( visual is null )
		{
			var bounds = GetAuthoredLocalBounds();
			visual = new GameObject( true, DevVisualName );
			visual.SetParent( authoredObject, false );
			visual.LocalPosition = bounds.Center;
			visual.LocalScale = bounds.Size;
		}

		var renderer = visual.GetOrAddComponent<ModelRenderer>();
		renderer.Model = Model.Load( "models/dev/box.vmdl" );
		renderer.Tint = Mode == LargeLadBarricadeMode.SkinnyProgression
			? new Color( 0.25f, 0.85f, 1.0f )
			: new Color( 1.0f, 0.22f, 0.08f );
		visual.LocalScale = ScaleModelToSize( renderer.Model, GetAuthoredLocalBounds().Size );
	}

	private static Vector3 ScaleModelToSize( Model model, Vector3 targetSize )
	{
		var size = model?.Bounds.Size ?? Vector3.One;

		return new Vector3(
			size.x > 0.001f ? targetSize.x / size.x : 1.0f,
			size.y > 0.001f ? targetSize.y / size.y : 1.0f,
			size.z > 0.001f ? targetSize.z / size.z : 1.0f );
	}

	private BBox GetAuthoredLocalBounds()
	{
		if ( BarricadeCollider is not null )
			return BarricadeCollider.LocalBounds;

		if ( AuthoredHammerMesh?.Model is not null )
			return AuthoredHammerMesh.Model.Bounds;

		return AuthoredTarget.GetLocalBounds();
	}

	public bool TryApplyDamage(
		LargeLadDamageContext damage,
		out LargeLadDamageContext appliedDamage )
	{
		appliedDamage = damage.WithAppliedDamage( 0.0f );

		if ( !Networking.IsHost || NetworkState is null || IsDestroyed || CurrentHealth <= 0.0f )
			return false;

		float amount;

		if ( Mode == LargeLadBarricadeMode.SkinnyProgression )
		{
			if ( damage.AttackerRole != LargeLadRole.SkinnyKid ||
				damage.DamageType != LargeLadDamageType.Firearm )
			{
				return false;
			}

			amount = System.MathF.Max( 0.0f, damage.BaseDamage );
		}
		else
		{
			if ( damage.AttackerRole != LargeLadRole.LargeLad ||
				damage.DamageType != LargeLadDamageType.Melee )
			{
				return false;
			}

			amount = System.MathF.Max( 0.0f, LadStructuralDamage );
		}

		if ( amount <= 0.0f )
			return false;

		if ( !NetworkState.TryApplyDamage( amount ) )
			return false;

		appliedDamage = damage.WithAppliedDamage( amount );
		ApplyDestroyedState();

		return true;
	}

	public void ResetForRound()
	{
		if ( !Networking.IsHost )
			return;

		ResolveAuthoredParts();
		ResolveNetworkState();

		if ( NetworkState is null )
		{
			Log.Error( $"Cannot reset barricade '{AuthoredTarget.Name}' without a network state child." );
			return;
		}

		NetworkState.Reset( MaximumHealth );
		ApplyDestroyedState();
		Log.Info( $"Reset barricade '{AuthoredTarget.Name}' for the new round." );
	}

	public void HandleNetworkStateChanged( bool oldValue, bool newValue )
	{
		ApplyDestroyedState();

		if ( newValue && !oldValue )
		{
			PlayCosmeticDebris();
		}
	}

	private void ApplyDestroyedState()
	{
		appliedDestroyedState = IsDestroyed;

		if ( AuthoredHammerMesh is not null )
		{
			AuthoredHammerMesh.Enabled = !IsDestroyed;

			if ( !IsDestroyed )
				SetCosmeticDebrisEnabled( false );

			return;
		}

		if ( BarricadeRenderer is not null )
			BarricadeRenderer.Enabled = !IsDestroyed;

		if ( BarricadeCollider is not null )
			BarricadeCollider.Enabled = !IsDestroyed;

		if ( !IsDestroyed )
			SetCosmeticDebrisEnabled( false );
	}

	private void PlayCosmeticDebris()
	{
		SetCosmeticDebrisEnabled( true );
		Invoke( System.MathF.Max( 0.1f, CosmeticDebrisLifetime ), () =>
		{
			if ( IsValid )
				SetCosmeticDebrisEnabled( false );
		} );
	}

	private void SetCosmeticDebrisEnabled( bool enabled )
	{
		foreach ( var debris in CosmeticDebris )
		{
			if ( debris is not null )
				debris.Enabled = enabled;
		}
	}

	protected override void DrawGizmos()
	{
		ResolveAuthoredParts();

		// In Hammer, the tied brush is already the exact editable preview.
		// Drawing another component-space box over it is both redundant and,
		// because Hammer owns the brush transform, prone to being offset.
		if ( AuthoredHammerMesh is not null )
			return;

		var padding = new Vector3( System.MathF.Max( 0.0f, GizmoPadding ) );
		var worldBounds = BarricadeCollider is not null
			? BarricadeCollider.GetWorldBounds()
			: AuthoredTarget.GetBounds();
		var paddedBounds = new BBox(
			worldBounds.Mins - padding,
			worldBounds.Maxs + padding );
		var color = Mode == LargeLadBarricadeMode.SkinnyProgression
			? new Color( 0.25f, 0.85f, 1.0f )
			: new Color( 1.0f, 0.22f, 0.08f );

		Gizmo.Transform = global::Transform.Zero;
		Gizmo.Draw.IgnoreDepth = true;
		Gizmo.Draw.Color = color.WithAlpha( 0.16f );
		Gizmo.Draw.SolidBox( paddedBounds );
		Gizmo.Draw.Color = color.WithAlpha( 0.95f );
		Gizmo.Draw.LineThickness = 2.0f;
		Gizmo.Draw.LineBBox( paddedBounds );
		Gizmo.Draw.LineThickness = 1.0f;
		Gizmo.Draw.IgnoreDepth = false;
	}
}
