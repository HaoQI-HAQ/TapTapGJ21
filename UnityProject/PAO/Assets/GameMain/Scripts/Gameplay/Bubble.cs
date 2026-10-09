using System.Collections.Generic;
using UnityEngine;

namespace PAO
{
    /// <summary>
    /// 挂在每个飞出去的泡泡上：记录自己的类型，并执行该类型对应的行为。
    ///
    /// 浮粘泡泡：粘住碰到的其他泡泡；够大时还能载人
    /// 弹力泡泡：撞到地形泡泡会被收进去填充容积
    /// 炸弹泡泡：按 R 引爆，把范围内刚体推开
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Bubble : MonoBehaviour
    {
        /// <summary>本泡泡的类型。</summary>
        public BubbleType Type { get; private set; }

        /// <summary>当前乘客。没有人在里面时为 null。</summary>
        public Transform Rider { get; private set; }

        /// <summary>炸弹编号，从 1 开始。非炸弹泡泡为 0。</summary>
        public int BombIndex { get; set; }

        /// <summary>
        /// 发射时的蓄力进度 0~1。0 = 点按的最小泡泡，1 = 蓄满力。
        /// 冲击力、填充量之类跟「吹多大」相关的数值都读它。
        /// </summary>
        public float SizeProgress { get; set; }

        /// <summary>当前所在的地形泡泡容器。没被打进去则为 null。</summary>
        public TerrainBubble Container
        {
            get { return m_Container; }
        }

        private BubbleSettings m_Settings;
        private StickyBubbleSettings m_StickySettings;
        private BouncyBubbleSettings m_BouncySettings;
        private BombBubbleSettings m_BombSettings;

        private TerrainBubble m_Container;      // 被打进去的那个地形泡泡
        private int m_BounceCount;
        private bool m_HasEjected;              // 弹力泡泡是否已经弹射过（只弹一次）
        private bool m_IsStuck;                 // 是否已经粘在可粘地形上了
        private Collider m_IgnoredPlayerCollider;    // 发射时被忽略掉的那个玩家碰撞体，粘住时要恢复
        private Bubble m_AbsorbedBomb;               // 体内装的炸弹泡泡（吸收来的）

        private const int kDetonateMouseButton = 1;  // 1 = 鼠标右键，和 BombBubbleManager 保持一致
        private const float kAbsorbGatherForce = 40f;   // 被吸收后朝宿主中心收拢的力度
        private bool m_AbsorbedBombArmed;           // 炸弹是否已激活（被操控或弹射后才允许手动引爆）
        private bool m_IsHeld;                      // 是否滞留在发射口还没发射
        private PlayerController m_LiftTarget;              // 滞留时被我提着的玩家
        private Vector3 m_HeldLocalOffset;                  // 滞留时相对玩家的局部位置，松手瞬间记下就不再变

        private const float kMaxLiftDistance = 4f;          // 超过这个距离就不再提着玩家
        private const float kHeldForwardOffset = 0.5f;      // 滞留时泡泡离玩家身前多远（米）
        private const float kHeldHeightOffset = 0.2f;       // 再往上抬一点，免得贴地
        private Bubble m_HostBubble;                 // 我是炸弹、被谁吸收了（吸收方记录在 m_AbsorbedBomb）

        /// <summary>
        /// 由发射器在生成泡泡时调用，把类型与参数注入进来。
        /// </summary>
        public void Setup(BubbleType type, BubbleSettings settings)
        {
            Type = type;
            m_Settings = settings;

            m_StickySettings = settings as StickyBubbleSettings;
            m_BouncySettings = settings as BouncyBubbleSettings;
            m_BombSettings = settings as BombBubbleSettings;
        }

        private void OnCollisionEnter(Collision collision)
        {
            // 已经在容器里的泡泡只处理同类粘连，不再走外面的撞击逻辑
            if (m_Container != null)
            {
                HandleContainedCollision(collision);
                return;
            }

            switch (Type)
            {
                case BubbleType.Sticky:
                    HandleSticky(collision);
                    break;

                case BubbleType.Bouncy:
                    HandleBouncy(collision);
                    break;

                case BubbleType.Bomb:
                    HandleBomb(collision);
                    break;
            }
        }

        /// <summary>
        /// 每帧朝容器球心收拢。多个泡泡会自然挤到一起。
        /// </summary>
        private void FixedUpdate()
        {
            // 滞留中的浮粘泡泡优先处理。
            // 注意要放在下面的 kinematic 检查【之前】：
            // 滞留时泡泡是 kinematic 的，放后面就永远进不来了。
            if (m_IsHeld)
            {
                UpdateHeldLift();
                return;
            }

            Rigidbody body = GetComponent<Rigidbody>();
            if (body == null || body.isKinematic)
            {
                return;
            }

            // 被地形泡泡收容：朝它的球心收拢
            if (m_Container != null)
            {
                Vector3 toCenter = m_Container.Center - transform.position;
                body.AddForce(toCenter * m_Container.GatherForce, ForceMode.Acceleration);
                return;
            }

            // 被别的泡泡吸收（我是那颗炸弹）：同样朝宿主球心收拢，
            // 于是看起来就是「飞进去、停在中间」
            if (m_HostBubble != null)
            {
                Vector3 toHost = m_HostBubble.transform.position - transform.position;
                body.AddForce(toHost * kAbsorbGatherForce, ForceMode.Acceleration);
            }
        }

        /// <summary>
        /// 浮粘泡泡：粘住碰到的其他泡泡。
        /// 用 FixedJoint 把两个刚体锁在一起，粘住后它们会作为一个整体运动。
        /// </summary>
        private void HandleSticky(Collision collision)
        {
            if (m_StickySettings == null)
            {
                return;
            }

            // 优先判断是不是「可粘地形」：是的话整颗泡泡钉死在接触点上，
            // 不再走下面的泡泡互粘逻辑
            StickySurface surface = collision.collider.GetComponentInParent<StickySurface>();
            if (surface != null)
            {
                StickToSurface(collision, surface);
                return;
            }

            // 只粘泡泡，不粘墙和地面
            Bubble other = collision.collider.GetComponentInParent<Bubble>();
            if (other == null || other == this)
            {
                return;
            }

            // 炸弹优先走「被吸收」这条路，不走粘连。
            // 否则同一帧里既粘住又被吸收，两条逻辑会打架。
            if (other.Type == BubbleType.Bomb && CanAbsorbBomb)
            {
                return;
            }

            // 按类型过滤：哪种泡泡能被粘，在 Inspector 里勾
            if (!CanStickType(other.Type))
            {
                return;
            }

            Rigidbody otherBody = other.GetComponent<Rigidbody>();
            if (otherBody == null)
            {
                return;
            }

            // 已经连过这个泡泡就不重复加关节，否则会越粘越乱
            FixedJoint[] existingJoints = GetComponents<FixedJoint>();
            for (int i = 0; i < existingJoints.Length; i++)
            {
                if (existingJoints[i] != null && existingJoints[i].connectedBody == otherBody)
                {
                    return;
                }
            }

            FixedJoint joint = gameObject.AddComponent<FixedJoint>();
            joint.connectedBody = otherBody;
            joint.breakForce = m_StickySettings.stickBreakForce;
            joint.breakTorque = m_StickySettings.stickBreakForce;

            // 对方已经是「固定住」的了（粘在地形上，或者属于某座已经成形的桥），
            // 那我也跟着固定：不消失、不飘走，成为桥的一部分。
            // 注意这里不切成 kinematic —— 保留物理才会摇摇晃晃，
            // 而玩家是 CharacterController，站上来不会产生真实压力，压不塌。
            if (other.IsStuck)
            {
                BecomeStuck();
            }
        }

        /// <summary>
        /// 弹力泡泡：撞到地形泡泡就被收进去。
        /// 本体不销毁，留在里面朝球心收拢，和别的泡泡挤成一团。
        /// </summary>
        private void HandleBouncy(Collision collision)
        {
            if (m_BouncySettings == null)
            {
                return;
            }

            TerrainBubble terrain = collision.collider.GetComponentInParent<TerrainBubble>();
            if (terrain == null)
            {
                // TODO 撞到别的东西：按 m_BouncySettings.bounciness 反弹（待实现）
                return;
            }

            // 泡泡吹得越大，填进去的容积越多
            int amount = Mathf.RoundToInt(Mathf.Lerp(
                m_BouncySettings.fillAmountMin,
                m_BouncySettings.fillAmountMax,
                Mathf.Clamp01(SizeProgress)));

            // 先装进去再加容积：万一这一下正好填满，
            // 地形爆炸时会连带把刚进去的它一起炸掉，符合预期
            terrain.ContainBubble(this);
            terrain.Absorb(amount);
        }

        /// <summary>
        /// 炸弹泡泡：碰到东西时的行为。
        /// 目前只支持按 R 手动引爆，撞到东西不炸；要做「撞到就炸」在这里调 Explode()。
        /// </summary>
        /// <summary>
        /// 炸弹泡泡：被浮粘泡泡或弹力泡泡吸收。
        /// 撞上去就钻进对方体内，自己消失，对方从此装着一颗炸弹。
        /// </summary>
        /// <summary>
        /// 炸弹泡泡：被浮粘泡泡或弹力泡泡吸收。
        /// 本体不销毁 —— 它会像弹力泡泡被地形吸收那样飞进对方体内、停在中心，
        /// 直到对方引爆时才跟着一起炸。
        /// </summary>
        private void HandleBomb(Collision collision)
        {
            // 已经被吸收过了就不再重复
            if (m_HostBubble != null)
            {
                return;
            }

            Bubble host = collision.collider.GetComponentInParent<Bubble>();
            if (host == null || host == this)
            {
                return;
            }

            if (!host.CanAbsorbBomb)
            {
                return;
            }

            // 由宿主登记我，然后我自己飞进去（不销毁）
            host.AbsorbBomb(this);
        }

        /// <summary>
        /// 容器内泡泡之间的碰撞：粘在一起，这样会挤成一团而不是互相弹开。
        /// </summary>
        private void HandleContainedCollision(Collision collision)
        {
            if (m_Container == null || !m_Container.StickContainedBubbles)
            {
                return;
            }

            Bubble other = collision.collider.GetComponentInParent<Bubble>();
            if (other == null || other == this)
            {
                return;
            }

            // 只粘同一个容器里的泡泡
            if (other.Container != m_Container)
            {
                return;
            }

            Rigidbody otherBody = other.GetComponent<Rigidbody>();
            if (otherBody == null)
            {
                return;
            }

            FixedJoint[] existingJoints = GetComponents<FixedJoint>();
            for (int i = 0; i < existingJoints.Length; i++)
            {
                if (existingJoints[i] != null && existingJoints[i].connectedBody == otherBody)
                {
                    return;
                }
            }

            FixedJoint joint = gameObject.AddComponent<FixedJoint>();
            joint.connectedBody = otherBody;
            joint.breakForce = float.PositiveInfinity;   // 容器内不会自己散开
            joint.breakTorque = float.PositiveInfinity;
        }

        /// <summary>
        /// 这一种类型的泡泡能不能被粘住。三种开关都在 Inspector 里。
        /// </summary>
        private bool CanStickType(BubbleType otherType)
        {
            if (m_StickySettings == null)
            {
                return false;
            }

            switch (otherType)
            {
                case BubbleType.Bouncy:
                    return m_StickySettings.stickBouncy;

                case BubbleType.Bomb:
                    return m_StickySettings.stickBomb;

                default:
                    return m_StickySettings.stickSticky;
            }
        }

        // ==================== 载人 ====================

        /// <summary>
        /// 现在能不能钻进去：必须是浮粘泡泡、尺寸够大、而且里面还没人。
        /// UI 提示要不要显示就看它。
        /// </summary>
        public bool CanRide
        {
            get
            {
                // 还在嘴上（滞留）时不能钻进去，得先吹出去
                if (m_IsHeld)
                {
                    return false;
                }

                // 已经粘成地形了：它不再是可乘坐的泡泡，
                // 交互提示也不该再出现
                if (m_IsStuck)
                {
                    return false;
                }

                // 体内装了炸弹就不能钻进去了，太危险
                if (m_AbsorbedBomb != null)
                {
                    return false;
                }

                if (Rider != null)
                {
                    return false;   // 已经有人了
                }

                // 浮粘泡泡：按它自己的门槛
                if (m_StickySettings != null)
                {
                    return GetDiameter() >= m_StickySettings.rideMinSize;
                }

                // 弹力泡泡：也能钻进去，用它自己的门槛
                if (m_BouncySettings != null)
                {
                    return GetDiameter() >= m_BouncySettings.rideMinSize;
                }

                return false;   // 炸弹泡泡不给载
            }
        }

        /// <summary>
        /// 乘客应该待的位置：泡泡中心略偏下，看起来像站在泡泡里。
        /// </summary>
        public Vector3 RideAnchorPosition
        {
            get
            {
                float radius = GetDiameter() * 0.5f;
                return transform.position - Vector3.up * (radius * 0.45f);
            }
        }

        /// <summary>是不是弹力泡泡。交互层靠它决定按 F 是弹射还是跳出。</summary>
        /// <summary>
        /// 能不能被 E 键远程操控。只有浮粘泡泡可以。
        /// 弹力泡泡只走 F 那条线（钻进去 + 按 F 弹射），不参与远程操控。
        /// </summary>
        public bool CanControl
        {
            get
            {
                // 还在嘴上（滞留）时也不能远程操控
                if (m_IsHeld)
                {
                    return false;
                }

                // 粘成地形了就不能再操控
                if (m_IsStuck)
                {
                    return false;
                }

                // 不是浮粘泡泡就没这个功能
                if (m_StickySettings == null)
                {
                    return false;
                }

                return GetDiameter() >= m_StickySettings.rideMinSize;
            }
        }

        public bool IsBouncy
        {
            get { return m_BouncySettings != null; }
        }

        /// <summary>弹力泡泡是否已经弹射过。弹射只能来一次。</summary>
        public bool HasEjected
        {
            get { return m_HasEjected; }
        }

        /// <summary>乘客可以在里头活动的内半径（米）。</summary>
        public float RideInnerRadius
        {
            get { return GetDiameter() * 0.5f * 0.6f; }
        }

        /// <summary>
        /// 让乘客进来。成功返回 true。
        /// </summary>
        public bool EnterRide(Transform rider)
        {
            if (!CanRide || rider == null)
            {
                return false;
            }

            Rider = rider;

            if (m_BouncySettings != null)
            {
                // 弹力泡泡：没有浮力，靠重力贴地，玩家推着它在地上走
                EnterBouncyRide();
            }
            else
            {
                // 浮粘泡泡：保留浮力，载人后升得更快
                ApplyBuoyancyWithRide();

                // 按 F 钻进来后开始倒计时，时间到自己炸开
                if (m_StickySettings != null && m_StickySettings.rideFloatDuration > 0f)
                {
                    CancelInvoke("OnFloatTimeout");
                    Invoke("OnFloatTimeout", m_StickySettings.rideFloatDuration);
                }
            }

            return true;
        }

        /// <summary>
        /// 乘客离开（主动跳出，或泡泡被打破）。
        /// </summary>
        public void ExitRide()
        {
            if (Rider == null)
            {
                return;
            }

            Rider = null;
            ApplyBuoyancyWithRide();
        }

        /// <summary>
        /// 弹力泡泡的载人初始化：关掉浮力、打开重力。
        /// 没有浮力它才不会自己往上飘，玩家才能控制它在地面上四处走。
        /// </summary>
        private void EnterBouncyRide()
        {
            ConstantForce buoyancy = GetComponent<ConstantForce>();
            if (buoyancy != null)
            {
                buoyancy.force = Vector3.zero;
            }

            Rigidbody body = GetComponent<Rigidbody>();
            if (body != null)
            {
                body.isKinematic = false;
                body.useGravity = true;          // 有重力才贴得住地面
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
        }

        /// <summary>
        /// 连人带泡泡一起弹射出去，走标准的抛物线。
        /// 做法是把朝向往水平面压平后，按仰角拆成水平分量与垂直分量，
        /// 之后交给重力自然形成弧线。
        /// </summary>
        public void EjectRide(Vector3 direction)
        {
            if (m_BouncySettings == null || m_HasEjected)
            {
                return;
            }

            Rigidbody body = GetComponent<Rigidbody>();
            if (body == null)
            {
                return;
            }

            m_HasEjected = true;

            Vector3 flat = new Vector3(direction.x, 0f, direction.z);
            if (flat.sqrMagnitude < 0.0001f)
            {
                flat = transform.forward;
            }

            flat.Normalize();

            float speed = m_BouncySettings.ejectSpeed;
            float angle = Mathf.Clamp(m_BouncySettings.ejectAngle, 0f, 89f) * Mathf.Deg2Rad;

            Vector3 velocity = flat * (Mathf.Cos(angle) * speed)
                + Vector3.up * (Mathf.Sin(angle) * speed);

            // 飞行期间必须是自由的：关浮力、开重力、不是 kinematic
            ConstantForce buoyancy = GetComponent<ConstantForce>();
            if (buoyancy != null)
            {
                buoyancy.force = Vector3.zero;
            }

            body.isKinematic = false;
            body.useGravity = true;
            body.velocity = velocity;
        }

        /// <summary>
        /// 重算浮力。载人时额外加一段上升速度，泡泡越大加得越多，
        /// 所以吹得越大、钻进去后升得越快。
        /// </summary>
        private void ApplyBuoyancyWithRide()
        {
            if (m_Settings == null || m_StickySettings == null)
            {
                return;
            }

            Rigidbody body = GetComponent<Rigidbody>();
            ConstantForce buoyancy = GetComponent<ConstantForce>();
            if (body == null || buoyancy == null)
            {
                return;
            }

            float sizeProgress = Mathf.InverseLerp(m_Settings.minSize, m_Settings.maxSize, GetDiameter());
            float riseSpeed = Mathf.Lerp(m_Settings.smallRiseSpeed, m_Settings.largeRiseSpeed, sizeProgress);

            if (Rider != null)
            {
                riseSpeed += m_StickySettings.rideExtraRiseSpeed * sizeProgress;
            }

            buoyancy.force = Vector3.up * (riseSpeed * m_Settings.drag * body.mass);
        }

        /// <summary>
        /// 当前直径（米）。取三轴缩放里最大的那个，兼容非等比缩放。
        /// </summary>
        private float GetDiameter()
        {
            Vector3 scale = transform.lossyScale;
            return Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z));
        }

