using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public sealed class LargeLadRoleSelectionTests
{
	[DataTestMethod]
	[DataRow( LargeLadRolePreference.NoPreference )]
	[DataRow( LargeLadRolePreference.PreferLargeLad )]
	[DataRow( LargeLadRolePreference.PreferSkinnyKid )]
	public void DefinedPreferenceValues_AreAccepted(
		LargeLadRolePreference requested )
	{
		Assert.IsTrue(
			LargeLadRoleSelectionRules.TryAcceptPreference(
				LargeLadRolePreference.NoPreference,
				requested,
				out var accepted ) );
		Assert.AreEqual( requested, accepted );
	}

	[TestMethod]
	public void InvalidPreference_IsRejectedAndRestoresCurrentValue()
	{
		Assert.IsFalse(
			LargeLadRoleSelectionRules.TryAcceptPreference(
				LargeLadRolePreference.PreferSkinnyKid,
				(LargeLadRolePreference)999,
				out var accepted ) );
		Assert.AreEqual(
			LargeLadRolePreference.PreferSkinnyKid,
			accepted );
	}

	[TestMethod]
	public void WillingPlayer_IsChosenBeforeNeutralAndSkinnyPreferences()
	{
		var preferences = new[]
		{
			LargeLadRolePreference.NoPreference,
			LargeLadRolePreference.PreferSkinnyKid,
			LargeLadRolePreference.PreferLargeLad
		};

		Assert.AreEqual(
			2,
			LargeLadRoleSelectionRules.SelectLargeLadIndex(
				preferences,
				nextSelectionIndex: 0 ) );
	}

	[TestMethod]
	public void NeutralPlayer_IsChosenBeforeSkinnyPreference()
	{
		var preferences = new[]
		{
			LargeLadRolePreference.PreferSkinnyKid,
			LargeLadRolePreference.NoPreference,
			LargeLadRolePreference.PreferSkinnyKid
		};

		Assert.AreEqual(
			1,
			LargeLadRoleSelectionRules.SelectLargeLadIndex(
				preferences,
				nextSelectionIndex: 2 ) );
	}

	[TestMethod]
	public void EveryonePrefersSkinnyKid_RoundRobinStillSelectsSomeone()
	{
		var preferences = new[]
		{
			LargeLadRolePreference.PreferSkinnyKid,
			LargeLadRolePreference.PreferSkinnyKid,
			LargeLadRolePreference.PreferSkinnyKid
		};

		Assert.AreEqual(
			2,
			LargeLadRoleSelectionRules.SelectLargeLadIndex(
				preferences,
				nextSelectionIndex: 2 ) );
	}

	[TestMethod]
	public void EligibleTier_RotatesAndWrapsWithoutFavoringRosterStart()
	{
		var preferences = new[]
		{
			LargeLadRolePreference.PreferLargeLad,
			LargeLadRolePreference.PreferSkinnyKid,
			LargeLadRolePreference.PreferLargeLad,
			LargeLadRolePreference.NoPreference
		};

		var first = LargeLadRoleSelectionRules.SelectLargeLadIndex(
			preferences,
			nextSelectionIndex: 0 );
		var nextCursor =
			LargeLadRoleSelectionRules.GetNextSelectionIndex(
				first,
				preferences.Length );
		var second = LargeLadRoleSelectionRules.SelectLargeLadIndex(
			preferences,
			nextCursor );
		nextCursor = LargeLadRoleSelectionRules.GetNextSelectionIndex(
			second,
			preferences.Length );
		var third = LargeLadRoleSelectionRules.SelectLargeLadIndex(
			preferences,
			nextCursor );

		Assert.AreEqual( 0, first );
		Assert.AreEqual( 2, second );
		Assert.AreEqual( 0, third );
	}

	[TestMethod]
	public void EmptyRoster_HasNoCandidate()
	{
		Assert.AreEqual(
			-1,
			LargeLadRoleSelectionRules.SelectLargeLadIndex(
				System.Array.Empty<LargeLadRolePreference>(),
				nextSelectionIndex: 0 ) );
	}
}
