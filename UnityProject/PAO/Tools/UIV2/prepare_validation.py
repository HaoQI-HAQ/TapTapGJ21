"""Prepare a disposable Unity UI test project outside the source project's Temp directory.

Usage: python Tools/UIV2/prepare_validation.py ABSOLUTE_TEST_PROJECT_PATH
Requires the source project's existing Library/ScriptAssemblies dependency cache.
"""
from pathlib import Path
import json
import re
import shutil
import sys
import uuid

root = Path(__file__).resolve().parents[2]
dest = Path(sys.argv[1]).resolve()
if dest == root or dest.is_relative_to(root):
    raise SystemExit('Use a verification directory outside the original Unity project.')
dest.mkdir(parents=True, exist_ok=True)
(dest / 'PAO_UI_VERIFICATION_PROJECT').write_text('Disposable UI validation copy. Source: ' + str(root), encoding='utf-8')

def copy(source):
    target = dest / source.relative_to(root)
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, target)

namespace = uuid.UUID('ad8d330d-3449-4f82-a7d3-890207ff7549')
for source in (root / 'Assets/GameMain/Scripts/UI/V2').glob('*.cs'):
    meta = Path(str(source) + '.meta')
    if not meta.exists():
        meta.write_text('fileFormatVersion: 2\nguid: ' + uuid.uuid5(namespace, source.relative_to(root).as_posix()).hex + '\n', encoding='utf-8')

for folder in ('Assets/GameMain/Scripts/UI', 'Assets/GameMain/Scripts/Gameplay', 'Assets/GameMain/Resources'):
    for source in (root / folder).rglob('*'):
        if source.is_file():
            copy(source)
for name in ('UIV2Tools', 'UIV2Verification', 'UIMenuVerification'):
    for suffix in ('.cs', '.cs.meta'):
        copy(root / ('Assets/GameMain/Scripts/Editor/' + name + suffix))
for source in (root / 'Assets/GameMain/UI/Demigiant/DOTween').rglob('*'):
    if source.is_file() and 'Editor' not in source.parts:
        copy(source)
for source in (root / 'Assets/GameMain/Art/Shaders').glob('*.hlsl*'):
    copy(source)
for source in (root / 'ProjectSettings').glob('*'):
    if source.is_file() and source.name not in {'EditorBuildSettings.asset', 'GraphicsSettings.asset', 'QualitySettings.asset'}:
        copy(source)

plugins = dest / 'Assets/Plugins/VerificationDependencies'
plugins.mkdir(parents=True, exist_ok=True)
required = {'UnityEngine.UI', 'Unity.TextMeshPro', 'Unity.RenderPipelines.Universal.Runtime',
            'Unity.RenderPipelines.Core.Runtime', 'Unity.RenderPipelines.Universal.Shaders',
            'Unity.RenderPipeline.Universal.ShaderLibrary', 'Unity.RenderPipelines.Core.ShaderLibrary',
            'Unity.RenderPipelines.Universal.Config.Runtime', 'Unity.Mathematics', 'Unity.Collections', 'Unity.Burst'}
for source in (root / 'Library/ScriptAssemblies').glob('*.dll'):
    if source.stem in required:
        shutil.copy2(source, plugins / source.name)
for relative in ('com.unity.burst@1.8.24/Unity.Burst.Unsafe.dll',
                 'com.unity.collections@1.2.4/Unity.Collections.LowLevel.ILSupport/Unity.Collections.LowLevel.ILSupport.dll'):
    shutil.copy2(root / 'Library/PackageCache' / relative, plugins / Path(relative).name)
(dest / 'Packages').mkdir(parents=True, exist_ok=True)
manifest = json.loads((root / 'Packages/manifest.json').read_text(encoding='utf-8-sig'))
modules = {k: v for k, v in manifest['dependencies'].items() if k.startswith('com.unity.modules.')}
(dest / 'Packages/manifest.json').write_text(json.dumps({'dependencies': modules}, indent=2), encoding='utf-8')

