using System;

namespace HvH;

/// <summary>
/// A shootable practice target. Gives the arena something to fight so the
/// combat loop can be tested by one person, and gives the round something to
/// be won against.
///
/// It owns only its own presentation and death reaction - the round rules live
/// in <see cref="RoundManager"/>, which just counts how many are still up.
/// </summary>
public sealed class TargetDummy : Component
{
	[Property] public string DisplayName { get; set; } = "Dummy";

	[Property] public Color AliveTint { get; set; } = new( 0.90f, 0.25f, 0.20f );

	[Property] public Color DeadTint { get; set; } = new( 0.22f, 0.22f, 0.26f );

	public HealthComponent Health { get; private set; }
	public ModelRenderer Renderer { get; private set; }
	public Collider Collider { get; private set; }

	public bool IsAlive => Health.IsValid() && Health.IsAlive;

	public static IEnumerable<TargetDummy> All
		=> Game.ActiveScene?.GetAllComponents<TargetDummy>() ?? Enumerable.Empty<TargetDummy>();

	public static int TotalCount => All.Count();

	public static int AliveCount => All.Count( x => x.IsAlive );

	protected override void OnAwake()
	{
		Health = GetComponent<HealthComponent>();
		Renderer = GetComponentInChildren<ModelRenderer>( true );
		Collider = GetComponentInChildren<Collider>( true );
	}

	protected override void OnStart()
	{
		if ( Health.IsValid() )
		{
			Health.Died += OnDied;
			Health.Revived += OnRevived;
		}

		// OnAwake runs before the lobby exists, so the synced health values can
		// still be defaulted by the time the object becomes networked. Re-assert
		// them here, once networking is actually up.
		if ( Networking.IsHost && Health.IsValid() )
			Health.Revive();

		ApplyVisuals();
	}

	protected override void OnDestroy()
	{
		if ( !Health.IsValid() ) return;

		Health.Died -= OnDied;
		Health.Revived -= OnRevived;
	}

	private void OnDied()
	{
		ApplyVisuals();

		// Only the host knows who actually landed the hit.
		if ( !Networking.IsHost ) return;

		var killer = Health.LastAttacker.IsValid()
			? Health.LastAttacker.GetComponentInParent<PlayerState>()
			: null;

		var weaponName = Health.LastWeapon.IsValid()
			? Health.LastWeapon.GetComponent<Weapon>()?.DisplayName ?? ""
			: "";

		GameEvents.Current?.ReportTargetKill(
			killer, DisplayName, weaponName, Health.LastHitZone == HitZone.Head );
	}

	private void OnRevived() => ApplyVisuals();

	/// <summary>
	/// Dead dummies go dark and stop blocking shots, so a corpse never soaks a
	/// bullet meant for a live target behind it.
	/// </summary>
	private void ApplyVisuals()
	{
		var alive = IsAlive;

		if ( Renderer.IsValid() )
			Renderer.Tint = alive ? AliveTint : DeadTint;

		if ( Collider.IsValid() )
			Collider.Enabled = alive;
	}

	/// <summary>Host-side: bring this target back for a new round.</summary>
	public void Revive()
	{
		if ( !Networking.IsHost || !Health.IsValid() ) return;

		Health.Revive();
	}

	/// <summary>Host-side: put every target back up.</summary>
	public static void ReviveAll()
	{
		foreach ( var dummy in All.ToArray() )
			dummy.Revive();
	}
}
