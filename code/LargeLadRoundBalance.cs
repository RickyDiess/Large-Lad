using Sandbox;

/// <summary>
/// Fixed player-count bands selected from the Skinny Kids present at the
/// successful start of a round.
/// </summary>
public enum LargeLadBalanceBand
{
	Small,
	Medium,
	Large,
	VeryLarge,
	Full
}

/// <summary>
/// The synchronized facts that make one round's balance selection
/// authoritative. Roster changes retain this state; only another successful
/// round start replaces it.
/// </summary>
public readonly struct LargeLadRoundBalanceState
{
	public LargeLadRoundBalanceState(
		bool hasSelection,
		LargeLadBalanceBand selectedBand,
		int skinnyKidCountAtRoundStart )
	{
		HasSelection = hasSelection;
		SelectedBand = selectedBand;
		SkinnyKidCountAtRoundStart = skinnyKidCountAtRoundStart;
	}

	public bool HasSelection { get; }
	public LargeLadBalanceBand SelectedBand { get; }
	public int SkinnyKidCountAtRoundStart { get; }
}

/// <summary>
/// Health-only multipliers for one player-count band.
/// </summary>
public sealed class LargeLadBalanceBandMultipliers
{
	[Property]
	public float LargeLadMaximumHealth { get; set; } = 1.0f;

	[Property]
	public float SkinnyProgressionBarricadeMaximumHealth { get; set; } = 1.0f;
}

/// <summary>
/// Central, authored player-count balance configuration. Medium is the neutral
/// baseline. The other defaults are deliberately restrained and provisional.
/// </summary>
[AssetType(
	Name = "Large Lad Round Balance",
	Extension = "llbalance",
	Category = "Large Lad",
	Flags = AssetTypeFlags.NoEmbedding )]
public sealed class LargeLadRoundBalanceSettings : GameResource
{
	[Property]
	public LargeLadBalanceBandMultipliers Small { get; set; } = new()
	{
		LargeLadMaximumHealth = 0.9f,
		SkinnyProgressionBarricadeMaximumHealth = 0.9f
	};

	[Property]
	public LargeLadBalanceBandMultipliers Medium { get; set; } = new();

	[Property]
	public LargeLadBalanceBandMultipliers Large { get; set; } = new()
	{
		LargeLadMaximumHealth = 1.1f,
		SkinnyProgressionBarricadeMaximumHealth = 1.1f
	};

	[Property]
	public LargeLadBalanceBandMultipliers VeryLarge { get; set; } = new()
	{
		LargeLadMaximumHealth = 1.2f,
		SkinnyProgressionBarricadeMaximumHealth = 1.2f
	};

	[Property]
	public LargeLadBalanceBandMultipliers Full { get; set; } = new()
	{
		LargeLadMaximumHealth = 1.3f,
		SkinnyProgressionBarricadeMaximumHealth = 1.3f
	};

	public bool TryGetMultipliers(
		LargeLadBalanceBand band,
		out LargeLadBalanceBandMultipliers multipliers )
	{
		multipliers = band switch
		{
			LargeLadBalanceBand.Small => Small,
			LargeLadBalanceBand.Medium => Medium,
			LargeLadBalanceBand.Large => Large,
			LargeLadBalanceBand.VeryLarge => VeryLarge,
			LargeLadBalanceBand.Full => Full,
			_ => null
		};

		return multipliers is not null;
	}

	public IReadOnlyList<string> GetValidationWarnings()
	{
		var warnings = new List<string>();

		ValidateBand( warnings, nameof( Small ), Small );
		ValidateBand( warnings, nameof( Medium ), Medium );
		ValidateBand( warnings, nameof( Large ), Large );
		ValidateBand( warnings, "Very Large", VeryLarge );
		ValidateBand( warnings, nameof( Full ), Full );

		return warnings;
	}

	private static void ValidateBand(
		List<string> warnings,
		string bandName,
		LargeLadBalanceBandMultipliers multipliers )
	{
		if ( multipliers is null )
		{
			warnings.Add( $"{bandName} round-balance multipliers are missing." );
			return;
		}

		ValidateMultiplier(
			warnings,
			bandName,
			"Large Lad maximum-health",
			multipliers.LargeLadMaximumHealth );
		ValidateMultiplier(
			warnings,
			bandName,
			"SkinnyProgression barricade maximum-health",
			multipliers.SkinnyProgressionBarricadeMaximumHealth );
	}

	private static void ValidateMultiplier(
		List<string> warnings,
		string bandName,
		string fieldName,
		float value )
	{
		if ( !float.IsFinite( value ) || value <= 0.0f )
		{
			warnings.Add(
				$"{bandName} {fieldName} multiplier must be finite and positive." );
		}
	}
}

/// <summary>
/// Pure round-balance decisions shared by runtime code and unit tests.
/// </summary>
public static class LargeLadRoundBalanceRules
{
	public const int MinimumSkinnyKidCount = 1;
	public const int MaximumSkinnyKidCount = 31;

	public static LargeLadBalanceBand GetBand( int skinnyKidCount )
	{
		if ( skinnyKidCount < MinimumSkinnyKidCount ||
			skinnyKidCount > MaximumSkinnyKidCount )
		{
			throw new System.ArgumentOutOfRangeException(
				nameof( skinnyKidCount ),
				$"Skinny Kid count must be {MinimumSkinnyKidCount} through " +
				$"{MaximumSkinnyKidCount}." );
		}

		return skinnyKidCount switch
		{
			<= 3 => LargeLadBalanceBand.Small,
			<= 7 => LargeLadBalanceBand.Medium,
			<= 15 => LargeLadBalanceBand.Large,
			<= 23 => LargeLadBalanceBand.VeryLarge,
			_ => LargeLadBalanceBand.Full
		};
	}

	/// <summary>
	/// Replaces the state only at a confirmed successful round-start boundary.
	/// Disconnects, deaths, conversions, and late joins pass false and retain
	/// the original selection.
	/// </summary>
	public static LargeLadRoundBalanceState ResolveState(
		LargeLadRoundBalanceState current,
		int currentSkinnyKidCount,
		bool roundSuccessfullyBeginning )
	{
		if ( !roundSuccessfullyBeginning )
			return current;

		return new LargeLadRoundBalanceState(
			hasSelection: true,
			GetBand( currentSkinnyKidCount ),
			currentSkinnyKidCount );
	}

	/// <summary>
	/// Composes rather than overwrites multiplier layers, so a future
	/// map-specific health factor can be supplied without mutating the band's
	/// authoritative value.
	/// </summary>
	public static float ComposeHealthMultipliers(
		float bandMultiplier,
		float mapSpecificMultiplier = 1.0f )
	{
		return NormalizeMultiplier( bandMultiplier ) *
			NormalizeMultiplier( mapSpecificMultiplier );
	}

	/// <summary>
	/// Always derives health from its authored baseline. Calling this during
	/// every reset therefore cannot compound earlier results.
	/// </summary>
	public static float GetScaledMaximumHealth(
		float authoredMaximumHealth,
		float bandMultiplier,
		float mapSpecificMultiplier = 1.0f )
	{
		var baseline = float.IsFinite( authoredMaximumHealth )
			? System.MathF.Max( 1.0f, authoredMaximumHealth )
			: 1.0f;
		return System.MathF.Max(
			1.0f,
			baseline * ComposeHealthMultipliers(
				bandMultiplier,
				mapSpecificMultiplier ) );
	}

	private static float NormalizeMultiplier( float multiplier )
	{
		return float.IsFinite( multiplier ) && multiplier > 0.0f
			? multiplier
			: 1.0f;
	}
}
