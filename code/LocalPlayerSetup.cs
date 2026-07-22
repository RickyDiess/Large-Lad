using Sandbox;

public sealed class LocalPlayerSetup : Component
{
	[Property]
	public CameraComponent PlayerCamera { get; set; }

	[Property]
	public AudioListener PlayerAudioListener { get; set; }

	private bool? lastLocalState;

	protected override void OnStart()
	{
		UpdateLocalState();
	}

	protected override void OnUpdate()
	{
		// Keeping this check in Update also handles ownership being
		// assigned shortly after the prefab initially appears.
		UpdateLocalState();
	}

	private void UpdateLocalState()
	{
		bool isLocalPlayer = !IsProxy;

		if ( lastLocalState == isLocalPlayer )
			return;

		lastLocalState = isLocalPlayer;

		if ( PlayerCamera is not null )
			PlayerCamera.Enabled = isLocalPlayer;

		if ( PlayerAudioListener is not null )
			PlayerAudioListener.Enabled = isLocalPlayer;
	}
}