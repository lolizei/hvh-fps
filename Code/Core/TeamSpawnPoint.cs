using System;

namespace HvH;

/// <summary>
/// Marks a spawn point as belonging to one side. Sits alongside the engine's
/// <see cref="SpawnPoint"/> so maps that don't care about teams still work.
/// </summary>
public sealed class TeamSpawnPoint : Component
{
	[Property] public Team Team { get; set; } = Team.None;
}
