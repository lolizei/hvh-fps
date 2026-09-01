# Goal: An Opponent That Fights Back

**Status:** done
**Set:** 2026-09-01

## Shipped
A bot opponent spawns on the enemy team through the same Player/Weapon/round
systems a human uses: it hunts you, shoots, kills you, dies, holds a team slot,
and resets with the round.
Five tasks, all runtime-verified; the population converges on a total-player
target in both directions and teams stay balanced through the human path.

## Objective
A bot opponent spawns on the enemy team and fights you in a live round — it hunts you, shoots at you, you can kill it, and it can kill you.

## Why this one
Right now the game cannot be lost. The three targets in the arena are inert boxes that never shoot, so there is no opposition, no threat, and no reason to use cover. Everything else — movement, weapons, rounds, HUD, menu — already works. An opponent is the missing half of "a working game", and it needs no HVH code whatsoever.

## Definition of Done
- [x] A bot is present in the arena on the opposing team and appears on the scoreboard (Tab) with its own name, kills and deaths
- [x] The bot moves toward you rather than standing still
- [x] The bot shoots at you and can take your health to zero — **you can lose**
- [x] You can kill the bot; the kill appears in the kill feed and increments your score
- [x] With bots alive, the round ends by team elimination and the next round starts with the bot back
- [x] Nothing about the existing single-player experience regresses: your HUD, camera and weapon still bind to *you*

## Non-Goals
- **All HVH / mod features.** Not touched, not tested, not enabled. They stay off and stay last.
- Navmesh or pathfinding — steering only; the bot may bump into walls
- Difficulty levels, reaction-time modelling, or skill tuning beyond "not obviously unfair"
- Bot animations, voice lines, avatar cosmetics
- Two real human clients over the network — that is a later goal
- Bots choosing or buying weapons — the rifle they spawn with is enough
- Cover usage, peeking, grenades, any tactical behaviour
- Muzzle flash, tracers, hit markers, sounds — cosmetic feedback is its own goal

## Constraints
- **Host authoritative.** Bots are created, ticked and destroyed only on the host. No bot code runs on a client, and no bot decision is ever taken from a client message.
- Bots must reuse the existing `player.prefab` and the `Player` / `PlayerState` / `Weapon` systems. A parallel "AI pawn" is not acceptable — a bot that runs the real player code path is also a test of that code path.
- The client/host split in the weapon fire path must not change. A bot fires through the same `Weapon.FireOnce()` → host trace → `ApplyDamage` chain a human uses.
- No task may leave the game unable to run.
- s&box only, current APIs, no invented engine calls.

## Systems touched
| System | File(s) | Change |
|---|---|---|
| Local player resolution | `Code/Player/Player.cs` | Exclude bots from `Player.Local`; add an `IsBot` marker |
| Input ownership | `Code/Player/PlayerMovement.cs`, `Code/Weapons/Weapon.cs` | Read input only for the real local player, never for a bot |
| Bot brain | `Code/Core/BotController.cs` *(new)* | Host-only: pick a target, aim, fire, steer |
| Bot spawning | `Code/Core/BotManager.cs` *(new)* | Clone the player prefab, force team, fill to a count each round |
| Team assignment | `Code/Player/PlayerState.cs` | Allow a forced team and a fallback name for ownerless pawns |
| Round rules | `Code/Core/RoundManager.cs` | Prefer team elimination whenever a bot is alive |
| Dev tooling | `Code/Core/DevCommands.cs` | `hvh_bot`, `hvh_bots <n>`, `hvh_clearbots` |

## Tasks
1. [x] **Make `Player.Local` bot-proof.** Add `PlayerState.IsBot`; `Player.Local` returns only the non-proxy, non-bot pawn, and `PlayerMovement` / `Weapon` refuse to read input for a bot. No bots exist yet — the game must play exactly as it does today. *(Must be first: a host-spawned bot is also non-proxy, so without this the HUD, camera and weapon would happily bind to the bot and it would fire on your mouse clicks.)*
2. [x] **Spawn one inert bot.** `hvh_bot` clones `player.prefab` on the host, forces it onto the enemy team, flags it as a bot, gives it a name. It stands still, is shootable, dies, shows in the kill feed and on the scoreboard.
3. [x] **Bot shoots.** Host-only: pick the nearest visible enemy, face it, fire through `Weapon.FireOnce()` with a reaction delay and imperfect aim. It can now kill you.
4. [x] **Bot moves.** Advance while far, strafe while close, respect gravity and walls via the existing `CharacterController`.
5. [x] **Round rules and auto-spawn.** `BotManager` refills the enemy team to N at round start; `RoundManager` uses team elimination whenever a bot lives, and skips the dummy rule while it does.

## Verification
Single player, `scenes/game.scene`, editor Play, one human + one bot unless stated.

1. **Task 1** — Play with no bots. `hvh_state` reports your own pawn, HUD shows your health and ammo, firing consumes your ammo. Identical to today.
2. **Task 2** — `hvh_bot`, then Tab: two rows on opposing teams. Shoot the bot dead; the feed reads `<you> · VK-7 Rifle · <bot>` and your score goes up. Your own ammo is unaffected while the bot exists (proves task 1 held).
3. **Task 3** — Stand still in the open in front of the bot. Your health drops and you die. Confirm damage arrived through the weapon path, not a direct `ApplyDamage` call.
4. **Task 4** — Spawn the bot at the far corner. It closes the distance, strafes near you, stays on the floor and inside the arena for at least 30 seconds.
5. **Task 5** — Round with 1 bot: kill it, round ends by elimination, score increments, next round starts with the bot alive again. Then stand still and let it kill you — the round must also end when the human side is wiped, and you respawn into the next one.

## Risks
- **`Player.Local` binding to a bot** — the highest risk, which is why it is task 1. Symptom: HUD shows someone else's health, or the camera is attached to the bot.
- **Bots reading your input.** Bot pawns are non-proxy on the host, so `PlayerMovement` and `Weapon` will read the local keyboard and mouse for them unless explicitly gated. The failure mode is silent — the bot mirrors your movement and fires when you fire — not a crash.
- **Ownerless pawns.** `PlayerState.OnStart` reads `Network.Owner.DisplayName`; a bot has no `Connection` and must fall back to a generated name instead of throwing.
- **Two round rules firing at once.** The arena still holds three dummies. Elimination must take precedence and the dummy rule must be skipped while a bot is alive, or a round could resolve twice.
- Fallback for task 3: if driving `Weapon` from a bot proves tangled, the bot may call the host damage path directly for one build to keep the game runnable — but the task is not ticked until the real fire path is restored.

## Blockers
- None.
