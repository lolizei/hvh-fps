using System;

namespace HvH;

/// <summary>
/// A single firearm. Behaviour comes entirely from <see cref="WeaponData"/>, so
/// adding a gun means adding data, not code.
///
/// Split of authority: the shooting client decides where it is aiming and
/// applies its own recoil and spread; the host owns ammo, the fire rate gate,
/// the trace and the damage. A client cannot invent hits or ammunition.
/// </summary>
public sealed class Weapon : Component
{
	/// <summary>Authored weapon asset. Takes priority over <see cref="BuiltInId"/>.</summary>
	[Property] public WeaponData Data { get; set; }

	/// <summary>Id from <see cref="WeaponDefinitions"/>, used when no asset is set.</summary>
	[Property] public string BuiltInId { get; set; } = WeaponDefinitions.Rifle;

	[Sync( Flags = SyncFlags.FromHost )] public int Ammo { get; set; } = -1;
	[Sync( Flags = SyncFlags.FromHost )] public int Reserve { get; set; } = -1;
	[Sync( Flags = SyncFlags.FromHost )] public bool IsReloading { get; set; }

	/// <summary>
	/// The mod framework's hook into ballistics. Raised on every shot, on both
	/// the firing client and the host, so a mod can change damage, spread or
	/// recoil for that shot without touching the underlying asset.
	/// </summary>
	public static event Action<Weapon, WeaponStats> StatsModifier;

	/// <summary>Raised locally when this weapon actually fires. Drives effects and the HUD.</summary>
	public event Action Fired;

	/// <summary>
	/// Gunshot sound. Left unassigned by default: s&amp;box ships impact and
	/// footstep sounds but NO gunshot, so there is nothing correct to point this
	/// at without adding an asset or a package reference. Assign one and every
	/// shot - yours and the bots' - becomes audible.
	/// </summary>
	[Property] public SoundEvent FireSound { get; set; }

	public WeaponData Resolved => Data ?? WeaponDefinitions.Get( BuiltInId );

	public string DisplayName => Resolved?.DisplayName ?? "Unknown";
	public WeaponSlot Slot => Resolved?.Slot ?? WeaponSlot.Primary;

	public Player Owner { get; private set; }

	private float _nextFire;
	private float _reloadFinishTime;
	private bool _wasReloading;

	protected override void OnAwake() => EnsureSetup();

	// Weapons sit disabled in the inventory until selected, and a component that
	// starts disabled may not get OnAwake - so setup has to be safe to call
	// twice and must also run the moment we are switched to.
	protected override void OnEnabled() => EnsureSetup();

	private void EnsureSetup()
	{
		Owner ??= GetComponentInParent<Player>();

		// -1 means "never initialised", so a weapon dropped into a scene by hand
		// still fills its magazine from the data.
		var data = Resolved;
		if ( data is null || !Networking.IsHost ) return;

		if ( Ammo < 0 ) Ammo = data.MagazineSize;
		if ( Reserve < 0 ) Reserve = data.ReserveAmmo;
	}

	/// <summary>
	/// Host-side: put this weapon back to a full magazine and full reserve.
	/// Called when a player respawns - without it you come back from a round
	/// holding whatever empty gun you died with.
	/// </summary>
	public void RestoreAmmo()
	{
		if ( !Networking.IsHost ) return;

		var data = Resolved;
		if ( data is null ) return;

		Ammo = data.MagazineSize;
		Reserve = data.ReserveAmmo;
		IsReloading = false;
		_wasReloading = false;
		_reloadFinishTime = 0f;
		_nextFire = 0f;
	}

	/// <summary>Build this shot's numbers: base data, then whatever the mods want.</summary>
	public WeaponStats BuildStats()
	{
		var data = Resolved;
		if ( data is null ) return null;

		var stats = WeaponStats.From( data );
		StatsModifier?.Invoke( this, stats );

		return stats;
	}

	protected override void OnUpdate()
	{
		// Reload audio runs on EVERY machine, above the simulation gate. Hearing
		// someone else reload is information you play on, and IsReloading is
		// synced, so both transitions arrive everywhere with no extra RPC.
		TickReloadAudio();

		// Simulated by whoever owns the pawn - the local human, or the host for
		// its bots. Intent comes from the pawn, never from the keyboard, so a
		// bot cannot fire on the human's mouse clicks.
		if ( !Owner.IsValid() || !Owner.IsSimulatedHere ) return;

		var data = Resolved;
		if ( data is null ) return;

		TickReload();

		var input = Owner.InputState;

		if ( input.ReloadPressed )
			RequestReload();

		var wantsFire = data.Automatic ? input.AttackDown : input.AttackPressed;

		if ( !wantsFire ) return;
		if ( !CanFire() ) return;

		Fire();
	}

	/// <summary>
	/// Turn the synced reloading flag into two audible cues: one as the magazine
	/// comes out, one as it seats.
	/// </summary>
	private void TickReloadAudio()
	{
		if ( IsReloading == _wasReloading ) return;

		_wasReloading = IsReloading;
		WeaponEffects.Reload( WorldPosition, finished: !IsReloading );
	}

