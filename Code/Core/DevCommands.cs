using System;

namespace HvH;

/// <summary>
/// Developer console commands for playtesting. Not gameplay - these exist so a
/// single person in the editor can exercise the damage, death and respawn path
/// without needing a second player in the lobby.
/// </summary>
public static class DevCommands
{
	/// <summary>
	/// Report footstep state for every pawn. `hvh_steps`
	/// </summary>
	[ConCmd( "hvh_steps" )]
	public static void Steps()
	{
		var players = Player.All.ToArray();
		if ( players.Length == 0 )
		{
			Log.Info( "hvh_steps: no pawns" );
			return;
		}

		foreach ( var player in players )
		{
			var steps = player.GetComponent<PlayerFootsteps>();
			var movement = player.Movement;

			if ( !steps.IsValid() || !movement.IsValid() )
			{
				Log.Warning( $"hvh_steps: {player.State?.DisplayName} has no footstep component" );
				continue;
			}

			var speed = movement.Velocity.WithZ( 0f ).Length;
			var stride = steps.StepDistance * ( movement.IsCrouching ? steps.CrouchStrideMultiplier : 1f );

			Log.Info(
				$"{player.State?.DisplayName}{( player.IsBot ? " [bot]" : "" )}: steps {steps.StepCount}" +
				$" | speed {speed:0} u/s | ground {movement.IsOnGround} | crouch {movement.IsCrouching}" +
				$" | stride {stride:0} u | implied {( stride > 0f ? speed / stride : 0f ):0.00} steps/s" +
				$" | accum {steps.Accumulator:0.0} | lands {steps.LandCount}" );

			var surface = steps.ProbeGround();
			Log.Info( surface is null
				? "    ground surface: none - every step uses the fallback sound"
				: $"    ground surface: {surface.ResourceName}" +
				  $" | left {( surface.SoundCollection.FootLeft?.ResourceName ?? "MISSING" )}" +
				  $" | right {( surface.SoundCollection.FootRight?.ResourceName ?? "MISSING" )}" +
				  $" | land {( surface.SoundCollection.FootLand?.ResourceName ?? "MISSING" )}" );
		}
	}

	/// <summary>
	/// Walk the local pawn under scripted input and measure its step cadence.
	/// `hvh_steptest run 5` - modes: walk, run, crouch, jump.
	/// </summary>
	[ConCmd( "hvh_steptest" )]
	public static void StepTest( string mode = "walk", float seconds = 4f )
	{
		var player = Player.Local;
		if ( !player.IsValid() )
		{
			Log.Warning( "hvh_steptest: no local player" );
			return;
		}

		if ( player.GetComponent<StepTestDriver>().IsValid() )
		{
			Log.Warning( "hvh_steptest: a test is already running" );
			return;
		}

		if ( !Enum.TryParse<StepTestDriver.TestMode>( mode, true, out var parsed ) )
		{
			Log.Warning( $"hvh_steptest: unknown mode '{mode}' - use walk, run, crouch or jump" );
			return;
		}

		var driver = player.AddComponent<StepTestDriver>( false );
		driver.Mode = parsed;
		driver.Duration = Math.Clamp( seconds, 0.5f, 30f );
		driver.Enabled = true;
	}

	/// <summary>
	/// Make room for <paramref name="extra"/> hand-spawned bots.
	///
	/// <see cref="BotManager.Converge"/> trims the bot count back to
	/// DesiredPlayers within a frame or two, so a command that spawns bots
	/// directly has them deleted immediately after it reports success. Every
	/// such command must call this first.
	/// </summary>
	private static void EnsureRoomForBots( BotManager manager, int extra )
	{
		var humans = Player.All.Count( x => !x.IsBot );
		var needed = humans + extra;
		if ( manager.DesiredPlayers >= needed ) return;

		manager.DesiredPlayers = needed;
		Log.Info( $"  (raised DesiredPlayers to {needed} so the new bots are not trimmed)" );
	}

