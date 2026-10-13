"""Collect successful menu and shared UI regression artifacts from the isolated test project."""
from pathlib import Path
import hashlib
import re
import shutil
import sys
import zipfile

root = Path(__file__).resolve().parents[2]
test = Path(sys.argv[1]).resolve()
if not (test / 'PAO_UI_VERIFICATION_PROJECT').is_file():
    raise SystemExit('Expected an isolated UI verification project.')
dest = root / 'Docs/UI-V2/menu-design'
counts = []
for name, source_name in [('menu', 'MenuVerificationArtifacts'), ('regression', 'VerificationArtifacts')]:
    source = test / source_name
    report = (source / 'report.txt').read_text(encoding='utf-8-sig')
    if not re.search(r'^Errors: 0$', report, re.M):
        raise SystemExit('Refusing to collect unsuccessful verification: ' + name)
    target = dest / name
    target.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source / 'report.txt', target / 'report.txt')
    screenshots = re.findall(r'^Rendered ([^\n]+)\.$', report, re.M)
    for screenshot in screenshots:
        shutil.copy2(source / (screenshot + '.png'), target / (screenshot + '.png'))
    counts.append((name, int(re.search(r'^Passed: (\d+)$', report, re.M)[1]), len(screenshots)))
    print(name, report.splitlines()[0], 'screenshots:', len(screenshots))

snapshot = sorted((test / 'Assets/GameMain/Scripts').rglob('*.cs'))
snapshot += [test / 'Assets/GameMain/Resources/PAOUIConfig.asset', test / 'ProjectSettings/InputManager.asset']
hashes = []
with zipfile.ZipFile(dest / 'verified-source.zip', 'w', zipfile.ZIP_DEFLATED) as archive:
    for source in snapshot:
        relative = source.relative_to(test).as_posix()
        archive.write(source, relative)
        hashes.append(hashlib.sha256(source.read_bytes()).hexdigest() + '  ' + relative)
(dest / 'verified-source-sha256.txt').write_text('\n'.join(hashes) + '\n', encoding='utf-8')

summary = '\n'.join('| ' + name + ' | ' + str(count) + ' | 0 | ' + str(images) + ' |' for name, count, images in counts)
verification = '''# 主菜单与视觉细化验收

2026-10-13，Unity 2022.3.34f1。运行脚本编译通过；仅有原有 `LevelPathPanel.m_DragStartedByTouch` 未使用警告。

| 验证组 | 通过记录 | 运行错误 | 截图 |
| --- | ---: | ---: | ---: |
''' + summary + '''

通过记录包含行为断言、尺寸及焦点检查和截图记录。原始报告见 [真实主菜单专项](menu/report.txt) 与 [完整 UI 回归](regression/report.txt)。本轮独立工程从实际 `Assets/GameMain/UI/Menu.unity` 启动，验证继续游戏进入实际 Map0 并恢复位置和意识碎片。完整 UI 回归使用示例玩法场景覆盖设置、叙事、建构、结算与结局；它不代表六关路线或最终 URP 美术已通过验收。

## 本轮实现

- 主菜单重新组织品牌标题、水母插画、导航、操作说明和旅程概览；继续入口显示最近章节及记录时间，概览显示已完成水域、意识碎片与探索时间。
- 空记录默认选择新的旅程；损坏记录、有效备份与不可用场景分别说明恢复路径；提交继续游戏时重新校验记录。记录恢复或删除后，返回菜单同步刷新并恢复原入口焦点。
- 修复实际旧菜单画布隐藏时同时关闭其 EventSystem 的问题；新菜单保持一个可用的事件系统。所有记录失效时清理内存中的旧旅程，防止章节继续继承失效进度。
- 新旅程在清理进度前先确认第一章可进入，并对已有自动记录或备份进行确认；退出、删除和覆盖使用警示色确认按钮，默认焦点保持取消。
- 章节及存档使用暖白卡片、青绿当前状态和可读的不可用说明；制作人员读取现有配置并保留返回焦点。
- 共用按钮增加悬停、焦点和按下反馈；动效使用不受暂停影响的时间，隐藏和销毁时终止。减少动态时关闭缩放及插画呼吸。
- 主菜单限制超宽屏构图宽度；窄窗口切为单列。按钮根据文本换行和字号扩展高度；大字号下通过滚动及焦点自动露出全部入口。

## 视觉与输入验证

覆盖 1600×900、1280×800、1920×820、960×540 和 800×900，检查首次进入、已有记录、损坏记录、不可用场景；在 130% 字号、120% UI 缩放、伪本地化、高对比和减少动态组合下逐项检查按钮文字及焦点可见性。方向导航、上下层返回、退出取消、键位冲突、设置撤销和备份恢复均通过。

- [首次进入](menu/menu-new-1600x900.png)
- [已有记录与旅程概览](menu/menu-returning-1600x900.png)
- [超宽屏构图](menu/menu-returning-1920x820.png)
- [窄窗口单列](menu/menu-returning-800x900.png)
- [大字号及伪本地化](menu/menu-accessible-pseudo.png)
- [章节卡片](menu/chapter-cards.png)
- [存档卡片](menu/record-cards.png)
- [制作人员](menu/credits.png)

硬件手柄测试仍需实体设备；本轮输入验证使用 Unity 导航事件与现有测试注入。制作人员姓名、正式章节叙事及完整剧情翻译仍从内容配置提供，本轮沿用已有内容。源码与配置的验收快照见 [verified-source.zip](verified-source.zip)，SHA-256 见 [校验文件](verified-source-sha256.txt)。

## 重现

从主工程运行 `Tools/UIV2/prepare_validation.py`，参数使用工程外的独立路径。在该独立工程依次执行 `PAO.EditorTools.UIMenuVerification.RunCLI` 和 `PAO.EditorTools.UIV2Verification.RunAllCLI`，两次 Unity 运行分别等待退出，确认报告 `Errors: 0` 后运行 `Tools/UIV2/collect_menu_results.py` 收集。测试使用独立存档目录与产品名，不修改玩家的正式存档。

示例 PowerShell：

```powershell
python Tools/UIV2/prepare_validation.py C:/PAOMenuValidation
& 'D:/Unity 2022 3 34f1/Unity 2022.3.34f1/Editor/Unity.exe' -batchmode -noUpm --burst-disable-compilation -projectPath C:/PAOMenuValidation -executeMethod PAO.EditorTools.UIMenuVerification.RunCLI -logFile C:/PAOMenuValidation/menu.log -screen-width 1600 -screen-height 900
# 等待上述 Unity 进程退出后，再运行完整回归。
& 'D:/Unity 2022 3 34f1/Unity 2022.3.34f1/Editor/Unity.exe' -batchmode -noUpm --burst-disable-compilation -projectPath C:/PAOMenuValidation -executeMethod PAO.EditorTools.UIV2Verification.RunAllCLI -logFile C:/PAOMenuValidation/regression.log -screen-width 1600 -screen-height 900
# 等待第二次 Unity 进程退出后收集。
python Tools/UIV2/collect_menu_results.py C:/PAOMenuValidation
```
'''
(dest / 'verification.md').write_text(verification, encoding='utf-8')
