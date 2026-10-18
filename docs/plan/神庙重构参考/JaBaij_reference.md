# Map2 · Ja Baij / 加·巴希夫 · 原始布局参考

记录日期：2026-10-18（Asia/Hong_Kong）。只读研究；没有修改 Unity 场景。

## 依据与数据文件

- 名称对应：`https://objmap.zeldamods.org/game_files/text/StaticMsg/Dungeon.json` 中 `Dungeon041 = Ja Baij Shrine`、`Dungeon041_sub = Bomb Trial`。
- 查询：`https://radar.zeldamods.org/objs/CDungeon/Dungeon041?q=map%3ADungeon041&withMapNames=true&limit=-1`。
- 原始静态数据：`https://raw.githubusercontent.com/zeldamods/radar/master/content/map/CDungeon/Dungeon041/Dungeon041_Static.json`。
- 原始动态数据：`https://raw.githubusercontent.com/zeldamods/radar/master/content/map/CDungeon/Dungeon041/Dungeon041_Dynamic.json`。
- 模型包围盒：`https://raw.githubusercontent.com/zeldamods/radar/master/content/ActorInfo.product.json`。

本目录 `Dungeon041_objects.json` 共 785 条：73 条 Static、712 条 Dynamic。每条 API 记录都已通过 HashId 关联完整 `data`（Translate、Rotate、可选 Scale、参数及链接），没有缺失关联。另存 `Dungeon041_Static.json`、`Dungeon041_Dynamic.json`，前者含唯一往返平台轨道。

**这是社区发布的游戏放置数据，可用于保留原始坐标和比例；没有原始网格、材质和相机参数，不能称像素级复刻。** 对象的包围盒也不是准确的碰撞体；坡、梯、门框、房间外壳不能直接填成实心大立方体。

## 坐标约定

以下均为原始 `(X,Y,Z)`，未平移、未翻转。主要前进方向为 `-Z`；如 Unity 需要 `+Z` 前进，应统一使用刚体旋转或全局坐标转换，不可只改位置而漏改朝向。

- Rotate 为标量时表示 Y 轴旋转，单位弧度；数组为 XYZ 弧度。
- Translate 是模型原点，不一定是中心或台面高度。大部分 Box/Crack 的本地 Y 从 0 起，故 Unity 方块中心需要加上变换后的包围盒中心。
- `BlockCrack4x4x4` 本地 AABB 为 `(-2,0,-2)` 到 `(2,4,2)`。
- `DgnObj_Hrl_BoxCover8x8_01` 本地 AABB 为约 `(-4,-0.04,-4)` 到 `(4,0,4)`：这是薄盖面，不是 8 米高实体。
- `DgnObj_Hrl_RailLift_6x6_01` 本地 AABB 为 `(-3,-0.715664,-3)` 到 `(3,0.024609,3)`。
- BaseRoom60x180x20 的整体 AABB 包括外部构造，不能将其当作可行走地面或实心墙；应单独重建薄地板与外围墙。

## 主流程与机关

| 节点 | 原始位置 / 区间 | 可确认内容 |
|---|---|---|
| 出生平台 | `(-18,2,25)` | `DgnObj_Hrl_Warpin_2x2_01`；重置点 `(-18,2,20)` 朝 -Z |
| 炸弹能力终端 | `(-26,2.05,11)` | 位于入口区左侧，不在入口中心线上 |
| 滚落坡 | `(-18,0,12)` | `Slope8x4x2`，Y 旋转 -180° |
| 第一面炸墙 | `(-20,0,0)`、`(-16,0,0)` | 两个 4×4×4 方块并排；底 Y=0、顶 Y=4 |
| 右侧宝箱支路 | 宝箱 `(-6,0,-4)`；阻块 `(-8,0,-8)`、`(-4,0,-8)` | 宝箱支路位于首墙之后的右手方向；需要进入横廊后再转向宝箱，不能放在首墙之前 |
| 左侧主路阻块 | `(-27.9,0,-12)`、`(-27.9,0,-16)` | 通向梯子与渡台区域 |
| 上层梯子 | `(-28,0,-22)`、`(-22,0,-24)` | 两个 1×4 梯子，后一梯 Y 旋转 90°；均从 Y=0 升至约 Y=4 |
| 炸弹渡台 | actor `(-15,3.5,-28)` | 6×6 平台；轨道见下；真实运动沿 X |
| 渡台对面炸墙 | X=-4，Z=-24/-28/-32；每列底 Y=4 和 8 | 共 6 块，3 列×2 层；**墙面垂直于渡台运动方向**，不是过河再转弯遇墙 |
| 墙后 T 形路面 | `(4,4,-28)` 为 RoadTJunction；向右到 `(16,4,-28)` 再向 -Z；另一路在 X=4 向 -Z | 路面是地面装饰/导向，不等于独立悬空走道；保留周围实体台面 |
| 下到末厅底层 | Stair8x8x4 `(4,0,-52)`，Y 旋转 -180° | 上面 Y=4 通道下降到底部 Y=0；底层路面 X=4 延至 Z=-64，再右转 X=24 |
| 左侧人物弹射 | 活塞 `(-23,3,-46.5)`；基座 `(-23,4,-53)` | 朝 -Z；对应宝箱落点 `(-23,4,-77)` |
| 中左示范弹球 | 活塞 `(-10,3,-46.5)` 与对向 `(-10.5,3,-77.5)` | 两个活塞相向往复传球；初始球 `(-10,5.5,-50.5)` |
| 右侧炸弹弹射 | 活塞 `(24,5,-47)`；基座 `(24,6,-53.5)` | 朝 -Z；比左侧机构高 2 个单位 |
| 炸弹漏管 | `(18,8,-51.2)`，Y 旋转 90° | 侧面投入口连接右侧弹射位置；不是沿主流程铺设的长炮管 |
| 末端梯 | `(24,0,-74)` | 1×6 梯，从底层 Y=0 上到目标台面 Y=6 |
| 末端炸石堆 | X=20/24/28，Z=-76/-80/-84，底 Y=6；上层在 Y=10 | 共 14 块，见下；是右侧目标台的体积石堆，不是横封全厅的一面长墙 |
| 祭坛折返 | Y=6 的道路从 `(24,6,-89.5)` 向左到 `(8,6,-89.5)`，再转 -Z | 右侧目标后向左拐，再向前上台阶 |
| 祭坛台阶/终点 | Stair4x8x2 `(8,6,-98)`；GoalSystem `(8,8,-111.5)` | 通关点偏右，不在旧场景 X=0 中轴线上 |

