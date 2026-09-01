using System;

namespace HvH;

/// <summary>
/// Generic health container for anything that can be damaged - players now,
/// breakable props later. The host is authoritative: damage is only ever
/// applied on the host, and the resulting values replicate down to clients.
/// </summary>
public sealed class HealthComponent : Component, Component.IDamageable
{
	[Property] public float MaxHealth { get; set; } = 100f;

	[Property] public float MaxArmor { get; set; } = 100f;

	/// <summary>Fraction of incoming damage armor soaks while it lasts.</summary>
	[Property] public float ArmorAbsorption { get; set; } = 0.5f;

	[Sync( Flags = SyncFlags.FromHost )] public float Health { get; set; } = 100f;

	[Sync( Flags = SyncFlags.FromHost )] public bool IsAlive { get; set; } = true;

	/// <summary>Armor soaks part of each hit and wears down as it does.</summary>
	[Sync( Flags = SyncFlags.FromHost )] public float Armor { get; set; }

	/// <summary>Fraction of max health remaining, 0-1. Handy for UI.</summary>
	public float Fraction => MaxHealth <= 0f ? 0f : Math.Clamp( Health / MaxHealth, 0f, 1f );

	/// <summary>
	/// Whoever landed the killing blow. Host only - clients get the kill feed
	/// through the round/score systems instead.
	/// </summary>
	public GameObject LastAttacker { get; private set; }

	/// <summary>What killed us, and where it hit. Host only, used for the kill feed.</summary>
	public GameObject LastWeapon { get; private set; }

	public HitZone LastHitZone { get; private set; }

	/// <summary>Raised on every machine the moment this dies.</summary>
	public event Action Died;

	/// <summary>Raised on every machine when this is restored to full health.</summary>
	public event Action Revived;

	protected override void OnAwake()
	{
		// Deliberately NOT gated on Networking.IsHost. Objects placed in the
		// scene awake before the lobby exists, so gating here left them with an
		// unset IsAlive - scene props read as dead from the moment they loaded.
		// On a real client these values are replaced by replication anyway.
		Health = MaxHealth;
		Armor = MaxArmor;
		IsAlive = true;
	}

	/// <summary>
	/// Engine damage entry point - everything that hurts this object funnels
	/// through here. Ignored anywhere but the host so a client cannot decide
	/// its own health.
	/// </summary>
	public void OnDamage( in DamageInfo damage ) => ApplyDamage( damage, HitZone.Body );

	/// <summary>
	/// Damage with a known hit location. Weapons call this so the kill feed can
	/// say whether it was a headshot; generic world damage goes through
	/// <see cref="OnDamage"/> and counts as a body hit.
	/// </summary>
	public void ApplyDamage( in DamageInfo damage, HitZone zone )
	{
		if ( !Networking.IsHost ) return;
		if ( !IsAlive ) return;
		if ( !DamageRules.CanDamage( damage.Attacker, GameObject ) ) return;

		Health = MathF.Max( 0f, Health - AbsorbWithArmor( damage.Damage ) );
		if ( Health > 0f ) return;

		IsAlive = false;
		LastAttacker = damage.Attacker;
		LastWeapon = damage.Weapon;
		LastHitZone = zone;
		AnnounceDied();
	}

	/// <summary>
	/// Route damage through armor first. Armor takes its share of the hit and
	/// loses that much durability, so it degrades as it protects.
	/// </summary>
	private float AbsorbWithArmor( float damage )
	{
		if ( Armor <= 0f || ArmorAbsorption <= 0f ) return damage;

		var absorbed = MathF.Min( Armor, damage * ArmorAbsorption );
		Armor -= absorbed;

		return damage - absorbed;
	}

	/// <summary>Host-side: restore to full health and tell everyone.</summary>
	public void Revive()
	{
		if ( !Networking.IsHost ) return;

		Health = MaxHealth;
		Armor = MaxArmor;
		IsAlive = true;
		AnnounceRevived();
	}

	// Death and revival are broadcast rather than derived from the synced flag
	// so that presentation (sounds, ragdolls, kill feed) fires exactly once on
	// every machine instead of whenever replication happens to land.
	[Rpc.Broadcast]
	private void AnnounceDied() => Died?.Invoke();

	[Rpc.Broadcast]
	private void AnnounceRevived() => Revived?.Invoke();
}
