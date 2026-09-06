import json, math, uuid, copy

ROOT = r"C:/Users/Anwender/Documents/s&box projects/hvh_testication/Assets/scenes/"
SRC = ROOT + "game.scene"
DST = ROOT + "compound.scene"

# ---- scale knob -----------------------------------------------------------
# The source layout implies ~150m x 100m. That is battle-royale sized; at this
# game's round length it would be mostly walking. Compressed to keep every zone
# and every route while making a rotation take seconds. Change these two
# numbers and rerun to rescale the entire map.
HALF_X = 900.0     # east-west half-extent
HALF_Y = 1600.0    # north-south half-extent (the long axis)

BOX = 50.0         # models/dev/box.vmdl is a 50-unit cube
PLANE = 100.0      # models/dev/plane.vmdl is 100 units per scale unit
WALL_H, WALL_T = 200.0, 24.0

GROUND_T = "0.35,0.38,0.42,1"
BLD_T = "0.50,0.48,0.44,1"
COVER_T = "0.55,0.50,0.42,1"
VEH_T = "0.30,0.32,0.34,1"
PANEL_T = "0.22,0.24,0.30,1"


def guid():
    return str(uuid.uuid4())


def quat_yaw(deg):
    r = math.radians(deg) / 2.0
    return "0,0,%.9f,%.9f" % (math.sin(r), math.cos(r))


def renderer(model, tint):
    return {
        "__type": "Sandbox.ModelRenderer", "__guid": guid(), "__enabled": True,
        "Flags": 0, "BodyGroups": 18446744073709551615, "CreateAttachments": False,
        "LodOverride": None, "MaterialGroup": None,
        "MaterialOverride": "materials/dev/reflectivity_50.vmat", "Materials": None,
        "Model": model, "OnComponentDestroy": None, "OnComponentDisabled": None,
        "OnComponentEnabled": None, "OnComponentFixedUpdate": None,
        "OnComponentStart": None, "OnComponentUpdate": None,
        "RenderOptions": {"GameLayer": True, "OverlayLayer": False,
                          "BloomLayer": False, "AfterUILayer": False},
        "RenderType": "On", "Tint": tint,
    }


def collider(scale="50,50,50", center="0,0,0", static=False):
    return {
        "__type": "Sandbox.BoxCollider", "__guid": guid(), "__enabled": True,
        "Flags": 0, "Center": center, "ColliderFlags": 0, "Elasticity": None,
        "Friction": None, "IsTrigger": False, "OnComponentDestroy": None,
        "OnComponentDisabled": None, "OnComponentEnabled": None,
        "OnComponentFixedUpdate": None, "OnComponentStart": None,
        "OnComponentUpdate": None, "OnTriggerEnter": None, "OnTriggerExit": None,
        "Scale": scale, "Static": static, "Surface": None, "SurfaceVelocity": "0,0,0",
    }


def obj(name, pos, rot, scale, tags, comps):
    return {
        "__guid": guid(), "__version": 2, "Flags": 0, "Name": name,
        "Position": pos, "Rotation": rot, "Scale": scale, "Tags": tags,
        "Enabled": True, "NetworkMode": 1, "NetworkFlags": 0,
        "NetworkOrphaned": 0, "NetworkTransmit": True, "OwnerTransfer": 0,
        "Components": comps,
    }


def block(name, cx, cy, size, tint, yaw=0.0, z=None):
    """A dev-box block. size is (sx, sy, sz) in world units; z is centre height."""
    sx, sy, sz = size
    cz = sz / 2.0 if z is None else z
    return obj(name,
               "%.2f,%.2f,%.2f" % (cx, cy, cz),
               quat_yaw(yaw),
               "%.6f,%.6f,%.6f" % (sx / BOX, sy / BOX, sz / BOX),
               "solid",
               [renderer("models/dev/box.vmdl", tint), collider()])


def wall_between(name, a, b, height=WALL_H, thick=WALL_T, tint="0.45,0.47,0.5,1"):
    """A wall spanning two points - used for the angled boundary."""
    (x1, y1), (x2, y2) = a, b
    cx, cy = (x1 + x2) / 2.0, (y1 + y2) / 2.0
    length = math.hypot(x2 - x1, y2 - y1)
    yaw = math.degrees(math.atan2(y2 - y1, x2 - x1))
    return block(name, cx, cy, (length, thick, height), tint, yaw=yaw)


