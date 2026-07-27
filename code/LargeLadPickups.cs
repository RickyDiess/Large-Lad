using Sandbox;

/// <summary>
/// One authored firearm pickup. Core placements are permanent unlock points
/// available independently to every Skinny Kid. Exclusive placements own one
/// persistent physical instance for the match and remain hidden while that
/// instance is carried or represented by a dropped runtime object.
/// </summary>
public sealed class LargeLadWeaponPickup : LargeLadRoundResettableComponent,
	Component.ITriggerListener
{
	[Property]
	public LargeLadWeaponId Weapon { get; set; } =
		LargeLadWeaponId.Pistol;

	[Property, Title( "Pickup Policy (Per Instance)" )]
	public LargeLadPickupPolicy PickupPolicy { get; set; } =
		LargeLadPickupPolicy.Core;

	[Property]
	public Renderer PickupRenderer { get; set; }

	[Property]
	public Collider PickupCollider { get; set; }

	[Sync( SyncFlags.FromHost ), Change( nameof( OnAvailableChanged ) )]
	public bool Available { get; private set; } = true;

	[Sync( SyncFlags.FromHost )]
	public int ExclusiveInstanceId { get; private set; }

	private Transform authoredTransform;
	private bool hasAuthoredTransform;
	private LargeLadExclusiveInstance exclusiveInstance;
	private LargeLadDroppedExclusiveWeapon droppedInstance;

	protected override void OnAwake()
	{
		ResolveAuthoredParts();
	}

	protected override void OnStart()
	{
		ResolveAuthoredParts();
		authoredTransform = GameObject.WorldTransform;
		hasAuthoredTransform = true;

		if ( PickupCollider is not null )
			PickupCollider.IsTrigger = true;

		if ( Networking.IsHost )
			EnsureExclusiveInstance();

		ApplyAvailableState();
	}

	protected override void OnValidate()
	{
		ResolveAuthoredParts();

		if ( !LargeLadWeaponCatalog.IsFirearm( Weapon ) )
		{
			Log.Warning(
				$"{GameObject.Name}: weapon pickup must use a firearm definition." );
		}

		if ( !System.Enum.IsDefined(
			typeof( LargeLadPickupPolicy ),
			PickupPolicy ) )
		{
			Log.Warning(
				$"{GameObject.Name}: weapon pickup needs a valid pickup policy." );
		}

		if ( PickupPolicy == LargeLadPickupPolicy.Exclusive &&
			GameObject.NetworkMode != NetworkMode.Object )
		{
			Log.Warning(
				$"{GameObject.Name}: an exclusive weapon pickup must use " +
				"Network Mode Object." );
		}
	}

	public void OnTriggerEnter( Collider other )
	{
		if ( !Networking.IsHost )
			return;

		var player = other?.GameObject?.Components.Get<LargeLadPlayer>(
			FindMode.EverythingInSelfAndAncestors );

		if ( player?.Role != LargeLadRole.SkinnyKid ||
			player.Health?.IsDead != false )
		{
			return;
		}

		var inventory = player.Inventory;

		if ( inventory is null )
			return;

		if ( PickupPolicy == LargeLadPickupPolicy.Core )
		{
			// Duplicate ownership is rejected by the inventory. The authored
			// pickup remains visible and available in either outcome.
			inventory.TryGrantCoreWeapon( Weapon );
			return;
		}

		if ( inventory.HasExclusiveWeapon )
		{
			inventory.NotifyExclusiveSlotFull();
			return;
		}

		TryCollectExclusiveFromOrigin( inventory );
	}

	public void OnTriggerExit( Collider other )
	{
	}

	internal void EnsureExclusiveIdentityForHost()
	{
		if ( Networking.IsHost )
			EnsureExclusiveInstance();
	}

	internal bool TryDropFromCarrier(
		LargeLadInventory carrier,
		LargeLadWeaponState state,
		Vector3 nearPosition,
		Vector3 forward,
		out LargeLadDroppedExclusiveWeapon dropped )
	{
		dropped = null;

		if ( !Networking.IsHost ||
			PickupPolicy != LargeLadPickupPolicy.Exclusive ||
			carrier is null ||
			!carrier.IsValid ||
			!LargeLadInventoryRules.IsValidExclusiveState( state ) )
		{
			return false;
		}

		EnsureExclusiveInstance();

		if ( exclusiveInstance is null ||
			!LargeLadDropPlacement.TryFind(
				Scene,
				carrier.GameObject,
				nearPosition,
				forward,
				out var worldPosition ) )
		{
			return false;
		}

		var runtime = CreateDroppedRuntime( state, worldPosition );

		if ( runtime is null ||
			!exclusiveInstance.TryDrop( carrier, state ) )
		{
			runtime?.GameObject?.Destroy();
			return false;
		}

		droppedInstance = runtime;
		SetAvailable( false );

		try
		{
			runtime.GameObject.NetworkSpawn();
		}
		catch ( System.Exception exception )
		{
			droppedInstance = null;
			exclusiveInstance.TryCollectDropped( carrier, out _ );
			runtime.GameObject.Destroy();
			Log.Error(
				$"{GameObject.Name}: failed to network-spawn dropped " +
				$"exclusive weapon: {exception.Message}" );
			return false;
		}

		dropped = runtime;
		return true;
	}

	internal bool ReturnCarrierToOrigin(
		LargeLadInventory carrier,
		LargeLadWeaponState state )
	{
		if ( !Networking.IsHost ||
			PickupPolicy != LargeLadPickupPolicy.Exclusive )
		{
			return false;
		}

		EnsureExclusiveInstance();

		if ( exclusiveInstance is null )
		{
			return false;
		}

		var returned = exclusiveInstance.ReturnCarrierToOrigin(
			carrier,
			state );

		if ( !returned )
			returned = exclusiveInstance.ForceReturnToOrigin( state );

		if ( !returned )
			return false;

		if ( droppedInstance is not null &&
			droppedInstance.IsValid )
		{
			var runtime = droppedInstance;
			droppedInstance = null;
			runtime.GameObject.Destroy();
		}

		RestoreAuthoredTransform();
		SetAvailable( true );
		return true;
	}

	internal void ReleaseCarrierForRoundReset(
		LargeLadInventory carrier )
	{
		if ( !Networking.IsHost ||
			exclusiveInstance is null ||
			exclusiveInstance.Location != LargeLadExclusiveLocation.Carried ||
			!ReferenceEquals( exclusiveInstance.Carrier, carrier ) )
		{
			return;
		}

		exclusiveInstance.ReturnCarrierToOrigin(
			carrier,
			exclusiveInstance.State );
	}

	internal void TryCollectDropped(
		LargeLadPlayer player,
		LargeLadDroppedExclusiveWeapon dropped )
	{
		if ( !Networking.IsHost ||
			PickupPolicy != LargeLadPickupPolicy.Exclusive ||
			player?.Role != LargeLadRole.SkinnyKid ||
			player.Health?.IsDead != false ||
			dropped is null ||
			!dropped.IsValid ||
			dropped != droppedInstance )
		{
			return;
		}

		var inventory = player.Inventory;

		if ( inventory is null )
			return;

		if ( inventory.HasExclusiveWeapon )
		{
			inventory.NotifyExclusiveSlotFull();
			return;
		}

		EnsureExclusiveInstance();
		var pendingState = exclusiveInstance?.State ?? default;

		if ( exclusiveInstance is null ||
			!inventory.CanAcceptExclusive(
				this,
				pendingState,
				pickupAvailable: true ) ||
			!exclusiveInstance.TryCollectDropped(
				inventory,
				out var state ) )
		{
			return;
		}

		if ( !inventory.TryGrantExclusiveWeapon( this, state ) )
		{
			exclusiveInstance.RestoreDroppedAfterRejectedTransfer();
			return;
		}

		droppedInstance = null;
		dropped.GameObject.Destroy();
	}

	internal void HandleDroppedDestroyed(
		LargeLadDroppedExclusiveWeapon dropped )
	{
		if ( !Networking.IsHost || dropped != droppedInstance )
			return;

		droppedInstance = null;

		if ( exclusiveInstance?.ReturnDroppedToOrigin() == true )
		{
			RestoreAuthoredTransform();
			SetAvailable( true );
		}
	}

	public override void ResetForRound()
	{
		if ( !Networking.IsHost )
			return;

		if ( droppedInstance is not null &&
			droppedInstance.IsValid )
		{
			var runtime = droppedInstance;
			droppedInstance = null;
			runtime.GameObject.Destroy();
		}

		EnsureExclusiveInstance();
		exclusiveInstance?.ResetForRound();
		RestoreAuthoredTransform();
		SetAvailable( true );
	}

	private void TryCollectExclusiveFromOrigin(
		LargeLadInventory inventory )
	{
		if ( !Available ||
			PickupPolicy != LargeLadPickupPolicy.Exclusive )
		{
			return;
		}

		EnsureExclusiveInstance();
		var pendingState = exclusiveInstance?.State ?? default;

		if ( exclusiveInstance is null ||
			!inventory.CanAcceptExclusive(
				this,
				pendingState,
				pickupAvailable: Available ) ||
			!exclusiveInstance.TryCollectFromOrigin(
				inventory,
				out var state ) )
		{
			return;
		}

		if ( !inventory.TryGrantExclusiveWeapon( this, state ) )
		{
			exclusiveInstance.ReturnCarrierToOrigin(
				inventory,
				state );
			return;
		}

		SetAvailable( false );
	}

	private LargeLadDroppedExclusiveWeapon CreateDroppedRuntime(
		LargeLadWeaponState state,
		Vector3 worldPosition )
	{
		var definition = LargeLadWeaponCatalog.Get( state.Weapon );
		var worldModel = string.IsNullOrWhiteSpace(
			definition.WorldModelPath )
			? null
			: Model.Load( definition.WorldModelPath );

		if ( worldModel is null )
			return null;

		var droppedObject = Scene.CreateObject();
		droppedObject.Name =
			$"Dropped Exclusive {definition.DisplayName} " +
			$"#{state.ExclusiveInstanceId}";
		droppedObject.NetworkMode = NetworkMode.Object;
		droppedObject.WorldPosition = worldPosition;
		droppedObject.WorldRotation = Rotation.Identity;
		droppedObject.Tags.Add( "pickup" );

		var renderer =
			droppedObject.Components.Create<ModelRenderer>();
		renderer.Model = worldModel;
		renderer.Tint = definition.PickupColor;

		var collider =
			droppedObject.Components.Create<BoxCollider>();
		collider.Center = Vector3.Up * 9.0f;
		collider.Scale = new Vector3( 44.0f, 44.0f, 18.0f );
		collider.IsTrigger = true;
		collider.Static = true;

		var dropped =
			droppedObject.Components
				.Create<LargeLadDroppedExclusiveWeapon>();
		dropped.Initialize( this, state, collider, renderer );
		return dropped;
	}

	private void EnsureExclusiveInstance()
	{
		if ( PickupPolicy != LargeLadPickupPolicy.Exclusive ||
			exclusiveInstance is not null )
		{
			return;
		}

		var definition = LargeLadWeaponCatalog.Get( Weapon );
		var instanceId = CreateStableInstanceId();
		ExclusiveInstanceId = instanceId;
		exclusiveInstance = new LargeLadExclusiveInstance(
			instanceId,
			Weapon,
			definition.MagazineSize,
			definition.StartingReserve );
	}

	private int CreateStableInstanceId()
	{
		var hash = System.HashCode.Combine(
			GameObject.Id,
			Id ) & int.MaxValue;
		return hash == 0 ? 1 : hash;
	}

	private void ResolveAuthoredParts()
	{
		PickupCollider ??= Components.Get<Collider>();
		PickupRenderer ??= Components.Get<Renderer>(
			FindMode.EverythingInSelfAndDescendants );
	}

	private void RestoreAuthoredTransform()
	{
		if ( !hasAuthoredTransform )
			return;

		GameObject.WorldTransform = authoredTransform;
		GameObject.Network.ClearInterpolation();
	}

	private void SetAvailable( bool available )
	{
		Available = PickupPolicy == LargeLadPickupPolicy.Core || available;
		ApplyAvailableState();
	}

	private void OnAvailableChanged( bool oldValue, bool newValue )
	{
		ApplyAvailableState();
	}

	private void ApplyAvailableState()
	{
		var shown = PickupPolicy == LargeLadPickupPolicy.Core || Available;

		if ( PickupRenderer is not null )
			PickupRenderer.Enabled = shown;

		if ( PickupCollider is not null )
			PickupCollider.Enabled = shown;
	}

	protected override void DrawGizmos()
	{
		var color = LargeLadWeaponCatalog.Get( Weapon ).PickupColor;
		var alpha = PickupPolicy == LargeLadPickupPolicy.Exclusive
			? 0.7f
			: 0.45f;
		Gizmo.Draw.Color = color.WithAlpha( alpha );
		Gizmo.Draw.SolidBox(
			new BBox(
				new Vector3( -12.0f ),
				new Vector3( 12.0f ) ) );
	}
}