	/// <summary>
	/// Counts along the whole hit path, so a double marker can be diagnosed with
	/// numbers. `hvh_hitdebug` reports, `hvh_hitdebug 1` resets first.
	/// </summary>
	[ConCmd( "hvh_hitdebug" )]
	public static void HitDebug( int reset = 0 )
	{
		if ( reset != 0 )
		{
			Weapon.ResetCounters();
			Log.Info( "hvh_hitdebug: counters reset" );
			return;
		}

		var scene = Game.ActiveScene;
		var huds = scene?.GetAllComponents<HvH.UI.Hud>().Count() ?? 0;
		var screens = scene?.GetAllComponents<ScreenPanel>().Count() ?? 0;

		Log.Info(
			$"fireRequests={Weapon.FireRequests}" +
			$" damageApplications={Weapon.DamageApplications}" +
			$" confirmInvoked={Weapon.ConfirmHitInvocations}" +
			$" confirmDelivered={Weapon.ConfirmHitDeliveries}" +
			$" markerShows={HitMarker.ShowCount}" );
		Log.Info(
			$"  live UI: Hud={huds} ScreenPanel={screens}" +
			$" Crosshair={HvH.UI.Crosshair.LiveCount}" +
			$" | markerElements={HvH.UI.Crosshair.LiveMarkerElements}" +
			$" (incl. deleting {HvH.UI.Crosshair.MarkerElementsIncludingDeleting})" );
	}

	/// <summary>
	/// Hold the hit marker on screen so it can actually be looked at.
	/// `hvh_marker_hold 8` then shoot; `hvh_marker_hold` restores the default.
	/// </summary>
	[ConCmd( "hvh_marker_hold" )]
	public static void MarkerHold( float seconds = 0.4f )
	{
		HitMarker.Duration = MathF.Max( 0.05f, seconds );
		Log.Info( $"hvh_marker_hold -> marker duration {HitMarker.Duration:0.##}s" );
	}

	/// <summary>
	/// Report each target's origin against its actual world bounds. `hvh_bounds`
	/// Exists because hit zones were being measured from the origin, which is at
	/// the feet for a player and at the middle for a dummy.
	/// </summary>
	[ConCmd( "hvh_bounds" )]
	public static void Bounds()
	{
		foreach ( var health in Game.ActiveScene.GetAllComponents<HealthComponent>() )
		{
			var go = health.GameObject;
			var b = go.GetBounds();
			var originZ = go.WorldPosition.z;
			var stand = health.GetComponentInParent<PlayerMovement>()?.StandHeight ?? 72f;

			Log.Info( $"{go.Name}: originZ={originZ:0.#} boundsZ={b.Mins.z:0.#}..{b.Maxs.z:0.#}" +
				$" height={b.Size.z:0.#} standHeight={stand:0.#}" +
				$" | originIsFeet={( MathF.Abs( originZ - b.Mins.z ) < 4f )}" );
		}
	}

	/// <summary>Damage yourself. `hvh_hurt 25`</summary>
	[ConCmd( "hvh_hurt" )]
	public static void Hurt( float amount = 25f )
	{
		var player = Player.Local;
		if ( !player.IsValid() || !player.Health.IsValid() )
		{
			Log.Warning( "hvh_hurt: no local player" );
			return;
		}

		player.Health.ApplyDamage( new DamageInfo
		{
			Damage = amount,
			Attacker = player.GameObject,
			Position = player.WorldPosition,
			Origin = player.WorldPosition,
		}, HitZone.Body );

		Log.Info( $"hvh_hurt {amount} -> health {player.Health.Health}, alive {player.Health.IsAlive}" );
	}

	/// <summary>Kill yourself outright, to watch the respawn path. `hvh_kill`</summary>
	[ConCmd( "hvh_kill" )]
	public static void Kill() => Hurt( 100000f );

