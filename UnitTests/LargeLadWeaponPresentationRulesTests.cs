using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public sealed class LargeLadWeaponPresentationVisibilityTests
{
	[TestMethod]
	public void SynchronizedSelection_DirectlyResolvesObserverWeapon()
	{
		Assert.AreEqual(
			LargeLadWeaponId.Pistol,
			LargeLadWeaponPresentationRules.ResolvePresentedWeapon(
				LargeLadRole.SkinnyKid,
				LargeLadInventorySelection.ForCoreFirearm(
					LargeLadWeaponId.Pistol ) ) );
		Assert.AreEqual(
			LargeLadWeaponId.Melee,
			LargeLadWeaponPresentationRules.ResolvePresentedWeapon(
				LargeLadRole.SkinnyKid,
				LargeLadInventorySelection.ForRoleMelee() ) );
		Assert.AreEqual(
			LargeLadWeaponId.None,
			LargeLadWeaponPresentationRules.ResolvePresentedWeapon(
				LargeLadRole.SkinnyKid,
				LargeLadInventorySelection.None ) );
	}

	[TestMethod]
	public void UtilityAndHunterRoles_ResolveWithoutStaleSkinnyKidWeapons()
	{
		var dodgeball = LargeLadInventorySelection.ForUtility(
			LargeLadUtilityId.Dodgeball,
			instanceId: 42 );
		Assert.AreEqual(
			LargeLadWeaponId.None,
			LargeLadWeaponPresentationRules.ResolvePresentedWeapon(
				LargeLadRole.SkinnyKid,
				dodgeball ) );

		var stalePistol = LargeLadInventorySelection.ForCoreFirearm(
			LargeLadWeaponId.Pistol );
		Assert.AreEqual(
			LargeLadWeaponId.Melee,
			LargeLadWeaponPresentationRules.ResolvePresentedWeapon(
				LargeLadRole.LargeLad,
				stalePistol ) );
		Assert.AreEqual(
			LargeLadWeaponId.Melee,
			LargeLadWeaponPresentationRules.ResolvePresentedWeapon(
				LargeLadRole.Minion,
				stalePistol ) );
	}

	[TestMethod]
	public void LocalOwner_UsesViewmodelOnlyWithItsFirstPersonCamera()
	{
		var firstPerson = ActiveSkinnyKid();
		Assert.AreEqual(
			LargeLadWeaponPresentationView.FirstPerson,
			LargeLadWeaponPresentationRules.ResolveView( firstPerson ) );

		Assert.AreEqual(
			LargeLadWeaponPresentationView.ThirdPerson,
			LargeLadWeaponPresentationRules.ResolveView(
				WithCamera( firstPerson, thirdPerson: true ) ) );
		Assert.AreEqual(
			LargeLadWeaponPresentationView.ThirdPerson,
			LargeLadWeaponPresentationRules.ResolveView(
				WithCamera( firstPerson, hasCamera: false ) ) );
		Assert.AreEqual(
			LargeLadWeaponPresentationView.ThirdPerson,
			LargeLadWeaponPresentationRules.ResolveView(
				WithOwnership( firstPerson, isLocalOwner: false ) ) );
	}

	[TestMethod]
	public void InvalidLifecycleStates_HideAllWeaponPresentation()
	{
		var active = ActiveSkinnyKid();
		var hiddenStates = new[]
		{
			WithDeath( active, isDead: true ),
			WithPhase( active, LargeLadRoundPhase.RoundOver ),
			WithPhase( active, LargeLadRoundPhase.WaitingForPlayers ),
			WithWeapon( active, LargeLadWeaponId.None ),
			WithRole( active, LargeLadRole.Unassigned ),
			WithRoleAndPhase(
				WithWeapon( active, LargeLadWeaponId.Melee ),
				LargeLadRole.Minion,
				LargeLadRoundPhase.HeadStart )
		};

		foreach ( var state in hiddenStates )
		{
			Assert.AreEqual(
				LargeLadWeaponPresentationView.Hidden,
				LargeLadWeaponPresentationRules.ResolveView( state ) );
		}
	}

	[TestMethod]
	public void UtilitySelectionAndConversionCannotLeakStaleFirearmGeometry()
	{
		var firearm = ActiveSkinnyKid();
		var utility = WithDodgeball( firearm, instanceId: 12 );
		var conversionBeforeInventoryCatchesUp =
			WithRole( firearm, LargeLadRole.Minion );
		var convertedMelee = WithRole(
			WithWeapon( firearm, LargeLadWeaponId.Melee ),
			LargeLadRole.Minion );

		Assert.AreEqual(
			LargeLadWeaponPresentationView.FirstPerson,
			LargeLadWeaponPresentationRules.ResolveView( utility ) );
		Assert.IsTrue(
			LargeLadWeaponPresentationRules.IsDodgeballSelected( utility ) );
		Assert.AreEqual(
			LargeLadWeaponPresentationView.Hidden,
			LargeLadWeaponPresentationRules.ResolveView(
				conversionBeforeInventoryCatchesUp ) );
		Assert.AreEqual(
			LargeLadWeaponPresentationView.FirstPerson,
			LargeLadWeaponPresentationRules.ResolveView( convertedMelee ) );
	}

	internal static LargeLadWeaponPresentationState ActiveSkinnyKid()
	{
		return new LargeLadWeaponPresentationState
		{
			Role = LargeLadRole.SkinnyKid,
			RoundPhase = LargeLadRoundPhase.Playing,
			Weapon = LargeLadWeaponId.Pistol,
			Selection = LargeLadInventorySelection.ForCoreFirearm(
				LargeLadWeaponId.Pistol ),
			IsDead = false,
			IsLocalOwner = true,
			HasOwnedCamera = true,
			IsThirdPersonCamera = false,
			IsReloading = false
		};
	}

	internal static LargeLadWeaponPresentationState WithCamera(
		LargeLadWeaponPresentationState state,
		bool hasCamera = true,
		bool thirdPerson = false )
	{
		return Copy(
			state,
			hasCamera: hasCamera,
			thirdPerson: thirdPerson );
	}

	internal static LargeLadWeaponPresentationState WithOwnership(
		LargeLadWeaponPresentationState state,
		bool isLocalOwner )
	{
		return Copy( state, isLocalOwner: isLocalOwner );
	}

	internal static LargeLadWeaponPresentationState WithDeath(
		LargeLadWeaponPresentationState state,
		bool isDead )
	{
		return Copy( state, isDead: isDead );
	}

	internal static LargeLadWeaponPresentationState WithPhase(
		LargeLadWeaponPresentationState state,
		LargeLadRoundPhase phase )
	{
		return Copy( state, phase: phase );
	}

	internal static LargeLadWeaponPresentationState WithWeapon(
		LargeLadWeaponPresentationState state,
		LargeLadWeaponId weapon )
	{
		var selection = weapon switch
		{
			LargeLadWeaponId.Melee =>
				LargeLadInventorySelection.ForRoleMelee(),
			LargeLadWeaponId.Pistol or LargeLadWeaponId.Smg =>
				LargeLadInventorySelection.ForCoreFirearm( weapon ),
			_ => LargeLadInventorySelection.None
		};
		return Copy( state, weapon: weapon, selection: selection );
	}

	internal static LargeLadWeaponPresentationState WithRole(
		LargeLadWeaponPresentationState state,
		LargeLadRole role )
	{
		return Copy( state, role: role );
	}

	internal static LargeLadWeaponPresentationState WithRoleAndPhase(
		LargeLadWeaponPresentationState state,
		LargeLadRole role,
		LargeLadRoundPhase phase )
	{
		return Copy( state, role: role, phase: phase );
	}

	internal static LargeLadWeaponPresentationState WithReload(
		LargeLadWeaponPresentationState state,
		bool isReloading )
	{
		return Copy( state, isReloading: isReloading );
	}

	internal static LargeLadWeaponPresentationState WithSelection(
		LargeLadWeaponPresentationState state,
		LargeLadInventorySelection selection )
	{
		return Copy( state, selection: selection );
	}

	internal static LargeLadWeaponPresentationState WithDodgeball(
		LargeLadWeaponPresentationState state,
		int instanceId )
	{
		return Copy(
			state,
			weapon: LargeLadWeaponId.None,
			selection: LargeLadInventorySelection.ForUtility(
				LargeLadUtilityId.Dodgeball,
				instanceId ) );
	}

	private static LargeLadWeaponPresentationState Copy(
		LargeLadWeaponPresentationState state,
		LargeLadRole? role = null,
		LargeLadRoundPhase? phase = null,
		LargeLadWeaponId? weapon = null,
		LargeLadInventorySelection? selection = null,
		bool? isDead = null,
		bool? isLocalOwner = null,
		bool? hasCamera = null,
		bool? thirdPerson = null,
		bool? isReloading = null )
	{
		return new LargeLadWeaponPresentationState
		{
			Role = role ?? state.Role,
			RoundPhase = phase ?? state.RoundPhase,
			Weapon = weapon ?? state.Weapon,
			Selection = selection ?? state.Selection,
			IsDead = isDead ?? state.IsDead,
			IsLocalOwner = isLocalOwner ?? state.IsLocalOwner,
			HasOwnedCamera = hasCamera ?? state.HasOwnedCamera,
			IsThirdPersonCamera = thirdPerson ?? state.IsThirdPersonCamera,
			IsReloading = isReloading ?? state.IsReloading
		};
	}
}

