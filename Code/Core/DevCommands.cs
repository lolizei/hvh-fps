using System;
using System.Collections.Generic;

namespace HvH;

/// <summary>
/// Developer console commands for playtesting. Not gameplay - these exist so a
/// single person in the editor can exercise the damage, death and respawn path
/// without needing a second player in the lobby.
/// </summary>
public static class DevCommands
{
	// ======================================================================
	//  Consolidated command surface.
	//
	//  These dispatchers are the whole public surface; everything below them is
	//  an implementation. Grouped by verb so there is one obvious place to look
	//  rather than one command per thing that was ever tested.
	// ======================================================================

	/// <summary>
	/// Everything read-only, in one place. `hvh_report players`
	/// state | players | bots | steps | dummies | marker | hits | bounds | all
	/// </summary>
	[ConCmd( "hvh_report" )]
	public static void Report( string what = "state" )
	{
		switch ( what.ToLowerInvariant() )
		{
			case "state": State(); return;
			case "players": Players(); return;
			case "bots": BotInfo(); return;
			case "steps": Steps(); return;
			case "dummies": Dummies(); return;
			case "marker": HitMarkerState(); return;
			case "hits": HitDebug(); return;
			case "bounds": Bounds(); return;
			case "view": ViewModel(); return;
			case "bones": Bones(); return;
			case "anim": AnimParams(); return;
			case "hold": HoldTypes(); return;
			case "all":
				State(); Players(); BotInfo(); Steps();
				Dummies(); HitMarkerState(); HitDebug(); Bounds();
				return;
			default:
				Log.Warning( $"hvh_report: unknown '{what}' - use state, players, bots, " +
					"steps, dummies, marker, hits, bounds, view, bones, anim, hold or all" );
				return;
		}
	}

	/// <summary>
	/// Bot population and placement. `hvh_bots 4`, `hvh_bots duel`
	/// &lt;n&gt; | add | duel | near [dist] | kill | clear
	/// </summary>
	[ConCmd( "hvh_bots" )]
	public static void Bots( string what = "", float value = 0f )
	{
		if ( string.IsNullOrWhiteSpace( what ) )
		{
			PopulationReport( "hvh_bots" );
			Log.Info( "  usage: hvh_bots <n> | add | duel | near [dist] | kill | clear" );
			return;
		}

		if ( int.TryParse( what, out var total ) )
		{
			Target( total );
			PopulationReport( "hvh_bots" );
			return;
		}

		var removed = false;

		switch ( what.ToLowerInvariant() )
		{
			case "add": SpawnBot(); break;
			case "duel": BotDuel( value > 0f ? value : 400f ); removed = true; break;
			case "near": BotNear( value > 0f ? value : 250f ); break;
			case "kill": KillBots(); removed = true; break;
			case "clear": ClearBots(); removed = true; break;
			default:
				Log.Warning( $"hvh_bots: unknown '{what}' - use <n>, add, duel, near, kill or clear" );
				return;
		}

		PopulationReport( "hvh_bots", removed );
	}

	/// <summary>Practice dummies. `hvh_dummies kill` | `hvh_dummies revive`</summary>
	[ConCmd( "hvh_dummies" )]
	public static void DummiesControl( string what = "" )
	{
		switch ( what.ToLowerInvariant() )
		{
			case "kill": KillDummies(); return;
			case "revive":
				TargetDummy.ReviveAll();
				Log.Info( $"hvh_dummies revive -> alive {TargetDummy.AliveCount}/{TargetDummy.TotalCount}" );
				return;
			default:
				Log.Warning( "hvh_dummies: use kill or revive (hvh_report dummies to look)" );
				return;
		}
	}

	/// <summary>
	/// Put diagnostic state back to a known baseline before a measurement.
	/// `hvh_reset` | `hvh_reset marker` | `hvh_reset counters`
	/// </summary>
	[ConCmd( "hvh_reset" )]
	public static void Reset( string what = "all" )
	{
		var key = what.ToLowerInvariant();

		if ( key is "all" or "marker" ) HitMarkerClear();
		if ( key is "all" or "counters" )
		{
			Weapon.ResetCounters();
			WeaponEffects.ResetReloadCues();
		}

		if ( key is "all" or "weapons" )
		{
			WeaponDefinitions.Reset();
			Log.Info( "  weapon definitions rebuilt from source" );
		}

		if ( key is not ( "all" or "marker" or "counters" or "weapons" ) )
		{
			Log.Warning( $"hvh_reset: unknown '{what}' - use all, marker, counters or weapons" );
			return;
		}

		Log.Info( $"hvh_reset {key} -> done" );
	}