	/// <summary>Point the view at the nearest living dummy. `hvh_aim`</summary>
	[ConCmd( "hvh_aim" )]
	public static void AimAtDummy( float height = 48f )
	{
		var player = Player.Local;
		if ( !player.IsValid() ) return;

		var eye = player.AimRay.Position;

		TargetDummy best = null;
		var bestDistance = float.MaxValue;

		foreach ( var dummy in TargetDummy.All )
		{
			if ( !dummy.IsAlive ) continue;

			var distance = dummy.WorldPosition.Distance( eye );
			if ( distance >= bestDistance ) continue;

			bestDistance = distance;
			best = dummy;
		}

		// Prefer a living enemy player (a bot counts) over a practice dummy, so
		// this can be used to test combat against an actual opponent.
		Player bestPlayer = null;
		var bestPlayerDistance = float.MaxValue;

		foreach ( var other in Player.All )
		{
			if ( !other.IsValid() || other == player || !other.IsAlive ) continue;
			if ( player.Team.IsPlaying() && other.Team == player.Team ) continue;

			var distance = other.WorldPosition.Distance( eye );
			if ( distance >= bestPlayerDistance ) continue;

			bestPlayerDistance = distance;
			bestPlayer = other;
		}

		if ( bestPlayer.IsValid() )
		{
			// Height above the bottom of the target, not above its origin - a
			// dummy is anchored at its middle, so origin-relative heights meant
			// different things for different targets. 48 is chest, 66+ is head.
			var aimAt = AimPoint( bestPlayer.GameObject, height );
			player.EyeAngles = Rotation.LookAt( ( aimAt - eye ).Normal ).Angles();

			Log.Info( $"hvh_aim -> {bestPlayer.State?.DisplayName} (player) at " +
				$"{bestPlayerDistance:0}u, hp {bestPlayer.Health?.Health}" );
			return;
		}

		if ( best is null )
		{
			Log.Info( "hvh_aim: no living target" );
			return;
		}

		// Dummies used to ignore the height argument entirely and take a shot at
		// the origin, so a "head" aim was never actually aimed at a head.
		var dummyAim = AimPoint( best.GameObject, height );
		player.EyeAngles = Rotation.LookAt( ( dummyAim - eye ).Normal ).Angles();
		Log.Info( $"hvh_aim -> {best.DisplayName} at {bestDistance:0}u, hp {best.Health?.Health}" );
	}

	/// <summary>
	/// A point <paramref name="height"/> units above the bottom of the target,
	/// so the same number means the same body part on any target.
	/// </summary>
	private static Vector3 AimPoint( GameObject target, float height )
	{
		var bounds = target.GetBounds();
		var floor = bounds.Size.z < 1f ? target.WorldPosition.z : bounds.Mins.z;

		return target.WorldPosition.WithZ( floor + height );
	}

	/// <summary>Fire the active weapon N times through its real code path. `hvh_fire 5`</summary>
	[ConCmd( "hvh_fire" )]
	public static void Fire( int shots = 1 )
	{
		var weapon = Player.Local?.Inventory?.ActiveWeapon;
		if ( !weapon.IsValid() )
		{
			Log.Warning( "hvh_fire: no active weapon" );
			return;
		}

		var fired = 0;
		for ( var i = 0; i < shots; i++ )
		{
			if ( !weapon.CanFire() ) break;

			weapon.FireOnce();
			fired++;
		}

		Log.Info( $"hvh_fire {fired}/{shots} -> ammo {weapon.Ammo}/{weapon.Reserve}" );
	}

