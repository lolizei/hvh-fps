using System;
using HvH.Mods;

namespace HvH.HVH;

/// <summary>
/// Air-strafe helper. While airborne it nudges the view yaw in the direction
/// the player is already strafing, which is the input pattern that builds speed
/// in Source-style movement.
///
/// It only assists an intent the player has expressed - with no strafe key held
/// it does nothing at all.
/// </summary>
public sealed class StrafeAssistFeature : ModFeature
{
	public override string Name => "Strafe Assist";
	public override string Category => "Movement";
	public override string Description => "Turns with your air-strafe to help build speed.";

	/// <summary>Degrees per second of assistance at full strength.</summary>
	public float Strength
	{
		get => Setting( "Strength", 120f );
		set => SetSetting( "Strength", value );
	}

	/// <summary>Stop helping once we are already this fast.</summary>
	public float SpeedCap
	{
		get => Setting( "SpeedCap", 600f );
		set => SetSetting( "SpeedCap", value );
	}

	protected override IEnumerable<ModSetting> BuildSettings()
	{
		yield return ModSetting.Slider( "Strength", () => Strength, v => Strength = v, 0f, 400f, 5f );
		yield return ModSetting.Slider( "Speed Cap", () => SpeedCap, v => SpeedCap = v, 200f, 1500f, 10f );
	}

	protected override void OnTick()
	{
		if ( !CanAct ) return;

		var player = LocalPlayer;
		var movement = player.Movement;
		if ( !movement.IsValid() || movement.IsOnGround ) return;

		if ( movement.Velocity.WithZ( 0f ).Length >= SpeedCap ) return;

		// AnalogMove.y is the strafe axis: positive left, negative right.
		var strafe = Input.AnalogMove.y;
		if ( MathF.Abs( strafe ) < 0.1f ) return;

		var angles = player.EyeAngles;
		angles.yaw += MathF.Sign( strafe ) * Strength * Time.Delta;

		player.EyeAngles = angles;
	}
}
