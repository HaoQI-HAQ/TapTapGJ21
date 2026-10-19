// Adds PAO.StickySurface to selected Map4 colliders (dry-run by default).
// Usage: node patch_sticky.js [--apply]
const fs = require('fs');

const SCENE = 'Assets/GameMain/Scenes/Map4.unity';
const STICKY_GUID = 'e382010327e838a4da7e7d0fc39ff968'; // StickySurface.cs
const APPLY = process.argv.includes('--apply');

const raw = fs.readFileSync(SCENE, 'utf8');
const lines = raw.split('\r\n');
const hadTrailingNewline = lines[lines.length - 1] === '';

// --- doc boundaries -------------------------------------------------------
const docs = [];
let cur = null;
for (let i = 0; i < lines.length; i++) {
  const m = /^--- !u!(\d+) &(-?\d+)(.*)$/.exec(lines[i]);
  if (m) {
    if (cur) docs.push(cur);
    cur = { classId: Number(m[1]), fileId: m[2], flags: m[3].trim(), start: i, end: lines.length };
  }
}
if (cur) docs.push(cur);
for (let i = 0; i < docs.length - 1; i++) docs[i].end = docs[i + 1].start;

const bodyOf = (d) => lines.slice(d.start + 1, d.end);
const fieldOf = (d, field) => {
  for (let i = d.start + 1; i < d.end; i++) {
    const m = new RegExp('^  ' + field + ':(.*)$').exec(lines[i]);
    if (m) return m[1].trim();
  }
  return null;
};

const allIds = new Set(docs.map(d => d.fileId));

const docByFileId = new Map(docs.map(d => [d.fileId, d]));

// --- materials / scripts --------------------------------------------------
const guidToAsset = new Map();
(function walk(d) {
  for (const e of fs.readdirSync(d, { withFileTypes: true })) {
    if (['Library', 'Temp', 'Logs', 'obj', '.git', 'UserSettings'].includes(e.name)) continue;
    const p = d + '/' + e.name;
    if (e.isDirectory()) walk(p);
    else if (e.name.endsWith('.meta')) {
      const s = fs.readFileSync(p, 'utf8');
      const g = /guid: ([0-9a-f]{32})/.exec(s);
      if (g) guidToAsset.set(g[1], p.replace(/\.meta$/, ''));
    }
  }
})('Assets');

const COLLIDER_CLASSES = new Set([64, 65, 135, 136, 142, 143, 154]);

const gameObjects = []; // {fileId, name, comps:[ids], active, layer}
for (const d of docs) {
  if (d.classId !== 1) continue;
  const comps = [];
  let inComp = false;
  for (let i = d.start + 1; i < d.end; i++) {
    const l = lines[i];
    if (/^  m_Component:/.test(l)) { inComp = true; continue; }
    if (inComp) {
      const m = /^  - component: \{fileID: (-?\d+)\}/.exec(l);
      if (m) comps.push(m[1]);
      else if (/^  [A-Za-z_]/.test(l)) inComp = false;
    }
  }
  gameObjects.push({ fileId: d.fileId, name: fieldOf(d, 'm_Name'), active: fieldOf(d, 'm_IsActive'), comps, doc: d });
}

// classify
const EXCLUDE_REASONS = [];
const targets = [];
const stats = {};
for (const go of gameObjects) {
  const cols = [];
  const scripts = [];
  const mats = [];
  let hasTerrain = false;
  let hasRb = false;
  for (const cid of go.comps) {
    const cd = docByFileId.get(cid);
    if (!cd) continue;
    if (COLLIDER_CLASSES.has(cd.classId)) {
      cols.push({ fileId: cid, cls: cd.classId, trigger: fieldOf(cd, 'm_IsTrigger') === '1' });
    } else if (cd.classId === 114) {
      const s = fieldOf(cd, 'm_Script') || '';
      const g = /guid: ([0-9a-f]{32})/.exec(s);
      scripts.push(g ? guidToAsset.get(g[1]) || g[1] : 'none');
    } else if (cd.classId === 23) {
      for (let i = cd.start + 1; i < cd.end; i++) {
        const m = /^\s*- \{fileID: -?\d+, guid: ([0-9a-f]{32}), type: 2\}/.exec(lines[i]);
        if (m) mats.push(guidToAsset.get(m[1]) || m[1]);
      }
    } else if (cd.classId === 218 || cd.classId === 154) hasTerrain = true;
    else if (cd.classId === 54) hasRb = true;
  }
  if (!cols.length) continue;
  const live = cols.filter(c => !c.trigger);
  const matNames = mats.map(m => m.split('/').pop().replace(/\.mat$/, ''));
  const scriptNames = scripts.map(s => s.split('/').pop().replace(/\.cs$/, ''));
  const reason = (() => {
    if (scriptNames.includes('StickySurface')) return 'already-sticky(script)';
    if (hasTerrain) return 'ground(Terrain)';
    if (scriptNames.includes('TerrainBubble')) return 'bubble-wall(TerrainBubble)';
    if (matNames.includes('WB_Ground')) return 'ground(WB_Ground)';
    if (matNames.includes('WB_Destructible')) return 'bubble-wall(WB_Destructible)';
    if (/Pavement/i.test(go.name)) return 'ground(TFD_Pavement)';
    if (!live.length) return 'trigger-only';
    return null;
  })();
  if (reason) { EXCLUDE_REASONS.push({ name: go.name, reason }); continue; }
  const key = matNames.length ? matNames.join('+') : '(art-collider)';
  stats[key] = (stats[key] || 0) + 1;
  targets.push({ fileId: go.fileId, name: go.name, materials: matNames, scripts: scriptNames, colliders: live.length, hasRb });
}

