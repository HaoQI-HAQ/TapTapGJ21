# Map3 原作空间参考：摩·拉塔尼阿 / Mogg Latan / Synced Swing

采集日期：2026-10-18。只读研究；未修改 Unity。

## 证据等级与复刻范围

- 名称和关卡 ID 已由游戏文本抽取数据确认：`Dungeon058` = `Mogg Latan Shrine`，副标题 `Synced Swing`。
- 主要依据已从攻略文字升级为公开的原作物件放置数据。Static 117 项 + Dynamic 748 项，共 865 项，含 `Translate`、`Rotate`、`Scale`、`HashId`、物理约束关联和部分机关参数。
- 坐标是原作世界单位；可按 1 原作单位 = 1 Unity 单位保持摆放比例。`Rotate` 的角度单位是弧度；标量旋转表示 Y 轴。不要把弧度直接当 Unity 角度。
- ActorInfo 的 `aabbMin/aabbMax` 是模型包围盒，可以还原比例与枢轴偏移，但不是原始网格，也不是精确碰撞体。复杂房壳、拱门、阶梯、机关不能直接用一整块包围盒填实。因此可以准确声明“按原作物件坐标与包围尺寸重建白盒”，不能声明“像素级模型复刻”。
- 本子任务浏览器对图片/视频页面连续超时；网页图片搜索返回的文字说明不作为亲眼观察证据。以下几何与数量结论来自原数据，流程语义由攻略交叉核对。

## 原始数据与资料

- 名称映射：https://objmap.zeldamods.org/game_files/text/StaticMsg/Dungeon.json
- Static：https://raw.githubusercontent.com/zeldamods/radar/master/content/map/CDungeon/Dungeon058/Dungeon058_Static.json
- Dynamic：https://raw.githubusercontent.com/zeldamods/radar/master/content/map/CDungeon/Dungeon058/Dungeon058_Dynamic.json
- 模型 bounds：https://raw.githubusercontent.com/zeldamods/radar/master/content/ActorInfo.product.json
- 可交叉查询的物件索引：https://radar.zeldamods.org/objs/CDungeon/Dungeon058?q=map%3ADungeon058&withMapNames=true&limit=-1
- 攻略及原作截图页面：https://www.zeldadungeon.net/breath-of-the-wild-walkthrough/shrine-locations/mogg-latan-shrine-guide/
- 中文完整跑关录像候选：https://www.youtube.com/watch?v=Hv-69axLFwg
- 用户提供的合集：https://www.bilibili.com/video/BV1DE411E7kD

本地数据：`Dungeon058_Static.json` / `Dungeon058_Dynamic.json` 是批量下载的原结构；`Dungeon058_full.json` 是 865 条 API 包装数据（每条真实放置字段位于 `.data`）；`Dungeon058_objects.json` 是位置与名称索引。

## 空间拓扑

入口位于正 Z 端，祭坛位于负 Z 端；主要流程从 Z≈60 走向 Z≈-101。房间是长厅中的悬空折线路径，不是十字房间。原作主模块名为 `DgnObj_Hrl_BaseRoom60x180x20_B_01`。

```text
                      祭坛 / Z -101
                           |
                  双扇金属门 / Z -86.5
                吊灯 -4,+4 / Z -78
                           |
        阶梯滑块组 X0 / Z -72 / Y -17,-15,-13,-11
                    （宝箱在下层 Z-76.5）
                           |
                    斜轨滑块 / X≈0,Z≈-32
                       ↗
   最后大刺球 X-12,Z-37.5 —— 分路（宝箱继续Z-43）
            |
   大刺球 X-12,Z-23
            |
   大刺球 X-12,Z-13.5          右侧宝箱 X≈8,Z≈-13
            |                         |
   小刺球 X-12,Z-4.5  ——  吊桥落脚区
                                |
                     木吊桥 X-2,Z2.2
                                |
                 第二悬摆板 X-7,Z29.5
                              ↗
                  第一悬摆板 X2.25,Z45.25
                                |
                       入口 X0,Z60
```

这张文字拓扑表示连接关系，不替代按所有静态模块坐标生成的平面图；具体折角、接岸和高度以原数据为准。

## 关键机关：原始 actor 枢轴坐标

下表 Y 为 actor 的 Translate，不一定是台面。平台脚底高度必须加上旋转后的局部 bounds。