	/// <summary>
	/// Population, printed after anything that changes it.
	///
	/// Task 5 found three commands that logged success while Converge() deleted
	/// their bots a frame later, so a pending trim is called out explicitly
	/// rather than left for the next measurement to discover.
	/// </summary>
	private static void PopulationReport( string label, bool afterDeliberateRemoval = false )
	{
		var manager = BotManager.Current;
		var humans = Player.All.Count( x => !x.IsBot );
		var bots = BotManager.BotCount;

		if ( !manager.IsValid() )
		{
			Log.Warning( $"{label}: no BotManager - nothing here can work" );
			return;
		}

		Log.Info( $"  humans={humans} bots={bots} desired={manager.DesiredPlayers} wanted={manager.WantedBots}" );

		// Destruction is deferred to the end of the frame, so straight after a
		// deliberate kill or clear this count is still counting the dead. Warning
		// there would be crying wolf, and a warning nobody trusts is no warning.
		if ( afterDeliberateRemoval )
		{
			Log.Info( $"  {label}: count settles at the end of the frame after a removal" );
			return;
		}

		if ( manager.WantedBots < bots )
			Log.Warning( $"  {label}: TRIM PENDING - {bots - manager.WantedBots} bot(s) will be " +
				$"deleted within a frame or two. Raise the target first." );
	}

	/// <summary>
	/// The spread cone the active weapon would actually use this instant, by the
	/// same rule <see cref="Weapon"/> applies. Exists so `hvh_centerray` can say
	/// how wrong it is rather than leaving that in a doc nobody reads at 2am.
	/// </summary>
	private static float CurrentSpreadDegrees()
	{
		var weapon = Player.Local?.Inventory?.ActiveWeapon;
		if ( !weapon.IsValid() ) return 0f;

		var stats = weapon.BuildStats();
		if ( stats is null ) return 0f;

		var spread = stats.Spread;
		var movement = Player.Local?.Movement;

		if ( movement.IsValid() )
		{
			var speed = movement.Velocity.WithZ( 0f ).Length;
			spread += stats.MovementInaccuracy * MathF.Min( 1f, speed / MathF.Max( 1f, movement.RunSpeed ) );

			if ( !movement.IsOnGround )
				spread += stats.MovementInaccuracy;
		}

		return spread;
	}

	/// <summary>
	/// Find which holdtype value actually produces a two-handed carry.
	///
	/// The parameter is real - it moves the pose - but the meaning of each value
	/// is not written down anywhere reachable from here. So measure it: a hand
	/// brought up and forward of the pelvis is a hand on a weapon.
	/// </summary>
	private static void HoldTypes()
	{
		var body = Player.Local?.GetComponent<PlayerPresentation>()?.Current;
		var skinned = body.IsValid() ? body.GetComponentInChildren<SkinnedModelRenderer>( true ) : null;

		if ( !skinned.IsValid() )
		{
			Log.Warning( "hvh_report hold: local pawn has no Citizen body" );
			return;
		}

		_ = ProbeHoldTypes( skinned );
	}

	private static async System.Threading.Tasks.Task ProbeHoldTypes( SkinnedModelRenderer skinned )
	{
		Log.Info( "holdtype: hand height and forward reach, relative to the pelvis" );

		for ( var value = 0; value <= 7; value++ )
		{
			skinned.Set( "holdtype", value );
			await GameTask.DelayRealtimeSeconds( 0.45f );

			skinned.TryGetBoneTransform( "pelvis", out var root );
			skinned.TryGetBoneTransform( "hand_L", out var left );
			skinned.TryGetBoneTransform( "hand_R", out var right );

			var fwd = root.Rotation.Forward;
			var lUp = left.Position.z - root.Position.z;
			var rUp = right.Position.z - root.Position.z;
			var lFwd = ( left.Position - root.Position ).Dot( fwd );
			var rFwd = ( right.Position - root.Position ).Dot( fwd );

			Log.Info( $"  holdtype {value}: left up {lUp,6:0.0} fwd {lFwd,6:0.0}" +
				$" | right up {rUp,6:0.0} fwd {rFwd,6:0.0}" +
				$"{( lUp > 6f && lFwd > 6f ? "   <-- both hands up and forward" : "" )}" );
		}

		skinned.Set( "holdtype", 2 );
		Log.Info( "holdtype probe done, left at 2" );
	}

