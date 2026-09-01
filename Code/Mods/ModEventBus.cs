using System;

namespace HvH.Mods;

/// <summary>
/// The game's events, republished for mods.
///
/// This exists so a mod never has to hold a reference to a gameplay component
/// or subscribe to an internal static. <see cref="ModManager"/> owns the one
/// instance and forwards the game's own events into it; mods only ever see
/// this surface, which means gameplay code can be refactored without breaking
/// third-party mods.
/// </summary>
public sealed class ModEventBus
{
	/// <summary>Round phase changed. Fires on every machine.</summary>
	public event Action<RoundState> RoundStateChanged;

	/// <summary>Someone died, anywhere in the match.</summary>
	public event Action<KillEvent> Kill;

	/// <summary>A round was decided. <see cref="Team.None"/> is a draw.</summary>
	public event Action<Team> RoundOver;

	/// <summary>
	/// A shot is being built. Mutate the stats to change damage, spread or
	/// recoil for this shot only. This is the ballistics hook.
	/// </summary>
	public event Action<Weapon, WeaponStats> WeaponStats;

	/// <summary>Every frame, after features have ticked.</summary>
	public event Action Frame;

	/// <summary>The local player's pawn changed - respawn, or first spawn.</summary>
	public event Action<Player> LocalPlayerChanged;

	// Raisers are internal: mods listen, the framework publishes.
	internal void RaiseRoundStateChanged( RoundState state ) => RoundStateChanged?.Invoke( state );
	internal void RaiseKill( KillEvent kill ) => Kill?.Invoke( kill );
	internal void RaiseRoundOver( Team winner ) => RoundOver?.Invoke( winner );
	internal void RaiseWeaponStats( Weapon weapon, WeaponStats stats ) => WeaponStats?.Invoke( weapon, stats );
	internal void RaiseFrame() => Frame?.Invoke();
	internal void RaiseLocalPlayerChanged( Player player ) => LocalPlayerChanged?.Invoke( player );
}
