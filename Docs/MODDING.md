# Writing a mod

Everything in the HVH layer is a mod, including the one that ships with the
game. There is no privileged path — `DefaultHvhMod` is discovered, initialised
and ticked exactly like yours, and you can replace it entirely.

All of this is a mechanic of *this* game. It reads and writes this game's own
scene objects and nothing else.

## The shortest possible mod

```csharp
using HvH.Mods;

public sealed class MyMod : ModBase
{
    public override string Id     => "my-mod";     // used for config paths
    public override string Name   => "My Mod";
    public override string Author => "You";

    protected override void OnInitialize()
    {
        Register( new MyFeature() );
    }
}

public sealed class MyFeature : ModFeature
{
    public override string Name     => "Do The Thing";
    public override string Category => "Combat";

    protected override void OnTick()
    {
        if ( !CanAct ) return;
        // runs every frame while enabled
    }
}
```

That is the whole integration. `ModManager` finds `MyMod` by reflection at
scene start — there is no registry to edit and no scene object to place.

## The contracts

| Interface | Purpose |
|---|---|
| `IGameMod` | A mod: id, name, features, config, optional menu |
| `IModFeature` | One switchable capability |
| `IModMenu` | Show/hide a UI, plus its toggle key |
| `IModConfig` | Named settings profiles, saved to disk |

`ModBase` and `ModFeature` are conveniences that implement the boring parts.
Implementing the interfaces directly is fully supported.

## Settings that any menu can draw

A feature describes its options with `ModSetting`. This is the part that makes
"write your own menu" true rather than aspirational: a menu walks
`feature.Settings` and renders controls without knowing what the feature is.

```csharp
protected override IEnumerable<ModSetting> BuildSettings()
{
    yield return ModSetting.Slider( "Strength", () => Strength, v => Strength = v, 0f, 1f, 0.05f );
    yield return ModSetting.Toggle( "Visible Only", () => Visible, v => Visible = v );
    yield return ModSetting.Choice( "Bone", () => (int)Bone, v => Bone = (TargetBone)v,
        "Head", "Chest", "Pelvis" );
}
```

Values you expose through `Setting<T>` / `SetSetting<T>` are persisted in the
mod's own config automatically, as is each feature's enabled flag.

## Your own menu

Implement `IModMenu`, or use the Razor helper:

```csharp
public sealed class MyMenu : RazorModMenu<MyMenuPanel>
{
    public MyMenu( IGameMod mod, ModManager manager )
        : base( manager, "AltModMenu", "My Mod" ) => _mod = mod;

    protected override void Configure( MyMenuPanel panel ) => panel.Mod = _mod;
}
```

`RazorModMenu<T>` creates a `ScreenPanel` + your panel on open and destroys it
on close. If you would rather draw with `camera.Hud`, or build the whole thing
out of world-space panels, implement `IModMenu` yourself — the framework only
ever calls `Open()` and `Close()`.

The menu's `ToggleKey` is an input action name. `ModMenu` (Insert) and
`AltModMenu` (Delete) are defined; add more in `ProjectSettings/Input.config`.
Opening one menu closes the others.

See `Code/Examples/ExampleCustomMod.cs` and `Code/UI/ExampleModMenu.razor` for a
complete third-party mod that does not use the default menu.

## The event bus

Hook game events through `Context.Events` rather than reaching into gameplay
components, so your mod keeps working when the game's internals change.

| Event | Fires |
|---|---|
| `RoundStateChanged` | Round phase changes |
| `Kill` | Anyone dies |
| `RoundOver` | A round is decided |
| `WeaponStats` | A shot is being built — **mutate to change ballistics** |
| `LocalPlayerChanged` | Local pawn spawned or respawned |
| `Frame` | Every frame |

`WeaponStats` is the ballistics seam. Each shot copies the weapon asset into a
fresh `WeaponStats`, raises this, and uses the result — so you change one shot,
never the underlying asset:

```csharp
public override void Enable()  => Context.Events.WeaponStats += OnStats;
public override void Disable() => Context.Events.WeaponStats -= OnStats;

void OnStats( Weapon weapon, WeaponStats stats )
{
    if ( !weapon.IsValid() || weapon.IsProxy ) return; // ours only
    stats.Recoil = 0f;
}
```

## Targeting

`TargetSelector` is public API, not built-in-mod internals, so your mod gets the
same targeting the shipped one uses:

```csharp
var target = TargetSelector.Find( Context, fovLimit: 25f,
    bone: TargetBone.Head, mode: TargetMode.Crosshair, requireVisible: true );

if ( target.IsValid )
    Log.Info( $"{target.Player} at {target.Distance} units, {target.Fov} degrees off" );
```

`ModContext.AliveEnemies` gives you the filtered enemy list directly.

## Config profiles

Each mod gets its own folder: `mods/<mod-id>/<profile>.json`.

```csharp
Config.Set( "MyKey", 0.5f );
var value = Config.Get( "MyKey", 0.5f );

Config.Save( "rage" );      // write mods/my-mod/rage.json
Config.Load( "legit" );     // swap profiles at runtime
Config.ListProfiles();      // what exists on disk
```

Reading a key that is missing or has changed type returns your fallback rather
than throwing, so an old profile never breaks a newer mod.

## What the framework guarantees

- A mod that throws in `Initialize` is skipped; the rest still load.
- A feature that throws in `Tick` is disabled and logged, not left to spam.
- Duplicate mod ids are rejected — the second one is ignored.
- `ModManager.BlockedMods` disables a mod by id without deleting it.
- Turning a mod off preserves which of its features were on.

## Scope

These systems are gameplay mechanics of this project. They act only on this
game's scene, through its own components. Nothing here touches other processes,
other games, or anything outside this application, and nothing in this
framework should be extended in that direction.