        // ==================== 地形容器 ====================

        /// <summary>
        /// 被地形泡泡吸进去了：停掉浮力、加大阻力，之后交给 FixedUpdate 收拢。
        /// 注意这里不销毁泡泡本体，它要留在容器里作为可见的填充物。
        /// </summary>
        public void OnContained(TerrainBubble container)
        {
            if (container == null)
            {
                return;
            }

            m_Container = container;

            // 停掉上升浮力，否则它会一直往上顶，挤不到中心去
            ConstantForce buoyancy = GetComponent<ConstantForce>();
            if (buoyancy != null)
            {
                buoyancy.force = Vector3.zero;
            }

            Rigidbody body = GetComponent<Rigidbody>();
            if (body != null)
            {
                body.useGravity = false;
                body.drag = container.ContainedDrag;
                body.angularDrag = container.ContainedDrag;
            }

            // 忽略与地形外壳的碰撞，不然会被外壳弹开、根本挤不进去
            Collider ownCollider = GetComponent<Collider>();
            if (ownCollider != null)
            {
                container.IgnoreCollisionWith(ownCollider);
            }
        }

        // ==================== 爆炸 ====================

        /// <summary>
        /// 引爆：把半径内的刚体按爆炸力推开，然后销毁自己。
        /// 由 BombBubbleManager 在按下引爆键时调用；
        /// 地形泡泡被填满时也会调它来清掉里面的泡泡。
        /// </summary>
        public void Explode()
        {
            ExplodeWithMultiplier(1f);
        }

