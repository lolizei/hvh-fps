using System;

namespace HvH;

/// <summary>
/// Owns team assignment, team scores and the friendly fire rule.
/// Host authoritative; scores replicate so the HUD can read them anywhere.
/// </summary>
public sealed class TeamManager : Component
{
	public static TeamManager Current
		=> Game.ActiveScene?.GetAllComponents<TeamManager>().FirstOrDefault();

	[Property] public bool FriendlyFire { get; set; }

	[Sync( Flags = SyncFlags.FromHost )] public int VanguardScore { get; set; }

	[Sync( Flags = SyncFlags.FromHost )] public int SyndicateScore { get; set; }

	public int ScoreOf( Team team ) => team switch
	{
		Team.Vanguard => VanguardScore,
		Team.Syndicate => SyndicateScore,
		_ => 0,
	};

	/// <summary>Host-side: give a team a round win.</summary>
	public void AddScore( Team team, int amount = 1 )
	{
		if ( !Networking.IsHost ) return;

		switch ( team )
		{
			case Team.Vanguard: VanguardScore += amount; break;
			case Team.Syndicate: SyndicateScore += amount; break;
		}
	}

	public void ResetScores()
	{
		if ( !Networking.IsHost ) return;

		VanguardScore = 0;
		SyndicateScore = 0;
	}

	/// <summary>
	/// Put the next player on whichever side is short-handed, breaking ties
	/// toward Vanguard so the first player in has a deterministic team.
	/// </summary>
	public Team PickTeamForNewPlayer()
	{
		var vanguard = PlayerState.OnTeam( Team.Vanguard ).Count();
		var syndicate = PlayerState.OnTeam( Team.Syndicate ).Count();

		return syndicate < vanguard ? Team.Syndicate : Team.Vanguard;
	}

	/// <summary>Host-side: move a player, used by the future team select UI.</summary>
	public void AssignTeam( PlayerState state, Team team )
	{
		if ( !Networking.IsHost || !state.IsValid() ) return;

		state.Team = team;
	}
}
