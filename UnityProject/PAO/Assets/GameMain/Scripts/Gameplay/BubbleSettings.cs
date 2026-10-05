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
    }

    /// <summary>
    /// 炸弹泡泡：会爆炸。
    /// 参数已就位，行为待实现（见 Bubble.cs 的 HandleBomb）。
    /// </summary>
    [System.Serializable]
    public class BombBubbleSettings : BubbleSettings
    {
        [Header("炸弹泡泡 · 待实现")]
        [Tooltip("爆炸影响半径（米）")]
        public float blastRadius = 3f;

        [Tooltip("爆炸推力大小")]
        public float blastForce = 12f;

        [Tooltip("引信时间（秒），从发射算起")]
        public float fuseTime = 2f;
    }
}