## 平台与活塞参数

### 往返平台

`HashId = 1163260337`，`DgnObj_Hrl_RailLift_6x6_01`。

- 放置：`(-15,3.5,-28)`；RotateY=-π/2。
- `RailMoveSpeed = 0.1`，参数单位不能未经验证当作 Unity 米/秒。
- 轨道 `3296105458`：`(-9,4,-28)` ↔ `(-19,4,-28)`，线性，单程 10 单位。
- 两端 `WaitFrame = 45`。初始模型位置与轨道 Y 有 0.5 差，导入时应明确按原 actor 或按轨道基准构造，不能混为同一个中心值。

### 弹射柱（共四个）

三个近侧机构的旋转数组约 `(-135°,0°,180°)`；对向机构约 `(-135°,180°,180°)`。所有 `MoveAxis = 1`、`MoveDis = 5`。人物弹射柱 MoveSpeed=1.2，其余为1.0。

注意 `DgnObj_BoxShutter_4x4x4` AABB 是 `(-2.0812,-12,-2.0812)` 到 `(2.0812,4,2.0812)`，其长活塞轴有 16 单位。用完整 AABB 替代会把凹槽填实，适合表示外形范围但不等于正确碰撞；建议分开短推面、长杆和不带碰撞的基座包络。

左侧 `SwitchTimer` ID 786521806 的 `WaitTime=120`，关联 `LinkTagAnd` 3833490079，同时驱动近侧两活塞，另经反向逻辑驱动远侧回球活塞。右侧 Timer 1680758514 的 `WaitTime=120`，经 LinkTagAnd 2519065250 驱动右侧炸弹活塞。此处计时参数的具体帧率换算需要实现时验证，不能仅凭数值推定 120 秒。

## 全部炸块分组（26块）

1. 首墙：2块，`(-20,0,0)`、`(-16,0,0)`。
2. 首宝箱入口：2块，`(-8,0,-8)`、`(-4,0,-8)`。
3. 左通道：2块，`(-27.9,0,-12)`、`(-27.9,0,-16)`。
4. 渡台目标：6块，X=-4，Y=4/8，Z=-24/-28/-32。
5. 末厅目标：14块。底层9块：X=20/24/28 × Z=-76/-80/-84，Y=6。上层5块：`(20,10,-79)`、`(20,10,-83)`、`(24,10,-80)`、`(28,10,-79)`、`(28,10,-83)`。

## 对旧白盒需要修正的事实

旧 `BuildMap2ShrineGeometry` 当前不是这一坐标布局，不能用整体缩放修正：终端在中轴线；宝箱入口在首墙前；渡台目标墙落在转弯之后；末厅只有两根炮；没有相向传球示范；末端石堆被替换为长墙；终点被改到中轴线。应依据上述完整放置数据重建，而不是继续微调旧坐标。

## 可视参考链接（备用）

本次浏览器工具在子代理里超时，因此以下原作截图 URL 已从网页链接中确认可访问，但本次没有获得可视像素，**不将其作为已经逐帧核对的证据**。本次结论主要来自上述原始坐标与对象类型。

- T廊：`https://www.gamerguides.com/assets/media/17/144/The_Legend_of_Zelda_Breath_of_the_Wild_Ja_Baij_Shrine_Trial.jpg`
- 渡台：`https://www.gamerguides.com/assets/media/17/144/The_Legend_of_Zelda_Breath_of_the_Wild_Ja_Baij_Shrine_Trial_2.jpg`
- 末厅：`https://www.gamerguides.com/assets/media/17/144/The_Legend_of_Zelda_Breath_of_the_Wild_Ja_Baij_Shrine_Trial_3.jpg`
- 炸弹管：`https://www.gamerguides.com/assets/media/17/144/The_Legend_of_Zelda_Breath_of_the_Wild_Ja_Baij_Shrine_Trial_5.jpg`
- 末端：`https://www.gamerguides.com/assets/media/17/144/The_Legend_of_Zelda_Breath_of_the_Wild_Ja_Baij_Shrine_Trial_7.jpg`
- 用户录像：`https://www.bilibili.com/video/BV1DE411E7kD`。
