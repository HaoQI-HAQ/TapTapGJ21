"""Collect actual Unity page captures and make contact sheets for design review.

Usage: python Tools/UIV2/collect_design_review.py ISOLATED_REVIEW_PROJECT
"""
from pathlib import Path
import hashlib
import json
import math
import shutil
import sys
from PIL import Image, ImageDraw, ImageFont

root = Path(__file__).resolve().parents[2]
review = Path(sys.argv[1]).resolve()
if not (review / 'PAO_UI_VERIFICATION_PROJECT').is_file():
    raise SystemExit('Expected an isolated review project.')
captures = review / 'VisualReview'
summary = (captures / 'capture-summary.txt').read_text(encoding='utf-8')
expected = int(summary.split()[0])
shots = sorted(p for p in captures.glob('*.png') if p.name != 'overview.png')
if len(shots) != expected:
    raise SystemExit(f'Expected {expected} captures, found {len(shots)}.')
out = root / 'Docs/UI-V2/design-review'
out.mkdir(parents=True, exist_ok=True)
for shot in shots:
    shutil.copy2(shot, out / shot.name)
shutil.copy2(review / 'compile-check.txt', out / 'compile-check.txt')
(out / 'capture-summary.txt').write_text(summary, encoding='utf-8')

labels = {
    'menu': '主菜单', 'pause': '暂停 · 贝壳休憩', 'chapters': '章节 · 水流路径',
    'records': '存档 · 旅程卡片', 'guide': '教学 · 操作图鉴', 'journal': '日志 · 意识手账',
    'journal-empty': '日志 · 空状态', 'dialogue': '叙事 · 底部回声', 'dialogue-choices': '叙事 · 分支选择',
    'construction': '建构 · 配方蓝图', 'construction-preview': '建构 · 世界预览',
    'construction-preview-narrow': '建构 · 窄屏预览', 'completion': '结算 · 日光统计',
    'ending': '结局 · 空状态', 'ending-sample-branches': '结局 · 示例分支',
    'failure': '失败 · 救生浮环', 'credits': '署名 · 共创星图', 'confirmation': '危险操作 · 确认',
    'controller-layout': '手柄 · 设备图示', 'keyboard-layout': '键鼠 · 设备图示',
    'controller-layout-narrow': '手柄图示 · 窄屏', 'keyboard-layout-narrow': '键鼠图示 · 窄屏',
    'hud': 'HUD · 简洁引导', 'hud-detailed': 'HUD · 详细模式', 'hud-guidance': 'HUD · 情境图形引导',
    'subtitle-narrow': '字幕 · 窄屏避让', 'loading': '加载 · 水流进度',
    'menu-contrast': '主菜单 · 高对比', 'pause-contrast': '暂停 · 高对比', 'menu-wide': '主菜单 · 宽屏',
    'pause-narrow-large-text': '暂停 · 窄屏大字号', 'settings-narrow-large-text': '设置 · 窄屏大字号',
    'menu-english': '主菜单 · 英文', 'settings-pseudo': '设置 · 长文本压力',
    'settings-keyboard-options': '键鼠 · 输入参数', 'settings-controller-options': '手柄 · 输入参数',
    'settings-controller-portrait': '手柄 · 竖屏大字号', 'settings-controller-portrait-binding': '手柄 · 绑定行重排',
    'keyboard-capture': '键鼠 · 等待改键', 'controller-capture': '手柄 · 等待改键',
    'binding-conflict': '改键 · 冲突确认',
}
for i, label in enumerate(('声音', '画面', '键鼠', '手柄', '辅助', '语言')):
    labels[f'settings-{i}'] = f'设置 · {label}'

font_path = 'C:/Windows/Fonts/msyh.ttc'
heading = ImageFont.truetype(font_path, 30)
caption = ImageFont.truetype(font_path, 20)
small = ImageFont.truetype(font_path, 17)

def contact_sheet(names, filename, title, cell_width=400):
    columns, gap, pad = 4, 18, 24
    cell_height, header = round(cell_width * 9 / 16) + 38, 124
    sheet = Image.new('RGB', (pad * 2 + columns * cell_width + (columns - 1) * gap,
                             header + math.ceil(len(names) / columns) * (cell_height + gap) + pad), '#fff9eb')
    draw = ImageDraw.Draw(sheet)
    draw.text((pad, 16), title, font=heading, fill='#284141')
    draw.text((pad, 62), f'实际 Unity 页面渲染 · {expected} 个页面与状态 · 点击审查报告中的链接查看原尺寸', font=small, fill='#307570')
    for i, color in enumerate(('#fff9eb', '#fff2d4', '#f1b951', '#307570', '#b1523c')):
        x = pad + i * 190
        draw.rounded_rectangle((x, 91, x + 26, 114), radius=4, fill=color, outline='#cfb992')
        draw.text((x + 34, 92), color.upper(), font=small, fill='#284141')
    for i, name in enumerate(names):
        x = pad + (i % columns) * (cell_width + gap)
        y = header + (i // columns) * (cell_height + gap)
        draw.text((x, y), labels.get(name, name), font=caption, fill='#284141')
        with Image.open(out / (name + '.png')) as picture:
            picture = picture.convert('RGB')
            picture.thumbnail((cell_width, cell_height - 38), Image.Resampling.LANCZOS)
            sheet.paste(picture, (x + (cell_width - picture.width) // 2, y + 34))
    sheet.save(out / filename, optimize=True)

main = ['menu', 'pause', 'chapters', 'records', 'settings-0', 'settings-2', 'settings-3', 'guide',
        'journal', 'dialogue', 'hud', 'construction', 'construction-preview', 'completion',
        'ending-sample-branches', 'failure', 'credits', 'loading']
contact_sheet(main, 'overview.png', '泡涌 · 各模块设计总览')
contact_sheet([p.stem for p in shots], 'all-states.png', '泡涌 · 页面与状态检查', 320)

sources = []
mismatches = []
for file in sorted((review / 'Assets/GameMain/Scripts/UI/V2').glob('*.cs')):
    relative = file.relative_to(review)
    digest = hashlib.sha256(file.read_bytes()).hexdigest()
    sources.append({'path': relative.as_posix(), 'sha256': digest})
    original = root / relative
    if not original.is_file() or hashlib.sha256(original.read_bytes()).hexdigest() != digest:
        mismatches.append(relative.as_posix())
manifest = {'captures': expected, 'source_project': str(root), 'unity': '2022.3.34f1',
            'rendered_sources': sources, 'source_changes_since_capture': mismatches,
            'images': [{'file': p.name, 'title': labels.get(p.stem, p.stem)} for p in shots]}
(out / 'manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding='utf-8')
print(f'Collected {expected} captures into {out}')
print('Source changes since capture:', mismatches)
