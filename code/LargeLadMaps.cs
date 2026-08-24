using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// The one compatibility version understood by this Large Lad build. Maps use
/// an exact integer version because compatibility is binary; this is not a
/// package dependency or semantic-version range.
/// </summary>
public static class LargeLadMapContract
{
	public const int CurrentVersion = 1;

	public static bool IsSupported( int declaredVersion )
	{
		return declaredVersion == CurrentVersion;
	}
}

/// <summary>
/// The only gameplay-level values a map is allowed to tune. Zero means that
/// the map does not supply that override and the game-wide default is used.
/// </summary>
public sealed class LargeLadMapBalanceOverrides
{
	[Property, Title( "Survival Duration (Seconds)" )]
	[Description(
		"Zero uses the game default. A positive value replaces only the survival " +
		"phase duration for this map." )]
	public float SurvivalDurationSeconds { get; set; }

	[Property, Title( "Skinny Progression Barricade Health Multiplier" )]
	[Description(
		"Zero uses the neutral factor. A positive value composes with Large Lad's " +
		"player-count balance band." )]
	public float SkinnyProgressionBarricadeMaximumHealthMultiplier
	{
		get;
		set;
	}

	[Property, Title( "Large Lad Health Multiplier" )]
	[Description(
		"Zero uses the neutral factor. A positive value composes with Large Lad's " +
		"player-count balance band." )]
	public float LargeLadMaximumHealthMultiplier { get; set; }

	[Property, Title( "Late-Round Hunter Escalation Multiplier" )]
	[Description(
		"Zero uses the neutral factor. A value of at least one composes with both " +
		"global Hunter movement maximums without changing the ramp interval." )]
	public float HunterEscalationMultiplier { get; set; }
}

/// <summary>
/// Mapper-authored facts that ship with both official and community maps.
/// Published package metadata remains authoritative for package title,
/// thumbnail, and summary whenever it is available.
/// </summary>
[AssetType(
	Name = "Large Lad Map Manifest",
	Extension = "llmap",
	Category = "Large Lad",
	Flags = AssetTypeFlags.NoEmbedding )]
public sealed class LargeLadMapManifest : GameResource
{
	[Property, Title( "Stable Map Id" )]
	[Description(
		"Permanent lowercase identity used by catalogs and future voting, for " +
		"example my_org.school_escape. Do not use the display name." )]
	public string StableMapId { get; set; }

	[Property, Title( "Large Lad Contract Version" )]
	[Description(
		"Compatibility version required by this map. Set this to the current " +
		"version documented by Large Lad." )]
	public int ContractVersion { get; set; } =
		LargeLadMapContract.CurrentVersion;

	[Property, Title( "Published Package Ident" )]
	[Description(
		"The immutable org.ident selected when this map asset is published. Leave " +
		"empty until publishing; it must match the ident passed to MapInstance." )]
	public string PublishedPackageIdent { get; set; }

	[Property, Title( "Local/Fallback Display Name" )]
	[Description(
		"Used for local development and only as a fallback for a published map. " +
		"The published package title is authoritative when available." )]
	public string DisplayName { get; set; }

	[Property, Title( "Mapper/Author Credit" )]
	[Description(
		"Human-readable mapper credit. The publishing organization is used only " +
		"when this is empty." )]
	public string MapperCredit { get; set; }

	[Property, Title( "Local/Fallback Thumbnail" ), TextureImagePath]
	[Description(
		"Select a texture or image for local development. Never select the map " +
		"scene or a prefab: that creates a recursive asset dependency. The " +
		"published package thumbnail is authoritative when available." )]
	public string PresentationAsset { get; set; }

	[Property]
	[Description(
		"Optional short in-world backstory. When empty, a published package's " +
		"summary is used as the normalized description." )]
	public string Backstory { get; set; }

	[Property]
	[Description( "Optional short gameplay tip shown by future map presentation." )]
	public string GameplayTip { get; set; }

	[Property, Title( "Recommended Minimum Players" )]
	public int RecommendedMinimumPlayers { get; set; } =
		LargeLadGameManager.MinimumSupportedPlayerCount;