        /// <summary>按倍率放大范围引爆（用自身的炸弹参数）。</summary>
        public void ExplodeWithMultiplier(float radiusMultiplier)
        {
            ExplodeWithMultiplier(radiusMultiplier, null);
        }

        /// <summary>
        /// 真正的爆炸实现。
        /// settingsOverride 是给「宿主」用的：宿主自己是浮粘/弹力泡泡，
        /// 没有 m_BombSettings，必须由调用方把体内那颗炸弹的参数传进来。
        /// 不能靠 m_AbsorbedBomb 现取 —— 调用方往往在清空引用之后才调这里。
        /// </summary>
        public void ExplodeWithMultiplier(float radiusMultiplier, BombBubbleSettings settingsOverride)
        {
            // 宿主自己是浮粘/弹力泡泡，没有 m_BombSettings。
            // 这时必须借用体内那颗炸弹的参数，否则下面会直接销毁、
            // 什么爆炸都不发生（炸不动地形就是这么来的）。
            BombBubbleSettings bombSettings = settingsOverride != null
                ? settingsOverride
                : m_BombSettings;

            if (bombSettings == null && m_AbsorbedBomb != null)
            {
                bombSettings = m_AbsorbedBomb.BombSettings;
            }

            // 体内还装着一颗炸弹的话，先让它炸掉自己。
            // 否则宿主先消失，那颗炸弹会因为「宿主检查」永远留在场上。
            if (m_AbsorbedBomb != null)
            {
                Bubble payload = m_AbsorbedBomb;
                m_AbsorbedBomb = null;
                m_AbsorbedBombArmed = false;

                if (payload != null)
                {
                    payload.Explode();
                }
            }

            Vector3 center = transform.position;
            if (bombSettings == null)
            {
                Destroy(gameObject);
                return;
            }

            float radius = bombSettings.blastRadius * radiusMultiplier;
            float force = bombSettings.blastForce;

            // 先让乘客出来，否则人会被留在正在销毁的泡泡里
            if (Rider != null)
            {
                ExitRide();
            }

            // 同一个物体可能挂多个碰撞体，用 HashSet 去重，避免被叠加多次力
            Collider[] hits = Physics.OverlapSphere(center, radius);
            HashSet<Rigidbody> affected = new HashSet<Rigidbody>();

            for (int i = 0; i < hits.Length; i++)
            {
                Rigidbody body = hits[i].GetComponentInParent<Rigidbody>();
                if (body == null || body.isKinematic)
                {
                    continue;
                }

                affected.Add(body);
            }

            foreach (Rigidbody body in affected)
            {
                // AddExplosionForce 会自动按距离衰减，方向由爆炸中心指向物体，
                // 所以在左边炸就会把物体往右推，正是要的效果
                body.AddExplosionForce(force, center, radius, 0f, ForceMode.Impulse);
            }

            // 连带引爆范围内的地形泡泡。用 HashSet 去重，
            // 免得一个地形因为挂了多个碰撞体被引爆好几次
            if (bombSettings.detonateTerrains)
            {
                HashSet<TerrainBubble> terrains = new HashSet<TerrainBubble>();

                for (int t = 0; t < hits.Length; t++)
                {
                    TerrainBubble terrain = hits[t].GetComponentInParent<TerrainBubble>();
                    if (terrain != null)
                    {
                        terrains.Add(terrain);
                    }
                }

                // TerrainBubble.Explode 自带防重入，连锁时不会互相递归
                foreach (TerrainBubble terrain in terrains)
                {
                    terrain.Explode();
                }
            }

            Destroy(gameObject);
        }