| 编号 | 物件 | HashId | 原始位置 X,Y,Z | 已确认尺寸 / 状态 |
|---|---|---:|---|---|
| S | 入口升降台 | 1294403915 | 0,-6,60 | 升降台 actor 初始位置，不应当成玩家入口地面高度 |
| 01 | 第一悬摆板 | 4166417922 | 2.25,-16.220314,45.25 | 4×0.3×4；top=-15.920314；初始未旋转 |
| 02 | 第二悬摆板 | 3976724771 | -7,-18.612499,29.5 | 4×0.3×4；top=-18.312499；Y转90° |
| 03 | 木吊桥 | 2150642217 | -2,-15.7,2.2 | 竖直模型约4宽8长0.667厚；初始X转5° |
| 04A | 小刺球 | 455512060 | -12,-14.757813,-4.5 | bounds 半径1.000980 |
| 04B | 大刺球1 | 2184175174 | -12,-12.865623,-13.5 | bounds 半径2.561550 |
| 04C | 大刺球2 | 311477207 | -12,-12.862499,-23 | bounds 半径2.561550 |
| 04D | 大刺球3 | 2115379704 | -12,-12.870312,-37.5 | bounds 半径2.561550；保护弓宝箱旁路径 |
| 05 | 斜轨滑块 | 1631371168 | 0.237500,-15.390625,-31.629028 | 4×2×4；Rotate=(-3.071779,0,π) |
| 06A | 底层滑块 | 290868015 | 0,-17,-71 | 4×2×4，初始台面-15 |
| 06B | 第2滑块 | 1716616121 | 0,-15,-72 | 4×2×4，初始台面-13 |
| 06C | 第3滑块 | 2287934101 | 0,-13,-72 | 4×2×4，初始台面-11 |
| 06D | 第4滑块 | 1877424018 | 0,-11,-72 | 4×2×4，初始台面-9 |
| 07 | 水晶开关 | 1778907467 | 3,-8,-73 | BasicSig → LinkTagAnd 4149587382 |
| 08L/R | 两盏悬吊灯 | 3209385601 / 556778274 | -4,4,-78 / 4,4,-78 | 绳索长度Scale.Y=4.5，有GuideRef |
| 09L/R | 双扇金属门 | 2310007186 / 1158015244 | 3,4.6,-86.5 / -3,4.6,-86.5 | 注意左右门模型命名与世界X反向；两门通过CogWheel约束联动 |
| 09W | 阻门木构件 | 1338040341 | 0,4.8,-86 | 与两门FixedCs连接，焚毁后解除 |
| E | 祭坛基座 | 1054236540 | 0,-1.5,-101 | 与Npc、GoalExit不是同一物件 |
| E | 通关点 | 66204640 | 0,0,-97.5 | DgnObj_GoalExit |

## 悬挂和运动关系

### 两块摆台

- 第一块由四条 `RopeWire` 连到同一 `ConstraintDummy` 1501174257，dummy 坐标 `(2.25,-7.5,39.5)`。
- 第一组 RopeWire 初始 X 旋转 -35°、Scale.Y=10，四个上端位置为 X=0.5/4，Y=-7.403，Z=37.774956/41.274956。下端通过 `Reference` 指向 `FldObj_ChainEyeBolt_A_01`，其 `ModelBind` 固定到摆板四角。这也说明第一块初始已经偏到摆幅的一侧，不能只让板原地旋转。
- 第二块四条 RopeWire，Scale.Y=8，起点 X=-8.75/-5.25、Y=-10、Z=27.75/31.25，Reference 指向第二摆板角螺栓。公共 dummy 1825148117 位于 `(-5.5,-9,29.5)`。
- 不要把这两组四条绳都简单收束到同一个视觉点；应保留原四角连接布局。

### 木吊桥

- `DgnObj_HingeBoard_A` 通过 `LimitHingeCs` 连接 dummy 1919206521；Axis=(1,0,0)，LimitAngMin=0，Max=92，Friction=0。
- 两条可断 `Rope` 初始坐标 X=-3.9/-0.1、Y=-1、Z=2.9，Scale.Y=6.5；其下端 Reference 绑定吊桥上端左右两个眼栓。
- `DgnObj_HingeBoard_A_02` 是静态铰接/底座配件，坐标(-2,-16,2)，Y转180°，不要误当成第三块悬摆台。

### 刺球

- 1小+3大，共4颗；每颗都有对应 RopeWire 和眼栓。
- 绳索视觉顶端分别为 `(-12,-7.5,-4.5)`、`(-12,-3.5,-13.5)`、`(-12,-2,-23)`、`(-12,-2,-37.5)`；绳长分别6、6.5、8、8。
- 对应 constraint dummy 分别(-14,-7,-4.5)、(-14,-4,-13.5)、(-13.5,-1.5,-23)、(-13.5,-1.5,-37.5)，均Y转90°。

### 滑块

- 斜轨滑块 SliderCs 范围[-4,20]、Friction=10，目标dummy 4164283930。Axis=(0,0,1)。动态轨道几何实际连续排列在 X≈0.2375、Z=-25.6436…-53.5754，Y随Z略微下降，因此实际轨迹沿Z并带约4°坡度。不要套用dummy旋转猜运动方向。
- 四个阶梯滑块的 SliderCs Axis=(0,0,1)，底层范围[-1,15]，其他[0,16]；Friction=30000。对应轨道实体 X=2、Z=-54…-74，分布在Y=-10/-12/-14/-16四层，实际轨迹沿Z。目标dummy虽然Y转90°，不代表滑块沿世界X移动。
- 原作语义为磁力拉动滑块，不能把“自动往返”称为原作等同实现。若本项目适配成自动移动，流程图应注明适配。

### 最终门和吊灯

- 水晶开关控制吊灯组，灯下落后能被摆动/断绳，引燃门前藤蔓/木件；门通过高摩擦铰链（100000）限制角度±80°。
- 门原始Rotate=(π/2,≈0,-π/2)，不可忽略旋转。
- Rope的Rails是原作绳索导引点，并非玩家平台往返轨道；不能把这些短方形Rails做成移动平台路径。

## 白盒导入注意

1. 按 Static/Dynamic 全量数据导入基本模块，保留HashId，避免手编平台坐标。
2. `localPosition=(aabbMin+aabbMax)/2`，`localScale=aabbMax-aabbMin`，再施加actor的Scale/Rotate/Translate；否则大量Bottom pivot砖块会偏移一个半高。
3. `DgnObj_Hrl_BaseRoom60x180x20_B_01` 为中空房壳，aabb外界很大；绝对不要填成实体Cube。拱门、洞口、坡道、楼梯、轨道和房壳均需分件/专门形状。
4. 普通Box模块可以用真实AABB，`Arch`不可整块填实。`Slope`用楔体，吊桥用以铰接端为pivot的长板。
5. 本数据没有原作视觉网格、材质和所有物理行为。按数据复原位置尺寸与原作完整机关/像素级外观是不同交付等级。
