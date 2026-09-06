"""
Generates the project's test assets as prefabs composed from engine primitives.

The audit found the project owns 5 asset files and no models at all, and engine
content has zero weapon models. So these are built from models/dev/box.vmdl -
original geometry, described here as numbers rather than authored in a modelling
tool. Regenerate by rerunning; nothing is hand-edited in the editor.

Local axes in s&box: +X forward, +Y left, +Z up.
"""
import json, math, uuid, os

ROOT = r"C:/Users/Anwender/Documents/s&box projects/hvh_testication/Assets/"
BOX = 50.0                      # models/dev/box.vmdl is a 50-unit cube
MAT = "materials/dev/reflectivity_30.vmat"


def guid():
    return str(uuid.uuid4())


def quat(yaw=0.0, pitch=0.0, roll=0.0):
    cy, sy = math.cos(math.radians(yaw) / 2), math.sin(math.radians(yaw) / 2)
    cp, sp = math.cos(math.radians(pitch) / 2), math.sin(math.radians(pitch) / 2)
    cr, sr = math.cos(math.radians(roll) / 2), math.sin(math.radians(roll) / 2)
    return "%.6f,%.6f,%.6f,%.6f" % (
        sr * cp * cy - cr * sp * sy,
        cr * sp * cy + sr * cp * sy,
        cr * cp * sy - sr * sp * cy,
        cr * cp * cy + sr * sp * sy,
    )


def renderer(tint):
    return {
        "__type": "Sandbox.ModelRenderer", "__guid": guid(), "__enabled": True,
        "Flags": 0, "BodyGroups": 18446744073709551615, "CreateAttachments": False,
        "LodOverride": None, "MaterialGroup": None, "MaterialOverride": MAT,
        "Materials": None, "Model": "models/dev/box.vmdl",
        "OnComponentDestroy": None, "OnComponentDisabled": None,
        "OnComponentEnabled": None, "OnComponentFixedUpdate": None,
        "OnComponentStart": None, "OnComponentUpdate": None,
        "RenderOptions": {"GameLayer": True, "OverlayLayer": False,
                          "BloomLayer": False, "AfterUILayer": False},
        "RenderType": "On", "Tint": tint,
    }


def part(name, pos, size, tint, rot=(0.0, 0.0, 0.0)):
    """One box of a composed model. No collider - presentation only."""
    return {
        "__guid": guid(), "__version": 2, "Flags": 0, "Name": name,
        "Position": "%.3f,%.3f,%.3f" % pos,
        "Rotation": quat(*rot),
        "Scale": "%.5f,%.5f,%.5f" % (size[0] / BOX, size[1] / BOX, size[2] / BOX),
        "Tags": "", "Enabled": True, "NetworkMode": 0, "NetworkFlags": 0,
        "NetworkOrphaned": 0, "NetworkTransmit": True, "OwnerTransfer": 0,
        "Components": [renderer(tint)], "Children": [],
    }


def write_prefab(path, name, children):
    doc = {
        "__guid": guid(),
        "RootObject": {
            "__guid": guid(), "__version": 2, "Flags": 0, "Name": name,
            "Position": "0,0,0", "Rotation": "0,0,0,1", "Scale": "1,1,1",
            "Tags": "", "Enabled": True, "NetworkMode": 0, "NetworkFlags": 0,
            "NetworkOrphaned": 0, "NetworkTransmit": True, "OwnerTransfer": 0,
            "Components": [], "Children": children,
        },
        "ShowInMenu": False, "MenuPath": None, "MenuIcon": None,
        "DontBreakLinks": False, "ResourceVersion": 2, "Title": name,
        "Description": None, "__references": [], "__version": 2,
    }
    os.makedirs(os.path.dirname(path), exist_ok=True)
    json.dump(doc, open(path, "w", encoding="utf-8"), indent=2)
    return path


# ---------------------------------------------------------------- weapons ---
# A shared design language: dark polymer bodies, lighter metal barrels, a warm
# accent on magazines so each gun reads instantly in the corner of the eye.
BODY = "0.13,0.14,0.16,1"
METAL = "0.38,0.40,0.44,1"
GRIP = "0.09,0.09,0.10,1"
ACCENT_RIFLE = "0.55,0.42,0.20,1"
ACCENT_PISTOL = "0.22,0.42,0.55,1"
ACCENT_SMG = "0.45,0.25,0.45,1"
ACCENT_SNIPER = "0.25,0.45,0.30,1"
GLASS = "0.15,0.35,0.45,1"

