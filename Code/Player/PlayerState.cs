using System;

namespace HvH;

/// <summary>
/// Per-player bookkeeping that outlives any single life: name, team, score.
/// Separate from <see cref="Player"/> so the scoreboard and round logic can read
/// it without caring whether the pawn is currently alive.
/// </summary>
public sealed class PlayerState : Component
{
	[Sync( Flags = SyncFlags.FromHost )] public string DisplayName { get; set; } = "Player";

	[Sync( Flags = SyncFlags.FromHost )] public Team Team { get; set; } = Team.None;

	/// <summary>
	/// Whether this pawn is driven by a bot brain. Synced because the scoreboard,
	/// kill feed and round rules all need to know on every machine, not just the
	/// host that owns it.
	/// </summary>
	[Sync( Flags = SyncFlags.FromHost )] public bool IsBot { get; set; }

	[Sync( Flags = SyncFlags.FromHost )] public int Kills { get; set; }

	[Sync( Flags = SyncFlags.FromHost )] public int Deaths { get; set; }

	protected override void OnStart()
	{
		if ( !Networking.IsHost ) return;

		// Network.Owner is the connection controlling this pawn. A bot has no
		// connection, so it keeps whatever name it was given at spawn.
		var owner = Network.Owner;
		if ( owner is not null && !IsBot )
			DisplayName = owner.DisplayName;

		if ( Team == Team.None )
			Team = TeamManager.Current?.PickTeamForNewPlayer() ?? Team.Vanguard;
	}

	/// <summary>Every player state in the scene, including dead players.</summary>
	public static IEnumerable<PlayerState> All
		=> Game.ActiveScene?.GetAllComponents<PlayerState>() ?? Enumerable.Empty<PlayerState>();

	public static IEnumerable<PlayerState> OnTeam( Team team )
		=> All.Where( x => x.Team == team );
}
