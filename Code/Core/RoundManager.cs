using System;

namespace HvH;

/// <summary>
/// Drives the round loop:
///   Warmup -> RoundStart -> Playing -> RoundEnd -> Restarting -> RoundStart...
///
/// Host authoritative. Everything else in the game asks this what is allowed
/// right now (<see cref="AllowMovement"/>, <see cref="AllowShooting"/>,
/// <see cref="AllowRespawn"/>) rather than tracking the phase itself.
/// </summary>
public sealed class RoundManager : Component
{
	public static RoundManager Current
		=> Game.ActiveScene?.GetAllComponents<RoundManager>().FirstOrDefault();

	[Property] public float WarmupDuration { get; set; } = 15f;
	[Property] public float FreezeDuration { get; set; } = 5f;
	[Property] public float RoundDuration { get; set; } = 115f;
	[Property] public float RoundEndDuration { get; set; } = 7f;
	[Property] public float RestartDuration { get; set; } = 2f;

	/// <summary>Rounds needed to take the match.</summary>
	[Property] public int ScoreToWin { get; set; } = 16;

	/// <summary>Below this the game sits in warmup with free respawning.</summary>
	[Property] public int MinPlayers { get; set; } = 1;

	[Sync( Flags = SyncFlags.FromHost )] public RoundState State { get; set; } = RoundState.Warmup;

	/// <summary>Scene time the current phase ends at. Drives every clock in the HUD.</summary>
	[Sync( Flags = SyncFlags.FromHost )] public float PhaseEndTime { get; set; }

	[Sync( Flags = SyncFlags.FromHost )] public int RoundNumber { get; set; }

	[Sync( Flags = SyncFlags.FromHost )] public Team LastWinner { get; set; } = Team.None;

	public float TimeRemaining => MathF.Max( 0f, PhaseEndTime - Time.Now );

	/// <summary>Players are frozen at spawn during the pre-round freeze.</summary>
	public bool AllowMovement => State != RoundState.RoundStart;

	public bool AllowShooting => State is RoundState.Playing or RoundState.Warmup;

	/// <summary>
	/// Only warmup respawns you. A live round is elimination: a kill has to
	/// stick, or shooting someone means nothing and they simply come back and
	/// kill you. Death during Playing waits for the next round.
	/// </summary>
	public bool AllowRespawn => State == RoundState.Warmup;

	/// <summary>Raised on the host whenever the phase changes. The mod framework hooks this.</summary>
	public event Action<RoundState> StateChanged;

	/// <summary>Raised on the host when a round is decided. <see cref="Team.None"/> means a draw.</summary>
	public event Action<Team> RoundDecided;

	protected override void OnStart()
	{
		if ( !Networking.IsHost ) return;

		EnterState( RoundState.Warmup );
	}

	protected override void OnUpdate()
	{
		// Clients just read the synced state; only the host advances it.
		if ( !Networking.IsHost ) return;

		switch ( State )
		{
			case RoundState.Warmup: TickWarmup(); break;
			case RoundState.RoundStart: TickRoundStart(); break;
			case RoundState.Playing: TickPlaying(); break;
			case RoundState.RoundEnd: TickRoundEnd(); break;
			case RoundState.Restarting: TickRestarting(); break;
		}
	}

	private bool PhaseElapsed => Time.Now >= PhaseEndTime;

	private void TickWarmup()
	{
		if ( CountPlayers() < MinPlayers ) 
		{
			// Hold warmup open indefinitely while we wait for people.
			PhaseEndTime = Time.Now + WarmupDuration;
			return;
		}

		if ( PhaseElapsed )
			EnterState( RoundState.RoundStart );
	}

	private void TickRoundStart()
	{
		if ( PhaseElapsed )
			EnterState( RoundState.Playing );
	}