console.log('candidates:', targets.length);
console.log('by category:', stats);
const excl = {};
for (const e of EXCLUDE_REASONS) excl[e.reason] = (excl[e.reason] || 0) + 1;
console.log('excluded:', EXCLUDE_REASONS.length, excl);

if (!APPLY) {
  fs.writeFileSync('Temp/stickytool/targets.json', JSON.stringify({ targets, excluded: EXCLUDE_REASONS }, null, 1));
  console.log('dry-run: wrote Temp/stickytool/targets.json');
  process.exit(0);
}

// --- apply ---------------------------------------------------------------
function newId() {
  for (;;) {
    const id = String(Math.floor(Math.random() * 8e15) + 1e14); // 15 digits, positive
    if (!allIds.has(id)) { allIds.add(id); return id; }
  }
}

const insertAfterLine = new Map(); // line index -> array of extra lines
const newDocs = [];
for (const t of targets) {
  const goDoc = docByFileId.get(t.fileId);
  // find last "- component:" line of this GO doc
  let lastComp = -1;
  let inComp = false;
  for (let i = goDoc.start + 1; i < goDoc.end; i++) {
    if (/^  m_Component:/.test(lines[i])) { inComp = true; continue; }
    if (inComp) {
      if (/^  - component: \{fileID: -?\d+\}/.test(lines[i])) lastComp = i;
      else if (/^  [A-Za-z_]/.test(lines[i])) break;
    }
  }
  if (lastComp < 0) throw new Error('no component list for ' + t.name);
  const compId = newId();
  const extra = insertAfterLine.get(lastComp) || [];
  extra.push('  - component: {fileID: ' + compId + '}');
  insertAfterLine.set(lastComp, extra);
  newDocs.push([
    '--- !u!114 &' + compId,
    'MonoBehaviour:',
    '  m_ObjectHideFlags: 0',
    '  m_CorrespondingSourceObject: {fileID: 0}',
    '  m_PrefabInstance: {fileID: 0}',
    '  m_PrefabAsset: {fileID: 0}',
    '  m_GameObject: {fileID: ' + t.fileId + '}',
    '  m_Enabled: 1',
    '  m_EditorHideFlags: 0',
    '  m_Script: {fileID: 11500000, guid: ' + STICKY_GUID + ', type: 3}',
    '  m_Name: ',
    '  m_EditorClassIdentifier: ',
    '  m_SnapToContactPoint: 1',
    '  m_FreezeRotation: 1',
  ]);
}

// rebuild
const out = [];
for (let i = 0; i < lines.length; i++) {
  out.push(lines[i]);
  const extra = insertAfterLine.get(i);
  if (extra) for (const e of extra) out.push(e);
}
// drop trailing empty element (from final newline) then append new docs, then trailing newline
if (out.length && out[out.length - 1] === '') out.pop();
for (const d of newDocs) for (const l of d) out.push(l);

fs.writeFileSync(SCENE, out.join('\r\n') + '\r\n', 'utf8');
fs.writeFileSync('Temp/stickytool/applied.json', JSON.stringify({ count: targets.length, names: targets.map(t => t.name) }, null, 1));
console.log('applied', targets.length, 'StickySurface components; file lines:', out.length);