WEAPONS = {
    # long body, stock, magazine, sight - the silhouette of a full-size rifle
    "vk7_rifle": [
        ("Receiver", (0, 0, 0), (22, 3.0, 5.0), BODY),
        ("Handguard", (13, 0, 0.6), (10, 2.6, 3.2), BODY),
        ("Barrel", (21, 0, 0.8), (10, 1.4, 1.4), METAL),
        ("Stock", (-13, 0, -0.4), (9, 2.6, 4.2), BODY),
        ("Cheek Rest", (-10, 0, 2.2), (7, 2.2, 1.4), GRIP),
        ("Magazine", (1.5, 0, -5.2), (3.4, 2.4, 7.0), ACCENT_RIFLE),
        ("Grip", (-4.5, 0, -4.4), (2.6, 2.4, 5.4), GRIP),
        ("Rear Sight", (-6, 0, 3.4), (2.0, 1.2, 1.8), METAL),
        ("Front Sight", (17, 0, 3.0), (1.4, 1.0, 1.6), METAL),
    ],
    # compact: slide over a grip, nothing else
    "m9_pistol": [
        ("Slide", (0, 0, 0), (10, 2.4, 3.0), BODY),
        ("Barrel", (5.6, 0, 0), (2.4, 1.3, 1.3), METAL),
        ("Frame", (-1.2, 0, -2.4), (7.5, 2.2, 2.0), BODY),
        ("Grip", (-3.0, 0, -5.6), (3.0, 2.2, 6.4), GRIP),
        ("Magazine Base", (-3.0, 0, -9.2), (3.2, 2.4, 1.2), ACCENT_PISTOL),
        ("Sight", (-4.2, 0, 2.0), (1.4, 1.0, 1.2), METAL),
    ],
    # short and stubby, magazine forward of the grip
    "pk9_smg": [
        ("Receiver", (0, 0, 0), (14, 3.0, 5.0), BODY),
        ("Barrel", (9.5, 0, 0.4), (7, 1.4, 1.4), METAL),
        ("Folding Stock", (-9.5, 0, 0.4), (6, 1.8, 2.2), METAL),
        ("Magazine", (0.5, 0, -5.6), (2.8, 2.2, 8.0), ACCENT_SMG),
        ("Grip", (-4.0, 0, -4.2), (2.6, 2.2, 5.0), GRIP),
        ("Sight", (-3.0, 0, 3.2), (1.6, 1.0, 1.4), METAL),
    ],
    # very long barrel and a scope sitting proud of the receiver
    "lr40_sniper": [
        ("Receiver", (0, 0, 0), (24, 3.2, 5.0), BODY),
        ("Barrel", (24, 0, 0.6), (22, 1.6, 1.6), METAL),
        ("Muzzle Brake", (35, 0, 0.6), (3.0, 2.4, 2.4), METAL),
        ("Stock", (-16, 0, -0.6), (11, 3.0, 5.2), BODY),
        ("Cheek Rest", (-13, 0, 2.6), (8, 2.4, 1.6), GRIP),
        ("Scope Body", (2, 0, 5.6), (11, 2.6, 2.6), METAL),
        ("Scope Lens", (7.8, 0, 5.6), (0.6, 2.2, 2.2), GLASS),
        ("Scope Mount F", (5.5, 0, 3.6), (1.4, 1.6, 2.0), METAL),
        ("Scope Mount R", (-1.5, 0, 3.6), (1.4, 1.6, 2.0), METAL),
        ("Magazine", (2.0, 0, -4.6), (2.8, 2.2, 5.0), ACCENT_SNIPER),
        ("Grip", (-6.5, 0, -4.4), (2.6, 2.2, 5.4), GRIP),
        ("Bipod", (20, 0, -3.0), (1.2, 5.0, 4.0), GRIP),
    ],
}

made = []
for key, parts in WEAPONS.items():
    kids = [part(n, p, s, t) for n, p, s, t in parts]
    made.append(write_prefab(ROOT + "prefabs/weapons/%s.prefab" % key, key, kids))

# ---------------------------------------------------------------- players ---
# A blocky humanoid, not the engine Citizen. The Citizen exists and looks far
# better, but driving its animation graph is exactly the "advanced animation"
# this goal rules out, and a static Citizen would stand in its bind pose - worse
# to read than honest boxes. Recorded as an upgrade path instead.
#
# Built around the existing 72-unit collider so nothing about hit zones moves:
# legs 0-32, torso 32-58, head 58-70.
def humanoid(team_tint, kit_tint):
    return [
        part("Head", (0, 0, 64), (9, 9, 10), team_tint),
        part("Visor", (3.6, 0, 65), (1.6, 7.0, 3.0), "0.10,0.12,0.14,1"),
        part("Torso", (0, 0, 45), (10, 15, 26), team_tint),
        part("Chest Rig", (2.0, 0, 47), (3.0, 13.0, 14.0), kit_tint),
        part("Backpack", (-5.5, 0, 46), (4.0, 11.0, 14.0), kit_tint),
        part("Shoulder L", (0, 8.5, 54), (7, 4, 7), kit_tint),
        part("Shoulder R", (0, -8.5, 54), (7, 4, 7), kit_tint),
        part("Arm L", (0, 8.6, 42), (6, 4.5, 18), team_tint),
        part("Arm R", (0, -8.6, 42), (6, 4.5, 18), team_tint),
        part("Leg L", (0, 3.6, 16), (7, 6.5, 32), "0.16,0.16,0.18,1"),
        part("Leg R", (0, -3.6, 16), (7, 6.5, 32), "0.16,0.16,0.18,1"),
        part("Boot L", (1.0, 3.6, 2.5), (9, 6.5, 5), "0.08,0.08,0.09,1"),
        part("Boot R", (1.0, -3.6, 2.5), (9, 6.5, 5), "0.08,0.08,0.09,1"),
    ]


# Vanguard: cool blue-grey, high-vis shoulders. Syndicate: warm sand, dark kit.
# Distinguishable by hue AND by kit contrast, so it survives colourblindness.
made.append(write_prefab(ROOT + "prefabs/players/player_vanguard.prefab",
                         "player_vanguard", humanoid("0.20,0.34,0.52,1", "0.55,0.62,0.70,1")))
made.append(write_prefab(ROOT + "prefabs/players/player_syndicate.prefab",
                         "player_syndicate", humanoid("0.55,0.42,0.24,1", "0.24,0.20,0.16,1")))

for m in made:
    print("wrote", m.replace(ROOT, "Assets/"))
print("%d prefabs" % len(made))
