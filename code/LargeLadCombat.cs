using Sandbox;

public enum LargeLadDamageType
{
	Firearm,
	Melee,
	Eat,
	Environment
}

/// <summary>
/// The single damage envelope used by players and authored map objects.
/// AppliedDamage is filled in by the receiver after its own modifiers.
/// </summary>
public struct LargeLadDamageContext
{
	public GameObject Attacker { get; set; }
	public LargeLadRole AttackerRole { get; set; }
	public LargeLadWeaponId SourceWeapon { get; set; }
	public int SourceShotSequence { get; set; }
	public LargeLadDamageType DamageType { get; set; }
	public bool IsExecution { get; set; }
	public LargeLadHitRegion HitRegion { get; set; }
	public float BaseDamage { get; set; }
	public float AppliedDamage { get; set; }

	public bool IsExplicitExecution =>
		IsExecution &&
		DamageType is LargeLadDamageType.Eat or
			LargeLadDamageType.Environment;

	public bool IsFirearmHeadshot =>
		LargeLadFirearmHitRules.IsFirearmHeadshot(
			SourceWeapon,
			DamageType,
			HitRegion );

	public LargeLadKillfeedCause KillfeedCause =>
		LargeLadFirearmHitRules.GetKillfeedCause(
			SourceWeapon,
			DamageType,
			HitRegion );

	public LargeLadDamageContext WithAppliedDamage( float amount )
	{
		AppliedDamage = amount;
		return this;
	}
}

/// <summary>
/// Deterministic composition of ordinary incoming damage and the two explicit
/// execution paths. The execution flag is intentionally meaningful only for
/// Eat and Environment damage, so no other source can opt into lethal damage.
/// </summary>
public static class LargeLadDamageRules
{
	public static float ResolveIncomingDamage(
		LargeLadRole victimRole,
		bool isLiving,
		bool isLastSkinnyKid,
		LargeLadWeaponId sourceWeapon,
		LargeLadDamageType damageType,
		LargeLadHitRegion hitRegion,
		bool requestsExecution,
		float currentHealth,
		float baseDamage,
		float incomingDamageMultiplier )
	{
		var isExplicitExecution = requestsExecution &&
			damageType is LargeLadDamageType.Eat or
				LargeLadDamageType.Environment;
		var ordinaryIncomingDamage =
			baseDamage * System.MathF.Max(
				0.0f,
				incomingDamageMultiplier );
		var amount = isExplicitExecution
			? SafeDamage( currentHealth )
			: LargeLadFirearmHitRules.ResolveIncomingDamage(
				victimRole,
				isLiving,
				sourceWeapon,
				damageType,
				hitRegion,
				currentHealth,
				ordinaryIncomingDamage );

		return LargeLadSkinnyKidSurvivabilityRules
			.ApplyLastSkinnyKidDamageReduction(
				victimRole,
				isLastSkinnyKid,
				damageType,
				isExplicitExecution,
				amount );
	}

	private static float SafeDamage( float amount )
	{
		return float.IsFinite( amount )
			? System.MathF.Max( 0.0f, amount )
			: 0.0f;
	}
}

public interface ILargeLadDamageable
{
	bool TryApplyDamage(
		LargeLadDamageContext damage,
		out LargeLadDamageContext appliedDamage );
}

/// <summary>
/// Implemented by authored map state that must return to its original state
/// immediately before a new round begins.
/// </summary>
public interface ILargeLadRoundResettable
{
	void ResetForRound();
}