	/// <summary>
	/// Work out which animation graph parameters this model actually responds to.
	///
	/// Parameter names cannot be read back from a compiled graph and Set() on an
	/// unknown name silently does nothing, so guessing is untestable. Instead:
	/// set a candidate, let a frame pass, and see whether the pose moved. A
	/// parameter that changes the hand is real; one that does not, is not.
	/// </summary>
	private static void AnimParams()
	{
		// Probe the LOCAL player's body. A bot is walking, so its pose changes on
		// its own and every parameter looks real - that confounder made the first
		// run report all seventeen as working.
		var body = Player.Local?.GetComponent<PlayerPresentation>()?.Current;
		var skinned = body.IsValid() ? body.GetComponentInChildren<SkinnedModelRenderer>( true ) : null;

		if ( !skinned.IsValid() )
		{
			Log.Warning( "hvh_report anim: local pawn has no Citizen body - set Style to Citizen" );
			return;
		}

		_ = ProbeAnim( skinned );
	}

	private static async System.Threading.Tasks.Task ProbeAnim( SkinnedModelRenderer skinned )
	{
		// World bone transforms relative to the pelvis. The *Local* variants can
		// hand back bind-pose data, which never changes no matter what the graph
		// is doing - a probe that always reads zero proves nothing.
		Vector3 Pose()
		{
			skinned.TryGetBoneTransform( "pelvis", out var root );
			skinned.TryGetBoneTransform( "hand_R", out var r );
			skinned.TryGetBoneTransform( "arm_lower_R", out var l );
			return ( r.Position - root.Position ) + ( l.Position - root.Position );
		}

		var ints = new[] { "holdtype", "holdtype_pose", "holdtype_handedness", "handedness" };
		var bools = new[] { "b_attack", "b_reload", "b_grounded", "b_swim", "b_noclip" };
		var floats = new[] { "move_speed", "move_groundspeed", "move_direction", "duck",
			"aim_body_weight", "aim_head_weight", "wish_x", "wish_y" };

		// How much the pose drifts on its own over the same window. Anything that
		// does not clearly beat this is indistinguishable from doing nothing.
		var drift = 0f;
		for ( var i = 0; i < 4; i++ )
		{
			var a = Pose();
			await GameTask.DelayRealtimeSeconds( 0.25f );
			drift = MathF.Max( drift, ( Pose() - a ).Length );
		}

		var threshold = MathF.Max( 1.5f, drift * 3f );
		Log.Info( $"idle drift {drift:0.00} -> a parameter must move the pose more than {threshold:0.00}" );

		foreach ( var name in ints )
		{
			skinned.Set( name, 0 );
			await GameTask.DelayRealtimeSeconds( 0.15f );
			var before = Pose();

			skinned.Set( name, 2 );
			await GameTask.DelayRealtimeSeconds( 0.25f );
			var delta = ( Pose() - before ).Length;

			Log.Info( $"  int   {name,-22} pose moved {delta:0.00}{( delta > threshold ? "   <-- REAL" : "" )}" );
			skinned.Set( name, 0 );
		}

		foreach ( var name in bools )
		{
			skinned.Set( name, false );
			await GameTask.DelayRealtimeSeconds( 0.15f );
			var before = Pose();

			skinned.Set( name, true );
			await GameTask.DelayRealtimeSeconds( 0.25f );
			var delta = ( Pose() - before ).Length;

			Log.Info( $"  bool  {name,-22} pose moved {delta:0.00}{( delta > threshold ? "   <-- REAL" : "" )}" );
			skinned.Set( name, false );
		}

		foreach ( var name in floats )
		{
			skinned.Set( name, 0f );
			await GameTask.DelayRealtimeSeconds( 0.15f );
			var before = Pose();

			skinned.Set( name, name.Contains( "direction" ) ? 90f : 1f );
			await GameTask.DelayRealtimeSeconds( 0.25f );
			var delta = ( Pose() - before ).Length;

			Log.Info( $"  float {name,-22} pose moved {delta:0.00}{( delta > threshold ? "   <-- REAL" : "" )}" );
			skinned.Set( name, 0f );
		}

		Log.Info( "probe done" );
	}