	[Property, Title( "Recommended Maximum Players" )]
	public int RecommendedMaximumPlayers { get; set; } =
		LargeLadGameManager.TargetPlayerCount;

	[Property, Title( "Approved Balance Overrides" )]
	public LargeLadMapBalanceOverrides BalanceOverrides { get; set; } = new();
}

/// <summary>
/// Required once in loaded map content. Keeping the manifest as a referenced
/// asset makes it part of a normally published map and lets validation inspect
/// the exact loaded map instead of searching a cache of previously mounted
/// packages.
/// </summary>
[Description(
	"Place exactly one in the map and assign its Large Lad map manifest. " +
	"Use this component's two buttons to validate the open content scene; the " +
	"persistent game shell validates the same map rules before Ready." )]
public sealed class LargeLadMapProfile : Component
{
	[Property, Title( "Large Lad Map Manifest" )]
	[Description(
		"Assign the .llmap asset for this content scene. Published Package Ident " +
		"may remain empty during local development." )]
	public LargeLadMapManifest Manifest { get; set; }

	protected override void OnValidate()
	{
		if ( Manifest is null )
		{
			Log.Warning(
				$"{GameObject.Name}: assign this map's .llmap manifest, then use " +
				"Validate Large Lad Map." );
		}
	}

	[Button( "Validate Large Lad Map" )]
	public void ValidateLargeLadMap()
	{
		LargeLadMapValidator.ValidateScene(
			Scene,
			rebuildSpawnPreview: false )
			.LogMapperSummary();
	}

	[Button( "Rebuild Spawns and Validate" )]
	public void RebuildSpawnsAndValidate()
	{
		LargeLadMapValidator.ValidateScene(
			Scene,
			rebuildSpawnPreview: true )
			.LogMapperSummary();
	}
}

/// <summary>
/// First-party curation entry. Nothing in a mapper-authored manifest can set
/// or imitate this membership.
/// </summary>
public sealed class LargeLadOfficialMapEntry
{
	[Property]
	[Description(
		"The local scene path or published package ident supplied to MapInstance." )]
	public string MapInstanceIdentifier { get; set; }

	[Property]
	public LargeLadMapManifest Manifest { get; set; }
}

/// <summary>
/// First-party official-map membership owned by the Large Lad game package.
/// Both official and community entries still normalize through the same rules.
/// </summary>
[AssetType(
	Name = "Large Lad Official Map Catalog",
	Extension = "llmaps",
	Category = "Large Lad",
	Flags = AssetTypeFlags.NoEmbedding )]
public sealed class LargeLadOfficialMapCatalog : GameResource
{
	[Property]
	public List<LargeLadOfficialMapEntry> Entries { get; set; } = new();
}

/// <summary>
/// Package-authored presentation facts used by the pure normalization rules.
/// Discovery/fetching stays outside the descriptor itself.
/// </summary>
public sealed class LargeLadMapPackageMetadata
{
	public string PackageIdent { get; init; }
	public string DisplayName { get; init; }
	public string PublisherCredit { get; init; }
	public string PresentationAsset { get; init; }
	public string Description { get; init; }
}

/// <summary>
/// Validated, immutable copy of one map's approved balance layer. Resource
/// objects are never retained here, so hotloads or later map changes cannot
/// mutate the active values or compound a previous resolution.
/// </summary>
public sealed class LargeLadResolvedMapBalance
{
	internal LargeLadResolvedMapBalance(
		float? survivalDurationSeconds,
		float? skinnyProgressionBarricadeMaximumHealthMultiplier,
		float? largeLadMaximumHealthMultiplier,
		float? hunterEscalationMultiplier )
	{
		SurvivalDurationSeconds = survivalDurationSeconds;
		SkinnyProgressionBarricadeMaximumHealthMultiplier =
			skinnyProgressionBarricadeMaximumHealthMultiplier;
		LargeLadMaximumHealthMultiplier = largeLadMaximumHealthMultiplier;
		HunterEscalationMultiplier = hunterEscalationMultiplier;
	}

	public float? SurvivalDurationSeconds { get; }
	public float? SkinnyProgressionBarricadeMaximumHealthMultiplier { get; }
	public float? LargeLadMaximumHealthMultiplier { get; }
	public float? HunterEscalationMultiplier { get; }

