using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public sealed class LargeLadRoleAbilityPresentationRulesTests
{
	[TestMethod]
	public void SkinnyKidWithoutActiveUtility_HasNoCustomPresentation()
	{
		var state = ActiveState( LargeLadRole.SkinnyKid );

		Assert.AreEqual(
			LargeLadRoleAbilityPresentationKind.None,
			LargeLadRoleAbilityPresentationRules.ResolveKind( state ) );
		Assert.AreEqual(
			LargeLadRoleAbilityPresentationView.Hidden,
			LargeLadRoleAbilityPresentationRules.ResolveView( state ) );
	}

	[TestMethod]
	public void LargeLadAndMinion_KeepRoleMeleePresentationDuringPlay()
	{
		foreach ( var role in new[]
		{
			LargeLadRole.LargeLad,
			LargeLadRole.Minion
		} )
		{
			var state = ActiveState( role );

			Assert.AreEqual(
				LargeLadRoleAbilityPresentationKind.RoleMelee,
				LargeLadRoleAbilityPresentationRules.ResolveKind( state ) );
			Assert.AreEqual(
				LargeLadRoleAbilityPresentationView.FirstPerson,
				LargeLadRoleAbilityPresentationRules.ResolveView( state ) );
		}
	}

	[TestMethod]
	public void ActiveDodgeball_KeepsUtilityPresentation()
	{
		var state = ActiveState(
			LargeLadRole.SkinnyKid,
			LargeLadUtilityId.Dodgeball,
			utilityInstanceId: 12 );

		Assert.AreEqual(
			LargeLadRoleAbilityPresentationKind.Dodgeball,
			LargeLadRoleAbilityPresentationRules.ResolveKind( state ) );
		Assert.AreEqual(
			LargeLadRoleAbilityPresentationView.FirstPerson,
			LargeLadRoleAbilityPresentationRules.ResolveView( state ) );
	}

	[TestMethod]
	public void CameraAndOwnership_SelectFirstOrThirdPersonPresentation()
	{
		var firstPerson = ActiveState( LargeLadRole.Minion );
		var thirdPersonCamera = Copy(
			firstPerson,
			isThirdPersonCamera: true );
		var proxy = Copy(
			firstPerson,
			isLocalOwner: false );

		Assert.AreEqual(
			LargeLadRoleAbilityPresentationView.FirstPerson,
			LargeLadRoleAbilityPresentationRules.ResolveView( firstPerson ) );
		Assert.AreEqual(
			LargeLadRoleAbilityPresentationView.ThirdPerson,
			LargeLadRoleAbilityPresentationRules.ResolveView(
				thirdPersonCamera ) );
		Assert.AreEqual(
			LargeLadRoleAbilityPresentationView.ThirdPerson,
			LargeLadRoleAbilityPresentationRules.ResolveView( proxy ) );
	}

	[TestMethod]
	public void InvalidLifecycleStates_HideCustomPresentation()
	{
		var active = ActiveState( LargeLadRole.Minion );

		Assert.AreEqual(
			LargeLadRoleAbilityPresentationView.Hidden,
			LargeLadRoleAbilityPresentationRules.ResolveView(
				Copy( active, isDead: true ) ) );
		Assert.AreEqual(
			LargeLadRoleAbilityPresentationView.Hidden,
			LargeLadRoleAbilityPresentationRules.ResolveView(
				Copy(
					active,
					roundPhase: LargeLadRoundPhase.PostRound ) ) );
	}

	private static LargeLadRoleAbilityPresentationState ActiveState(
		LargeLadRole role,
		LargeLadUtilityId utility = LargeLadUtilityId.None,
		int utilityInstanceId = 0 )
	{
		return new LargeLadRoleAbilityPresentationState
		{
			Role = role,
			RoundPhase = LargeLadRoundPhase.Playing,
			Utility = utility,
			UtilityInstanceId = utilityInstanceId,
			IsDead = false,
			IsLocalOwner = true,
			HasOwnedCamera = true,
			IsThirdPersonCamera = false
		};
	}

	private static LargeLadRoleAbilityPresentationState Copy(
		LargeLadRoleAbilityPresentationState state,
		LargeLadRoundPhase? roundPhase = null,
		bool? isDead = null,
		bool? isLocalOwner = null,
		bool? isThirdPersonCamera = null )
	{
		return new LargeLadRoleAbilityPresentationState
		{
			Role = state.Role,
			RoundPhase = roundPhase ?? state.RoundPhase,
			Utility = state.Utility,
			UtilityInstanceId = state.UtilityInstanceId,
			IsDead = isDead ?? state.IsDead,
			IsLocalOwner = isLocalOwner ?? state.IsLocalOwner,
			HasOwnedCamera = state.HasOwnedCamera,
			IsThirdPersonCamera =
				isThirdPersonCamera ?? state.IsThirdPersonCamera
		};
	}
}