	/// <summary>
	/// Ask the Citizen model which bones and attachments it actually has.
	///
	/// Guessing bone names from a compiled model is how an afternoon disappears -
	/// TryGetBoneTransform answers definitively, so ask it.
	/// </summary>
	private static void Bones()
	{
		var skinned = Game.ActiveScene?.GetAllComponents<SkinnedModelRenderer>()
			.FirstOrDefault( x => x.Model is not null );

		if ( !skinned.IsValid() )
		{
			Log.Warning( "hvh_report bones: no SkinnedModelRenderer in the scene" );
			return;
		}

		Log.Info( $"model: {skinned.Model?.Name}" );

		var candidates = new[]
		{
			"hand_R", "hand_L", "hold_R", "hold_L", "arm_upper_R", "arm_lower_R",
			"arm_upper_L", "arm_lower_L", "clavicle_R", "clavicle_L",
			"spine_0", "spine_1", "spine_2", "pelvis", "head", "hand", "hand2",
			"finger_index_0_R", "weapon", "hold",
		};

		foreach ( var name in candidates )
		{
			if ( skinned.TryGetBoneTransform( name, out var tx ) )
				Log.Info( $"  BONE {name,-18} world {tx.Position}" );
		}

		foreach ( var name in new[] { "hand_R", "hand_L", "hold_R", "hold_L", "eyes", "muzzle", "weapon" } )
		{
			var a = skinned.GetAttachment( name, true );
			if ( a.HasValue )
				Log.Info( $"  ATTACH {name,-16} world {a.Value.Position}" );
		}
	}

	/// <summary>Presentation state - what the local pawn is actually showing.</summary>
	private static void ViewModel()
	{
		var player = Player.Local;
		if ( !player.IsValid() )
		{
			Log.Warning( "hvh_report view: no local player" );
			return;
		}

		var vm = player.GetComponent<WeaponViewModel>();
		var pres = player.GetComponent<PlayerPresentation>();

		Log.Info( vm.IsValid()
			? $"viewmodel: {vm.Status} | object={( vm.Current.IsValid() ? vm.Current.Name : "none" )}" +
			  $" | weapon={vm.CurrentWeapon?.DisplayName ?? "none"}"
			: "viewmodel: component missing from the pawn" );

		var held = player.GetComponent<HeldWeaponPresentation>();
		if ( held.IsValid() && held.Current.IsValid() )
		{
			var body = pres?.Current;
			var sk = body.IsValid() ? body.GetComponentInChildren<SkinnedModelRenderer>( true ) : null;
			if ( sk.IsValid() && sk.TryGetBoneTransform( "hand_L", out var lh ) )
			{
				var grip = held.Current.WorldPosition + held.Current.WorldRotation * held.SupportGrip;
				Log.Info( $"support hand: hand_L is {lh.Position.Distance( grip ):0.0}u from the foregrip" );
			}
		}

		Log.Info( pres.IsValid()
			? $"body: team={pres.CurrentTeam} | object={( pres.Current.IsValid() ? pres.Current.Name : "none" )}"
			: "body: component missing from the pawn" );

		foreach ( var other in Player.All )
		{
			var p2 = other.GetComponent<PlayerPresentation>();
			var h = other.GetComponent<HeldWeaponPresentation>();
			Log.Info( $"  {other.State?.DisplayName}: team={other.Team}" +
				$" body={( p2.IsValid() && p2.Current.IsValid() ? "yes" : "NO" )}" +
				$" | held={( h.IsValid() ? ( h.Current.IsValid() ? "yes" : "NO" ) : "no component" )}" +
				$" [{( h.IsValid() ? h.Status : "-" )}]" );
		}
	}

