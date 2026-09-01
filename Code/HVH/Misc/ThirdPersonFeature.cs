using System;
using HvH.Mods;

namespace HvH.HVH;

/// <summary>
/// Pulls the camera back behind the player. Traces to the desired camera
/// position so the view never ends up inside geometry.
/// </summary>
public sealed class ThirdPersonFeature : ModFeature
{
	public override string Name => "Third Person";
	public override string Category => "Misc";
	public override string Description => "Moves the camera behind you.";

	public float Distance
	{
		get => Setting( "Distance", 140f );
		set => SetSetting( "Distance", value );
	}

	/// <summary>Sideways offset, for an over-the-shoulder view.</summary>
	public float ShoulderOffset
	{
		get => Setting( "ShoulderOffset", 26f );
		set => SetSetting( "ShoulderOffset", value );
	}

	protected override IEnumerable<ModSetting> BuildSettings()
	{
		yield return ModSetting.Slider( "Distance", () => Distance, v => Distance = v, 40f, 400f, 5f );
		yield return ModSetting.Slider( "Shoulder Offset", () => ShoulderOffset, v => ShoulderOffset = v, -80f, 80f, 2f );
	}

	public override void Disable()
	{
		var camera = LocalPlayer?.GetComponent<PlayerCamera>();
		if ( camera.IsValid() )
			camera.PositionOverride = null;
	}

	protected override void OnTick()
	{
		if ( !CanAct )
			return;

		var player = LocalPlayer;
		var camera = player.GetComponent<PlayerCamera>();
		if ( !camera.IsValid() ) return;

		var rotation = player.EyeAngles.ToRotation();
		var eye = player.AimRay.Position;
		var wanted = eye - rotation.Forward * Distance + rotation.Right * ShoulderOffset;

		// Keep the camera out of walls.
		var trace = player.Scene.Trace
			.Ray( eye, wanted )
			.Radius( 8f )
			.IgnoreGameObjectHierarchy( player.GameObject )
			.Run();

		camera.PositionOverride = trace.Hit ? trace.EndPosition : wanted;
	}
}
