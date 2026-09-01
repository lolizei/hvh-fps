# Goal: Every Shot Has Weight

**Status:** active
**Set:** 2026-09-01

*Previous goal "An Opponent That Fights Back" shipped and is archived at
`Docs/goals/2026-09-01-opponent-that-fights-back.md`.*

## Objective
When you fire, you see and hear it; when you hit someone, you know instantly without reading the HUD.

## Why this one
The game is mechanically complete — movement, weapons, rounds, and an opponent that hunts and kills you — but firing produces nothing at all. No flash, no sound, no tracer, no impact, no hit confirmation. Right now you learn that you hit something by watching a number change in the corner of the screen. This is the largest remaining gap between "the systems work" and "it feels like a shooter", it carries no netcode risk, and it needs no new gameplay rules.

## Definition of Done
- [~] Firing produces a muzzle flash **(done)** and a fire sound **(blocked, see Blockers)**
- [x] Each shot leaves a visible tracer along its path
- [x] Shots that hit the world leave an impact effect at the hit point (surface-appropriate, with sound)
- [x] Hitting a player shows a hit marker on your crosshair and plays a distinct hit sound
- [x] Killing a player gives a clearly different confirmation from a body hit (plus a separate headshot marker)
- [ ] Reloading is audible
- [x] Footsteps are audible — speed-dependent cadence, surface-appropriate, spatialised, and produced by bots through the same path
- [x] The bot's shots produce the same flash, tracer and impact **(observed via a bot duel)** — sound still blocked, so you can tell where you are being shot from

## Non-Goals
- **Viewmodel / first-person arms.** The gun being invisible is a real gap, but it needs models and animation and is its own goal.
- Third-person player animations
- Persistent decals, surface-specific impact sounds, bullet holes
- Blood, gibs, ragdolls
- Music, voice lines, announcer
- Any gameplay change whatsoever — damage, spread, recoil and round rules stay exactly as they are
- HVH / mod features
- Two real human clients

## Constraints
- **Cosmetic only.** Nothing here may change a gameplay value. If an effect appears to need a number that does not exist, report it rather than inventing balance.
- **Effects are broadcast, never trusted.** The host decides a hit happened; the effect is then announced. A client never tells anyone else that it hit something.
- State replication and cosmetic effects stay separate concerns, per the project rules.
- Reuse existing seams: `Weapon.Fired` exists for local fire, `GameEvents` already broadcasts kills, `PlayerMovement.FootstepSound` exists and is merely unassigned. Do not add a parallel effects system if one of these fits.
- Zero direct `Input.*` reads outside `HumanInputSource`.
- Must not regress human input, bot behaviour, or the round loop.

## Systems touched
| System | File(s) | Change |
|---|---|---|
| Shot effects | `Code/Weapons/Weapon.cs` | Broadcast fire/impact from the host |
| Effect playback | `Code/Weapons/WeaponEffects.cs` *(new)* | Muzzle flash, tracer, impact, sounds |
| Hit feedback | `Code/UI/Crosshair.razor`, `Code/Core/GameEvents.cs` | Hit marker and hit/kill confirmation for the shooter |
| Footsteps | `Code/Player/PlayerFootsteps.cs` *(new)*, `Assets/prefabs/player.prefab` | Distance-driven cadence, surface sounds, spatialised |
| Step measurement | `Code/Core/StepTest.cs` *(new)*, `Code/Core/DevCommands.cs` | `hvh_steps`, `hvh_steptest` - scripted-input cadence harness |
| Assets | `Assets/sounds/*` | Sound events for fire, reload, hit, kill, footstep |

## Tasks
1. [~] **Fire is visible (done) and audible (blocked).** Muzzle flash at the muzzle and a fire sound, broadcast from the host so every player sees and hears every shot — the bot's included.
2. [x] **Tracer and impact.** A tracer along the shot path and an impact effect where it lands.
3. [x] **Hit confirmation.** The shooter gets a crosshair hit marker and a hit sound when damage lands; a kill reads differently from a body hit.
4. [~] **Reload and footsteps.** Footsteps **(done)**; reload sound still open.

## Verification
Single player, `scenes/game.scene`, editor Play, one human + one bot.

1. **Task 1** — Fire: a flash appears and a shot is audible. Stand where the bot can see you: its shots are visible and audible from your position and you can tell which direction they came from.
2. **Task 2** — Fire at a wall: a tracer travels to it and an impact appears at the point the trace reported. Fire at the sky: the tracer still reads correctly.
3. **Task 3** — Shoot the bot in the body: hit marker and hit sound. Kill it: a distinct confirmation. Shoot a wall: neither fires.

   **Task 4 measured, 2026-09-01**, via `hvh_steptest`, which drives the pawn
   through `IPlayerInputSource` with no keyboard involved. All runs clean —
   no deaths, no respawn teleports:

   | Mode | Cadence | Avg speed | Measured stride | Configured stride |
   |---|---|---|---|---|
   | Walk | 1.62 steps/s | 144 u/s | 89.0 u | 85 u |
   | Run | 2.62 steps/s | 229 u/s | 87.2 u | 85 u |
   | Crouch | 0.50 steps/s | 80 u/s | 160.6 u | 153 u |
   | Jump | 0.00 steps/s | — | — | airborne 9.84s of 10s, **0 steps**, 13 landings |

   Run is 1.6x walk and crouch is 3.2x slower than walk, from one distance
   accumulator and no per-mode rules. Bot footsteps with the human standing
   still: human frozen at 118 steps / 0 u/s across four consecutive samples
   while the bot went 16 -> 17 -> 20 -> 24 at 150 u/s. Leak check: 36 scene
   objects before, during and after ~21 steps - steps spawn nothing.