	public float ResolveSurvivalDuration( float gameDefault )
	{
		return SurvivalDurationSeconds ?? gameDefault;
	}

	public float ResolveSkinnyProgressionBarricadeMultiplier()
	{
		return SkinnyProgressionBarricadeMaximumHealthMultiplier ?? 1.0f;
	}

	public float ResolveLargeLadMaximumHealthMultiplier()
	{
		return LargeLadMaximumHealthMultiplier ?? 1.0f;
	}

	public float ResolveHunterEscalationMultiplier()
	{
		return HunterEscalationMultiplier ?? 1.0f;
	}
}

/// <summary>
/// The one normalized representation consumed by runtime and future lobby or
/// voting systems, regardless of where its metadata originated.
/// </summary>
public sealed class LargeLadMapDescriptor
{
	internal LargeLadMapDescriptor(
		string stableMapId,
		string displayName,
		string mapperCredit,
		string presentationAsset,
		string description,
		string gameplayTip,
		int recommendedMinimumPlayers,
		int recommendedMaximumPlayers,
		int contractVersion,
		string mapInstanceIdentifier,
		bool isOfficiallyCurated,
		LargeLadResolvedMapBalance balance )
	{
		StableMapId = stableMapId;
		DisplayName = displayName;
		MapperCredit = mapperCredit;
		PresentationAsset = presentationAsset;
		Description = description;
		GameplayTip = gameplayTip;
		RecommendedMinimumPlayers = recommendedMinimumPlayers;
		RecommendedMaximumPlayers = recommendedMaximumPlayers;
		ContractVersion = contractVersion;
		MapInstanceIdentifier = mapInstanceIdentifier;
		IsOfficiallyCurated = isOfficiallyCurated;
		Balance = balance;
	}

	public string StableMapId { get; }
	public string DisplayName { get; }
	public string MapperCredit { get; }
	public string PresentationAsset { get; }
	public string Description { get; }
	public string GameplayTip { get; }
	public int RecommendedMinimumPlayers { get; }
	public int RecommendedMaximumPlayers { get; }
	public int ContractVersion { get; }
	public string MapInstanceIdentifier { get; }
	public bool IsOfficiallyCurated { get; }
	public LargeLadResolvedMapBalance Balance { get; }
}

/// <summary>
/// Pure compatibility, validation, curation, and normalization rules. Package
/// discovery is deliberately not part of this API.
/// </summary>
public static class LargeLadMapCatalog
{
	private const string LocalEditorMapIdentifier =
		"scenes/large_lad_local_validation.scene";

	public static bool TryResolveOfficial(
		LargeLadOfficialMapCatalog catalog,
		string stableMapIdOrMapInstanceIdentifier,
		LargeLadMapPackageMetadata packageMetadata,
		out LargeLadMapDescriptor descriptor,
		out IReadOnlyList<string> issues )
	{
		descriptor = null;
		var failures = new List<string>();
		var matches = catalog?.Entries?
			.Where( entry => EntryMatchesLookup(
				entry,
				stableMapIdOrMapInstanceIdentifier ) )
			.ToList() ?? new List<LargeLadOfficialMapEntry>();

		if ( matches.Count != 1 )
		{
			failures.Add(
				matches.Count == 0
					? $"Official map '{Display( stableMapIdOrMapInstanceIdentifier )}' " +
						"is not present in Large Lad's curated catalog."
					: $"Official map lookup '{Display( stableMapIdOrMapInstanceIdentifier )}' " +
						"matches more than one curated catalog entry." );
			issues = failures;
			return false;
		}

		var entry = matches[0];
		return TryResolve(
			entry.Manifest,
			entry.MapInstanceIdentifier,
			packageMetadata,
			entry,
			out descriptor,
			out issues );
	}

	public static bool TryResolveCommunity(
		LargeLadMapManifest manifest,
		string mapInstanceIdentifier,
		LargeLadMapPackageMetadata packageMetadata,
		out LargeLadMapDescriptor descriptor,
		out IReadOnlyList<string> issues )
	{
		return TryResolve(
			manifest,
			mapInstanceIdentifier,
			packageMetadata,
			officialEntry: null,
			out descriptor,
			out issues );
	}

