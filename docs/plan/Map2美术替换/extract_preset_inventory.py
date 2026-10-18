"""Read-only GLB inventory. Writes documentation, never source assets or Unity scenes."""
import collections
import itertools
import json
import math
from pathlib import Path
import re
import struct

OUT = Path(__file__).resolve().parent
ROOT = OUT.parents[2]
SOURCE = ROOT / 'UnityProject/PAO/Assets/GameMain/Art/Preset'


def read_glb(path):
    with path.open('rb') as stream:
        magic, version, length = struct.unpack('<4sII', stream.read(12))
        if magic != b'glTF' or version != 2:
            raise ValueError('Expected a GLB 2.0 container')
        while stream.tell() < length:
            size, kind = struct.unpack('<II', stream.read(8))
            if kind == 0x4e4f534a:
                return json.loads(stream.read(size))
            stream.seek(size, 1)
    raise ValueError('No JSON chunk')


def quat_rotate(q, v):
    x, y, z, w = q
    norm = math.sqrt(sum(n*n for n in q))
    x, y, z, w = (n/norm for n in (x, y, z, w))
    vx, vy, vz = v
    tx, ty, tz = 2*(y*vz-z*vy), 2*(z*vx-x*vz), 2*(x*vy-y*vx)
    return [vx+w*tx+y*tz-z*ty, vy+w*ty+z*tx-x*tz, vz+w*tz+x*ty-y*tx]


def transform(node, point, translation=True):
    if 'matrix' in node:
        m = node['matrix']
        return [sum(m[j*4+i]*point[j] for j in range(3))+(m[12+i] if translation else 0) for i in range(3)]
    scale = node.get('scale', [1, 1, 1])
    result = quat_rotate(node.get('rotation', [0, 0, 0, 1]), [a*b for a, b in zip(point, scale)])
    return [a+b for a, b in zip(result, node.get('translation', [0, 0, 0]))] if translation else result


def bounds(points):
    if not points:
        return None
    mn = [min(p[i] for p in points) for i in range(3)]
    mx = [max(p[i] for p in points) for i in range(3)]
    return {'min': mn, 'max': mx, 'size': [b-a for a, b in zip(mn, mx)]}


def category(name):
    n = name.lower()
    for k, label in [('trap', 'Trap'), ('pavement', 'Pavement'), ('rock_slab', 'RockSlab'), ('rock', 'Rock'), ('stairs', 'Stairs'), ('stair', 'Stairs'), ('wall', 'Wall'), ('beam', 'Beam'), ('column', 'Column'), ('pillar', 'Column'), ('stone', 'Stone'), ('canyon', 'Canyon'), ('floor', 'Floor'), ('gate', 'Gate'), ('building', 'Building'), ('plant', 'Vegetation'), ('vine', 'Vegetation')]:
        if k in n:
            return label
    return 'Other'


