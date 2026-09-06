namespace HvH;

/// <summary>
/// Which body a pawn wears.
///
/// Both sets are kept deliberately. The Citizen is s&amp;box's own playermodel and
/// looks the part; the blocky humanoids are unambiguous test geometry and read
/// clearly at any distance. Which one is better depends on what you are looking
/// at that day, so it is a switch rather than a decision baked into the code.
/// </summary>
public enum PlayerBodyStyle
{
	/// <summary>The engine Citizen playermodel, team-tinted.</summary>
	Citizen,

	/// <summary>The original blocky test humanoids.</summary>
	Blocks,
}
