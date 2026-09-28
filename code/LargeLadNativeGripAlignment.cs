using Sandbox;
using System.Linq;

/// <summary>Rigid world-model placement at a native hand attachment. No arm IK.</summary>
internal static class LargeLadNativeGripAlignment
{
	internal static void Align( GameObject worldModel, GameObject attachment,
		ref GameObject cachedWorldModel, ref GameObject cachedGrip )
	{
		if ( worldModel is null || !worldModel.IsValid ||
			attachment is null || !attachment.IsValid )
			return;

		if ( cachedWorldModel != worldModel || cachedGrip is null || !cachedGrip.IsValid )
		{
			cachedWorldModel = worldModel;
			cachedGrip = worldModel.GetAllObjects( true )
				.FirstOrDefault( candidate => candidate.Name == "RightHandGrip" );
		}

		if ( cachedGrip is null || !cachedGrip.IsValid )
			return;

		if ( worldModel.Parent != attachment )
			worldModel.SetParent( attachment, true );

		var desiredGrip = attachment.WorldTransform;
		var currentGrip = cachedGrip.WorldTransform;
		var alignedRoot = worldModel.WorldTransform;
		var deltaRotation = desiredGrip.Rotation * currentGrip.Rotation.Inverse;
		alignedRoot.Position = desiredGrip.Position +
			deltaRotation * (alignedRoot.Position - currentGrip.Position);
		alignedRoot.Rotation = deltaRotation * alignedRoot.Rotation;
		worldModel.WorldTransform = alignedRoot;
	}
}