def inventory(path):
    d = read_glb(path)
    nodes = d.get('nodes', [])
    meshes = d.get('meshes', [])
    accessors = d.get('accessors', [])
    materials = d.get('materials', [])
    refs = collections.defaultdict(list)
    parents = {}
    for i, n in enumerate(nodes):
        if 'mesh' in n:
            refs[n['mesh']].append(i)
        for c in n.get('children', []):
            parents[c] = i
    rows = []
    for mesh_index, mesh in enumerate(meshes):
        indices = refs[mesh_index]
        if not indices:
            continue
        node_index = indices[0]
        node = nodes[node_index]
        names = list(dict.fromkeys(nodes[i].get('name', f'node_{i}') for i in indices))
        label = names[0]
        name_hint = None
        # Some rock meshes have generic names. Adjacent empty nodes provide only
        # a naming hint, not a parent-child relationship or a transform to apply.
        if not label.startswith('TFD_') and node_index+1 < len(nodes):
            neighbor = nodes[node_index+1]
            if 'mesh' not in neighbor and math.dist(node.get('translation', [0, 0, 0]), neighbor.get('translation', [0, 0, 0])) < 0.001:
                name_hint = neighbor.get('name')
        primitive_rows = []
        points = []
        triangles = vertices = 0
        material_ids = set()
        for primitive_index, primitive in enumerate(mesh.get('primitives', [])):
            pos_index = primitive['attributes'].get('POSITION')
            pos = accessors[pos_index] if pos_index is not None else {}
            ix_index = primitive.get('indices')
            element_count = accessors[ix_index]['count'] if ix_index is not None else pos.get('count', 0)
            mode = primitive.get('mode', 4)
            tri_count = element_count//3 if mode == 4 else max(0, element_count-2) if mode in (5, 6) else 0
            if 'min' in pos and 'max' in pos:
                points += list(itertools.product(*zip(pos['min'], pos['max'])))
            material_id = primitive.get('material')
            if material_id is not None:
                material_ids.add(material_id)
            vertices += pos.get('count', 0)
            triangles += tri_count
            primitive_rows.append({'primitiveIndex': primitive_index, 'mode': mode, 'positionAccessor': pos_index, 'indexAccessor': ix_index, 'vertexCount': pos.get('count', 0), 'elementCount': element_count, 'triangleCount': tri_count, 'materialIndex': material_id, 'attributes': primitive['attributes'], 'hasMorphTargets': bool(primitive.get('targets'))})
        local = bounds(points)
        oriented = bounds([transform(node, v, False) for v in points])
        world = bounds([transform(node, v) for v in points])
        lod = re.search(r'_LOD(\d+)', label)
        rows.append({'meshIndex': mesh_index, 'meshName': mesh.get('name'), 'representativeNodeIndex': node_index, 'representativeNodeName': label, 'adjacentEmptyNameHint': name_hint, 'categoryHint': category(name_hint or label), 'lodLevel': int(lod.group(1)) if lod else None, 'instanceCountInPackage': len(indices), 'nodeNameExamples': names[:5], 'parentNodeIndex': parents.get(node_index), 'children': node.get('children', []), 'sourceTransform': {k: node[k] for k in ('translation', 'rotation', 'scale', 'matrix') if k in node}, 'localAccessorBounds': local, 'representativeOrientedBoundsWithoutTranslation': oriented, 'representativeSceneWorldBounds': world, 'suggestedBaseRecenterAfterSourceRotationScale': [-(oriented['min'][0]+oriented['max'][0])/2, -oriented['min'][1], -(oriented['min'][2]+oriented['max'][2])/2] if oriented else None, 'vertexCountAcrossPrimitives': vertices, 'triangleCount': triangles, 'materialIndices': sorted(material_ids), 'materialNames': [materials[i].get('name', f'material_{i}') for i in sorted(material_ids)], 'primitives': primitive_rows})
    return {'file': path.name, 'unityAssetPath': 'Assets/GameMain/Art/Preset/'+path.name, 'fileBytes': path.stat().st_size, 'gltfAsset': d.get('asset'), 'isCollection': len(rows)>1, 'nodeCount': len(nodes), 'meshNodeCount': sum('mesh' in n for n in nodes), 'emptyNodeCount': sum('mesh' not in n for n in nodes), 'uniqueMeshCount': len(meshes), 'hierarchyChildLinkCount': len(parents), 'sceneCount': len(d.get('scenes', [])), 'sceneRootCounts': [len(s.get('nodes', [])) for s in d.get('scenes', [])], 'accessorCount': len(accessors), 'skinCount': len(d.get('skins', [])), 'animationCount': len(d.get('animations', [])), 'extensionsUsed': d.get('extensionsUsed', []), 'materials': [{'index': i, **m} for i, m in enumerate(materials)], 'textures': d.get('textures', []), 'images': d.get('images', []), 'categoryCountsByUniqueMesh': dict(collections.Counter(r['categoryHint'] for r in rows)), 'candidates': rows}


def main():
    packages = [inventory(SOURCE/f'{i:02}.glb') for i in range(1, 7)]
    result = {'schemaVersion': 1, 'generatedDate': '2026-10-18', 'scope': 'Read-only JSON/mesh/node/accessor inventory; no source assets or Unity scenes changed.', 'coordinateNotes': ['Bounds are glTF XYZ. The importer may convert handedness on Unity import.', 'Mesh local positions are commonly 100 times larger than the placed unit; preserve source node scale approximately 0.01 and rotation (often Rx90).', 'Discard display-scene translation, preserve or bake source rotation and scale, then recenter to the base.', 'Every package is a collection. All nodes are scene roots in these source files; similarly named empty nodes are not parents.', 'Representative oriented bounds include that instance rotation and scale, not an invariant canonical module orientation.', 'Bounds are projected accessor AABBs, not exact collider shapes; flat tops and holes require visual inspection.', 'LOD variants are distinct meshes; do not instantiate LOD0/1/2/3 all at once.', 'Adjacent empty name hints are inferred from adjacency and matching translation, not from a hierarchy relationship.'], 'packages': packages}
    target = OUT/'预设资产清单.json'
    target.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
    for p in packages:
        print(p['file'], p['uniqueMeshCount'], p['categoryCountsByUniqueMesh'])
    print('Saved', target.name, target.stat().st_size, 'bytes')


if __name__ == '__main__':
    main()
