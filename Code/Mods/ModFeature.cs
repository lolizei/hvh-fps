using System;

namespace HvH.Mods;

/// <summary>
/// Convenience base for features. Handles the Enabled/Enable/Disable plumbing
/// and persists its own on/off state into the owning mod's config, so a feature
/// only has to implement <see cref="OnTick"/>.
///
/// Implementing <see cref="IModFeature"/> directly is perfectly fine - this is
/// a shortcut, not a requirement.
/// </summary>
public abstract class ModFeature : IModFeature
{
	public abstract string Name { get; }
	public virtual string Category => "Misc";
	public virtual string Description => "";

	/// <summary>Config key for this feature's enabled flag.</summary>
	protected string EnabledKey => $"{Category}.{Name}.Enabled".Replace( " ", "" );

	protected ModContext Context { get; private set; }
	protected IModConfig Config { get; private set; }

	private bool _enabled;

	public bool Enabled
	{
		get => _enabled;
		set
		{
			if ( _enabled == value ) return;

			_enabled = value;

			if ( value ) Enable();
			else Disable();

			Config?.Set( EnabledKey, value );
		}
	}

	/// <summary>Called by the framework before the feature is used.</summary>
	public void Attach( ModContext context, IModConfig config )
	{
		Context = context;
		Config = config;

		// Restore the saved on/off state without re-writing it.
		var saved = config?.Get( EnabledKey, false ) ?? false;
		if ( !saved ) return;

		_enabled = true;
		Enable();
	}

	private IReadOnlyList<ModSetting> _settings;

	/// <summary>Built once on first access, after Attach has supplied the config.</summary>
	public IReadOnlyList<ModSetting> Settings => _settings ??= BuildSettings().ToArray();

	/// <summary>Override to describe this feature's options to menus.</summary>
	protected virtual IEnumerable<ModSetting> BuildSettings() => Enumerable.Empty<ModSetting>();

	public virtual void Enable() { }
	public virtual void Disable() { }

	public void Tick()
	{
		if ( !_enabled ) return;

		OnTick();
	}

	/// <summary>Runs every frame while this feature is on.</summary>
	protected virtual void OnTick() { }

	// --- helpers most features want -------------------------------------

	protected Player LocalPlayer => Context?.LocalPlayer;

	protected bool CanAct => LocalPlayer.IsValid() && LocalPlayer.IsAlive;

	/// <summary>Read a numeric setting for this feature, namespaced by its name.</summary>
	protected T Setting<T>( string key, T fallback )
		=> Config is null ? fallback : Config.Get( $"{Name}.{key}".Replace( " ", "" ), fallback );

	protected void SetSetting<T>( string key, T value )
		=> Config?.Set( $"{Name}.{key}".Replace( " ", "" ), value );
}