        private void OnDrawGizmosSelected()
        {
            if (m_BombSettings == null)
            {
                return;
            }

            Gizmos.color = new Color(1f, 0.5f, 0.2f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, m_BombSettings.blastRadius);
        }

        /// <summary>
        /// 排入寿命倒计时。不用 Destroy(obj, t) 是因为那个撤不掉，
        /// 而进了地形容器的泡泡需要「一直留着直到容器炸掉」。
        /// </summary>
        public void ScheduleLifeEnd(float lifeTime)
        {
            CancelInvoke("OnLifeEnd");
            Invoke("OnLifeEnd", Mathf.Max(0.1f, lifeTime));
        }

        private void OnLifeEnd()
        {
            // 已经进了容器：不自动消失，等容器被填满炸掉时一起处理
            if (m_Container != null)
            {
                return;
            }

            // 粘在可粘地形上了：同样留着不消失。
            // 它已经变成场景的一部分（可以踩、可以当落脚点），
            // 不该因为发射时定的存活时间到点就凭空不见。
            // 被别的泡泡吸收了：同样留着不消失，等宿主引爆时一起炸
            if (m_HostBubble != null)
            {
                return;
            }

            if (m_IsStuck)
            {
                return;
            }

            Destroy(gameObject);
        }
        /// <summary>
        /// 粘在可粘地形上：直接冻住刚体，泡泡会永远停在这个位置。
        /// 存活时间到点也不会消失——它已经是场景的一部分了。
        /// </summary>
        private void StickToSurface(Collision collision, StickySurface surface)
        {
            if (m_IsStuck)
            {
                return;
            }

            Rigidbody body = GetComponent<Rigidbody>();
            if (body == null)
            {
                return;
            }

            m_IsStuck = true;

            // 停掉浮力，否则粘住了还会一直往上顶
            ConstantForce buoyancy = GetComponent<ConstantForce>();
            if (buoyancy != null)
            {
                buoyancy.force = Vector3.zero;
            }

            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;

            if (surface.SnapToContactPoint && collision.contactCount > 0)
            {
                // 贴着接触面摆正：沿接触法线把球心往外推一个半径
                ContactPoint contact = collision.GetContact(0);
                transform.position = contact.point + contact.normal * (GetDiameter() * 0.5f);
            }

            if (surface.FreezeRotation)
            {
                body.freezeRotation = true;
            }

            // 完全停住物理，才是真正的「一直粘在这个位置」
            body.isKinematic = true;

            // 同理恢复与玩家的碰撞：粘在地形上的泡泡也要能踩
            RestorePlayerCollision();
        }

