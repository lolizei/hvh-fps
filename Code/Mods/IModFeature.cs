using System;

namespace HvH.Mods;

/// <summary>
/// One switchable capability inside a mod - aim assist, an ESP overlay, a
/// movement tweak. Features are the unit the menu draws and the unit the
/// player toggles.
/// </summary>
public interface IModFeature
{
	string Name { get; }

	/// <summary>Grouping label for menus: Combat, Visual, Movement, Misc.</summary>
	string Category { get; }

	/// <summary>Optional one-liner shown as a tooltip.</summary>
	string Description { get; }

	bool Enabled { get; set; }

	/// <summary>
	/// This feature's tweakable values, described well enough that any menu can
	/// render them without knowing what the feature is.
	/// </summary>
	IReadOnlyList<ModSetting> Settings { get; }

	/// <summary>Called when switched on. Hook things here, not in the constructor.</summary>
	void Enable();

	/// <summary>Called when switched off. Undo whatever Enable did.</summary>
	void Disable();

	/// <summary>Called every frame while enabled.</summary>
	void Tick();
}
