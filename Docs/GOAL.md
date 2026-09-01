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
- [ ] Firing produces a muzzle flash at the weapon and a fire sound
- [ ] Each shot leaves a visible tracer along its path
- [ ] Shots that hit the world leave an impact effect at the hit point
- [ ] Hitting a player shows a hit marker on your crosshair and plays a distinct hit sound
- [ ] Killing a player gives a clearly different confirmation from a body hit
- [ ] Reloading is audible
- [ ] Footsteps are audible (the hook already exists and is unassigned)
- [ ] The bot's shots produce the same flash/sound/tracer, so you can tell where you are being shot from

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
| Footsteps | `Assets/prefabs/player.prefab` | Assign the existing sound property |
| Assets | `Assets/sounds/*` | Sound events for fire, reload, hit, kill, footstep |

## Tasks
1. [ ] **Fire is visible and audible.** Muzzle flash at the muzzle and a fire sound, broadcast from the host so every player sees and hears every shot — the bot's included.
2. [ ] **Tracer and impact.** A tracer along the shot path and an impact effect where it lands.
3. [ ] **Hit confirmation.** The shooter gets a crosshair hit marker and a hit sound when damage lands; a kill reads differently from a body hit.
4. [ ] **Reload and footsteps.** Assign the existing footstep hook and add a reload sound.

## Verification
Single player, `scenes/game.scene`, editor Play, one human + one bot.

1. **Task 1** — Fire: a flash appears and a shot is audible. Stand where the bot can see you: its shots are visible and audible from your position and you can tell which direction they came from.
2. **Task 2** — Fire at a wall: a tracer travels to it and an impact appears at the point the trace reported. Fire at the sky: the tracer still reads correctly.
3. **Task 3** — Shoot the bot in the body: hit marker and hit sound. Kill it: a distinct confirmation. Shoot a wall: neither fires.
4. **Task 4** — Walk: footsteps. Reload: audible. The bot's footsteps are audible as it approaches.

## Risks
- ~~s&box may not ship a usable gunshot sound.~~ **Checked before starting and cleared:** base content has 157 gunshot-type sound assets (e.g. `1911_shoot`), 69 footstep sounds, and ready-made `default_muzzleflash`, `default_tracer` and `default_brasseject` prefabs under `addons/base/Assets/prefabs/effects/`. Task 1 and 2 should reuse those prefabs rather than build effects from scratch.
- Per-shot effect broadcasts are the project's first per-shot RPC traffic. At 600 RPM this is the first thing that could flood the wire — keep the payload small and flag it if it looks heavy.
- `Weapon.Fired` is local-only, while the host-side `RequestFire` is where hits are actually known. The two halves may need different effects.

## Blockers
- None.
