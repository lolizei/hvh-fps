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
- [~] Reloading is audible — implemented and compile-verified; **not yet runtime-verified** (editor was closed)
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
4. [~] **Reload and footsteps.** Footsteps **(done)**; reload audio written, runtime check outstanding.

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

## Task 8 - reload audio (2026-09-01) - INCOMPLETE

**Written and compile-clean, but NOT runtime-verified: the s&box editor was
closed for this task and never came back, so nothing here has been heard.**
Treat every claim below as unproven until it is played.

### What was built
Two positional cues driven off the existing synced `IsReloading` flag - one as
the magazine leaves, one as it seats - via `WeaponEffects.Reload`. No new RPC:
`IsReloading` already replicates, so watching its transitions gives both cues on
every machine for free.

The watch sits **above** the `IsSimulatedHere` gate in `Weapon.OnUpdate`, which
is the whole point: a reload you can only hear when you are the one reloading is
worth nothing. Hearing an enemy reload is information, so it is spatialised the
same way footsteps are.

`RestoreAmmo` clears the flag on respawn, which is not a completed reload, so it
also clears the watch to stop a phantom "magazine seated" cue.

### Sound choice, and a correction
`sounds/impacts/bullets/impact-bullet-metal` at pitch 0.8 (out) and 1.25 (in).
Deliberately **not** `impact-melee-metal`, which `HitMarker` already uses for a
kill - a reload that sounds like a kill confirmation is worse than a silent one.

Correcting two things I wrote earlier:
- **NOTES claimed `core/` has "763 sounds".** It has **63 playable sound
  events**. The 763 counted raw `.vsnd_c` audio, most of which is not
  addressable at all - `core/sounds/Physics` has 68 audio files and zero events.
  Corrected in NOTES with the command that produces the real number.
- **`HitMarker` claimed s&box has no UI click sound event.** It has thirteen
  (`sounds/kenney/ui/*`). The melee-impact marker ticks were chosen on a false
  premise. Left alone because they are approved and changing them changes feel,
  but worth revisiting deliberately.

### Still to do
- Play it. Confirm both cues fire, at the right moments, from a bot's reload as
  well as the local player's.
- Confirm the counters: one reload = exactly 2 cues.
- Confirm no phantom cue on respawn-mid-reload.

## Task 7 - console command consolidation (2026-09-01)

**29 commands -> 16.** Nothing deleted; every capability kept.

| Was | Now |
|---|---|
| `hvh_state` `hvh_players` `hvh_botinfo` `hvh_steps` `hvh_dummies` `hvh_hitmarker` `hvh_hitdebug` `hvh_bounds` | `hvh_report <state\|players\|bots\|steps\|dummies\|marker\|hits\|bounds\|all>` |
| `hvh_target` `hvh_bot` `hvh_botduel` `hvh_botnear` `hvh_killbots` `hvh_clearbots` | `hvh_bots <n\|add\|duel\|near\|kill\|clear>` |
| `hvh_killdummies` | `hvh_dummies kill` (plus a new `revive`) |
| `hvh_hitmarker_clear` | `hvh_reset marker` (plus `counters`, `all`) |
| `hvh_menu` | `hvh_loadscene menu` |
| `hvh_traceaim` | `hvh_centerray` |
| `hvh_aim` `hvh_fire` `hvh_shoot` `hvh_hurt` `hvh_kill` `hvh_refill` `hvh_slot` `hvh_sandbox` `hvh_steptest` `hvh_marker_hold` `hvh_loadscene` | unchanged |

16, not the proposed 13: the proposal forgot dummy control, diagnostic reset and
the marker hold. Those are real capabilities, so they got commands rather than
being dropped.

### The spread caveat is structural now
`hvh_centerray` computes the live spread cone by the same rule `Weapon` uses and
leads with it:

```
hvh_centerray: CENTRE RAY ONLY - no spread modelled. A real shot right now
scatters up to 0.7 deg from this line.
  hit 'Crate C' at 170u | health=none | owner=none | canDamage=True
```

### Bugs found by running every command once
- **`hvh_bots near` placed the bot outside the arena.** It used
  `player + forward * distance` with no validity check; from a corner spawn
  facing out that lands past a wall. Every scripted shot then hit the wall, which
  looks exactly like broken hit detection. Task 6 passed only because the shooter
  happened to face inward. It now tries a fan of ten directions, requires solid
  ground *and* line of sight, refuses to move the bot if none qualifies, and
  prints why each direction was rejected.
- **A `WithTag("solid")` trace still hit the shooter.** The line-of-sight check
  reported `hit 'Body' at 0u` in all ten directions: the eye starts inside its own
  body collider, and a trace that begins solid reports a hit at distance zero
  whatever the tag filter says. This contradicts what I wrote in NOTES during
  Task 5 - corrected there.
