# Goal: Two Real Clients

**Status:** active
**Set:** 2026-09-06

*Previous goal "Every Shot Has Weight" shipped and is archived at
`Docs/goals/2026-09-06-every-shot-has-weight.md` — 7 of 8 done, the eighth
blocked on a gunshot sound that does not exist in engine content.*

## Objective
Two people on two machines play this game together, and the netcode assumptions
the whole project rests on are observed rather than believed.

## Why this one
Nothing here has ever run with a second machine. That is not one gap, it is the
foundation under every other claim:

- The **ownership model** — `IsProxy`, `IsLocallyControlled`, `IsSimulatedHere` —
  decides who simulates, who aims, who sees a hit marker. It has only ever been
  exercised where host and client are the same process.
- **Two fixes shipped blind.** The round clock and remote-player footsteps were
  both provably broken by reading, and both fixes are unverifiable in a
  single-player session by definition. They are currently faith.
- **The HVH framework is inherently multiplayer.** Testing cheats against
  yourself is close to meaningless, and that framework is the project's premise.

## The blocker this goal starts from
**There is no way to join a game.** The only networking call in the entire
codebase is `Networking.Disconnect()`. `PLAY` loads the gameplay scene, and
`NetworkHelper.StartServer = true` means every instance creates its own lobby —
two people pressing PLAY get two separate sessions and never see each other.

So task 1 is not testing. It is building a join path that does not exist.

The API is there (verified by reflection, not assumed):

```
Networking.CreateLobby( LobbyConfig )     // host
Networking.JoinBestLobby( string ident )  // simplest possible join
Networking.QueryLobbies( ... )            // a real browser, later
Networking.IsActive / IsHost / IsClient / Connections
```

## Definition of Done
- [ ] Two instances, one hosting and one joining, see each other's pawns move
- [ ] Ownership observed on both machines: each player controls exactly their own
      pawn, and neither can drive the other's
- [ ] The round clock reads the same on host and client
- [ ] A client hears the other player's footsteps
- [ ] A shot fired on one machine registers on the other, on a moving target
- [ ] Hit markers appear only for the shooter
- [ ] A client that joins mid-round ends up in a sane state
- [ ] A client that disconnects does not break the host

## Non-Goals
- A polished server browser. `JoinBestLobby` is enough to learn what we need.
- Matchmaking, lobbies with settings, passwords, regions.
- Lag compensation or rollback. **Measure** what the latency does; do not build
  netcode features to hide it.
- Dedicated servers.
- Fixing the fire-rate and aim-vector authority question — that is a separate
  design decision, though this goal will make its consequences visible.
- HVH features, viewmodel, map polish, weapon balance.

## Constraints
- **Host-authoritative stays.** Nothing in this goal may move authority to a
  client to make a test pass.
- Do not change gameplay to suit the network. If something plays badly with two
  players, report it; do not tune it inside this goal.
- The existing single-player and bot paths must keep working. Every runtime
  check we already rely on — cadence, hit markers, one-hit-one-marker, the map's
  sightline suite — must still pass on the host.
- Engine content only. No `PackageReference`.

## The hard dependency, stated plainly
**I cannot complete this goal alone.** I can build the host/join path and drive
one machine through the editor. A second real client has to come from you — a
second PC, or a friend from the repo. Steam generally refuses two simultaneous
instances of the same game on one account, so a second machine is the likely
path, and that is worth checking early rather than at task 4.

If a second machine turns out to be impossible, say so and this goal should be
replaced rather than faked.

## Tasks
1. [ ] **A join path.** HOST and JOIN in the main menu. Host creates a lobby;
       join uses `JoinBestLobby`. Make sure `NetworkHelper.StartServer` does not
       stomp a session that has joined one. Evidence: two instances, second sees
       the first's pawn.
2. [ ] **Ownership, observed.** On both machines, report every pawn's `IsProxy`,
       `IsLocallyControlled`, `IsSimulatedHere` and owner. Confirm exactly one
       pawn per machine is locally controlled, and inputs never cross.
3. [ ] **The two blind fixes.** Round clock equal on both machines; the other
       player's footsteps audible on a client.
4. [ ] **Shooting across the wire.** Hits register on a moving remote target;
       markers stay private to the shooter; effects appear on both machines.
5. [ ] **Churn.** Join mid-round, join while dead, join mid-reload, disconnect,
       host leaves.
6. [ ] **Load.** Two humans plus bots, sustained fire; measure per-shot traffic
       and object counts on both machines.

## Verification
Every task reports what was observed **on each machine separately**. A result
from the host alone is not a result — that is exactly the trap this goal exists
to escape.

## Risks
- **`hvh_*` diagnostics are host-centric.** `hvh_bots`, `hvh_dummies` and
  `hvh_sandbox` all act through host-gated systems and will silently do nothing
  on a client. They need to refuse honestly before they are trusted in a
  two-machine session — this project has already been bitten three times by
  commands reporting success while doing nothing.
- **Diagnostic counters are `static` and per-process.** `hvh_report hits` counts
  the host's pawn plus all its bots on the host, but only your own pawn on a
  client. The same command means two different things depending on where it runs.
- The editor has dropped out mid-session repeatedly. A two-machine session is
  more fragile still.
- Interpolation quality on remote pawns is completely unknown, and it decides
  whether shooting a moving player feels fair.

## Blockers
- None yet. The second machine is a dependency, not a blocker, until it is.
