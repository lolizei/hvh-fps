using System;

namespace HvH;

/// <summary>
/// Decides what a bot wants to do each frame.
///
/// This is an <see cref="IPlayerInputSource"/>, which means it plugs into the
/// exact seam a human's keyboard plugs into: it returns intent, and the ordinary
/// <see cref="PlayerMovement"/> and <see cref="Weapon"/> components act on it.
/// The bot therefore fires through the same client-decides-direction /
/// host-decides-damage path a person does, with no separate combat code.
///
/// Host only - a client never runs this. Bots are host-owned, so on a client the
/// pawn is a proxy, is not simulated there, and BuildInput is never called.
///
/// This task gives the bot eyes and a trigger finger only. It does not move.
/// </summary>
public sealed class BotBrain : Component, IPlayerInputSource
{
	/// <summary>How long after seeing a target before it starts shooting.</summary>
	[Property] public float ReactionTime { get; set; } = 0.35f;

	/// <summary>Degrees per second it can turn. Stops it snapping like an aimbot.</summary>
	[Property] public float TurnSpeed { get; set; } = 220f;

	/// <summary>Degrees of deliberate sloppiness, re-rolled periodically.</summary>
	[Property] public float AimError { get; set; } = 2.5f;

	/// <summary>How often the aim error is re-rolled, in seconds.</summary>
	[Property] public float AimDrift { get; set; } = 0.4f;

	[Property] public float MaxRange { get; set; } = 4096f;

	/// <summary>
	/// It only notices things roughly in front of it. Kept generous because a
	/// stationary bot cannot reposition to look around yet.
	/// </summary>
	[Property] public float ViewAngle { get; set; } = 200f;

	/// <summary>
	/// Degrees per second it sweeps its view while it has no target.
	///
	/// Without this the bot deadlocks: it can only acquire a target inside its
	/// view cone, but it only turns toward a target it has already acquired - so
	/// anything behind it stays invisible forever.
	/// </summary>
	[Property] public float ScanSpeed { get; set; } = 70f;

	/// <summary>Only pulls the trigger once the crosshair is roughly on target.</summary>
	[Property] public float FireAngle { get; set; } = 8f;

	// ---- movement ----------------------------------------------------------

	/// <summary>Closes the distance while further away than this.</summary>
	[Property] public float CombatRange { get; set; } = 350f;

	/// <summary>Backs off inside this, so it never grinds into the enemy.</summary>
	[Property] public float TooCloseRange { get; set; } = 140f;

	[Property] public float StrafeMinTime { get; set; } = 0.6f;
	[Property] public float StrafeMaxTime { get; set; } = 1.5f;

	/// <summary>How often the full scene is searched for a target.</summary>
	[Property] public float TargetScanInterval { get; set; } = 0.2f;

	/// <summary>Distance to the current target, for diagnostics.</summary>
	public float LastDistance { get; private set; }

	/// <summary>Local-space move intent produced this frame, for diagnostics.</summary>
	public Vector3 LastMove { get; private set; }

	/// <summary>How close it needs to get to a search point before picking another.</summary>
	[Property] public float SearchArriveRange { get; set; } = 200f;

	private Vector3? _lastKnownTargetPosition;
	private Vector3? _searchPoint;
	private int _strafeSign = 1;
	private float _nextStrafeFlip;
	private float _nextTargetScan;
	private float _stuckSince;
	private float _sidestepUntil;

	/// <summary>Current target, for debugging and later tasks.</summary>
	public Player Target { get; private set; }

	// Diagnostics - why it is or isn't shooting right now.
	public float LastAngleToTarget { get; private set; }
	public bool LastSettled { get; private set; }
	public bool LastReacted { get; private set; }
	public string LastReason { get; private set; } = "no target";
	public int CandidatesSeen { get; private set; }
	public string LastRejection { get; private set; } = "";

	/// <summary>
	/// How long it remembers a target after losing sight of it. Without this the
	/// reaction clock restarts on every brief line-of-sight break and the bot
	/// never gets to shoot at all.
	/// </summary>
	[Property] public float TargetMemory { get; set; } = 1.5f;