4. **Task 4** — Walk: footsteps. Reload: audible. The bot's footsteps are audible as it approaches.

## Risks
- **s&box ships no gunshot sound. CONFIRMED, and my earlier "cleared" note was wrong.** That count of 157 included `download/assets/...`, which is the cloud cache of *other people's packages*, not base content. `addons/base` contains zero sound files. Engine `core/` has 763 sounds — 294 footsteps, 123 impacts (including `impact-bullet-flesh` and `impact-bullet-concrete`), UI clicks — but nothing resembling a gun.
- The effect prefabs DO exist and work: `prefabs/effects/default_muzzleflash.prefab` and `prefabs/effects/default_tracer.prefab`. Note the `prefabs/` prefix - the on-disk folder layout is misleading and the path without it silently resolves to nothing.
- Per-shot effect broadcasts are the project's first per-shot RPC traffic. At 600 RPM this is the first thing that could flood the wire — keep the payload small and flag it if it looks heavy.
- `Weapon.Fired` is local-only, while the host-side `RequestFire` is where hits are actually known. The two halves may need different effects.

## Task 5 - consolidation sweep (2026-09-01)

All four feature tasks re-verified in one session on current main. **Current
state, not as-shipped.**

| Task | Check | Result |
|---|---|---|
| 1 | Muzzle flash spawns per shot, host-broadcast | PASS - exactly 1 per shot |
| 1 | Fire sound | **BLOCKED** - no gunshot asset exists |
| 2 | Tracer spawns per shot | PASS |
| 2 | Impact spawns where the shot lands | PASS |
| 2 | Impact sits on the hit geometry | PASS - 3 consecutive impacts at exactly x=-300.0, one plane |
| 2 | Effects self-destruct | PASS - 36 -> 42 -> 36 objects |
| 3 | Body marker | PASS - 12/14 |
| 3 | Headshot marker | PASS - 9/14 vs a bot |
| 3 | Kill marker | PASS |
| 3 | Miss shows nothing | PASS |
| 3 | Someone else's hit never shows on my crosshair | PASS - 22/22 invisible during bot combat |
| 3 | Headshot on a **target dummy** | **FAIL** - impossible by geometry, see below |
| 4 | Cadence differs walk/run/crouch | PASS - 1.37 / 2.12 / 0.50 steps/s |
| 4 | Airborne silence | PASS - 9.83s airborne, 0 steps, 13 landings |
| 4 | Bot-produced footsteps, human idle | PASS - bot 5->18 while human held at 198 |
| 4 | Ground surface resolves (not silently falling back) | PASS - `default` -> `footstep-concrete` |

Cadence is lower than the Task 4 figures because average speed was lower this
run (more wall contact). The speed-independent measure, stride, matches:
90.8 / 86.5 / 171.7 u against 85 / 85 / 153 configured.

### Sustained load - 8 bots plus a human, ~4.5 minutes continuous combat

Object counts sampled every 7s throughout, not just before and after:

- Quiescent floor with 9 pawns: **exactly 100**, returned to on 14 separate
  samples spread across the whole run. Never crept.
- Peak: **413 objects** (124 effect objects in flight) during a heavy exchange.
- After clearing bots and settling: **36** - the identical baseline the session
  started at.

No leak. This is the first time all four effect systems ran together for a long
stretch. 9 pawns is at the ~10-simultaneous-shooter pooling threshold and the
numbers are reported, not acted on, as agreed.

### Bugs found and fixed by the sweep
- **`hvh_botnear` teleported the bot onto its own head.** Its ground trace chained
  two `IgnoreGameObjectHierarchy` calls - the documented trap, second occurrence -
  so it stopped ignoring the bot, hit the bot standing there and placed it at
  z=128, falling. Scripted aim then missed 13/13. Now traces `WithTag("solid")`.
  Same test after the fix: 9 headshots in 14.
- **`hvh_botduel` reported success and delivered nothing.** `BotManager.Converge()`
  trimmed both duellists within a frame or two. It now raises `DesiredPlayers`
  first. This had silently emptied the arena under three measurements.

### Found, not fixed
- **Head zones cannot be hit on target dummies.** `ClassifyHit` measures the hit
  height upward from the target's origin. A player's origin is at its feet;
  a `TargetDummy` sits at z=36, its middle. So on a dummy the head zone begins
  above the dummy's own head, and everything below its waist reads as a limb.
  Player-versus-player hit zones are unaffected, which is why this is reported
  rather than fixed: the fix is either re-placing the dummies or changing
  `ClassifyHit`, and the damage path is a working system. Verify zones on a bot.

## Known issues (task 4)
- **The default surface uses the same sound for left and right feet.** The
  alternation in `PlayerFootsteps` is real, but `default` points `FootLeft` and
  `FootRight` at the same `footstep-concrete` event, so on the current grey-box
  floor it produces no audible variation. It will once a map uses surfaces that
  define distinct feet. Not worth working around.
- **Reload is still silent.** Task 4 covered footsteps only.

## Blockers
- **No gunshot sound exists to point `FireSound` at.** Everything else in task 1
  is done: the muzzle flash spawns per shot, is broadcast from the host so bots'
  shots show too, and self-destroys. `Weapon.FireSound` is wired and left
  deliberately empty. Three ways forward, needs a decision:
    1. Add a `PackageReference` to a sound package from sbox.game (the normal
       s&box route, adds a cloud dependency to the project).
    2. Drop a gunshot .wav into `Assets/sounds/` and author a .sound resource.
    3. Ship visual-only for now. Impacts, flesh hits and footsteps all have
       usable engine sounds, so tasks 2-4 are NOT blocked by this.
  A stopgap exists — `sounds/effects/explosion/explosion_small` — but it will
  sound wrong, so I have not wired it.
