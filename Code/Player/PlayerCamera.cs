using System;

namespace HvH;

/// <summary>
/// Drives the first person view. Runs in OnPreRender rather than OnUpdate so
/// the camera is placed after all movement for the frame has already happened -
/// doing it in OnUpdate leaves the view a frame behind and reads as jitter.
/// </summary>
public sealed class PlayerCamera : Component
{
	/// <summary>Auto-resolved from the pawn's children if left unset in the prefab.</summary>
	[Property] public CameraComponent Camera { get; set; }

	[Property] public float StandEyeHeight { get; set; } = 64f;
	[Property] public float CrouchEyeHeight { get; set; } = 38f;

	/// <summary>How fast the view drops when ducking. Instant snapping looks awful.</summary>
	[Property] public float EyeLerpSpeed { get; set; } = 12f;

	[Property] public float FieldOfView { get; set; } = 90f;

	/// <summary>
	/// Set by mods (custom FOV, third person) to take over the view angle.
	/// Null means "use the player's own setting".
	/// </summary>
	public float? FovOverride { get; set; }

	/// <summary>
	/// Set by mods (third person) to place the camera somewhere other than the
	/// player's eyes. Null means "use the eye position".
	/// </summary>
	public Vector3? PositionOverride { get; set; }

	/// <summary>The pawn's visible body. Hidden from its own camera in first person.</summary>
	[Property] public ModelRenderer Body { get; set; }

	private Player _player;
	private PlayerMovement _movement;
	private float _eyeHeight;

	protected override void OnAwake()
	{
		_player = GetComponent<Player>();
		_movement = GetComponent<PlayerMovement>();
		_eyeHeight = StandEyeHeight;

		if ( !Camera.IsValid() )
			Camera = GetComponentInChildren<CameraComponent>( true );

		if ( !Body.IsValid() )
			Body = GetComponentInChildren<ModelRenderer>( true );
	}

	protected override void OnStart()
	{
		// Only the pawn the human is actually playing renders from its eyes.
		// This must be IsLocallyControlled, not !IsProxy: a host-owned bot is
		// not a proxy, so it would switch on a second camera and hide its own
		// body from everyone.
		if ( !_player.IsValid() || !_player.IsLocallyControlled )
		{
			if ( Camera.IsValid() )
				Camera.Enabled = false;

			return;
		}

		// We're inside our own body in first person - keep casting a shadow so
		// the player can still see themselves on the floor, but don't render it.
		if ( Body.IsValid() )
			Body.RenderType = ModelRenderer.ShadowRenderType.ShadowsOnly;
	}

	protected override void OnPreRender()
	{
		if ( !_player.IsValid() || !_player.IsLocallyControlled ) return;
		if ( !Camera.IsValid() ) return;

		var target = _movement.IsValid() && _movement.IsCrouching ? CrouchEyeHeight : StandEyeHeight;
		_eyeHeight = _eyeHeight.LerpTo( target, Time.Delta * EyeLerpSpeed );

		var eye = _player.Eye;
		if ( eye.IsValid() )
			eye.LocalPosition = Vector3.Up * _eyeHeight;

		Camera.WorldPosition = PositionOverride ?? ( WorldPosition + Vector3.Up * _eyeHeight );
		Camera.WorldRotation = _player.EyeAngles.ToRotation();
		// Field of view is a player preference, not a per-pawn constant. A mod
		// can still override it by writing to FieldOfView directly.
		Camera.FieldOfView = FovOverride ?? GameSettings.Current.FieldOfView;
	}
}
