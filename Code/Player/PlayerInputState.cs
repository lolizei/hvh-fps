using System;

namespace HvH;

/// <summary>
/// One frame of intent for a pawn: where it wants to move, where it wants to
/// look, and which buttons it is holding.
///
/// This is the seam between "who is driving" and "what the pawn does". Movement,
/// weapons and the inventory read this and never touch <c>Input</c> directly, so
/// a bot can drive the exact same code path a human does without the two ever
/// being able to consume each other's controls.
/// </summary>
public struct PlayerInputState
{
	/// <summary>Analog move in local space - x forward, y left. Unrotated.</summary>
	public Vector3 Move;

	/// <summary>Angles to add to the pawn's view this frame, sensitivity already applied.</summary>
	public Angles LookDelta;

	public bool JumpPressed;
	public bool RunDown;
	public bool DuckDown;

	public bool AttackDown;
	public bool AttackPressed;
	public bool ReloadPressed;

	/// <summary>Weapon slot requested this frame, or -1 for none.</summary>
	public int SlotRequest;

	public bool SlotNextPressed;
	public bool SlotPrevPressed;

	/// <summary>Intent that does nothing. What a pawn gets when nobody is driving it.</summary>
	public static PlayerInputState Idle => new() { SlotRequest = -1 };
}

/// <summary>
/// Supplies a pawn's intent each frame.
///
/// <see cref="HumanInputSource"/> reads the keyboard and mouse. A bot brain will
/// implement this in a later task to drive a pawn with no hardware involved.
/// </summary>
public interface IPlayerInputSource
{
	PlayerInputState BuildInput( Player player );
}

/// <summary>
/// Reads the real keyboard and mouse.
///
/// This is deliberately the ONLY place in gameplay code that touches
/// <c>Input</c>. If a second one ever appears, a bot can start eating the
/// human's controls again.
/// </summary>
public sealed class HumanInputSource : IPlayerInputSource
{
	/// <summary>Shared - it holds no per-pawn state.</summary>
	public static readonly HumanInputSource Instance = new();

	public PlayerInputState BuildInput( Player player )
	{
		var state = new PlayerInputState
		{
			Move = Input.AnalogMove,

			// AnalogLook already carries the engine's own sensitivity; ours is an
			// extra multiplier the player controls from the settings menu.
			LookDelta = Input.AnalogLook * GameSettings.Current.MouseSensitivity,

			JumpPressed = Input.Pressed( "Jump" ),
			RunDown = Input.Down( "Run" ),
			DuckDown = Input.Down( "Duck" ),

			AttackDown = Input.Down( "Attack1" ),
			AttackPressed = Input.Pressed( "Attack1" ),
			ReloadPressed = Input.Pressed( "Reload" ),

			SlotRequest = -1,
			SlotNextPressed = Input.Pressed( "SlotNext" ),
			SlotPrevPressed = Input.Pressed( "SlotPrev" ),
		};

		for ( var i = 0; i < 9; i++ )
		{
			if ( !Input.Pressed( $"Slot{i + 1}" ) ) continue;

			state.SlotRequest = i;
			break;
		}

		return state;
	}
}
