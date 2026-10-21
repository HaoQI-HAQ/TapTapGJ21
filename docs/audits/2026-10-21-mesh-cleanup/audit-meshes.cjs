// Read-only audit of saved Unity assets. Writes reports only; never deletes assets.
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const root = path.resolve(__dirname, '../../..');
const project = path.join(root, 'UnityProject/PAO');
const output = __dirname;
const posix = p => p.split(path.sep).join('/');
const relative = p => posix(path.relative(project, p));
const modelExtensions = new Set(['.fbx', '.obj', '.glb', '.gltf', '.blend', '.dae', '.3ds', '.max', '.dxf', '.ma', '.mb']);
const mediaExtensions = new Set(['.png', '.jpg', '.jpeg', '.tga', '.psd', '.exr', '.hdr', '.gif', '.bmp', '.webp', '.tif', '.tiff', '.dds', '.wav', '.mp3', '.ogg', '.aiff', '.aif', '.mp4', '.mov', '.dll', '.pdb', '.ttf', '.otf', '.woff', '.woff2', '.zip', '.unitypackage', '.pdf', '.docx', '.xlsx']);
function walk(dir) {
  if (!fs.existsSync(dir)) return [];
  return fs.readdirSync(dir, { withFileTypes: true }).flatMap(e => {
    if (['.git', 'node_modules', 'Library', 'Temp', 'Logs', 'obj', 'bin'].includes(e.name)) return [];
    const p = path.join(dir, e.name);
    return e.isDirectory() ? walk(p) : e.isFile() ? [p] : [];
  });
}
const files = ['Assets', 'Packages', 'ProjectSettings'].flatMap(p => walk(path.join(project, p)));
const texts = new Map();
const binarySerialized = [];
const binaryBuffers = new Map();
for (const file of files) {
  const ext = path.extname(file).toLowerCase();
  if (mediaExtensions.has(ext) || modelExtensions.has(ext) && ext !== '.gltf') continue;
  const data = fs.readFileSync(file);
  if (data.subarray(0, 4096).includes(0)) {
    if (['.asset', '.unity', '.prefab', '.mesh'].includes(ext)) {
      binarySerialized.push(relative(file));
      binaryBuffers.set(relative(file), data);
    }
    continue;
  }
  texts.set(relative(file), data.toString('utf8'));
}
const guidToPath = new Map();
const guidByPath = new Map();
const duplicateGuids = [];
for (const [p, content] of texts) {
  if (!p.endsWith('.meta')) continue;
  const guid = content.match(/^guid:\s*([a-f0-9]{32})/mi)?.[1].toLowerCase();
  if (!guid) continue;
  const asset = p.slice(0, -5);
  if (guidToPath.has(guid)) duplicateGuids.push([guid, guidToPath.get(guid), asset]);
  guidToPath.set(guid, asset);
  guidByPath.set(asset, guid);
}
const incoming = new Map();
const graph = new Map();
for (const [p, content] of texts) {
  const owner = p.endsWith('.meta') ? p.slice(0, -5) : p;
  if (!graph.has(owner)) graph.set(owner, new Set());
  for (const match of content.matchAll(/\b[a-f0-9]{32}\b/gi)) {
    const target = guidToPath.get(match[0].toLowerCase());
    if (!target || target === owner) continue;
    graph.get(owner).add(target);
    if (!incoming.has(target)) incoming.set(target, new Set());
    incoming.get(target).add(p);
  }
}
const allScenes = files.filter(p => p.toLowerCase().endsWith('.unity') && relative(p).startsWith('Assets/')).map(relative).sort();
const buildText = texts.get('ProjectSettings/EditorBuildSettings.asset');
const buildScenes = [...buildText.matchAll(/- enabled: ([01])\s+path: ([^\r\n]+)\s+guid: ([a-f0-9]{32})/g)].map(m => ({ enabled: m[1] === '1', path: guidToPath.get(m[3]) || m[2], guid: m[3] }));
function closure(seeds) {
  const visited = new Set(seeds);
  const queue = [...seeds];
  for (let i = 0; i < queue.length; i++) {
    for (const target of graph.get(queue[i]) || []) {
      if (visited.has(target)) continue;
      visited.add(target);
      queue.push(target);
    }
  }
  return visited;
}
const sceneClosures = new Map(allScenes.map(p => [p, closure([p])]));
const resourceRoots = files.filter(p => relative(p).startsWith('Assets/') && /\/(Resources|StreamingAssets)\//i.test(relative(p)) && !p.endsWith('.meta')).map(relative);
const resourceClosure = closure(resourceRoots);
const enabledClosure = closure(buildScenes.filter(s => s.enabled).map(s => s.path).concat(resourceRoots));
const meshes = [];
const mixedAssetFiles = [];
for (const file of files) {
  const p = relative(file);
  if (!p.startsWith('Assets/') || p.endsWith('.meta')) continue;
  const ext = path.extname(file).toLowerCase();
  let kind;
  if (modelExtensions.has(ext)) kind = 'model-container';
  else if (['.asset', '.mesh'].includes(ext)) {
    const classes = [...(texts.get(p) || '').matchAll(/^--- !u!(\d+) /gm)].map(m => Number(m[1]));
    if (!classes.includes(43)) continue;
    if (classes.some(c => c !== 43)) {
      mixedAssetFiles.push({ path: p, classes });
      continue;
    }
    kind = 'standalone-mesh';
  } else continue;
  const meta = texts.get(p + '.meta') || '';
  const guid = guidByPath.get(p) || null;
  const directReferences = [...incoming.get(p) || []].sort();
  const binaryGuidMatches = [];
  if (guid) {
    const raw = Buffer.from(guid, 'hex');
    const wordReverse = Buffer.concat([0, 4, 8, 12].map(i => Buffer.from(raw.subarray(i, i + 4)).reverse()));
    const nibbleReverse = Buffer.from(guid.match(/.{8}/g).map(w => [...w].reverse().join('')).join(''), 'hex');
    const variants = [raw, Buffer.from(raw).reverse(), wordReverse, nibbleReverse, Buffer.from(guid, 'ascii')];
    for (const [source, data] of binaryBuffers) {
      if (variants.some(v => data.includes(v))) binaryGuidMatches.push(source);
    }
  }
  const filename = path.basename(file);
  const stem = path.basename(file, ext);
  const stringReferences = [];
  const nameMentions = [];
  for (const [source, content] of texts) {
    if (source === p || source === p + '.meta' || source.endsWith('.meta')) continue;
    if (!/\.(cs|json|csv|xml|txt|md|yaml|yml|gltf|asset|prefab|unity)$/i.test(source)) continue;
    const lower = content.toLowerCase();
    if (!lower.includes(filename.toLowerCase()) && !lower.includes(p.toLowerCase()) && !(stem.length >= 4 && (content.includes('"' + stem + '"') || content.includes("'" + stem + "'")))) continue;
    const lines = content.split(/\r?\n/);
    for (let i = 0; i < lines.length; i++) {
      const line = lines[i];
      if (line.toLowerCase().includes(p.toLowerCase()) || line.toLowerCase().includes(filename.toLowerCase())) {
        stringReferences.push({ path: source, line: i + 1, text: line.trim().slice(0, 300) });
      } else if (stem.length >= 4 && (line.includes('"' + stem + '"') || line.includes("'" + stem + "'"))) {
        nameMentions.push({ path: source, line: i + 1, text: line.trim().slice(0, 300) });
      }
    }
  }
  const scenes = allScenes.filter(s => sceneClosures.get(s).has(p));
  const bundle = meta.match(/^[ \t]*assetBundleName:[ \t]*([^\r\n]*)/m)?.[1]?.trim() || null;
  let container = null;
  if (ext === '.glb') {
    const data = fs.readFileSync(file);
    if (data.toString('ascii', 0, 4) === 'glTF' && data.readUInt32LE(16) === 0x4e4f534a) {
      const json = JSON.parse(data.toString('utf8', 20, 20 + data.readUInt32LE(12)).trim());
      container = { meshes: json.meshes?.length || 0, materials: json.materials?.length || 0, images: json.images?.length || 0, animations: json.animations?.length || 0, skins: json.skins?.length || 0, meshNames: (json.meshes || []).map(m => m.name).filter(Boolean) };
    }
  }
  const reasons = [];
  if (!guid) reasons.push('missing-meta-guid');
  if (directReferences.length) reasons.push('referenced-by-retained-asset');
  if (binaryGuidMatches.length) reasons.push('possible-reference-in-binary-asset');
  if (stringReferences.some(r => !/\.(md|txt)$/i.test(r.path))) reasons.push('referenced-by-path');
  if (resourceClosure.has(p)) reasons.push('resource-or-streamingasset-dependency');
  if (bundle) reasons.push('asset-bundle');
  const data = fs.readFileSync(file);
  const metaData = fs.existsSync(file + '.meta') ? fs.readFileSync(file + '.meta') : Buffer.alloc(0);
  const animationType = Number(meta.match(/^[ \t]*animationType:[ \t]*(\d+)/m)?.[1] || 0);
  const scopeExclusions = [];
  if (container && (container.images || container.animations || container.skins)) scopeExclusions.push('model-container-includes-images-animation-or-skins');
  if (ext === '.fbx' && animationType > 0) scopeExclusions.push('fbx-rig-or-animation-import-enabled');
  meshes.push({ path: p, kind, guid, bytes: data.length, metaBytes: metaData.length, sha256: crypto.createHash('sha256').update(data).digest('hex'), metaSha256: crypto.createHash('sha256').update(metaData).digest('hex'), directReferences, binaryGuidMatches, stringReferences, nameMentions, scenes, enabledBuildOrResources: enabledClosure.has(p), resourceDependency: resourceClosure.has(p), bundle, container, animationType, scopeExclusions, status: reasons.length ? 'retain' : 'candidate-needs-review', reasons });
}
meshes.sort((a, b) => a.path.localeCompare(b.path));
const candidates = meshes.filter(m => m.status === 'candidate-needs-review');
const result = { generatedAt: new Date().toISOString(), project, scope: 'Saved files only; no asset deleted; all Assets scenes and retained prefabs are protected.', filesScanned: files.length, textFilesScanned: texts.size, binarySerialized, duplicateGuids, mixedAssetFiles, allScenes, buildScenes, resourceRootCount: resourceRoots.length, summary: { meshFiles: meshes.length, candidateFiles: candidates.length, candidateBytes: candidates.reduce((s, m) => s + m.bytes, 0), retainedFiles: meshes.length - candidates.length }, candidates, meshes };
fs.writeFileSync(path.join(output, 'mesh-audit.json'), JSON.stringify(result, null, 2) + '\n');
const proposed = candidates.filter(m => !m.scopeExclusions.length);
const excluded = candidates.filter(m => m.scopeExclusions.length);
const plan = {
  generatedAt: result.generatedAt,
  approvalStatus: 'WAITING_FOR_USER_CONFIRMATION',
  instruction: 'Only the proposed four mesh/model source files and their matching .meta files may be deleted after explicit user confirmation. Do not delete excluded containers, materials, textures, animations, folders, scenes, or prefabs. Recheck asset references, file hashes, and editor dirty state immediately before deletion.',
  project,
  proposed: proposed.map(m => ({ ...m, absolutePath: path.join(project, m.path), absoluteMetaPath: path.join(project, m.path + '.meta') })),
  excludedUnusedContainers: excluded,
  proposedAssetBytes: proposed.reduce((s, m) => s + m.bytes, 0),
  proposedMetaBytes: proposed.reduce((s, m) => s + m.metaBytes, 0)
};
fs.writeFileSync(path.join(output, 'deletion-proposal.json'), JSON.stringify(plan, null, 2) + '\n');
const link = p => '[' + path.basename(p) + '](<' + posix(path.join(project, p)) + '>)';
const notes = {
  'Assets/GameMain/Art/Materials/L4海草2/Kelp.fbx': '海草模型重复文件；与仍被关卡引用的 L4海草1/Kelp.fbx 源文件 SHA-256 相同，两个文件 GUID 不同。代码中的 Kelp 字样是场景对象名称筛选，不是对此文件的加载。',
  'Assets/GameMain/Art/Models/level0.obj': '未使用的 OBJ 网格；材质文件 level0.mtl 如存在，也不在删除范围。',
  'Assets/GameMain/Data/ShrineReferences/GeneratedMeshes/Slope_a805a83c6fde940cd56f0aafefc5fbb7.asset': '独立 Mesh 资产，Unity 序列化类型 43；其余两个仍被 Map2 / Map2_5 引用的坡面保留。',
  'Assets/ThirdParty/Thalassophobia/Contents/Meshes/Light Cone.fbx': '光锥网格；仅插件 README 列出了文件路径，没有场景、预制体或加载代码引用。README 不属于运行时加载入口。'
};
const report = [
  '# 网格体清理待确认清单', '',
  '状态：等待用户确认；尚未删除任何资源。审查日期：2026-10-21（Asia/Hong_Kong）。', '',
  '## 建议删除的 4 个文件', '',
  '遵守“只删除网格体”的范围：本表仅包含静态模型源文件和独立 Mesh，确认后同时删除各自配套 .meta；不删除任何文件夹。', '',
  '| 编号 | 文件 | 源文件大小 | 判定依据 |', '| --- | --- | ---: | --- |',
  ...proposed.map((m, i) => `| ${i + 1} | ${link(m.path)}<br>\`${m.path}\` | ${(m.bytes / 1024).toFixed(2)} KiB | ${notes[m.path]} |`), '',
  `合计：${plan.proposedAssetBytes.toLocaleString('en-US')} 字节（${(plan.proposedAssetBytes / 1024).toFixed(2)} KiB）；配套 .meta 共 ${plan.proposedMetaBytes.toLocaleString('en-US')} 字节。`, '',
  '## 未使用但不纳入本次删除的容器', '',
  '| 文件 | 大小 | 保留原因 |', '| --- | ---: | --- |',
  ...excluded.map(m => `| ${link(m.path)}<br>\`${m.path}\` | ${(m.bytes / 1048576).toFixed(2)} MiB | ${m.container ? `${m.container.meshes} 个网格，另含 ${m.container.materials} 个材质、${m.container.images} 张图像；直接删除整包超出纯网格范围。` : 'FBX 使用 Generic 骨骼/动画导入设置（animationType: 2）；整文件删除可能同时移除骨骼、Avatar、动画子资源。'} |`), '',
  '这两个容器合计约 12.27 MiB。若后续明确授权清理整个未使用模型容器，可另行列入；当前对上表 4 项的确认不包含这两个文件。', '',
  '## 检查范围与证据', '',
  `扫描 ${result.filesScanned} 个文件，其中 ${result.textFilesScanned} 个可读文本；覆盖 Assets、嵌入式 Packages 与 ProjectSettings。检查 ${allScenes.length} 个已保存场景、预制体、材质、控制器、动画及导入器 .meta 的 GUID 引用与递归依赖。`, '',
  '保留所有仍被任何现有场景或预制体引用的模型，包含停用关卡、隐藏对象、禁用对象和碰撞体；不会因为没有渲染或没有启用构建而判断为未使用。Resources / StreamingAssets 的文件及其依赖、资源包设置和脚本中的具体模型路径也纳入保护。', '',
  '发现 35 个项目内模型/网格文件；其中 29 个有现存引用，6 个未发现引用。Unity AssetDatabase 的 t:Mesh 查询与文件扫描结果一致（项目内 35 项，查询另含 2 项包资源）。查询记录见 editor-mesh-index.json。', '',
  '当前编辑器只打开 Creat 场景，isDirty=false，查询时没有未保存场景改动；见 editor-scene-status.json。审查针对查询时的已保存文件，后续确认执行前需重新核对。', '',
  `另有 ${binarySerialized.length} 个二进制 .asset：${binarySerialized.map(p => '\`' + p + '\`').join('、')}。这些文件未做完整类型树解析；已扫描候选 GUID 的常见二进制表示，未发现匹配。未将这些资产列为清理对象。`, '',
  '还在 docs/plan、_merge_recovery、UnityProject/PAO/Docs、Tools 中搜索候选 GUID，未找到引用；见 historical-references.json。历史备份和审查报告不作为运行时入口。', '',
  '注意：本次是静态引用检查及只读编辑器查询，没有逐关运行验证。删除前重新检查引用与文件哈希，以免期间新增的关卡引用被遗漏。', '',
  '## 其余 29 个文件与关卡对应关系', '',
  '| 文件（相对于项目根目录） | 场景递归依赖 |', '| --- | --- |',
  ...meshes.filter(m => m.status === 'retain').map(m => `| \`${m.path}\` | ${m.scenes.map(s => path.basename(s, '.unity')).join('、') || '保留资产/Resources 依赖'} |`), '',
  'Preset/04.glb、Preset/05.glb 以及两个坡面 Mesh 仍被停用的 Map2 / Map2_5 使用，因此保留。大体积 Preset01.fbx、Preset4.fbx 和其余 GLB 均有现有关卡引用；不会拆删共享模型包中的子网格。', '',
  '## 执行清单', '',
  '机器可读清单：deletion-proposal.json。每项保存绝对路径、GUID、源文件与 .meta 的 SHA-256。确认后仅删除 proposed 数组里的源文件和 .meta；excludedUnusedContainers 中的文件不包含在此次确认中。', '',
  '独立材质、贴图、音频、脚本、预制体、场景、TerrainData 以及当前工作区其他修改均不在本次删除范围。', ''
];
fs.writeFileSync(path.join(output, '待确认删除清单.md'), report.join('\n'));
console.log(JSON.stringify({ summary: result.summary, proposed: proposed.map(m => ({path:m.path,bytes:m.bytes})), excluded: excluded.map(m => ({path:m.path,bytes:m.bytes,scopeExclusions:m.scopeExclusions})), proposedAssetBytes:plan.proposedAssetBytes, binaryGuidMatches:meshes.filter(m=>m.binaryGuidMatches.length).map(m=>({path:m.path,matches:m.binaryGuidMatches})) }, null, 2));
