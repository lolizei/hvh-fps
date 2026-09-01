using System;

namespace HvH;

/// <summary>
/// A mutable, per-shot copy of a weapon's numbers.
///
/// This type is the seam the HVH mod framework hooks into: every shot builds
/// fresh stats from the base <see cref="WeaponData"/>, then hands them to
/// <see cref="Weapon.BuildStats"/> so mods can change damage, spread or recoil
/// for that shot only. Nothing ever writes back to the asset.
/// </summary>
public sealed class WeaponStats
{
	public float Damage;
	public float Spread;
	public float Recoil;
	public float Range;
	public float FireDelay;
	public float MovementInaccuracy;
	public float HeadMultiplier;
	public float BodyMultiplier;
	public float LimbMultiplier;

	public static WeaponStats From( WeaponData data ) => new()
	{
		Damage = data.Damage,
		Spread = data.Spread,
		Recoil = data.Recoil,
		Range = data.Range,
		FireDelay = data.FireDelay,
		MovementInaccuracy = data.MovementInaccuracy,
		HeadMultiplier = data.HeadMultiplier,
		BodyMultiplier = data.BodyMultiplier,
		LimbMultiplier = data.LimbMultiplier,
	};

	public float MultiplierFor( HitZone zone ) => zone switch
	{
		HitZone.Head => HeadMultiplier,
		HitZone.Limb => LimbMultiplier,
		_ => BodyMultiplier,
	};
}
