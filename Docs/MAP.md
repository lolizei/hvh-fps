# Map Layout Reference — "Compound"

**Status:** reference only. Not built, not scheduled. Recorded 2026-09-01 from a top-down layout the user supplied.

This describes the *structure* we want — a layout archetype. Geometry, proportions,
naming and art are ours; nothing is traced or copied from another game's map.

---

## Shape

Vertically elongated, roughly a kite/hexagon. Wider through the middle, tapering to a
point at the north and south ends. Longer on the north–south axis than east–west, so
the map has a clear long axis to fight along and short flanks to cut across.

```
                  ╭─────────╮
                 ╱  NORTH    ╲          north building
                ╱   BLOCK     ╲
               │                │
              ╱                  ╲
             │    ▢   ▢    ▢      │     ring cover
             │      ┌───────┐     │
             │  ▢   │ CORE  │  ▢  │     central building
             │      │       │     │
             │      └───────┘     │
             │   ▢     ▢     ▢    │
              ╲                  ╱
               │                │
                ╲   SOUTH      ╱
                 ╲   GATE     ╱          south structure
                  ╰─────────╯
```

## Zones

| Zone | What it is | Role |
|---|---|---|
| **Core** | Large multi-room building dead centre, enclosed, internal walls and doorways | The contested prize. Breaks every long sightline across the map. |
| **The Ring** | Open courtyard encircling the Core | Rotation space. Fast but exposed. |
| **North Block** | Building complex at the north point | One team's anchor. Elevated or enclosed entry onto the Ring. |
| **South Gate** | Covered structure / gateway at the south point | Mirror anchor for the other side. |
| **East & West Yards** | The wider flanks either side of the Core | Flanking routes, denser cover, slower. |
| **Panel Arrays** | Two grid structures, roughly NE and W | Hard cover blocks that break diagonals asymmetrically. |
| **Motor Pool** | A handful of vehicles, clustered NW / W / SW | Chest-high cover, irregular shapes, partial sightline blockers. |
| **Treeline** | Dense vegetation around the whole perimeter | Soft boundary. |
| **Outfields** | Greenhouse/field structures beyond the treeline, east and west | Out of bounds. Scenery only — depth, never enterable. |

## Design intent

- **The Core blocks the middle.** No corner of the map can see the opposite corner. This is
  the single most important property and the thing our current arena gets wrong — its
  Centre Platform blocks *some* diagonals inconsistently, which is why the target dummies
  had to be traced into place.
- **Two anchors, one axis.** North Block and South Gate face each other down the long axis.
  Spawns go here, one per team.
- **Three routes between anchors:** through the Core, or around it east, or around it west.
  A team that commits to one can be flanked from the other two.
- **Cover density rises at the edges.** The Ring is open and dangerous; the Yards are
  cluttered and slow. Rewards choosing between speed and safety.
- **Roughly rotationally symmetric, not mirrored.** Balanced sides without being a
  copy-paste, so each half still reads as its own place.

## Scale (proposed, needs a decision)

The source layout implies about **150 m × 100 m**. In s&box units that is roughly
**5,900 × 3,900** — far larger than our current 1,200 × 1,200 test arena.

That is a battle-royale-sized footprint, not a competitive-shooter one. For 5v5 rounds
of ~90 seconds it would be mostly empty walking. Two options:

- **Compress to ~3,000 × 2,200 units** (~75 m × 55 m) — keeps every zone and every route,
  but rotations take seconds instead of half a minute. **Recommended.**
- **Build full size** and raise player count and round length to match.

Decide this before any geometry is placed; it changes every dimension downstream.

## What we already have that carries over

| Existing | Reuse |
|---|---|
| `TeamSpawnPoint` | Spawns at North Block and South Gate, already team-aware |
| `SpawnSystem` | Already prefers team spawns, then neutral, then anything |
| Dev-textured box geometry | Fine for a greybox pass — art comes later |
| `scene_trace` sightline probing | The method used to place the dummies; use it to verify the Core actually blocks the diagonals |

## Build order, when this becomes a goal

1. Greybox the boundary and the Core shell at the chosen scale — playable, ugly.
2. North Block and South Gate with team spawns; verify a full rotation on foot feels right.
3. The Ring's cover pass; verify no cross-map sightline survives.
4. East/West Yards, Panel Arrays, Motor Pool.
5. Treeline and out-of-bounds Outfields.
6. Art pass — last.

## Open questions

- Scale: compress or full size?
- Player count this map is balanced for?
- Is the Core enterable on one floor or two? A second storey changes every sightline.
- Does the round mode stay elimination, or does this map want an objective?
