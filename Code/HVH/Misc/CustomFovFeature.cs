using System;
using HvH.Mods;

namespace HvH.HVH;

/// <summary>
/// Overrides the camera's field of view, independent of the player's own
/// setting in the options menu.
/// </summary>
public sealed class CustomFovFeature : ModFeature
{
	public override string Name => "Custom FOV";
	public override string Category => "Misc";
	public override string Description => "Forces a field of view, ignoring your settings.";

	public float FieldOfView
	{
		get => Setting( "Fov", 110f );
		set => SetSetting( "Fov", Math.Clamp( value, 50f, 140f ) );
	}

	protected override IEnumerable<ModSetting> BuildSettings()
	{
		yield return ModSetting.Slider( "Field of View", () => FieldOfView, v => FieldOfView = v, 50f, 140f, 1f );
	}

	public override void Disable()
	{
		var camera = LocalPlayer?.GetComponent<PlayerCamera>();
		if ( camera.IsValid() )
			camera.FovOverride = null;
	}

	protected override void OnTick()
	{
		var camera = LocalPlayer?.GetComponent<PlayerCamera>();
		if ( !camera.IsValid() ) return;

		camera.FovOverride = FieldOfView;
	}
}
