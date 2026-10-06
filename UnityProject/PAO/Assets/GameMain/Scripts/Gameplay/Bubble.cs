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
            if (m_Container == null)
            {
                return;
            }

            Rigidbody body = GetComponent<Rigidbody>();
            if (body == null || body.isKinematic)
            {
                return;
            }

            Vector3 toCenter = m_Container.Center - transform.position;
            body.AddForce(toCenter * m_Container.GatherForce, ForceMode.Acceleration);
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

            // 只粘泡泡，不粘墙和地面
            Bubble other = collision.collider.GetComponentInParent<Bubble>();
            if (other == null || other == this)
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
        private void HandleBomb(Collision collision)
        {
            if (m_BombSettings == null)
            {
                return;
            }
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
                if (m_StickySettings == null)
                {
                    return false;   // 只有浮粘泡泡能载人
                }

                if (Rider != null)
                {
                    return false;   // 已经有人了
                }

                return GetDiameter() >= m_StickySettings.rideMinSize;
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
            ApplyBuoyancyWithRide();
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
            if (m_BombSettings == null)
            {
                Destroy(gameObject);
                return;
            }

            Vector3 center = transform.position;
            float radius = m_BombSettings.blastRadius;
            float force = m_BombSettings.blastForce;

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
            if (m_BombSettings.detonateTerrains)
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

            Destroy(gameObject);
        }
    }
}
