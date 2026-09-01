using System;

namespace HvH;

/// <summary>
/// Root component of a player pawn. Owns the authoritative view angles and
/// decides *when* things happen (death, respawn); the sibling components
/// decide *how*. Kept deliberately thin so the round manager in Phase 2 and
/// the mod framework in Phase 8 have something small to talk to.
/// </summary>
public sealed class Player : Component
{
	/// <summary>
	/// The GameObject the camera sits on, a child of this pawn. Auto-resolved
	/// from the child named "Eye" if it isn't wired up in the prefab.
	/// </summary>
	[Property] public GameObject Eye { get; set; }

	/// <summary>Seconds between dying and being put back in the world.</summary>
	[Property] public float RespawnDelay { get; set; } = 3f;

	/// <summary>
	/// Where this player is looking. Owned by the controlling client and synced
	/// so other machines can aim this pawn's model and know what it can see.
	/// </summary>
	[Sync] public Angles EyeAngles { get; set; }

	private HealthComponent _health;
	private PlayerMovement _movement;
	private PlayerState _state;
	private WeaponInventory _inventory;

	// Resolved on demand, including while disabled. These must NOT wait for
	// OnAwake: a pawn is cloned disabled and configured before being enabled
	// (that is how a bot gets its flag set before PlayerState.OnStart reads it),
	// so anything that only populated in OnAwake reads as null during that
	// window - which silently made IsBot false and spawned bots forever.
	public HealthComponent Health => Resolve( ref _health );
	public PlayerMovement Movement => Resolve( ref _movement );
	public PlayerState State => Resolve( ref _state );
	public WeaponInventory Inventory => Resolve( ref _inventory );

	private T Resolve<T>( ref T cached ) where T : Component
	{
		if ( !cached.IsValid() )
			cached = GetComponent<T>( true );

		return cached;
	}

	public Team Team => State.IsValid() ? State.Team : Team.None;

	public bool IsAlive => Health.IsValid() && Health.IsAlive;

	/// <summary>
	/// True when a bot brain is driving this pawn instead of a person.
	///
	/// This has to be an explicit flag. Neither of the engine's ownership
	/// concepts can tell a bot apart from your own pawn: a bot spawned by the
	/// host has IsProxy == false AND Network.Owner == the local connection,
	/// exactly like the host's own player.
	/// </summary>
	public bool IsBot => State.IsValid() && State.IsBot;

	/// <summary>
	/// True only for the pawn the person at this keyboard is playing. Gates the
	/// camera, the HUD and anything that means "me".
	/// </summary>
	public bool IsLocallyControlled => !IsProxy && !IsBot;

	/// <summary>
	/// True when this machine advances this pawn's movement and weapons - the
	/// local human, plus any bots the host owns. Distinct from
	/// <see cref="IsLocallyControlled"/>: a bot simulates here but is not "me".
	/// </summary>
	public bool IsSimulatedHere => !IsProxy;

	/// <summary>
	/// Where this pawn's intent comes from. Defaults to the keyboard; a bot
	/// brain replaces it so the pawn runs the same code with different input.
	/// </summary>
	public IPlayerInputSource InputSource { get; set; } = HumanInputSource.Instance;

	/// <summary>This frame's intent. Movement, weapons and the inventory read this.</summary>
	public PlayerInputState InputState { get; private set; } = PlayerInputState.Idle;

	/// <summary>
	/// The source actually permitted to drive this pawn this frame.
	///
	/// A bot can never fall through to the keyboard - not even for the single
	/// frame between being cloned and having its brain attached. This is a
	/// structural guarantee rather than a spawn-order convention, because the
	/// failure it prevents is silent: a bot mirroring the human's every move.
	/// </summary>
	private IPlayerInputSource ResolveInputSource()
		=> IsBot && InputSource is HumanInputSource ? null : InputSource;

	/// <summary>World-space ray from this player's eye, along their view.</summary>
	public Ray AimRay => new( Eye.IsValid() ? Eye.WorldPosition : WorldPosition, EyeAngles.Forward );

	/// <summary>Every player pawn currently in the scene.</summary>
	public static IEnumerable<Player> All
		=> Game.ActiveScene?.GetAllComponents<Player>() ?? Enumerable.Empty<Player>();

	private static Player _local;

	/// <summary>
	/// The pawn the person at this keyboard is playing, or null while
	/// spectating or loading. Never a bot - see <see cref="IsLocallyControlled"/>.
	/// </summary>
	public static Player Local
	{
		get
		{
			// Re-resolve if the cache went stale OR is somehow holding a pawn we
			// don't control, so a bot can never get stuck in here.
			if ( !_local.IsValid() || !_local.IsLocallyControlled )
				_local = All.FirstOrDefault( x => x.IsLocallyControlled );

			return _local;
		}
	}

