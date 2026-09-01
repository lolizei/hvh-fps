using System;

namespace HvH;

/// <summary>Where a weapon lives in the inventory.</summary>
public enum WeaponSlot
{
	Primary = 1,
	Secondary = 2,
	Melee = 3,
}

public enum HitZone
{
	Body,
	Head,
	Limb,
}

/// <summary>
/// Everything that makes one weapon different from another. Authored as a
/// .weapon asset in the editor, or taken from <see cref="WeaponDefinitions"/>
/// for the built-in guns.
///
/// This is the *base* data and is never mutated at runtime - the weapon copies
/// it into a <see cref="WeaponStats"/> each shot so mods can alter a shot
/// without permanently editing the asset.
/// </summary>
[AssetType( Name = "Weapon", Extension = "weapon", Category = "HVH" )]
public sealed class WeaponData : GameResource
{
	[Property] public string DisplayName { get; set; } = "Weapon";
	[Property] public WeaponSlot Slot { get; set; } = WeaponSlot.Primary;

	[Property] public float Damage { get; set; } = 25f;

	/// <summary>Rounds per minute.</summary>
	[Property] public float FireRate { get; set; } = 600f;

	[Property] public bool Automatic { get; set; } = true;

	[Property] public int MagazineSize { get; set; } = 30;
	[Property] public int ReserveAmmo { get; set; } = 90;
	[Property] public float ReloadTime { get; set; } = 2.5f;

	/// <summary>Base cone half-angle in degrees when standing still.</summary>
	[Property] public float Spread { get; set; } = 0.6f;

	/// <summary>Degrees of upward kick per shot.</summary>
	[Property] public float Recoil { get; set; } = 1.4f;

	[Property] public float Range { get; set; } = 8192f;

	/// <summary>
	/// Extra spread at full sprint, in degrees. This is what makes running and
	/// gunning bad without banning it outright.
	/// </summary>
	[Property] public float MovementInaccuracy { get; set; } = 4f;

	[Property] public float HeadMultiplier { get; set; } = 4f;
	[Property] public float BodyMultiplier { get; set; } = 1f;
	[Property] public float LimbMultiplier { get; set; } = 0.75f;

	/// <summary>Seconds between the shot and the weapon being ready to fire again.</summary>
	public float FireDelay => FireRate <= 0f ? 0.1f : 60f / FireRate;

	public float MultiplierFor( HitZone zone ) => zone switch
	{
		HitZone.Head => HeadMultiplier,
		HitZone.Limb => LimbMultiplier,
		_ => BodyMultiplier,
	};
}
