using System;

namespace HvH;

/// <summary>
/// Source-style ground/air movement built on <see cref="CharacterController"/>.
/// Every tuning value is a property rather than a constant, because the Phase 8
/// mod framework needs to read and modify these at runtime without touching
/// this class.
/// </summary>
public sealed class PlayerMovement : Component
{
	[Property] public float WalkSpeed { get; set; } = 150f;
	[Property] public float RunSpeed { get; set; } = 260f;
	[Property] public float CrouchSpeed { get; set; } = 90f;

	[Property] public float Gravity { get; set; } = 800f;
	[Property] public float JumpPower { get; set; } = 300f;

	[Property] public float GroundAcceleration { get; set; } = 10f;
	[Property] public float AirAcceleration { get; set; } = 35f;

	/// <summary>How much of the wish velocity actually counts while airborne.
	/// Low values are what make strafe jumping a skill instead of free speed.</summary>
	[Property] public float AirSpeedCap { get; set; } = 30f;

	[Property] public float Friction { get; set; } = 6f;
	[Property] public float StopSpeed { get; set; } = 100f;

	[Property] public float StandHeight { get; set; } = 72f;
	[Property] public float CrouchHeight { get; set; } = 44f;

	public CharacterController Controller { get; private set; }
	public Player Player { get; private set; }

	/// <summary>Where the player is trying to go this frame, in world space.</summary>
	public Vector3 WishVelocity { get; private set; }

	public bool IsCrouching { get; private set; }
	public bool IsOnGround => Controller.IsValid() && Controller.IsOnGround;
	public Vector3 Velocity => Controller.IsValid() ? Controller.Velocity : Vector3.Zero;

	protected override void OnAwake()
	{
		Controller = GetComponent<CharacterController>();
		Player = GetComponent<Player>();

		if ( Controller.IsValid() )
			Controller.Height = StandHeight;
	}

	/// <summary>Kill all momentum - used on respawn so players don't inherit their death velocity.</summary>
	public void ClearVelocity()
	{
		if ( !Controller.IsValid() ) return;

		Controller.Velocity = Vector3.Zero;
		WishVelocity = Vector3.Zero;
	}

	protected override void OnUpdate()
	{
		// Only machines that own this pawn simulate it - the local human, or the
		// host for its bots. Remote pawns are moved by network interpolation.
		if ( !Player.IsValid() || !Player.IsSimulatedHere ) return;
		if ( !Controller.IsValid() ) return;

		if ( Player.IsValid() && !Player.IsAlive )
		{
			Controller.Velocity = Vector3.Zero;
			return;
		}

		// Frozen during the pre-round countdown: refuse input but keep falling,
		// otherwise anyone caught mid-air hangs there until the round goes live.
		if ( !(RoundManager.Current?.AllowMovement ?? true) )
		{
			WishVelocity = Vector3.Zero;
			Controller.Velocity = Controller.IsOnGround
				? Vector3.Zero
				: Controller.Velocity.WithX( 0f ).WithY( 0f ) + Vector3.Down * Gravity * Time.Delta;

			Controller.Move();
			return;
		}

		UpdateCrouch();
		BuildWishVelocity();

		if ( Controller.IsOnGround && Player.InputState.JumpPressed )
			DoJump();

		// Source applies half of gravity before the move and half after, which
		// keeps jump height independent of framerate.
		if ( Controller.IsOnGround )
		{
			Controller.Velocity = Controller.Velocity.WithZ( 0f );
			Controller.Acceleration = GroundAcceleration;
			Controller.ApplyFriction( Friction, StopSpeed );
			Controller.Accelerate( WishVelocity );
		}
		else
		{
			Controller.Acceleration = AirAcceleration;
			Controller.Velocity += Vector3.Down * Gravity * Time.Delta * 0.5f;
			Controller.Accelerate( WishVelocity.ClampLength( AirSpeedCap ) );
		}

		Controller.Move();

		if ( Controller.IsOnGround )
			Controller.Velocity = Controller.Velocity.WithZ( 0f );
		else
			Controller.Velocity += Vector3.Down * Gravity * Time.Delta * 0.5f;

		// Footsteps live in PlayerFootsteps, which reads this component's state.
	}

	private void BuildWishVelocity()
	{
		// Move intent is forward/left in local space - rotate it by our yaw only,
		// so looking up or down never changes where we walk.
		var yaw = new Angles( 0f, Player.IsValid() ? Player.EyeAngles.yaw : WorldRotation.Yaw(), 0f ).ToRotation();
		var wish = yaw * Player.InputState.Move;

		wish = wish.WithZ( 0f );

		if ( wish.IsNearZeroLength )
		{
			WishVelocity = Vector3.Zero;
			return;
		}

		WishVelocity = wish.Normal * GetWishSpeed();
	}

	private float GetWishSpeed()
	{
		if ( IsCrouching ) return CrouchSpeed;
		if ( Player.IsValid() && Player.InputState.RunDown ) return RunSpeed;

		return WalkSpeed;
	}

	private void DoJump() => ForceJump();

	/// <summary>
	/// Jump right now, ignoring input. Public because movement mods drive it -
	/// bunny hop is exactly this, called on the landing frame.
	/// </summary>
	public void ForceJump()
	{
		if ( !Controller.IsValid() || !Controller.IsOnGround ) return;

		Controller.Punch( Vector3.Up * JumpPower );
		Controller.IsOnGround = false;
	}

	private void UpdateCrouch()
	{
		var wantsCrouch = Player.IsValid() && Player.InputState.DuckDown;

		if ( wantsCrouch == IsCrouching ) return;

		// Standing up into a ceiling would push us through it, so refuse.
		if ( !wantsCrouch && !HasHeadroom() ) return;

		IsCrouching = wantsCrouch;
		Controller.Height = wantsCrouch ? CrouchHeight : StandHeight;
	}

	private bool HasHeadroom()
	{
		var radius = Controller.Radius;
		var hull = new BBox(
			new Vector3( -radius, -radius, Controller.Height ),
			new Vector3( radius, radius, StandHeight ) );

		var trace = Scene.Trace
			.Box( hull, WorldPosition, WorldPosition )
			.IgnoreGameObjectHierarchy( GameObject )
			.Run();

		return !trace.Hit;
	}

}
