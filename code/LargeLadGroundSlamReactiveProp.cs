using Sandbox;
using System.Collections.Generic;

/// <summary>
/// Explicit mapper opt-in for a prop that may react to Ground Slam. This is
/// intentionally separate from generic rigidbodies and damageables: without
/// this component, a prop is never selected by the slam.
/// </summary>
[Description(
	"Attach directly to a collidable model or Prop to make it react to Ground " +
	"Slam. Networking and simple model physics are configured automatically; " +
	"critical gameplay objects and authoritative blockers are always rejected." )]
public sealed class LargeLadGroundSlamReactiveProp :
	LargeLadRoundResettableComponent,
	Component.ExecuteInEditor
{
	private sealed class AuthoredComponentState
	{
		public Component Target { get; init; }
		public bool Enabled { get; init; }
	}

	[Property, Group( "Setup" )]
	[Description(
		"Optional child containing the prop visuals and physics. Leave empty to " +
		"use this component's GameObject." )]
	public GameObject ReactiveRoot { get; set; }

	[Property, Group( "Setup" )]
	public LargeLadGroundSlamPropBehavior Behavior { get; set; } =
		LargeLadGroundSlamPropBehavior.Move;

	[Property, Group( "Setup" ), Title( "Start Frozen" )]
	[Description(
		"Hold the prop in its authored position until Ground Slam first moves it. " +
		"Disable this for props that should already be live physics, such as the " +
		"future dodgeball." )]
	public bool StartFrozen { get; set; } = true;

	[Property, Group( "Impulse" ), Title( "Horizontal Impulse" )]
	public float HorizontalImpulse { get; set; } = 125000.0f;

	[Property, Group( "Impulse" ), Title( "Upward Impulse" )]
	public float UpwardImpulse { get; set; } = 150000.0f;

	[Property, Group( "Optional Cleanup" ), Title( "Enable Out-of-bounds Cleanup" )]
	[Description(
		"Disables the prop until round reset after it drops below Minimum World Z " +
		"or travels beyond Maximum Distance From Start. Vent clearance remains the " +
		"protected-passage system's responsibility." )]
	public bool EnableOutOfBoundsCleanup { get; set; }

	[Property, Group( "Optional Cleanup" ), Title( "Minimum World Z" )]
	[ShowIf( nameof( EnableOutOfBoundsCleanup ), true )]
	public float MinimumWorldZ { get; set; } = -4096.0f;

	[Property, Group( "Optional Cleanup" ), Title( "Maximum Distance From Start" )]
	[ShowIf( nameof( EnableOutOfBoundsCleanup ), true )]
	[Description( "Set to zero to use only the Minimum World Z check." )]
	public float MaximumDistanceFromStart { get; set; } = 4096.0f;

	[Property, Group( "Diagnostics" )]
	[Description(
		"Logs host-side initialization, queued slam impulses, and the resulting " +
		"physics velocity. Intended for focused multiplayer testing." )]
	public bool EnableDebugLogging { get; set; } = true;

	[Sync( SyncFlags.FromHost ), Change( nameof( OnBrokenChanged ) )]
	public bool IsBroken { get; private set; }

	[Sync( SyncFlags.FromHost ), Change( nameof( OnCleanedUpChanged ) )]
	public bool IsCleanedUp { get; private set; }

	private readonly Dictionary<Component, AuthoredComponentState>
		authoredComponentStates = new();
	private Transform authoredLocalTransform;
	private Vector3 authoredWorldPosition;
	private bool authoredRootEnabled;
	private bool authoredRootIsStatic;
	private bool authoredPropIsStatic;
	private bool authoredPropStartAsleep;
	private bool authoredRigidbodyEnabled;
	private bool authoredHasPhysicsBody;
	private PhysicsBodyType authoredBodyType;
	private bool authoredMotionEnabled;
	private bool authoredSleeping;
	private bool hasCapturedAuthoredState;
	private bool hasPendingImpulse;
	private bool pendingUnanchor;
	private Vector3 pendingImpulse;
	private Prop reactiveProp;
	private Rigidbody reactiveRigidbody;

	public static LargeLadGroundSlamReactiveProp FindFor( GameObject target )
	{
		return target?.Components.Get<LargeLadGroundSlamReactiveProp>(
			FindMode.EverythingInSelfAndAncestors );
	}

	protected override void OnAwake()
	{
		ConfigureObject();
		ResolveReactiveParts();
		EnsureDirectModelPhysics();
	}

	protected override void OnStart()
	{
		ConfigureObject();
		ResolveReactiveParts();
		EnsureDirectModelPhysics();
		TryInitializeAuthoredState();
	}

	protected override void OnUpdate()
	{
		if ( Scene?.IsEditor == true )
		{
			// OnValidate is not guaranteed to run when a mapper is attached during
			// every editor workflow. ExecuteInEditor keeps the scene-authored
			// network mode correct before the scene is saved or played.
			ConfigureObject();
			ResolveReactiveParts();
			EnsureDirectModelPhysics();
			return;
		}

		// Prop generates its hidden physics components after its own lifecycle
		// begins. Retry until that Rigidbody has a body, then freeze and capture
		// the actual starting physics state before gameplay can use the mapper.
		if ( !hasCapturedAuthoredState )
		{
			TryInitializeAuthoredState();

			if ( !hasCapturedAuthoredState )
				return;
		}

		if ( !Networking.IsHost ||
			!EnableOutOfBoundsCleanup ||
			IsBroken ||
			IsCleanedUp ||
			!hasCapturedAuthoredState )
		{
			return;
		}

		var root = GetReactiveRoot();

		if ( root is null || !root.IsValid )
			return;

		var belowWorld = root.WorldPosition.z < MinimumWorldZ;
		var maximumDistance = System.MathF.Max(
			0.0f,
			MaximumDistanceFromStart );
		var tooFar = maximumDistance > 0.0f &&
			root.WorldPosition.Distance( authoredWorldPosition ) > maximumDistance;

		if ( belowWorld || tooFar )
		{
			IsCleanedUp = true;
			RefreshPresentation();
		}
	}

	protected override void OnFixedUpdate()
	{
		if ( Scene?.IsEditor == true || !Networking.IsHost || !hasPendingImpulse )
			return;

		if ( ApplyPendingImpulseAuthoritatively(
			pendingImpulse,
			pendingUnanchor ) )
		{
			hasPendingImpulse = false;
			pendingUnanchor = false;
			pendingImpulse = Vector3.Zero;
		}
	}

	protected override void OnValidate()
	{
		// Network mode must be authored before the scene starts. Changing it only
		// from OnAwake is too late for the scene's initial network snapshot.
		ConfigureObject();
		ResolveReactiveParts();
		EnsureDirectModelPhysics();

		foreach ( var warning in GetValidationWarnings() )
			Log.Warning( $"{GameObject.Name}: Ground Slam prop: {warning}" );
	}

	public IReadOnlyList<string> GetValidationWarnings()
	{
		ResolveReactiveParts();
		var warnings = new List<string>();
		var root = GetReactiveRoot();

		if ( root is null )
		{
			warnings.Add( "Reactive Root could not be resolved." );
			return warnings;
		}

		if ( root != GameObject &&
			root.Components.Get<LargeLadGroundSlamReactiveProp>(
				FindMode.EverythingInSelfAndAncestors ) != this )
		{
			warnings.Add(
				"Reactive Root must be this GameObject or one of its descendants." );
		}

		if ( GameObject.NetworkMode != NetworkMode.Object )
		{
			warnings.Add(
				"the component root must use Network Mode Object so physics and " +
				"break state replicate." );
		}

		if ( !System.Enum.IsDefined(
			typeof( LargeLadGroundSlamPropBehavior ),
			Behavior ) )
		{
			warnings.Add( "Behavior must be Move, Unanchor, or Break." );
		}

		var isCritical = IsCriticalGameplayObject();
		var isBlocker = IsAuthoritativeBlocker();

		if ( isCritical )
		{
			warnings.Add(
				"critical gameplay objects cannot opt into Ground Slam prop reactions." );
		}

		if ( isBlocker )
		{
			warnings.Add(
				"authoritative blockers cannot opt into Ground Slam prop reactions." );
		}

		if ( Behavior != LargeLadGroundSlamPropBehavior.Break &&
			reactiveRigidbody is null &&
			reactiveProp is null )
		{
			warnings.Add(
				$"{Behavior} requires a Prop or a collidable model that can receive " +
				"an automatic Rigidbody." );
		}

		var body = reactiveRigidbody?.PhysicsBody;

		if ( Behavior == LargeLadGroundSlamPropBehavior.Move &&
			(root.IsStatic ||
				reactiveProp?.IsStatic == true ||
				(!StartFrozen &&
					body is not null &&
					body.BodyType != PhysicsBodyType.Dynamic)) )
		{
			warnings.Add(
				"Move requires an authored dynamic prop; choose Unanchor for an " +
				"anchored prop." );
		}

		if ( !float.IsFinite( HorizontalImpulse ) ||
			HorizontalImpulse < 0.0f ||
			HorizontalImpulse >
				LargeLadGroundSlamRules.MaximumHorizontalImpulse )
		{
			warnings.Add(
				$"Horizontal Impulse must be 0 to " +
				$"{LargeLadGroundSlamRules.MaximumHorizontalImpulse}; runtime use " +
				"is clamped." );
		}

		if ( !float.IsFinite( UpwardImpulse ) ||
			UpwardImpulse < 0.0f ||
			UpwardImpulse > LargeLadGroundSlamRules.MaximumUpwardImpulse )
		{
			warnings.Add(
				$"Upward Impulse must be 0 to " +
				$"{LargeLadGroundSlamRules.MaximumUpwardImpulse}; runtime use is " +
				"clamped." );
		}

		if ( EnableOutOfBoundsCleanup &&
			(!float.IsFinite( MinimumWorldZ ) ||
				!float.IsFinite( MaximumDistanceFromStart ) ||
				MaximumDistanceFromStart < 0.0f) )
		{
			warnings.Add(
				"cleanup bounds must be finite and Maximum Distance From Start " +
				"cannot be negative." );
		}

		return warnings;
	}

	public Vector3 GetSlamEffectPoint()
	{
		var root = GetReactiveRoot();
		return root is not null && root.IsValid
			? root.GetBounds().Center
			: GameObject.WorldPosition;
	}

	internal bool TryReactToGroundSlam( Vector3 slamOrigin )
	{
		if ( !Networking.IsHost || IsBroken || IsCleanedUp )
			return false;

		ResolveReactiveParts();
		var root = GetReactiveRoot();
		var body = reactiveRigidbody?.PhysicsBody;
		var hasUsableRigidbody = body is not null;

		if ( !LargeLadGroundSlamRules.CanReactivePropReact(
			isExplicitlyDesignated: true,
			IsCriticalGameplayObject(),
			IsAuthoritativeBlocker(),
			Behavior,
			hasUsableRigidbody ) )
		{
			return false;
		}

		if ( Behavior == LargeLadGroundSlamPropBehavior.Move &&
			(root.IsStatic ||
				reactiveProp?.IsStatic == true ||
				(!StartFrozen && body.BodyType != PhysicsBodyType.Dynamic)) )
		{
			return false;
		}

		if ( Behavior == LargeLadGroundSlamPropBehavior.Break )
		{
			if ( reactiveProp is not null &&
				reactiveProp.IsValid &&
				reactiveProp.Enabled )
			{
				reactiveProp.NetworkCreateGibs( false );
			}

			IsBroken = true;
			RefreshPresentation();
			return true;
		}

		var impulse = LargeLadGroundSlamRules.GetRadialImpulse(
			slamOrigin,
			GetSlamEffectPoint(),
			HorizontalImpulse,
			UpwardImpulse,
			GameObject.Id.CompareTo( root.Id ) >= 0 );
		pendingImpulse = impulse;
		pendingUnanchor = Behavior == LargeLadGroundSlamPropBehavior.Unanchor;
		hasPendingImpulse = true;

		if ( EnableDebugLogging )
		{
			Log.Info(
				$"[Debug/Ground Slam Prop] Host queued '{GameObject.Name}' " +
				$"impulse {impulse}; behavior {Behavior}, body type " +
				$"{body.BodyType}, motion {body.MotionEnabled}, mass " +
				$"{body.Mass:0.###}." );
		}

		return true;
	}

	public override void ResetForRound()
	{
		if ( !Networking.IsHost )
			return;

		ResolveReactiveParts();
		CaptureAuthoredState();
		hasPendingImpulse = false;
		pendingUnanchor = false;
		pendingImpulse = Vector3.Zero;
		IsBroken = false;
		IsCleanedUp = false;
		RestoreForOwner();
		RefreshPresentation();
	}

	private bool ApplyPendingImpulseAuthoritatively(
		Vector3 impulse,
		bool unanchor )
	{
		if ( GameObject.Network.Active && GameObject.Network.IsProxy )
		{
			// Reactive props are server-simulated. If stale client ownership made
			// this object a host proxy, reclaim server simulation before release.
			GameObject.Network.DropOwnership();
			return false;
		}

		ResolveReactiveParts();
		var root = GetReactiveRoot();
		var body = reactiveRigidbody?.PhysicsBody;

		if ( root is null || body is null )
		{
			if ( EnableDebugLogging )
			{
				Log.Warning(
					$"[Debug/Ground Slam Prop] '{GameObject.Name}' lost its " +
					"physics body before the queued impulse could be applied." );
			}

			return false;
		}

		if ( unanchor )
		{
			root.IsStatic = false;

			if ( reactiveProp is not null && reactiveProp.IsValid )
				reactiveProp.IsStatic = false;

			// Static flags may rebuild Prop's generated body. Resolve again rather
			// than applying to the body instance that was just replaced.
			ResolveReactiveParts();
			body = reactiveRigidbody?.PhysicsBody;

			if ( body is null )
				return false;
		}

		// Release and impulse immediately before physics simulation. Applying an
		// impulse in the same update that MotionEnabled changes can be discarded
		// by the physics body's deferred state transition.
		body.BodyType = PhysicsBodyType.Dynamic;
		body.MotionEnabled = true;
		body.Sleeping = false;

		if ( body.ShapeCount <= 0 || body.Mass <= 0.001f )
		{
			// Collider attachment and dynamic mass calculation can complete one
			// physics step after the body is released. Keep the impulse queued and
			// retry instead of silently consuming it against a zero-mass shell.
			return false;
		}

		var velocityBefore = body.Velocity;
		body.Velocity = velocityBefore + impulse / body.Mass;

		if ( EnableDebugLogging )
		{
			Log.Info(
				$"[Debug/Ground Slam Prop] Host applied '{GameObject.Name}' " +
				$"impulse {impulse}; velocity {velocityBefore} -> " +
				$"{body.Velocity}, body type {body.BodyType}, motion " +
				$"{body.MotionEnabled}." );
		}

		return true;
	}

	[Rpc.Owner( NetFlags.HostOnly )]
	private void RestoreForOwner()
	{
		RestoreAuthoredState();
	}

	private void ConfigureObject()
	{
		if ( GameObject is null || !GameObject.IsValid )
			return;

		if ( GameObject.NetworkMode != NetworkMode.Object )
			GameObject.NetworkMode = NetworkMode.Object;
	}

	/// <summary>
	/// A Prop creates and manages its own physics components. For a plain model
	/// with an authored collider, add the only missing piece so attaching this
	/// mapper is enough to make Move and Unanchor usable.
	/// </summary>
	private void EnsureDirectModelPhysics()
	{
		if ( Behavior == LargeLadGroundSlamPropBehavior.Break ||
			ReactiveRoot is not null ||
			reactiveProp is not null ||
			reactiveRigidbody is not null )
		{
			return;
		}

		var collider = GameObject.Components.Get<Collider>(
			FindMode.EverythingInSelfAndDescendants );

		if ( collider is null )
			return;

		reactiveRigidbody = GameObject.Components.Create<Rigidbody>();
	}

	private void TryInitializeAuthoredState()
	{
		if ( hasCapturedAuthoredState )
			return;

		ResolveReactiveParts();
		EnsureDirectModelPhysics();

		if ( Networking.IsHost &&
			GameObject.Network.Active &&
			GameObject.Network.IsProxy )
		{
			GameObject.Network.DropOwnership();
			return;
		}

		var body = reactiveRigidbody?.PhysicsBody;

		if ( Behavior != LargeLadGroundSlamPropBehavior.Break &&
			(body is null || body.ShapeCount <= 0) )
			return;

		if ( body is not null &&
			Networking.IsHost &&
			GameObject.Network.IsProxy == false )
		{
			// A mapper can start before its Collider has promoted the temporary
			// keyframed shell into the real simulated body. Establish the intended
			// runtime type before capturing the reset state.
			body.BodyType = PhysicsBodyType.Dynamic;

			if ( body.Mass <= 0.001f )
				return;
		}

		if ( (StartFrozen ||
				Behavior == LargeLadGroundSlamPropBehavior.Unanchor) &&
			body is not null )
		{
			body.MotionEnabled = false;
			body.Velocity = Vector3.Zero;
			body.AngularVelocity = Vector3.Zero;
			body.Sleeping = true;
		}

		CaptureAuthoredState();
		RefreshPresentation();

		if ( EnableDebugLogging &&
			Networking.IsHost &&
			Scene?.IsEditor == false )
		{
			Log.Info(
				$"[Debug/Ground Slam Prop] Initialized '{GameObject.Name}': " +
				$"network {GameObject.NetworkMode}, behavior {Behavior}, start " +
				$"frozen {StartFrozen}, body type {body?.BodyType}, motion " +
				$"{body?.MotionEnabled}, shapes {body?.ShapeCount}, mass " +
				$"{body?.Mass:0.###}, network active " +
				$"{GameObject.Network.Active}, owner " +
				$"{GameObject.Network.IsOwner}, proxy " +
				$"{GameObject.Network.IsProxy}." );
		}
	}

	private GameObject GetReactiveRoot()
	{
		return ReactiveRoot is not null && ReactiveRoot.IsValid
			? ReactiveRoot
			: GameObject;
	}

	private void ResolveReactiveParts()
	{
		var root = GetReactiveRoot();

		if ( root is null )
			return;

		reactiveProp = root.Components.Get<Prop>(
			FindMode.EverythingInSelfAndDescendants );
		Rigidbody bestRigidbody = null;
		var bestScore = int.MinValue;

		foreach ( var candidate in root.Components.GetAll<Rigidbody>(
			FindMode.EverythingInSelfAndDescendants ) )
		{
			if ( candidate is null || !candidate.IsValid || !candidate.Enabled )
				continue;

			var body = candidate.PhysicsBody;
			var score = body is null
				? 0
				: body.ShapeCount * 1000 +
					(body.Mass > 0.001f ? 100 : 0) +
					(body.BodyType == PhysicsBodyType.Dynamic ? 10 : 0);

			if ( score <= bestScore )
				continue;

			bestScore = score;
			bestRigidbody = candidate;
		}

		reactiveRigidbody = bestRigidbody;
	}

	private void CaptureAuthoredState()
	{
		if ( hasCapturedAuthoredState )
			return;

		ResolveReactiveParts();
		var root = GetReactiveRoot();

		if ( root is null )
			return;

		authoredLocalTransform = root.LocalTransform;
		authoredWorldPosition = root.WorldPosition;
		authoredRootEnabled = root.Enabled;
		authoredRootIsStatic = root.IsStatic;
		authoredPropIsStatic = reactiveProp?.IsStatic ?? false;
		authoredPropStartAsleep = reactiveProp?.StartAsleep ?? false;
		authoredRigidbodyEnabled = reactiveRigidbody?.Enabled == true;
		var body = reactiveRigidbody?.PhysicsBody;
		authoredHasPhysicsBody = body is not null;
		authoredBodyType = body?.BodyType ?? default;
		authoredMotionEnabled = body?.MotionEnabled ?? false;
		authoredSleeping = body?.Sleeping ?? true;

		foreach ( var component in root.Components.GetAll(
			FindMode.EverythingInSelfAndDescendants ) )
		{
			if ( component is null || component == this )
				continue;

			authoredComponentStates[component] =
				new AuthoredComponentState
				{
					Target = component,
					Enabled = component.Enabled
				};
		}

		hasCapturedAuthoredState = true;
	}

	private void RestoreAuthoredState()
	{
		if ( !hasCapturedAuthoredState )
			return;

		var root = GetReactiveRoot();

		if ( root is null || !root.IsValid )
			return;

		root.Enabled = true;
		root.IsStatic = authoredRootIsStatic;
		root.LocalTransform = authoredLocalTransform;
		root.Network.ClearInterpolation();

		if ( reactiveProp is not null && reactiveProp.IsValid )
		{
			reactiveProp.IsStatic = authoredPropIsStatic;
			reactiveProp.StartAsleep = authoredPropStartAsleep;
		}

		foreach ( var state in authoredComponentStates.Values )
		{
			if ( state.Target is not null && state.Target.IsValid )
				state.Target.Enabled = state.Enabled;
		}

		if ( reactiveRigidbody is not null && reactiveRigidbody.IsValid )
		{
			reactiveRigidbody.Enabled = authoredRigidbodyEnabled;
			var body = reactiveRigidbody.PhysicsBody;

			if ( authoredHasPhysicsBody && body is not null )
			{
				body.BodyType = authoredBodyType;
				body.MotionEnabled = authoredMotionEnabled;
				body.Velocity = Vector3.Zero;
				body.AngularVelocity = Vector3.Zero;
				body.Sleeping = authoredSleeping;
			}
		}

		root.Enabled = authoredRootEnabled;
	}

	private bool IsCriticalGameplayObject()
	{
		var root = GetReactiveRoot();

		return HasInHierarchy<LargeLadPlayer>( root ) ||
			HasInHierarchy<LargeLadHealth>( root ) ||
			HasInHierarchy<LargeLadGameManager>( root ) ||
			HasInHierarchy<LargeLadSpawnAllocator>( root ) ||
			HasInHierarchy<LargeLadTeamSpawn>( root ) ||
			HasInHierarchy<LargeLadWeaponPickup>( root ) ||
			HasInHierarchy<LargeLadKillVolume>( root );
	}

	private bool IsAuthoritativeBlocker()
	{
		var root = GetReactiveRoot();

		return HasInHierarchy<LargeLadBarricade>( root ) ||
			HasInHierarchy<LargeLadMinionPassage>( root ) ||
			HasInHierarchy<LargeLadEatSmashable>( root );
	}

	private static bool HasInHierarchy<T>( GameObject target )
		where T : Component
	{
		return target is not null &&
			(target.Components.Get<T>(
				FindMode.EverythingInSelfAndAncestors ) is not null ||
				target.Components.Get<T>(
					FindMode.EverythingInSelfAndDescendants ) is not null);
	}

	private void OnBrokenChanged( bool oldValue, bool newValue )
	{
		RefreshPresentation();
	}

	private void OnCleanedUpChanged( bool oldValue, bool newValue )
	{
		RefreshPresentation();
	}

	private void RefreshPresentation()
	{
		if ( !hasCapturedAuthoredState )
			return;

		var root = GetReactiveRoot();

		if ( root is null || !root.IsValid )
			return;

		var active = !IsBroken && !IsCleanedUp;

		if ( root != GameObject )
		{
			root.Enabled = authoredRootEnabled && active;
			return;
		}

		foreach ( var state in authoredComponentStates.Values )
		{
			if ( state.Target is null || !state.Target.IsValid )
				continue;

			if ( state.Target is Renderer or Collider or Rigidbody or Prop or
				MeshComponent )
			{
				state.Target.Enabled = state.Enabled && active;
			}
		}
	}
}
