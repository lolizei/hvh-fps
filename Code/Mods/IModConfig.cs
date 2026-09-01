using System;

namespace HvH.Mods;

/// <summary>
/// A mod's settings bag, saved under a named profile so players can keep
/// several configs and switch between them.
/// </summary>
public interface IModConfig
{
	/// <summary>Profile currently loaded, e.g. "default" or "legit".</summary>
	string Profile { get; }

	T Get<T>( string key, T fallback = default );
	void Set<T>( string key, T value );
	bool Has( string key );

	/// <summary>Write the current values to the named profile.</summary>
	void Save( string profile = null );

	/// <summary>Replace the current values with the named profile's.</summary>
	void Load( string profile );

	/// <summary>Profiles that exist on disk for this mod.</summary>
	IEnumerable<string> ListProfiles();
}
