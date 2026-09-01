using System;

namespace HvH;

/// <summary>
/// Escape handling for the gameplay scene.
///
/// Escape closes an open mod menu first, and otherwise leaves the match and
/// goes back to the front end. It disconnects before loading the menu so the
/// lobby is torn down rather than left running behind the menu - that leftover
/// session is what makes a second Play fail.
/// </summary>
public sealed class GameExitHandler : Component
{
	/// <summary>The menu scene to return to.</summary>
	[Property] public SceneFile MenuScene { get; set; }

	protected override void OnUpdate()
	{
		if ( !Input.EscapePressed ) return;

		// Consume it so the engine doesn't also act on it this frame.
		Input.EscapePressed = false;

		// A mod menu is on screen - Escape should close that, not quit the match.
		var mods = Mods.ModManager.Current;
		if ( mods.IsValid() && mods.AnyMenuOpen )
		{
			mods.CloseAllMenus();
			return;
		}

		ReturnToMenu();
	}

	/// <summary>Leave the match and load the front end.</summary>
	public void ReturnToMenu()
	{
		if ( MenuScene is null )
		{
			Log.Warning( "GameExitHandler has no MenuScene assigned - staying put." );
			return;
		}

		// Drop the lobby first, otherwise it survives into the menu and the next
		// Play tries to create a second one.
		if ( Networking.IsActive )
			Networking.Disconnect();

		Scene.Load( MenuScene );
	}
}
