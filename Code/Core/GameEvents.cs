using System;

namespace HvH;

/// <summary>One entry in the kill feed. Plain data so the UI stays dumb.</summary>
public readonly record struct KillEvent(
	string KillerName,
	Team KillerTeam,
	string VictimName,
	Team VictimTeam,
	string WeaponName,
	bool Headshot );

/// <summary>
/// The game's announcement channel. The host decides something happened, this
/// broadcasts it, and UI on every machine reacts.
///
/// Kept separate from <see cref="RoundManager"/> so that gameplay code never
/// needs a reference to a UI panel - it raises an event and forgets.
/// </summary>
public sealed class GameEvents : Component
{
	public static GameEvents Current
		=> Game.ActiveScene?.GetAllComponents<GameEvents>().FirstOrDefault();

	/// <summary>Raised on every machine when someone dies.</summary>
	public static event Action<KillEvent> Kill;

	/// <summary>Raised on every machine when a round is decided.</summary>
	public static event Action<Team> RoundOver;

	/// <summary>How long a kill stays in the feed.</summary>
	[Property] public float KillFeedLifetime { get; set; } = 6f;

	[Property] public int KillFeedMaxEntries { get; set; } = 5;

	private readonly List<(KillEvent Kill, float Expiry)> _recent = new();

	/// <summary>
	/// Recent kills, newest last, already pruned. The feed panel reads this
	/// rather than subscribing, so a UI rebuild never loses entries.
	/// </summary>
	public IEnumerable<KillEvent> RecentKills
	{
		get
		{
			Prune();
			return _recent.Select( x => x.Kill );
		}
	}

	private void Prune()
	{
		_recent.RemoveAll( x => Time.Now >= x.Expiry );

		while ( _recent.Count > KillFeedMaxEntries )
			_recent.RemoveAt( 0 );
	}

	/// <summary>Host-side entry point. Safe to call when no one gets the credit.</summary>
	public void ReportKill( PlayerState killer, PlayerState victim, string weaponName, bool headshot )
	{
		if ( !Networking.IsHost || !victim.IsValid() ) return;

		BroadcastKill(
			killer.IsValid() ? killer.DisplayName : "",
			killer.IsValid() ? (int)killer.Team : 0,
			victim.DisplayName,
			(int)victim.Team,
			weaponName ?? "",
			headshot );
	}

	/// <summary>
	/// Host-side: a non-player target died. Same feed, but the victim has no
	/// PlayerState so it carries a plain name and no team.
	/// </summary>
	public void ReportTargetKill( PlayerState killer, string targetName, string weaponName, bool headshot )
	{
		if ( !Networking.IsHost ) return;

		BroadcastKill(
			killer.IsValid() ? killer.DisplayName : "",
			killer.IsValid() ? (int)killer.Team : 0,
			string.IsNullOrEmpty( targetName ) ? "target" : targetName,
			(int)Team.None,
			weaponName ?? "",
			headshot );
	}

	public void ReportRoundOver( Team winner )
	{
		if ( !Networking.IsHost ) return;

		BroadcastRoundOver( (int)winner );
	}

	// Teams travel as ints because enums add nothing over the wire here and
	// keep the RPC signature obviously stable.
	[Rpc.Broadcast]
	private void BroadcastKill( string killerName, int killerTeam, string victimName,
		int victimTeam, string weaponName, bool headshot )
	{
		var evt = new KillEvent(
			killerName, (Team)killerTeam,
			victimName, (Team)victimTeam,
			weaponName, headshot );

		_recent.Add( (evt, Time.Now + KillFeedLifetime) );
		Prune();

		Kill?.Invoke( evt );
	}

	[Rpc.Broadcast]
	private void BroadcastRoundOver( int winner ) => RoundOver?.Invoke( (Team)winner );
}
