using Sandbox;
using System.Collections.Generic;
using System.Linq;

public enum LargeLadBarricadeMode
{
	SkinnyProgression,
	LadShortcut
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

	private bool? appliedDestroyedState;

	public GameObject AuthoredTarget => GameObject.Parent ?? GameObject;

	public Vector3 GetClosestWorldPoint( Vector3 worldPoint )
	{
		var authoredObject = AuthoredTarget;
		var localPoint = authoredObject.WorldTransform.PointToLocal( worldPoint );
		var closestLocalPoint = authoredObject.GetLocalBounds().ClosestPoint( localPoint );
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
	}

	protected override void OnStart()
	{
		ResolveAuthoredParts();
		SetCosmeticDebrisEnabled( false );

		if ( Networking.IsHost && CurrentHealth <= 0.0f && !IsDestroyed )
		{
			CurrentHealth = System.MathF.Max( 1.0f, MaximumHealth );
		}

		ApplyDestroyedState();
	}

	protected override void OnUpdate()
	{
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

		if ( MaximumHealth <= 0.0f )
			Log.Warning( $"{GameObject.Name}: barricade health must be positive." );

		if ( GameObject.Parent is null )
			Log.Warning( $"{GameObject.Name}: barricade state must be a child of its authored geometry." );

		if ( BarricadeRenderer is null || BarricadeCollider is null )
			Log.Warning( $"{GameObject.Name}: parent needs a MeshComponent, or a renderer and collider." );
	}

	private void ResolveAuthoredParts()
	{
		var authoredObject = GameObject.Parent;

		BarricadeRenderer = null;
		BarricadeCollider = null;

		if ( authoredObject is null )
			return;

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
		BarricadeCollider = authoredObject.Components.Get<Collider>(
			FindMode.EverythingInSelf );
		BarricadeRenderer ??= BarricadeCollider;
	}

	public bool TryApplyDamage(
		LargeLadDamageContext damage,
		out LargeLadDamageContext appliedDamage )
	{
		appliedDamage = damage.WithAppliedDamage( 0.0f );

		if ( !Networking.IsHost || IsDestroyed || CurrentHealth <= 0.0f )
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

		CurrentHealth = System.MathF.Max( 0.0f, CurrentHealth - amount );
		appliedDamage = damage.WithAppliedDamage( amount );

		if ( CurrentHealth <= 0.0f )
		{
			IsDestroyed = true;
			ApplyDestroyedState();
		}

		return true;
	}

	public void ResetForRound()
	{
		if ( !Networking.IsHost )
			return;

		ResolveAuthoredParts();
		CurrentHealth = System.MathF.Max( 1.0f, MaximumHealth );
		IsDestroyed = false;
		ApplyDestroyedState();
		Log.Info( $"Reset barricade '{AuthoredTarget.Name}' for the new round." );
	}

	private void OnDestroyedChanged( bool oldValue, bool newValue )
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

		var authoredObject = AuthoredTarget;
		var padding = new Vector3( System.MathF.Max( 0.0f, GizmoPadding ) );
		var localBounds = authoredObject.GetLocalBounds();
		var paddedBounds = new BBox(
			localBounds.Mins - padding,
			localBounds.Maxs + padding );
		var color = Mode == LargeLadBarricadeMode.SkinnyProgression
			? new Color( 0.25f, 0.85f, 1.0f )
			: new Color( 1.0f, 0.22f, 0.08f );

		Gizmo.Transform = authoredObject.WorldTransform;
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
