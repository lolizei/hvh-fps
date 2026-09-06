using System;

namespace HvH;

/// <summary>
/// The weapon you can see in a pawn's hands, and the pose that sells it.
///
/// Presentation only. It reads the inventory, the pawn's aim and its movement,
/// and shows what they imply. It never fires, never decides, never touches
/// damage or ammo, and never tells the bot anything - the dependency runs one
/// way, gameplay to presentation.
///
/// The same component drives humans and bots. A bot is an ordinary pawn with an
/// ordinary inventory, so it gets a held weapon for the same reason a human
/// does, with no bot-specific code.
///
/// Everything here was measured against the model rather than assumed. The
/// Citizen exposes a `hold_R` attachment (verified with GetAttachment) and the
/// animation graph responds to `holdtype`, `holdtype_pose`, `move_*`, `wish_*`,
/// `duck`, `b_grounded` and the aim weights. It does NOT respond to `b_attack`,
/// `b_reload` or `handedness` - those moved the pose no more than it drifts on
/// its own - so firing and reloading are shown on the weapon itself.
/// </summary>
public sealed class HeldWeaponPresentation : Component
{
	/// <summary>Attachment the weapon is hung from. Verified to exist.</summary>
	[Property] public string HoldAttachment { get; set; } = "hold_R";

	/// <summary>Fine tuning, in the attachment's local space.</summary>
	[Property] public Vector3 Offset { get; set; } = new( 2f, 0f, 0f );

	[Property] public Angles Rotation { get; set; } = new( 0f, 0f, 0f );

	[Property] public float Scale { get; set; } = 1f;

	/// <summary>Where the support hand would sit. Diagnostics only now.</summary>
	[Property] public Vector3 SupportGrip { get; set; } = new( 12f, 0f, -1f );

	[Property] public float FireKick { get; set; } = 2.5f;
	[Property] public float ReloadDrop { get; set; } = 6f;
	[Property] public float Recovery { get; set; } = 9f;

	/// <summary>The live weapon object. Diagnostics.</summary>
	public GameObject Current { get; private set; }

	/// <summary>Which weapon it represents. Diagnostics.</summary>
	public Weapon CurrentWeapon { get; private set; }

	/// <summary>Why nothing is being shown, when nothing is. Diagnostics.</summary>
	public string Status { get; private set; } = "not ticked";

	private Player _player;
	private PlayerPresentation _presentation;
	private PlayerMovement _movement;
	private SkinnedModelRenderer _body;
	private GameObject _bodyRoot;

	private Vector3 _kick;
	private Angles _kickAngles;
	private bool _wasReloading;

	protected override void OnAwake()
	{
		_player = GetComponent<Player>();
		_presentation = GetComponent<PlayerPresentation>();
		_movement = GetComponent<PlayerMovement>();
	}

	protected override void OnDestroy() => Clear();

	protected override void OnUpdate()
	{
		if ( !_player.IsValid() )
		{
			Status = "no player";
			return;
		}

		// A corpse should not be holding anything up.
		if ( !_player.IsAlive )
		{
			Status = "dead";
			Clear();
			return;
		}

		if ( !ResolveBody() )
		{
			Status = "no Citizen body - Style is probably Blocks";
			Clear();
			return;
		}

		var weapon = _player.Inventory?.ActiveWeapon;
		if ( weapon != CurrentWeapon )
			Equip( weapon );

		DriveAnimation( weapon );

		if ( !Current.IsValid() ) return;

		TickReloadCue( weapon );
		Decay();
		Place();
	}

	/// <summary>The Citizen renderer, which PlayerPresentation owns and rebuilds.</summary>
	private bool ResolveBody()
	{
		var root = _presentation?.Current;
		if ( !root.IsValid() )
		{
			_body = null;
			_bodyRoot = null;
			return false;
		}

		// Re-resolve when the body is rebuilt under us - team change, respawn,
		// or a style switch. Comparing the root we resolved from is enough.
		if ( !_body.IsValid() || _bodyRoot != root )
		{
			_body = root.GetComponentInChildren<SkinnedModelRenderer>( true );
			_bodyRoot = root;
		}

		return _body.IsValid();
	}

