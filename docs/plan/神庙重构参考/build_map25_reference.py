"""Build only Map2.5's coordinate-preserving whitebox manifest from Dungeon054.

Source actor SRT/bounds are retained. Four shell-floor rectangles are explicit
approximations because the original hollow room mesh is not available.
"""
from pathlib import Path
from collections import Counter
import json
import math

from build_reference_layouts import classify, vec

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
DEST = ROOT / 'UnityProject/PAO/Assets/GameMain/Data/ShrineReferences'


def map25_kind(name):
    if name.startswith('Enemy_'):
        return 'omit', 'Enemy'
    if name.startswith('DgnObj_PartitionWall_'):
        return 'box', 'PartitionWalls'
    if name.startswith('DgnObj_Pillar_'):
        return 'cylinder', 'Detail'
    if name in ('DgnObj_BoxIron_Secret', 'DgnObj_BoxStone_A'):
        return 'box', 'Mechanisms'
    if name == 'DgnObj_DoorIronRail_A':
        return 'box', 'Detail'
    return classify(name)


def floor(name, x0, x1, z0, z1):
    return {
        'name': name,
        'position': vec([(x0+x1)/2, -.2, (z0+z1)/2]),
        'size': vec([x1-x0, .4, z1-z0]),
        'note': 'Approximation of Dungeon054 hollow room floor, clipped to wall enclosure. Top Y=0; not original mesh bounds.',
    }


def main():
    actors = {x['name']: x for x in json.loads((HERE/'ActorInfo.product.json').read_text(encoding='utf-8-sig'))['Actors']}
    raw = []
    for kind in ('Static', 'Dynamic'):
        raw += json.loads((HERE/f'Dungeon054_{kind}.json').read_text(encoding='utf-8-sig'))['Objs']
    output, skipped = [], []
    for obj in raw:
        name = obj['UnitConfigName']
        kind, group = map25_kind(name)
        meta = actors.get(name)
        if kind == 'omit' or not meta or 'aabbMin' not in meta:
            skipped.append({'name': name, 'hash': str(obj['HashId']), 'reason': group if kind == 'omit' else 'no bounds'})
            continue
        rotation = vec(obj.get('Rotate', [0, 0, 0]), True)
        note = 'Original Dungeon054 SRT and model bounds; simplified whitebox geometry, original gameplay not imported.'
        if group == 'PartitionWalls':
            note += ' Full source wall height retained. Hide the PartitionWalls group in Scene view for an open cutaway.'
        if 'DoorIron' in name and name != 'DgnObj_DoorIronRail_A':
            note += ' Door initial pose only. Model origin is not the hinge pivot; hinge and magnetic manipulation are not implemented.'
        if obj['HashId'] == 3331225445:
            note += ' Reusable detached-door source: hinge bound to crack block 298541694; one door, not multiple permanent bridges.'
        if kind in ('altar', 'stairs', 'slope', 'cylinder'):
            note += ' Interior profile is a proxy, not the original mesh.'
        output.append({
            'name': name, 'hash': str(obj['HashId']), 'kind': kind, 'group': group,
            'position': vec(obj['Translate']),
            'rotation': {k: v*180/math.pi for k, v in rotation.items()},
            'scale': vec(obj.get('Scale', [1, 1, 1])),
            'boundsMin': {k.lower(): v for k, v in meta['aabbMin'].items()},
            'boundsMax': {k.lower(): v for k, v in meta['aabbMax'].items()},
            'solid': group != 'Detail', 'note': note,
            'endpoint': vec(obj['Translate']), 'motion': '',
            'motionEnd': vec(obj['Translate']), 'motionSpeed': 0,
        })
    layout = {
        'schemaVersion': 2, 'sceneName': 'Map2_5', 'sourceMap': 'Dungeon054',
        'title': '卡奥·玛卡 / 连接道路之物',
        'sourceUrl': 'https://github.com/zeldamods/radar/tree/master/content/map/CDungeon/Dungeon054',
        'coordinateConvention': 'Original XYZ, 1 source unit = 1 Unity unit; general progress towards -Z.',
        'entry': vec([0, 0, 84.5]), 'goal': vec([12, 11.5, -23.5]),
        'spawnAnchor': 'Map2_5_Start', 'goalAnchor': 'Map2_5_Goal',
        'sourceCount': len(raw), 'objects': output, 'skipped': skipped,
        'supportGeometry': [
            floor('S00_Entrance_Floor_Proxy', -18, 18, 56.5, 94.5),
            floor('S01_Main_Hall_Floor_Proxy', -34, 34, -8.5, 56.5),
            floor('S02_Side_Room_Floor_Proxy', -58, -34, 6, 46),
            floor('S03_Altar_Hall_Floor_Proxy', -10, 34, -46, -8.5),
        ],
        'limitations': [
            'No source meshes/textures. Original actors retain SRT and bounds; proxy interior profiles are simplified.',
            'Original shell and ceiling omitted; four continuous floor rectangles inferred from source enclosure at Y=0.',
            'No enemies; doors, crack blocks and movable blocks are geometry-only proxies.',
            'No permanent bridge poses are inserted. Reuse of the movable door is illustrated in the separate flow diagram.',
        ],
    }
    DEST.mkdir(parents=True, exist_ok=True)
    (DEST/'Map2_5_ReferenceLayout.json').write_text(json.dumps(layout, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps({'source': len(raw), 'proxies': len(output), 'omitted': len(skipped), 'groups': dict(Counter(x['group'] for x in output))}, ensure_ascii=False))


if __name__ == '__main__':
    main()
