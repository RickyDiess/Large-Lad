/// <summary>
/// Coarse hit regions retained by the authoritative damage envelope. Firearm
/// traces classify exactly one region; non-firearm damage leaves this as None.
/// </summary>
public enum LargeLadHitRegion
{
	None,
	Body,
	Head
}

/// <summary>
/// Stable cause metadata intended for a later killfeed. The source weapon in
/// the damage context supplies the specific firearm name.
/// </summary>
public enum LargeLadKillfeedCause
{
	Unknown,
	Firearm,
	FirearmHeadshot,
	Melee,
	Eat,
	Environment
}

/// <summary>
/// Deterministic firearm hit rules shared by the host damage path and tests.
/// Dodgeballs are deliberately absent: only catalogued firearms can qualify.
/// </summary>
public static class LargeLadFirearmHitRules
{
	public const string HeadHitboxTag = "head";

	public static LargeLadHitRegion ClassifyHitRegion(
		string hitboxBoneName,
		bool hasHeadHitboxTag )
	{
		return hasHeadHitboxTag || IsHeadBoneName( hitboxBoneName )
			? LargeLadHitRegion.Head
			: LargeLadHitRegion.Body;
	}

	public static bool IsFirearmHeadshot(
		LargeLadWeaponId sourceWeapon,
		LargeLadDamageType damageType,
		LargeLadHitRegion hitRegion )
	{
		return damageType == LargeLadDamageType.Firearm &&
			hitRegion == LargeLadHitRegion.Head &&
			LargeLadWeaponCatalog.IsFirearm( sourceWeapon );
	}

	public static bool IsUniversalLethalMinionHeadshot(
		LargeLadRole victimRole,
		bool isLiving,
		LargeLadWeaponId sourceWeapon,
		LargeLadDamageType damageType,
		LargeLadHitRegion hitRegion )
	{
		return isLiving &&
			victimRole == LargeLadRole.Minion &&
			IsFirearmHeadshot( sourceWeapon, damageType, hitRegion );
	}

	/// <summary>
	/// Resolves the final incoming amount after ordinary role modifiers. A valid
	/// Minion firearm headshot consumes all current health; every other hit keeps
	/// the weapon-defined ordinary amount.
	/// </summary>
	public static float ResolveIncomingDamage(
		LargeLadRole victimRole,
		bool isLiving,
		LargeLadWeaponId sourceWeapon,
		LargeLadDamageType damageType,
		LargeLadHitRegion hitRegion,
		float currentHealth,
		float ordinaryIncomingDamage )
	{
		var safeOrdinaryDamage = float.IsFinite( ordinaryIncomingDamage )
			? System.MathF.Max( 0.0f, ordinaryIncomingDamage )
			: 0.0f;

		if ( !IsUniversalLethalMinionHeadshot(
			victimRole,
			isLiving,
			sourceWeapon,
			damageType,
			hitRegion ) )
		{
			return safeOrdinaryDamage;
		}

		return float.IsFinite( currentHealth )
			? System.MathF.Max( 0.0f, currentHealth )
			: 0.0f;
	}

	public static LargeLadKillfeedCause GetKillfeedCause(
		LargeLadWeaponId sourceWeapon,
		LargeLadDamageType damageType,
		LargeLadHitRegion hitRegion )
	{
		return damageType switch
		{
			LargeLadDamageType.Firearm
				when IsFirearmHeadshot(
					sourceWeapon,
					damageType,
					hitRegion ) =>
					LargeLadKillfeedCause.FirearmHeadshot,
			LargeLadDamageType.Firearm => LargeLadKillfeedCause.Firearm,
			LargeLadDamageType.Melee => LargeLadKillfeedCause.Melee,
			LargeLadDamageType.Eat => LargeLadKillfeedCause.Eat,
			LargeLadDamageType.Environment => LargeLadKillfeedCause.Environment,
			_ => LargeLadKillfeedCause.Unknown
		};
	}

	private static bool IsHeadBoneName( string boneName )
	{
		if ( string.IsNullOrWhiteSpace( boneName ) )
			return false;

		var normalized = boneName.Trim();
		return normalized.Equals(
			"head",
			System.StringComparison.OrdinalIgnoreCase ) ||
			normalized.EndsWith(
				"_head",
				System.StringComparison.OrdinalIgnoreCase ) ||
			normalized.StartsWith(
				"head_",
				System.StringComparison.OrdinalIgnoreCase );
	}
}

/// <summary>
/// Consumes owner shot sequences once on the host, before tracing or damage.
/// This makes a replayed request unable to create a second hit classification,
/// audiovisual confirmation, or lethal event.
/// </summary>
public sealed class LargeLadFirearmShotRequestGate
{
	public int LastConsumedSequence { get; private set; }

	public bool TryConsume( int ownerShotSequence )
	{
		if ( ownerShotSequence <= LastConsumedSequence )
			return false;

		LastConsumedSequence = ownerShotSequence;
		return true;
	}
}