	/// <summary>Kill every dummy, to exercise the round-end path. `hvh_killdummies`</summary>
	[ConCmd( "hvh_killdummies" )]
	public static void KillDummies()
	{
		var player = Player.Local;
		var killed = 0;

		foreach ( var dummy in TargetDummy.All.ToArray() )
		{
			if ( !dummy.IsAlive || !dummy.Health.IsValid() ) continue;

			dummy.Health.ApplyDamage( new DamageInfo
			{
				Damage = 100000f,
				Attacker = player.IsValid() ? player.GameObject : null,
				Position = dummy.WorldPosition,
				Origin = dummy.WorldPosition,
			}, HitZone.Body );

			killed++;
		}

		Log.Info( $"hvh_killdummies -> killed {killed}, alive {TargetDummy.AliveCount}/{TargetDummy.TotalCount}" );
	}

	/// <summary>Switch the local player's weapon slot. `hvh_slot 0`</summary>
	[ConCmd( "hvh_slot" )]
	public static void Slot( int index = 0 )
	{
		var inventory = Player.Local?.Inventory;
		if ( !inventory.IsValid() )
		{
			Log.Warning( "hvh_slot: no inventory" );
			return;
		}

		inventory.RequestSwitch( index );
		Log.Info( $"hvh_slot {index} -> {inventory.ActiveWeapon?.DisplayName}" );
	}

	/// <summary>Refill the local player's current weapon. `hvh_refill`</summary>
	[ConCmd( "hvh_refill" )]
	public static void Refill()
	{
		var weapon = Player.Local?.Inventory?.ActiveWeapon;
		if ( !weapon.IsValid() || weapon.Resolved is null )
		{
			Log.Warning( "hvh_refill: no weapon" );
			return;
		}

		weapon.Ammo = weapon.Resolved.MagazineSize;
		weapon.Reserve = weapon.Resolved.ReserveAmmo;

		Log.Info( $"hvh_refill -> {weapon.DisplayName} {weapon.Ammo}/{weapon.Reserve}" );
	}

	/// <summary>
	/// Run the weapon's exact trace from your eye and report what it hits.
	/// Answers "did the shot miss, or did the hit not register?". `hvh_traceaim`
	/// </summary>
	[ConCmd( "hvh_traceaim" )]
	public static void TraceAim()
	{
		var player = Player.Local;
		if ( !player.IsValid() )
		{
			Log.Warning( "hvh_traceaim: no local player" );
			return;
		}

		var ray = player.AimRay;
		var trace = player.Scene.Trace
			.Ray( ray.Position, ray.Position + ray.Forward * 8192f )
			.IgnoreGameObjectHierarchy( player.GameObject )
			.UseHitboxes()
			.Run();

		if ( !trace.Hit || !trace.GameObject.IsValid() )
		{
			Log.Info( "hvh_traceaim: hit nothing" );
			return;
		}

		var health = trace.GameObject.GetComponentInParent<HealthComponent>();
		var state = trace.GameObject.GetComponentInParent<PlayerState>();

		Log.Info(
			$"hvh_traceaim: hit '{trace.GameObject.Name}' at {trace.Distance:0}u " +
			$"| health={( health.IsValid() ? health.Health.ToString( "0" ) : "none" )} " +
			$"| owner={( state.IsValid() ? state.DisplayName : "none" )} " +
			$"| canDamage={DamageRules.CanDamage( player.GameObject, trace.GameObject )}" );
	}

	/// <summary>
	/// Aim at the nearest enemy and fire in the SAME frame. `hvh_shoot 5`
	///
	/// Aiming and firing as two separate commands does not work while a human is
	/// at the mouse: Player.OnUpdate folds in their look delta between the two,
	/// so the scripted aim is gone before the shot leaves.
	/// </summary>
	[ConCmd( "hvh_shoot" )]
	public static void Shoot( int shots = 1, float height = 48f )
	{
		for ( var i = 0; i < shots; i++ )
		{
			AimAtDummy( height );
			Fire( 1 );
		}
	}

	/// <summary>Report every dummy's health. `hvh_dummies`</summary>
	[ConCmd( "hvh_dummies" )]
	public static void Dummies()
	{
		Log.Info( $"dummies alive {TargetDummy.AliveCount}/{TargetDummy.TotalCount}" );

		foreach ( var dummy in TargetDummy.All )
			Log.Info( $"  {dummy.DisplayName}: hp={dummy.Health?.Health} alive={dummy.IsAlive}" );
	}

