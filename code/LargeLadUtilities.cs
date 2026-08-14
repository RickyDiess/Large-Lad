using Sandbox;

/// <summary>
/// The focused utility slot currently supports exactly one item. Additions here
/// must remain single-slot utilities rather than becoming a generic container.
/// </summary>
public enum LargeLadUtilityId
{
	None,
	Dodgeball
}

/// <summary>
/// Presentation-only data for a utility item. Utilities remain separate from
/// firearm definitions and never acquire ammo, reload, or damage behavior.
/// </summary>
public sealed class LargeLadUtilityPresentationDefinition
{
	public LargeLadUtilityId Id { get; init; }
	public int FirstPersonSkeleton { get; init; }
	public bool FirstPersonTwoHanded { get; init; }
	public string FirstPersonHeldModelPath { get; init; }
	public string FirstPersonHeldAttachmentBone { get; init; }
	public Vector3 FirstPersonHeldPositionOffset { get; init; }
	public Angles FirstPersonHeldRotationOffset { get; init; }
	public float FirstPersonHeldModelScale { get; init; } = 1.0f;
	public string ThirdPersonWorldModelPath { get; init; }
	public Vector3 ThirdPersonModelPosition { get; init; }
	public Angles ThirdPersonModelRotation { get; init; }
	public float ThirdPersonModelScale { get; init; } = 1.0f;
}

public static class LargeLadUtilityPresentationCatalog
{
	private static readonly LargeLadUtilityPresentationDefinition Dodgeball =
		new()
		{
			Id = LargeLadUtilityId.Dodgeball,
			FirstPersonSkeleton = 0,
			FirstPersonTwoHanded = false,
			FirstPersonHeldModelPath = "models/dev/sphere.vmdl",
			FirstPersonHeldAttachmentBone = "hand_R",
			FirstPersonHeldPositionOffset = new Vector3( 7.0f, 0.0f, 0.0f ),
			FirstPersonHeldRotationOffset = Angles.Zero,
			FirstPersonHeldModelScale = 0.18f,
			ThirdPersonWorldModelPath = "models/dev/sphere.vmdl",
			// Keep the ball centered at the authored hold attachment. The old
			// twelve-unit translation visibly floated it beyond the hand.
			ThirdPersonModelPosition = Vector3.Zero,
			ThirdPersonModelRotation = Angles.Zero,
			ThirdPersonModelScale = 0.5f
		};

	public static bool TryGet(
		LargeLadUtilityId utility,
		out LargeLadUtilityPresentationDefinition definition )
	{
		if ( utility == LargeLadUtilityId.Dodgeball )
		{
			definition = Dodgeball;
			return true;
		}

		definition = null;
		return false;
	}
}

/// <summary>
/// One host-authored, synchronized utility state. It intentionally contains no
/// ammunition, reserve, reload, or firearm-pickup data.
/// </summary>
public struct LargeLadUtilityState : System.IEquatable<LargeLadUtilityState>
{
	public LargeLadUtilityId Utility { get; set; }
	public int InstanceId { get; set; }

	public bool IsOwned => LargeLadUtilityRules.IsValidState( this );

	public static LargeLadUtilityState CreateDodgeball( int instanceId )
	{
		return new LargeLadUtilityState
		{
			Utility = LargeLadUtilityId.Dodgeball,
			InstanceId = System.Math.Max( 0, instanceId )
		};
	}

	public bool Equals( LargeLadUtilityState other )
	{
		return Utility == other.Utility && InstanceId == other.InstanceId;
	}

	public override bool Equals( object obj )
	{
		return obj is LargeLadUtilityState other && Equals( other );
	}

	public override int GetHashCode()
	{
		return System.HashCode.Combine( Utility, InstanceId );
	}

	public static bool operator ==(
		LargeLadUtilityState left,
		LargeLadUtilityState right )
	{
		return left.Equals( right );
	}

	public static bool operator !=(
		LargeLadUtilityState left,
		LargeLadUtilityState right )
	{
		return !left.Equals( right );
	}
}

public static class LargeLadUtilityRules
{
	public const string DodgeballDisplayName = "Dodgeball";
	public static Color DodgeballColor =>
		new( 0.94f, 0.28f, 0.18f );

	public static bool IsSupported( LargeLadUtilityId utility )
	{
		return utility == LargeLadUtilityId.Dodgeball;
	}

	public static bool IsValidState( LargeLadUtilityState state )
	{
		return IsSupported( state.Utility ) && state.InstanceId > 0;
	}

	public static bool CanUseUtility( LargeLadRole role, bool isDead )
	{
		return role == LargeLadRole.SkinnyKid && !isDead;
	}

	public static bool CanAccept(
		LargeLadRole role,
		bool isDead,
		bool alreadyHasUtility,
		bool pickupAvailable,
		LargeLadUtilityState state )
	{
		return CanUseUtility( role, isDead ) &&
			!alreadyHasUtility &&
			pickupAvailable &&
			IsValidState( state );
	}

	public static bool CanSelect(
		bool isHost,
		bool ownerRequest,
		LargeLadRole role,
		bool isDead,
		LargeLadUtilityState state )
	{
		return isHost &&
			ownerRequest &&
			CanUseUtility( role, isDead ) &&
			IsValidState( state );
	}

