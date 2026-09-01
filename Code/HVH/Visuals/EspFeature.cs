using System;
using Sandbox.Rendering;
using HvH.Mods;

namespace HvH.HVH;

/// <summary>
/// Draws information about other players over the world: a box, health, name
/// and distance.
///
/// Uses the camera's immediate-mode HUD painter rather than a Razor panel, so
/// it costs nothing when switched off and never fights the real HUD for layout.
/// </summary>
public sealed class EspFeature : ModFeature
{
	public override string Name => "Player Information";
	public override string Category => "Visual";
	public override string Description => "Boxes, health, names and distance for other players.";

	public bool ShowBox
	{
		get => Setting( "ShowBox", true );
		set => SetSetting( "ShowBox", value );
	}

	public bool ShowHealth
	{
		get => Setting( "ShowHealth", true );
		set => SetSetting( "ShowHealth", value );
	}

	public bool ShowName
	{
		get => Setting( "ShowName", true );
		set => SetSetting( "ShowName", value );
	}

	public bool ShowDistance
	{
		get => Setting( "ShowDistance", false );
		set => SetSetting( "ShowDistance", value );
	}

	/// <summary>Draw team-mates too, not just enemies.</summary>
	public bool ShowTeam
	{
		get => Setting( "ShowTeam", false );
		set => SetSetting( "ShowTeam", value );
	}

	/// <summary>Dim players we have no line of sight to.</summary>
	public bool DimOccluded
	{
		get => Setting( "DimOccluded", true );
		set => SetSetting( "DimOccluded", value );
	}

	protected override IEnumerable<ModSetting> BuildSettings()
	{
		yield return ModSetting.Toggle( "Box", () => ShowBox, v => ShowBox = v );
		yield return ModSetting.Toggle( "Health", () => ShowHealth, v => ShowHealth = v );
		yield return ModSetting.Toggle( "Name", () => ShowName, v => ShowName = v );
		yield return ModSetting.Toggle( "Distance", () => ShowDistance, v => ShowDistance = v );
		yield return ModSetting.Toggle( "Show Team-mates", () => ShowTeam, v => ShowTeam = v );
		yield return ModSetting.Toggle( "Dim Occluded", () => DimOccluded, v => DimOccluded = v );
	}

	protected override void OnTick()
	{
		var local = LocalPlayer;
		if ( !local.IsValid() ) return;

		var camera = local.GetComponentInChildren<CameraComponent>();
		if ( !camera.IsValid() ) return;

		// HudPainter is a struct - nothing to null-check.
		var hud = camera.Hud;

		var eye = local.AimRay.Position;

		foreach ( var player in Player.All )
		{
			if ( !player.IsValid() || player == local || !player.IsAlive ) continue;

			var friendly = local.Team.IsPlaying() && player.Team == local.Team;
			if ( friendly && !ShowTeam ) continue;

			Draw( hud, camera, player, eye, friendly );
		}
	}

	private void Draw( HudPainter hud, CameraComponent camera, Player player, Vector3 eye, bool friendly )
	{
		var movement = player.Movement;
		var height = movement.IsValid()
			? ( movement.IsCrouching ? movement.CrouchHeight : movement.StandHeight )
			: 72f;

		var feet = player.WorldPosition;
		var head = feet + Vector3.Up * height;

		// Project the two ends of the player; if either is behind us, skip.
		var bottom = camera.PointToScreenPixels( feet, out var feetBehind );
		var top = camera.PointToScreenPixels( head, out var headBehind );
		if ( feetBehind || headBehind ) return;

		var boxHeight = MathF.Abs( bottom.y - top.y );
		if ( boxHeight < 4f ) return;

		// Player boxes are roughly half as wide as they are tall.
		var boxWidth = boxHeight * 0.5f;
		var left = top.x - boxWidth * 0.5f;

		var visible = !DimOccluded || TargetSelector.IsVisible(
			LocalPlayer, player, eye, feet + Vector3.Up * ( height * 0.5f ) );

		// Team colour when sides are assigned, otherwise a neutral enemy red.
		var color = player.Team.IsPlaying()
			? player.Team.Color()
			: new Color( 0.95f, 0.35f, 0.3f );

		if ( !visible ) color = color.WithAlpha( 0.35f );

		if ( ShowBox )
			DrawBox( hud, left, top.y, boxWidth, boxHeight, color );

		if ( ShowHealth && player.Health.IsValid() )
			DrawHealthBar( hud, left, top.y, boxHeight, player.Health.Fraction );

		var label = BuildLabel( player, feet, eye );
		if ( !string.IsNullOrEmpty( label ) )
		{
			hud.DrawText( label, 13f, color,
				new Vector2( left + boxWidth * 0.5f, top.y - 14f ),
				TextFlag.Center );
		}
	}

	private static void DrawBox( HudPainter hud, float x, float y, float w, float h, Color color )
	{
		var thickness = 1.5f;

		hud.DrawLine( new Vector2( x, y ), new Vector2( x + w, y ), thickness, color, default );
		hud.DrawLine( new Vector2( x, y + h ), new Vector2( x + w, y + h ), thickness, color, default );
		hud.DrawLine( new Vector2( x, y ), new Vector2( x, y + h ), thickness, color, default );
		hud.DrawLine( new Vector2( x + w, y ), new Vector2( x + w, y + h ), thickness, color, default );
	}

	private static void DrawHealthBar( HudPainter hud, float boxLeft, float top, float height, float fraction )
	{
		var x = boxLeft - 6f;
		var filled = height * Math.Clamp( fraction, 0f, 1f );

		// Green at full, red as it drains.
		var color = Color.Lerp( Color.Red, Color.Green, fraction );

		hud.DrawLine( new Vector2( x, top + height ), new Vector2( x, top + height - filled ), 3f, color, default );
	}

	private string BuildLabel( Player player, Vector3 feet, Vector3 eye )
	{
		var parts = new List<string>();

		if ( ShowName )
		{
			var name = player.State.IsValid() ? player.State.DisplayName : "player";
			parts.Add( name );
		}

		if ( ShowDistance )
		{
			// Source units to metres, roughly.
			var metres = feet.Distance( eye ) / 39.37f;
			parts.Add( $"{metres:0}m" );
		}

		return string.Join( "  ", parts );
	}
}