	private float _targetAcquiredAt;
	private Player _lastTarget;
	private float _lostTargetAt;
	private Angles _aimError;
	private float _nextErrorRoll;

	public PlayerInputState BuildInput( Player player )
	{
		var intent = PlayerInputState.Idle;

		// Never think on a client, and never for a corpse.
		if ( !Networking.IsHost ) return intent;
		if ( !player.IsValid() || !player.IsAlive ) return ClearTarget();

		var previous = Target;

		// Drop a dead target at once, but only re-search the whole scene on an
		// interval - a full GetAllComponents sweep every frame is wasteful.
		if ( Target.IsValid() && !Target.IsAlive )
			Target = null;

		if ( !Target.IsValid() || Time.Now >= _nextTargetScan )
		{
			_nextTargetScan = Time.Now + TargetScanInterval;
			Target = FindTarget( player );
		}

		if ( !Target.IsValid() )
		{
			if ( previous.IsValid() )
				_lostTargetAt = Time.Now;

			LastReason = "searching";
			Target = null;

			// Sweep so something behind us can eventually be found.
			intent.LookDelta = new Angles( 0f, ScanSpeed * Time.Delta, 0f );

			// ...and go looking. A bot that only moves once it already has a
			// target never leaves its spawn, and never acquires one, because the
			// map's cover hides the spawns from each other.
			intent.Move = BuildSearchMove( player );
			LastMove = intent.Move;

			return intent;
		}

		// Restart the reaction clock only for a genuinely new target - not for
		// the same one reappearing after a short break. Snapping onto a fresh
		// target still costs full reaction time.
		if ( Target != previous )
		{
			var isNewTarget = Target != _lastTarget || Time.Now - _lostTargetAt > TargetMemory;
			if ( isNewTarget )
			{
				_targetAcquiredAt = Time.Now;
				RollAimError();
			}

			_lastTarget = Target;
		}

		if ( Time.Now >= _nextErrorRoll )
			RollAimError();

		var eye = player.AimRay.Position;
		var aimPoint = ChestOf( Target ) + _aimError.Forward * 12f;

		var desired = Rotation.LookAt( ( aimPoint - eye ).Normal ).Angles();
		var current = player.EyeAngles;
		var step = TurnSpeed * Time.Delta;

		intent.LookDelta = new Angles(
			StepTowards( current.pitch, desired.pitch, step ),
			StepTowards( current.yaw, desired.yaw, step ),
			0f );

		// Hold fire until it has had time to react and is actually looking at
		// the target - otherwise it shoots walls while still turning.
		LastAngleToTarget = Vector3.GetAngle( current.Forward, ( aimPoint - eye ).Normal );
		var settled = LastAngleToTarget <= FireAngle;
		var reacted = Time.Now - _targetAcquiredAt >= ReactionTime;

		LastSettled = settled;
		LastReacted = reacted;
		LastReason = settled ? ( reacted ? "firing" : "reacting" ) : "turning";

		intent.AttackDown = settled && reacted;
		intent.AttackPressed = intent.AttackDown;

		// Movement shares the target the aim uses - one target concept, not two.
		_lastKnownTargetPosition = Target.WorldPosition;
		_searchPoint = null;

		intent.Move = BuildMove( player, Target.WorldPosition );
		LastMove = intent.Move;

		// Reload when dry, or it fires one magazine and is harmless forever.
		var weapon = player.Inventory?.ActiveWeapon;
		if ( weapon.IsValid() && weapon.Ammo <= 0 && !weapon.IsReloading )
		{
			intent.ReloadPressed = true;
			intent.AttackDown = false;
			intent.AttackPressed = false;
		}

		return intent;
	}

