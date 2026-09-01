using System;
using HvH.Mods;

namespace HvH.HVH;

/// <summary>
/// Re-jumps the instant you touch the ground while jump is held, which is the
/// timing the base game deliberately does not give away (it jumps on press,
/// not on hold).
/// </summary>
public sealed class BunnyHopFeature : ModFeature
{
	public override string Name => "Bunny Hop";
	public override string Category => "Movement";
	public override string Description => "Auto-jumps on landing while jump is held.";

	/// <summary>Chance per landing that the hop actually fires, 0-1.</summary>
	public float Consistency
	{
		get => Setting( "Consistency", 1f );
		set => SetSetting( "Consistency", Math.Clamp( value, 0f, 1f ) );
	}

	protected override IEnumerable<ModSetting> BuildSettings()
	{
		yield return ModSetting.Slider( "Consistency", () => Consistency, v => Consistency = v, 0f, 1f, 0.05f );
	}

	private bool _wasOnGround;

	protected override void OnTick()
	{
		if ( !CanAct ) return;

		var movement = LocalPlayer.Movement;
		if ( !movement.IsValid() ) return;

		var onGround = movement.IsOnGround;
		var landed = onGround && !_wasOnGround;
		_wasOnGround = onGround;

		if ( !onGround ) return;
		if ( !Input.Down( "Jump" ) ) return;

		// Only act on the landing frame, otherwise we fight the normal jump.
		if ( !landed ) return;
		if ( Consistency < 1f && Random.Shared.Float( 0f, 1f ) > Consistency ) return;

		movement.ForceJump();
	}
}