/// <summary>
/// Runtime world representation of an authored exclusive instance. It never
/// owns/reset ammunition; its source pickup remains the durable host authority.
/// </summary>
public sealed class LargeLadDroppedExclusiveWeapon : Component,
	Component.ITriggerListener
{
	[Property]
	public Collider PickupCollider { get; private set; }

	[Property]
	public Renderer PickupRenderer { get; private set; }

	[Sync( SyncFlags.FromHost )]
	public LargeLadWeaponId Weapon { get; private set; }

	[Sync( SyncFlags.FromHost )]
	public int ExclusiveInstanceId { get; private set; }

	[Sync( SyncFlags.FromHost )]
	public int Magazine { get; private set; }

	[Sync( SyncFlags.FromHost )]
	public int Reserve { get; private set; }

	private LargeLadWeaponPickup source;

	internal void Initialize(
		LargeLadWeaponPickup source,
		LargeLadWeaponState state,
		Collider collider,
		Renderer renderer )
	{
		this.source = source;
		Weapon = state.Weapon;
		ExclusiveInstanceId = state.ExclusiveInstanceId;
		Magazine = state.Magazine;
		Reserve = state.Reserve;
		PickupCollider = collider;
		PickupRenderer = renderer;

		if ( PickupCollider is not null )
			PickupCollider.IsTrigger = true;
	}

	protected override void OnStart()
	{
		PickupCollider ??= Components.Get<Collider>();
		PickupRenderer ??= Components.Get<Renderer>();

		if ( PickupCollider is not null )
			PickupCollider.IsTrigger = true;
	}

	protected override void OnDestroy()
	{
		if ( Networking.IsHost )
			ResolveSource()?.HandleDroppedDestroyed( this );

		base.OnDestroy();
	}

	public void OnTriggerEnter( Collider other )
	{
		if ( !Networking.IsHost )
			return;

		var player = other?.GameObject?.Components.Get<LargeLadPlayer>(
			FindMode.EverythingInSelfAndAncestors );

		if ( player?.Role != LargeLadRole.SkinnyKid ||
			player.Health?.IsDead != false )
		{
			return;
		}

		if ( player.Inventory?.HasExclusiveWeapon == true )
		{
			player.Inventory.NotifyExclusiveSlotFull();
			return;
		}

		ResolveSource()?.TryCollectDropped( player, this );
	}

	public void OnTriggerExit( Collider other )
	{
	}

	private LargeLadWeaponPickup ResolveSource()
	{
		if ( source is not null &&
			source.IsValid &&
			source.ExclusiveInstanceId == ExclusiveInstanceId )
		{
			return source;
		}

		if ( !Networking.IsHost )
			return null;

		foreach ( var pickup in
			Scene.GetAllComponents<LargeLadWeaponPickup>() )
		{
			if ( pickup.PickupPolicy ==
					LargeLadPickupPolicy.Exclusive &&
				pickup.ExclusiveInstanceId ==
					ExclusiveInstanceId )
			{
				source = pickup;
				return source;
			}
		}

		return null;
	}
}