	public static bool TryResolveLoadedMap(
		LargeLadMapManifest manifest,
		string mapInstanceIdentifier,
		LargeLadMapPackageMetadata packageMetadata,
		LargeLadOfficialMapCatalog officialCatalog,
		out LargeLadMapDescriptor descriptor,
		out IReadOnlyList<string> issues )
	{
		var curatedMatches = officialCatalog?.Entries?
			.Where( entry => EntryMatchesLoadedMap(
				entry,
				manifest,
				mapInstanceIdentifier ) )
			.ToList() ?? new List<LargeLadOfficialMapEntry>();

		if ( curatedMatches.Count > 1 )
		{
			descriptor = null;
			issues = new[]
			{
				$"Map '{Display( mapInstanceIdentifier )}' matches more than one " +
				"Large Lad official-catalog entry; remove the duplicate curation."
			};
			return false;
		}

		return TryResolve(
			manifest,
			mapInstanceIdentifier,
			packageMetadata,
			curatedMatches.SingleOrDefault(),
			out descriptor,
			out issues );
	}

	public static async Task<LargeLadMapPackageMetadata>
		FetchPublishedPackageMetadata( string mapInstanceIdentifier )
	{
		if ( IsLocalMapIdentifier( mapInstanceIdentifier ) )
			return null;

		try
		{
			var package = await Package.FetchAsync(
				mapInstanceIdentifier,
				partial: false );

			if ( package is null )
				return null;

			return new LargeLadMapPackageMetadata
			{
				PackageIdent = package.FullIdent,
				DisplayName = package.Title,
				PublisherCredit = package.Org?.Title,
				PresentationAsset = FirstNonEmpty(
					package.ThumbWide,
					package.Thumb ),
				Description = FirstNonEmpty(
					package.Summary,
					package.Description )
			};
		}
		catch ( Exception exception )
		{
			Log.Warning(
				$"Could not fetch presentation metadata for published map " +
				$"'{Display( mapInstanceIdentifier )}': {exception.Message}" );
			return null;
		}
	}

	/// <summary>
	/// Validates every manifest rule available while editing an unpublished
	/// content scene. This deliberately uses the same resolver as runtime but a
	/// local scene identifier, so Published Package Ident is not required.
	/// </summary>
	public static IReadOnlyList<string> GetLocalManifestValidationIssues(
		LargeLadMapManifest manifest )
	{
		TryResolve(
			manifest,
			LocalEditorMapIdentifier,
			packageMetadata: null,
			officialEntry: null,
			out _,
			out var issues );
		return issues;
	}

	public static IReadOnlyList<string> GetBalanceValidationIssues(
		LargeLadMapBalanceOverrides overrides )
	{
		var issues = new List<string>();

		if ( overrides is null )
			return issues;

		ValidateOptionalPositive(
			issues,
			"survival duration",
			overrides.SurvivalDurationSeconds );
		ValidateOptionalPositive(
			issues,
			"Skinny Progression barricade maximum-health multiplier",
			overrides.SkinnyProgressionBarricadeMaximumHealthMultiplier );
		ValidateOptionalPositive(
			issues,
			"Large Lad maximum-health multiplier",
			overrides.LargeLadMaximumHealthMultiplier );

		if ( overrides.HunterEscalationMultiplier != 0.0f &&
			(!float.IsFinite( overrides.HunterEscalationMultiplier ) ||
				overrides.HunterEscalationMultiplier < 1.0f) )
		{
			issues.Add(
				"late-round Hunter escalation multiplier must be zero (use the " +
				"game default) or finite and at least one." );
		}

		return issues;
	}

	public static float ComposeHunterMaximumMultiplier(
		float gameMaximumMultiplier,
		LargeLadResolvedMapBalance mapBalance )
	{
		var gameMaximum =
			LargeLadHunterMovementEscalationRules.IsValidMaximumMultiplier(
				gameMaximumMultiplier )
				? gameMaximumMultiplier
				: 1.0f;
		var mapMultiplier =
			mapBalance?.ResolveHunterEscalationMultiplier() ?? 1.0f;
		return gameMaximum * mapMultiplier;
	}

