using System;

namespace HvH;

/// <summary>
/// Creates and removes bot players.
///
/// A bot is not a special kind of entity - it is the ordinary player prefab with
/// <see cref="PlayerState.IsBot"/> set and no input source attached. It therefore
/// runs the same movement, weapon, health, damage, team and round code a human
/// does, which means having bots in the game is also a continuous test of that
/// code.
///
/// Host only. Bots are created, owned and (later) thought for by the host; a
/// client never spawns one and never decides anything about one.
/// </summary>
public sealed class BotManager : Component
{
	public static BotManager Current
		=> Game.ActiveScene?.GetAllComponents<BotManager>().FirstOrDefault();

	/// <summary>The pawn to clone. Same prefab the humans use.</summary>
	[Property] public GameObject BotPrefab { get; set; }

	/// <summary>
	/// How many players the match should contain in total, humans included.
	///
	/// The target is TOTAL players, not bot count: bots only fill the slots
	/// humans have not taken. A human joining pushes a bot out; a human leaving
	/// lets one back in.
	/// </summary>
	[Property] public int DesiredPlayers { get; set; } = 2;

	/// <summary>Upper bound on bots regardless of how empty the server is.</summary>
	[Property] public int MaxBots { get; set; } = 8;

	[Property] public string BotName { get; set; } = "Training Bot";

	public static IEnumerable<Player> Bots => Player.All.Where( x => x.IsValid() && x.IsBot );

	public static int BotCount => Bots.Count();

	private static IEnumerable<Player> Humans => Player.All.Where( x => x.IsValid() && !x.IsBot );

	private float _nextSpawnAttempt;

	/// <summary>How many bots we currently want: the slots humans have not filled.</summary>
	public int WantedBots => Math.Clamp( DesiredPlayers - Humans.Count(), 0, MaxBots );

	protected override void OnUpdate()
	{
		if ( !Networking.IsHost ) return;

		Converge();
	}

	/// <summary>
	/// Move the bot population toward <see cref="WantedBots"/> in BOTH
	/// directions. Only topping up leaves strays behind after a console spawn or
	/// a human joining.
	/// </summary>
	private void Converge()
	{
		var wanted = WantedBots;
		var current = BotCount;

		if ( current > wanted )
		{
			var removed = RemoveBots( current - wanted );
			if ( removed > 0 )
				Log.Info( $"BotManager: trimmed {removed} bot(s), now {BotCount}/{wanted}" );

			return;
		}

		if ( current >= wanted ) return;

		// Wait for a human before spawning, so team balancing has something to
		// balance against.
		if ( !Humans.Any() ) return;

		// Hard rate limit. If a spawn ever fails to register as a bot this loop
		// would otherwise create one every frame forever - which is exactly what
		// happened the first time this ran.
		if ( Time.Now < _nextSpawnAttempt ) return;
		_nextSpawnAttempt = Time.Now + 1f;

		var bot = SpawnBot();
		if ( bot.IsValid() && !bot.IsBot )
			Log.Warning( "BotManager: spawned pawn did not register as a bot - not retrying blindly." );
	}

	/// <summary>Host-side: remove up to <paramref name="count"/> bots, newest first.</summary>
	public static int RemoveBots( int count )
	{
		if ( !Networking.IsHost || count <= 0 ) return 0;

		var removed = 0;
		foreach ( var bot in Bots.Reverse().Take( count ).ToArray() )
		{
			bot.GameObject.Destroy();
			removed++;
		}

		return removed;
	}

	/// <summary>
	/// Host-side: create one bot on the side opposing the humans. Returns null
	/// if it could not be created.
	/// </summary>
	public Player SpawnBot( Team forceTeam = Team.None, Vector3? at = null )
	{
		if ( !Networking.IsHost ) return null;

		if ( !BotPrefab.IsValid() )
		{
			Log.Warning( "BotManager has no BotPrefab assigned - cannot spawn a bot." );
			return null;
		}

		// Team is NOT chosen here. Leaving it unset lets PlayerState.OnStart run
		// TeamManager.PickTeamForNewPlayer - the identical balancing path a human
		// goes through - so bots and humans cannot drift apart. forceTeam exists
		// only for dev tooling.
		var spawn = at.HasValue
			? new Transform( at.Value )
			: SpawnSystem.Pick( Scene, Team.None );
		var name = NextName();

		// Cloned disabled so nothing simulates it before it is configured.
		var go = BotPrefab.Clone( spawn, null, false, name );
		if ( !go.IsValid() ) return null;

		// includeDisabled: true is essential here. The clone is disabled, and the
		// default component lookup skips components on a disabled object - so
		// this silently returned null and every line below it was skipped.
		var player = go.GetComponent<Player>( true );
		if ( player.IsValid() )
		{
			// Cut the keyboard off FIRST, while the pawn is still disabled.
			// InputSource is a plain property, so unlike the synced flags below
			// this write survives network spawn - which is what actually
			// guarantees the bot never reads a frame of the human's input.
			player.InputSource = null;
		}

		// Spawn and enable in one step, host-owned (no connection).
		go.NetworkSpawn( true, null );

		// [Sync] values MUST be written after NetworkSpawn. Writing them to a
		// not-yet-networked object looks like it works and is then silently
		// discarded when the network state initialises - which produced bots
		// that reported IsBot == false and inherited the host's name.
		var state = go.GetComponent<PlayerState>( true );
		if ( state.IsValid() )
		{
			state.IsBot = true;
			state.DisplayName = name;

			// Only dev tooling overrides the balanced team.
			if ( forceTeam.IsPlaying() )
				state.Team = forceTeam;
		}

		// Give it something to think with. The brain is the bot's equivalent of
		// a keyboard: it plugs into the same seam and returns intent.
		if ( player.IsValid() )
			player.InputSource = go.AddComponent<BotBrain>();

		// Reuse the normal placement path rather than writing a position here,
		// so bots and humans are put into the world by the same code. An
		// explicit position is only used by dev tooling.
		if ( at.HasValue )
		{
			if ( player.IsValid() ) player.WorldPosition = at.Value;
		}
		else
		{
			player?.Respawn();
		}

		Log.Info( $"BotManager: spawned '{name}' on " +
			$"{( state.IsValid() ? state.Team.DisplayName() : "?" )} " +
			$"({BotCount} bots, {Humans.Count()} humans, want {WantedBots})" );

		return player;
	}

	/// <summary>Host-side: remove every bot.</summary>
	public static int RemoveAllBots()
	{
		if ( !Networking.IsHost ) return 0;

		var removed = 0;
		foreach ( var bot in Bots.ToArray() )
		{
			bot.GameObject.Destroy();
			removed++;
		}

		return removed;
	}

	/// <summary>Deterministic and readable: "Training Bot", then "Training Bot 2".</summary>
	private string NextName()
	{
		var baseName = string.IsNullOrWhiteSpace( BotName ) ? "Bot" : BotName;
		var existing = BotCount;

		return existing == 0 ? baseName : $"{baseName} {existing + 1}";
	}
}