        /// <summary>
        /// 按 F 钻进来后的倒计时到点了：连人带泡一起炸掉。
        /// </summary>
        private void OnFloatTimeout()
        {
            // 人已经提前跳出去了就不再炸
            if (Rider == null)
            {
                return;
            }

            Explode();
        }

        /// <summary>是否已经粘在可粘地形上。</summary>
        public bool IsStuck
        {
            get { return m_IsStuck; }
        }
        /// <summary>
        /// 变成桥的一部分：不再消失、不再上浮，但保留物理所以会晃。
        ///
        /// 和「粘在地形上」的区别：锚点那颗是 kinematic 完全固定的，
        /// 桥上的泡泡则是动态的 —— 一端死锚、一端摇晃，正好是要的手感。
        /// </summary>
        /// <summary>
        /// 变成桥的一部分：直接冻住，位置就停在粘上的那一刻。
        /// 和「粘在地形上」用的是同一套手段（关浮力 + kinematic），
        /// 所以它同样不会因为存活时间到点而消失（见 OnLifeEnd）。
        /// </summary>
        private void BecomeStuck()
        {
            if (m_IsStuck)
            {
                return;
            }

            m_IsStuck = true;

            // 停掉浮力，否则冻住了还会一直往上顶
            ConstantForce buoyancy = GetComponent<ConstantForce>();
            if (buoyancy != null)
            {
                buoyancy.force = Vector3.zero;
            }

            Rigidbody body = GetComponent<Rigidbody>();
            if (body != null)
            {
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = true;   // 完全停住物理

            // 恢复与玩家的碰撞：粘住后它就是地形了，人要能踩上来。
            // 发射时为了让泡泡不弹回自己，曾把这一对碰撞忽略掉。
            RestorePlayerCollision();
            }
        }
        /// <summary>
        /// 由发射器登记：这个泡泡当前忽略了与哪个碰撞体的碰撞。
        /// 粘住时要靠它把碰撞恢复回来，人才能踩上去。
        /// </summary>
        public void SetIgnoredCollider(Collider playerCollider)
        {
            m_IgnoredPlayerCollider = playerCollider;
        }

