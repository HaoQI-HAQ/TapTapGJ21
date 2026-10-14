"""Collect the simplified menu's successful reports, images and validated source snapshot."""
from pathlib import Path
import hashlib
import re
import shutil
import sys
import zipfile

root = Path(__file__).resolve().parents[2]
test = Path(sys.argv[1]).resolve()
stage = sys.argv[2] if len(sys.argv) > 2 else 'layout'
if stage not in ('layout', 'polish'):
    raise SystemExit('The optional stage must be layout or polish.')
if not (test / 'PAO_UI_VERIFICATION_PROJECT').is_file():
    raise SystemExit('Expected an isolated UI verification project.')

# Check both groups before writing the delivery directory.
groups = []
for name, folder in [('menu', 'MenuVerificationArtifacts'), ('regression', 'VerificationArtifacts')]:
    source = test / folder
    report = (source / 'report.txt').read_text(encoding='utf-8-sig')
    if not re.search(r'^Errors: 0$', report, re.M):
        raise SystemExit('Refusing unsuccessful verification: ' + name)
    count = int(re.search(r'^Passed: (\d+)$', report, re.M)[1])
    images = re.findall(r'^Rendered (?:diagnostic world and HUD: )?([^\n]+)\.$', report, re.M)
    for image in images:
        if not (source / (image + '.png')).is_file():
            raise SystemExit('Missing verification image: ' + image)
    groups.append((name, source, count, images))

dest = root / ('Docs/UI-V2/menu-' + stage)
for name, source, count, images in groups:
    target = dest / name
    target.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source / 'report.txt', target / 'report.txt')
    for image in images:
        shutil.copy2(source / (image + '.png'), target / (image + '.png'))
    print(name, 'passed:', count, 'errors: 0, images:', len(images))

snapshot = sorted((test / 'Assets/GameMain/Scripts').rglob('*.cs'))
snapshot += [test / 'Assets/GameMain/Resources/PAOUIConfig.asset', test / 'ProjectSettings/InputManager.asset']
hashes = []
with zipfile.ZipFile(dest / 'verified-source.zip', 'w', zipfile.ZIP_DEFLATED) as archive:
    for source in snapshot:
        relative = source.relative_to(test).as_posix()
        archive.write(source, relative)
        hashes.append(hashlib.sha256(source.read_bytes()).hexdigest() + '  ' + relative)
(dest / 'verified-source-sha256.txt').write_text('\n'.join(hashes) + '\n', encoding='utf-8')

table = '\n'.join('| ' + name + ' | ' + str(count) + ' | 0 | ' + str(len(images)) + ' |'
                  for name, source, count, images in groups)