	/// <summary>
	/// Report footstep state for every pawn. `hvh_report steps`
	/// </summary>
	public static void Steps()
	{
		var players = Player.All.ToArray();
		if ( players.Length == 0 )
		{
			Log.Info( "hvh_report steps: no pawns" );
			return;
		}

		foreach ( var player in players )
		{
			var steps = player.GetComponent<PlayerFootsteps>();
			var movement = player.Movement;

			if ( !steps.IsValid() || !movement.IsValid() )
			{
				Log.Warning( $"hvh_report steps: {player.State?.DisplayName} has no footstep component" );
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
	/// numbers. `hvh_report hits` reports; `hvh_reset counters` zeroes them.
	/// </summary>
	public static void HitDebug( int reset = 0 )
	{
		if ( reset != 0 )
		{
			Weapon.ResetCounters();
			Log.Info( "hvh_reset counters -> counters zeroed" );
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
			$" markerShows={HitMarker.ShowCount}" +
			$" reloadCues={WeaponEffects.ReloadCues}" );
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
	/// Report each target's origin against its actual world bounds. `hvh_report bounds`
	/// Exists because hit zones were being measured from the origin, which is at
	/// the feet for a player and at the middle for a dummy.
	/// </summary>
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

	/// <summary>
	/// Reload the active weapon through its real code path. `hvh_reload`
	/// Spends ammo first if the magazine is full, since a full magazine refuses.
	/// </summary>
	[ConCmd( "hvh_reload" )]
	public static void Reload()
	{
		var weapon = Player.Local?.Inventory?.ActiveWeapon;
		if ( !weapon.IsValid() )
		{
			Log.Warning( "hvh_reload: no active weapon" );
			return;
		}

		var before = weapon.Ammo;
		weapon.RequestReload();

		Log.Info( $"hvh_reload -> {weapon.DisplayName} ammo {before}/{weapon.Reserve}, " +
			$"reloading={weapon.IsReloading}" );

		if ( !weapon.IsReloading )
			Log.Warning( "hvh_reload: refused - magazine already full, or no reserve. " +
				"Fire a shot first (hvh_fire), then retry." );
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

	/// <summary>Kill every dummy, to exercise the round-end path. `hvh_dummies kill`</summary>
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

		Log.Info( $"hvh_dummies kill -> killed {killed}, alive {TargetDummy.AliveCount}/{TargetDummy.TotalCount}" );
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
	/// Answers "did the shot miss, or did the hit not register?". `hvh_centerray`
	/// </summary>
	[ConCmd( "hvh_centerray" )]
	public static void CenterRay()
	{
		var player = Player.Local;
		if ( !player.IsValid() )
		{
			Log.Warning( "hvh_centerray: no local player" );
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
			Log.Info( $"hvh_centerray: CENTRE RAY ONLY (no spread; a real shot scatters up to {CurrentSpreadDegrees():0.##} deg) -> hit nothing" );
			return;
		}

		var health = trace.GameObject.GetComponentInParent<HealthComponent>();
		var state = trace.GameObject.GetComponentInParent<PlayerState>();

		Log.Info( $"hvh_centerray: CENTRE RAY ONLY - no spread modelled. A real shot " +
			$"right now scatters up to {CurrentSpreadDegrees():0.##} deg from this line." );
		Log.Info(
			$"  hit '{trace.GameObject.Name}' at {trace.Distance:0}u " +
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

	/// <summary>Report every dummy's health. `hvh_report dummies`</summary>
	public static void Dummies()
	{
		Log.Info( $"dummies alive {TargetDummy.AliveCount}/{TargetDummy.TotalCount}" );

		foreach ( var dummy in TargetDummy.All )
			Log.Info( $"  {dummy.DisplayName}: hp={dummy.Health?.Health} alive={dummy.IsAlive}" );
	}

	/// <summary>Leave the match and go back to the menu. `hvh_loadscene menu`</summary>
	public static void ToMenu()
	{
		var exit = Game.ActiveScene?.GetAllComponents<GameExitHandler>().FirstOrDefault();
		if ( !exit.IsValid() )
		{
			Log.Warning( "hvh_loadscene menu: no GameExitHandler in this scene" );
			return;
		}

		Log.Info( "hvh_loadscene menu -> returning to menu" );
		exit.ReturnToMenu();
	}

	/// <summary>Load a scene by path - the same hop the menu's PLAY makes. `hvh_loadscene`</summary>
	[ConCmd( "hvh_loadscene" )]
	public static void LoadScene( string path = "scenes/game.scene" )
	{
		// "menu" is not just the menu scene - leaving a match has to go through
		// GameExitHandler so the lobby is torn down properly.
		if ( path.Equals( "menu", StringComparison.OrdinalIgnoreCase ) )
		{
			ToMenu();
			return;
		}

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

	/// <summary>Spawn one bot on the opposing team. `hvh_bots add`</summary>
	public static void SpawnBot()
	{
		var manager = BotManager.Current;
		if ( !manager.IsValid() )
		{
			Log.Warning( "hvh_bots add: no BotManager in this scene" );
			return;
		}

		EnsureRoomForBots( manager, 1 );

		var bot = manager.SpawnBot();
		Log.Info( bot.IsValid()
			? $"hvh_bots add -> spawned, bots now {BotManager.BotCount}"
			: "hvh_bots add: spawn failed" );
	}

	/// <summary>
	/// Teleport the nearest bot to just in front of you, for close-range
	/// testing. The arena's centre cover blocks most spawn-to-spawn diagonals.
	/// `hvh_bots near 250`
	/// </summary>
	public static void BotNear( float distance = 250f )
	{
		var player = Player.Local;
		if ( !player.IsValid() )
		{
			Log.Warning( "hvh_bots near: no local player" );
			return;
		}

		var bot = BotManager.Bots.FirstOrDefault();
		if ( !bot.IsValid() )
		{
			Log.Warning( "hvh_bots near: no bot" );
			return;
		}

		// Straight ahead is often outside the arena - the shooter spawns in a
		// corner facing out, and player + forward * distance lands past a wall.
		// That placed the bot out of the world and every scripted shot hit the
		// wall instead, which looked like a hit-detection failure. So try a fan
		// of directions and take the first that is both standable and visible.
		var eye = player.AimRay.Position;
		var placed = Vector3.Zero;
		var found = false;
		var tried = 0;
		var reasons = new List<string>();

		foreach ( var offset in new[] { 0f, 30f, -30f, 60f, -60f, 90f, -90f, 130f, -130f, 180f } )
		{
			tried++;

			var angles = player.EyeAngles with { pitch = 0f };
			angles.yaw += offset;

			var candidate = player.WorldPosition + angles.Forward.Normal * distance;

			// Drop it onto the floor. World geometry only - a trace that can hit
			// the bot already standing there put it on its own head at z=128.
			var ground = player.Scene.Trace
				.Ray( candidate + Vector3.Up * 256f, candidate + Vector3.Down * 1024f )
				.WithTag( "solid" )
				.Run();

			if ( !ground.Hit )
			{
				reasons.Add( $"{offset:+0;-0;0}deg: nothing to stand on" );
				continue;
			}

			candidate = ground.EndPosition;

			// Useless if the shooter cannot see it - check against the chest.
			// Ignore ourselves. The eye sits inside our own body collider, and a
			// tag filter does NOT get you out of it - pawn colliders are hit by
			// a "solid" trace too, so this blocked every direction at 0u.
			var los = player.Scene.Trace
				.Ray( eye, candidate + Vector3.Up * 40f )
				.IgnoreGameObjectHierarchy( player.GameObject )
				.WithTag( "solid" )
				.Run();

			if ( los.Hit )
			{
				reasons.Add( $"{offset:+0;-0;0}deg: sight blocked by '{los.GameObject?.Name}' at {los.Distance:0}u" );
				continue;
			}

			placed = candidate;
			found = true;
			break;
		}

		if ( !found )
		{
			Log.Warning( $"hvh_bots near: no standable spot with line of sight within " +
				$"{distance:0}u after {tried} directions - bot NOT moved. Move and retry." );

			// Say why, or the next person just runs it again and gets the same
			// silence. Every rejection reason, in order.
			foreach ( var reason in reasons )
				Log.Warning( $"    {reason}" );

			return;
		}

		// The host owns the bot, so writing its transform here is authoritative.
		bot.WorldPosition = placed;
		bot.Movement?.ClearVelocity();

		// Verify the effect rather than assume it: confirm where it ended up and
		// that the shooter can actually see it from here.
		var check = player.Scene.Trace
			.Ray( eye, bot.WorldPosition + Vector3.Up * 40f )
			.IgnoreGameObjectHierarchy( player.GameObject )
			.WithTag( "solid" )
			.Run();

		Log.Info( $"hvh_bots near -> {bot.State?.DisplayName} at {placed} " +
			$"({eye.Distance( placed ):0}u away, {tried} direction(s) tried)" );

		if ( check.Hit )
			Log.Warning( "hvh_bots near: placed, but line of sight is blocked - shots will not land." );
	}

	/// <summary>Why is the bot not shooting? `hvh_report bots`</summary>
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

		if ( !any ) Log.Info( "hvh_report bots: no bots" );
	}

	/// <summary>
	/// Spawn two bots on opposing teams facing each other, to watch bot combat
	/// without a human in the loop. `hvh_bots duel`
	/// </summary>
	public static void BotDuel( float gap = 400f )
	{
		var manager = BotManager.Current;
		var player = Player.Local;
		if ( !manager.IsValid() || !player.IsValid() )
		{
			Log.Warning( "hvh_bots duel: need a BotManager and a local player" );
			return;
		}

		// Fixed positions on the arena's south strip, verified by trace to have a
		// clear line between them. Placing these relative to the player put them
		// outside the walls, where they fell out of the world and ended up
		// 240,000 units away.
		var left = new Vector3( -gap * 0.5f, -450f, 16f );
		var right = new Vector3( gap * 0.5f, -450f, 16f );

		// A duel means exactly two bots. Any bot already in the arena would push
		// the count past the target and get one of the duellists trimmed instead,
		// so clear first and then make room for precisely two.
		var existing = BotManager.BotCount;
		if ( existing > 0 )
		{
			ClearBots();
			Log.Info( $"hvh_bots duel: cleared {existing} existing bot(s) so the duel is a duel" );
		}

		EnsureRoomForBots( manager, 2 );

		var a = manager.SpawnBot( Team.Vanguard, left );
		var b = manager.SpawnBot( Team.Syndicate, right );

		Log.Info( $"hvh_bots duel -> {( a.IsValid() ? a.State?.DisplayName : "fail" )} vs " +
			$"{( b.IsValid() ? b.State?.DisplayName : "fail" )}, gap {gap}u" );
	}

	/// <summary>
	/// Set the total player target (humans + bots) and report convergence.
	/// `hvh_bots 4`
	/// </summary>
	public static void Target( int total = 2 )
	{
		var manager = BotManager.Current;
		if ( !manager.IsValid() )
		{
			Log.Warning( "hvh_bots: no BotManager" );
			return;
		}

		manager.DesiredPlayers = total;

		Log.Info( $"hvh_bots {total} -> humans={Player.All.Count( x => !x.IsBot )} " +
			$"bots={BotManager.BotCount} wantedBots={manager.WantedBots}" );
	}

	/// <summary>
	/// Kill every bot through the normal damage path, credited to you.
	/// Exercises death, scoring, kill feed, round elimination and respawn
	/// without depending on scripted aim landing. `hvh_bots kill`
	/// </summary>
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

		Log.Info( $"hvh_bots kill -> killed {killed}" );
	}

	/// <summary>Remove every bot. `hvh_bots clear`</summary>
	public static void ClearBots()
		=> Log.Info( $"hvh_bots clear -> removed {BotManager.RemoveAllBots()}" );

	/// <summary>List every player pawn and who drives it. `hvh_report players`</summary>
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

	/// <summary>Current hit-marker state on THIS machine. `hvh_report marker`</summary>
	public static void HitMarkerState()
		=> Log.Info( $"hitmarker visible={HitMarker.Visible} kind={HitMarker.Kind} fade={HitMarker.Fade:0.00}" );

	/// <summary>Clear the hit marker, so a test starts from a known state. `hvh_reset marker`</summary>
	public static void HitMarkerClear()
	{
		HitMarker.Clear();
		Log.Info( "hitmarker cleared" );
	}

	/// <summary>Print the local player's live state. `hvh_report state`</summary>
	public static void State()
	{
		var player = Player.Local;
		if ( !player.IsValid() )
		{
			Log.Info( "hvh_report state: no local player" );
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