[TestClass]
public sealed class LargeLadWeaponPresentationTransitionTests
{
	[TestMethod]
	public void SwitchingAndExclusiveDrop_InterruptThenDrawTheNewWeapon()
	{
		var pistol =
			LargeLadWeaponPresentationVisibilityTests.ActiveSkinnyKid();
		var smg =
			LargeLadWeaponPresentationVisibilityTests.WithWeapon(
				pistol,
				LargeLadWeaponId.Smg );
		var actions = LargeLadWeaponPresentationRules.ResolveTransition(
			smg,
			pistol );

		Assert.IsTrue( actions.HasFlag(
			LargeLadWeaponPresentationAction.Interrupt ) );
		Assert.IsTrue( actions.HasFlag(
			LargeLadWeaponPresentationAction.Rebuild ) );
		Assert.IsTrue( actions.HasFlag(
			LargeLadWeaponPresentationAction.Draw ) );

		var exclusivePistol =
			LargeLadWeaponPresentationVisibilityTests.WithSelection(
				LargeLadWeaponPresentationVisibilityTests.WithReload(
					pistol,
					isReloading: true ),
				LargeLadInventorySelection.ForExclusiveFirearm(
					LargeLadWeaponId.Pistol,
					instanceId: 17 ) );
		var sameWeaponFallback =
			LargeLadWeaponPresentationRules.ResolveTransition(
				exclusivePistol,
				pistol );

		Assert.IsTrue( sameWeaponFallback.HasFlag(
			LargeLadWeaponPresentationAction.Interrupt ) );
		Assert.IsTrue( sameWeaponFallback.HasFlag(
			LargeLadWeaponPresentationAction.Draw ) );
	}

