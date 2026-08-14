using Sandbox;
using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>
/// Shared process-local mount for the remaining custom role-ability arms.
/// Native weapon models, effects, and sounds come from native weapon prefabs.
/// </summary>
internal static class LargeLadPresentationAssets
{
	private static readonly Dictionary<string, Model> models = new();
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

	private static async Task LoadAllAsync()
	{
		var packageLoads = new Task[]
		{
			LoadModelAsync( "facepunch/v_first_person_arms_human" )
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

}
