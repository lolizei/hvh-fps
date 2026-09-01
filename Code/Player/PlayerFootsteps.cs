using System;

namespace HvH;

/// <summary>
/// Footstep audio for a pawn.
///
/// This is the one effect in the game that is not decoration: hearing where
/// somebody is, and roughly how fast they are moving, is information you play
/// on. So it is spatialised and it is driven by real movement, never a timer.
///
/// It reads <see cref="PlayerMovement"/> and nothing else. There is no
/// bot-specific code here and no second velocity source - a bot produces
/// footsteps because it moves the same pawn a human does.
/// </summary>
public sealed class PlayerFootsteps : Component
{
	/// <summary>Distance travelled between steps at normal pace, in units.</summary>
	[Property] public float StepDistance { get; set; } = 85f;

	/// <summary>
	/// Crouching stretches the stride, so sneaking is both quieter and less
	/// frequent rather than just quieter.
	/// </summary>
	[Property] public float CrouchStrideMultiplier { get; set; } = 1.8f;

	[Property] public float Volume { get; set; } = 1f;

	[Property] public float CrouchVolume { get; set; } = 0.35f;

	/// <summary>Below this speed we are not really walking, so no steps.</summary>
	[Property] public float MinimumSpeed { get; set; } = 25f;

	/// <summary>Used when the ground surface has no footstep sound registered.</summary>
	[Property] public string FallbackStep { get; set; } = "sounds/footsteps/footstep-concrete.sound";

	[Property] public string FallbackLand { get; set; } = "sounds/footsteps/footstep-concrete-land.sound";

	/// <summary>How many steps this pawn has played. Diagnostics only.</summary>
	public int StepCount { get; private set; }

	/// <summary>How many landings we have detected. Diagnostics only.</summary>
	public int LandCount { get; private set; }

	/// <summary>Distance banked toward the next step. Diagnostics only.</summary>
	public float Accumulator => _accumulator;

	private PlayerMovement _movement;
	private float _accumulator;
	private bool _leftFoot;
	private bool _wasOnGround = true;

	protected override void OnAwake()
	{
		_movement = GetComponent<PlayerMovement>();
	}

	protected override void OnUpdate()
	{
		if ( !_movement.IsValid() ) return;

		var onGround = _movement.IsOnGround;

		// Landing gets its own sound; the surface provides one, so it is free.
		if ( onGround && !_wasOnGround )
		{
			LandCount++;
			PlayLand();
			_accumulator = 0f;
		}

		_wasOnGround = onGround;

		// Silent in the air. No step until we are back on something.
		if ( !onGround ) return;

		var speed = _movement.Velocity.WithZ( 0f ).Length;
		if ( speed < MinimumSpeed ) return;

		// Cadence comes from distance covered, not from a clock - so running
		// steps come faster than walking ones with no extra rules.
		_accumulator += speed * Time.Delta;

		var stride = StepDistance * ( _movement.IsCrouching ? CrouchStrideMultiplier : 1f );
		if ( _accumulator < stride ) return;

		_accumulator = 0f;
		PlayStep();
	}

	private void PlayStep()
	{
		var surface = GroundSurface();

		// Alternate feet so repeated steps are not the identical sample.
		_leftFoot = !_leftFoot;
		var sound = _leftFoot
			? surface?.SoundCollection.FootLeft
			: surface?.SoundCollection.FootRight;

		StepCount++;

		if ( sound is not null )
		{
			Play( sound );
			return;
		}

		Play( FallbackStep );
	}

	private void PlayLand()
	{
		var surface = GroundSurface();
		var sound = surface?.SoundCollection.FootLand;

		if ( sound is not null )
		{
			Play( sound );
			return;
		}

		Play( FallbackLand );
	}

	/// <summary>
	/// The surface under this pawn right now. Diagnostics only - so a test can
	/// tell "the floor has proper footstep sounds" from "we are silently falling
	/// back on every step", which look and sound identical from the outside.
	/// </summary>
	public Surface ProbeGround() => GroundSurface();

	/// <summary>
	/// What we are standing on. One short trace per step - not per frame - so
	/// surface-correct footsteps cost almost nothing.
	/// </summary>
	private Surface GroundSurface()
	{
		var from = WorldPosition + Vector3.Up * 8f;
		var to = WorldPosition + Vector3.Down * 24f;

		var trace = Scene.Trace
			.Ray( from, to )
			.IgnoreGameObjectHierarchy( GameObject )
			.Run();

		return trace.Hit ? trace.Surface : null;
	}

	private void Play( SoundEvent sound )
	{
		try
		{
			Configure( Sound.Play( sound, WorldPosition ) );
		}
		catch ( Exception )
		{
		}
	}

	private void Play( string soundPath )
	{
		if ( string.IsNullOrWhiteSpace( soundPath ) ) return;

		try
		{
			Configure( Sound.Play( soundPath, WorldPosition ) );
		}
		catch ( Exception )
		{
		}
	}

	/// <summary>
	/// Spatialisation is the point of this component, so distance attenuation
	/// and occlusion are switched on explicitly rather than left to chance.
	/// </summary>
	private void Configure( SoundHandle handle )
	{
		if ( handle is null ) return;

		handle.Position = WorldPosition;
		handle.DistanceAttenuation = true;
		handle.OcclusionEnabled = true;
		handle.Volume = _movement.IsValid() && _movement.IsCrouching ? CrouchVolume : Volume;
	}
}