guids = {}
for meta in (root / 'Assets').rglob('*.meta'):
    match = re.search(r'^guid: ([a-f0-9]{32})', meta.read_text(encoding='utf-8-sig'), re.M)
    if match:
        guids[match[1]] = Path(str(meta)[:-5])
package_scripts = {}
for meta in (root / 'Library/PackageCache').rglob('*.cs.meta'):
    match = re.search(r'^guid: ([a-f0-9]{32})', meta.read_text(encoding='utf-8-sig'), re.M)
    if match:
        package_scripts[match[1]] = Path(str(meta)[:-8]).name
queue = [root / f'Assets/GameMain/Scenes/Map{i}.unity' for i in range(6)]
queue.append(root / 'Assets/GameMain/Resources/PAOUIConfig.asset')
queue.append(root / 'Assets/GameMain/UI/Menu.unity')
queue.append(root / 'Assets/GameMain/Art/Fonts/TextMesh Pro/Resources/TMP Settings.asset')
visited = set()
while queue:
    source = queue.pop()
    if source in visited or not source.is_file():
        continue
    visited.add(source)
    copy(source)
    meta = Path(str(source) + '.meta')
    texts = []
    if meta.is_file():
        copy(meta); texts.append(meta.read_text(encoding='utf-8-sig'))
    if source.suffix.lower() in {'.unity', '.prefab', '.asset', '.mat', '.controller', '.anim'}:
        texts.append(source.read_text(encoding='utf-8-sig'))
    for content in texts:
        for guid in re.findall(r'guid: ([a-f0-9]{32})', content):
            if guid in guids:
                queue.append(guids[guid])
(dest / 'PackageScriptGuids.txt').write_text('\n'.join(k + ' ' + v for k, v in package_scripts.items()), encoding='utf-8')

# Preserve shader includes for import diagnostics without compiling a second copy of package C# sources.
shader_packages = ('com.unity.render-pipelines.core', 'com.unity.render-pipelines.universal', 'com.unity.render-pipelines.universal-config')
for package in shader_packages:
    package_source = next((root / 'Library/PackageCache').glob(package + '@*'))
    for source in package_source.rglob('*'):
        if source.is_file() and (source.suffix in {'.hlsl', '.shader', '.cginc', '.compute'} or
                                source.name.endswith(('.hlsl.meta', '.shader.meta', '.cginc.meta', '.compute.meta'))):
            target = dest / 'Assets/VerificationShaderIncludes' / package / source.relative_to(package_source)
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(source, target)
for folder in (dest / 'Assets/VerificationShaderIncludes', dest / 'Assets/GameMain/Art/Shaders'):
    for target in folder.rglob('*'):
        if target.is_file() and target.suffix in {'.hlsl', '.shader', '.cginc', '.compute'}:
            content = target.read_text(encoding='utf-8-sig')
            for package in shader_packages:
                content = content.replace('Packages/' + package + '/', 'Assets/VerificationShaderIncludes/' + package + '/')
            target.write_text(content, encoding='utf-8')

# Also compile all current runtime source against the original project's resolved references.
rsp_path = next((root / 'Library/Bee/artifacts').glob('*.dag/Assembly-CSharp.rsp'))
rsp = rsp_path.read_text(encoding='utf-8-sig')
lines = [line for line in rsp.splitlines() if not line.startswith(('-out:', '-refout:', '/out:', '/refout:', '-analyzer:', '/analyzer:', '/additionalfile:'))]
lines.insert(0, '-out:"' + str(dest / 'CompileCheck.dll') + '"')
for source in (root / 'Assets/GameMain/Scripts/UI/V2').glob('*.cs'):
    relative = source.relative_to(root).as_posix()
    if relative not in rsp:
        lines.append('"' + relative + '"')
(dest / 'compile.rsp').write_text('\n'.join(lines), encoding='utf-8')
print('Prepared', dest, 'with', len(visited), 'formal map dependency assets.')