internal static class LargeLadDropPlacement
{
	private static readonly Vector2[] CandidateDirections =
	{
		new( 1.0f, 0.0f ),
		new( 1.0f, 0.65f ),
		new( 1.0f, -0.65f ),
		new( 0.0f, 1.0f ),
		new( 0.0f, -1.0f ),
		new( -0.65f, 0.0f )
	};

	public static bool TryFind(
		Scene scene,
		GameObject ignoredPlayer,
		Vector3 nearPosition,
		Vector3 forward,
		out Vector3 worldPosition )
	{
		worldPosition = default;

		if ( scene is null ||
			ignoredPlayer is null ||
			!ignoredPlayer.IsValid )
		{
			return false;
		}

		forward.z = 0.0f;
		forward = forward.LengthSquared > 0.001f
			? forward.Normal
			: Vector3.Forward;
		var right = Vector3.Cross( forward, Vector3.Up ).Normal;

		foreach ( var direction in CandidateDirections )
		{
			var offset =
				(forward * direction.x + right * direction.y).Normal *
				58.0f;
			var traceStart = nearPosition + offset + Vector3.Up * 42.0f;
			var traceEnd = traceStart - Vector3.Up * 112.0f;
			var ground = scene.Trace
				.Box(
					new Vector3( 13.0f, 13.0f, 8.0f ),
					traceStart,
					traceEnd )
				.IgnoreGameObjectHierarchy( ignoredPlayer )
				.WithoutTags( "player" )
				.Run();

			if ( !ground.Hit || ground.StartedSolid )
				continue;

			var candidate = ground.EndPosition + Vector3.Up * 2.0f;
			var clearance = scene.Trace
				.Box(
					new Vector3( 13.0f, 13.0f, 8.0f ),
					candidate,
					candidate )
				.IgnoreGameObjectHierarchy( ignoredPlayer )
				.WithoutTags( "player" )
				.Run();

			if ( clearance.Hit || clearance.StartedSolid )
				continue;

			worldPosition = candidate;
			return true;
		}

		return false;
	}
}
