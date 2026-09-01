using System;

namespace HvH;

/// <summary>
/// Chooses where players spawn. Prefers spawn points belonging to the player's
/// team, and falls back progressively so a map with no spawn markers at all
/// still produces something playable.
/// </summary>
public static class SpawnSystem
{
	/// <summary>
	/// Pick a spawn transform for a team. Falls back to any spawn point, then
	/// to a point above the world origin.
	/// </summary>
	public static Transform Pick( Scene scene, Team team = Team.None )
	{
		var points = scene?.GetAllComponents<SpawnPoint>().ToArray();
		if ( points is null || points.Length == 0 )
			return new Transform( Vector3.Up * 64f );

		if ( team.IsPlaying() )
		{
			var owned = points.Where( x => TeamOf( x ) == team ).ToArray();
			if ( owned.Length > 0 )
				return Choose( owned );
		}

		// Prefer neutral points over an enemy's spawn when we have no team ones.
		var neutral = points.Where( x => TeamOf( x ) == Team.None ).ToArray();
		return Choose( neutral.Length > 0 ? neutral : points );
	}

	private static Transform Choose( SpawnPoint[] points )
		=> Random.Shared.FromArray( points ).WorldTransform.WithScale( 1f );

	private static Team TeamOf( SpawnPoint point )
		=> point.GetComponent<TeamSpawnPoint>()?.Team ?? Team.None;
}