	public static bool CanDrop(
		bool isHost,
		bool ownerRequest,
		LargeLadRole role,
		bool isDead,
		LargeLadUtilityState state,
		LargeLadInventorySelection activeSelection )
	{
		return CanSelect(
				isHost,
				ownerRequest,
				role,
				isDead,
				state ) &&
			activeSelection == SelectionFor( state );
	}

	public static LargeLadInventorySelection SelectionFor(
		LargeLadUtilityState state )
	{
		return IsValidState( state )
			? LargeLadInventorySelection.ForUtility(
				state.Utility,
				state.InstanceId )
			: LargeLadInventorySelection.None;
	}

	public static string GetDisplayName( LargeLadUtilityId utility )
	{
		return utility == LargeLadUtilityId.Dodgeball
			? DodgeballDisplayName
			: "No Utility";
	}

	public static Color GetColor( LargeLadUtilityId utility )
	{
		return utility == LargeLadUtilityId.Dodgeball
			? DodgeballColor
			: Color.Gray;
	}
}

public enum LargeLadUtilityLocation
{
	OriginAvailable,
	Carried,
	Dropped,
	Thrown
}

/// <summary>
/// Host-only lifecycle for one authored dodgeball utility instance.
/// </summary>
public sealed class LargeLadUtilityInstance
{
	private int nextThrowSequence;

	public LargeLadUtilityInstance( int instanceId )
	{
		InstanceId = System.Math.Max( 0, instanceId );
		ResetForRound();
	}

	public int InstanceId { get; }
	public LargeLadUtilityLocation Location { get; private set; }
	public object Carrier { get; private set; }
	public LargeLadUtilityState State { get; private set; }
	public int ActiveThrowSequence { get; private set; }

	public bool TryCollectFromOrigin(
		object carrier,
		out LargeLadUtilityState state )
	{
		state = State;

		if ( carrier is null ||
			Location != LargeLadUtilityLocation.OriginAvailable )
		{
			return false;
		}

		Carrier = carrier;
		Location = LargeLadUtilityLocation.Carried;
		return true;
	}

	public bool TryCollectDropped(
		object carrier,
		out LargeLadUtilityState state )
	{
		state = State;

		if ( carrier is null ||
			Location is not (LargeLadUtilityLocation.Dropped or
				LargeLadUtilityLocation.Thrown) )
		{
			return false;
		}

		Carrier = carrier;
		Location = LargeLadUtilityLocation.Carried;
		ActiveThrowSequence = 0;
		return true;
	}

	public bool TryDrop( object carrier, LargeLadUtilityState state )
	{
		if ( Location != LargeLadUtilityLocation.Carried ||
			!ReferenceEquals( Carrier, carrier ) ||
			!MatchesIdentity( state ) )
		{
			return false;
		}

		Carrier = null;
		Location = LargeLadUtilityLocation.Dropped;
		ActiveThrowSequence = 0;
		return true;
	}

	public bool TryThrow(
		object carrier,
		LargeLadUtilityState state,
		out int throwSequence )
	{
		throwSequence = 0;

		if ( Location != LargeLadUtilityLocation.Carried ||
			!ReferenceEquals( Carrier, carrier ) ||
			!MatchesIdentity( state ) )
		{
			return false;
		}

		nextThrowSequence = nextThrowSequence == int.MaxValue
			? 1
			: nextThrowSequence + 1;
		throwSequence = nextThrowSequence;
		ActiveThrowSequence = throwSequence;
		Carrier = null;
		Location = LargeLadUtilityLocation.Thrown;
		return true;
	}

	public bool TrySettleThrow( int throwSequence )
	{
		if ( throwSequence <= 0 ||
			Location != LargeLadUtilityLocation.Thrown ||
			ActiveThrowSequence != throwSequence )
		{
			return false;
		}

		Location = LargeLadUtilityLocation.Dropped;
		ActiveThrowSequence = 0;
		return true;
	}

	public bool ReturnCarrierToOrigin(
		object carrier,
		LargeLadUtilityState state )
	{
		if ( Location != LargeLadUtilityLocation.Carried ||
			!ReferenceEquals( Carrier, carrier ) ||
			!MatchesIdentity( state ) )
		{
			return false;
		}

		Carrier = null;
		Location = LargeLadUtilityLocation.OriginAvailable;
		ActiveThrowSequence = 0;
		return true;
	}

	public bool ForceReturnToOrigin( LargeLadUtilityState state )
	{
		if ( !MatchesIdentity( state ) )
			return false;

		Carrier = null;
		Location = LargeLadUtilityLocation.OriginAvailable;
		ActiveThrowSequence = 0;
		return true;
	}

	public void ResetForRound()
	{
		State = LargeLadUtilityState.CreateDodgeball( InstanceId );
		Carrier = null;
		Location = LargeLadUtilityLocation.OriginAvailable;
		ActiveThrowSequence = 0;
	}

	private bool MatchesIdentity( LargeLadUtilityState state )
	{
		return LargeLadUtilityRules.IsValidState( state ) &&
			state.Utility == LargeLadUtilityId.Dodgeball &&
			state.InstanceId == InstanceId;
	}
}
