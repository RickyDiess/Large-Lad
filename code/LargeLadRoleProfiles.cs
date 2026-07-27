using Sandbox;

/// <summary>
/// Every gameplay value that varies by player role.
/// </summary>
public sealed class LargeLadRoleProfile
{
	[Property]
	public float WalkSpeed { get; set; }

	[Property]
	public float RunSpeed { get; set; }

	[Property]
	public float MaximumHealth { get; set; }

	[Property]
	public float IncomingDamageMultiplier { get; set; }

	[Property]
	public Color BodyTint { get; set; }

	[Property]
	public Vector3 BodyVisualScale { get; set; }

	[Property]
	public float MeleeRange { get; set; }

	[Property]
	public float MeleeCooldown { get; set; }

	[Property]
	public float MeleeDamage { get; set; }

	[Property]
	public bool MeleeAimAssist { get; set; }
}

/// <summary>
/// Authoritative serialized balance source for every playable role.
/// </summary>
[AssetType(
	Name = "Large Lad Role Profiles",
	Extension = "llroles",
	Category = "Large Lad",
	Flags = AssetTypeFlags.NoEmbedding )]
public sealed class LargeLadRoleProfiles : GameResource
{
	[Property]
	public LargeLadRoleProfile SkinnyKid { get; set; }

	[Property]
	public LargeLadRoleProfile LargeLad { get; set; }

	[Property]
	public LargeLadRoleProfile Minion { get; set; }

	public bool TryGetProfile(
		LargeLadRole role,
		out LargeLadRoleProfile profile )
	{
		// Unassigned players historically use the Skinny Kid baseline until the
		// host gives them their round role.
		profile = LargeLadGameplayRules.SelectRoleProfile(
			role,
			SkinnyKid,
			LargeLad,
			Minion );

		return profile is not null;
	}

	public IReadOnlyList<string> GetValidationWarnings()
	{
		var warnings = new List<string>();

		ValidateProfile( warnings, "Skinny Kid", SkinnyKid );
		ValidateProfile( warnings, "Large Lad", LargeLad );
		ValidateProfile( warnings, "Minion", Minion );

		return warnings;
	}

	private static void ValidateProfile(
		List<string> warnings,
		string roleName,
		LargeLadRoleProfile profile )
	{
		if ( profile is null )
		{
			warnings.Add( $"{roleName} profile is missing." );
			return;
		}

		ValidatePositive( warnings, roleName, "walk speed", profile.WalkSpeed );
		ValidatePositive( warnings, roleName, "run speed", profile.RunSpeed );
		ValidatePositive(
			warnings,
			roleName,
			"maximum health",
			profile.MaximumHealth );

		if ( !float.IsFinite( profile.IncomingDamageMultiplier ) ||
			profile.IncomingDamageMultiplier < 0.0f )
		{
			warnings.Add(
				$"{roleName} incoming damage multiplier must be finite and non-negative." );
		}

		if ( !IsPositive( profile.BodyVisualScale.x ) ||
			!IsPositive( profile.BodyVisualScale.y ) ||
			!IsPositive( profile.BodyVisualScale.z ) )
		{
			warnings.Add(
				$"{roleName} body visual scale must be finite and positive on every axis." );
		}

		ValidatePositive(
			warnings,
			roleName,
			"melee range",
			profile.MeleeRange );
		ValidatePositive(
			warnings,
			roleName,
			"melee cooldown",
			profile.MeleeCooldown );
		ValidatePositive(
			warnings,
			roleName,
			"melee damage",
			profile.MeleeDamage );
	}

	private static void ValidatePositive(
		List<string> warnings,
		string roleName,
		string fieldName,
		float value )
	{
		if ( !IsPositive( value ) )
		{
			warnings.Add(
				$"{roleName} {fieldName} must be finite and positive." );
		}
	}

	private static bool IsPositive( float value )
	{
		return float.IsFinite( value ) && value > 0.0f;
	}
}
