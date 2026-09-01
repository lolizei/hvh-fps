using System;

namespace HvH;

/// <summary>
/// Drives the local pawn forward for a fixed window and reports how often it
/// stepped. Testing only - it never runs during normal play.
///
/// It works by taking over the pawn's <see cref="IPlayerInputSource"/>, which is
/// the same seam a bot uses. That matters twice over: the measurement exercises
/// the real movement path rather than a simulation of it, and because no
/// keyboard is involved anywhere, footsteps coming out of it are proof the
/// footstep code is not reading <c>Input</c> behind our backs.
/// </summary>
public sealed class StepTestDriver : Component, IPlayerInputSource
{
	public enum TestMode
	{
		Walk,
		Run,
		Crouch,
		Jump,
	}

	public TestMode Mode { get; set; } = TestMode.Walk;
	public float Duration { get; set; } = 4f;

	private Player _player;
	private PlayerFootsteps _footsteps;
	private IPlayerInputSource _previous;

	private float _startTime;
	private int _startSteps;
	private int _startLands;
	private int _airborneSteps;
	private int _lastSteps;
	private float _airTime;
	private float _distance;
	private Vector3 _lastPosition;
	private int _teleports;
	private float _deadTime;

	// Walking into a wall would measure nothing, so turn around when we stall.
	private float _maxSpeed;
	private float _timeAtSpeed;
	private float _stuckTime;
	private bool _turnAround;

	protected override void OnStart()
	{
		_player = GetComponent<Player>();
		_footsteps = GetComponent<PlayerFootsteps>();

		if ( !_player.IsValid() || !_footsteps.IsValid() )
		{
			Log.Warning( "hvh_steptest: pawn is missing Player or PlayerFootsteps" );
			Destroy();
			return;
		}

		_previous = _player.InputSource;
		_player.InputSource = this;

		_startTime = Time.Now;
		_startSteps = _footsteps.StepCount;
		_startLands = _footsteps.LandCount;
		_lastSteps = _startSteps;
		_lastPosition = _player.WorldPosition;

		Log.Info( $"hvh_steptest: {Mode} for {Duration:0.#}s - hands off the keyboard." );
	}

	public PlayerInputState BuildInput( Player player )
	{
		var state = PlayerInputState.Idle;

		// Straight ahead, at whatever pace this mode asks for.
		state.Move = new Vector3( 1f, 0f, 0f );
		state.RunDown = Mode == TestMode.Run;
		state.DuckDown = Mode == TestMode.Crouch;
		state.JumpPressed = Mode == TestMode.Jump;

		if ( _turnAround )
		{
			state.LookDelta = new Angles( 0f, 180f, 0f );
			_turnAround = false;
		}

		return state;
	}

	protected override void OnUpdate()
	{
		if ( !_player.IsValid() || !_footsteps.IsValid() )
		{
			Finish();
			return;
		}

		var movement = _player.Movement;
		var position = _player.WorldPosition;


		// A respawn teleports the pawn. Counting that as distance travelled makes
		// a corpse look like a sprinter - it is what made the first run of this
		// test read 746 units and zero steps.
		var moved = position.WithZ( 0f ).Distance( _lastPosition.WithZ( 0f ) );
		var reachable = MathF.Max( 40f, movement.IsValid() ? movement.Velocity.Length * Time.Delta * 2f : 0f );

		if ( moved > reachable ) _teleports++;
		else _distance += moved;

		_lastPosition = position;

		// Being shot mid-test invalidates the numbers, so measure that too.
		if ( _player.Health.IsValid() && !_player.Health.IsAlive )
			_deadTime += Time.Delta;

		var onGround = movement.IsValid() && movement.IsOnGround;
		if ( !onGround ) _airTime += Time.Delta;

		// Any step credited while off the ground is a bug, so count them.
		var steps = _footsteps.StepCount;
		if ( steps != _lastSteps )
		{
			if ( !onGround ) _airborneSteps += steps - _lastSteps;
			_lastSteps = steps;
		}

		// Nose against a wall: turn and keep measuring rather than report a zero.
		var speed = movement.IsValid() ? movement.Velocity.WithZ( 0f ).Length : 0f;
		_maxSpeed = MathF.Max( _maxSpeed, speed );

		if ( speed >= TargetSpeed() * 0.9f )
			_timeAtSpeed += Time.Delta;

		if ( speed < 20f && onGround )
		{
			_stuckTime += Time.Delta;
			if ( _stuckTime > 0.3f )
			{
				_turnAround = true;
				_stuckTime = 0f;
			}
		}
		else
		{
			_stuckTime = 0f;
		}

		if ( Time.Now - _startTime >= Duration )
			Finish();
	}

	/// <summary>Speed this mode should reach with clear ground ahead.</summary>
	private float TargetSpeed()
	{
		var movement = _player.IsValid() ? _player.Movement : null;
		if ( !movement.IsValid() ) return 1f;

		return Mode switch
		{
			TestMode.Run => movement.RunSpeed,
			TestMode.Crouch => movement.CrouchSpeed,
			_ => movement.WalkSpeed,
		};
	}

	private void Finish()
	{
		var elapsed = MathF.Max( 0.01f, Time.Now - _startTime );
		var steps = _footsteps.IsValid() ? _footsteps.StepCount - _startSteps : 0;

		Log.Info(
			$"hvh_steptest {Mode}: {steps} steps in {elapsed:0.00}s" +
			$" = {steps / elapsed:0.00} steps/s" +
			$" | distance {_distance:0} u, avg speed {_distance / elapsed:0} u/s" +
			$" | measured stride {( steps > 0 ? _distance / steps : 0f ):0.0} u" +
			$" | airborne {_airTime:0.00}s, steps while airborne {_airborneSteps}" +
			$" | landings {( _footsteps.IsValid() ? _footsteps.LandCount - _startLands : 0 )}" +
			$" | dead {_deadTime:0.00}s, teleports {_teleports}" +
			$" | peak {_maxSpeed:0} of {TargetSpeed():0} u/s, {_timeAtSpeed / elapsed * 100f:0}% of the time at pace" +
			( _deadTime > 0.05f || _teleports > 0 ? "  <-- DISTURBED, numbers not trustworthy" : "" ) +
			// Cadence is speed divided by stride. A pawn that spent the test
			// bouncing off cover never reached its pace, so its steps/s says
			// nothing about the mode - stride is the number that still holds.
			// Jump mode is not a cadence test - zero steps is the pass condition,
			// so "never reached pace" is expected there rather than a warning.
			( Mode != TestMode.Jump && _timeAtSpeed / elapsed < 0.4f
				? "  <-- NEVER REACHED PACE, cadence not comparable (stride still valid)"
				: "" ) );

		Restore();
		Destroy();
	}

	protected override void OnDestroy() => Restore();

	/// <summary>Hand the pawn back. Must happen however this component ends.</summary>
	private void Restore()
	{
		if ( !_player.IsValid() ) return;
		if ( _player.InputSource != this ) return;

		_player.InputSource = _previous ?? HumanInputSource.Instance;
		_player = null;
	}
}
