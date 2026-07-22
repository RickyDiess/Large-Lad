using Sandbox;

public enum LargeLadDamageType
{
	Firearm,
	Melee,
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
	public LargeLadDamageType DamageType { get; set; }
	public float BaseDamage { get; set; }
	public float AppliedDamage { get; set; }

	public LargeLadDamageContext WithAppliedDamage( float amount )
	{
		AppliedDamage = amount;
		return this;
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
