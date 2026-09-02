using System;

namespace HvH;

/// <summary>
/// Plays the cosmetic side of a shot: muzzle flash, tracer, impact, sounds.
///
/// Nothing here affects gameplay. Every method is safe to call on any machine
/// and is expected to be driven by a broadcast from the host, never by a client
/// deciding for itself that something happened. <see cref="Weapon"/> resolves
/// the trace and broadcasts; it never spawns an effect itself.
///
/// The prefabs are s&amp;box's own, so there is no art to author. Note the
/// `prefabs/` prefix - the on-disk folder layout is misleading and a path that
/// resolves to nothing spawns nothing and reports nothing. See Docs/NOTES.md.
/// </summary>
public static class WeaponEffects
{
	public const string MuzzleFlashPrefab = "prefabs/effects/default_muzzleflash.prefab";
	public const string TracerPrefab = "prefabs/effects/default_tracer.prefab";

	/// <summary>
	/// Fallback impact sound, used only when a surface has no bullet sound of
	/// its own. Engine content - no asset of ours, no cloud package. The
	/// addressable path is lowercase, unlike the folder on disk.
	/// </summary>
	public const string FallbackImpactSound = "sounds/impacts/bullets/impact-bullet-concrete.sound";

	/// <summary>
	/// Reload cues. s&amp;box ships no weapon-handling audio, so these are engine
	/// bullet-impact sounds pitched to read as a magazine leaving and seating -
	/// a stand-in, and the first thing to replace when real audio arrives.
	///
	/// Deliberately NOT the melee-metal event: <see cref="HitMarker"/> uses that
	/// for a kill, and a reload that sounds like a kill confirmation is worse
	/// than a reload that sounds like nothing.
	/// </summary>
	public const string ReloadSound = "sounds/impacts/bullets/impact-bullet-metal.sound";

	private const float ReloadOutPitch = 0.8f;
	private const float ReloadInPitch = 1.25f;

	/// <summary>Reload cues played on this machine. Diagnostics only.</summary>
	public static int ReloadCues { get; private set; }

	/// <summary>How far in front of the eye effects are treated as starting.</summary>
	private const float MuzzleOffset = 40f;

	/// <summary>
	/// Play one complete shot: flash at the muzzle, a tracer along its path, and
	/// an impact where it landed.
	/// </summary>
	public static void Shot( Vector3 origin, Vector3 end, Vector3 normal, bool hit, string surfacePath )
	{
		var direction = ( end - origin ).Normal;
		var muzzle = origin + direction * MuzzleOffset;

		MuzzleFlash( origin, direction );
		Tracer( muzzle, end );

		if ( hit )
			Impact( end, normal, surfacePath );
	}

	/// <summary>
	/// Muzzle flash for a shot.
	///
	/// Placed a little in front of the eye rather than on a muzzle bone: there
	/// is no viewmodel or world weapon model yet, so no muzzle exists to attach
	/// to. This offset is the first thing to delete once a viewmodel lands.
	/// </summary>
	public static void MuzzleFlash( Vector3 eyePosition, Vector3 direction )
	{
		var rotation = Rotation.LookAt( direction );
		var position = eyePosition + direction * MuzzleOffset - rotation.Up * 6f;

		Spawn( MuzzleFlashPrefab, position, rotation, "muzzleflash" );
	}

	/// <summary>
	/// A round travelling from the muzzle to where it landed. The prefab's
	/// TracerEffect draws a moving segment between its own position and its
	/// EndPoint, so both ends have to be set.
	/// </summary>
	public static void Tracer( Vector3 from, Vector3 to )
	{
		var go = Spawn( TracerPrefab, from, Rotation.LookAt( ( to - from ).Normal ), "tracer" );
		if ( !go.IsValid() ) return;

		var tracer = go.GetComponent<TracerEffect>( true );
		if ( !tracer.IsValid() ) return;

		// Position is derived and read-only; with no Parent, local is world.
		tracer.EndPoint = new SceneAnchor { LocalPosition = to };
	}

	/// <summary>
	/// Impact effect and sound where the round landed.
	///
	/// The surface carries its own bullet impact prefab and sound, so concrete
	/// sounds like concrete and flesh sounds like flesh without any mapping of
	/// ours. The surface is resolved from the path the host sent; when it has
	/// nothing registered we fall back to a generic impact sound.
	/// </summary>
	public static void Impact( Vector3 position, Vector3 normal, string surfacePath )
	{
		var surface = ResolveSurface( surfacePath );
		var rotation = Rotation.LookAt( normal );

		var prefab = surface?.PrefabCollection.BulletImpact;
		if ( prefab.IsValid() )
			prefab.Clone( new Transform( position, rotation ), null, true, "impact" );

		var sound = surface?.SoundCollection.Bullet;
		if ( sound is not null )
		{
			PlayAt( sound, position );
			return;
		}

		PlayAt( FallbackImpactSound, position );
	}

	/// <summary>
	/// One end of a reload, at the weapon.
	///
	/// Positional, because an enemy reloading near you is information you should
	/// be able to act on - the same reason footsteps are spatialised.
	/// </summary>
	public static void Reload( Vector3 position, bool finished )
	{
		try
		{
			var handle = Sound.Play( ReloadSound, position );
			if ( handle is null ) return;

			handle.Position = position;
			handle.DistanceAttenuation = true;
			handle.OcclusionEnabled = true;
			handle.Pitch = finished ? ReloadInPitch : ReloadOutPitch;
			handle.Volume = finished ? 0.9f : 0.7f;

			ReloadCues++;
		}
		catch ( Exception )
		{
			// A missing sound must never interrupt a reload.
		}
	}

	/// <summary>Reset the reload cue count. Diagnostics only.</summary>
	public static void ResetReloadCues() => ReloadCues = 0;

	/// <summary>Play a sound at a world position, by engine asset path.</summary>
	public static void PlayAt( string soundPath, Vector3 position )
	{
		if ( string.IsNullOrWhiteSpace( soundPath ) ) return;

		try
		{
			Sound.Play( soundPath, position );
		}
		catch ( Exception )
		{
			// A missing sound must never interrupt a shot.
		}
	}

	/// <summary>Play an authored sound event, if one has been assigned.</summary>
	public static void PlayAt( SoundEvent sound, Vector3 position )
	{
		if ( sound is null ) return;

		try
		{
			Sound.Play( sound, position );
		}
		catch ( Exception )
		{
		}
	}

	private static Surface ResolveSurface( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) ) return null;

		try
		{
			return ResourceLibrary.Get<Surface>( path );
		}
		catch ( Exception )
		{
			return null;
		}
	}

	private static GameObject Spawn( string prefabPath, Vector3 position, Rotation rotation, string name )
	{
		try
		{
			// A path that resolves to nothing spawns nothing and reports nothing,
			// so say so ourselves rather than losing an afternoon to silence.
			var go = GameObject.Clone( prefabPath, new Transform( position, rotation ), null, true, name );
			if ( !go.IsValid() )
				Log.Warning( $"WeaponEffects: '{prefabPath}' resolved to nothing - check the addressable path." );

			return go;
		}
		catch ( Exception e )
		{
			Log.Warning( $"WeaponEffects: couldn't spawn '{prefabPath}': {e.Message}" );
			return null;
		}
	}
}