	/// <summary>Leave the match and go back to the menu. `hvh_menu`</summary>
	[ConCmd( "hvh_menu" )]
	public static void ToMenu()
	{
		var exit = Game.ActiveScene?.GetAllComponents<GameExitHandler>().FirstOrDefault();
		if ( !exit.IsValid() )
		{
			Log.Warning( "hvh_menu: no GameExitHandler in this scene" );
			return;
		}

		Log.Info( "hvh_menu -> returning to menu" );
		exit.ReturnToMenu();
	}

	/// <summary>Load a scene by path - the same hop the menu's PLAY makes. `hvh_loadscene`</summary>
	[ConCmd( "hvh_loadscene" )]
	public static void LoadScene( string path = "scenes/game.scene" )
	{
		var scene = ResourceLibrary.Get<SceneFile>( path );
		if ( scene is null )
		{
			Log.Warning( $"hvh_loadscene: no scene at '{path}'" );
			return;
		}

		Log.Info( $"hvh_loadscene -> {path}" );
		Game.ActiveScene?.Load( scene );
	}

	/// <summary>Who last damaged this pawn, host-side. Empty if nothing has.</summary>
	private static string LastAttackerName( Player player )
	{
		var attacker = player.Health?.LastAttacker;
		if ( !attacker.IsValid() ) return "none";

		var state = attacker.GetComponentInParent<PlayerState>();

		return state.IsValid() ? state.DisplayName : attacker.Name;
	}

	/// <summary>Spawn one bot on the opposing team. `hvh_bot`</summary>
	[ConCmd( "hvh_bot" )]
	public static void SpawnBot()
	{
		var manager = BotManager.Current;
		if ( !manager.IsValid() )
		{
			Log.Warning( "hvh_bot: no BotManager in this scene" );
			return;
		}

		EnsureRoomForBots( manager, 1 );

		var bot = manager.SpawnBot();
		Log.Info( bot.IsValid()
			? $"hvh_bot -> spawned, bots now {BotManager.BotCount}"
			: "hvh_bot: spawn failed" );
	}

	/// <summary>
	/// Teleport the nearest bot to just in front of you, for close-range
	/// testing. The arena's centre cover blocks most spawn-to-spawn diagonals.
	/// `hvh_botnear 250`
	/// </summary>
	[ConCmd( "hvh_botnear" )]
	public static void BotNear( float distance = 250f )
	{
		var player = Player.Local;
		if ( !player.IsValid() )
		{
			Log.Warning( "hvh_botnear: no local player" );
			return;
		}

		var bot = BotManager.Bots.FirstOrDefault();
		if ( !bot.IsValid() )
		{
			Log.Warning( "hvh_botnear: no bot" );
			return;
		}

		var forward = player.EyeAngles.Forward.WithZ( 0f ).Normal;
		var target = player.WorldPosition + forward * distance;

		// Drop it onto the floor. Placing it at the shooter's Z leaves it in the
		// air whenever the shooter is stood on something, and a falling target
		// invalidates the aim taken a moment earlier.
		//
		// Hit world geometry only. The two chained IgnoreGameObjectHierarchy calls
		// this used to have did not stack - the second replaced the first - so the
		// bot stopped being ignored and the trace landed on the bot already stood
		// there, teleporting it onto its own head at z=128 and leaving it falling.
		var ground = player.Scene.Trace
			.Ray( target + Vector3.Up * 256f, target + Vector3.Down * 1024f )
			.WithTag( "solid" )
			.Run();

		if ( ground.Hit )
			target = ground.EndPosition;

		// The host owns the bot, so writing its transform here is authoritative.
		bot.WorldPosition = target;
		bot.Movement?.ClearVelocity();

		Log.Info( $"hvh_botnear -> {bot.State?.DisplayName} moved to {target}" );
	}

