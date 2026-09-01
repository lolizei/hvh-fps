using System;

namespace HvH.Mods;

/// <summary>
/// An <see cref="IModMenu"/> backed by a Razor panel.
///
/// The menu owns a throwaway GameObject carrying a <see cref="ScreenPanel"/> and
/// the panel component, created on open and destroyed on close. Because the
/// framework only ever calls Open/Close, a mod is free to ignore this class
/// entirely and implement <see cref="IModMenu"/> with any UI it likes.
/// </summary>
/// <typeparam name="TPanel">The Razor PanelComponent to show.</typeparam>
public class RazorModMenu<TPanel> : IModMenu where TPanel : PanelComponent, new()
{
	private readonly ModManager _manager;
	private readonly string _name;
	private GameObject _root;

	public RazorModMenu( ModManager manager, string toggleKey, string name = null )
	{
		_manager = manager;
		ToggleKey = toggleKey;
		_name = name ?? typeof( TPanel ).Name;
	}

	/// <summary>Input action that toggles this menu.</summary>
	public string ToggleKey { get; }

	public bool IsOpen => _root.IsValid();

	/// <summary>The live panel while open, for menus that need to poke at it.</summary>
	protected TPanel Panel { get; private set; }

	public void Open()
	{
		if ( IsOpen ) return;

		var scene = _manager.IsValid() ? _manager.Scene : Game.ActiveScene;
		if ( scene is null ) return;

		_root = scene.CreateObject();
		_root.Name = $"ModMenu - {_name}";

		var screen = _root.AddComponent<ScreenPanel>();
		// Sit above the HUD so the menu is never drawn behind the crosshair.
		screen.ZIndex = 200;

		Panel = _root.AddComponent<TPanel>();
		Configure( Panel );

		// Menus are click-driven, so the player needs a pointer back.
		Mouse.Visibility = MouseVisibility.Visible;
	}

	public void Close()
	{
		if ( !IsOpen )
			return;

		_root.Destroy();
		_root = null;
		Panel = null;

		Mouse.Visibility = MouseVisibility.Auto;
	}

	/// <summary>Hook for handing the panel whatever it needs before it renders.</summary>
	protected virtual void Configure( TPanel panel ) { }
}
