using System;

namespace HvH.Mods;

/// <summary>
/// A mod: a named bundle of features, its own config, and optionally its own
/// menu. Implement this anywhere in the codebase and <see cref="ModManager"/>
/// will find it - there is no registration list to edit.
///
/// The game's own HVH menu is written against this interface like any other
/// mod, so a third party can replace it wholesale.
/// </summary>
public interface IGameMod
{
	/// <summary>Stable identifier. Used for config filenames, so keep it filesystem-safe.</summary>
	string Id { get; }

	string Name { get; }
	string Author { get; }
	string Version { get; }

	/// <summary>
	/// A disabled mod ticks nothing and its menu cannot be opened, but it stays
	/// loaded and keeps its config.
	/// </summary>
	bool Enabled { get; set; }

	/// <summary>Everything this mod can do. Read after <see cref="Initialize"/>.</summary>
	IReadOnlyList<IModFeature> Features { get; }

	/// <summary>This mod's UI, or null if it doesn't want one.</summary>
	IModMenu Menu { get; }

	/// <summary>This mod's saved settings.</summary>
	IModConfig Config { get; }

	/// <summary>
	/// Called once when the mod is loaded. Register features here. The context
	/// is the mod's handle on the game - prefer it over reaching for statics.
	/// </summary>
	void Initialize( ModContext context );

	/// <summary>Called when the game tears down. Release anything you hooked.</summary>
	void Shutdown();
}
