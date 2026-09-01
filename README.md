# HvH — a round-based competitive FPS in s&box

A small competitive shooter built in [s&box](https://sbox.game). Two teams, short rounds,
data-driven weapons, and a bot opponent that hunts you. It also carries a first-class
**in-game mod framework** — the long-term idea is a hack-versus-hack sandbox where the
mods are part of the game's design rather than something fought against.

> **Scope note, since the name invites the question.** Everything here is a mechanic of
> *this* game, running inside s&box, acting on this game's own scene objects. There is no
> external process injection, no memory manipulation, no anti-cheat interaction, and none
> of that will be accepted. The "cheat" systems are a gameplay feature of a game whose
> whole premise is that both sides have them.

---

## Status

The core loop is real and has been played: you spawn, move, shoot, kill, die, respawn,
and rounds resolve. A bot opponent will hunt you down and kill you.

| System | State |
|---|---|
| Movement, camera, collision | Working — Source-style accel/friction, crouch, air control |
| Weapons | Working — 4 data-driven guns, spread, recoil, reload, hit zones |
| Health / damage / death / respawn | Working — host-authoritative |
| Round loop | Working — Warmup → RoundStart → Playing → RoundEnd → Restarting |
| Teams + scoreboard + kill feed | Working |
| HUD | Working — health, armour, ammo, round clock, crosshair, kill feed |
| Menu → game → menu | Working, round-trips cleanly |
| **Bot opponent** | Working — searches, chases, strafes, shoots, reloads, kills you |
| Mod framework + example mod | Loads at runtime, API documented |
| HVH features (aim assist, ESP, etc.) | Written, **all default off, never tuned or tested** |
| Multiplayer with 2+ real clients | **Never tested.** Biggest unknown in the project |
| Shot feedback | Muzzle flash, tracers, impacts, hit markers, footsteps working. **No gunshot sound** — s&box ships none |
| Viewmodel | Not implemented — the gun is invisible |
| Real map | Not started. Current arena is a grey box |

Roughly 6k lines of C# and Razor. Every "Working" row above was verified by actually
playing it, not by the code compiling.

---

## Getting set up

You need s&box (free, on Steam) and .NET 10.

1. Clone this repo into your s&box projects folder, typically
   `Documents/s&box projects/`.
2. Open s&box, then **Add existing project** and point it at `hvh_testication.sbproj`.
3. Open `scenes/game.scene` and press **Play**.

**The `.csproj` is deliberately not committed.** s&box generates it with absolute paths to
*your* engine install, so committing it would break everyone else. The editor regenerates
it the first time you open the project — if the build looks broken before you have opened
the editor once, that is why.

You can compile without launching the editor once the csproj exists:

```bash
dotnet build Code/hvh_testication.csproj
```

⚠️ **A clean `dotnet build` does not mean it compiles.** s&box has its own, stricter Razor
generator. It has rejected code the .NET SDK accepted. Always confirm in the editor before
believing a green build.

---

## Where things live

```
Code/
  Core/        round loop, teams, health, spawns, bots, dev commands
  Player/      pawn, movement, camera, per-player state, input seam
  Weapons/     weapon data, weapon component, inventory
  UI/          HUD, menus, scoreboard, kill feed, mod menus (Razor)
  Mods/        the public mod API — interfaces, manager, config, event bus
  HVH/         the built-in mod and its features
  Examples/    a third-party mod proving the API works
Assets/
  scenes/      menu.scene (startup), game.scene (gameplay)
  prefabs/     player.prefab
Docs/
  GOAL.md      the ONE current goal — read this first
  MODDING.md   how to write a mod
  MAP.md       planned map layout
  goals/       shipped goals, archived
```

### The one architectural idea worth knowing

Input is separated from the things that act on it:

```
PlayerInputState        one frame of intent (move, look, buttons)
IPlayerInputSource      supplies it
  ├── HumanInputSource  reads the keyboard/mouse — the ONLY place that touches Input.*
  └── BotBrain          an AI supplies the same struct
```

`PlayerMovement`, `Weapon` and `WeaponInventory` read intent and never touch `Input`
directly. That is why a bot can drive the identical movement and weapon code a human does,
and why a bot can never accidentally eat your keyboard. **If you add a gameplay component,
read intent — do not add a second `Input.Down(...)` call.**

Three ownership concepts, and the distinction matters:

| Property | Means | Gates |
|---|---|---|
| `IsBot` | driven by an AI | scoreboard, kill feed, round rules |
| `IsLocallyControlled` | the pawn *you* are playing | camera, HUD, `Player.Local` |
| `IsSimulatedHere` | this machine advances it | movement + weapon simulation |

Neither of s&box's ownership concepts can tell a host-spawned bot from your own pawn —
both are non-proxy and both are owned by the local connection — which is why `IsBot` is an
explicit flag rather than something derived.

---

## Dev console commands

Open the console in-game. These made everything above testable.

| Command | Does |
|---|---|
| `hvh_state` | position, angles, health, team, weapon, ammo, round |
| `hvh_players` | every pawn: bot?, team, K/D, input, who last hit them |
| `hvh_botinfo` | bot brain: target, why it is or isn't shooting, distance, speed |
| `hvh_bot` / `hvh_clearbots` | spawn / remove bots |
| `hvh_target <n>` | set the total player target (bots fill the rest) |
| `hvh_sandbox` | hold the round in Playing and revive everything — for testing |
| `hvh_hurt <n>` / `hvh_kill` | damage or kill yourself |
| `hvh_killbots` / `hvh_killdummies` | clear the opposition |
| `hvh_shoot <n>` | aim at nearest enemy and fire, in one frame |
| `hvh_traceaim` | run the weapon's exact trace and report what it would hit |
| `hvh_refill` / `hvh_slot <n>` | refill ammo / switch weapon |
| `hvh_steps` | per-pawn footstep state, cadence and ground surface |
| `hvh_steptest <mode>` | drive the pawn under scripted input and measure step cadence |

`hvh_traceaim` and `hvh_botinfo` are the two that solve most "why isn't this working"
questions.

---

## How to help

Read `Docs/GOAL.md` first. The project runs **one goal at a time**, broken into tasks that
each leave the game playable. Please don't start a parallel system — if a seam you need
doesn't exist, say so rather than building a second one alongside.

Good places to jump in:

- **The current goal** — shot feedback: muzzle flash, tracers, impacts, hit markers, sounds.
  s&box ships `default_muzzleflash`, `default_tracer` and `default_brasseject` prefabs,
  plus impact and footstep sounds, so much of this is wiring rather than art. Note there
  is **no gunshot sound** in s&box — that one needs an asset dropping in.
- **A viewmodel.** The gun is currently invisible. Needs a model and animation.
- **Multiplayer testing.** If you can run a second client, you are more useful than anyone
  else on this list — none of the netcode has ever run with two real players.
- **A real map.** `Docs/MAP.md` has the intended layout. The current arena is a grey box
  whose cover blocks sightlines in annoying ways.
- **Weapon tuning.** The damage and recoil numbers were written blind and never balanced.

### House rules

- Verify by playing, not by compiling. "It builds" is not evidence.
- Server-authoritative: client input is a request, never a result. Never trust a client for
  damage, position, score or ammo.
- Don't mix state replication and cosmetic effects in one change.
- No external cheating functionality, ever. See the scope note at the top.

---

## Traps we already fell into

Documented so you don't lose the same afternoon we did. Fuller detail, with the
fixes, is in [`Docs/NOTES.md`](Docs/NOTES.md).

- **Asset paths need the `prefabs/` prefix, and a wrong path fails silently.**
  `GameObject.Clone` with a bad path throws nothing, logs nothing and spawns
  nothing. Confirm the real path in the asset browser before using it.
- **`download/assets/…` is other people's cloud cache, not base content.** If
  you find an asset only there, it is not available to this project.
- **A dead pawn still moves.** Respawn teleports count as distance travelled and
  will make a corpse look like a sprinter in any movement measurement.

- **`[Sync]` writes before `NetworkSpawn()` are silently discarded.** Set networked values
  *after* spawning. This produced bots that reported `IsBot == false` and inherited the
  host's name.
- **Scene objects wake before the lobby exists.** Anything gated on `Networking.IsHost`
  inside `OnAwake` never runs for scene-placed objects. Initialise unconditionally.
- **`GetComponent` on a disabled object returns null** unless you pass
  `includeDisabled: true`. This silently skipped a whole configuration block.
- **Chaining two `IgnoreGameObjectHierarchy` calls replaces rather than adds.** The shooter
  stopped ignoring its own body, its eye sits inside that body, so every trace hit itself.
- **`SliderControl` params are `Min`/`Max`/`Step`.** Lowercase compiles fine and silently
  doesn't bind.
- **`Consolas` doesn't exist in s&box.** Use `Poppins`.
- The editor reopens the scene named in `.sbox/project.json` → `editor.activescene`, *not*
  the project's `StartupScene`.

---

## License

None yet — ask before reusing. No assets, maps, sounds or code are taken from any
commercial game; all content is either original or from s&box's own base content.
