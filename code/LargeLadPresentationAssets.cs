using Sandbox;
using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>
/// Shared process-local mounts for the remaining custom arms and native firearm
/// reload audio. Weapon view/world models and fire effects come from native
/// weapon prefabs and are intentionally absent from this cache.
/// </summary>
internal static class LargeLadPresentationAssets
{
	private static readonly Dictionary<string, Model> models = new();
	private static readonly Dictionary<string, SoundEvent> sounds = new();
	private static Task loadTask;

	public static Task EnsureLoadedAsync()
	{
		loadTask ??= LoadAllAsync();
		return loadTask;
	}

	public static Model GetModel( string packageIdent )
	{
		return models.TryGetValue( packageIdent, out var model )
			? model
			: null;
	}

	public static SoundEvent GetSound( string packageIdent )
	{
		return sounds.TryGetValue( packageIdent, out var sound )
			? sound
			: null;
	}

	private static async Task LoadAllAsync()
	{
		var packageLoads = new Task[]
		{
			LoadModelAsync( "facepunch/v_first_person_arms_human" ),
			LoadSoundAsync( "drakefruit/pistol_reload" ),
			LoadSoundAsync( "drakefruit/rifle_reload" )
		};

		foreach ( var packageLoad in packageLoads )
			await packageLoad;
	}

	private static async Task LoadModelAsync( string packageIdent )
	{
		try
		{
			var model = await Cloud.Load<Model>( packageIdent );
			if ( model is not null && !model.IsError )
				models[packageIdent] = model;
		}
		catch ( System.Exception exception )
		{
			Log.Warning(
				$"Unable to mount presentation model package " +
				$"'{packageIdent}': {exception.Message}" );
		}
	}

	private static async Task LoadSoundAsync( string packageIdent )
	{
		try
		{
			var sound = await Cloud.Load<SoundEvent>( packageIdent );
			if ( sound is not null )
				sounds[packageIdent] = sound;
		}
		catch ( System.Exception exception )
		{
			Log.Warning(
				$"Unable to mount presentation sound package " +
				$"'{packageIdent}': {exception.Message}" );
		}
	}
}
