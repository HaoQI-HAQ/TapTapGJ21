using UnityEngine;
using System.Collections.Generic;

namespace PAO
{
    /// <summary>
    /// 挂在每个飞出去的泡泡上：记录自己的类型，并执行该类型对应的行为。
    ///
    /// 浮粘泡泡：碰到其他泡泡时粘在一起   —— 已实现
    /// 弹力泡泡：碰到东西反弹             —— 参数已就位，行为待实现
    /// 炸弹泡泡：引爆                     —— 参数已就位，行为待实现
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

        private BubbleSettings m_Settings;
        private StickyBubbleSettings m_StickySettings;
        private BouncyBubbleSettings m_BouncySettings;
        private BombBubbleSettings m_BombSettings;
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
        /// 弹力泡泡：碰到东西反弹。
        /// TODO 待实现：按 m_BouncySettings.bounciness 反射速度，
        ///      并用 m_BouncySettings.maxBounceCount 限制次数。
        /// </summary>
        private void HandleBouncy(Collision collision)
        {
            if (m_BouncySettings == null)
            {
                return;
            }

            // 占位：目前不做任何事，泡泡行为等同于浮粘泡泡
        }

        /// <summary>
        /// 炸弹泡泡：引爆。
        /// TODO 待实现：按 m_BombSettings.blastRadius 找范围内物体，
        ///      用 blastForce 施加冲量，然后销毁自己。
        /// </summary>
        private void HandleBomb(Collision collision)
        {
            if (m_BombSettings == null)
            {
                return;
            }

            // 占位：目前不做任何事，泡泡行为等同于浮粘泡泡
        }

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

        /// <summary>
        /// 引爆：把半径内的刚体按爆炸力推开，然后销毁自己。
        /// 由 BombBubbleManager 在按下引爆键时调用。
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
    }
}