	/// <summary>
	/// Head somewhere useful while we have no target: the enemy's last known
	/// position if we ever saw them, otherwise the middle of the map, which is
	/// where the spawns face and where a fight is most likely to be found.
	/// </summary>
	private Vector3 BuildSearchMove( Player player )
	{
		_searchPoint ??= _lastKnownTargetPosition ?? MapCentre( player );

		var flat = ( _searchPoint.Value - player.WorldPosition ).WithZ( 0f );
		LastDistance = flat.Length;

		// Arrived and still nothing to shoot - go somewhere else.
		if ( LastDistance <= SearchArriveRange )
		{
			_lastKnownTargetPosition = null;
			_searchPoint = RandomSpawnPoint( player ) ?? MapCentre( player );
			return Vector3.Zero;
		}

		UpdateStuckCheck( player );

		var world = flat.Normal;

		// While sidestepping, go fully sideways - we are against something and
		// need to clear it before heading for the goal again.
		world = Time.Now < _sidestepUntil
			? Vector3.Cross( Vector3.Up, world ).Normal * _strafeSign
			: ( world + Vector3.Cross( Vector3.Up, world ) * ( _strafeSign * 0.25f ) ).Normal;

		return ToLocalMove( player, world );
	}

	/// <summary>Centroid of the map's spawn points - a map-agnostic "middle".</summary>
	private static Vector3 MapCentre( Player player )
	{
		var points = player.Scene?.GetAllComponents<SpawnPoint>().ToArray();
		if ( points is null || points.Length == 0 ) return Vector3.Zero;

		var total = Vector3.Zero;
		foreach ( var point in points ) total += point.WorldPosition;

		return total / points.Length;
	}

	private static Vector3? RandomSpawnPoint( Player player )
	{
		var points = player.Scene?.GetAllComponents<SpawnPoint>().ToArray();
		if ( points is null || points.Length == 0 ) return null;

		return Random.Shared.FromArray( points ).WorldPosition;
	}

	/// <summary>
	/// Turn "where the enemy is" into the local-space move intent
	/// <see cref="PlayerMovement"/> expects.
	///
	/// The pawn always faces its target (aim drives body yaw), so approaching is
	/// forward and circling is pure strafe. The result is converted out of world
	/// space through our own yaw, because Move is interpreted relative to it -
	/// world X/Y is not forward/right.
	/// </summary>
	private Vector3 BuildMove( Player player, Vector3 targetPosition )
	{
		var flat = ( targetPosition - player.WorldPosition ).WithZ( 0f );
		LastDistance = flat.Length;

		if ( LastDistance < 1f ) return Vector3.Zero;

		var toTarget = flat.Normal;
		var sideways = Vector3.Cross( Vector3.Up, toTarget ).Normal;

		// Close in when far, hold at fighting distance, back off when too near.
		var forward = 0f;
		if ( LastDistance > CombatRange ) forward = 1f;
		else if ( LastDistance < TooCloseRange ) forward = -1f;

		// Circle once roughly in range, flipping direction on a timer so it is
		// not trivially predictable.
		var strafe = 0f;
		if ( LastDistance <= CombatRange * 1.5f )
		{
			if ( Time.Now >= _nextStrafeFlip ) FlipStrafe();
			strafe = _strafeSign;
		}

		var world = toTarget * forward + sideways * strafe;
		if ( world.IsNearZeroLength ) return Vector3.Zero;

		world = world.Normal;

		UpdateStuckCheck( player );

		if ( Time.Now < _sidestepUntil )
			world = sideways * _strafeSign;

		return ToLocalMove( player, world );
	}

	/// <summary>
	/// Convert a world-space direction into the local move intent
	/// <see cref="PlayerMovement"/> expects: x forward, y left, relative to our
	/// own yaw. World X/Y is not forward/right.
	/// </summary>
	private static Vector3 ToLocalMove( Player player, Vector3 worldDirection )
	{
		var yaw = new Angles( 0f, player.EyeAngles.yaw, 0f ).ToRotation();
		var local = yaw.Inverse * worldDirection;

		return new Vector3( local.x, local.y, 0f );
	}

