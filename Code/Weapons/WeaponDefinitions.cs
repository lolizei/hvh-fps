using System;

namespace HvH;

/// <summary>
/// The built-in weapons, defined in code so the game is playable without any
/// hand-authored .weapon assets. A map or mod can still ship its own
/// <see cref="WeaponData"/> resources and use those instead.
///
/// Numbers are original and tuned for this game - fast time-to-kill, heavy
/// movement penalty, big headshot reward.
/// </summary>
public static class WeaponDefinitions
{
	public const string Rifle = "rifle";
	public const string Pistol = "pistol";
	public const string Sniper = "sniper";
	public const string Smg = "smg";

	private static Dictionary<string, WeaponData> _cache;

	public static IReadOnlyDictionary<string, WeaponData> All => _cache ??= Build();

	public static WeaponData Get( string id )
		=> All.TryGetValue( id, out var data ) ? data : null;

	private static Dictionary<string, WeaponData> Build() => new()
	{
		[Rifle] = new WeaponData
		{
			DisplayName = "VK-7 Rifle", Slot = WeaponSlot.Primary,
			Damage = 33f, FireRate = 600f, Automatic = true,
			MagazineSize = 30, ReserveAmmo = 90, ReloadTime = 2.6f,
			Spread = 0.5f, Recoil = 1.5f, Range = 8192f, MovementInaccuracy = 4.5f,
			HeadMultiplier = 4f, BodyMultiplier = 1f, LimbMultiplier = 0.75f,
		},
		[Pistol] = new WeaponData
		{
			DisplayName = "M9 Sidearm", Slot = WeaponSlot.Secondary,
			Damage = 26f, FireRate = 400f, Automatic = false,
			MagazineSize = 12, ReserveAmmo = 48, ReloadTime = 1.9f,
			Spread = 0.7f, Recoil = 1.1f, Range = 4096f, MovementInaccuracy = 2.5f,
			HeadMultiplier = 4f, BodyMultiplier = 1f, LimbMultiplier = 0.8f,
		},
		[Sniper] = new WeaponData
		{
			DisplayName = "LR-40 Sniper", Slot = WeaponSlot.Primary,
			Damage = 115f, FireRate = 41f, Automatic = false,
			MagazineSize = 5, ReserveAmmo = 20, ReloadTime = 3.4f,
			Spread = 0.02f, Recoil = 5f, Range = 16384f, MovementInaccuracy = 14f,
			HeadMultiplier = 2f, BodyMultiplier = 1f, LimbMultiplier = 0.6f,
		},
		[Smg] = new WeaponData
		{
			DisplayName = "PK-9 SMG", Slot = WeaponSlot.Primary,
			Damage = 21f, FireRate = 850f, Automatic = true,
			MagazineSize = 25, ReserveAmmo = 100, ReloadTime = 2.1f,
			Spread = 0.9f, Recoil = 0.9f, Range = 4096f, MovementInaccuracy = 1.8f,
			HeadMultiplier = 3.5f, BodyMultiplier = 1f, LimbMultiplier = 0.85f,
		},
	};
}
