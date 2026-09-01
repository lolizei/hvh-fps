using System;

namespace HvH;

/// <summary>
/// Holds a player's weapons and decides which one is in their hands.
///
/// Weapons are child objects of the pawn rather than things spawned at runtime,
/// so they come along with the prefab and need no separate network spawning.
/// Switching is just enabling one and disabling the rest, which also stops an
/// inactive weapon from reading input.
/// </summary>
public sealed class WeaponInventory : Component
{
	/// <summary>Index into <see cref="Weapons"/>. Host authoritative.</summary>
	[Sync( Flags = SyncFlags.FromHost )] public int ActiveIndex { get; set; }

	/// <summary>Every weapon this player carries, in child order.</summary>
	public List<Weapon> Weapons { get; private set; } = new();

	/// <summary>Weapon currently in the player's hands, or null.</summary>
	public Weapon ActiveWeapon
		=> ActiveIndex >= 0 && ActiveIndex < Weapons.Count ? Weapons[ActiveIndex] : null;

	private int _appliedIndex = -1;

	protected override void OnStart()
	{
		Weapons = GetComponentsInChildren<Weapon>( true ).ToList();
		ApplyActive( force: true );
	}

	private Player _player;

	protected override void OnUpdate()
	{
		_player ??= GetComponent<Player>();

		if ( _player.IsValid() && _player.IsSimulatedHere )
			HandleSwitchInput();

		// Runs on every machine so remote players show the right weapon too.
		if ( _appliedIndex != ActiveIndex )
			ApplyActive();
	}

	private void HandleSwitchInput()
	{
		if ( Weapons.Count == 0 ) return;

		var input = _player.InputState;

		if ( input.SlotRequest >= 0 && input.SlotRequest < Weapons.Count )
			RequestSwitch( input.SlotRequest );

		if ( input.SlotNextPressed ) RequestSwitch( Wrap( ActiveIndex + 1 ) );
		if ( input.SlotPrevPressed ) RequestSwitch( Wrap( ActiveIndex - 1 ) );
	}

	private int Wrap( int index )
	{
		if ( Weapons.Count == 0 ) return 0;

		return ( index % Weapons.Count + Weapons.Count ) % Weapons.Count;
	}

	/// <summary>Ask the host to put a different weapon in our hands.</summary>
	public void RequestSwitch( int index )
	{
		if ( index == ActiveIndex ) return;

		SwitchTo( index );
	}

	[Rpc.Host]
	private void SwitchTo( int index )
	{
		if ( index < 0 || index >= Weapons.Count ) return;

		ActiveIndex = index;
	}

	/// <summary>Host-side: refill every weapon this player carries.</summary>
	public void RestoreAmmo()
	{
		foreach ( var weapon in Weapons )
		{
			if ( weapon.IsValid() )
				weapon.RestoreAmmo();
		}
	}

	private void ApplyActive( bool force = false )
	{
		if ( Weapons.Count == 0 ) return;

		_appliedIndex = ActiveIndex;

		for ( var i = 0; i < Weapons.Count; i++ )
		{
			var weapon = Weapons[i];
			if ( !weapon.IsValid() ) continue;

			var shouldBeActive = i == ActiveIndex;
			if ( force || weapon.Enabled != shouldBeActive )
				weapon.Enabled = shouldBeActive;
		}
	}
}
