using Sandbox;

public sealed class LargeLadKillVolume : Component, Component.ITriggerListener
{
	[Property]
	public Collider TriggerCollider { get; set; }

	[Property, Title( "Editor Gizmo Padding" )]
	public float GizmoPadding { get; set; } = 2.0f;

	protected override void OnAwake()
	{
		ResolveTriggerCollider();
	}

	protected override void OnStart()
	{
		ResolveTriggerCollider();

		if ( TriggerCollider is not null )
		{
			TriggerCollider.IsTrigger = true;
		}
	}

	protected override void OnValidate()
	{
		ResolveTriggerCollider();

		if ( TriggerCollider is null )
			Log.Warning( $"{GameObject.Name}: kill volume is missing its trigger collider reference." );
	}

	private void ResolveTriggerCollider()
	{
		TriggerCollider ??= Components.Get<Collider>();
	}

	public void OnTriggerEnter( Collider other )
	{
		if ( !Networking.IsHost )
			return;

		var player = other?.GameObject?.Components.Get<LargeLadPlayer>(
			FindMode.EverythingInSelfAndAncestors );

		if ( player?.Health is null || player.Health.IsDead )
			return;

		if ( player.HasKillVolumeTeleportGrace )
		{
			Log.Info(
				$"{GameObject.Name} ignored {player.GameObject.Name} during teleport settle." );
			return;
		}

		Log.Info(
			$"{player.GameObject.Name} entered {GameObject.Name} at " +
			$"{player.GameObject.WorldPosition}." );

		LargeLadGameManager.FindForScene( Scene )?
			.RequestEnvironmentalDeath( player );
	}

	public void OnTriggerExit( Collider other )
	{
	}

	protected override void DrawGizmos()
	{
		ResolveTriggerCollider();

		var colliderObject = TriggerCollider?.GameObject ?? GameObject;
		var padding = new Vector3( System.MathF.Max( 0.0f, GizmoPadding ) );
		var localBounds = colliderObject.GetLocalBounds();
		var paddedBounds = new BBox(
			localBounds.Mins - padding,
			localBounds.Maxs + padding );
		var color = new Color( 1.0f, 0.0f, 0.4f );

		Gizmo.Transform = colliderObject.WorldTransform;
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
