using Sandbox;
using System.Linq;

public sealed class LargeLadKillVolume : Component, Component.ITriggerListener
{
	[Property]
	public Collider TriggerCollider { get; set; }

	public HammerMesh TriggerMesh { get; private set; }

	public bool HasTriggerShape => TriggerMesh is not null || TriggerCollider is not null;

	[Property, Title( "Editor Gizmo Padding" )]
	public float GizmoPadding { get; set; } = 2.0f;

	protected override void OnAwake()
	{
		ResolveTriggerCollider();
	}

	protected override void OnStart()
	{
		LargeLadHammerPreview.HideAtRuntime( GameObject );
		ResolveTriggerCollider();
		ConfigureTriggerShape();
	}

	protected override void OnUpdate()
	{
		// Tied Hammer meshes can arrive after this component during VMAP loading.
		// Retry until the authored trigger shape has been resolved.
		if ( HasTriggerShape )
			return;

		ResolveTriggerCollider();
		ConfigureTriggerShape();
	}

	protected override void OnValidate()
	{
		ResolveTriggerCollider();
		ConfigureTriggerShape();

		// The deferred whole-map validator handles a genuinely missing shape.
		// Logging here creates a false warning before Hammer attaches its mesh.
	}

	private void ResolveTriggerCollider()
	{
		TriggerMesh = Components.Get<HammerMesh>( FindMode.EverythingInSelf );

		if ( TriggerCollider is null || !TriggerCollider.IsValid )
			TriggerCollider = Components.Get<Collider>( FindMode.EverythingInSelf );
	}

	private void ConfigureTriggerShape()
	{
		if ( TriggerMesh is not null )
		{
			TriggerMesh.UseCollision = true;
			TriggerMesh.UseRenderer = false;
			TriggerMesh.IsTrigger = true;
			TriggerMesh.Static = true;
		}

		if ( TriggerCollider is not null )
			TriggerCollider.IsTrigger = true;
	}

	public void OnTriggerEnter( Collider other )
	{
		if ( !Networking.IsHost )
			return;

		var player = other?.GameObject?.Components.Get<LargeLadPlayer>(
			FindMode.EverythingInSelfAndAncestors );

		if ( player?.Health is null || player.Health.IsDead )
			return;

		var round = Scene
			.GetAllComponents<LargeLadRoundManager>()
			.FirstOrDefault();

		round?.HandleKillVolumeDeath( player );
	}

	public void OnTriggerExit( Collider other )
	{
	}

	protected override void DrawGizmos()
	{
		ResolveTriggerCollider();

		// A tied Hammer brush is the authoritative, editable preview. Hammer
		// already renders it at its exact bounds, so avoid a second offset box.
		if ( TriggerMesh is not null )
			return;

		var padding = new Vector3( System.MathF.Max( 0.0f, GizmoPadding ) );
		var worldBounds = TriggerCollider is not null
			? TriggerCollider.GetWorldBounds()
			: GameObject.GetBounds();
		var paddedBounds = new BBox(
			worldBounds.Mins - padding,
			worldBounds.Maxs + padding );
		var color = new Color( 1.0f, 0.0f, 0.4f );

		Gizmo.Transform = global::Transform.Zero;
		Gizmo.Draw.IgnoreDepth = true;
		Gizmo.Draw.Color = color.WithAlpha( 0.14f );
		Gizmo.Draw.SolidBox( paddedBounds );
		Gizmo.Draw.Color = color.WithAlpha( 0.95f );
		Gizmo.Draw.LineThickness = 2.0f;
		Gizmo.Draw.LineBBox( paddedBounds );
		Gizmo.Draw.LineThickness = 1.0f;
		Gizmo.Draw.IgnoreDepth = false;
	}

}
