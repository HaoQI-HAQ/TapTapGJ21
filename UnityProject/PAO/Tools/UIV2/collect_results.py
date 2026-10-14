"""Collect successful regression and formal-map reports and their referenced PNGs."""
from pathlib import Path
import re
import shutil
import sys
import hashlib
import zipfile

root = Path(__file__).resolve().parents[2]
test = Path(sys.argv[1]).resolve()
dest = root / 'Docs/UI-V2/continuation'
for name, source_name in [('regression', 'RegressionArtifacts'), ('formal', 'VerificationArtifacts')]:
    source = test / source_name
    report = (source / 'report.txt').read_text(encoding='utf-8-sig')
    if not re.search(r'^Errors: 0$', report, re.M):
        raise SystemExit('Refusing to package failed verification: ' + name)
    target = dest / name
    target.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source / 'report.txt', target / 'report.txt')
    screenshots = re.findall(r'^Rendered (?:diagnostic world and HUD: )?([^\n]+)\.$', report, re.M)
    for screenshot in screenshots:
        shutil.copy2(source / (screenshot + '.png'), target / (screenshot + '.png'))
    print(name, report.splitlines()[0], 'screenshots:', len(screenshots))

snapshot_files = sorted((test / 'Assets/GameMain/Scripts').rglob('*.cs'))
snapshot_files += [test / 'Assets/GameMain/Resources/PAOUIConfig.asset', test / 'ProjectSettings/InputManager.asset']
with zipfile.ZipFile(dest / 'verified-source.zip', 'w', zipfile.ZIP_DEFLATED) as archive:
    hashes = []
    for source in snapshot_files:
        relative = source.relative_to(test).as_posix()
        archive.write(source, relative)
        hashes.append(hashlib.sha256(source.read_bytes()).hexdigest() + '  ' + relative)
(dest / 'verified-source-sha256.txt').write_text('\n'.join(hashes) + '\n', encoding='utf-8')

try:
    from PIL import Image, ImageDraw
    sheet = Image.new('RGB', (1200, 3 * 370), '#edf3f1')
    draw = ImageDraw.Draw(sheet)
    for index in range(6):
        screenshot = Image.open(dest / 'formal' / f'Map{index}-entry.png')
        screenshot.thumbnail((590, 332))
        x = (index % 2) * 600 + 5
        y = (index // 2) * 370 + 30
        sheet.paste(screenshot, (x, y))
        draw.text((x + 5, y - 22), f'Map{index} - diagnostic geometry and HUD', fill='#17384d')
    sheet.save(dest / 'maps-contact-sheet.png')
except ImportError:
    pass