        /// <summary>
        /// 恢复与玩家的碰撞。粘住后泡泡就是地形的一部分了，
        /// 必须让 CharacterController 能站上来，而不是穿过去。
        /// </summary>
        private void RestorePlayerCollision()
        {
            if (m_IgnoredPlayerCollider == null)
            {
                return;
            }

            Collider ownCollider = GetComponent<Collider>();
            if (ownCollider != null)
            {
                Physics.IgnoreCollision(ownCollider, m_IgnoredPlayerCollider, false);
            }
        }
        // ==================== 炸弹载荷 ====================

        /// <summary>体内是否装着一颗吸收来的炸弹。</summary>
        /// <summary>本泡泡的炸弹参数（非炸弹泡泡为 null）。吸收方要读爆炸范围倍率。</summary>
        public BombBubbleSettings BombSettings
        {
            get { return m_BombSettings; }
        }

        public bool HasBomb
        {
            get { return m_AbsorbedBomb != null; }
        }

        /// <summary>
        /// 能不能吸收炸弹泡泡。浮粘与弹力都可以，
        /// 但必须满足：自己还没装炸弹、没粘成地形、而且里面没有人。
        /// 「吸收只能发生在没上黏浮泡泡的时候」就对应最后那条。
        /// </summary>
        public bool CanAbsorbBomb
        {
            get
            {
                if (m_AbsorbedBomb != null) { return false; }
                if (m_IsStuck) { return false; }
                if (Rider != null) { return false; }
                if (m_BombSettings != null) { return false; }   // 炸弹不能装炸弹

                return m_StickySettings != null || m_BouncySettings != null;
            }
        }

        /// <summary>
        /// 吸收一颗炸弹泡泡。只会记下来，炸弹本体由调用方销毁。
        /// </summary>
        public void AbsorbBomb(Bubble bomb)
        {
            if (bomb == null || !CanAbsorbBomb)
            {
                return;
            }

            m_AbsorbedBomb = bomb;
            m_AbsorbedBombArmed = false;

            // 让它自己飞进来、停在球心（和地形泡泡吸收弹力泡泡是同一套）
            bomb.OnAbsorbedInto(this);

            // 吸收后视觉上稍微变一下，方便玩家看出这颗泡泡带着炸弹
            Renderer ownRenderer = GetComponent<Renderer>();
            if (ownRenderer != null)
            {
                MaterialPropertyBlock block = new MaterialPropertyBlock();
                ownRenderer.GetPropertyBlock(block);
                block.SetColor(Shader.PropertyToID("_BaseColor"), new Color(1f, 0.55f, 0.2f, 1f));
                block.SetColor(Shader.PropertyToID("_Color"), new Color(1f, 0.55f, 0.2f, 1f));
                ownRenderer.SetPropertyBlock(block);
            }
        }

        /// <summary>把体内的炸弹激活，之后就能手动引爆了。</summary>
        public void ArmAbsorbedBomb()
        {
            if (m_AbsorbedBomb != null)
            {
                m_AbsorbedBombArmed = true;
            }
        }

        /// <summary>
        /// 能不能用 F 把它弹射出去。装了炸弹的弹力泡泡走这条，
        /// 而不是钻进去。
        /// </summary>
        public bool CanLaunchWithBomb
        {
            get
            {
                return m_BouncySettings != null
                    && m_AbsorbedBomb != null
                    && !m_IsStuck;
            }
        }

        /// <summary>
        /// 带着炸弹弹射出去。direction 是弹射方向（水平面内），
        /// 俯仰角仍用弹力泡泡自己的 ejectAngle。
        /// </summary>
        public void LaunchWithBomb(Vector3 direction)
        {
            if (!CanLaunchWithBomb)
            {
                return;
            }

            // 弹出去之后炸弹就处于待爆状态，飞行途中可以按右键引爆
            ArmAbsorbedBomb();

            // 复用弹力泡泡那套抛物线
            EjectRide(direction);
        }

