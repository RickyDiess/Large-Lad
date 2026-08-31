using Sandbox;

/// <summary>
/// Presentation-only adapter for the official Spaghelli M4 incremental reload
/// graph. Ammo authority and the one-round insert loop remain entirely in
/// BaseCombatWeapon; this component only maps those callbacks to the graph's
/// incremental parameters at the cadence used by the official Facepunch setup.
/// </summary>
public sealed class LargeLadShotgunWeaponModel : BaseWeaponModel
{
	[Property]
	public float IncrementalAnimationSpeed { get; set; } = 1.75f;

	public override void OnReloadStart()
	{
		SetIncrementalReloading( true );
	}

	public override void OnIncrementalReload()
	{
		Renderer?.Set( "speed_reload", IncrementalAnimationSpeed );
		Renderer?.Set( "b_reloading_shell", true );
	}

	public override void OnReloadFinish()
	{
		SetIncrementalReloading( false );
		base.OnReloadFinish();
	}

	public override void OnReloadCancel()
	{
		SetIncrementalReloading( false );
		base.OnReloadCancel();
	}

	private void SetIncrementalReloading( bool reloading )
	{
		Renderer?.Set( "speed_reload", IncrementalAnimationSpeed );
		Renderer?.Set( "b_reload", false );
		Renderer?.Set( "b_reloading_shell", false );
		Renderer?.Set( "b_reloading", reloading );
	}
}