	[TestMethod]
	public void DeathUtilityAndRoundEnd_InterruptWithoutDrawing()
	{
		var active =
			LargeLadWeaponPresentationVisibilityTests.ActiveSkinnyKid();
		var endings = new[]
		{
			LargeLadWeaponPresentationVisibilityTests.WithDeath(
				active,
				isDead: true ),
			LargeLadWeaponPresentationVisibilityTests.WithWeapon(
				active,
				LargeLadWeaponId.None ),
			LargeLadWeaponPresentationVisibilityTests.WithPhase(
				active,
				LargeLadRoundPhase.RoundOver )
		};

		foreach ( var ending in endings )
		{
			var actions = LargeLadWeaponPresentationRules.ResolveTransition(
				active,
				ending );
			Assert.AreEqual(
				LargeLadWeaponPresentationAction.Interrupt,
				actions );
		}
	}

	[TestMethod]
	public void ReloadAndCameraRecreation_HaveFocusedNonRepeatingDecisions()
	{
		var idle =
			LargeLadWeaponPresentationVisibilityTests.ActiveSkinnyKid();
		var reloading =
			LargeLadWeaponPresentationVisibilityTests.WithReload(
				idle,
				isReloading: true );

		Assert.AreEqual(
			LargeLadWeaponPresentationAction.StartReload,
			LargeLadWeaponPresentationRules.ResolveTransition(
				idle,
				reloading ) );
		Assert.AreEqual(
			LargeLadWeaponPresentationAction.None,
			LargeLadWeaponPresentationRules.ResolveTransition(
				reloading,
				reloading ) );
		Assert.AreEqual(
			LargeLadWeaponPresentationAction.FinishReload,
			LargeLadWeaponPresentationRules.ResolveTransition(
				reloading,
				idle ) );
		Assert.AreEqual(
			LargeLadWeaponPresentationAction.Rebuild,
			LargeLadWeaponPresentationRules.ResolveTransition(
				idle,
				idle,
				cameraChanged: true ) );
	}
}

