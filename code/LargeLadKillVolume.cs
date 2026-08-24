using Sandbox;
using System.Collections.Generic;

/// <summary>
/// Mapper-authored lethal trigger volume. Prefer the supplied Kill Volume
/// prefab, then resize and position its box collider.
/// </summary>
public sealed class LargeLadKillVolume : Component, Component.ITriggerListener
{
	[Property, Group( "Kill Volume" ), Title( "Trigger Collider" )]
	public Collider TriggerCollider { get; set; }

	[Property, Title( "Editor Gizmo Padding" )]
	public float GizmoPadding { get; set; } = 2.0f;

	private LargeLadGameManager cachedGameManager;

	protected override void OnAwake()
	{
		ResolveTriggerCollider();
		ResolveGameManager();
	}

	protected override void OnStart()
	{
		ResolveTriggerCollider();
		ResolveGameManager();

		if ( TriggerCollider is not null )
		{
			TriggerCollider.IsTrigger = true;
		}
	}

	protected override void OnValidate()
	{
		ResolveTriggerCollider();

		foreach ( var warning in GetValidationWarnings() )
			Log.Warning( $"{GameObject.Name}: kill volume: {warning}" );
	}

	public IReadOnlyList<string> GetValidationWarnings()
	{
		var warnings = new List<string>();

		if ( TriggerCollider is null )
		{
			warnings.Add(
				"its Trigger Collider is missing. Restore the supplied Kill " +
				"Volume prefab or assign a collider on this root." );
		}
		else if ( !TriggerCollider.IsTrigger )
		{
			warnings.Add(
				"its collider must have Is Trigger enabled. Enable it or restore " +
				"the supplied Kill Volume prefab." );
		}

		return warnings;
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

		var gameManager = GetGameManager();

		if ( player.HasKillVolumeTeleportGrace )
		{
			if ( gameManager?.EnableKillVolumeDebugLogging == true )
			{
				Log.Info(
					$"[Debug/Kill Volume] {GameObject.Name} ignored " +
					$"{player.GameObject.Name} during teleport settle." );
			}

			return;
		}

		if ( gameManager?.EnableKillVolumeDebugLogging == true )
		{
			Log.Info(
				$"[Debug/Kill Volume] {player.GameObject.Name} entered " +
				$"{GameObject.Name} at {player.GameObject.WorldPosition}." );
		}

		gameManager?.RequestEnvironmentalDeath( player );
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

	private LargeLadGameManager GetGameManager()
	{
		if ( cachedGameManager is not null &&
			cachedGameManager.IsValid &&
			cachedGameManager.Enabled &&
			cachedGameManager.Scene == Scene &&
			cachedGameManager.HasSceneGameplayOwnership )
		{
			return cachedGameManager;
		}

		ResolveGameManager();
		return cachedGameManager;
	}

	private void ResolveGameManager()
	{
		cachedGameManager = LargeLadGameManager.FindForScene( Scene );
	}
}
