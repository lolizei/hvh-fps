using System;
using HvH.Mods;

namespace HvH.HVH;

/// <summary>
/// Reduces the upward kick a weapon applies to the view.
///
/// Works through the weapon stats hook rather than by fighting the camera
/// afterwards, so the shot itself is affected consistently and there is no
/// visible tug-of-war on the view.
/// </summary>
public sealed class RecoilControlFeature : ModFeature
{
	public override string Name => "Recoil Control";
	public override string Category => "Combat";
	public override string Description => "Scales weapon recoil down. 100% removes it entirely.";

	/// <summary>0 = full recoil, 1 = none.</summary>
	public float Strength
	{
		get => Setting( "Strength", 0.75f );
		set => SetSetting( "Strength", Math.Clamp( value, 0f, 1f ) );
	}

	protected override IEnumerable<ModSetting> BuildSettings()
	{
		yield return ModSetting.Slider( "Strength", () => Strength, v => Strength = v, 0f, 1f, 0.05f );
	}

	public override void Enable()
	{
		if ( Context is null ) return;

		Context.Events.WeaponStats += OnWeaponStats;
	}

	public override void Disable()
	{
		if ( Context is null ) return;

		Context.Events.WeaponStats -= OnWeaponStats;
	}

	private void OnWeaponStats( Weapon weapon, WeaponStats stats )
	{
		// Only touch our own shots - this hook also runs on the host for
		// everyone else's weapons.
		if ( !weapon.IsValid() || weapon.IsProxy ) return;

		stats.Recoil *= 1f - Math.Clamp( Strength, 0f, 1f );
	}
}
