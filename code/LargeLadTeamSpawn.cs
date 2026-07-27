using Sandbox;
using System.Collections.Generic;
using System.Linq;

public enum LargeLadSpawnGroup
{
	Lobby,
	SkinnyKid,
	Hunter
}

/// <summary>
/// Defines a circular spawn area for one team. A single component can provide
/// enough clear positions for an entire match.
/// </summary>
public sealed class LargeLadTeamSpawn : Component
{
	[Property]
	public LargeLadSpawnGroup Group { get; set; }

	[Property]
	public float SpawnRadius { get; set; } = 160.0f;

	[Property]
	public int Capacity { get; set; } = 16;

	[Property]
	public float MinimumSeparation { get; set; } = 48.0f;

	public Color MarkerColor => Group switch
	{
		LargeLadSpawnGroup.Lobby => Color.White,
		LargeLadSpawnGroup.SkinnyKid => new Color( 0.25f, 0.85f, 1.0f ),
		LargeLadSpawnGroup.Hunter => new Color( 1.0f, 0.22f, 0.08f ),
		_ => Color.Gray
	};

	protected override void OnValidate()
	{
		SpawnRadius = System.MathF.Max( 0.0f, SpawnRadius );
		Capacity = System.Math.Clamp(
			Capacity,
			1,
			LargeLadMapDefinition.TargetPlayerCount );
		MinimumSeparation = System.MathF.Max( 32.0f, MinimumSeparation );

		GetSpawnAllocator()?.InvalidateCandidateCache();
	}

	/// <summary>
	/// Reprojects all team-spawn candidates after static geometry has changed.
	/// Authored spawn-property edits invalidate the cache automatically.
	/// </summary>
	[Button]
	public void RebuildProjectedCandidates()
	{
		var allocator = GetSpawnAllocator();

		if ( allocator is null )
		{
			Log.Warning( "No LargeLadSpawnAllocator is available to rebuild." );
			return;
		}

		allocator.RebuildCandidateCache();
	}

	protected override void DrawGizmos()
	{
		var radius = System.MathF.Max( 0.0f, SpawnRadius );
		var previewCount = System.Math.Clamp(
			Capacity,
			1,
			LargeLadMapDefinition.TargetPlayerCount );
		var color = MarkerColor;
		const float goldenAngle = 2.39996323f;
		IReadOnlyList<LargeLadSpawnLocation> cachedCandidates = null;
		var allocator = GetSpawnAllocator();
		var hasCachedCandidates = allocator is not null &&
			allocator.TryGetCachedCandidates( this, out cachedCandidates );

		Gizmo.Draw.IgnoreDepth = true;
		Gizmo.Draw.LineThickness = 2.0f;
		Gizmo.Draw.Color = color.WithAlpha( 0.9f );
		DrawHorizontalCircle( radius );

		if ( hasCachedCandidates )
		{
			Gizmo.Transform = global::Transform.Zero;
			Gizmo.Draw.Color = color.WithAlpha( 0.18f );

			foreach ( var candidate in cachedCandidates )
			{
				Gizmo.Draw.SolidCapsule(
					candidate.Position + Vector3.Up * 16.0f,
					candidate.Position + Vector3.Up * 56.0f,
					16.0f,
					8,
					4 );
			}

			Gizmo.Transform = GameObject.WorldTransform;
		}
		else
		{
			for ( var index = 0; index < previewCount; index++ )
			{
				var normalizedRadius = System.MathF.Sqrt( (index + 0.5f) / previewCount );
				var angle = index * goldenAngle;
				var position = new Vector3(
					System.MathF.Cos( angle ) * radius * normalizedRadius,
					System.MathF.Sin( angle ) * radius * normalizedRadius,
					0.0f );

				Gizmo.Draw.Color = color.WithAlpha( 0.18f );
				Gizmo.Draw.SolidCapsule(
					position + Vector3.Up * 16.0f,
					position + Vector3.Up * 56.0f,
					16.0f,
					8,
					4 );
			}
		}

		Gizmo.Draw.Color = color;
		Gizmo.Draw.Arrow( Vector3.Up * 54.0f, Vector3.Forward * 38.0f );
		Gizmo.Draw.Text(
			$"{Group} Spawn ({Capacity})",
			new Transform( Vector3.Up * 82.0f ),
			"Inter",
			14.0f );
		Gizmo.Draw.LineThickness = 1.0f;
		Gizmo.Draw.IgnoreDepth = false;
	}

	private LargeLadSpawnAllocator GetSpawnAllocator()
	{
		return Scene?
			.GetAllComponents<LargeLadSpawnAllocator>()
			.FirstOrDefault();
	}

	private static void DrawHorizontalCircle( float radius )
	{
		const int sections = 64;
		var previous = new Vector3( radius, 0.0f, 0.0f );

		for ( var index = 1; index <= sections; index++ )
		{
			var angle = index * (System.MathF.PI * 2.0f / sections);
			var current = new Vector3(
				System.MathF.Cos( angle ) * radius,
				System.MathF.Sin( angle ) * radius,
				0.0f );

			Gizmo.Draw.Line( previous, current );
			previous = current;
		}
	}
}

public readonly struct LargeLadSpawnLocation
{
	public Vector3 Position { get; }

	public Rotation Rotation { get; }

	public float MinimumSeparation { get; }

	public LargeLadSpawnLocation(
		Vector3 position,
		Rotation rotation,
		float minimumSeparation )
	{
		Position = position;
		Rotation = rotation;
		MinimumSeparation = minimumSeparation;
	}
}