	private void TickPlaying()
	{
		// Real opponents outrank practice targets. Once both sides actually have
		// players on them - human or bot - the round is decided by elimination
		// and the dummy rule is ignored, otherwise a round could resolve twice.
		if ( !IsContested() && TargetDummy.TotalCount > 0 )
		{
			TickPlayingAgainstTargets();
			return;
		}

		var vanguardAlive = CountAlive( Team.Vanguard );
		var syndicateAlive = CountAlive( Team.Syndicate );

		// Elimination beats the clock.
		if ( vanguardAlive == 0 || syndicateAlive == 0 )
		{
			if ( vanguardAlive == syndicateAlive ) DecideRound( Team.None );
			else DecideRound( vanguardAlive > 0 ? Team.Vanguard : Team.Syndicate );
			return;
		}

		if ( !PhaseElapsed ) return;

		// Time out: whoever has more bodies left takes it, otherwise a draw.
		if ( vanguardAlive == syndicateAlive ) DecideRound( Team.None );
		else DecideRound( vanguardAlive > syndicateAlive ? Team.Vanguard : Team.Syndicate );
	}

	/// <summary>
	/// True when both sides have at least one player on them. Counts bots -
	/// they are ordinary players and must count toward win conditions.
	/// </summary>
	public static bool IsContested()
		=> PlayerState.OnTeam( Team.Vanguard ).Any() && PlayerState.OnTeam( Team.Syndicate ).Any();

	/// <summary>Clear every target to win the round; run out the clock and it's a draw.</summary>
	private void TickPlayingAgainstTargets()
	{
		if ( TargetDummy.AliveCount == 0 )
		{
			DecideRound( Team.Vanguard );
			return;
		}

		if ( PhaseElapsed )
			DecideRound( Team.None );
	}

	private void TickRoundEnd()
	{
		if ( PhaseElapsed )
			EnterState( RoundState.Restarting );
	}

	private void TickRestarting()
	{
		if ( !PhaseElapsed ) return;

		var teams = TeamManager.Current;
		var matchOver = teams.IsValid() &&
			( teams.VanguardScore >= ScoreToWin || teams.SyndicateScore >= ScoreToWin );

		if ( matchOver )
		{
			teams.ResetScores();
			RoundNumber = 0;
			EnterState( RoundState.Warmup );
			return;
		}

		EnterState( RoundState.RoundStart );
	}

	private void DecideRound( Team winner )
	{
		LastWinner = winner;

		if ( winner.IsPlaying() )
			TeamManager.Current?.AddScore( winner );

		RoundDecided?.Invoke( winner );
		GameEvents.Current?.ReportRoundOver( winner );
		EnterState( RoundState.RoundEnd );
	}

	/// <summary>Host-side phase transition. All the side effects live here.</summary>
	private void EnterState( RoundState state )
	{
		State = state;
		PhaseEndTime = Time.Now + DurationOf( state );

		if ( state == RoundState.RoundStart )
		{
			RoundNumber++;
			RespawnEveryone();
			TargetDummy.ReviveAll();
		}

		StateChanged?.Invoke( state );
	}

	private float DurationOf( RoundState state ) => state switch
	{
		RoundState.Warmup => WarmupDuration,
		RoundState.RoundStart => FreezeDuration,
		RoundState.Playing => RoundDuration,
		RoundState.RoundEnd => RoundEndDuration,
		RoundState.Restarting => RestartDuration,
		_ => 5f,
	};

	private void RespawnEveryone()
	{
		foreach ( var player in Player.All.ToArray() )
			player.Respawn();
	}

	private static int CountPlayers() => PlayerState.All.Count( x => x.Team.IsPlaying() );

	/// <summary>Living players on a team. Used for both win conditions.</summary>
	public static int CountAlive( Team team )
	{
		var count = 0;
		foreach ( var player in Player.All )
		{
			if ( !player.IsAlive ) continue;
			if ( player.GetComponent<PlayerState>()?.Team != team ) continue;
			count++;
		}

		return count;
	}
}