	/// <summary>Why is the bot not shooting? `hvh_botinfo`</summary>
	[ConCmd( "hvh_botinfo" )]
	public static void BotInfo()
	{
		var any = false;

		foreach ( var bot in BotManager.Bots )
		{
			any = true;
			var brain = bot.GetComponent<BotBrain>();
			if ( !brain.IsValid() )
			{
				Log.Info( $"  {bot.State?.DisplayName}: NO BRAIN attached" );
				continue;
			}

			Log.Info(
				$"  {bot.State?.DisplayName}: reason={brain.LastReason} " +
				$"| target={brain.Target?.State?.DisplayName ?? "none"} " +
				$"| angle={brain.LastAngleToTarget:0.0} settled={brain.LastSettled} reacted={brain.LastReacted} " +
				$"| dist={brain.LastDistance:0} move={brain.LastMove} " +
				$"| speed={( bot.Movement.IsValid() ? bot.Movement.Velocity.WithZ( 0f ).Length : 0f ):0} " +
				$"| alive={bot.IsAlive} source={( bot.InputSource is null ? "null" : bot.InputSource.GetType().Name )}" );
		}

		if ( !any ) Log.Info( "hvh_botinfo: no bots" );
	}

	/// <summary>
	/// Spawn two bots on opposing teams facing each other, to watch bot combat
	/// without a human in the loop. `hvh_botduel`
	/// </summary>
	[ConCmd( "hvh_botduel" )]
	public static void BotDuel( float gap = 400f )
	{
		var manager = BotManager.Current;
		var player = Player.Local;
		if ( !manager.IsValid() || !player.IsValid() )
		{
			Log.Warning( "hvh_botduel: need a BotManager and a local player" );
			return;
		}

		// Fixed positions on the arena's south strip, verified by trace to have a
		// clear line between them. Placing these relative to the player put them
		// outside the walls, where they fell out of the world and ended up
		// 240,000 units away.
		var left = new Vector3( -gap * 0.5f, -450f, 16f );
		var right = new Vector3( gap * 0.5f, -450f, 16f );

		EnsureRoomForBots( manager, 2 );

		var a = manager.SpawnBot( Team.Vanguard, left );
		var b = manager.SpawnBot( Team.Syndicate, right );

		Log.Info( $"hvh_botduel -> {( a.IsValid() ? a.State?.DisplayName : "fail" )} vs " +
			$"{( b.IsValid() ? b.State?.DisplayName : "fail" )}, gap {gap}u" );
	}

	/// <summary>
	/// Set the total player target (humans + bots) and report convergence.
	/// `hvh_target 4`
	/// </summary>
	[ConCmd( "hvh_target" )]
	public static void Target( int total = 2 )
	{
		var manager = BotManager.Current;
		if ( !manager.IsValid() )
		{
			Log.Warning( "hvh_target: no BotManager" );
			return;
		}

		manager.DesiredPlayers = total;

		Log.Info( $"hvh_target {total} -> humans={Player.All.Count( x => !x.IsBot )} " +
			$"bots={BotManager.BotCount} wantedBots={manager.WantedBots}" );
	}

	/// <summary>
	/// Kill every bot through the normal damage path, credited to you.
	/// Exercises death, scoring, kill feed, round elimination and respawn
	/// without depending on scripted aim landing. `hvh_killbots`
	/// </summary>
	[ConCmd( "hvh_killbots" )]
	public static void KillBots()
	{
		var player = Player.Local;
		var killed = 0;

		foreach ( var bot in BotManager.Bots.ToArray() )
		{
			if ( !bot.IsAlive || !bot.Health.IsValid() ) continue;

			bot.Health.ApplyDamage( new DamageInfo
			{
				Damage = 100000f,
				Attacker = player.IsValid() ? player.GameObject : null,
				Weapon = player.IsValid() ? player.Inventory?.ActiveWeapon?.GameObject : null,
				Position = bot.WorldPosition,
				Origin = bot.WorldPosition,
			}, HitZone.Body );

			killed++;
		}

		Log.Info( $"hvh_killbots -> killed {killed}" );
	}

