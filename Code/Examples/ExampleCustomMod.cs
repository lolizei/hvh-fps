using System;
using Sandbox.Rendering;
using HvH.Mods;
using HvH.UI;

namespace HvH.Examples;

/// <summary>
/// A worked example of a third-party mod.
///
/// It exists to prove six things about the framework:
///   1. the game discovers this mod without anyone editing a registry,
///   2. it exposes its own features,
///   3. it has its own config, saved separately from every other mod,
///   4. it brings its own UI,
///   5. it does NOT use the default HVH menu, and
///   6. it coexists with the default mod - both are loaded, both work, and
///      each has its own toggle key.
///
/// Everything here uses only the public API in HvH.Mods. Nothing reaches into
/// the game's internals, which is exactly the constraint a real third-party
/// author would be under.
/// </summary>
public sealed class ExampleCustomMod : ModBase
{
	public override string Id => "example-custom";
	public override string Name => "Example Mod";
	public override string Author => "Third Party";
	public override string Version => "0.1.0";

	private IModMenu _menu;

	public override IModMenu Menu => _menu;

	protected override void OnInitialize()
	{
		Register( new TriggerAssistFeature() );
		Register( new VelocityReadoutFeature() );
		Register( new MatchInfoFeature() );

		// Its own key and its own panel - the default menu is never involved.
		_menu = new ExampleModMenuBinding( this, Context.Manager );
	}
}

internal sealed class ExampleModMenuBinding : RazorModMenu<ExampleModMenu>
{
	private readonly IGameMod _mod;

	public ExampleModMenuBinding( IGameMod mod, ModManager manager )
		: base( manager, "AltModMenu", "Example Mod" )
	{
		_mod = mod;
	}

	protected override void Configure( ExampleModMenu panel ) => panel.Mod = _mod;
}

/// <summary>
/// Fires the moment the crosshair crosses an enemy, if the player is holding
/// the alternate fire key as a deliberate opt-in.
/// </summary>
public sealed class TriggerAssistFeature : ModFeature
{
	public override string Name => "Trigger Assist";
	public override string Category => "Combat";
	public override string Description => "Shoots when your crosshair crosses a target.";

	public float Delay
	{
		get => Setting( "Delay", 0.05f );
		set => SetSetting( "Delay", value );
	}

	/// <summary>Only act while the secondary key is held, so it can't run away with itself.</summary>
	public bool HoldToUse
	{
		get => Setting( "HoldToUse", true );
		set => SetSetting( "HoldToUse", value );
	}

	private float _armedAt;

	protected override IEnumerable<ModSetting> BuildSettings()
	{
		yield return ModSetting.Slider( "Delay", () => Delay, v => Delay = v, 0f, 0.5f, 0.01f );
		yield return ModSetting.Toggle( "Hold To Use", () => HoldToUse, v => HoldToUse = v );
	}

	protected override void OnTick()
	{
		if ( !CanAct ) return;
		if ( HoldToUse && !Input.Down( "Attack2" ) ) return;

		// A tight cone: this is "your crosshair is already on them", not aim assist.
		var target = TargetSelector.Find( Context, fovLimit: 1.5f, requireVisible: true );
		if ( !target.IsValid )
		{
			_armedAt = 0f;
			return;
		}

		if ( _armedAt <= 0f )
			_armedAt = Time.Now;

		if ( Time.Now - _armedAt < Delay ) return;

		var weapon = LocalPlayer.Inventory?.ActiveWeapon;
		if ( !weapon.IsValid() || !weapon.CanFire() ) return;

		Input.SetAction( "Attack1", true );
	}

	public override void Disable() => _armedAt = 0f;
}

/// <summary>Speedometer. Handy for movement practice.</summary>
public sealed class VelocityReadoutFeature : ModFeature
{
	public override string Name => "Velocity Readout";
	public override string Category => "Visual";
	public override string Description => "Shows your horizontal speed.";

	public bool ShowPeak
	{
		get => Setting( "ShowPeak", true );
		set => SetSetting( "ShowPeak", value );
	}

	private float _peak;

	protected override IEnumerable<ModSetting> BuildSettings()
	{
		yield return ModSetting.Toggle( "Show Peak", () => ShowPeak, v => ShowPeak = v );
	}

	public override void Disable() => _peak = 0f;

	protected override void OnTick()
	{
		var player = LocalPlayer;
		if ( !player.IsValid() || !player.Movement.IsValid() ) return;

		var camera = player.GetComponentInChildren<CameraComponent>();
		if ( !camera.IsValid() ) return;

		var speed = player.Movement.Velocity.WithZ( 0f ).Length;

		if ( player.Movement.IsOnGround ) _peak = 0f;
		else _peak = MathF.Max( _peak, speed );

		var hud = camera.Hud;
		var centre = new Vector2( Screen.Width * 0.5f, Screen.Height * 0.72f );

		hud.DrawText( $"{speed:0}", 26f, Color.White, centre, TextFlag.Center );

		if ( ShowPeak && _peak > 0f )
		{
			hud.DrawText( $"peak {_peak:0}", 13f, new Color( 1f, 1f, 1f, 0.5f ),
				centre + new Vector2( 0f, 24f ), TextFlag.Center );
		}
	}
}

/// <summary>Round state and how many people are still standing on each side.</summary>
public sealed class MatchInfoFeature : ModFeature
{
	public override string Name => "Match Info";
	public override string Category => "Information";
	public override string Description => "Round phase and living player counts.";

	protected override void OnTick()
	{
		var player = LocalPlayer;
		if ( !player.IsValid() ) return;

		var camera = player.GetComponentInChildren<CameraComponent>();
		if ( !camera.IsValid() ) return;

		var round = Context.Round;
		if ( !round.IsValid() ) return;

		var vanguard = RoundManager.CountAlive( Team.Vanguard );
		var syndicate = RoundManager.CountAlive( Team.Syndicate );

		var text = $"{round.State}  |  V {vanguard} - {syndicate} S";

		camera.Hud.DrawText( text, 14f, new Color( 0.7f, 0.95f, 1f ),
			new Vector2( 24f, Screen.Height * 0.5f ), TextFlag.LeftCenter );
	}
}
