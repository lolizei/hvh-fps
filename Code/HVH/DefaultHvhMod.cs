using System;
using HvH.Mods;
using HvH.UI;

namespace HvH.HVH;

/// <summary>
/// The game's built-in HVH mod.
///
/// This is deliberately an ordinary <see cref="IGameMod"/> with no special
/// privileges: <see cref="ModManager"/> discovers it by reflection exactly like
/// a third-party mod, it registers features through the same Register call, and
/// its menu is just an IModMenu. Delete this class and the framework carries on
/// happily with whatever other mods are present.
///
/// Everything here is a mechanic of this game, operating on this game's own
/// scene objects.
/// </summary>
public sealed class DefaultHvhMod : ModBase
{
	public override string Id => "hvh-default";
	public override string Name => "HVH Core";
	public override string Author => "HVH Team";
	public override string Version => "1.0.0";

	private IModMenu _menu;

	public override IModMenu Menu => _menu;

	protected override void OnInitialize()
	{
		// Combat
		Register( new AimAssistFeature() );
		Register( new RecoilControlFeature() );
		Register( new SpreadControlFeature() );

		// Visual
		Register( new EspFeature() );

		// Movement
		Register( new BunnyHopFeature() );
		Register( new StrafeAssistFeature() );

		// Misc
		Register( new ThirdPersonFeature() );
		Register( new CustomFovFeature() );

		_menu = new DefaultHvhMenu( this, Context.Manager );
	}
}

/// <summary>
/// Binds the default menu panel to this mod. Subclassing
/// <see cref="RazorModMenu{TPanel}"/> is only needed to hand the panel a
/// reference to the mod it is drawing.
/// </summary>
public sealed class DefaultHvhMenu : RazorModMenu<DefaultModMenu>
{
	private readonly IGameMod _mod;

	public DefaultHvhMenu( IGameMod mod, ModManager manager )
		: base( manager, "ModMenu", "HVH Core" )
	{
		_mod = mod;
	}

	protected override void Configure( DefaultModMenu panel )
	{
		panel.Mod = _mod;
	}
}
