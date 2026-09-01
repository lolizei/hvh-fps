using System;

namespace HvH.Mods;

/// <summary>Which part of a target to aim at.</summary>
public enum TargetBone
{
	Head,
	Chest,
	Pelvis,
	Nearest,
}

/// <summary>How to choose between several valid targets.</summary>
public enum TargetMode
{
	/// <summary>Closest to where the crosshair already points.</summary>
	Crosshair,
	/// <summary>Closest in world space.</summary>
	Distance,
	/// <summary>Lowest health.</summary>
	Weakest,
}

/// <summary>The result of a target search.</summary>
public readonly struct AimTarget
{
	public AimTarget( Player player, Vector3 point, float fov, float distance )
	{
		Player = player;
		Point = point;
		Fov = fov;
		Distance = distance;
	}

	public Player Player { get; }

	/// <summary>World point to aim at.</summary>
	public Vector3 Point { get; }

	/// <summary>Angle in degrees between the current view and this target.</summary>
	public float Fov { get; }

	public float Distance { get; }

	public bool IsValid => Player.IsValid();
}

/// <summary>
/// Shared target selection. Aim assist, ESP-driven features and anything else
/// that needs "who am I looking at" go through here so they all agree, and so a
/// custom mod gets the same quality of targeting the built-in one has.
/// </summary>
public static class TargetSelector
{
	/// <summary>
	/// Best target for the local player, or an invalid result if nothing
	/// qualifies.
	/// </summary>
	/// <param name="context">The calling mod's context, used for the enemy list.</param>
	/// <param name="fovLimit">Maximum angle from the crosshair, in degrees.</param>
	/// <param name="bone">Which part of the target to aim at.</param>
	/// <param name="mode">How to choose between several valid targets.</param>
	/// <param name="requireVisible">Discard targets we have no line of sight to.</param>
	/// <param name="maxDistance">Ignore targets further away than this.</param>
	public static AimTarget Find(
		ModContext context,
		float fovLimit = 30f,
		TargetBone bone = TargetBone.Head,
		TargetMode mode = TargetMode.Crosshair,
		bool requireVisible = true,
		float maxDistance = 8192f )
	{
		var local = context?.LocalPlayer;
		if ( !local.IsValid() || !local.IsAlive ) return default;

		var eye = local.AimRay.Position;
		var forward = local.EyeAngles.Forward;

		AimTarget best = default;
		var bestScore = float.MaxValue;

		foreach ( var enemy in context.AliveEnemies )
		{
			var point = PointOn( enemy, bone, eye );
			var toTarget = point - eye;

			var distance = toTarget.Length;
			if ( distance > maxDistance || distance <= 0f ) continue;

			var fov = Vector3.GetAngle( forward, toTarget.Normal );
			if ( fov > fovLimit ) continue;

			if ( requireVisible && !IsVisible( local, enemy, eye, point ) ) continue;

			var score = mode switch
			{
				TargetMode.Distance => distance,
				TargetMode.Weakest => enemy.Health.IsValid() ? enemy.Health.Health : float.MaxValue,
				_ => fov,
			};

			if ( score >= bestScore ) continue;

			bestScore = score;
			best = new AimTarget( enemy, point, fov, distance );
		}

		return best;
	}

	/// <summary>World position of the requested bone on a player.</summary>
	public static Vector3 PointOn( Player player, TargetBone bone, Vector3 from )
	{
		if ( !player.IsValid() ) return Vector3.Zero;

		var feet = player.WorldPosition;
		var movement = player.Movement;
		var height = movement.IsValid()
			? ( movement.IsCrouching ? movement.CrouchHeight : movement.StandHeight )
			: 72f;

		return bone switch
		{
			TargetBone.Head => feet + Vector3.Up * ( height * 0.92f ),
			TargetBone.Chest => feet + Vector3.Up * ( height * 0.66f ),
			TargetBone.Pelvis => feet + Vector3.Up * ( height * 0.45f ),
			// Nearest: whichever of the three is the smallest angle away is a
			// good enough approximation and costs nothing to compute.
			_ => NearestPoint( feet, height, from ),
		};
	}

	private static Vector3 NearestPoint( Vector3 feet, float height, Vector3 from )
	{
		var candidates = new[]
		{
			feet + Vector3.Up * ( height * 0.92f ),
			feet + Vector3.Up * ( height * 0.66f ),
			feet + Vector3.Up * ( height * 0.45f ),
		};

		var best = candidates[0];
		var bestDistance = float.MaxValue;

		foreach ( var candidate in candidates )
		{
			var distance = candidate.Distance( from );
			if ( distance >= bestDistance ) continue;

			bestDistance = distance;
			best = candidate;
		}

		return best;
	}

	/// <summary>Line of sight test that ignores both players' own bodies.</summary>
	public static bool IsVisible( Player from, Player to, Vector3 eye, Vector3 point )
	{
		if ( !from.IsValid() || !to.IsValid() ) return false;

		var trace = from.Scene.Trace
			.Ray( eye, point )
			.IgnoreGameObjectHierarchy( from.GameObject )
			.IgnoreGameObjectHierarchy( to.GameObject )
			.Run();

		// Nothing solid in between means we can see them.
		return !trace.Hit;
	}
}
