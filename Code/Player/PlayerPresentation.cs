using System;
using System.Collections.Generic;

namespace HvH;

/// <summary>
/// What a pawn looks like to everyone else.
///
/// Presentation only. It reads the pawn's synced team and shows the matching
/// body; it never decides anything. Gameplay does not branch on which model is
/// showing, and the hit zones are unchanged - the models are built around the
/// existing 72-unit collider rather than replacing it.
///
/// The body is cloned locally on every machine rather than networked. Team is
/// already synced, so each machine can work out what to draw on its own and the
/// wire carries nothing extra for it.
/// </summary>
public sealed class PlayerPresentation : Component
{
	[Property] public string VanguardModel { get; set; } = "prefabs/players/player_vanguard.prefab";
	[Property] public string SyndicateModel { get; set; } = "prefabs/players/player_syndicate.prefab";

	/// <summary>The placeholder box body, hidden once a real model is up.</summary>
	[Property] public ModelRenderer PlaceholderBody { get; set; }

	/// <summary>The live body, for diagnostics.</summary>
	public GameObject Current { get; private set; }

	/// <summary>Which team the current body is dressed for.</summary>
	public Team CurrentTeam { get; private set; } = Team.None;

	private Player _player;
	private readonly List<ModelRenderer> _renderers = new();

	protected override void OnAwake()
	{
		_player = GetComponent<Player>();

		if ( !PlaceholderBody.IsValid() )
			PlaceholderBody = GetComponentInChildren<ModelRenderer>( true );
	}

	protected override void OnDestroy() => Clear();

	protected override void OnUpdate()
	{
		var team = _player.IsValid() ? _player.Team : Team.None;

		if ( team != CurrentTeam || !Current.IsValid() )
			Rebuild( team );
	}

	private void Rebuild( Team team )
	{
		Clear();
		CurrentTeam = team;

		var path = team switch
		{
			Team.Syndicate => SyndicateModel,
			_ => VanguardModel,
		};

		if ( string.IsNullOrWhiteSpace( path ) ) return;

		try
		{
			Current = GameObject.Clone( path, global::Transform.Zero, GameObject, true, "body" );
		}
		catch ( Exception e )
		{
			Log.Warning( $"PlayerPresentation: couldn't load '{path}': {e.Message}" );
			return;
		}

		if ( !Current.IsValid() )
		{
			Log.Warning( $"PlayerPresentation: '{path}' resolved to nothing - check the path." );
			return;
		}

		// The placeholder box would sit inside the new body.
		if ( PlaceholderBody.IsValid() )
			PlaceholderBody.RenderType = ModelRenderer.ShadowRenderType.Off;

		_renderers.Clear();
		_renderers.AddRange( Current.GetComponentsInChildren<ModelRenderer>( true ) );

		// We are inside our own body in first person. Keep the shadow so you can
		// still see yourself on the floor, but do not render the body itself.
		if ( _player.IsValid() && _player.IsLocallyControlled )
		{
			foreach ( var r in _renderers )
				r.RenderType = ModelRenderer.ShadowRenderType.ShadowsOnly;
		}
	}

	private void Clear()
	{
		if ( Current.IsValid() )
			Current.Destroy();

		Current = null;
		_renderers.Clear();
		CurrentTeam = Team.None;
	}
}
