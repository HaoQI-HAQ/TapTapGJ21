using UnityEngine;

namespace PAO
{
    /// <summary>
    /// 泡泡通用参数。三种泡泡各持有一份，互不干扰。
    /// 想调哪种就展开哪一组，改这里不会影响另外两种。
    /// </summary>
    [System.Serializable]
    public class BubbleSettings
    {
        [Header("外观")]
        [Tooltip("泡泡预制体。留空则用代码生成的球体")]
        public GameObject prefab;

        [Tooltip("代码生成泡泡时的颜色")]
        public Color color = new Color(0.55f, 0.85f, 1f, 1f);

        [Header("蓄力尺寸（直径，米）")]
        [Tooltip("点按时泡泡的直径")]
        public float minSize = 0.2f;

        [Tooltip("蓄满力时泡泡的直径")]
        public float maxSize = 1.5f;

        [Header("发射速度（米/秒）")]
        [Tooltip("点按时的初速度")]
        public float minLaunchSpeed = 8f;

        [Tooltip("蓄满力时的初速度")]
        public float maxLaunchSpeed = 22f;

        [Header("浮力")]
        [Tooltip("最小泡泡的上升速度，最快")]
        public float smallRiseSpeed = 2.5f;

        [Tooltip("最大泡泡的上升速度，最慢")]
        public float largeRiseSpeed = 0.25f;

        [Tooltip("空气阻力。越大越快进入匀速，但飞得越近")]
        public float drag = 0.8f;

        [Tooltip("是否受重力。关掉即为漂浮")]
        public bool useGravity = false;

        [Header("寿命")]
        [Tooltip("存活时间（秒），到点自动消失")]
        public float lifeTime = 10f;

        [Header("泡泡值消耗")]
        [Tooltip("最小泡泡（点按）消耗的泡泡值")]
        public int minCost = 2;

        [Tooltip("最大泡泡（蓄满）消耗的泡泡值")]
        public int maxCost = 12;

        [Tooltip("剩余寿命少于这个秒数就不允许钻进去（防止「快消失时进去续命」）")]
        public float rideMinLifeTime = 1f;
    }

    /// <summary>
    /// 浮粘泡泡：会粘住碰到的其他泡泡。
    /// </summary>
    [System.Serializable]
    public class StickyBubbleSettings : BubbleSettings
    {
        [Header("浮粘泡泡 · 粘连")]
        [Tooltip("粘连被扯断所需的力，越大粘得越牢")]
        public float stickBreakForce = 5000f;

        [Header("浮粘泡泡 · 可粘类型")]
        [Tooltip("能不能粘住其他浮粘泡泡")]
        public bool stickSticky = true;

        [Tooltip("能不能粘住弹力泡泡")]
        public bool stickBouncy = true;

        [Tooltip("能不能粘住炸弹泡泡")]
        public bool stickBomb = true;

        [Header("浮粘泡泡 · 载人")]
        [Tooltip("可以钻进去的最小直径（米）。比这小的泡泡进不去")]
        public float rideMinSize = 1.0f;

        [Tooltip("钻进去后额外的上升速度（米/秒）。泡泡越大加得越多，所以别填太大")]
        public float rideExtraRiseSpeed = 1.6f;

        [Tooltip("按 F 钻进去后，多久自动爆炸（秒）")]
        public float rideFloatDuration = 6f;

        [Tooltip("按 E 操控时的移动推力（米/秒²）")]
        public float controlMoveForce = 7f;

        [Tooltip("滞留（还在嘴上）时带着玩家上升的速度（米/秒）。和发射后那套无关，单独调")]
        public float heldLiftSpeed = 3f;

        [Tooltip("在嘴上（滞留）时的独立存活时间（秒）。0 = 沿用 lifeTime")]
        public float heldLifeTime = 5f;
    }

    /// <summary>
    /// 弹力泡泡：碰到东西会弹跳。
    /// 参数已就位，行为待实现（见 Bubble.cs 的 HandleBouncy）。
    /// </summary>
    [System.Serializable]
    public class BouncyBubbleSettings : BubbleSettings
    {
        [Header("弹力泡泡 · 待实现")]
        [Tooltip("弹性系数，0~1，1 为完全弹性")]
        public float bounciness = 0.9f;

        [Tooltip("最多反弹几次，0 表示不限")]
        public int maxBounceCount = 0;

        [Header("弹力泡泡 · 填充地形泡泡")]
        [Tooltip("最小泡泡（点按）打进地形泡泡时提供的容积")]
        public int fillAmountMin = 1;

        [Tooltip("最大泡泡（蓄满力）打进地形泡泡时提供的容积")]
        public int fillAmountMax = 3;

        [Header("弹力泡泡 · 载人与弹射")]
        [Tooltip("可以钻进去的最小直径（米）。比这小的进不去")]
        public float rideMinSize = 1.0f;

