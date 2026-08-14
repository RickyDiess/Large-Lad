using Sandbox;

public enum LargeLadShotResult
{
	AcceptedMiss,
	PlayerHit,
	PlayerHeadshot,
	BarricadeHit
}

/// <summary>
/// Temporary serialization-only shell retained because existing player prefabs
/// reference this historical component type. It no longer owns firearm input,
/// selection, ammunition, reload state, shot RPCs, or damage. Production
/// firearms run exclusively through <see cref="LargeLadFirearm"/> and
/// <see cref="LargeLadNativeInventory"/>.
/// </summary>
public sealed class LargeLadPrototypeWeapon : Component
{
	[Property, Title( "Retired Firearm Debug" )]
	public bool EnableFireDebug { get; set; }

	public bool HasConfirmedHitmarker => false;
	public LargeLadShotResult LastShotResult =>
		LargeLadShotResult.AcceptedMiss;
	public int PresentationShotSequence => 0;
	public LargeLadWeaponId PresentationShotWeapon =>
		LargeLadWeaponId.None;
	public int PresentationEmptySequence => 0;
	public LargeLadWeaponId PresentationEmptyWeapon =>
		LargeLadWeaponId.None;
}
