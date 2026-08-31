using Sandbox;

/// <summary>
/// Presentation-only adapter for the official Spaghelli M4 incremental reload
/// graph. Ammo authority and the one-round insert loop remain entirely in
/// BaseCombatWeapon; this component only maps those callbacks to the graph's
/// incremental parameters at the cadence used by the official Facepunch setup.
/// </summary>
public sealed class LargeLadShotgunWeaponModel : BaseWeaponModel
{
	private const string SimpleReloadParameter = "b_reload";
	private const string ReloadingParameter = "b_reloading";
	private const string ShellTriggerParameter = "b_reloading_shell";
	private const string ReloadSpeedParameter = "speed_reload";
	private const string ShellGestureTag = "reload_increment";

	private SkinnedModelRenderer subscribedRenderer;
	private int pendingShellInsertions;
	private bool shellGesturePending;
	private bool reloadPresentationActive;
	private bool mechanicalReloadFinished;

	[Property]
	public float IncrementalAnimationSpeed { get; set; } = 1.75f;

	public override void OnReloadStart()
	{
		ResetReloadPresentation();
		EnsureAnimationEventSubscription();
		reloadPresentationActive = true;
		SetIncrementalReloading( true );
	}

	public override void OnIncrementalReload()
	{
		// Clip1 and reload timing remain native. This count represents only the
		// graph gestures that those authoritative insert callbacks have earned.
		pendingShellInsertions++;

		// A viewmodel can become active part-way through a reload after a camera
		// presentation change. Enter the stance before consuming its next event.
		if ( !reloadPresentationActive )
		{
			reloadPresentationActive = true;
			SetIncrementalReloading( true );
		}

		AdvanceReloadPresentation();
	}

	public override void OnReloadFinish()
	{
		if ( !reloadPresentationActive )
		{
			base.OnReloadFinish();
			return;
		}

		// BaseCombatWeapon finishes mechanically in the same task continuation
		// that inserts the last shell. Keep only the cosmetic stance alive until
		// the graph reports the end of that shell's visible gesture.
		mechanicalReloadFinished = true;
		AdvanceReloadPresentation();
	}

	public override void OnReloadCancel()
	{
		ResetReloadPresentation();
		base.OnReloadCancel();
	}

	public override void OnAttack( Vector3? hitPoint, Vector3? origin )
	{
		// Firing supersedes a mechanically-finished cosmetic tail immediately.
		ResetReloadPresentation();
		base.OnAttack( hitPoint, origin );
	}

	public override void OnDeploy()
	{
		ResetReloadPresentation();
		base.OnDeploy();
		EnsureAnimationEventSubscription();
	}

	protected override void OnEnabled()
	{
		base.OnEnabled();
		EnsureAnimationEventSubscription();
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		AdvanceReloadPresentation();
	}

	protected override void OnDisabled()
	{
		ResetReloadPresentation();
		RemoveAnimationEventSubscription();
		base.OnDisabled();
	}

	protected override void OnDestroy()
	{
		ResetReloadPresentation();
		RemoveAnimationEventSubscription();
		base.OnDestroy();
	}

	private void AdvanceReloadPresentation()
	{
		if ( !reloadPresentationActive )
			return;

		EnsureAnimationEventSubscription();

		if ( Renderer is null || !Renderer.IsValid )
		{
			pendingShellInsertions = 0;
			shellGesturePending = false;

			if ( mechanicalReloadFinished )
				CompleteReloadPresentation();

			return;
		}

		// The graph resets its trigger as soon as it has accepted the request,
		// before the visible shell gesture is over. Wait for reload_increment's
		// End tag instead so the final shell cannot be cut off by reload finish.
		if ( shellGesturePending )
			return;

		if ( pendingShellInsertions > 0 )
		{
			pendingShellInsertions--;
			shellGesturePending = true;
			Renderer.Set( ReloadSpeedParameter, IncrementalAnimationSpeed );
			Renderer.Set( ReloadingParameter, true );
			Renderer.Set( ShellTriggerParameter, true );
			return;
		}

		if ( mechanicalReloadFinished )
			CompleteReloadPresentation();
	}

	private void CompleteReloadPresentation()
	{
		pendingShellInsertions = 0;
		shellGesturePending = false;
		reloadPresentationActive = false;
		mechanicalReloadFinished = false;
		SetIncrementalReloading( false );
		base.OnReloadFinish();
	}

	private void ResetReloadPresentation()
	{
		pendingShellInsertions = 0;
		shellGesturePending = false;
		reloadPresentationActive = false;
		mechanicalReloadFinished = false;
		SetIncrementalReloading( false );
	}

	private void EnsureAnimationEventSubscription()
	{
		if ( subscribedRenderer == Renderer )
			return;

		RemoveAnimationEventSubscription();

		if ( Renderer is null || !Renderer.IsValid )
			return;

		subscribedRenderer = Renderer;
		subscribedRenderer.OnAnimTagEvent += HandleAnimationTagEvent;
	}

	private void RemoveAnimationEventSubscription()
	{
		if ( subscribedRenderer is not null && subscribedRenderer.IsValid )
			subscribedRenderer.OnAnimTagEvent -= HandleAnimationTagEvent;

		subscribedRenderer = null;
	}

	private void HandleAnimationTagEvent( SceneModel.AnimTagEvent animationEvent )
	{
		if ( !string.Equals( animationEvent.Name, ShellGestureTag, System.StringComparison.OrdinalIgnoreCase ) )
			return;

		if ( animationEvent.Status is not SceneModel.AnimTagStatus.End
			and not SceneModel.AnimTagStatus.Fired )
			return;

		shellGesturePending = false;
		AdvanceReloadPresentation();
	}

	private void SetIncrementalReloading( bool reloading )
	{
		if ( Renderer is null || !Renderer.IsValid )
			return;

		Renderer.Set( ReloadSpeedParameter, IncrementalAnimationSpeed );
		Renderer.Set( SimpleReloadParameter, false );
		Renderer.Set( ShellTriggerParameter, false );
		Renderer.Set( ReloadingParameter, reloading );
	}
}