verification = '''# 主菜单精简与布局验收

2026-10-13，Unity 2022.3.34f1。运行脚本编译通过，仅有原有 `LevelPathPanel.m_DragStartedByTouch` 未使用警告。

| 验证组 | 通过记录 | 运行错误 | 截图 |
| --- | ---: | ---: | ---: |
''' + table + '''

通过记录包括行为断言、布局及焦点检查和截图记录。原始报告：[主菜单专项](menu/report.txt)、[完整 UI 回归](regression/report.txt)。专项从实际 Menu 场景启动，验证继续进入实际 Map0 并恢复位置与意识碎片；完整回归覆盖示例场景中的设置、叙事、建构、结算、玩法提示和 DOTween 交互。

## 布局调整

- 按规划第 5 部分突出居中标题、水母纸感背景与单列入口；默认选择继续游戏，没有有效记录时选择新的旅程。
- 移除常驻旅程概览、碎片/时长统计、重复存档时间、入口解释、口号和导航提示。必要的记录异常仍提供简短恢复说明。
- 标题、菜单滚动区与底部帮助使用独立布局。按钮根据文字高度扩展，大字号时滚动并自动露出焦点，避免覆盖固定区域。
- 保留背景低频呼吸、按钮交互反馈、记录恢复与危险操作确认。高对比按钮使用相反的黑白文字和底色，并保持默认焦点边界可见。

## 实际画面与无重叠检查

覆盖 1600×900、1280×800、1920×820、960×540、用户截图的 934×534，以及 800×900 窄窗口。130% 字号、120% UI 缩放、伪本地化、高对比和减少动态组合均检查文字可读性与聚焦可见性。

每张主菜单截图及逐项聚焦后，使用控件实际四角计算可见矩形，先裁切滚动区域，再检查标题、副标题、所有入口、页面提示和帮助入口之间是否交叠，避免把已裁切的滚动内容算作遮挡。

- [用户窗口大小：934×534](menu/menu-returning-934x534.png)
- [首次进入](menu/menu-new-1600x900.png)
- [超宽窗口](menu/menu-returning-1920x820.png)
- [窄窗口](menu/menu-returning-800x900.png)
- [大字号与伪本地化](menu/menu-accessible-pseudo.png)
- [高对比模式](menu/menu-high-contrast-934x534.png)

截图来自隔离 Unity 工程的实际 uGUI 渲染。代码和配置快照见 [verified-source.zip](verified-source.zip)，校验值见 [verified-source-sha256.txt](verified-source-sha256.txt)。报告对应该快照；不将其他会话后续改动自动视作已验收。导航测试使用 Unity 事件注入，未进行实体手柄测试。

## 重现

在工程外准备独立目录，依次运行两项 Unity 验证，等待每次进程退出且报告为 `Errors: 0` 后收集。验证使用独立存档目录。

```powershell
python Tools/UIV2/prepare_validation.py C:/PAOMenuLayoutValidation
& 'D:/Unity 2022 3 34f1/Unity 2022.3.34f1/Editor/Unity.exe' -batchmode -noUpm --burst-disable-compilation -projectPath C:/PAOMenuLayoutValidation -executeMethod PAO.EditorTools.UIMenuVerification.RunCLI -logFile C:/PAOMenuLayoutValidation/menu.log -screen-width 1600 -screen-height 900
# 等待上述 Unity 退出后再运行完整回归。
& 'D:/Unity 2022 3 34f1/Unity 2022.3.34f1/Editor/Unity.exe' -batchmode -noUpm --burst-disable-compilation -projectPath C:/PAOMenuLayoutValidation -executeMethod PAO.EditorTools.UIV2Verification.RunAllCLI -logFile C:/PAOMenuLayoutValidation/regression.log -screen-width 1600 -screen-height 900
# 等待第二次 Unity 退出后收集。
python Tools/UIV2/collect_menu_layout_results.py C:/PAOMenuLayoutValidation
```
'''
if stage == 'polish':
    verification = verification.replace('主菜单精简与布局验收', '主菜单补充优化验收')
    start = verification.index('## 布局调整')
    end = verification.index('## 实际画面与无重叠检查')
    details = '''## 补充优化

- 延续当前左侧菜单、右侧水母插画的构图。限制菜单列宽，统一标题、副标题、菜单边界；窄窗口改为居中列，英文和长标题按空间缩放并换行。
- 主入口使用更大的点击区域和字号，底部图鉴、制作人员与离开保持轻量外观，并按文字大小自动换行布局。
- 菜单图标使用 140 ms DOTween 反馈，在预留范围内移动并随焦点变色；减少动态立即复位，快速悬停替换旧 Tween，隐藏或销毁时终止。
- 复用共用滚动条，仅在内容超出视口时显示，不额外添加说明文字。滚动条不进入默认焦点与方向导航，修复危险确认可能先选中滚动条的问题。
- 保留精简的信息结构、有效记录继续入口、异常恢复、取消确认与返回焦点。高对比主按钮文字对比度至少 7:1。

专项还验证悬停图标不触碰文字、连续快速悬停回到正确终态、动态设置立即生效、标题完整可读与滚动条按内容显隐。原有主菜单布局验收保留在 `../menu-layout/`。

'''
    verification = verification[:start] + details + verification[end:]
    verification = verification.replace('python Tools/UIV2/collect_menu_layout_results.py C:/PAOMenuLayoutValidation\n',
                                        'python Tools/UIV2/collect_menu_layout_results.py C:/PAOMenuLayoutValidation polish\n')
(dest / 'verification.md').write_text(verification, encoding='utf-8')