	private void TickReload()
	{
		if ( !IsReloading ) return;
		if ( Time.Now < _reloadFinishTime ) return;

		FinishReload();
	}

	public bool CanFire()
	{
		if ( !Owner.IsValid() || !Owner.IsAlive ) return false;
		if ( !( RoundManager.Current?.AllowShooting ?? true ) ) return false;
		if ( IsReloading ) return false;
		if ( Ammo <= 0 ) return false;
		if ( Time.Now < _nextFire ) return false;

		return true;
	}

	/// <summary>
	/// Fire one shot through the normal path - spread, recoil, the host-side
	/// trace and damage all included. Used by dev commands so the real weapon
	/// chain can be exercised without a human holding the mouse button.
	/// </summary>
	public void FireOnce()
	{
		if ( !CanFire() ) return;

		Fire();
	}

	private void Fire()
	{
		var stats = BuildStats();
		if ( stats is null ) return;

		_nextFire = Time.Now + stats.FireDelay;

		var ray = Owner.AimRay;
		var direction = ApplySpread( ray.Forward, stats );

		// Recoil kicks our own view. We own EyeAngles, so this is a local write -
		// and it is exactly the value a recoil-control mod cancels out.
		ApplyRecoil( stats );

		Fired?.Invoke();

		RequestFire( direction );
	}

	/// <summary>
	/// Scatter the shot inside the accuracy cone. Note this runs on the firing
	/// client by design: in an HVH game, manipulating your own spread is a
	/// feature exposed through the mod menu, not an exploit to defend against.
	/// </summary>
	private Vector3 ApplySpread( Vector3 forward, WeaponStats stats )
	{
		var spread = stats.Spread;

		var movement = Owner.Movement;
		if ( movement.IsValid() )
		{
			// Scale the movement penalty by how fast we are actually going.
			var speed = movement.Velocity.WithZ( 0f ).Length;
			var fraction = MathF.Min( 1f, speed / MathF.Max( 1f, movement.RunSpeed ) );
			spread += stats.MovementInaccuracy * fraction;

			if ( !movement.IsOnGround )
				spread += stats.MovementInaccuracy;
		}

		if ( spread <= 0f ) return forward;

		// Scatter inside a cone of `spread` degrees, area-weighted so shots are
		// not bunched in the middle.
		var angle = Random.Shared.Float( 0f, 360f );
		var radius = MathF.Tan( spread.DegreeToRadian() ) * MathF.Sqrt( Random.Shared.Float( 0f, 1f ) );

		var rotation = Rotation.LookAt( forward );
		var offset = rotation.Right * MathF.Cos( angle.DegreeToRadian() ) * radius
		           + rotation.Up * MathF.Sin( angle.DegreeToRadian() ) * radius;

		return ( forward + offset ).Normal;
	}

	private void ApplyRecoil( WeaponStats stats )
	{
		if ( stats.Recoil <= 0f ) return;

		var angles = Owner.EyeAngles;
		angles.pitch = Math.Clamp( angles.pitch - stats.Recoil, -89f, 89f );
		angles.yaw += Random.Shared.Float( -stats.Recoil, stats.Recoil ) * 0.25f;

		Owner.EyeAngles = angles;
	}

	/// <summary>
	/// Host-side shot resolution. The client supplies a direction; everything
	/// that matters - ammo, the trace, the damage - is decided here.
	/// </summary>
	// ---- diagnostics -------------------------------------------------------
	// Counts along one shot's path, so "two markers" can be answered with
	// numbers instead of a theory. Read and reset by hvh_hitdebug.
	public static int FireRequests;
	public static int DamageApplications;
	public static int ConfirmHitInvocations;
	public static int ConfirmHitDeliveries;

	public static void ResetCounters()
	{
		FireRequests = 0;
		DamageApplications = 0;
		ConfirmHitInvocations = 0;
		ConfirmHitDeliveries = 0;
		HitMarker.ShowCount = 0;
	}

