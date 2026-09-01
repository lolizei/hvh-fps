using System;

namespace HvH;

/// <summary>
/// One place that answers "is this hit allowed?". Sits between weapons and
/// health so friendly fire is a rule of the game rather than something each
/// weapon has to remember to check.
/// </summary>
public static class DamageRules
{
	/// <summary>
	/// False when the hit should be discarded entirely. Self-damage is always
	/// allowed so future explosives can hurt the person who threw them.
	/// </summary>
	public static bool CanDamage( GameObject attacker, GameObject victim )
	{
		if ( !attacker.IsValid() || !victim.IsValid() ) return true;
		if ( attacker == victim ) return true;

		var attackerTeam = TeamOf( attacker );
		var victimTeam = TeamOf( victim );

		// Unassigned players and world damage are never filtered.
		if ( !attackerTeam.IsPlaying() || !victimTeam.IsPlaying() ) return true;
		if ( attackerTeam != victimTeam ) return true;

		return TeamManager.Current?.FriendlyFire ?? false;
	}

	public static Team TeamOf( GameObject go )
		=> go.IsValid() ? go.GetComponentInParent<PlayerState>()?.Team ?? Team.None : Team.None;
}
