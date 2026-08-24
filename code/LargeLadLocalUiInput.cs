/// <summary>
/// Client-process presentation gate for interactive Large Lad UI. It never
/// changes replicated gameplay state; local input consumers use it only to
/// avoid acting on clicks intended for an open interface.
/// </summary>
public static class LargeLadLocalUiInput
{
	public static bool IsScoreboardOpen { get; private set; }

	public static bool ShouldSuppressGameplayInput => IsScoreboardOpen;

	internal static void SetScoreboardOpen( bool isOpen )
	{
		IsScoreboardOpen = isOpen;
	}
}