	private void Equip( Weapon weapon )
	{
		Clear();
		CurrentWeapon = weapon;

		// Subscribe rather than be called: Weapon already raises Fired, and it
		// should not know a third-person model exists.
		if ( weapon.IsValid() )
			weapon.Fired += OnFired;

		var path = weapon?.Resolved?.ViewModelPath;
		if ( string.IsNullOrWhiteSpace( path ) )
		{
			Status = weapon.IsValid() ? $"'{weapon.DisplayName}' has no model" : "nothing equipped";
			return;
		}

		try
		{
			// Parented to the pawn so it is cleaned up with it, but positioned
			// from the hand attachment every frame.
			Current = GameObject.Clone( path, WorldTransform, GameObject, true, "held" );
		}
		catch ( Exception e )
		{
			Log.Warning( $"HeldWeaponPresentation: couldn't load '{path}': {e.Message}" );
			return;
		}

		if ( !Current.IsValid() )
		{
			Log.Warning( $"HeldWeaponPresentation: '{path}' resolved to nothing." );
			return;
		}

		// The pawn you are playing is invisible from its own eyes, and so is the
		// gun in its hands - the first-person view model is the one you see.
		if ( _player.IsLocallyControlled )
		{
			foreach ( var r in Current.GetComponentsInChildren<ModelRenderer>( true ) )
				r.RenderType = ModelRenderer.ShadowRenderType.ShadowsOnly;
		}

		Status = $"holding {path}";
	}

	/// <summary>
	/// Tell the Citizen what it is doing: carrying a weapon, moving, aiming.
	///
	/// Every parameter here was confirmed to actually move the pose.
	/// </summary>
	private void DriveAnimation( Weapon weapon )
	{
		if ( !_body.IsValid() ) return;

		// Hold pose by weapon size. A pistol is held differently to a rifle, and
		// this is the difference between "armed" and "carrying a plank".
		var slot = weapon?.Resolved?.Slot ?? WeaponSlot.Melee;
		var holdType = weapon.IsValid()
			? slot == WeaponSlot.Secondary ? PistolHold : RifleHold
			: NoHold;

		_body.Set( "holdtype", holdType );
		_body.Set( "holdtype_pose", Aiming ? AimPose : ReadyPose );

		// Locomotion, from the movement component rather than a second guess at
		// what the pawn is doing.
		var velocity = _movement.IsValid() ? _movement.Velocity : Vector3.Zero;
		var flat = velocity.WithZ( 0f );
		var forward = WorldRotation.Forward;
		var right = WorldRotation.Right;

		_body.Set( "move_speed", flat.Length );
		_body.Set( "move_groundspeed", flat.Length );
		_body.Set( "wish_x", flat.Dot( forward ) );
		_body.Set( "wish_y", flat.Dot( right ) );
		_body.Set( "move_direction", MathF.Atan2( flat.Dot( right ), flat.Dot( forward ) ).RadianToDegree() );
		_body.Set( "b_grounded", _movement.IsValid() && _movement.IsOnGround );
		_body.Set( "duck", _movement.IsValid() && _movement.IsCrouching ? 1f : 0f );

		// Aim. The pawn's own eye angles are the source of truth - the same value
		// the weapon actually shoots along - so the gun cannot point somewhere
		// the shot does not go.
		var look = _player.EyeAngles.Forward;
		_body.Set( "aim_body_weight", Aiming ? 1f : 0.4f );
		_body.Set( "aim_head_weight", 1f );
		_body.SetLookDirection( "aim_eyes", look );
		_body.SetLookDirection( "aim_head", look );
		_body.SetLookDirection( "aim_body", look, Aiming ? 1f : 0.5f );
	}

