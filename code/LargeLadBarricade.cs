using Sandbox;
using System.Collections.Generic;

public enum LargeLadBarricadeMode
{
	SkinnyProgression,
	LadShortcut
}

/// <summary>
/// A self-contained scene destructible. Put this component on the same
/// GameObject as the scene mesh or renderer and collider.
/// </summary>
public sealed class LargeLadBarricade : LargeLadRoundResettableComponent,
	ILargeLadDamageable
{
	[Property]
	public LargeLadBarricadeMode Mode { get; set; }

	[Property]
	public float MaximumHealth { get; set; } = 300.0f;

	[Property]
	public Component BarricadeRenderer { get; set; }

	[Property]
	public Collider BarricadeCollider { get; set; }

	[Property, Title( "Local Cosmetic Debris" )]
	public List<GameObject> CosmeticDebris { get; set; } = new();

	[Property]
	public float CosmeticDebrisLifetime { get; set; } = 1.5f;

	[Property, Title( "Editor Gizmo Padding" )]
	public float GizmoPadding { get; set; } = 2.0f;

	[Sync( SyncFlags.FromHost )]
	public float CurrentHealth { get; private set; }

	[Sync( SyncFlags.FromHost ), Change( nameof( OnDestroyedChanged ) )]
	public bool IsDestroyed { get; private set; }

	public bool HasVisibleGeometry => BarricadeRenderer is not null;

	public bool HasCollision => BarricadeCollider is not null;

	public GameObject AuthoredTarget => GameObject;

	private bool? appliedDestroyedState;

	public Vector3 GetClosestWorldPoint( Vector3 worldPoint )
	{
		if ( BarricadeCollider is not null )
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
		SetCosmeticDebrisEnabled( false );

		if ( Networking.IsHost && CurrentHealth <= 0.0f && !IsDestroyed )
			CurrentHealth = System.MathF.Max( 1.0f, MaximumHealth );

		ApplyDestroyedState();
	}

	protected override void OnUpdate()
	{
		if ( appliedDestroyedState == IsDestroyed )
			return;

		ResolveAuthoredParts();
		ApplyDestroyedState();
	}

	protected override void OnValidate()
	{
		ConfigureObject();
		ResolveAuthoredParts();

		if ( MaximumHealth <= 0.0f )
			Log.Warning( $"{GameObject.Name}: barricade health must be positive." );

		if ( BarricadeRenderer is null || BarricadeCollider is null )
		{
			Log.Warning(
				$"{GameObject.Name}: add LargeLadBarricade to the same " +
				"GameObject as its rendering and collision." );
		}
	}

	private void ConfigureObject()
	{
		GameObject.NetworkMode = NetworkMode.Object;
		GameObject.IsStatic = true;
	}

	private void ResolveAuthoredParts()
	{
		var editableMesh = Components.Get<MeshComponent>(
			FindMode.EverythingInSelf );

		if ( editableMesh is not null )
		{
			BarricadeRenderer = editableMesh;
			BarricadeCollider = editableMesh;
			return;
		}

		BarricadeRenderer ??= Components.Get<Renderer>(
			FindMode.EverythingInSelf );
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
		{
			IsDestroyed = true;
			ApplyDestroyedState();
		}

		return true;
	}

	public override void ResetForRound()
	{
		if ( !Networking.IsHost )
			return;

		ResolveAuthoredParts();
		CurrentHealth = System.MathF.Max( 1.0f, MaximumHealth );
		IsDestroyed = false;
		ApplyDestroyedState();
		Log.Info( $"Reset barricade '{GameObject.Name}' for the new round." );
	}

	private void OnDestroyedChanged( bool oldValue, bool newValue )
	{
		ApplyDestroyedState();

		if ( newValue && !oldValue )
			PlayCosmeticDebris();
	}

	private void ApplyDestroyedState()
	{
		appliedDestroyedState = IsDestroyed;

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

		var padding = new Vector3( System.MathF.Max( 0.0f, GizmoPadding ) );
		var bounds = BarricadeCollider is not null
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