	private float _respawnTimer;

	protected override void OnAwake()
	{
		if ( !Eye.IsValid() )
			Eye = GetComponentInChildren<CameraComponent>( true )?.GameObject;
	}

	protected override void OnStart()
	{
		if ( Health.IsValid() )
			Health.Died += OnDied;

		// We're spawned facing the spawn point's direction, but UpdateEyeAngles
		// would immediately snap us to yaw 0 because EyeAngles starts zeroed.
		// Adopt the spawn facing as our starting view instead. Bots want this too.
		if ( IsSimulatedHere )
			EyeAngles = WorldRotation.Angles().WithPitch( 0f ).WithRoll( 0f );
	}

	protected override void OnDestroy()
	{
		if ( Health.IsValid() )
			Health.Died -= OnDied;

		if ( _local == this )
			_local = null;
	}

	protected override void OnUpdate()
	{
		// Remote pawns are moved by network interpolation - we neither read
		// input for them nor simulate them.
		if ( IsSimulatedHere )
		{
			InputState = ResolveInputSource()?.BuildInput( this ) ?? PlayerInputState.Idle;
			UpdateEyeAngles();
		}
		else
		{
			InputState = PlayerInputState.Idle;
		}

		TickRespawn();
	}

	/// <summary>
	/// Apply this frame's look intent. The intent already has sensitivity
	/// applied by whoever produced it, so we only clamp it here.
	/// </summary>
	private void UpdateEyeAngles()
	{
		if ( !IsAlive ) return;

		var angles = EyeAngles + InputState.LookDelta;
		angles.pitch = Math.Clamp( angles.pitch, -89f, 89f );
		angles.roll = 0f;

		EyeAngles = angles;

		// Turn the pawn body to match our yaw. The camera sets its own rotation
		// in OnPreRender, so this only affects what other players see.
		WorldRotation = new Angles( 0f, angles.yaw, 0f ).ToRotation();
	}

	private void OnDied()
	{
		// Movement and the shooter read IsAlive and stop themselves; all we do
		// here is score it and start the clock. Host only.
		if ( !Networking.IsHost ) return;

		ScoreDeath();

		// During a live round death is final - the round manager respawns
		// everyone when the next one starts.
		if ( RoundManager.Current?.AllowRespawn ?? true )
			_respawnTimer = RespawnDelay;
	}

	/// <summary>Host-side: credit the kill and count the death.</summary>
	private void ScoreDeath()
	{
		if ( State.IsValid() )
			State.Deaths++;

		var killer = Health.IsValid() ? Health.LastAttacker : null;
		if ( !killer.IsValid() || killer == GameObject ) return;

		var killerState = killer.GetComponentInParent<PlayerState>();
		if ( !killerState.IsValid() ) return;

		// Don't reward team kills, but still announce them.
		var teamKill = State.IsValid() && killerState.Team == State.Team;
		if ( !teamKill )
			killerState.Kills++;

		GameEvents.Current?.ReportKill(
			killerState, State,
			Health.LastWeapon.IsValid()
				? Health.LastWeapon.GetComponent<Weapon>()?.DisplayName ?? "" : "",
			Health.LastHitZone == HitZone.Head );
	}

	private void TickRespawn()
	{
		if ( !Networking.IsHost ) return;
		if ( IsAlive ) return;
		if ( _respawnTimer <= 0f ) return;

		_respawnTimer -= Time.Delta;
		if ( _respawnTimer > 0f ) return;

		Respawn();
	}

	/// <summary>Host-side: put this player back in the world at a spawn point.</summary>
	public void Respawn()
	{
		if ( !Networking.IsHost ) return;

		_respawnTimer = 0f;

		var spawn = SpawnSystem.Pick( Scene, Team );
		Health.Revive();

		// Respawning with the empty gun you died holding is never what anyone
		// wants. Humans and bots share this path, so both get a fresh loadout.
		Inventory?.RestoreAmmo();

		// The pawn's transform is owned by the controlling client, so the host
		// cannot just write WorldPosition here - it would be overwritten on the
		// next network update. Ask the owner to move itself instead.
		TeleportTo( spawn.Position, spawn.Rotation.Angles() );
	}

	[Rpc.Owner]
	private void TeleportTo( Vector3 position, Angles angles )
	{
		WorldPosition = position;
		EyeAngles = angles.WithPitch( 0f ).WithRoll( 0f );

		if ( Movement.IsValid() )
			Movement.ClearVelocity();
	}
}
