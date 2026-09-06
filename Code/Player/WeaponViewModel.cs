using System;

namespace HvH;

/// <summary>
/// The gun you actually see in first person.
///
/// Presentation only, and deliberately separate from <see cref="Weapon"/>:
/// gameplay owns damage, ammo, fire rate and reload, this owns a model and
/// where it sits. Nothing here may change a gameplay value, and gameplay never
/// asks which weapon it is - the model comes from
/// <see cref="WeaponData.ViewModelPath"/>, so swapping art is a data change.
///
/// Client-side and local-only. The model is cloned, never network-spawned, and
/// carries no colliders, so it is never a world physics object.
/// </summary>
public sealed class WeaponViewModel : Component
{
	/// <summary>Rest position relative to the camera: forward, left, up.</summary>
	[Property] public Vector3 Offset { get; set; } = new( 34f, -11f, -10f );

	/// <summary>
	/// View models are drawn at world scale but very close to the eye, so a
	/// 22-unit rifle fills the screen. Real shooters solve this with a separate
	/// narrow FOV for the weapon; scaling it down is the cheap equivalent and
	/// keeps it out of the way of the crosshair.
	/// </summary>
	[Property] public float Scale { get; set; } = 0.55f;

	[Property] public float FireKick { get; set; } = 2.2f;
	[Property] public float FireRise { get; set; } = 1.1f;
	[Property] public float ReloadDip { get; set; } = 7f;
	[Property] public float EquipDip { get; set; } = 9f;

	/// <summary>How fast the gun returns to rest. Higher is snappier.</summary>
	[Property] public float Recovery { get; set; } = 9f;

	/// <summary>Sway from moving, in units per unit of speed.</summary>
	[Property] public float SwayScale { get; set; } = 0.012f;

	/// <summary>The live view model, for diagnostics.</summary>
	public GameObject Current { get; private set; }

	/// <summary>Which weapon the current model belongs to.</summary>
	public Weapon CurrentWeapon { get; private set; }

	/// <summary>Muzzle point in world space, or null when nothing is equipped.</summary>
	public Vector3? MuzzleWorld
	{
		get
		{
			if ( !Current.IsValid() || CurrentWeapon?.Resolved is null ) return null;

			return Current.WorldTransform.PointToWorld( CurrentWeapon.Resolved.MuzzleOffset );
		}
	}

	/// <summary>Why there is no view model right now. Diagnostics only.</summary>
	public string Status { get; private set; } = "not ticked";

	private Player _player;
	private PlayerCamera _camera;
	private PlayerMovement _movement;

	// Animation state, all in the camera's local space.
	private Vector3 _positionOffset;
	private Angles _angleOffset;
	private bool _wasReloading;

	protected override void OnAwake()
	{
		_player = GetComponent<Player>();
		_camera = GetComponent<PlayerCamera>();
		_movement = GetComponent<PlayerMovement>();
	}

	protected override void OnDestroy() => Clear();

	protected override void OnUpdate()
	{
		// Only the pawn this machine is actually playing has a first-person
		// view at all. A bot, or somebody else's pawn, must never spawn one.
		if ( !_player.IsValid() || !_player.IsLocallyControlled )
		{
			Status = "not locally controlled";
			Clear();
			return;
		}

		var weapon = _player.Inventory?.ActiveWeapon;
		if ( weapon != CurrentWeapon )
			Equip( weapon );

		if ( !Current.IsValid() ) return;

		TickReloadCue( weapon );
		Decay();
		Place();
	}

	/// <summary>Swap the model. Called when the active weapon changes.</summary>
	private void Equip( Weapon weapon )
	{
		Clear();
		CurrentWeapon = weapon;

		// Subscribe rather than have Weapon call us: gameplay should not know a
		// view model exists. Weapon already raises Fired for exactly this.
		if ( weapon.IsValid() )
			weapon.Fired += OnFired;

		if ( !weapon.IsValid() )
		{
			Status = "no active weapon";
			return;
		}

		var path = weapon.Resolved?.ViewModelPath;
		if ( string.IsNullOrWhiteSpace( path ) )
		{
			Status = $"'{weapon.DisplayName}' has no ViewModelPath";
			return;
		}

		var camera = _camera?.Camera;
		if ( !camera.IsValid() )
		{
			Status = "no camera to parent to";
			return;
		}

		try
		{
			// A local clone, not a NetworkSpawn: this exists on one screen only.
			Current = GameObject.Clone( path, global::Transform.Zero, camera.GameObject,
				true, "viewmodel" );
		}
		catch ( Exception e )
		{
			Log.Warning( $"WeaponViewModel: couldn't load '{path}': {e.Message}" );
			return;
		}

		if ( !Current.IsValid() )
		{
			// A bad addressable path spawns nothing and reports nothing, so say so.
			Log.Warning( $"WeaponViewModel: '{path}' resolved to nothing - check the path." );
			return;
		}

		Status = $"showing {path}";

		// Come up into frame rather than appearing fully formed.
		_positionOffset = _positionOffset.WithZ( _positionOffset.z - EquipDip );
		_angleOffset += new Angles( -14f, 6f, 0f );
	}

	/// <summary>Dip the gun while reloading, driven off the synced flag.</summary>
	private void TickReloadCue( Weapon weapon )
	{
		var reloading = weapon.IsValid() && weapon.IsReloading;

		if ( reloading && !_wasReloading )
		{
			_positionOffset = _positionOffset.WithZ( _positionOffset.z - ReloadDip );
			_angleOffset += new Angles( 18f, -8f, 0f );
		}

		_wasReloading = reloading;
	}

	/// <summary>Kick the gun. Called by the weapon when a shot leaves.</summary>
	public void OnFired()
	{
		_positionOffset += new Vector3( -FireKick, 0f, FireRise * 0.35f );
		_angleOffset += new Angles( -FireRise * 2.4f, Game.Random.Float( -0.8f, 0.8f ), 0f );
	}

	/// <summary>Everything eases back to rest; that easing IS the animation.</summary>
	private void Decay()
	{
		var t = MathF.Min( 1f, Time.Delta * Recovery );

		_positionOffset = Vector3.Lerp( _positionOffset, Vector3.Zero, t );
		_angleOffset = new Angles(
			MathX.Lerp( _angleOffset.pitch, 0f, t ),
			MathX.Lerp( _angleOffset.yaw, 0f, t ),
			MathX.Lerp( _angleOffset.roll, 0f, t ) );
	}

	private void Place()
	{
		var camera = _camera?.Camera;
		if ( !camera.IsValid() ) return;

		// Walking sway, so the gun is not welded rigidly to the view.
		var speed = _movement.IsValid() ? _movement.Velocity.WithZ( 0f ).Length : 0f;
		var bob = MathF.Sin( Time.Now * 9f ) * speed * SwayScale;
		var sway = new Vector3( 0f, bob * 0.5f, MathF.Abs( bob ) * 0.4f );

		Current.LocalPosition = Offset + _positionOffset + sway;
		Current.LocalRotation = Rotation.From( _angleOffset );
		Current.LocalScale = Scale;
	}

	private void Clear()
	{
		if ( CurrentWeapon.IsValid() )
			CurrentWeapon.Fired -= OnFired;

		if ( Current.IsValid() )
			Current.Destroy();

		Current = null;
		CurrentWeapon = null;
	}
}
