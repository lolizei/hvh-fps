using System;

namespace HvH;

/// <summary>
/// The two sides. Deliberately original names - this game is inspired by
/// tactical shooters, it does not borrow their branding.
/// </summary>
public enum Team
{
	/// <summary>Not playing - connecting, spectating, or unassigned.</summary>
	None = 0,
	Vanguard = 1,
	Syndicate = 2,
}

public static class TeamExtensions
{
	public static string DisplayName( this Team team ) => team switch
	{
		Team.Vanguard => "Vanguard",
		Team.Syndicate => "Syndicate",
		_ => "Spectator",
	};

	/// <summary>Team colour, used by the HUD, scoreboard and enemy indicators.</summary>
	public static Color Color( this Team team ) => team switch
	{
		Team.Vanguard => new Color( 0.35f, 0.65f, 0.95f ),
		Team.Syndicate => new Color( 0.95f, 0.68f, 0.25f ),
		_ => new Color( 0.7f, 0.7f, 0.7f ),
	};

	public static Team Opposite( this Team team ) => team switch
	{
		Team.Vanguard => Team.Syndicate,
		Team.Syndicate => Team.Vanguard,
		_ => Team.None,
	};

	/// <summary>True for the two sides that actually play. Excludes <see cref="Team.None"/>.</summary>
	public static bool IsPlaying( this Team team ) => team is Team.Vanguard or Team.Syndicate;
}
