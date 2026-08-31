using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox;

[TestClass]
public sealed class LargeLadNativeWeaponRulesTests
{
	[TestMethod]
	public void SlotLayout_UsesFourNativeBuckets()
	{
		Assert.AreEqual( 0, LargeLadNativeWeaponRules.MeleeSlot );
		Assert.AreEqual( 1, LargeLadNativeWeaponRules.CoreFirearmSlot );
		Assert.AreEqual( 2, LargeLadNativeWeaponRules.ExclusiveFirearmSlot );
		Assert.AreEqual( 3, LargeLadNativeWeaponRules.UtilitySlot );
		Assert.AreEqual( 4, LargeLadNativeWeaponRules.SlotCount );
	}

	[TestMethod]
	public void CoreFirearmPolicy_AllowsDifferentWeaponsButRejectsDuplicates()
	{
		var fullCoreRoster = new[]
		{
			LargeLadWeaponId.Pistol,
			LargeLadWeaponId.Smg,
			LargeLadWeaponId.Shotgun,
			LargeLadWeaponId.Rifle
		};

		foreach ( var weapon in fullCoreRoster )
		{
			Assert.IsFalse( LargeLadNativeWeaponRules.CanAddCoreFirearm(
				weapon,
				fullCoreRoster ),
				$"Duplicate {weapon} should be rejected." );
		}

		Assert.IsTrue( LargeLadNativeWeaponRules.CanAddCoreFirearm(
			LargeLadWeaponId.Smg,
			new[] { LargeLadWeaponId.Pistol } ) );
	}

	[TestMethod]
	public void ShotgunClaimEnvelope_AcceptsEightPelletsButNeverMore()
	{
		Assert.IsTrue( LargeLadNativeWeaponRules.IsValidClaimEnvelope(
			sequence: 1,
			lastSequence: 0,
			damage: 15.0f,
			expectedDamage: 15.0f,
			force: 3000.0f,
			expectedForce: 3000.0f,
			claimedPellets: 8,
			maximumPellets: 8 ) );
		Assert.IsFalse( LargeLadNativeWeaponRules.IsValidClaimEnvelope(
			sequence: 1,
			lastSequence: 0,
			damage: 15.0f,
			expectedDamage: 15.0f,
			force: 3000.0f,
			expectedForce: 3000.0f,
			claimedPellets: 9,
			maximumPellets: 8 ) );
	}

	[TestMethod]
	public void PelletRangeValidation_RetainsSmallSixteenUnitTolerance()
	{
		Assert.AreEqual( 16.0f, LargeLadNativeWeaponRules.ClaimRangeTolerance );
		Assert.IsTrue( LargeLadNativeWeaponRules.IsPlausiblePellet(
			Vector3.Zero,
			Vector3.Zero,
			new Vector3( 4016.0f, 0.0f, 0.0f ),
			Vector3.Forward,
			range: 4000.0f ) );
		Assert.IsFalse( LargeLadNativeWeaponRules.IsPlausiblePellet(
			Vector3.Zero,
			Vector3.Zero,
			new Vector3( 4016.1f, 0.0f, 0.0f ),
			Vector3.Forward,
			range: 4000.0f ) );
	}

	[TestMethod]
	public void ExclusiveFirearmPolicy_AllowsAtMostOneItem()
	{
		Assert.IsTrue(
			LargeLadNativeWeaponRules.CanAddExclusiveFirearm( 0 ) );
		Assert.IsFalse(
			LargeLadNativeWeaponRules.CanAddExclusiveFirearm( 1 ) );
	}

	[TestMethod]
	public void NativeInventoryCycling_WrapsAcrossAllBuckets()
	{
		Assert.AreEqual(
			0,
			LargeLadNativeWeaponRules.GetCycledIndex(
				currentIndex: 4,
				selectionCount: 5,
				direction: 1 ) );
		Assert.AreEqual(
			4,
			LargeLadNativeWeaponRules.GetCycledIndex(
				currentIndex: 0,
				selectionCount: 5,
				direction: -1 ) );
		Assert.AreEqual(
			0,
			LargeLadNativeWeaponRules.GetCycledIndex(
				currentIndex: -1,
				selectionCount: 5,
				direction: 1 ) );
	}

	[TestMethod]
	public void ExclusiveDrop_RequiresActiveLivingSkinnyKidItem()
	{
		Assert.IsTrue(
			LargeLadNativeWeaponRules.CanDropExclusiveFirearm(
				LargeLadRole.SkinnyKid,
				isLiving: true,
				isEatBusy: false,
				isExclusive: true,
				isActive: true ) );
		Assert.IsFalse(
			LargeLadNativeWeaponRules.CanDropExclusiveFirearm(
				LargeLadRole.SkinnyKid,
				isLiving: true,
				isEatBusy: false,
				isExclusive: true,
				isActive: false ) );
		Assert.IsFalse(
			LargeLadNativeWeaponRules.CanDropExclusiveFirearm(
				LargeLadRole.Minion,
				isLiving: true,
				isEatBusy: false,
				isExclusive: true,
				isActive: true ) );
	}

