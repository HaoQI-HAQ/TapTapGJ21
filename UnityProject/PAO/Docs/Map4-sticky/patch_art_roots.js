// Adds StickySurface on the root GameObjects of the three art prefab instances
// in Map4 (ruins A/B + mountain rock), by writing a stripped GameObject stub,
// the MonoBehaviour doc and the PrefabInstance m_AddedComponents registration.
// Usage: node patch_art_roots.js [--apply]
const fs = require('fs');

const SCENE = 'Assets/GameMain/Scenes/Map4.unity';
const STICKY_GUID = 'e382010327e838a4da7e7d0fc39ff968';
const APPLY = process.argv.includes('--apply');

// instanceId -> { guid, rootGoId }
const TARGETS = [
  // SM_L4_yi_ ji_2glb.glb
  { instance: '307893898', guid: '570ac9390f8a6764f801e8d6d4ac8bce', rootGo: '-3910990755373119420', label: 'SM_L4_yi_ji_2glb' },
  // SM_L4_yi_ ji_1glb.glb
  { instance: '366470565', guid: '1ee526b49e3df5946a2ff50c2857297c', rootGo: '-3910990755373119420', label: 'SM_L4_yi_ji_1glb' },
  // SM_L4_shan_ shi.glb
  { instance: '1339118477', guid: '9ed98f21d17bbed45b7848655aa86497', rootGo: '-8673601721700108098', label: 'SM_L4_shan_shi' },
];

const text = fs.readFileSync(SCENE, 'utf8');
const lines = text.split('\r\n');
const hadTrailingNewline = lines[lines.length - 1] === '';
const allIds = new Set();
for (const l of lines) {
  const m = /^--- !u!\d+ &(-?\d+)/.exec(l);
  if (m) allIds.add(m[1]);
}
function newId() {
  for (;;) {
    const id = String(Math.floor(Math.random() * 8e15) + 1e14);
    if (!allIds.has(id)) { allIds.add(id); return id; }
  }
}

const insertAfter = new Map(); // line index -> lines
const newDocs = [];

for (const t of TARGETS) {
  const docStart = lines.findIndex(l => l.startsWith('--- !u!1001 &' + t.instance));
  if (docStart < 0) throw new Error('instance not found: ' + t.instance);
  // must belong to this prefab guid
  const srcLine = lines.findIndex((l, i) => i > docStart && l.includes('m_SourcePrefab') );
  const srcBlock = lines.slice(srcLine, srcLine + 2).join(' ');
  if (!srcBlock.includes(t.guid)) throw new Error('source prefab mismatch for ' + t.instance);
  // already patched?
  const addedIdx = lines.findIndex((l, i) => i > docStart && /^    m_AddedComponents:/.test(l));
  if (addedIdx < 0) throw new Error('no m_AddedComponents for ' + t.instance);
  let already = false;
  for (let i = addedIdx + 1; i < lines.length && !/^  \S/.test(lines[i]) && !/^    m_SourcePrefab/.test(lines[i]); i++) {
    if (lines[i].includes(STICKY_GUID)) { already = true; break; }
  }
  if (already) { console.log('skip (already patched):', t.label); continue; }

  const stubGoId = newId();
  const monoId = newId();

  insertAfter.set(addedIdx, [
    '    - targetCorrespondingSourceObject: {fileID: ' + t.rootGo + ', guid: ' + t.guid + ',',
    '        type: 3}',
    '      insertIndex: -1',
    '      addedObject: {fileID: ' + monoId + '}',
  ]);

  newDocs.push([
    '--- !u!1 &' + stubGoId + ' stripped',
    'GameObject:',
    '  m_CorrespondingSourceObject: {fileID: ' + t.rootGo + ', guid: ' + t.guid + ',',
    '    type: 3}',
    '  m_PrefabInstance: {fileID: ' + t.instance + '}',
    '  m_PrefabAsset: {fileID: 0}',
  ]);
  newDocs.push([
    '--- !u!114 &' + monoId,
    'MonoBehaviour:',
    '  m_ObjectHideFlags: 0',
    '  m_CorrespondingSourceObject: {fileID: 0}',
    '  m_PrefabInstance: {fileID: 0}',
    '  m_PrefabAsset: {fileID: 0}',
    '  m_GameObject: {fileID: ' + stubGoId + '}',
    '  m_Enabled: 1',
    '  m_EditorHideFlags: 0',
    '  m_Script: {fileID: 11500000, guid: ' + STICKY_GUID + ', type: 3}',
    '  m_Name: ',
    '  m_EditorClassIdentifier: ',
    '  m_SnapToContactPoint: 1',
    '  m_FreezeRotation: 1',
  ]);
  console.log('will patch', t.label, 'root GO', t.rootGo, '-> component', monoId);
}

if (!APPLY) { console.log('dry-run only'); process.exit(0); }

const out = [];
for (let i = 0; i < lines.length; i++) {
  out.push(lines[i]);
  const extra = insertAfter.get(i);
  if (extra) for (const e of extra) out.push(e);
}
if (hadTrailingNewline && out[out.length - 1] === '') out.pop();
for (const d of newDocs) for (const l of d) out.push(l);
fs.writeFileSync(SCENE, out.join('\r\n') + '\r\n', 'utf8');
console.log('applied; docs added:', newDocs.length / 2);
