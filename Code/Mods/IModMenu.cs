using System;

namespace HvH.Mods;

/// <summary>
/// A mod's user interface. Intentionally tiny: the framework only needs to
/// know how to show and hide it, never what it looks like or what UI toolkit
/// it uses. That is what makes the default menu fully replaceable.
/// </summary>
public interface IModMenu
{
	bool IsOpen { get; }

	/// <summary>Key that toggles this menu, as an input action or key name.</summary>
	string ToggleKey { get; }

	void Open();
	void Close();
}
