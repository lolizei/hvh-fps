using System;

namespace HvH.Mods;

/// <summary>
/// What the framework hands a mod at startup: the scene it lives in, the event
/// bus, and shortcuts to the things almost every mod needs.
///
/// Passing this instead of letting mods reach for statics keeps mods testable
/// and gives us one place to widen or narrow what mods can touch.
/// </summary>
public sealed class ModContext
{
	public ModContext( ModManager manager, ModEventBus events )
	{
		Manager = manager;
		Events = events;
	}

	public ModManager Manager { get; }

	public ModEventBus Events { get; }

	public Scene Scene => Manager.IsValid() ? Manager.Scene : Game.ActiveScene;

	/// <summary>The pawn this machine controls. Null while dead or loading.</summary>
	public Player LocalPlayer => Player.Local;

	/// <summary>Every player pawn in the scene.</summary>
	public IEnumerable<Player> Players => Player.All;

	public RoundManager Round => RoundManager.Current;

	public TeamManager Teams => TeamManager.Current;

	/// <summary>
	/// Enemies of the local player that are currently alive. The single most
	/// requested thing in an HVH mod, so it lives here rather than being
	/// re-implemented in every mod.
	/// </summary>
	public IEnumerable<Player> AliveEnemies
	{
		get
		{
			var local = LocalPlayer;
			if ( !local.IsValid() ) return Enumerable.Empty<Player>();

			var myTeam = local.Team;

			return Player.All.Where( p =>
				p.IsValid() && p != local && p.IsAlive &&
				// With no teams assigned yet, treat everyone as fair game.
				( !myTeam.IsPlaying() || p.Team != myTeam ) );
		}
	}
}
