using Sandbox;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

/// <summary>
/// Bridges component lifecycle registration to the game manager in the same
/// scene. There is deliberately no global "current" manager: every operation
/// is keyed by the component's exact scene.
/// </summary>
internal static class LargeLadSceneRegistry
{
	private sealed class SceneRegistrations
	{
		public readonly HashSet<LargeLadGameManager> Managers = new();
		public readonly HashSet<LargeLadPlayer> Players = new();
		public readonly HashSet<ILargeLadRoundResettable> Resettables = new();
	}

	private static readonly ConditionalWeakTable<Scene, SceneRegistrations>
		RegistrationsByScene = new();

	public static void RegisterManager(
		Scene scene,
		LargeLadGameManager manager )
	{
		if ( scene is null || !IsActiveInScene( manager, scene ) )
			return;

		var registrations = RegistrationsByScene.GetValue(
			scene,
			_ => new SceneRegistrations() );
		Prune( scene, registrations );
		registrations.Managers.Add( manager );

		foreach ( var player in registrations.Players
			.Where( player => IsActiveInScene( player, scene ) )
			.OrderBy( player => player.GameObject.Id )
			.ThenBy( player => player.Id ) )
		{
			manager.RegisterPlayer( player );
		}

		foreach ( var resettable in registrations.Resettables
			.Where( resettable => IsActiveInScene( resettable, scene ) )
			.OrderBy( resettable => ((Component)resettable).GameObject.Id )
			.ThenBy( resettable => ((Component)resettable).Id ) )
		{
			manager.RegisterRoundResettable( resettable );
		}
	}

	public static void UnregisterManager(
		Scene scene,
		LargeLadGameManager manager )
	{
		if ( scene is null || manager is null ||
			!RegistrationsByScene.TryGetValue( scene, out var registrations ) )
		{
			return;
		}

		registrations.Managers.Remove( manager );
	}

	public static void RegisterPlayer(
		Scene scene,
		LargeLadPlayer player )
	{
		if ( scene is null || !IsActiveInScene( player, scene ) )
			return;

		var registrations = RegistrationsByScene.GetValue(
			scene,
			_ => new SceneRegistrations() );
		Prune( scene, registrations );

		if ( !registrations.Players.Add( player ) )
			return;

		foreach ( var manager in registrations.Managers )
			manager.RegisterPlayer( player );
	}

	public static void UnregisterPlayer(
		Scene scene,
		LargeLadPlayer player )
	{
		if ( scene is null || player is null ||
			!RegistrationsByScene.TryGetValue( scene, out var registrations ) )
		{
			return;
		}

		registrations.Players.Remove( player );

		foreach ( var manager in registrations.Managers.ToList() )
		{
			if ( IsActiveInScene( manager, scene ) )
				manager.UnregisterPlayer( player );
			else
				registrations.Managers.Remove( manager );
		}
	}

	public static void NotifyPlayerRoleChanged(
		Scene scene,
		LargeLadPlayer player,
		LargeLadRole oldRole,
		LargeLadRole newRole )
	{
		if ( scene is null || player is null ||
			!RegistrationsByScene.TryGetValue( scene, out var registrations ) ||
			!registrations.Players.Contains( player ) )
		{
			return;
		}

		foreach ( var manager in registrations.Managers.ToList() )
		{
			if ( IsActiveInScene( manager, scene ) )
				manager.UpdatePlayerRole( player, oldRole, newRole );
			else
				registrations.Managers.Remove( manager );
		}
	}

	public static void RegisterRoundResettable(
		Scene scene,
		ILargeLadRoundResettable resettable )
	{
		if ( scene is null || !IsActiveInScene( resettable, scene ) )
			return;

		var registrations = RegistrationsByScene.GetValue(
			scene,
			_ => new SceneRegistrations() );
		Prune( scene, registrations );

		if ( !registrations.Resettables.Add( resettable ) )
			return;

		foreach ( var manager in registrations.Managers )
			manager.RegisterRoundResettable( resettable );
	}

	public static void UnregisterRoundResettable(
		Scene scene,
		ILargeLadRoundResettable resettable )
	{
		if ( scene is null || resettable is null ||
			!RegistrationsByScene.TryGetValue( scene, out var registrations ) )
		{
			return;
		}

		registrations.Resettables.Remove( resettable );

		foreach ( var manager in registrations.Managers.ToList() )
		{
			if ( IsActiveInScene( manager, scene ) )
				manager.UnregisterRoundResettable( resettable );
			else
				registrations.Managers.Remove( manager );
		}
	}

	public static LargeLadGameManager FindManager( Scene scene )
	{
		if ( scene is null ||
			!RegistrationsByScene.TryGetValue( scene, out var registrations ) )
		{
			return null;
		}

		Prune( scene, registrations );
		return registrations.Managers
			.OrderBy( manager => manager.GameObject.Id )
			.ThenBy( manager => manager.Id )
			.FirstOrDefault();
	}

	private static void Prune(
		Scene scene,
		SceneRegistrations registrations )
	{
		registrations.Managers.RemoveWhere(
			manager => !IsActiveInScene( manager, scene ) );
		registrations.Players.RemoveWhere(
			player => !IsActiveInScene( player, scene ) );
		registrations.Resettables.RemoveWhere(
			resettable => !IsActiveInScene( resettable, scene ) );
	}

	private static bool IsActiveInScene( Component component, Scene scene )
	{
		return component is not null &&
			component.IsValid &&
			component.Enabled &&
			component.Scene == scene;
	}

	private static bool IsActiveInScene(
		ILargeLadRoundResettable resettable,
		Scene scene )
	{
		return resettable is Component component &&
			IsActiveInScene( component, scene );
	}
}

/// <summary>
/// Lifecycle-aware base for authored state that participates in round reset.
/// Disabled and destroyed components leave the scene registry immediately.
/// </summary>
public abstract class LargeLadRoundResettableComponent : Component,
	ILargeLadRoundResettable
{
	private Scene registeredScene;

	protected override void OnEnabled()
	{
		base.OnEnabled();
		RegisterForRoundReset();
	}

	protected override void OnDisabled()
	{
		UnregisterFromRoundReset();
		base.OnDisabled();
	}

	protected override void OnDestroy()
	{
		UnregisterFromRoundReset();
		base.OnDestroy();
	}

	public abstract void ResetForRound();

	private void RegisterForRoundReset()
	{
		if ( registeredScene is not null && registeredScene != Scene )
		{
			LargeLadSceneRegistry.UnregisterRoundResettable(
				registeredScene,
				this );
		}

		registeredScene = Scene;
		LargeLadSceneRegistry.RegisterRoundResettable( registeredScene, this );
	}

	private void UnregisterFromRoundReset()
	{
		if ( registeredScene is null )
			return;

		LargeLadSceneRegistry.UnregisterRoundResettable(
			registeredScene,
			this );
		registeredScene = null;
	}
}
