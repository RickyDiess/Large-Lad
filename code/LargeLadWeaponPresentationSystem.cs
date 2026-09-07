using Sandbox;

/// <summary>Resolve weapon placement and hand targets after controller updates,
/// before s&box evaluates animation, IK, attachments and bone-merged clothing.</summary>
public sealed class LargeLadWeaponPresentationSystem : GameObjectSystem<LargeLadWeaponPresentationSystem>
{
	public LargeLadWeaponPresentationSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.UpdateBones, -1, UpdatePresentation, "Large Lad weapon presentation" );
	}

	private void UpdatePresentation()
	{
		if ( Scene.IsEditor )
			return;

		foreach ( var player in Scene.GetAllComponents<LargeLadPlayer>() )
			player.UpdateWeaponPresentation();
	}
}