[TestClass]
public sealed class LargeLadWeaponPresentationSignalTests
{
	[TestMethod]
	public void AcceptedShots_PresentOnceOnlyForMatchingVisibleWeapon()
	{
		var pistol =
			LargeLadWeaponPresentationVisibilityTests.ActiveSkinnyKid();

		Assert.IsTrue(
			LargeLadWeaponPresentationRules.ShouldPresentAcceptedShot(
				pistol,
				lastPresentedSequence: 4,
				authoritativeSequence: 5,
				authoritativeWeapon: LargeLadWeaponId.Pistol ) );
		Assert.IsFalse(
			LargeLadWeaponPresentationRules.ShouldPresentAcceptedShot(
				pistol,
				lastPresentedSequence: 5,
				authoritativeSequence: 5,
				authoritativeWeapon: LargeLadWeaponId.Pistol ) );
		Assert.IsFalse(
			LargeLadWeaponPresentationRules.ShouldPresentAcceptedShot(
				pistol,
				lastPresentedSequence: 4,
				authoritativeSequence: 5,
				authoritativeWeapon: LargeLadWeaponId.Smg ) );
		Assert.IsFalse(
			LargeLadWeaponPresentationRules.ShouldPresentAcceptedShot(
				LargeLadWeaponPresentationVisibilityTests.WithDeath(
					pistol,
					isDead: true ),
				lastPresentedSequence: 4,
				authoritativeSequence: 5,
				authoritativeWeapon: LargeLadWeaponId.Pistol ) );
	}

	[TestMethod]
	public void EmptyFire_IsLocalFirstPersonFeedbackOnly()
	{
		var localFirstPerson =
			LargeLadWeaponPresentationVisibilityTests.ActiveSkinnyKid();
		var remote =
			LargeLadWeaponPresentationVisibilityTests.WithOwnership(
				localFirstPerson,
				isLocalOwner: false );
		var localThirdPerson =
			LargeLadWeaponPresentationVisibilityTests.WithCamera(
				localFirstPerson,
				thirdPerson: true );

		Assert.IsTrue(
			LargeLadWeaponPresentationRules.ShouldPresentEmptyFire(
				localFirstPerson,
				lastPresentedSequence: 8,
				authoritativeSequence: 9,
				authoritativeWeapon: LargeLadWeaponId.Pistol ) );
		Assert.IsFalse(
			LargeLadWeaponPresentationRules.ShouldPresentEmptyFire(
				remote,
				lastPresentedSequence: 8,
				authoritativeSequence: 9,
				authoritativeWeapon: LargeLadWeaponId.Pistol ) );
		Assert.IsFalse(
			LargeLadWeaponPresentationRules.ShouldPresentEmptyFire(
				localThirdPerson,
				lastPresentedSequence: 8,
				authoritativeSequence: 9,
				authoritativeWeapon: LargeLadWeaponId.Pistol ) );
	}
}
