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
sounds/impacts/bullets/impact-bullet-flesh
sounds/impacts/bullets/impact-bullet-concrete
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

`BotBrain.CanSee` has the fixed version.

**This has now bitten twice.** The second was `hvh_botnear`, whose "drop the bot
onto the floor" trace chained the same two calls, so it stopped ignoring the bot
and landed on the bot already standing there - teleporting it to z=128, on top of
its own head, where it fell while the aim taken a moment earlier went stale. It
reported success every time. Symptom: a scripted aim that misses 13 shots out of
13 for no visible reason.

When you want world geometry, filter for the thing you actually want:

```csharp
Scene.Trace.Ray( a, b ).WithTag( "solid" ).Run();   // world only
```

World geometry is tagged `solid`; pawns and their children are tagged `player`.

**But a tag filter does not save you from starting inside a collider.** Measured:
a `WithTag( "solid" )` trace from a pawn's eye reported `hit 'Body' at 0u` in all
ten directions tried, because the eye sits inside that pawn's own body collider
and a trace that begins solid reports a hit at distance zero whatever the filter
says. The same filter works fine for a trace that starts in open air - which is
why the vertical ground trace in `hvh_bots near` was genuinely fixed by it while
the line-of-sight trace right next to it was not.

So: **filter by tag for what you want to hit, and still ignore your own
hierarchy if the trace starts inside you.** Both, not either.

```csharp
Scene.Trace.Ray( eye, target )
    .IgnoreGameObjectHierarchy( self.GameObject )   // we start inside ourselves
    .WithTag( "solid" )                             // and only care about world
    .Run();
```

`TargetSelector.IsVisible` in the mod layer still has the original bug and is
knowingly left alone - the HVH features are all default-off and untested.

---

## `BotManager.Converge()` deletes bots you spawned by hand

`Converge()` trims the bot count to `DesiredPlayers - humans`. Anything that
spawns bots directly - `hvh_botduel` did - has its bots removed within a frame or
two, *after* the command has already logged that it succeeded. From
`hvh_target 1`, `hvh_botduel` reported two duellists and delivered zero, and
`hvh_bot` reported a spawn and delivered zero.

Raise `DesiredPlayers` first - `DevCommands.EnsureRoomForBots` does this - or the
arena empties under you. This silently invalidated three separate measurements
before it was spotted.

---

## Practice dummies are placed at their centre, players at their feet

**Fixed 2026-09-01** - `ClassifyHit` and `hvh_aim` now measure from
`GameObject.GetBounds().Mins.z` instead of the origin, so one rule is correct for
both. Kept here because the origin convention is still not uniform and anything
new that assumes "origin == feet" will be wrong for dummies.

Measured: dummy `originZ=36`, bounds `0..72`; player `originZ=0`, bounds `0..72`.


`Weapon.ClassifyHit` used to measure the hit height as a fraction of stand height
*upward from the target's origin*. A `Player` origin is at the feet, so that was
correct. `TargetDummy` objects sit at z=36 - their middle - so the fraction was
measured from the dummy's waist: the head zone started above the dummy's own head
and could not be hit at all, and everything below its waist read as a limb.

---

## A clean `dotnet build` does not mean it compiles

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

## CSS transform order: `rotate` before `translate` moves along rotated axes

This drew the hit marker as **two** markers for weeks and read as a gameplay bug.

```scss
// WRONG - rotate first, so the translate runs in the tick's own rotated frame
.tl { transform: rotate(45deg)  translate(-9px, -9px); }
.tr { transform: rotate(-45deg) translate(9px, -9px); }

// RIGHT - place it, then spin it
.tl { transform: translate(-9px, -9px) rotate(45deg); }
.tr { transform: translate(9px, -9px)  rotate(-45deg); }
```

With the wrong order every one of the marker's four ticks resolved to
`(0, +/-12.7)`: `.tl` and `.tr` both landed straight above the centre and
`.bl`/`.br` both straight below. Four ticks collapsed onto two points, and one
hit marker looked like two.

Nothing about this is visible from the counts - one damage application, one
`ConfirmHit`, one `HitMarker.Show`, one marker element. **The engine-side
measurements were all correct and all beside the point.** When every count says
one and the screen says two, capture the screen.

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