	private static bool TryResolve(
		LargeLadMapManifest manifest,
		string requestedMapInstanceIdentifier,
		LargeLadMapPackageMetadata packageMetadata,
		LargeLadOfficialMapEntry officialEntry,
		out LargeLadMapDescriptor descriptor,
		out IReadOnlyList<string> issues )
	{
		descriptor = null;
		var failures = new List<string>();
		var requestedIdentifier = Clean( requestedMapInstanceIdentifier );
		var initialMapLabel = FirstNonEmpty(
			packageMetadata?.DisplayName,
			manifest?.DisplayName,
			requestedIdentifier,
			"unknown map" );

		if ( manifest is null )
		{
			failures.Add(
				$"Map '{initialMapLabel}' is missing its required Large Lad map " +
				"manifest. Create a Large Lad Map Manifest and assign it to the " +
				"map's LargeLadMapProfile." );
			issues = failures;
			return false;
		}

		var stableMapId = Clean( manifest.StableMapId );
		var displayName = FirstNonEmpty(
			packageMetadata?.DisplayName,
			manifest.DisplayName );
		var mapLabel = FirstNonEmpty(
			displayName,
			stableMapId,
			requestedIdentifier,
			"unknown map" );
		var expectedIdentifier = IsLocalMapIdentifier( requestedIdentifier )
			? requestedIdentifier
			: Clean( manifest.PublishedPackageIdent );

		if ( string.IsNullOrWhiteSpace( requestedIdentifier ) )
		{
			failures.Add(
				$"Map '{mapLabel}' has no MapInstance identifier." );
		}
		else if ( string.IsNullOrWhiteSpace( expectedIdentifier ) )
		{
			failures.Add(
				$"Map '{mapLabel}' must set Published Package Ident to " +
				$"'{requestedIdentifier}'." );
		}
		else if ( !IdentifiersEqual(
			expectedIdentifier,
			requestedIdentifier ) )
		{
			failures.Add(
				$"Map '{mapLabel}' declares MapInstance identifier " +
				$"'{expectedIdentifier}', but Large Lad loaded " +
				$"'{requestedIdentifier}'. Update the corresponding manifest field." );
		}

		if ( packageMetadata is not null &&
			!string.IsNullOrWhiteSpace( packageMetadata.PackageIdent ) &&
			!IdentifiersEqual(
				packageMetadata.PackageIdent,
				requestedIdentifier ) )
		{
			failures.Add(
				$"Map '{mapLabel}' resolved package metadata for " +
				$"'{packageMetadata.PackageIdent}' while MapInstance loaded " +
				$"'{requestedIdentifier}'." );
		}

		if ( !IsValidStableMapId( stableMapId ) )
		{
			failures.Add(
				$"Map '{mapLabel}' needs a stable lowercase map id containing " +
				"only letters, digits, '.', '_' or '-'." );
		}

		if ( string.IsNullOrWhiteSpace( displayName ) )
		{
			failures.Add(
				$"Map '{mapLabel}' needs a display name in its publishing metadata " +
				"or manifest fallback." );
		}

		var mapperCredit = FirstNonEmpty(
			manifest.MapperCredit,
			packageMetadata?.PublisherCredit );

		if ( string.IsNullOrWhiteSpace( mapperCredit ) )
		{
			failures.Add(
				$"Map '{mapLabel}' needs mapper/author credit in its manifest or " +
				"published organization metadata." );
		}

		var presentationAsset = FirstNonEmpty(
			packageMetadata?.PresentationAsset,
			manifest.PresentationAsset );
		var manifestPresentationAsset = Clean( manifest.PresentationAsset );

		if ( string.IsNullOrWhiteSpace( presentationAsset ) )
		{
			failures.Add(
				$"Map '{mapLabel}' needs a package thumbnail or manifest " +
				"presentation asset." );
		}

		if ( IsRecursivePresentationAsset( manifestPresentationAsset ) )
		{
			failures.Add(
				$"Map '{mapLabel}' uses '{manifestPresentationAsset}' as its local " +
				"thumbnail. Select a texture or image; a scene, map, manifest, " +
				"catalog, or prefab creates a recursive content dependency." );
		}
		else if ( !string.IsNullOrWhiteSpace( manifestPresentationAsset ) &&
			!IsValidLocalPresentationAsset( manifestPresentationAsset ) )
		{
			failures.Add(
				$"Map '{mapLabel}' uses '{manifestPresentationAsset}' as its local " +
				"thumbnail. Select a project-relative texture or image path " +
				"(.vtex, .png, .jpg, .jpeg, .tga, .psd, .hdr, .exr, .webp, or .svg)." );
		}

		if ( !LargeLadMapContract.IsSupported( manifest.ContractVersion ) )
		{
			failures.Add(
				$"Map '{mapLabel}' declares Large Lad map-contract version " +
				$"{manifest.ContractVersion}, but this Large Lad build supports " +
				$"version {LargeLadMapContract.CurrentVersion}. Update the manifest's " +
				"Large Lad Contract Version and revalidate the map." );
		}

		if ( manifest.RecommendedMinimumPlayers <
				LargeLadGameManager.MinimumSupportedPlayerCount ||
			manifest.RecommendedMaximumPlayers >
				LargeLadGameManager.TargetPlayerCount ||
			manifest.RecommendedMinimumPlayers >
				manifest.RecommendedMaximumPlayers )
		{
			failures.Add(
				$"Map '{mapLabel}' has recommended player range " +
				$"{manifest.RecommendedMinimumPlayers}-" +
				$"{manifest.RecommendedMaximumPlayers}; use an ordered range within " +
				$"Large Lad's {LargeLadGameManager.MinimumSupportedPlayerCount}-" +
				$"{LargeLadGameManager.TargetPlayerCount} player contract." );
		}

		foreach ( var balanceIssue in GetBalanceValidationIssues(
			manifest.BalanceOverrides ) )
		{
			failures.Add( $"Map '{mapLabel}' {balanceIssue}" );
		}

		if ( officialEntry is not null )
		{
			if ( officialEntry.Manifest is null ||
				!IdentifiersEqual(
					officialEntry.Manifest.StableMapId,
					stableMapId ) ||
				!IdentifiersEqual(
					officialEntry.MapInstanceIdentifier,
					requestedIdentifier ) )
			{
				failures.Add(
					$"Map '{mapLabel}' does not match its first-party official " +
					"catalog entry." );
			}
		}

		if ( failures.Count > 0 )
		{
			issues = failures;
			return false;
		}

		var sourceOverrides = manifest.BalanceOverrides ?? new();
		var balance = new LargeLadResolvedMapBalance(
			Optional( sourceOverrides.SurvivalDurationSeconds ),
			Optional(
				sourceOverrides
					.SkinnyProgressionBarricadeMaximumHealthMultiplier ),
			Optional( sourceOverrides.LargeLadMaximumHealthMultiplier ),
			Optional( sourceOverrides.HunterEscalationMultiplier ) );

		descriptor = new LargeLadMapDescriptor(
			stableMapId,
			Clean( displayName ),
			Clean( mapperCredit ),
			Clean( presentationAsset ),
			FirstNonEmpty(
				packageMetadata?.Description,
				manifest.Backstory ),
			Clean( manifest.GameplayTip ),
			manifest.RecommendedMinimumPlayers,
			manifest.RecommendedMaximumPlayers,
			manifest.ContractVersion,
			requestedIdentifier,
			isOfficiallyCurated: officialEntry is not null,
			balance );
		issues = Array.Empty<string>();
		return true;
	}