        /// <summary>
        /// 手动引爆体内的炸弹：按倍率放大范围，然后自己也炸掉。
        /// </summary>
        /// <summary>
        /// 手动引爆体内的炸弹：先炸掉炸弹本体，再按放大的范围炸自己。
        /// </summary>
        public bool DetonateAbsorbedBomb()
        {
            if (m_AbsorbedBomb == null)
            {
                return false;
            }

            Bubble bomb = m_AbsorbedBomb;

            // 先把参数取出来存好，因为下面会把 m_AbsorbedBomb 清空，
            // 清空之后 ExplodeWithMultiplier 就借不到了
            BombBubbleSettings payloadSettings = bomb.BombSettings;

            float multiplier = bomb.BombSettings != null
                ? bomb.BombSettings.absorbedBlastMultiplier
                : 2f;

            m_AbsorbedBomb = null;
            m_AbsorbedBombArmed = false;

            // 只炸一次：销毁体内的炸弹本体，但不单独引爆它。
            // 否则会有两组力（一小一大）叠加，推两次。
            if (bomb != null)
            {
                Destroy(bomb.gameObject);
            }

            // 再按放大的范围炸宿主自己
            ExplodeWithMultiplier(multiplier, payloadSettings);
            return true;
        }

        /// <summary>
        /// 只有装着已激活炸弹的泡泡才需要每帧看一眼引爆键。
        /// </summary>
        private void Update()
        {
            if (m_AbsorbedBomb == null || !m_AbsorbedBombArmed)
            {
                return;
            }

            if (Input.GetMouseButtonDown(kDetonateMouseButton))
            {
                DetonateAbsorbedBomb();
            }
        }
        /// <summary>
        /// 被别的泡泡吸收了：保留本体飞进对方体内，停在球心。
        /// 走的是和「弹力泡泡被地形泡泡吸收」完全一样的路子，
        /// 所以视觉上也是那种飞进去、往中间收的效果。
        /// </summary>
        public void OnAbsorbedInto(Bubble host)
        {
            if (host == null || m_HostBubble != null)
            {
                return;
            }

            m_HostBubble = host;

            // 停掉浮力，否则进了人家肚子里还一直往上顶
            ConstantForce buoyancy = GetComponent<ConstantForce>();
            if (buoyancy != null)
            {
                buoyancy.force = Vector3.zero;
            }

            Rigidbody body = GetComponent<Rigidbody>();
            if (body != null)
            {
                body.useGravity = false;
                body.velocity *= 0.3f;          // 收一下速度，飞进去更顺
                body.drag = 5f;
                body.angularDrag = 5f;
            }

            // 忽略与宿主的碰撞，不然会被它的外壳弹开、挤不进去
            Collider ownCollider = GetComponent<Collider>();
            Collider hostCollider = host.GetComponent<Collider>();
            if (ownCollider != null && hostCollider != null)
            {
                Physics.IgnoreCollision(ownCollider, hostCollider, true);
            }
        }

        /// <summary>我是不是已经被某个泡泡吸收了。</summary>
        public bool IsAbsorbed
        {
            get { return m_HostBubble != null; }
        }

        /// <summary>吸收我的那个宿主泡泡。</summary>
        public Bubble HostBubble
        {
            get { return m_HostBubble; }
        }
        /// <summary>
        /// 从「滞留在发射口」变成「飞出去」时调用。
        /// 默认什么都不做；浮粘/弹力泡泡会在这里交接它们的滞留态效果。
        /// </summary>
        public void OnLaunchedFromHold()
        {
            m_IsHeld = false;

            // 发射了就不再提着玩家，让他自己飞
            ReleaseLiftTarget();
        }

        /// <summary>是否正滞留在发射口还没发射。</summary>
        public bool IsHeld
        {
            get { return m_IsHeld; }
            set { m_IsHeld = value; }
        }
        /// <summary>
        /// 由发射器在生成时调用：告诉泡泡「滞留时该提谁」。
        /// </summary>
        /// <summary>
        /// 由发射器在滞留瞬间调用：记下泡泡此刻相对玩家的位置。
        /// 之后它就锁在这个位置 —— 松手时在哪，就一直在哪，不会再跳。
        /// </summary>
        public void SetHeldOffset(Vector3 localOffset)
        {
            m_HeldLocalOffset = localOffset;
        }

        public void SetLiftTarget(PlayerController player)
        {
            m_LiftTarget = player;

            // 顺手把与玩家所有碰撞体的碰撞都忽略掉。
            // 只忽略主碰撞体是不够的：泡泡贴着身体时会被子物体的碰撞体
            // 反复顶开，而跟随逻辑又把它拉回来 —— 会加重抖动。
            IgnorePlayerCollisions();
        }