	/// <summary>
	/// Is this pawn engaging something?
	///
	/// For a bot that is its brain's own target state - read, never driven. For
	/// a human it is holding the trigger. Either way the weapon comes up.
	/// </summary>
	private bool Aiming
	{
		get
		{
			// BotBrain.Target is the brain's own state, read only. The
			// presentation must never drive it.
			if ( _player.IsBot )
				return GetComponent<BotBrain>()?.Target.IsValid() ?? false;

			return _player.InputState.AttackDown || _player.InputState.AttackPressed;
		}
	}

	private void TickReloadCue( Weapon weapon )
	{
		var reloading = weapon.IsValid() && weapon.IsReloading;

		if ( reloading && !_wasReloading )
		{
			_kick += new Vector3( -2f, 0f, -ReloadDrop );
			_kickAngles += new Angles( 22f, 0f, -14f );
		}

		_wasReloading = reloading;
	}

	/// <summary>Kick the held weapon. Subscribed to the weapon's own fire event.</summary>
	private void OnFired()
	{
		_kick += new Vector3( -FireKick, 0f, 0.5f );
		_kickAngles += new Angles( -4f, 0f, 0f );
	}

	private void Decay()
	{
		var t = MathF.Min( 1f, Time.Delta * Recovery );

		_kick = Vector3.Lerp( _kick, Vector3.Zero, t );
		_kickAngles = new Angles(
			MathX.Lerp( _kickAngles.pitch, 0f, t ),
			MathX.Lerp( _kickAngles.yaw, 0f, t ),
			MathX.Lerp( _kickAngles.roll, 0f, t ) );
	}

	/// <summary>Sit the weapon on the hand attachment, wherever the animation put it.</summary>
	private void Place()
	{
		var hand = _body.GetAttachment( HoldAttachment, true );
		if ( !hand.HasValue )
		{
			Status = $"attachment '{HoldAttachment}' not found on this model";
			return;
		}

		var tx = hand.Value;

		// Position follows the hand, so the weapon moves with the body and its
		// animation. Rotation comes from the pawn's own aim instead of the hand.
		//
		// Two things were measured on this model and both came back negative:
		// every holdtype value leaves the arms in the same place, and SetIk on
		// hand_L changes nothing - 6.7u from the grip either way. So the arms
		// will not wrap the weapon, and inheriting the hand's rotation would
		// leave the gun pointing at the floor whenever the arm hangs. Aiming it
		// along the pawn's eye angles is what makes a bot look like it is
		// actually aiming at you, which is the point of this.
		var aim = global::Rotation.From( _player.EyeAngles.WithRoll( 0f ) );
		var rot = aim * global::Rotation.From( Rotation + _kickAngles );

		// Lift toward the chest when engaging, drop toward the hip when not.
		var carry = Aiming ? AimCarry : ReadyCarry;

		Current.WorldPosition = tx.Position + rot * ( Offset + carry + _kick );
		Current.WorldRotation = rot;
		Current.WorldScale = Scale;


	}

	private void Clear()
	{
		// Let the arm go, or it keeps reaching for a weapon that is gone.
		if ( CurrentWeapon.IsValid() )
			CurrentWeapon.Fired -= OnFired;

		if ( Current.IsValid() )
			Current.Destroy();

		Current = null;
		CurrentWeapon = null;
	}

	// Hold types the Citizen graph understands. Values chosen by observation -
	// see the report; they are not documented anywhere reachable from here.
	private const int NoHold = 0;
	private const int PistolHold = 1;
	private const int RifleHold = 2;

	private const int ReadyPose = 0;
	private const int AimPose = 1;

	/// <summary>Weapon carried up at the shoulder while engaging.</summary>
	private static readonly Vector3 AimCarry = new( 6f, 0f, 14f );

	/// <summary>And lowered toward the hip when not.</summary>
	private static readonly Vector3 ReadyCarry = new( 2f, 0f, 6f );
}
