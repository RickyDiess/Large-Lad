using Sandbox;
using System.Collections.Generic;

/// <summary>
/// One cumulative presentation stage for a compound barricade. Thresholds are
/// expressed as remaining-health fractions so authored stages continue to line
/// up when round balance scales maximum health.
/// </summary>
[Description(
	"Breaks the next authored child pieces when the barricade reaches the " +
	"configured remaining-health fraction." )]
public sealed class LargeLadBarricadeStage
{
	[Property, Title( "Remaining Health Fraction" )]
	[Description(
		"Activates this stage at or below this fraction of maximum health. Use a " +
		"unique value greater than 0 and less than 1, such as 0.75 or 0.5." )]
	public float RemainingHealthFraction { get; set; } = -1.0f;

	[Property, Title( "Child Objects To Break" )]
	[Description(
		"Breaks this many of the next intact direct child GameObjects, following " +
		"their hierarchy order. Use 0 for a visual-free threshold." )]
	public int ChildObjectsToBreak { get; set; } = 1;
}

/// <summary>
/// One round's one-shot destruction edge. Reset deliberately rearms it for the
/// next round without retaining any spawn-system knowledge.
/// </summary>
public sealed class LargeLadBarricadeDestructionGate
{
	public bool HasCommittedDestruction { get; private set; }

	public bool TryCommitDestruction()
	{
		if ( HasCommittedDestruction )
			return false;

		HasCommittedDestruction = true;
		return true;
	}

	public void ResetForRound()
	{
		HasCommittedDestruction = false;
	}
}

/// <summary>
/// Pure staged-barricade decisions shared by runtime code and unit tests.
/// </summary>
public static class LargeLadBarricadeStageRules
{
	public static bool IsValidThreshold( float threshold )
	{
		return float.IsFinite( threshold ) &&
			threshold > 0.0f &&
			threshold < 1.0f;
	}

	public static IReadOnlyList<string> GetThresholdWarnings(
		IReadOnlyList<float> thresholds )
	{
		var warnings = new List<string>();
		var firstStageByThreshold = new Dictionary<float, int>();

		if ( thresholds is null )
			return warnings;

		for ( var index = 0; index < thresholds.Count; index++ )
		{
			var threshold = thresholds[index];
			var stageNumber = index + 1;

			if ( !float.IsFinite( threshold ) || threshold < 0.0f )
			{
				warnings.Add(
					$"Stage {stageNumber} is missing a remaining-health threshold." );
				continue;
			}

			if ( !IsValidThreshold( threshold ) )
			{
				warnings.Add(
					$"Stage {stageNumber} threshold must be greater than 0 and " +
					"less than 1." );
				continue;
			}

			if ( firstStageByThreshold.TryGetValue(
				threshold,
				out var firstStage ) )
			{
				warnings.Add(
					$"Stage {stageNumber} duplicates stage {firstStage}'s " +
					$"{threshold:0.###} threshold." );
				continue;
			}

			firstStageByThreshold.Add( threshold, stageNumber );
		}

		return warnings;
	}

	public static int GetActiveStageCount(
		float currentHealth,
		float maximumHealth,
		IReadOnlyList<float> thresholds )
	{
		if ( thresholds is null ||
			!float.IsFinite( currentHealth ) ||
			!float.IsFinite( maximumHealth ) ||
			maximumHealth <= 0.0f )
		{
			return 0;
		}

		var remainingFraction = System.Math.Clamp(
			currentHealth / maximumHealth,
			0.0f,
			1.0f );
		var count = 0;

		foreach ( var threshold in thresholds )
		{
			if ( IsValidThreshold( threshold ) &&
				remainingFraction <= threshold )
			{
				count++;
			}
		}

		return count;
	}

	public static int GetCumulativeChildBreakCount(
		int activeStageCount,
		int totalChildCount,
		IReadOnlyList<int> orderedStageBreakCounts )
	{
		if ( orderedStageBreakCounts is null || totalChildCount <= 0 )
			return 0;

		var stageCount = System.Math.Clamp(
			activeStageCount,
			0,
			orderedStageBreakCounts.Count );
		var childCount = 0;

		for ( var index = 0; index < stageCount; index++ )
		{
			childCount += System.Math.Max(
				0,
				orderedStageBreakCounts[index] );
		}

		return System.Math.Clamp( childCount, 0, totalChildCount );
	}

	public static bool ShouldOpenPassage(
		bool isDestroyed,
		bool stagedPassageEnabled,
		float stagedPassageHealthFraction,
		float currentHealth,
		float maximumHealth )
	{
		if ( isDestroyed )
			return true;

		if ( !stagedPassageEnabled ||
			!IsValidThreshold( stagedPassageHealthFraction ) ||
			!float.IsFinite( currentHealth ) ||
			!float.IsFinite( maximumHealth ) ||
			maximumHealth <= 0.0f )
		{
			return false;
		}

		return currentHealth / maximumHealth <=
			stagedPassageHealthFraction;
	}

	public static string CreateDestructionAnnouncement(
		bool announcementEnabled,
		LargeLadBarricadeMode mode,
		string mapperDisplayName )
	{
		if ( !announcementEnabled ||
			mode != LargeLadBarricadeMode.SkinnyProgression ||
			string.IsNullOrWhiteSpace( mapperDisplayName ) )
		{
			return null;
		}

		var words = mapperDisplayName.Split(
			(char[])null,
			System.StringSplitOptions.RemoveEmptyEntries );
		var normalizedName = string.Join( " ", words );

		return string.IsNullOrWhiteSpace( normalizedName )
			? null
			: $"{normalizedName} destroyed.";
	}
}
