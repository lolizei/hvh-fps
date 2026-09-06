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

	/// <summary>
	/// How a round is won.
	///
	/// Defaults to Elimination so every existing scene behaves exactly as it
	/// did. Deathmatch is opted into per scene - the Compound map sets it.
	/// </summary>
	[Property] public GameMode Mode { get; set; } = GameMode.Elimination;

	/// <summary>Team frags that take a deathmatch round.</summary>
	[Property] public int FragLimit { get; set; } = 30;

	/// <summary>Rounds needed to take the match.</summary>
	[Property] public int ScoreToWin { get; set; } = 16;

	/// <summary>Below this the game sits in warmup with free respawning.</summary>
	[Property] public int MinPlayers { get; set; } = 1;

	[Sync( Flags = SyncFlags.FromHost )] public RoundState State { get; set; } = RoundState.Warmup;

	/// <summary>
	/// Scene time the current phase ends at, in the HOST's clock.
	///
	/// Host-side logic only. Do NOT compare this against a client's Time.Now:
	/// scene time is per-machine and starts when that machine loaded the scene,
	/// so the two never agree and the error grows the later someone joins.
	/// <see cref="SecondsRemaining"/> is what clients read.
	/// </summary>
	[Sync( Flags = SyncFlags.FromHost )] public float PhaseEndTime { get; set; }

	/// <summary>
	/// Seconds left in this phase, written by the host every tick.
	///
	/// Replicated as a duration rather than a deadline precisely because a
	/// duration means the same thing on every machine and a deadline does not.
	/// </summary>
	[Sync( Flags = SyncFlags.FromHost )] public float SecondsRemaining { get; set; }

	[Sync( Flags = SyncFlags.FromHost )] public int RoundNumber { get; set; }

	[Sync( Flags = SyncFlags.FromHost )] public Team LastWinner { get; set; } = Team.None;

	/// <summary>
	/// Time left in the current phase. The host computes it; everyone else reads
	/// the replicated duration, because only the host's clock matches
	/// <see cref="PhaseEndTime"/>.
	/// </summary>
	public float TimeRemaining => Networking.IsHost
		? MathF.Max( 0f, PhaseEndTime - Time.Now )
		: MathF.Max( 0f, SecondsRemaining );

	/// <summary>Players are frozen at spawn during the pre-round freeze.</summary>
	public bool AllowMovement => State != RoundState.RoundStart;

	public bool AllowShooting => State is RoundState.Playing or RoundState.Warmup;

	/// <summary>
	/// Warmup always respawns you. In Elimination a live round does not: a kill
	/// has to stick, or shooting someone means nothing and they come straight
	/// back. Deathmatch inverts exactly that - respawning IS the mode.
	/// </summary>
	public bool AllowRespawn
		=> State == RoundState.Warmup
		|| ( Mode == GameMode.Deathmatch && State == RoundState.Playing );

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

		// Publish the phase clock as a duration so clients have something whose
		// meaning survives the trip.
		SecondsRemaining = MathF.Max( 0f, PhaseEndTime - Time.Now );

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

	/// <summary>Team frags this round - bots included, they are ordinary players.</summary>
	public static int FragsFor( Team team )
		=> PlayerState.OnTeam( team ).Sum( x => x.Kills );

	/// <summary>
	/// Deathmatch: nobody is eliminated, so the round is decided by frags or by
	/// the clock. Never by who is left standing - in this mode everyone is.
	/// </summary>
	private void TickDeathmatch()
	{
		var vanguard = FragsFor( Team.Vanguard );
		var syndicate = FragsFor( Team.Syndicate );

		if ( vanguard >= FragLimit || syndicate >= FragLimit )
		{
			DecideRound( vanguard == syndicate ? Team.None
				: vanguard > syndicate ? Team.Vanguard : Team.Syndicate );
			return;
		}

		if ( !PhaseElapsed ) return;

		DecideRound( vanguard == syndicate ? Team.None
			: vanguard > syndicate ? Team.Vanguard : Team.Syndicate );
	}

	private void TickPlaying()
	{
		if ( Mode == GameMode.Deathmatch )
		{
			TickDeathmatch();
			return;
		}

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
