using System;
using System.Text.Json.Serialization;

namespace HvH;

/// <summary>
/// Ordinary player preferences - sensitivity, view, crosshair, video, audio.
///
/// Deliberately has nothing to do with the mod framework. Mod state lives in
/// <see cref="Mods.IModConfig"/> and is stored separately, so wiping your cheat
/// config never resets your sensitivity and vice versa.
/// </summary>
public sealed class GameSettings
{
	public const string FileName = "settings.json";

	// ---- Input ----------------------------------------------------------
	public float MouseSensitivity { get; set; } = 1f;

	// ---- View -----------------------------------------------------------
	public float FieldOfView { get; set; } = 90f;
	public float ViewmodelFieldOfView { get; set; } = 70f;
	public bool ViewmodelVisible { get; set; } = true;

	// ---- Crosshair ------------------------------------------------------
	public float CrosshairLength { get; set; } = 7f;
	public float CrosshairGap { get; set; } = 4f;
	public float CrosshairThickness { get; set; } = 2f;
	public bool CrosshairDot { get; set; }
	public bool CrosshairDynamic { get; set; } = true;

	[JsonIgnore]
	public Color CrosshairColor
	{
		get => Color.Parse( CrosshairColorHex ) ?? Color.Green;
		set => CrosshairColorHex = value.Hex;
	}

	/// <summary>Stored as hex so the settings file stays human-editable.</summary>
	public string CrosshairColorHex { get; set; } = "#00FF6AFF";

	// ---- Video ----------------------------------------------------------
	public bool Bloom { get; set; } = true;
	public bool MotionBlur { get; set; }

	// ---- Audio ----------------------------------------------------------
	public float MasterVolume { get; set; } = 1f;
	public float EffectsVolume { get; set; } = 1f;

	private static GameSettings _current;

	/// <summary>Loaded once, then held for the session.</summary>
	public static GameSettings Current => _current ??= Load();

	/// <summary>Raised after settings change so live systems can re-read them.</summary>
	public static event Action Changed;

	public static GameSettings Load()
	{
		try
		{
			return FileSystem.Data.ReadJson( FileName, new GameSettings() );
		}
		catch ( Exception )
		{
			// A corrupt settings file must never stop the game booting.
			return new GameSettings();
		}
	}

	public void Save()
	{
		try
		{
			FileSystem.Data.WriteJson( FileName, this );
		}
		catch ( Exception e )
		{
			Log.Warning( $"Couldn't save settings: {e.Message}" );
		}

		Changed?.Invoke();
	}

	/// <summary>Throw everything back to defaults and persist that.</summary>
	public static void ResetToDefaults()
	{
		_current = new GameSettings();
		_current.Save();
	}
}