        /// <summary>
        /// 滞留态：浮粘泡泡把玩家提起来。
        ///
        /// 泡泡在这里切成 kinematic 并直接摆到锚点上 —— 不走物理跟随。
        /// 之前用速度去追锚点，玩家一转身锚点就绕圈，泡泡追着一个移动的靶子
        /// 反复过冲，表现就是持续抖动（转得越快抖得越凶）。
        /// 直接摆位置就没有这个问题。
        ///
        /// 代价是 kinematic 撞静态地形不会触发碰撞事件，
        /// 所以粘附改成主动检测（见 CheckStickySurfaceWhileHeld）。
        /// </summary>
        private void UpdateHeldLift()
        {
            // 已经粘成地形了：不再是「嘴上的泡泡」，松开玩家让他掉下去
            if (m_IsStuck)
            {
                ReleaseLiftTarget();
                return;
            }

            // 只有浮粘泡泡有浮力这一说
            if (m_StickySettings == null || m_LiftTarget == null)
            {
                return;
            }

            // 离太远就松手，不然隔着半张地图还能把人吊起来
            float distance = Vector3.Distance(transform.position, m_LiftTarget.transform.position);
            if (distance > kMaxLiftDistance)
            {
                ReleaseLiftTarget();
                return;
            }

            Rigidbody body = GetComponent<Rigidbody>();
            if (body != null)
            {
                ConstantForce buoyancy = GetComponent<ConstantForce>();
                if (buoyancy != null)
                {
                    buoyancy.force = Vector3.zero;   // 浮力关掉，位置由跟随决定
                }

                body.useGravity = false;

                // 顺序很重要：必须先清速度、再切 kinematic。
                // 反过来的话，对已经是 kinematic 的刚体设 velocity 会每帧刷
                // "Setting linear velocity of a kinematic body is not supported"。
                if (!body.isKinematic)
                {
                    body.velocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                    body.isKinematic = true;
                }

                // 位置同步交给 LateUpdate 去做（和玩家同一个节奏），这里不再摆位置
            }

            // 上升体现在玩家身上，速度由 Held Lift Speed 直接控制
            m_LiftTarget.IsLifted = true;
            m_LiftTarget.LiftSpeed = m_StickySettings.heldLiftSpeed;

            // kinematic 撞不到静态地形，只能自己查有没有贴到可粘地形
            CheckStickySurfaceWhileHeld();
        }

        /// <summary>
        /// 滞留时的粘附检测。
        /// 泡泡此刻是 kinematic，撞静态地形不会有碰撞事件，
        /// 所以每帧主动查一下自己有没有贴到可粘地形。
        /// </summary>
        private void CheckStickySurfaceWhileHeld()
        {
            if (m_IsStuck || m_StickySettings == null)
            {
                return;
            }

            float radius = GetDiameter() * 0.5f + 0.15f;
            Collider[] hits = Physics.OverlapSphere(transform.position, radius);

            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i] == null)
                {
                    continue;
                }

                StickySurface surface = hits[i].GetComponentInParent<StickySurface>();
                if (surface != null)
                {
                    StickWhileHeld(surface);
                    return;
                }
            }
        }

        /// <summary>
        /// 滞留途中粘上了可粘地形：变成地形的一部分，同时把玩家放下。
        /// </summary>
        private void StickWhileHeld(StickySurface surface)
        {
            if (m_IsStuck)
            {
                return;
            }

            m_IsStuck = true;
            m_IsHeld = false;              // 不再是「嘴上的泡泡」

            // 玩家立刻掉下来 —— 这正是「吹泡黏住时角色自动落下」
            ReleaseLiftTarget();

            Rigidbody body = GetComponent<Rigidbody>();
            if (body != null)
            {
                ConstantForce buoyancy = GetComponent<ConstantForce>();
                if (buoyancy != null)
                {
                    buoyancy.force = Vector3.zero;
                }

                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;

                if (surface != null && surface.FreezeRotation)
                {
                    body.freezeRotation = true;
                }

                body.isKinematic = true;   // 彻底钉住
            }

            // 粘住后就是地形了，恢复与玩家的碰撞，人能踩上去
            RestorePlayerCollision();
        }

        /// <summary>滞留时泡泡该待的位置：松手那一刻记下的相对位置。</summary>
        private Vector3 GetHeldAnchorPosition(Transform player)
        {
            // 锁在松手时记下的那个相对位置，不再另算 —— 免得松手就跳一下
            return player.TransformPoint(m_HeldLocalOffset);
        }


        /// <summary>
        /// 泡泡被销毁（寿命到、爆炸、退出 Play）时也要松开玩家，
        /// 否则他会永远卡在「被提着」的状态里浮着下不来。
        /// </summary>
        private void OnDestroy()
        {
            ReleaseLiftTarget();
        }

        private void ReleaseLiftTarget()
        {
            if (m_LiftTarget != null)
            {
                m_LiftTarget.IsLifted = false;
                m_LiftTarget.LiftSpeed = 0f;
            }
        }
        /// <summary>
        /// 忽略与玩家身上所有碰撞体的碰撞。
        /// 滞留时泡泡紧贴身体，只忽略主碰撞体的话，
        /// 子物体（模型自带的碰撞体）会不停把它顶开，
        /// 而跟随逻辑又把它拉回来 —— 看起来就是抖个不停。
        /// </summary>
        private void IgnorePlayerCollisions()
        {
            if (m_LiftTarget == null)
            {
                return;
            }

            Collider ownCollider = GetComponent<Collider>();
            if (ownCollider == null)
            {
                return;
            }

            Collider[] playerColliders = m_LiftTarget.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < playerColliders.Length; i++)
            {
                if (playerColliders[i] != null)
                {
                    Physics.IgnoreCollision(ownCollider, playerColliders[i], true);
                }
            }
        }
        /// <summary>
        /// 滞留时在渲染帧同步位置。
        ///
        /// 为什么不在 FixedUpdate 里做：物理固定 50Hz，而玩家是按渲染帧移动的，
        /// 两者节奏不一致，泡泡就会在目标位置附近轻微摆动。
        /// 放到 LateUpdate 后和玩家同一个节奏，看起来就平稳了。
        ///
        /// 也刻意不去同步 rotation —— 一转头泡泡就跟着转，看着也像在摆。
        /// </summary>
        private void LateUpdate()
        {
            if (!m_IsHeld || m_IsStuck || m_LiftTarget == null)
            {
                return;
            }

            transform.position = GetHeldAnchorPosition(m_LiftTarget.transform);
        }
    }
}