	[Rpc.Host]
	private void RequestFire( Vector3 direction )
	{
		var data = Resolved;
		if ( data is null ) return;
		if ( Ammo <= 0 || IsReloading ) return;
		if ( !Owner.IsValid() || !Owner.IsAlive ) return;
		if ( !( RoundManager.Current?.AllowShooting ?? true ) ) return;

		FireRequests++;

		Ammo--;

		var stats = BuildStats();
		var origin = Owner.AimRay.Position;

		var trace = Scene.Trace
			.Ray( origin, origin + direction.Normal * stats.Range )
			.IgnoreGameObjectHierarchy( Owner.GameObject )
			.UseHitboxes()
			.Run();

		// Cosmetic only, and broadcast for every shot including misses - a client
		// never decides on its own that a shot happened.
		// The surface travels as its resource path so every machine can resolve
		// the engine's own impact prefab and sound for it.
		BroadcastShot( origin, trace.EndPosition, trace.Normal, trace.Hit,
			trace.Surface?.ResourcePath ?? "" );

		if ( !trace.Hit || !trace.GameObject.IsValid() ) return;

		var health = trace.GameObject.GetComponentInParent<HealthComponent>();
		if ( !health.IsValid() ) return;

		var zone = ClassifyHit( trace, health );
		var damage = stats.Damage * stats.MultiplierFor( zone );

		DamageApplications++;

		health.ApplyDamage( new DamageInfo
		{
			Damage = damage,
			Attacker = Owner.GameObject,
			Weapon = GameObject,
			Position = trace.EndPosition,
			Origin = origin,
		}, zone );

		// Confirm the hit to the shooter alone. Separate from the world-effect
		// broadcast on purpose: that is something everyone sees, this is one
		// person's feedback. Anything with health marks, dummies included - an
		// empty server is mostly dummy-shooting and silence there reads as broken.
		ConfirmHitInvocations++;
		ConfirmHit( !health.IsAlive, zone == HitZone.Head );
	}

	/// <summary>
	/// Play one shot's cosmetic effects on every machine. Carries only what the
	/// effects need - two points and a flag - so the per-shot traffic stays tiny
	/// even at 600 rounds per minute.
	/// </summary>
	[Rpc.Broadcast]
	private void BroadcastShot( Vector3 origin, Vector3 end, Vector3 normal, bool hit, string surfacePath )
	{
		WeaponEffects.Shot( origin, end, normal, hit, surfacePath );
		WeaponEffects.PlayAt( FireSound, origin );
	}

	/// <summary>
	/// Tell the shooter, and only the shooter, that they landed a hit.
	///
	/// [Rpc.Owner] goes to the connection that owns this weapon. A bot's weapon
	/// is host-owned, so for a bot this executes on the host - which would flash
	/// a marker on the host's screen for the bot's hits. The IsLocallyControlled
	/// check is what stops that.
	/// </summary>
	[Rpc.Owner]
	private void ConfirmHit( bool killed, bool headshot )
	{
		ConfirmHitDeliveries++;

		if ( !Owner.IsValid() || !Owner.IsLocallyControlled ) return;

		HitMarker.Show( killed ? HitKind.Kill : headshot ? HitKind.Headshot : HitKind.Body );
	}

	/// <summary>
	/// Work out which part of the target was hit. Prefers real model hitbox
	/// tags; falls back to height on the target, which is what the current
	/// primitive-box players need.
	/// </summary>
	private static HitZone ClassifyHit( SceneTraceResult trace, HealthComponent target )
	{
		var tags = trace.Hitbox?.Tags;
		if ( tags is not null )
		{
			if ( tags.Has( "head" ) ) return HitZone.Head;
			if ( tags.Has( "arm" ) || tags.Has( "leg" ) || tags.Has( "foot" ) || tags.Has( "hand" ) )
				return HitZone.Limb;
			if ( tags.Has( "chest" ) || tags.Has( "stomach" ) || tags.Has( "pelvis" ) )
				return HitZone.Body;
		}

		// Measure against the target's real bounds, not its origin. Deriving the
		// feet from the origin assumed every target is anchored at its feet -
		// true for a Player, false for a TargetDummy, which is anchored at its
		// middle. That put the head zone above the dummy's own head, so a dummy
		// could not be headshot at all and everything below its waist read as a
		// limb. Bounds make one rule correct for both.
		var bounds = target.GameObject.GetBounds();
		var floor = bounds.Mins.z;
		var height = bounds.Size.z;

		// No renderer or collider yet - fall back rather than divide by nothing.
		if ( height < 1f )
		{
			floor = target.WorldPosition.z;
			height = target.GetComponentInParent<PlayerMovement>()?.StandHeight ?? 72f;
		}

		var fraction = ( trace.EndPosition.z - floor ) / MathF.Max( 1f, height );

		if ( fraction >= 0.88f ) return HitZone.Head;
		if ( fraction <= 0.35f ) return HitZone.Limb;

		return HitZone.Body;
	}

	public void RequestReload()
	{
		var data = Resolved;
		if ( data is null || IsReloading ) return;
		if ( Ammo >= data.MagazineSize ) return;

		_reloadFinishTime = Time.Now + data.ReloadTime;
		StartReload();
	}

	[Rpc.Host]
	private void StartReload()
	{
		var data = Resolved;
		if ( data is null || IsReloading ) return;
		if ( Reserve <= 0 || Ammo >= data.MagazineSize ) return;

		IsReloading = true;
		_reloadFinishTime = Time.Now + data.ReloadTime;
	}

	private void FinishReload()
	{
		IsReloading = false;
		if ( !Networking.IsHost ) return;

		var data = Resolved;
		if ( data is null ) return;

		var wanted = Math.Min( data.MagazineSize - Ammo, Reserve );
		Ammo += wanted;
		Reserve -= wanted;
	}
}
