using System;

namespace HvH.Mods;

/// <summary>
/// Convenience base for mods: owns the feature list, the config, and the
/// enable/disable rules. A mod written on this only has to say what its
/// features are and, if it wants one, hand back a menu.
/// </summary>
public abstract class ModBase : IGameMod
{
	public abstract string Id { get; }
	public abstract string Name { get; }
	public abstract string Author { get; }
	public virtual string Version => "1.0.0";

	private readonly List<IModFeature> _features = new();

	public IReadOnlyList<IModFeature> Features => _features;

	public virtual IModMenu Menu => null;

	public IModConfig Config { get; private set; }

	protected ModContext Context { get; private set; }

	private bool _enabled = true;

	public bool Enabled
	{
		get => _enabled;
		set
		{
			if ( _enabled == value ) return;

			_enabled = value;

			// Turning a mod off must not lose which features were on - the
			// feature keeps its own Enabled flag, we simply stop ticking it
			// and close the menu.
			if ( !value )
				Menu?.Close();
		}
	}

	public void Initialize( ModContext context )
	{
		Context = context;
		Config = new ModConfig( Id );
		Config.Load( ModConfig.DefaultProfile );

		OnInitialize();

		foreach ( var feature in _features )
		{
			if ( feature is ModFeature typed )
				typed.Attach( context, Config );
		}
	}

	/// <summary>Register features here with <see cref="Register"/>.</summary>
	protected abstract void OnInitialize();

	/// <summary>Add a feature to this mod. Call from <see cref="OnInitialize"/>.</summary>
	protected T Register<T>( T feature ) where T : IModFeature
	{
		_features.Add( feature );
		return feature;
	}

	public virtual void Shutdown()
	{
		Menu?.Close();

		foreach ( var feature in _features )
		{
			if ( feature.Enabled )
				feature.Disable();
		}

		Config?.Save();
	}

	/// <summary>Features grouped by category, for menus that draw sections.</summary>
	public IEnumerable<IGrouping<string, IModFeature>> FeaturesByCategory()
		=> _features.GroupBy( f => f.Category );
}