	private static bool EntryMatchesLookup(
		LargeLadOfficialMapEntry entry,
		string lookup )
	{
		if ( entry?.Manifest is null )
			return false;

		return IdentifiersEqual( entry.Manifest.StableMapId, lookup ) ||
			IdentifiersEqual( entry.MapInstanceIdentifier, lookup );
	}

	private static bool EntryMatchesLoadedMap(
		LargeLadOfficialMapEntry entry,
		LargeLadMapManifest manifest,
		string mapInstanceIdentifier )
	{
		if ( entry?.Manifest is null || manifest is null )
			return false;

		return IdentifiersEqual(
			entry.MapInstanceIdentifier,
			mapInstanceIdentifier ) &&
			IdentifiersEqual(
				entry.Manifest.StableMapId,
				manifest.StableMapId );
	}

	private static void ValidateOptionalPositive(
		List<string> issues,
		string fieldName,
		float value )
	{
		if ( value != 0.0f && (!float.IsFinite( value ) || value <= 0.0f) )
		{
			issues.Add(
				$"{fieldName} must be zero (use the game default) or finite and " +
				"positive." );
		}
	}

	private static float? Optional( float value )
	{
		return value == 0.0f ? null : value;
	}

	private static bool IsValidStableMapId( string mapId )
	{
		if ( string.IsNullOrWhiteSpace( mapId ) ||
			!char.IsLetterOrDigit( mapId[0] ) )
		{
			return false;
		}

		foreach ( var character in mapId )
		{
			if ( char.IsLower( character ) ||
				char.IsDigit( character ) ||
				character is '.' or '_' or '-' )
			{
				continue;
			}

			return false;
		}

		return true;
	}