def spawn(name, x, y, yaw):
    return obj(name, "%.2f,%.2f,16" % (x, y), quat_yaw(yaw), "1,1,1", "",
               [{"__type": "Sandbox.SpawnPoint", "__guid": guid(), "__enabled": True,
                 "Flags": 0, "Color": "0.2,0.8,1,1", "OnComponentDestroy": None,
                 "OnComponentDisabled": None, "OnComponentEnabled": None,
                 "OnComponentFixedUpdate": None, "OnComponentStart": None,
                 "OnComponentUpdate": None}])


objs = []

# ---- ground ---------------------------------------------------------------
objs.append(obj("Ground", "0,0,0", "0,0,0,1",
                "%.4f,%.4f,1" % ((HALF_X * 2) / PLANE, (HALF_Y * 2) / PLANE),
                "solid",
                [renderer("models/dev/plane.vmdl", GROUND_T),
                 collider(scale="%.0f,%.0f,50" % (HALF_X * 2, HALF_Y * 2),
                          center="0,0,-25", static=True)]))

# ---- boundary: the kite / elongated hexagon -------------------------------
NX = HALF_X * 0.42          # narrow north end
SX = HALF_X * 0.42          # narrow south end
WY_N = HALF_Y * 0.475       # widest point, north of centre
WY_S = -HALF_Y * 0.35       # widest point, south of centre
V = [(-NX, HALF_Y), (NX, HALF_Y), (HALF_X, WY_N), (HALF_X, WY_S),
     (SX, -HALF_Y), (-SX, -HALF_Y), (-HALF_X, WY_S), (-HALF_X, WY_N)]
EDGE = ["North Edge", "NE Diagonal", "East Edge", "SE Diagonal",
        "South Edge", "SW Diagonal", "West Edge", "NW Diagonal"]
for i, n in enumerate(EDGE):
    objs.append(wall_between("Boundary " + n, V[i], V[(i + 1) % len(V)]))

# ---- the Core: multi-room building dead centre ----------------------------
# This is the single most important piece: no corner of the map may see the
# opposite corner. Doorways on all four sides so it is fought through, not
# merely walked around.
CX, CY = 320.0, 260.0
DOOR = 90.0          # doorway half-width


def walled(name, axis, fixed, span, door_centre, tint=BLD_T):
    """
    A Core wall with ONE doorway, whose centre is given explicitly.

    Opposing doorways are deliberately offset from each other. With both the
    north and south doors on x=0 the building had a clean firing lane straight
    through it, which defeats the entire point of putting it in the middle.
    """
    lo, hi = -span, span
    segs = [(lo, door_centre - DOOR), (door_centre + DOOR, hi)]
    for i, (a, b) in enumerate(segs):
        if b - a < 20:
            continue
        mid, length = (a + b) / 2.0, (b - a)
        if axis == "x":     # wall runs along x at a fixed y
            objs.append(block("%s %d" % (name, i + 1), mid, fixed,
                              (length, WALL_T, WALL_H), tint))
        else:               # wall runs along y at a fixed x
            objs.append(block("%s %d" % (name, i + 1), fixed, mid,
                              (WALL_T, length, WALL_H), tint))


walled("Core N Wall", "x", CY, CX, -120)     # north door west of centre
walled("Core S Wall", "x", -CY, CX, 120)     # south door east of centre - offset
walled("Core E Wall", "y", CX, CY, 100)      # east door north of centre
walled("Core W Wall", "y", -CX, CY, -100)    # west door south of centre - offset

# Partitions sit off the doorway lines so the Core stays enterable, while still
# breaking the interior into rooms.
objs.append(block("Core Partition NE", 190, 120, (240, WALL_T, WALL_H), BLD_T))
objs.append(block("Core Partition SW", -190, -120, (240, WALL_T, WALL_H), BLD_T))


# ---- the two anchors: North Block and South Gate --------------------------
def anchor(prefix, ny, facing):
    hx, hy = 300.0, 150.0
    objs.append(block(prefix + " Back Wall", 0, ny + facing * hy,
                      (hx * 2, WALL_T, WALL_H), BLD_T))
    objs.append(block(prefix + " East Wall", hx, ny, (WALL_T, hy * 2, WALL_H), BLD_T))
    objs.append(block(prefix + " West Wall", -hx, ny, (WALL_T, hy * 2, WALL_H), BLD_T))
    s = hx - 110
    for side in (-1, 1):
        objs.append(block(prefix + " Front " + ("W" if side < 0 else "E"),
                          side * (110 + s / 2), ny - facing * hy,
                          (s, WALL_T, WALL_H), BLD_T))


