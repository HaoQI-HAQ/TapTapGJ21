# Map0–Map5 泡泡模板统一

已将共享角色 `Assets/GameMain/Art/Models/takoyaki.prefab` 的浮粘、弹力、炸弹模板分别设为 `nian_p.prefab`、`tan_P.prefab`、`bao_P.prefab`，与 cs 主角使用的泡泡网格及材质一致。

Map0、Map2、Map3、Map4 由 UI 运行时创建共享角色，自动继承这三项配置。Map1 的已失效角色引用已映射到当前角色预制体，并保留原出生位置。Map5 移除三项场景模板覆盖及三个仅供克隆的模板对象，改为继承共享配置；炸弹最小费用 6、最小尺寸 0.3 的关卡设置保留。

独立 Unity 工程逐关运行 Map0–Map5，验证全部 18 组泡泡的实际生成、类型、网格、材质、资源扣除、切换锁定和释放后的返还费用。107 项行为断言通过，Map5 多余模板对象清理也通过。

验证程序仍记录了 4 条 Map4 环境模型导入错误：独立副本没有 glTF 导入器，无法解析三个 glb 模型，另有一条场景打开错误汇总。这三个模型及 GUID 在正式 clone 中均存在；这些错误不涉及本次泡泡模板，107 项行为断言仍全部运行通过。本次结果用于确认泡泡生成和材质引用，未验证关卡完整渲染效果。原始输出见 `report.txt`。

验证工程：`D:/TapTap21/PAO-UI-MergeValidation-20261014`。临时验证协程：`Assets/GameMain/Scripts/UI/V2/UIVerificationRunner.MapBubbles.cs`，通过副本的 `RunFormalCLI` 运行；正式工程没有添加验证脚本或修改玩法代码。

修改后重新进入 Play，已创建角色会在新一轮运行中使用更新后的配置。