	private static bool IsLocalMapIdentifier( string identifier )
	{
		return identifier?.EndsWith(
			".scene",
			StringComparison.OrdinalIgnoreCase ) == true ||
			identifier?.EndsWith(
				".vmap",
				StringComparison.OrdinalIgnoreCase ) == true;
	}

	private static bool IsRecursivePresentationAsset( string assetPath )
	{
		return assetPath.EndsWith(
			".scene",
			StringComparison.OrdinalIgnoreCase ) ||
			assetPath.EndsWith(
				".vmap",
				StringComparison.OrdinalIgnoreCase ) ||
			assetPath.EndsWith(
				".prefab",
				StringComparison.OrdinalIgnoreCase ) ||
			assetPath.EndsWith(
				".llmap",
				StringComparison.OrdinalIgnoreCase ) ||
			assetPath.EndsWith(
				".llmaps",
				StringComparison.OrdinalIgnoreCase );
	}

	private static bool IsValidLocalPresentationAsset( string assetPath )
	{
		var clean = Clean( assetPath );
		var normalized = clean.Replace( '\\', '/' );

		if ( string.IsNullOrWhiteSpace( clean ) ||
			normalized.StartsWith( "/" ) ||
			normalized.Contains( ':' ) ||
			normalized.Split( '/' ).Any( part => part is "." or ".." ) )
		{
			return false;
		}

		return clean.EndsWith( ".vtex", StringComparison.OrdinalIgnoreCase ) ||
			clean.EndsWith( ".png", StringComparison.OrdinalIgnoreCase ) ||
			clean.EndsWith( ".jpg", StringComparison.OrdinalIgnoreCase ) ||
			clean.EndsWith( ".jpeg", StringComparison.OrdinalIgnoreCase ) ||
			clean.EndsWith( ".tga", StringComparison.OrdinalIgnoreCase ) ||
			clean.EndsWith( ".psd", StringComparison.OrdinalIgnoreCase ) ||
			clean.EndsWith( ".hdr", StringComparison.OrdinalIgnoreCase ) ||
			clean.EndsWith( ".exr", StringComparison.OrdinalIgnoreCase ) ||
			clean.EndsWith( ".webp", StringComparison.OrdinalIgnoreCase ) ||
			clean.EndsWith( ".svg", StringComparison.OrdinalIgnoreCase );
	}

	private static bool IdentifiersEqual( string left, string right )
	{
		return string.Equals(
			Clean( left ),
			Clean( right ),
			StringComparison.OrdinalIgnoreCase );
	}

	private static string FirstNonEmpty( params string[] values )
	{
		return values?.FirstOrDefault(
			value => !string.IsNullOrWhiteSpace( value ) )?.Trim() ??
			string.Empty;
	}

	private static string Clean( string value )
	{
		return value?.Trim() ?? string.Empty;
	}

	private static string Display( string value )
	{
		return string.IsNullOrWhiteSpace( value ) ? "unknown" : value.Trim();
	}
}
