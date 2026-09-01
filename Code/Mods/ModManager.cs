using System;

namespace HvH.Mods;

/// <summary>
/// Finds every <see cref="IGameMod"/> in the loaded assemblies, starts them,
/// ticks them, and routes menu keys to them.
///
/// Discovery is by reflection, so adding a mod means adding a class - there is
/// no list here to edit. The game's own HVH mod is found exactly the same way
/// as a third-party one, which is what keeps the API honest.
/// </summary>
public sealed class ModManager : Component
{
	public static ModManager Current
		=> Game.ActiveScene?.GetAllComponents<ModManager>().FirstOrDefault();

	/// <summary>Turn the whole modding layer off. Nothing is discovered or ticked.</summary>
	[Property] public bool ModsEnabled { get; set; } = true;

	/// <summary>Ids that should not be loaded, for debugging a misbehaving mod.</summary>
	[Property] public List<string> BlockedMods { get; set; } = new();

	public ModEventBus Events { get; } = new();

	private readonly List<IGameMod> _mods = new();

	public IReadOnlyList<IGameMod> Mods => _mods;

	private ModContext _context;
	private Player _lastLocalPlayer;

	public IGameMod Find( string id )
		=> _mods.FirstOrDefault( m => string.Equals( m.Id, id, StringComparison.OrdinalIgnoreCase ) );

	protected override void OnStart()
	{
		if ( !ModsEnabled ) return;

		_context = new ModContext( this, Events );

		DiscoverMods();
		HookGameEvents();
	}

	protected override void OnDestroy()
	{
		UnhookGameEvents();

		foreach ( var mod in _mods )
		{
			try { mod.Shutdown(); }
			catch ( Exception e ) { Log.Warning( $"Mod '{mod.Id}' failed to shut down: {e.Message}" ); }
		}

		_mods.Clear();
	}

	/// <summary>
	/// Instantiate every non-abstract IGameMod. A mod that throws while loading
	/// is skipped rather than taking the game down with it.
	/// </summary>
	private void DiscoverMods()
	{
		foreach ( var type in TypeLibrary.GetTypes<IGameMod>() )
		{
			if ( type.IsAbstract || type.IsInterface ) continue;

			IGameMod mod;
			try
			{
				mod = type.Create<IGameMod>();
			}
			catch ( Exception e )
			{
				Log.Warning( $"Couldn't create mod '{type.Name}': {e.Message}" );
				continue;
			}

			if ( mod is null ) continue;
			if ( BlockedMods is not null && BlockedMods.Contains( mod.Id, StringComparer.OrdinalIgnoreCase ) )
				continue;

			if ( Find( mod.Id ) is not null )
			{
				Log.Warning( $"Two mods share the id '{mod.Id}' - ignoring the second." );
				continue;
			}

			try
			{
				mod.Initialize( _context );
				_mods.Add( mod );
				Log.Info( $"Loaded mod: {mod.Name} v{mod.Version} by {mod.Author}" );
			}
			catch ( Exception e )
			{
				Log.Warning( $"Mod '{mod.Id}' failed to initialise: {e.Message}" );
			}
		}
	}

	protected override void OnUpdate()
	{
		if ( !ModsEnabled ) return;

		DetectLocalPlayerChange();
		HandleMenuKeys();
		TickFeatures();

		Events.RaiseFrame();
	}

	private void DetectLocalPlayerChange()
	{
		var local = Player.Local;
		if ( local == _lastLocalPlayer ) return;

		_lastLocalPlayer = local;
		Events.RaiseLocalPlayerChanged( local );
	}

	/// <summary>
	/// Each mod's menu declares its own toggle key, so several mods can coexist
	/// with different keys. Opening one closes the others - two overlapping
	/// cheat menus is never what anyone wants.
	/// </summary>
	private void HandleMenuKeys()
	{
		foreach ( var mod in _mods )
		{
			if ( !mod.Enabled ) continue;

			var menu = mod.Menu;
			if ( menu is null || string.IsNullOrEmpty( menu.ToggleKey ) ) continue;
			if ( !Input.Pressed( menu.ToggleKey ) ) continue;

			if ( menu.IsOpen )
			{
				menu.Close();
			}
			else
			{
				CloseAllMenus();
				menu.Open();
			}
		}
	}

	public void CloseAllMenus()
	{
		foreach ( var mod in _mods )
			mod.Menu?.Close();
	}

	/// <summary>Any mod menu currently on screen. Used to swallow game input.</summary>
	public bool AnyMenuOpen => _mods.Any( m => m.Menu?.IsOpen ?? false );

	private void TickFeatures()
	{
		foreach ( var mod in _mods )
		{
			if ( !mod.Enabled ) continue;

			foreach ( var feature in mod.Features )
			{
				if ( !feature.Enabled ) continue;

				try
				{
					feature.Tick();
				}
				catch ( Exception e )
				{
					// One broken feature must not stop the others, and must not
					// spam - switch it off and say why.
					Log.Warning( $"Feature '{feature.Name}' threw and was disabled: {e.Message}" );
					feature.Enabled = false;
				}
			}
		}
	}

	// --- bridging the game's own events onto the bus ---------------------

	private void HookGameEvents()
	{
		GameEvents.Kill += OnKill;
		GameEvents.RoundOver += OnRoundOver;
		Weapon.StatsModifier += OnWeaponStats;

		var round = RoundManager.Current;
		if ( round.IsValid() )
			round.StateChanged += OnRoundStateChanged;
	}

	private void UnhookGameEvents()
	{
		GameEvents.Kill -= OnKill;
		GameEvents.RoundOver -= OnRoundOver;
		Weapon.StatsModifier -= OnWeaponStats;

		var round = RoundManager.Current;
		if ( round.IsValid() )
			round.StateChanged -= OnRoundStateChanged;
	}

	private void OnKill( KillEvent kill ) => Events.RaiseKill( kill );
	private void OnRoundOver( Team winner ) => Events.RaiseRoundOver( winner );
	private void OnRoundStateChanged( RoundState state ) => Events.RaiseRoundStateChanged( state );
	private void OnWeaponStats( Weapon weapon, WeaponStats stats ) => Events.RaiseWeaponStats( weapon, stats );
}
