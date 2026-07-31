using Sandbox;
using System.Collections.Generic;

/// <summary>
/// Explicit opt-in breakable for the Large Lad Eat fallback. Merely having a
/// physics body never makes an entity eligible; a mapper must add this
/// component (or use a Lad Shortcut barricade).
/// </summary>
public sealed class LargeLadEatSmashable : LargeLadRoundResettableComponent,
	ILargeLadDamageable
{
	[Property]
	public float MaximumHealth { get; set; } = 100.0f;

	[Property]
	public Collider BreakableCollider { get; set; }

	[Sync( SyncFlags.FromHost )]
	public float CurrentHealth { get; private set; }

	[Sync( SyncFlags.FromHost ), Change( nameof( OnDestroyedChanged ) )]
	public bool IsDestroyed { get; private set; }

	private readonly Dictionary<Renderer, bool> authoredRendererStates = new();
	private bool authoredColliderEnabled;
	private bool hasCapturedState;

	public Vector3 GetClosestWorldPoint( Vector3 worldPoint )
	{
		if ( BreakableCollider is not null )
			return BreakableCollider.FindClosestPoint( worldPoint );

		var localPoint = GameObject.WorldTransform.PointToLocal( worldPoint );
		var closest = GameObject.GetLocalBounds().ClosestPoint( localPoint );
		return GameObject.WorldTransform.PointToWorld( closest );
	}

	public static LargeLadEatSmashable FindFor( GameObject target )
	{
		return target?.Components.Get<LargeLadEatSmashable>(
			FindMode.EverythingInSelfAndAncestors );
	}

	protected override void OnAwake()
	{
		ConfigureObject();
		ResolveCollider();
	}

	protected override void OnStart()
	{
		ConfigureObject();
		ResolveCollider();
		CaptureAuthoredState();

		if ( Networking.IsHost && CurrentHealth <= 0.0f && !IsDestroyed )
			CurrentHealth = System.MathF.Max( 1.0f, MaximumHealth );

		RefreshPresentation();
	}

	protected override void OnValidate()
	{
		ResolveCollider();

		if ( MaximumHealth <= 0.0f )
			Log.Warning( $"{GameObject.Name}: Eat-smashable health must be positive." );

		if ( BreakableCollider is null )
		{
			Log.Warning(
				$"{GameObject.Name}: Eat-smashable needs an authored collider." );
		}
	}

	public bool TryApplyDamage(
		LargeLadDamageContext damage,
		out LargeLadDamageContext appliedDamage )
	{
		appliedDamage = damage.WithAppliedDamage( 0.0f );

		if ( !Networking.IsHost ||
			IsDestroyed ||
			CurrentHealth <= 0.0f ||
			damage.AttackerRole != LargeLadRole.LargeLad ||
			damage.DamageType != LargeLadDamageType.Melee )
		{
			return false;
		}

		var amount = System.MathF.Max( 0.0f, damage.BaseDamage );

		if ( amount <= 0.0f )
			return false;

		var previousHealth = CurrentHealth;
		CurrentHealth = System.MathF.Max( 0.0f, CurrentHealth - amount );
		appliedDamage = damage.WithAppliedDamage(
			previousHealth - CurrentHealth );

		if ( CurrentHealth <= 0.0f )
			IsDestroyed = true;

		RefreshPresentation();
		return appliedDamage.AppliedDamage > 0.0f;
	}

	public override void ResetForRound()
	{
		if ( !Networking.IsHost )
			return;

		ResolveCollider();
		CaptureAuthoredState();
		CurrentHealth = System.MathF.Max( 1.0f, MaximumHealth );
		IsDestroyed = false;
		RefreshPresentation();
	}

	private void ConfigureObject()
	{
		GameObject.NetworkMode = NetworkMode.Object;
	}

	private void ResolveCollider()
	{
		BreakableCollider ??= Components.Get<Collider>(
			FindMode.EverythingInSelfAndDescendants );
	}

	private void CaptureAuthoredState()
	{
		if ( hasCapturedState )
			return;

		authoredRendererStates.Clear();

		foreach ( var renderer in Components.GetAll<Renderer>(
			FindMode.EverythingInSelfAndDescendants ) )
		{
			if ( renderer is not null && renderer.IsValid )
				authoredRendererStates[renderer] = renderer.Enabled;
		}

		authoredColliderEnabled = BreakableCollider?.Enabled == true;
		hasCapturedState = true;
	}

	private void OnDestroyedChanged( bool oldValue, bool newValue )
	{
		RefreshPresentation();
	}

	private void RefreshPresentation()
	{
		if ( !hasCapturedState )
			return;

		foreach ( var entry in authoredRendererStates )
		{
			if ( entry.Key is not null && entry.Key.IsValid )
				entry.Key.Enabled = entry.Value && !IsDestroyed;
		}

		if ( BreakableCollider is not null && BreakableCollider.IsValid )
			BreakableCollider.Enabled = authoredColliderEnabled && !IsDestroyed;
	}
}
