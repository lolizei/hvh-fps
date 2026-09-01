using System;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HvH.Mods;

/// <summary>
/// File-backed <see cref="IModConfig"/>. Profiles live at
/// <c>mods/&lt;modId&gt;/&lt;profile&gt;.json</c> in the game's data folder, so
/// a player can keep "default", "legit" and "rage" side by side and swap them.
///
/// Values are held as JSON nodes rather than boxed objects so that reloading a
/// profile written by an older version of a mod doesn't throw - a missing or
/// mistyped key just falls back.
/// </summary>
public sealed class ModConfig : IModConfig
{
	public const string RootFolder = "mods";
	public const string DefaultProfile = "default";

	private readonly string _modId;
	private Dictionary<string, JsonNode> _values = new();

	public string Profile { get; private set; } = DefaultProfile;

	public ModConfig( string modId )
	{
		_modId = SanitiseName( modId );
	}

	public bool Has( string key ) => _values.ContainsKey( key );

	public T Get<T>( string key, T fallback = default )
	{
		if ( !_values.TryGetValue( key, out var node ) || node is null )
			return fallback;

		try
		{
			return node.Deserialize<T>() ?? fallback;
		}
		catch ( Exception )
		{
			// Type changed between versions - fall back rather than blow up.
			return fallback;
		}
	}

	public void Set<T>( string key, T value )
	{
		try
		{
			_values[key] = JsonSerializer.SerializeToNode( value );
		}
		catch ( Exception e )
		{
			Log.Warning( $"Mod config couldn't store '{key}': {e.Message}" );
		}
	}

	public void Save( string profile = null )
	{
		Profile = SanitiseName( profile ?? Profile );

		try
		{
			FileSystem.Data.CreateDirectory( FolderPath );
			FileSystem.Data.WriteJson( PathFor( Profile ), _values );
		}
		catch ( Exception e )
		{
			Log.Warning( $"Couldn't save mod config '{_modId}/{Profile}': {e.Message}" );
		}
	}

	public void Load( string profile )
	{
		Profile = SanitiseName( profile );

		try
		{
			_values = FileSystem.Data.ReadJson( PathFor( Profile ), new Dictionary<string, JsonNode>() )
				?? new Dictionary<string, JsonNode>();
		}
		catch ( Exception )
		{
			// A missing or corrupt profile means "start from defaults".
			_values = new Dictionary<string, JsonNode>();
		}
	}

	public IEnumerable<string> ListProfiles()
	{
		if ( !FileSystem.Data.DirectoryExists( FolderPath ) )
			return new[] { DefaultProfile };

		try
		{
			return FileSystem.Data
				.FindFile( FolderPath, "*.json" )
				.Select( System.IO.Path.GetFileNameWithoutExtension )
				.ToArray();
		}
		catch ( Exception )
		{
			return new[] { DefaultProfile };
		}
	}

	private string FolderPath => $"{RootFolder}/{_modId}";
	private string PathFor( string profile ) => $"{FolderPath}/{profile}.json";

	/// <summary>Keep ids and profile names safe to use as paths.</summary>
	private static string SanitiseName( string name )
	{
		if ( string.IsNullOrWhiteSpace( name ) ) return DefaultProfile;

		var cleaned = new string( name
			.Where( c => char.IsLetterOrDigit( c ) || c is '_' or '-' )
			.ToArray() );

		return string.IsNullOrEmpty( cleaned ) ? DefaultProfile : cleaned.ToLowerInvariant();
	}
}