anchor("North Block", HALF_Y * 0.72, 1)
anchor("South Gate", -HALF_Y * 0.72, -1)

# ---- the Ring: sparse cover, fast but exposed -----------------------------
RING = [(-560, 470, 200, 70, 128, 20), (560, 430, 200, 70, 128, -25),
        (-500, -420, 190, 70, 128, 35), (520, -450, 190, 70, 128, 15),
        (-210, 640, 200, 70, 96, 15), (210, -660, 200, 70, 96, -15),
        (-660, 0, 70, 220, 128, 0), (660, 0, 70, 220, 128, 0),
        (-330, 780, 160, 70, 96, 40), (350, -800, 160, 70, 96, -40)]
for i, (x, y, sx, sy, sz, yaw) in enumerate(RING):
    objs.append(block("Ring Cover %d" % (i + 1), x, y, (sx, sy, sz), COVER_T, yaw=yaw))

# ---- the Yards: denser and slower on the flanks ---------------------------
YARD = [(-780, 900, 150, 90, 128, 25), (-720, 640, 120, 120, 96, 0),
        (780, 880, 150, 90, 128, -20), (720, 620, 120, 120, 96, 0),
        (-770, -760, 150, 90, 128, -30), (-700, -520, 120, 120, 96, 0),
        (760, -740, 150, 90, 128, 30), (700, -500, 120, 120, 96, 0)]
for i, (x, y, sx, sy, sz, yaw) in enumerate(YARD):
    objs.append(block("Yard Cover %d" % (i + 1), x, y, (sx, sy, sz), COVER_T, yaw=yaw))

# ---- panel arrays: break the diagonals asymmetrically ---------------------
objs.append(block("Panel Array NE", 620, 760, (230, 40, 190), PANEL_T, yaw=-35))
objs.append(block("Panel Array W", -690, 180, (40, 260, 190), PANEL_T, yaw=10))

# ---- motor pool: chest-high, irregular, partial blockers ------------------
VEH = [(-430, 980, 210, 90, 68, 15), (-250, 1120, 210, 90, 68, -10),
       (300, 1050, 210, 90, 68, 25), (-380, -1000, 210, 90, 68, -20),
       (250, -1120, 210, 90, 68, 12), (470, -930, 210, 90, 68, -35)]
for i, (x, y, sx, sy, sz, yaw) in enumerate(VEH):
    objs.append(block("Vehicle %d" % (i + 1), x, y, (sx, sy, sz), VEH_T, yaw=yaw))

# ---- deathmatch spawns: distributed, not two team anchors -----------------
SP = [(0, 1380, 180), (-520, 1050, 200), (520, 1020, 160),
      (-760, 300, 90), (760, 260, 270),
      (-520, -980, 20), (520, -1000, 340), (0, -1380, 0),
      (-700, 760, 135), (700, -700, 315)]
for i, (x, y, yaw) in enumerate(SP):
    objs.append(spawn("Spawn %d" % (i + 1), x, y, yaw))

# ---- carry the game systems over from game.scene --------------------------
src = json.load(open(SRC, encoding="utf-8"))
KEEP = {"Scene Information", "Sun", "Skybox", "Fallback Camera", "Game", "HUD"}
carried = [copy.deepcopy(o) for o in src["GameObjects"] if o.get("Name") in KEEP]

out = copy.deepcopy(src)
out["__guid"] = guid()
out["GameObjects"] = carried + objs
out["Title"] = "Compound"
out["Description"] = "Deathmatch greybox - kite layout, multi-room Core, distributed spawns."
json.dump(out, open(DST, "w", encoding="utf-8"), indent=2)

print("carried over:", [o["Name"] for o in carried])
print("wrote", DST)
print("objects: %d (%d new)" % (len(out["GameObjects"]), len(objs)))
print("footprint: %.0f x %.0f units (%.0fm x %.0fm)"
      % (HALF_X * 2, HALF_Y * 2, HALF_X * 2 / 39.37, HALF_Y * 2 / 39.37))
print("spawns: %d" % len(SP))
