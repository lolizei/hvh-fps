using System;
using HvH.Mods;

namespace HvH.HVH;

/// <summary>
/// Tightens a weapon's spread cone, including the penalty for moving.
/// Same hook as recoil control, applied to the accuracy numbers instead.
/// </summary>
public sealed class SpreadControlFeature : ModFeature
{
	public override string Name => "Spread Control";
	public override string Category => "Combat";
	public override string Description => "Shrinks the accuracy cone and the movement penalty.";

	/// <summary>0 = untouched, 1 = perfectly accurate.</summary>
	public float Strength
	{
		get => Setting( "Strength", 0.8f );
		set => SetSetting( "Strength", Math.Clamp( value, 0f, 1f ) );
	}

	/// <summary>Also cancel the extra spread from running and jumping.</summary>
	public bool IncludeMovement
	{
		get => Setting( "IncludeMovement", true );
		set => SetSetting( "IncludeMovement", value );
	}

	protected override IEnumerable<ModSetting> BuildSettings()
	{
		yield return ModSetting.Slider( "Strength", () => Strength, v => Strength = v, 0f, 1f, 0.05f );
		yield return ModSetting.Toggle( "Include Movement", () => IncludeMovement, v => IncludeMovement = v );
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
		if ( !weapon.IsValid() || weapon.IsProxy ) return;

		var keep = 1f - Math.Clamp( Strength, 0f, 1f );

		stats.Spread *= keep;

		if ( IncludeMovement )
			stats.MovementInaccuracy *= keep;
	}
}