- **`hvh_bots duel` could trim its own duellist.** It made room for two bots but
  ignored bots already present, so it spawned into an over-target population and
  one got deleted. It now clears first, so a duel is exactly two.
- **The step harness reported cadence for runs that never reached pace.** A pawn
  bouncing off cover averages walking speed while "running", which silently made
  run and walk look identical. It now reports peak speed, the fraction of the test
  spent at pace, and flags a run that never got there - stride stays valid.

### Re-verified through the new commands
| Check | Result |
|---|---|
| Body marker | PASS - 10/10 |
| Headshot marker | PASS - 10/12 |
| Kill marker | PASS |
| Limb hit (15u) | PASS - 8/8 body-kind marker, limb damage |
| Miss shows nothing | PASS - 56 fire requests vs 50 damage, 0 markers |
| Others' hits never show | PASS - 50 hits by two bots, 0 markers |
| One landed hit = one marker | PASS - (1 show, 1 element) x 10/10 |
| Cadence walk/run/crouch | PASS - stride 81.8 / 87.8 / 171.2 u vs 85 / 85 / 153 |
| Airborne silence | PASS - 7.88s airborne, 0 steps, 10 landings |
| Every command invoked once | PASS - all 16, including bad-argument paths |

## Task 6 - double hit marker (2026-09-01)

**Reported from play: two hit markers on screen at once.** Measured before
touching anything, because a second damage application would have mattered far
more than the visual.

Counts for one landed shot, repeated over 4 isolated trials:

| Counter | Per landed shot |
|---|---|
| fireRequests | 1 |
| damageApplications | **1** |
| confirmHitInvocations | 1 |
| confirmHitDeliveries | 1 |
| markerShows | 1 |
| live marker elements | 1 |
| live Hud / ScreenPanel / Crosshair | 1 / 1 / 1 |

Held under sustained fire and under three simultaneous shooters: across 30s of a
two-bot duel, `damageApplications == confirmInvoked == confirmDelivered == 66`
at every sample, never 2:1. **Damage was never applying twice.**

So every engine-side count said one while the screen said two. The cause was in
the stylesheet: the marker's four ticks used `transform: rotate(...) translate(...)`,
which translates along the tick's *own rotated axes*. All four resolved to
`(0, +/-12.7)` - `.tl` and `.tr` both directly above the centre, `.bl` and `.br`
both directly below. One marker rendered as two marks. Fixed by translating
first, then rotating. Confirmed by screen capture before and after: two stacked
clumps became a single four-tick X.

Lesson recorded in `NOTES.md`: when every count says one and the screen says two,
capture the screen.

### Hit zones now measured from bounds
`ClassifyHit` derived the target's feet from its origin. Measured: a player is
`originZ=0` with bounds `0..72`; a dummy is `originZ=36` with bounds `0..72`. It
now uses `GameObject.GetBounds()`, so one rule fits both. `hvh_aim` had the same
assumption - and ignored its height argument entirely for dummies - and was
fixed with it, so a given height means the same body part on any target.

Damage by zone after the change:

| Target | Head (66u) | Chest (40u) | Legs (15u) |
|---|---|---|---|
| Dummy | 100 | 26 | 20.8 |
| Bot | 52 | 13 | 10.4 |

Same ratios on both (head 4x chest, limb 0.8x chest); the bot's lower absolute
numbers are its armour. Before the change a dummy could not be headshot at all.

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
| 3 | Someone else's hit never shows on my crosshair | PASS - 58 hits by other pawns, 0 markers |
| 3 | **One landed hit produces exactly one marker** | PASS - (1 show, 1 element) on every landed hit |
| 3 | Headshot on a **target dummy** | PASS - fixed, was impossible by geometry |
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
- **`hvh_botduel` and `hvh_bot` reported success and delivered nothing.**
  `BotManager.Converge()` trimmed the new bots within a frame or two. From
  `hvh_target 1`, `hvh_botduel` logged two duellists and produced zero, and
  `hvh_bot` logged a spawn and produced zero. Both now go through a shared
  `EnsureRoomForBots` helper so the rule lives in one place. This had silently
  emptied the arena under three measurements.

### Found, not fixed at the time (fixed in Task 6)
- **Head zones cannot be hit on target dummies.** `ClassifyHit` measures the hit
  height upward from the target's origin. A player's origin is at its feet;
  a `TargetDummy` sits at z=36, its middle. So on a dummy the head zone begins
  above the dummy's own head, and everything below its waist reads as a limb.
  Player-versus-player hit zones are unaffected, which is why this is reported
  rather than fixed: the fix is either re-placing the dummies or changing
  `ClassifyHit`, and the damage path is a working system. Verify zones on a bot.

## Known issues
- **`hvh_traceaim` does not model spread.** It traces the un-spread centre ray,
  while a real shot scatters up to ~9.5 degrees when moving or airborne. It
  answers "what am I pointed at", not "where will this shot land". To be renamed
  or made self-explanatory in Task 7.
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
