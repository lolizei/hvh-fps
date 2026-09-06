# Two real clients — readiness audit

**Nothing in this project has ever run with two actual machines.** This is a
read of the netcode, not a test. Every item says whether it is *certain from the
code* or *unknown until it runs*.

Written 2026-09-06, before any two-client session.

---

## What is already right

These are load-bearing and look correct:

- **Damage, health, score, ammo and round state are host-owned.** Every one is
  `[Sync( Flags = SyncFlags.FromHost )]`, so a client cannot write them.
- **The ownership model is coherent.** `IsBot` (synced), `IsLocallyControlled`
  (`!IsProxy && !IsBot`) and `IsSimulatedHere` (`!IsProxy`) are three distinct
  ideas and each is used for the right thing — camera and HUD on the first,
  movement and weapon simulation on the second.
- **Proxies are not simulated.** `Player.OnUpdate` feeds `Idle` intent to remote
  pawns and `PlayerMovement.OnUpdate` returns early for them, so two machines
  cannot fight over one pawn.
- **The input seam survives the network.** A bot pawn on a client is a proxy with
  the default `HumanInputSource`, and `ResolveInputSource()` still refuses to let
  it read a keyboard.
- **Hit markers are correctly private.** `ConfirmHit` is `[Rpc.Owner]` and
  additionally checks `IsLocallyControlled`, which is what stops a host seeing
  markers for its own bots' hits. Measured: 50 bot hits, 0 markers.
- **Reload audio uses the synced flag,** so it needs no new RPC and will work for
  remote players — the one audio system that will.

---

## What will break

### 1. The round clock will be wrong on every client — CERTAIN

```csharp
[Sync( Flags = SyncFlags.FromHost )] public float PhaseEndTime { get; set; }
public float TimeRemaining => MathF.Max( 0f, PhaseEndTime - Time.Now );
```

`PhaseEndTime` is an **absolute** timestamp computed from the *host's* `Time.Now`,
then replicated raw. `Time.Now` is per-machine scene time, so a client's clock
starts whenever *it* loaded the scene. The two never agree, and the error equals
the difference in scene start times — which grows the later someone joins.

`RoundBar.razor` shows this number to the player. A client sees a round timer
that is simply wrong, possibly wildly, possibly already at zero.

**Fix:** replicate the remaining duration and a host tick, or convert to a
network-synced clock. Do not compare a replicated absolute time against local
`Time.Now`.

### 2. Every other player will be silent — CERTAIN

`PlayerFootsteps` reads `PlayerMovement.Velocity`, `IsOnGround` and
`IsCrouching`. All three come from `CharacterController`, which is **only
simulated on the machine that owns the pawn** — `PlayerMovement.OnUpdate`
returns early for proxies. None of the three is `[Sync]`.

So on a client: your own footsteps play, and **every other player and bot is
silent**. Remote pawns move by transform interpolation with a velocity that is
never updated, so `speed < MinimumSpeed` and the component returns immediately.

This negates the feature. Footsteps were built specifically as information you
play on; on two machines you would only ever hear yourself.

**Fix:** derive speed from world-position delta rather than controller velocity,
so it works the same for a simulated pawn and an interpolated one, and sync the
crouch flag. That is a contained change to `PlayerFootsteps` plus one `[Sync]`.

The same reasoning applies to anything else that ever reads controller state for
a pawn it does not simulate.

### 3. Fire rate is client-authoritative — CERTAIN, but a design question

The rate limit lives in `CanFire()` on the firing client:

```csharp
if ( Time.Now < _nextFire ) return false;
```

`RequestFire` on the host checks ammo, reloading, alive and round state — but
**not the fire rate**. A client that calls it in a loop empties its magazine as
fast as it can send RPCs.

### 4. Shot direction and origin are client-supplied — CERTAIN, same question

The host resolves the trace, but from geometry the client hands it:

- `direction` is an RPC argument, unvalidated against where the client is looking.
- `origin` is `Owner.AimRay.Position`, derived from `EyeAngles`, which is plain
  `[Sync]` — owner-written.

Damage is host-*resolved* but client-*aimed*. The house rule says "client input
is a request, never a result... never trust a client for damage". Today the
client cannot set the damage number, but it fully determines who gets hit.

**3 and 4 need a decision, not a patch.** The spread comment says manipulating
your own spread is a designed feature of an HVH game, and that is coherent. But
there is a real difference between *"your spread is yours"* and *"your rate of
fire and your aim vector are unvalidated"*. The second is not a mod surface, it
is the absence of one. Pick deliberately:

- **Designed surface:** keep it, and say so explicitly in the mod docs, so nobody
  later "fixes" it as a security hole.
- **Authoritative:** clamp fire rate host-side and validate `direction` against
  the synced `EyeAngles` within a tolerance.

Doing nothing means the answer gets decided by whoever writes the next mod.

---

## Smaller things

- **Diagnostic counters are per-process.** `Weapon.FireRequests` and friends are
  `static`, so `hvh_report hits` counts the host's pawn *and all its bots* on the
  host, but only your own pawn on a client. The same command means two different
  things depending on where it runs. Label the output with the machine.
- **Host-only commands will silently no-op on a client.** `hvh_bots`,
  `hvh_dummies` and `hvh_sandbox` all act through host-gated systems. Given this
  project has already been bitten three times by commands that reported success
  while doing nothing, these should refuse and say "host only" rather than
  appear to work.
- **Client-local statics are fine.** `HitMarker`, `Crosshair.Instances`,
  `Player._local` and `GameSettings.Current` are per-process by nature and want
  to be.

---

## What cannot be known without two machines

- Whether `NetworkHelper` assigns pawn ownership the way the ownership model
  assumes. Everything above is built on it and none of it has been observed.
- Interpolation quality on remote pawns — whether shooting a moving remote player
  lands where it looks like it should.
- Whether per-shot broadcast traffic holds up. The effect RPC was sized small on
  purpose but has only ever run loopback.
- Join-in-progress: a client arriving mid-round, mid-reload, or while dead.
- Whether the bots' host-side simulation stays smooth with real clients attached.

## Suggested order

1. Round clock (2) and footsteps (1) — both certain, both contained, both make
   the first real session readable.
2. Decide 3 and 4 before anyone else writes a mod against the current behaviour.
3. Then run two clients and find out how much of the rest of this is wrong.
