using Sandbox;

/// <summary>
/// One authored firearm pickup. Core placements are permanent unlock points
/// available independently to every Skinny Kid. Exclusive placements own one
/// persistent native firearm for the match and remain unavailable while that
/// same item is carried or dropped elsewhere.
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
	private LargeLadFirearm exclusiveInstance;

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
		else
		{
			foreach ( var warning in
				LargeLadWeaponCatalog.Get( Weapon ).GetValidationWarnings() )
			{
				Log.Warning(
					$"{GameObject.Name}: weapon definition '{Weapon}': " +
					warning );
			}
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

		var inventory = player.NativeInventory;

		if ( inventory is null )
			return;

		if ( PickupPolicy == LargeLadPickupPolicy.Core )
		{
			// Duplicate ownership is rejected by the inventory. The authored
			// pickup remains visible and available in either outcome.
			if ( inventory.TryGrantCoreFirearm( Weapon ) )
			{
				LogPickupDebug(
					$"{player.GameObject.Name} collected core {Weapon} from " +
					$"'{GameObject.Name}'." );
			}

			return;
		}

		if ( inventory.HasExclusiveFirearm )
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
		LargeLadNativeInventory carrier,
		LargeLadFirearm firearm,
		Vector3 nearPosition,
		Vector3 forward )
	{
		if ( !Networking.IsHost ||
			PickupPolicy != LargeLadPickupPolicy.Exclusive ||
			carrier is null ||
			!carrier.IsValid ||
			firearm?.Inventory != carrier ||
			!MatchesExclusiveInstance( firearm ) )
		{
			return false;
		}

		if ( !LargeLadDropPlacement.TryFind(
				Scene,
				carrier.GameObject,
				nearPosition,
				forward,
				out var worldPosition ) )
		{
			return false;
		}

		return DropFromCarrierAt(
			carrier,
			firearm,
			new Transform( worldPosition, Rotation.Identity ) );
	}

	internal bool ReturnCarrierToOrigin(
		LargeLadNativeInventory carrier,
		LargeLadFirearm firearm )
	{
		if ( !Networking.IsHost ||
			PickupPolicy != LargeLadPickupPolicy.Exclusive ||
			carrier is null ||
			!carrier.IsValid ||
			firearm?.Inventory != carrier ||
			!MatchesExclusiveInstance( firearm ) )
		{
			return false;
		}

		if ( !DropFromCarrierAt( carrier, firearm, authoredTransform ) )
			return false;

		PlaceExclusiveAtOrigin( firearm, restoreStartingAmmunition: false );
		return true;
	}

	internal void ReleaseCarrierForRoundReset(
		LargeLadNativeInventory carrier )
	{
		var firearm = ResolveExclusiveInstance();

		if ( !Networking.IsHost || firearm?.Inventory != carrier )
		{
			return;
		}

		ReturnCarrierToOrigin( carrier, firearm );
	}

	internal void NotifyExclusivePickedUp(
		LargeLadFirearm firearm,
		LargeLadNativeInventory inventory )
	{
		if ( !Networking.IsHost ||
			PickupPolicy != LargeLadPickupPolicy.Exclusive ||
			inventory is null ||
			!inventory.IsValid ||
			firearm?.Inventory != inventory ||
			!MatchesExclusiveInstance( firearm ) )
		{
			return;
		}

		exclusiveInstance = firearm;
		SetAvailable( false );
		LogPickupDebug(
			$"{inventory.GameObject.Name} collected native exclusive {Weapon} " +
			$"from '{GameObject.Name}'." );
	}

	internal void HandleExclusiveInstanceDestroyed( LargeLadFirearm firearm )
	{
		if ( !Networking.IsHost || firearm != exclusiveInstance )
			return;

		exclusiveInstance = null;
		SetAvailable( false );
	}

	public override void ResetForRound()
	{
		if ( !Networking.IsHost )
			return;

		EnsureExclusiveInstance();

		var firearm = ResolveExclusiveInstance();
		if ( firearm?.Inventory is LargeLadNativeInventory carrier )
			ReturnCarrierToOrigin( carrier, firearm );

		if ( firearm is not null && firearm.Inventory is null )
			PlaceExclusiveAtOrigin( firearm, restoreStartingAmmunition: true );
		else
			SetAvailable( false );

		LogRoundResetDebug(
			$"Reset pickup '{GameObject.Name}' ({PickupPolicy} {Weapon})." );
	}

	private void TryCollectExclusiveFromOrigin(
		LargeLadNativeInventory inventory )
	{
		if ( !Available ||
			PickupPolicy != LargeLadPickupPolicy.Exclusive )
		{
			return;
		}

		EnsureExclusiveInstance();
		var firearm = ResolveExclusiveInstance();

		if ( firearm is null || firearm.Inventory is not null )
			return;

		inventory.PickupWorldItem( firearm );

		if ( firearm.Inventory == inventory )
			NotifyExclusivePickedUp( firearm, inventory );
	}

	private void LogPickupDebug( string message )
	{
		if ( LargeLadGameManager.FindForScene( Scene )?
			.EnablePickupAndRoundResetDebugLogging == true )
		{
			Log.Info( $"[Debug/Pickup] {message}" );
		}
	}

	private void LogRoundResetDebug( string message )
	{
		if ( LargeLadGameManager.FindForScene( Scene )?
			.EnablePickupAndRoundResetDebugLogging == true )
		{
			Log.Info( $"[Debug/Round Reset] {message}" );
		}
	}

	private bool DropFromCarrierAt(
		LargeLadNativeInventory carrier,
		LargeLadFirearm firearm,
		Transform worldTransform )
	{
		firearm.PrepareExclusiveWorldDrop( worldTransform );
		carrier.Drop( firearm );

		if ( firearm.Inventory is not null )
		{
			firearm.CancelExclusiveWorldDrop();
			return false;
		}

		exclusiveInstance = firearm;
		SetAvailable( false );
		return true;
	}

	private void EnsureExclusiveInstance()
	{
		if ( PickupPolicy != LargeLadPickupPolicy.Exclusive )
			return;

		ExclusiveInstanceId = ExclusiveInstanceId > 0
			? ExclusiveInstanceId
			: CreateStableInstanceId();

		if ( ResolveExclusiveInstance() is not null )
			return;

		if ( !LargeLadWeaponCatalog.TryGetFirearm(
			Weapon,
			out var definition ) ||
			string.IsNullOrWhiteSpace( definition.NativePrefabPath ) )
		{
			Log.Warning(
				$"{GameObject.Name}: no native prefab route exists for " +
				$"'{Weapon}'." );
			return;
		}

		var spawnTransform = hasAuthoredTransform
			? authoredTransform
			: GameObject.WorldTransform;
		var instanceObject = GameObject.Clone(
			definition.NativePrefabPath,
			new CloneConfig
			{
				Transform = spawnTransform,
				StartEnabled = true
			} );
		var firearm = instanceObject?.Components.Get<LargeLadFirearm>();

		if ( firearm is null ||
			!firearm.InitializeExclusiveState( ExclusiveInstanceId ) )
		{
			instanceObject?.Destroy();
			Log.Warning(
				$"{GameObject.Name}: failed to create its native Exclusive " +
				$"'{Weapon}' instance." );
			return;
		}

		instanceObject.Name =
			$"Exclusive {definition.DisplayName} #{ExclusiveInstanceId}";
		exclusiveInstance = firearm;
		firearm.PlaceExclusiveWorldItem( spawnTransform );

		try
		{
			instanceObject.NetworkSpawn();
		}
		catch ( System.Exception exception )
		{
			exclusiveInstance = null;
			instanceObject.Destroy();
			SetAvailable( false );
			Log.Error(
				$"{GameObject.Name}: failed to network-spawn native " +
				$"Exclusive weapon: {exception.Message}" );
			return;
		}

		SetAvailable( true );
	}

	private LargeLadFirearm ResolveExclusiveInstance()
	{
		if ( MatchesExclusiveInstance( exclusiveInstance ) )
			return exclusiveInstance;

		exclusiveInstance = Scene?
			.GetAllComponents<LargeLadFirearm>()
			.FirstOrDefault( MatchesExclusiveInstance );
		return exclusiveInstance;
	}

	private bool MatchesExclusiveInstance( LargeLadFirearm firearm )
	{
		return firearm is not null &&
			firearm.IsValid &&
			firearm.IsExclusive &&
			firearm.WeaponId == Weapon &&
			firearm.ExclusiveInstanceId > 0 &&
			firearm.ExclusiveInstanceId == ExclusiveInstanceId;
	}

	private void PlaceExclusiveAtOrigin(
		LargeLadFirearm firearm,
		bool restoreStartingAmmunition )
	{
		if ( firearm is null || !firearm.IsValid || firearm.Inventory is not null )
			return;

		if ( restoreStartingAmmunition )
			firearm.ResetExclusiveAmmunition();

		RestoreAuthoredTransform();
		firearm.PlaceExclusiveWorldItem( authoredTransform );
		exclusiveInstance = firearm;
		SetAvailable( true );
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
		// The authored object remains the stable origin/reset marker for an
		// Exclusive. Its one native firearm supplies the world presentation and
		// pickup trigger, including while it is resting at this origin.
		var shown = PickupPolicy == LargeLadPickupPolicy.Core;

		if ( PickupRenderer is not null )
			PickupRenderer.Enabled = shown;

		if ( PickupCollider is not null )
			PickupCollider.Enabled = shown;
	}

	protected override void DrawGizmos()
	{
		var color = LargeLadWeaponCatalog.Get( Weapon ).AccentColor;
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
