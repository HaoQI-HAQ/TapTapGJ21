"""Convert public BOTW placement/bounds metadata to explicit Unity blockout manifests.

No game meshes, textures or scripts are imported. Coordinates remain in source units.
"""
from pathlib import Path
import json
import math
from collections import Counter

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
DEST = ROOT / "UnityProject/PAO/Assets/GameMain/Data/ShrineReferences"


def vec(value, rotation=False):
    if isinstance(value, (int, float)):
        value = [0, value, 0] if rotation else [value] * 3
    return dict(zip("xyz", map(float, value)))


def classify(name):
    if name.startswith(("DgnMrgPrt", "Link", "Event", "Area", "Constraint", "Look", "Player", "Dungeon", "Destination", "Bottom", "Npc_", "SwitchTimer")):
        return "omit", "Logic"
    if any(s in name for s in ("BaseRoom", "CeilingPillar", "GoalShield", "EntranceElevator", "GoalExit")):
        return "omit", "Shell"
    if "GoalSystem_A_" in name:
        return "altar", "Landmarks"
    if "GoalSystem" in name:
        return "omit", "Landmarks"
    if name in ("Rope", "RopeWire"):
        return "rope", "Mechanisms"
    if "Ladder" in name:
        return "ladder", "Mechanisms"
    if "BoxShutterBase" in name:
        return "pistonBase", "Mechanisms"
    if "BoxShutter_" in name:
        return "piston", "Mechanisms"
    if "BombPipe" in name:
        return "channel", "Mechanisms"
    if "SpikeBall" in name:
        return "spikes", "Mechanisms"
    if "HangedLamp" in name:
        return "lamp", "Mechanisms"
    if "Slope" in name:
        return "slope", "Structure"
    if "Stair" in name:
        return "stairs", "Structure"
    if "Hole_" in name:
        return "frame", "Structure"
    if "Warpin" in name:
        return "cylinder", "Landmarks"
    if "Crack" in name:
        return "box", "Breakable"
    if "TBox_" in name or "Terminal" in name or name == "SwitchHit":
        return "box", "Landmarks"
    if name == "AncientBall":
        return "sphere", "Mechanisms"
    if any(s in name for s in ("Board", "RailLift", "SliderBlock", "DoorIron", "IronDoor")):
        return "box", "Mechanisms"
    if any(s in name for s in ("WallMark", "Candle", "Arch", "Wainscot", "EyeBolt", "SliderRail", "BoxCover", "Road")):
        return "box", "Detail"
    if name.startswith(("DgnObj_Hrl_", "DgnObj_hrl_")):
        return "box", "Structure"
    return "omit", "Unknown"


def main():
    DEST.mkdir(parents=True, exist_ok=True)
    info = json.loads((HERE / "ActorInfo.product.json").read_text(encoding="utf-8-sig"))
    actors = {a["name"]: a for a in info["Actors"]}
    used = {}
    for number, scene, title, start, goal in [
        ("041", "Map2", "加·巴希夫 / 破坏之力", [-18, 2, 25], [8, 8, -111.5]),
        ("058", "Map3", "摩·拉塔尼阿 / 配合摇晃", [0, -16, 60], [0, 0, -97.5]),
    ]:
        static = json.loads((HERE / f"Dungeon{number}_Static.json").read_text(encoding="utf-8-sig"))
        dynamic = json.loads((HERE / f"Dungeon{number}_Dynamic.json").read_text(encoding="utf-8-sig"))
        raw = static["Objs"] + dynamic["Objs"]
        by_id = {x["HashId"]: x for x in raw}
        output, skipped = [], []
        for obj in raw:
            name = obj["UnitConfigName"]
            kind, group = classify(name)
            meta = actors.get(name)
            if kind == "omit" or not meta or "aabbMin" not in meta:
                skipped.append({"name": name, "hash": str(obj["HashId"]), "reason": "non-geometric/cutaway/complex mesh" if kind == "omit" else "no bounds"})
                continue
            used[name] = meta
            rot = vec(obj.get("Rotate", [0, 0, 0]), True)
            lo = {k.lower(): v for k, v in meta["aabbMin"].items()}
            hi = {k.lower(): v for k, v in meta["aabbMax"].items()}
            scale = vec(obj.get("Scale", [1, 1, 1]))
            entry = {
                "name": name, "hash": str(obj["HashId"]), "kind": kind, "group": group,
                "position": vec(obj["Translate"]),
                "rotation": {k: v * 180 / math.pi for k, v in rot.items()},
                "scale": scale, "boundsMin": lo, "boundsMax": hi,
                "solid": group != "Detail", "endpoint": vec(obj["Translate"]),
                "note": "Source SRT + ActorInfo model bounds; simplified blockout mesh, not original mesh.",
                "motion": "", "motionEnd": vec(obj["Translate"]), "motionSpeed": 0,
            }
            if kind == "rope":
                references = [x for x in obj.get("LinksToObj", []) if x["DefinitionName"] == "Reference"]
                end = by_id.get(references[0]["DestUnitHashId"]) if references else None
                entry["endpoint"] = vec(end["Translate"]) if end else vec([obj["Translate"][0], obj["Translate"][1] - scale["y"], obj["Translate"][2]])
                entry["solid"] = False
                entry["note"] = "Rope endpoints from source Reference link; Scale.Y is rope length, not an 80/100m scaled mesh."
            if "RailLift" in name:
                entry["motion"] = "railPreview"
                entry["motionEnd"] = vec([-9, 3.5, -28])
                entry["motionSpeed"] = 1.5
                entry["note"] += " Source rail centers X=-19..-9,Y=4,Z=-28; actor sits 0.5 below rail. PAO motion preview disabled by default; speed is an adaptation."
            if kind in ("slope", "stairs", "frame", "channel", "altar", "pistonBase", "spikes", "lamp"):
                entry["note"] += " Internal profile is a proxy; AABB does not encode cavities or exact silhouette."
            output.append(entry)
        payload = {
            "schemaVersion": 1, "sceneName": scene, "sourceMap": f"Dungeon{number}", "title": title,
            "coordinateConvention": "Source XYZ preserved, scale 1.0; -Z is the general direction of progress.",
            "sourceUrl": f"https://github.com/zeldamods/radar/tree/master/content/map/CDungeon/Dungeon{number}",
            "entry": vec(start), "goal": vec(goal), "sourceCount": len(raw),
            "objects": output, "skipped": skipped,
        }
        (DEST / f"{scene}_ReferenceLayout.json").write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")
        print(scene, "source", len(raw), "proxies", len(output), "omitted", len(skipped), dict(Counter(x["kind"] for x in output)))
    (HERE / "ActorBounds_used.json").write_text(json.dumps(used, ensure_ascii=False, indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()
