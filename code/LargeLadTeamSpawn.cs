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
/// Mapper-owned circular team-spawn area. Use the supplied Lobby, Skinny Kid,
/// or Hunter prefab, place it over walkable floor, then rebuild from the map
/// profile. A single clear area can provide a complete match's positions.
/// </summary>
public sealed class LargeLadTeamSpawn : Component
{
	public const float DefaultSpawnRadius = 192.0f;
	public const float DefaultMinimumSeparation = 48.0f;

	[Property, Group( "Spawn Area" ), Title( "Spawn Group" )]
	public LargeLadSpawnGroup Group { get; set; }

	[Property, Group( "Spawn Area" )]
	public float SpawnRadius { get; set; } = DefaultSpawnRadius;

	[Property, Group( "Spawn Area" )]
	public int Capacity { get; set; } =
		LargeLadGameManager.TargetPlayerCount;

	[Property, Group( "Spawn Area" )]
	public float MinimumSeparation { get; set; } =
		DefaultMinimumSeparation;

	private LargeLadSpawnAllocator cachedSpawnAllocator;
	private IReadOnlyList<LargeLadSpawnLocation> projectedCandidatesPreview;
	private Transform projectedPreviewTransform;
	private bool hasProjectedCandidatesPreview;

	public Color MarkerColor => Group switch
	{
		LargeLadSpawnGroup.Lobby => Color.White,
		LargeLadSpawnGroup.SkinnyKid => new Color( 0.25f, 0.85f, 1.0f ),
		LargeLadSpawnGroup.Hunter => new Color( 1.0f, 0.22f, 0.08f ),
		_ => Color.Gray
	};

	protected override void OnEnabled()
	{
		base.OnEnabled();
		GetSpawnAllocator()?.InvalidateCandidateCache();
	}

	protected override void OnDisabled()
	{
		ClearProjectedCandidatesPreview();
		GetSpawnAllocator()?.InvalidateCandidateCache();
		base.OnDisabled();
	}

	protected override void OnDestroy()
	{
		ClearProjectedCandidatesPreview();
		GetSpawnAllocator()?.InvalidateCandidateCache();
		base.OnDestroy();
	}

	protected override void OnValidate()
	{
		SpawnRadius = System.MathF.Max( 0.0f, SpawnRadius );
		Capacity = System.Math.Clamp(
			Capacity,
			1,
			LargeLadGameManager.TargetPlayerCount );
		MinimumSeparation = System.MathF.Max( 32.0f, MinimumSeparation );

		ClearProjectedCandidatesPreview();
		GetSpawnAllocator()?.InvalidateCandidateCache();
	}

	/// <summary>
	/// Reprojects all team-spawn candidates after static geometry has changed.
	/// Authored spawn-property edits invalidate the cache automatically.
	/// </summary>
	[Button( "Rebuild Projected Candidates" )]
	public void RebuildProjectedCandidates()
	{
		var projection =
			LargeLadSpawnProjection.RebuildAuthoringPreview( Scene );
		var valid = projection.GetCandidates( Group ).Count;
		var configured = projection.GetSpawns( Group ).Sum( spawn =>
			LargeLadSpawnRules.GetUsableAuthoredCapacity( spawn.Capacity ) );
		Log.Info(
			$"Rebuilt {Group} spawn projection: {valid} usable positions " +
			$"from {configured} configured across " +
			$"{projection.GetSpawns( Group ).Count} area(s)." );
	}

	protected override void DrawGizmos()
	{
		var radius = System.MathF.Max( 0.0f, SpawnRadius );
		var previewCount =
			LargeLadSpawnRules.GetUsableAuthoredCapacity( Capacity );
		var color = MarkerColor;
		IReadOnlyList<LargeLadSpawnLocation> cachedCandidates = null;
		var hasCachedCandidates = TryGetProjectedCandidatesPreview(
			out cachedCandidates );

		if ( !hasCachedCandidates )
		{
			var allocator = GetSpawnAllocator();
			hasCachedCandidates = allocator is not null &&
				allocator.TryGetCachedCandidates( this, out cachedCandidates );
		}

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
				var position =
					LargeLadSpawnRules.GetDeterministicLayoutOffset(
						index,
						previewCount,
						radius );

				Gizmo.Draw.Color = color.WithAlpha( 0.18f );
				Gizmo.Draw.SolidCapsule(
					position + Vector3.Up * 16.0f,
					position + Vector3.Up * 56.0f,
					16.0f,
					8,
					4 );
			}
		}

		var validCount = hasCachedCandidates ? cachedCandidates.Count : -1;
		var isIndividuallyShort = validCount >= 0 && validCount < previewCount;
		var validLabel = validCount >= 0
			? $"Valid {validCount}/{previewCount}" +
				(isIndividuallyShort ? " - NEEDS CLEARANCE" : string.Empty)
			: "Valid: rebuild to check";

		Gizmo.Draw.Color = color;
		Gizmo.Draw.Arrow( Vector3.Up * 54.0f, Vector3.Forward * 38.0f );
		Gizmo.Draw.Text(
			$"{FriendlyGroupName()} Spawn | Capacity {Capacity} | {validLabel}",
			new Transform( Vector3.Up * 82.0f ),
			"Inter",
			14.0f );
		Gizmo.Draw.LineThickness = 1.0f;
		Gizmo.Draw.IgnoreDepth = false;
	}

	private LargeLadSpawnAllocator GetSpawnAllocator()
	{
		if ( cachedSpawnAllocator is not null &&
			cachedSpawnAllocator.IsValid &&
			cachedSpawnAllocator.Enabled &&
			cachedSpawnAllocator.Scene == Scene )
		{
			return cachedSpawnAllocator;
		}

		cachedSpawnAllocator = Scene?
			.GetAllComponents<LargeLadSpawnAllocator>()
			.FirstOrDefault( allocator =>
				allocator is not null &&
				allocator.IsValid &&
				allocator.Enabled &&
				allocator.Scene == Scene );
		return cachedSpawnAllocator;
	}

	internal void SetProjectedCandidatesPreview(
		IReadOnlyList<LargeLadSpawnLocation> candidates )
	{
		projectedCandidatesPreview =
			candidates ?? System.Array.Empty<LargeLadSpawnLocation>();
		projectedPreviewTransform = GameObject.WorldTransform;
		hasProjectedCandidatesPreview = true;
	}

	private bool TryGetProjectedCandidatesPreview(
		out IReadOnlyList<LargeLadSpawnLocation> candidates )
	{
		if ( hasProjectedCandidatesPreview &&
			projectedPreviewTransform.Equals( GameObject.WorldTransform ) )
		{
			candidates = projectedCandidatesPreview;
			return true;
		}

		ClearProjectedCandidatesPreview();
		candidates = null;
		return false;
	}

	private void ClearProjectedCandidatesPreview()
	{
		projectedCandidatesPreview = null;
		hasProjectedCandidatesPreview = false;
	}

	private string FriendlyGroupName()
	{
		return Group == LargeLadSpawnGroup.SkinnyKid
			? "Skinny Kid"
			: Group.ToString();
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