        [Tooltip("载人时的移动推力（米/秒²）。弹力泡泡没有浮力，靠这个在地面上走")]
        public float rideMoveForce = 8f;

        [Tooltip("按 F 弹射出去的初速度（米/秒）")]
        public float ejectSpeed = 14f;

        [Tooltip("弹射仰角（度）。0 = 水平，45 最远，90 直上")]
        public float ejectAngle = 35f;

        [Header("弹力泡泡 · 贴墙挤压弹射")]
        [Tooltip("嘴上的泡泡被挤进墙里多少米以上才算「压住了」")]
        public float squeezeThreshold = 0.06f;

        [Tooltip("压到极限时，沿前进方向只剩多少比例（0.55 = 压掉快一半）")]
        public float squeezeMinScale = 0.55f;

        [Tooltip("刚好压到阈值时的弹射速度（米/秒）")]
        public float squeezeLaunchSpeedMin = 7f;

        [Tooltip("压到极限时的弹射速度（米/秒）")]
        public float squeezeLaunchSpeedMax = 22f;

        [Tooltip("弹射时玩家一起被弹出去的速度倍率（1 = 和泡泡同速，0 = 人不动）")]
        public float riderLaunchRatio = 1f;

        [Tooltip("玩家被弹出去的持续时间（秒）。乘上速度就是人飞多远")]
        public float riderLaunchDuration = 0.4f;
    }

    /// <summary>
    /// 爆炸特效的参数。
    ///
    /// 挂在 <see cref="BombBubbleSettings"/> 里，三种泡泡共用一份 ——
    /// 因为被吸收后是「宿主借用体内那颗炸弹的参数」来炸的，
    /// 只要炸弹这份配好了，普通引爆和吸收引爆就都带上了。
    /// </summary>
    [System.Serializable]
    public class ExplosionVfxSettings
    {
        [Tooltip("爆炸特效预制体。留空则回退到旧的 EnergyExplosionVFX")]
        public GameObject prefab;

        [Tooltip("特效整体缩放 = 爆炸半径 × 这个系数。\n" +
                 "0.5 的含义：半径 0.3 米 → 0.15 倍，半径 0.6 米 → 0.3 倍；\n" +
                 "被吸收后半径变成 0.9~1.8 米，缩放就自动跟着变成 0.45~0.9 倍")]
        public float scalePerBlastRadius = 0.5f;

        [Tooltip("缩放下限。防止点按的小爆炸小到看不清")]
        public float minScale = 0.05f;

        [Tooltip("缩放上限。防止吸收后的大爆炸糊满屏幕")]
        public float maxScale = 0.9f;

        [Range(0f, 1f)]
        [Tooltip("上色强度。0 = 保留第三方原色，1 = 完全换成项目配色\n" +
                 "调小了可以当「万一颜色不好看」的保险丝")]
        public float tintStrength = 1f;

        [Tooltip("特效存活时间（秒），到点自动销毁")]
        public float lifeTime = 1.8f;

        [Tooltip("同时最多存在几个爆炸特效。超了当帧就不播（防止连爆刷屏、掉帧）")]
        public int maxConcurrent = 3;
    }

    /// <summary>
    /// 炸弹泡泡：会爆炸。
    /// 参数已就位，行为待实现（见 Bubble.cs 的 HandleBomb）。
    /// </summary>
    [System.Serializable]
    public class BombBubbleSettings : BubbleSettings
    {
        [Header("炸弹泡泡 · 爆炸范围")]
        [Tooltip("点按（最小泡泡）时的爆炸影响半径（米）")]
        public float blastRadius = 0.3f;

        [Tooltip("蓄满力时的爆炸影响半径（米）。中间按蓄力比例线性插值\n" +
                 "想恢复「不管吹多大都是固定范围」，把两个值填成一样的就行")]
        public float blastRadiusAtMaxSize = 0.6f;

        [Tooltip("爆炸推力大小")]
        public float blastForce = 12f;

        [Tooltip("引信时间（秒），从发射算起")]
        public float fuseTime = 2f;

        [Tooltip("爆炸时是否连带引爆范围内的地形泡泡（范围用上面的爆炸半径）")]
        public bool detonateTerrains = true;

        [Header("炸弹泡泡 · 被吸收后")]
        [Tooltip("被浮粘/弹力泡泡吸收后引爆，爆炸半径的倍率。1 = 跟普通引爆一样大")]
        public float absorbedBlastMultiplier = 2f;

        [Header("炸弹泡泡 · 爆炸特效")]
        [Tooltip("爆炸时播的特效。预制体留空则回退到旧的 EnergyExplosionVFX")]
        public ExplosionVfxSettings explosionVfx = new ExplosionVfxSettings();
    }
}