	/// <summary>
	/// If we are asking to move but barely moving, we are against geometry.
	/// Flipping the strafe slides us along the obstacle instead of grinding into
	/// it - enough to get around this map's boxes without any pathfinding.
	/// </summary>
	private void UpdateStuckCheck( Player player )
	{
		var movement = player.Movement;
		var speed = movement.IsValid() ? movement.Velocity.WithZ( 0f ).Length : 0f;

		if ( speed >= 25f )
		{
			_stuckSince = 0f;
			return;
		}

		if ( _stuckSince <= 0f )
		{
			_stuckSince = Time.Now;
			return;
		}

		if ( Time.Now - _stuckSince < 0.5f ) return;

		// Commit to a full sideways step. A gentle nudge is not enough to get
		// around a crate - it just oscillates against the face of it.
		FlipStrafe();
		_sidestepUntil = Time.Now + 0.7f;
		_stuckSince = 0f;
	}

	private void FlipStrafe()
	{
		_strafeSign = -_strafeSign;
		_nextStrafeFlip = Time.Now + Random.Shared.Float( StrafeMinTime, StrafeMaxTime );
	}

	private PlayerInputState ClearTarget()
	{
		Target = null;
		return PlayerInputState.Idle;
	}

	/// <summary>Nearest living enemy that is in front of us and in line of sight.</summary>
	private Player FindTarget( Player self )
	{
		var eye = self.AimRay.Position;
		var forward = self.EyeAngles.Forward;

		Player best = null;
		var bestDistance = float.MaxValue;
		CandidatesSeen = 0;
		LastRejection = "";

		foreach ( var other in Player.All )
		{
			if ( !other.IsValid() || other == self || !other.IsAlive ) continue;

			CandidatesSeen++;

			// Never shoot our own side. DamageRules would refuse the damage
			// anyway, but a bot that aims at team-mates looks broken.
			if ( self.Team.IsPlaying() && other.Team == self.Team ) { LastRejection = "same team"; continue; }

			var chest = ChestOf( other );
			var distance = chest.Distance( eye );
			if ( distance > MaxRange ) { LastRejection = $"out of range ({distance:0}u)"; continue; }
			if ( distance >= bestDistance ) continue;

			var offAxis = Vector3.GetAngle( forward, ( chest - eye ).Normal );
			if ( offAxis > ViewAngle * 0.5f ) { LastRejection = $"outside view ({offAxis:0} deg)"; continue; }
			if ( !CanSee( self, other, eye, chest ) ) { LastRejection = "no line of sight"; continue; }

			bestDistance = distance;
			best = other;
		}

		return best;
	}

	private static bool CanSee( Player from, Player to, Vector3 eye, Vector3 point )
	{
		// Only ONE IgnoreGameObjectHierarchy. Chaining a second call does not
		// add to the ignore list - it replaces it, so the shooter stopped
		// ignoring its own body, and since the eye sits inside that body every
		// ray hit itself and the bot concluded it could never see anything.
		var trace = from.Scene.Trace
			.Ray( eye, point )
			.IgnoreGameObjectHierarchy( from.GameObject )
			.Run();

		if ( !trace.Hit ) return true;

		// Hitting the target is seeing the target.
		return trace.GameObject.IsValid()
			&& trace.GameObject.GetComponentInParent<Player>() == to;
	}

	private static Vector3 ChestOf( Player player )
	{
		var movement = player.Movement;
		var height = movement.IsValid()
			? ( movement.IsCrouching ? movement.CrouchHeight : movement.StandHeight )
			: 72f;

		return player.WorldPosition + Vector3.Up * ( height * 0.66f );
	}

	private void RollAimError()
	{
		_aimError = new Angles(
			Random.Shared.Float( -AimError, AimError ),
			Random.Shared.Float( -AimError, AimError ),
			0f );

		_nextErrorRoll = Time.Now + AimDrift;
	}

	/// <summary>Shortest signed turn from one angle to another, capped to maxStep.</summary>
	private static float StepTowards( float current, float target, float maxStep )
	{
		var difference = Angles.NormalizeAngle( target - current );

		return Math.Clamp( difference, -maxStep, maxStep );
	}
}
