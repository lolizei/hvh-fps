using System;

namespace HvH;

/// <summary>What kind of hit the shooter just landed.</summary>
public enum HitKind
{
	Body,
	Headshot,
	Kill,
}

/// <summary>
/// Feedback for the person who pulled the trigger, and nobody else.
///
/// This is deliberately NOT part of the <see cref="WeaponEffects"/> broadcast.
/// Tracers and impacts are world events every machine should see; a hit marker
/// is a private reaction on one screen. The host confirms the hit and tells only
/// the shooter, via [Rpc.Owner] on the weapon.
///
/// Client-local state with no networking of its own - it is only ever set by
/// that owner-directed call.
/// </summary>
public static class HitMarker
{
	// Engine core/ sound events. s&box has no UI click registered as a sound
	// event - the kenney clicks are raw audio - so these short melee impacts are
	// the closest thing to a marker tick that actually exists.
	public const string BodySound = "sounds/impacts/melee/impact-melee-cloth.sound";
	public const string HeadshotSound = "sounds/impacts/melee/impact-melee-glass.sound";
	public const string KillSound = "sounds/impacts/melee/impact-melee-metal.sound";

	/// <summary>How long the marker stays on screen.</summary>
	public static float Duration { get; set; } = 0.4f;

	/// <summary>
	/// The kind of the marker currently on screen. Reads Body once the marker
	/// has expired rather than holding the last value - stale state here is
	/// read straight off the console and wastes the reader's time.
	/// </summary>
	public static HitKind Kind => Visible ? _kind : HitKind.Body;

	/// <summary>How many times Show has run. Diagnostics only.</summary>
	public static int ShowCount { get; set; }

	private static HitKind _kind;
	private static float _shownAt = float.MinValue;

	public static bool Visible => Time.Now - _shownAt < Duration;

	/// <summary>1 when it just appeared, falling to 0 as it expires.</summary>
	public static float Fade
	{
		get
		{
			if ( !Visible ) return 0f;

			return 1f - Math.Clamp( ( Time.Now - _shownAt ) / MathF.Max( 0.01f, Duration ), 0f, 1f );
		}
	}

	/// <summary>Show the marker and play its tick. Local to this machine only.</summary>
	public static void Show( HitKind kind )
	{
		ShowCount++;
		_kind = kind;
		_shownAt = Time.Now;

		var sound = kind switch
		{
			HitKind.Kill => KillSound,
			HitKind.Headshot => HeadshotSound,
			_ => BodySound,
		};

		try
		{
			// 2D - this is UI feedback, not something happening in the world.
			Sound.Play( sound );
		}
		catch ( Exception )
		{
			// A missing sound must never interrupt shooting.
		}
	}

	/// <summary>Colour the crosshair marker draws in, per kind.</summary>
	public static Color ColorFor( HitKind kind ) => kind switch
	{
		HitKind.Kill => new Color( 1f, 0.25f, 0.2f ),
		HitKind.Headshot => new Color( 1f, 0.85f, 0.2f ),
		_ => Color.White,
	};

	/// <summary>Reset between rounds or scene loads.</summary>
	public static void Clear() => _shownAt = float.MinValue;
}
