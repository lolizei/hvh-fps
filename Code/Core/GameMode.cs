namespace HvH;

/// <summary>
/// How a round is won.
///
/// Deliberately a scene-level property rather than a global setting: a map is
/// built for a mode, and the Compound layout only makes sense as deathmatch.
/// </summary>
public enum GameMode
{
	/// <summary>A kill sticks. Last team standing takes the round.</summary>
	Elimination,

	/// <summary>Everyone respawns. Frags take the round, or the clock does.</summary>
	Deathmatch,
}