	[TestMethod]
	public void FirearmUse_RequiresLivingUnlockedSkinnyKidDuringPlaying()
	{
		Assert.IsTrue( LargeLadNativeWeaponRules.CanUseFirearm(
			LargeLadRole.SkinnyKid,
			isLiving: true,
			LargeLadRoundPhase.Playing,
			isEatBusy: false,
			isMovementLocked: false,
			isGroundSlamBusy: false,
			isGroundSlamStaggered: false,
			isHeld: true,
			isActive: true ) );

		Assert.IsFalse( LargeLadNativeWeaponRules.CanUseFirearm(
			LargeLadRole.Minion,
			isLiving: true,
			LargeLadRoundPhase.Playing,
			isEatBusy: false,
			isMovementLocked: false,
			isGroundSlamBusy: false,
			isGroundSlamStaggered: false,
			isHeld: true,
			isActive: true ) );
		Assert.IsFalse( LargeLadNativeWeaponRules.CanUseFirearm(
			LargeLadRole.SkinnyKid,
			isLiving: true,
			LargeLadRoundPhase.HeadStart,
			isEatBusy: false,
			isMovementLocked: false,
			isGroundSlamBusy: false,
			isGroundSlamStaggered: false,
			isHeld: true,
			isActive: true ) );
		Assert.IsFalse( LargeLadNativeWeaponRules.CanUseFirearm(
			LargeLadRole.SkinnyKid,
			isLiving: true,
			LargeLadRoundPhase.Playing,
			isEatBusy: true,
			isMovementLocked: false,
			isGroundSlamBusy: false,
			isGroundSlamStaggered: false,
			isHeld: true,
			isActive: true ) );
	}

	[TestMethod]
	public void ClaimEnvelope_RequiresMonotonicExactWeaponConfiguration()
	{
		Assert.IsTrue( LargeLadNativeWeaponRules.IsValidClaimEnvelope(
			sequence: 0,
			lastSequence: -1,
			damage: 100.0f,
			expectedDamage: 100.0f,
			force: 3000.0f,
			expectedForce: 3000.0f,
			claimedPellets: 1,
			maximumPellets: 1 ) );
		Assert.IsTrue( LargeLadNativeWeaponRules.IsValidClaimEnvelope(
			sequence: 8,
			lastSequence: 7,
			damage: 100.0f,
			expectedDamage: 100.0f,
			force: 3000.0f,
			expectedForce: 3000.0f,
			claimedPellets: 1,
			maximumPellets: 1 ) );

		Assert.IsFalse( LargeLadNativeWeaponRules.IsValidClaimEnvelope(
			sequence: 7,
			lastSequence: 7,
			damage: 100.0f,
			expectedDamage: 100.0f,
			force: 3000.0f,
			expectedForce: 3000.0f,
			claimedPellets: 1,
			maximumPellets: 1 ) );
		Assert.IsFalse( LargeLadNativeWeaponRules.IsValidClaimEnvelope(
			sequence: 8,
			lastSequence: 7,
			damage: 101.0f,
			expectedDamage: 100.0f,
			force: 3000.0f,
			expectedForce: 3000.0f,
			claimedPellets: 1,
			maximumPellets: 1 ) );
		Assert.IsFalse( LargeLadNativeWeaponRules.IsValidClaimEnvelope(
			sequence: 8,
			lastSequence: 7,
			damage: 100.0f,
			expectedDamage: 100.0f,
			force: 3000.0f,
			expectedForce: 3000.0f,
			claimedPellets: 2,
			maximumPellets: 1 ) );
	}

	[TestMethod]
	public void PelletPlausibility_AllowsThirdPersonCameraButRejectsRemoteOrigin()
	{
		Assert.IsTrue( LargeLadNativeWeaponRules.IsPlausiblePellet(
			ownerEyePosition: Vector3.Zero,
			origin: new Vector3( -190.0f, 42.0f, 18.0f ),
			position: new Vector3( 800.0f, 42.0f, 18.0f ),
			direction: Vector3.Forward,
			range: 1200.0f ) );

		Assert.IsFalse( LargeLadNativeWeaponRules.IsPlausiblePellet(
			ownerEyePosition: Vector3.Zero,
			origin: new Vector3( -400.0f, 0.0f, 0.0f ),
			position: new Vector3( 800.0f, 0.0f, 0.0f ),
			direction: Vector3.Forward,
			range: 1200.0f ) );
	}

	[TestMethod]
	public void NativeDamageClassification_PrefersClaimTagsWhenHitboxIsAbsent()
	{
		Assert.AreEqual(
			LargeLadHitRegion.Head,
			LargeLadNativeWeaponRules.ClassifyNativeDamage(
				damageHasHeadTag: true,
				hitboxHasHeadTag: false,
				hitboxBoneName: null ) );
		Assert.AreEqual(
			LargeLadHitRegion.Body,
			LargeLadNativeWeaponRules.ClassifyNativeDamage(
				damageHasHeadTag: false,
				hitboxHasHeadTag: false,
				hitboxBoneName: null ) );
	}

	[TestMethod]
	public void PlayerTargetPolicy_AllowsOnlyLivingHunterTeamVictims()
	{
		Assert.IsTrue( LargeLadNativeWeaponRules.IsValidPlayerTarget(
			LargeLadRole.SkinnyKid,
			LargeLadRole.LargeLad,
			victimIsLiving: true ) );
		Assert.IsTrue( LargeLadNativeWeaponRules.IsValidPlayerTarget(
			LargeLadRole.SkinnyKid,
			LargeLadRole.Minion,
			victimIsLiving: true ) );
		Assert.IsFalse( LargeLadNativeWeaponRules.IsValidPlayerTarget(
			LargeLadRole.SkinnyKid,
			LargeLadRole.SkinnyKid,
			victimIsLiving: true ) );
		Assert.IsFalse( LargeLadNativeWeaponRules.IsValidPlayerTarget(
			LargeLadRole.SkinnyKid,
			LargeLadRole.Minion,
			victimIsLiving: false ) );
	}
}
