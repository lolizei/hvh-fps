# s&box engine notes

Things that cost us real time. Each one compiled fine, produced no error, and
behaved wrongly. Kept here rather than in `GOAL.md` because goals get archived
and these do not expire.

---

## Asset paths need the `prefabs/` prefix, and a wrong path fails silently

This is the one that burns the most time, because **nothing tells you it went
wrong**. `GameObject.Clone( path, ... )` with a path that resolves to nothing
throws no exception, logs no warning, and returns without spawning anything.
You get zero objects and a clean console.

```csharp
// WRONG - silently spawns nothing, no error of any kind
GameObject.Clone( "effects/default_muzzleflash.prefab", ... );

// RIGHT
GameObject.Clone( "prefabs/effects/default_muzzleflash.prefab", ... );
```

The on-disk layout is misleading: the file lives at
`addons/base/Assets/prefabs/effects/default_muzzleflash.prefab`, and prefab
references *inside* base content are written relative to a different root, so
copying a path you saw in another prefab's JSON gives you the wrong one.

**Always confirm the addressable path before using it.** In the editor, search
the asset browser. Over the editor's MCP API:

```
asset_search { "query": "muzzleflash" }
  -> { "Path": "prefabs/effects/default_muzzleflash.prefab", "Type": "Prefab" }
```

That is the authoritative answer. Two minutes there beats an hour wondering why
an effect never appears.

---

## `download/assets/…` is other people's cloud cache, not base content

Searching the whole s&box install for an asset will turn up hundreds of hits
under `download/assets/`. **Those are cloud packages other projects pulled
down.** They are not part of s&box, will not be on a teammate's machine, and
depending on one means adding a `PackageReference` to the project.

What you can actually rely on:

| Location | Contains |
|---|---|
| `core/` | 763 sounds — 294 footsteps, 123 impacts, UI clicks, ambience. No weapons. |
| `addons/base/Assets/` | prefabs, models, materials, fonts. **Zero sound files.** |
| `download/assets/` | **not yours** — cloud cache |

Concretely: there is **no gunshot sound in s&box**. Useful things that *are*
guaranteed present:

```
sounds/Impacts/Bullets/impact-bullet-flesh
sounds/Impacts/Bullets/impact-bullet-concrete
sounds/footsteps/footstep-concrete
prefabs/effects/default_muzzleflash.prefab
prefabs/effects/default_tracer.prefab
prefabs/effects/default_brasseject.prefab
```

---

## `[Sync]` writes before `NetworkSpawn()` are discarded

Set networked values **after** spawning. Writing them to a not-yet-networked
object looks like it works and is silently thrown away when the network state
initialises. This produced bots that reported `IsBot == false` and inherited the
host's Steam name.

Plain (non-`[Sync]`) properties are fine to set before spawn — and sometimes
must be, e.g. cutting a bot's input source off before it can read a frame.

---

## Scene objects wake before the lobby exists

Anything gated on `Networking.IsHost` inside `OnAwake` **never runs** for objects
placed in the scene, because `NetworkHelper` creates the lobby later. Initialise
unconditionally in `OnAwake` and re-assert in `OnStart` if it must be
host-authoritative. This left target dummies loading as already dead.

---

## `GetComponent` skips disabled objects by default

```csharp
var go = prefab.Clone( transform, null, startEnabled: false, name );
go.GetComponent<Player>();        // null - the object is disabled
go.GetComponent<Player>( true );  // works
```

Silently skipped an entire configuration block, which then failed much later and
somewhere else.

---

## Chaining `IgnoreGameObjectHierarchy` replaces, it does not add

```csharp
// WRONG - the second call replaces the first, so the shooter stops ignoring
// itself. Its eye sits inside its own body collider, so every trace hits
// itself and it concludes it can never see anything.
Scene.Trace.Ray( a, b )
    .IgnoreGameObjectHierarchy( self )
    .IgnoreGameObjectHierarchy( target );

// RIGHT - ignore self only, then treat hitting the target as visibility
var tr = Scene.Trace.Ray( a, b ).IgnoreGameObjectHierarchy( self ).Run();
var visible = !tr.Hit || tr.GameObject.GetComponentInParent<Player>() == target;
```

`BotBrain.CanSee` has the fixed version. `TargetSelector.IsVisible` in the mod
layer still has the bug and is knowingly left alone.

---

## A dead pawn still moves - respawns look like distance travelled

Measuring anything from `WorldPosition` deltas silently counts respawn
teleports. A footstep cadence test read **746 units travelled and zero steps**,
which looked like a broken accumulator. It was not: the bot was killing the test
subject, and every "distance" was a corpse being teleported to a spawn point.
The give-away was `speed 0` on a pawn that had supposedly just run 746 units.

Any harness that measures movement should reject implausible frame deltas and
report whether the subject stayed alive, rather than quietly averaging a corpse
into the result:

```csharp
var reachable = MathF.Max( 40f, velocity.Length * Time.Delta * 2f );
if ( moved > reachable ) _teleports++; else _distance += moved;
```

More generally: when a runtime number looks wrong, check what else was acting on
the subject before changing the code under test.

---

## `SoundHandle.Occlusion` is obsolete

Use `OcclusionEnabled`. `Occlusion` still compiles, with a warning.

---

## A clean `dotnet build` does not mean it compiles

s&box has its own, stricter Razor generator. It has rejected eight lambda
type-inference errors that the .NET SDK accepted without complaint. Always
confirm in the editor — `compile_status` over MCP, or just watch the console.

---

## UI details

- `SliderControl` parameters are `Min` / `Max` / `Step`. Lowercase compiles and
  silently fails to bind, leaving every slider on its 0–100 default.
- Razor attribute literals like `min="0.5"` parse as `double`; use `@(0.5f)`.
- `Consolas` does not exist. Use `Poppins`.
- `user-select` is not a supported property.

---

## Editor and tooling

- The editor reopens the scene named in `.sbox/project.json` →
  `editor.activescene`, **not** the project's `StartupScene`. StartupScene only
  applies when the game itself launches.
- `camera_screenshot` over MCP renders all text as solid tofu boxes — the
  offscreen path has no font atlas. The HUD looks broken and is fine. Capture
  the real window instead (PowerShell `CopyFromScreen`).
- The editor recompiles when its window regains focus.
