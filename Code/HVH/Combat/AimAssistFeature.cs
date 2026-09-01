using System;
using HvH.Mods;

namespace HvH.HVH;

/// <summary>
/// Pulls the player's view toward the selected target while they hold fire.
///
/// This is a mechanic of this game, not a tool aimed at anything outside it: it
/// reads the scene it is running in and writes to the local pawn's own view
/// angles, which the player already controls.
///
/// Smoothing is deliberate - at Smoothing 1 it snaps, at 20 it is a gentle
/// drag. Every number here is a config value so the menu can expose it.
/// </summary>
public sealed class AimAssistFeature : ModFeature
{
	public override string Name => "Aim Assist";
	public override string Category => "Combat";
	public override string Description => "Steers your view toward a target while firing.";

	/// <summary>Cone the target must be inside, in degrees.</summary>
	public float FieldOfView
	{
		get => Setting( "Fov", 25f );
		set => SetSetting( "Fov", value );
	}

	/// <summary>Higher is slower. 1 snaps instantly.</summary>
	public float Smoothing
	{
		get => Setting( "Smoothing", 8f );
		set => SetSetting( "Smoothing", value );
	}

	public bool RequireVisible
	{
		get => Setting( "RequireVisible", true );
		set => SetSetting( "RequireVisible", value );
	}

	/// <summary>Only assist while the trigger is held.</summary>
	public bool OnlyWhileFiring
	{
		get => Setting( "OnlyWhileFiring", true );
		set => SetSetting( "OnlyWhileFiring", value );
	}

	public TargetBone Bone
	{
		get => (TargetBone)Setting( "Bone", (int)TargetBone.Head );
		set => SetSetting( "Bone", (int)value );
	}

	public TargetMode Mode
	{
		get => (TargetMode)Setting( "Mode", (int)TargetMode.Crosshair );
		set => SetSetting( "Mode", (int)value );
	}

	/// <summary>Last target picked, so the ESP can highlight it.</summary>
	public Player CurrentTarget { get; private set; }

	protected override IEnumerable<ModSetting> BuildSettings()
	{
		yield return ModSetting.Slider( "Field of View", () => FieldOfView, v => FieldOfView = v, 1f, 180f, 1f );
		yield return ModSetting.Slider( "Smoothing", () => Smoothing, v => Smoothing = v, 1f, 30f, 0.5f );
		yield return ModSetting.Toggle( "Require Visible", () => RequireVisible, v => RequireVisible = v );
		yield return ModSetting.Toggle( "Only While Firing", () => OnlyWhileFiring, v => OnlyWhileFiring = v );
		yield return ModSetting.Choice( "Target Bone", () => (int)Bone, v => Bone = (TargetBone)v,
			"Head", "Chest", "Pelvis", "Nearest" );
		yield return ModSetting.Choice( "Selection", () => (int)Mode, v => Mode = (TargetMode)v,
			"Crosshair", "Distance", "Weakest" );
	}

	protected override void OnTick()
	{
		CurrentTarget = null;

		if ( !CanAct ) return;
		if ( OnlyWhileFiring && !Input.Down( "Attack1" ) ) return;

		var target = TargetSelector.Find(
			Context,
			FieldOfView,
			Bone,
			Mode,
			RequireVisible );

		if ( !target.IsValid ) return;

		CurrentTarget = target.Player;

		var player = LocalPlayer;
		var eye = player.AimRay.Position;
		var wanted = Rotation.LookAt( ( target.Point - eye ).Normal ).Angles();

		// Frame-rate independent approach, so assist strength doesn't change
		// with the player's FPS.
		var step = Smoothing <= 1f
			? 1f
			: MathF.Min( 1f, Time.Delta * ( 60f / Smoothing ) );

		var current = player.EyeAngles;
		var blended = current.LerpTo( wanted, step );

		blended.pitch = Math.Clamp( blended.pitch, -89f, 89f );
		blended.roll = 0f;

		player.EyeAngles = blended;
	}

	public override void Disable() => CurrentTarget = null;
}
