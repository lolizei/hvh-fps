namespace HvH;

/// <summary>Phases of the round loop. Driven by <see cref="RoundManager"/>.</summary>
public enum RoundState
{
	/// <summary>Not enough players. Everyone respawns freely.</summary>
	Warmup,
	/// <summary>Everyone is at spawn, frozen, waiting for the round to go live.</summary>
	RoundStart,
	/// <summary>Live. No respawning - last team standing wins.</summary>
	Playing,
	/// <summary>A team has won. Scores are updated, bodies stay where they fell.</summary>
	RoundEnd,
	/// <summary>Brief reset before the next round begins.</summary>
	Restarting,
}
