// Map4: make the art models sticky by wrapping each art prefab instance in a
// plain parent GameObject that carries StickySurface. A real parent is inherited
// by every child collider via GetComponentInParent, so 3 components cover the
// ~3900 art mesh colliders. Serialization mirrors Unity's own output for a
// prefab instance dragged under another GameObject.
//
// Usage: node wrap_art.js [--apply]
const fs = require('fs');

const SCENE = 'Assets/GameMain/Scenes/Map4.unity';
const STICKY_GUID = 'e382010327e838a4da7e7d0fc39ff968';
const APPLY = process.argv.includes('--apply');

const ART = [
  { guid: '570ac9390f8a6764f801e8d6d4ac8bce', name: 'Map4_StickyArt_Ruins_B' },
  { guid: '1ee526b49e3df5946a2ff50c2857297c', name: 'Map4_StickyArt_Ruins_A' },
  { guid: '9ed98f21d17bbed45b7848655aa86497', name: 'Map4_StickyArt_Mountain_Rock' },
];

const raw = fs.readFileSync(SCENE, 'utf8');
const EOL = raw.indexOf('\r\n') >= 0 ? '\r\n' : '\n';
const lines = raw.split(EOL);
const hadTrailingNewline = lines[lines.length - 1] === '';

// ---- doc index -----------------------------------------------------------
const docs = [];
let cur = null;
for (let i = 0; i < lines.length; i++) {
  const m = /^--- !u!(\d+) &(-?\d+)(.*)$/.exec(lines[i]);
  if (m) { if (cur) docs.push(cur); cur = { classId: +m[1], fileId: m[2], flags: m[3].trim(), start: i, end: lines.length }; }
}
if (cur) docs.push(cur);
for (let i = 0; i < docs.length - 1; i++) docs[i].end = docs[i + 1].start;
const field = (d, n, indent) => {
  const pre = indent === undefined ? '  ' : indent;
  for (let i = d.start + 1; i < d.end; i++) {
    const m = new RegExp('^' + pre + n + ':(.*)$').exec(lines[i]);
    if (m) return m[1].trim();
  }
  return null;
};

const ids = new Set(docs.map(d => d.fileId));
function newId() {
  for (;;) {
    const id = String(Math.floor(Math.random() * 8e15) + 1e14);
    if (!ids.has(id)) { ids.add(id); return id; }
  }
}

// ---- SceneRoots ----------------------------------------------------------
const rootsStart = lines.findIndex(l => l === '  m_Roots:');
if (rootsStart < 0) throw new Error('m_Roots not found');
let rootsEnd = rootsStart + 1;
while (rootsEnd < lines.length && /^  - \{fileID: -?\d+\}$/.test(lines[rootsEnd])) rootsEnd++;

const removeRootLines = new Set();
const addRootLines = [];
const replace = new Map();
const newDocs = [];

for (const art of ART) {
  const inst = docs.find(d => d.classId === 1001 && (field(d, 'm_SourcePrefab') || '').includes(art.guid));
  if (!inst) throw new Error('instance not found for ' + art.guid);
  if ((field(inst, 'm_TransformParent', '    ') || '') !== '{fileID: 0}') throw new Error('instance already parented: ' + inst.fileId);

  const rootTr = docs.find(d => d.classId === 4 && d.flags.includes('stripped') && (field(d, 'm_PrefabInstance') || '').includes(inst.fileId));
  if (!rootTr) throw new Error('root transform not found for instance ' + inst.fileId);

  const goId = newId();
  const trId = newId();
  const monoId = newId();

  let parentLine = -1;
  for (let i = inst.start + 1; i < inst.end; i++) {
    if (/^    m_TransformParent: \{fileID: 0\}$/.test(lines[i])) { parentLine = i; break; }
  }
  if (parentLine < 0) throw new Error('m_TransformParent line not found in ' + inst.fileId);
  replace.set(parentLine, '    m_TransformParent: {fileID: ' + trId + '}');

  const rootLine = lines.findIndex((l, i) => i >= rootsStart && i < rootsEnd && l === '  - {fileID: ' + inst.fileId + '}');
  if (rootLine < 0) throw new Error('instance not listed in m_Roots: ' + inst.fileId);
  removeRootLines.add(rootLine);
  addRootLines.push('  - {fileID: ' + trId + '}');

  const block = [
    '--- !u!1 &' + goId,
    'GameObject:',
    '  m_ObjectHideFlags: 0',
    '  m_CorrespondingSourceObject: {fileID: 0}',
    '  m_PrefabInstance: {fileID: 0}',
    '  m_PrefabAsset: {fileID: 0}',
    '  serializedVersion: 6',
    '  m_Component:',
    '  - component: {fileID: ' + trId + '}',
    '  - component: {fileID: ' + monoId + '}',
    '  m_Layer: 0',
    '  m_Name: ' + art.name,
    '  m_TagString: Untagged',
    '  m_Icon: {fileID: 0}',
    '  m_NavMeshLayer: 0',
    '  m_StaticEditorFlags: 0',
    '  m_IsActive: 1',
    '--- !u!4 &' + trId,
    'Transform:',
    '  m_ObjectHideFlags: 0',
    '  m_CorrespondingSourceObject: {fileID: 0}',
    '  m_PrefabInstance: {fileID: 0}',
    '  m_PrefabAsset: {fileID: 0}',
    '  m_GameObject: {fileID: ' + goId + '}',
    '  serializedVersion: 2',
    '  m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}',
    '  m_LocalPosition: {x: 0, y: 0, z: 0}',
    '  m_LocalScale: {x: 1, y: 1, z: 1}',
    '  m_ConstrainProportionsScale: 0',
    '  m_Children:',
    '  - {fileID: ' + rootTr.fileId + '}',
    '  m_Father: {fileID: 0}',
    '  m_LocalEulerAnglesHint: {x: 0, y: 0, z: 0}',
    '--- !u!114 &' + monoId,
    'MonoBehaviour:',
    '  m_ObjectHideFlags: 0',
    '  m_CorrespondingSourceObject: {fileID: 0}',
    '  m_PrefabInstance: {fileID: 0}',
    '  m_PrefabAsset: {fileID: 0}',
    '  m_GameObject: {fileID: ' + goId + '}',
    '  m_Enabled: 1',
    '  m_EditorHideFlags: 0',
    '  m_Script: {fileID: 11500000, guid: ' + STICKY_GUID + ', type: 3}',
    '  m_Name: ',
    '  m_EditorClassIdentifier: ',
    '  m_SnapToContactPoint: 1',
    '  m_FreezeRotation: 1',
  ];
  newDocs.push(block);
  console.log('wrap', art.name, ': instance', inst.fileId, 'rootTr', rootTr.fileId, '-> wrapper tr', trId, '| m_Children lines', block.filter(l => l.includes(rootTr.fileId)).length);
}

const out = [];
for (let i = 0; i < lines.length; i++) {
  if (removeRootLines.has(i)) continue;
  out.push(replace.has(i) ? replace.get(i) : lines[i]);
  if (i === rootsStart) for (const r of addRootLines) out.push(r);
}
if (hadTrailingNewline && out[out.length - 1] === '') out.pop();
for (const d of newDocs) for (const l of d) out.push(l);

if (!APPLY) { console.log('dry-run: would write', out.length, 'lines, eol=' + JSON.stringify(EOL)); process.exit(0); }
fs.writeFileSync(SCENE, out.join(EOL) + EOL, 'utf8');
console.log('written', out.length, 'lines');