	/// <summary>Remove every bot. `hvh_clearbots`</summary>
	[ConCmd( "hvh_clearbots" )]
	public static void ClearBots()
		=> Log.Info( $"hvh_clearbots -> removed {BotManager.RemoveAllBots()}" );

	/// <summary>List every player pawn and who drives it. `hvh_players`</summary>
	[ConCmd( "hvh_players" )]
	public static void Players()
	{
		var local = Player.Local;
		Log.Info( $"players {Player.All.Count()} (bots {BotManager.BotCount})" );

		foreach ( var player in Player.All )
		{
			var state = player.State;
			Log.Info(
				$"  {state?.DisplayName ?? "?"} | bot={player.IsBot} " +
				$"| team={player.Team} | local={player == local} " +
				$"| locallyControlled={player.IsLocallyControlled} " +
				$"| simulatedHere={player.IsSimulatedHere} " +
				$"| hp={player.Health?.Health} alive={player.IsAlive} " +
				$"| k={player.State?.Kills} d={player.State?.Deaths} " +
				$"| move={player.InputState.Move} attack={player.InputState.AttackDown} " +
				$"| lastHitBy={LastAttackerName( player )}" );
		}
	}

	/// <summary>
	/// Hold the round in Playing for an hour and put everything back on its
	/// feet. Round restarts respawn players and bots to their team spawns,
	/// which silently undoes any test placement - this stops that.
	/// `hvh_sandbox`
	/// </summary>
	[ConCmd( "hvh_sandbox" )]
	public static void Sandbox()
	{
		var round = RoundManager.Current;
		if ( round.IsValid() )
		{
			round.State = RoundState.Playing;
			round.PhaseEndTime = Time.Now + 3600f;
		}

		TargetDummy.ReviveAll();

		foreach ( var player in Player.All )
			player.Health?.Revive();

		Log.Info( $"hvh_sandbox -> round held in Playing, " +
			$"{TargetDummy.AliveCount} dummies and {Player.All.Count()} players revived" );
	}

	/// <summary>Current hit-marker state on THIS machine. `hvh_hitmarker`</summary>
	[ConCmd( "hvh_hitmarker" )]
	public static void HitMarkerState()
		=> Log.Info( $"hitmarker visible={HitMarker.Visible} kind={HitMarker.Kind} fade={HitMarker.Fade:0.00}" );

	/// <summary>Clear the hit marker, so a test starts from a known state. `hvh_hitmarker_clear`</summary>
	[ConCmd( "hvh_hitmarker_clear" )]
	public static void HitMarkerClear()
	{
		HitMarker.Clear();
		Log.Info( "hitmarker cleared" );
	}

	/// <summary>Print the local player's live state. `hvh_state`</summary>
	[ConCmd( "hvh_state" )]
	public static void State()
	{
		var player = Player.Local;
		if ( !player.IsValid() )
		{
			Log.Info( "hvh_state: no local player" );
			return;
		}

		var weapon = player.Inventory?.ActiveWeapon;
		var round = RoundManager.Current;

		Log.Info(
			$"pos={player.WorldPosition} " +
			$"eye={player.EyeAngles} " +
			$"hp={player.Health?.Health} armor={player.Health?.Armor} alive={player.IsAlive} " +
			$"team={player.Team} " +
			$"weapon={weapon?.DisplayName} ammo={weapon?.Ammo}/{weapon?.Reserve} " +
			$"ground={player.Movement?.IsOnGround} vel={player.Movement?.Velocity.Length:0} " +
			$"round={round?.State} t={round?.TimeRemaining:0}" );
	}
}
