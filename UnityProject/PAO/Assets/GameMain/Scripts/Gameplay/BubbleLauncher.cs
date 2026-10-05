using UnityEngine;

namespace PAO
{
    /// <summary>
    /// 泡泡发射器：按住鼠标左键蓄力让泡泡变大，松开射出。
    ///
    /// 操作：
    ///   点按左键 → 立刻射出一个小泡泡
    ///   长按左键 → 泡泡停在身前不断变大（不发射），松开才射出去
    ///
    /// 【场景里怎么搭】
    /// 挂在 Player 上即可，零配置可跑。
    /// 想控制生成位置就建一个空物体当 Muzzle（比如手部），拖进对应字段。
    ///
    /// 【泡泡外观】
    /// 指定 Bubble Prefab 就用你的美术资源；
    /// 留空则用代码生成的蓝色球体，方便先跑通手感。
    /// </summary>
    public class BubbleLauncher : MonoBehaviour
    {
        [Header("蓄力")]
        [Tooltip("蓄满力需要的时间（秒）。点按即为不足这个时间就松手")]
        [SerializeField] private float m_MaxChargeTime = 2f;

        [Tooltip("点按时泡泡的直径（米）")]
        [SerializeField] private float m_MinSize = 0.2f;

        [Tooltip("蓄满力时泡泡的直径（米）")]
        [SerializeField] private float m_MaxSize = 1.5f;

        [Header("生成位置")]
        [Tooltip("泡泡生成基准点。留空则以角色自身为基准")]
        [SerializeField] private Transform m_Muzzle;

        [Tooltip("相对基准点的局部偏移（米）：X=右 / Y=上 / Z=前。配不配 Muzzle 都生效")]
        [SerializeField] private Vector3 m_SpawnOffset = new Vector3(0f, 1.4f, 0.6f);

        [Header("吹泡成长")]
        [Tooltip("蓄力变大时圆心向外推移的系数。1 = 推移量等于半径（真实吹泡，背面贴着吹气点）；0 = 圆心原地膨胀")]
        [SerializeField] private float m_GrowthPushFactor = 1f;

        [Tooltip("圆心推移方向（局部）：X=右 / Y=上 / Z=前。默认沿正前方，可改成一侧或斜上方")]
        [SerializeField] private Vector3 m_GrowthPushDirection = new Vector3(0f, 0f, 1f);

        [Header("发射")]
        [Tooltip("点按时的初速度（米/秒）")]
        [SerializeField] private float m_MinLaunchSpeed = 8f;

        [Tooltip("蓄满力时的初速度（米/秒）")]
        [SerializeField] private float m_MaxLaunchSpeed = 22f;

        [Header("泡泡")]
        [Tooltip("泡泡预制体。留空则用代码生成的球体")]
        [SerializeField] private GameObject m_BubblePrefab;

        [Tooltip("代码生成泡泡时的颜色")]
        [SerializeField] private Color m_BubbleColor = new Color(0.55f, 0.85f, 1f, 1f);

        [Tooltip("泡泡存活时间（秒），到点自动消失")]
        [SerializeField] private float m_BubbleLifeTime = 10f;

        [Tooltip("泡泡是否受重力。关掉即为漂浮（更符合泡泡的手感）")]
        [SerializeField] private bool m_BubbleUseGravity = false;

        [Header("泡泡浮力")]
        [Tooltip("最小泡泡的上升速度（米/秒），最快")]
        [SerializeField] private float m_SmallRiseSpeed = 2.5f;

        [Tooltip("最大泡泡的上升速度（米/秒），最慢")]
        [SerializeField] private float m_LargeRiseSpeed = 0.25f;

        [Tooltip("空气阻力。越大越快进入匀速、飘得越稳，但飞得越近")]
        [SerializeField] private float m_BubbleDrag = 0.8f;

        // ---------- 运行时状态 ----------
        private GameObject m_CurrentBubble;     // 正在蓄力的泡泡
        private float m_ChargeTime;             // 已蓄力时间
        private bool m_Charging;
        private readonly Material[] m_RuntimeMaterials = new Material[3];   // 按泡泡类型缓存的材质，避免每次新建造成泄漏

        /// <summary>
        /// 蓄力进度 0~1，UI 想画蓄力条可以读它。
        /// </summary>
        public float ChargeProgress
        {
            get { return m_MaxChargeTime > 0f ? Mathf.Clamp01(m_ChargeTime / m_MaxChargeTime) : 1f; }
        }

        /// <summary>
        /// 是否正在蓄力，UI 想显示提示可以读它。
        /// </summary>
        public bool IsCharging
        {
            get { return m_Charging; }
        }

        private void Update()
        {
            if (Input.GetMouseButtonDown(0))
            {
                BeginCharge();
            }

            if (m_Charging && Input.GetMouseButton(0))
            {
                UpdateCharge();
            }

            if (m_Charging && Input.GetMouseButtonUp(0))
            {
                Release();
            }
        }

        private void OnDisable()
        {
            // 蓄力途中被禁用（比如退出 Play）要收拾干净，别留个泡泡在原地
            if (m_CurrentBubble != null)
            {
                Destroy(m_CurrentBubble);
                m_CurrentBubble = null;
            }

            m_Charging = false;
            m_ChargeTime = 0f;
        }

        /// <summary>
        /// 按下左键：生成泡泡，进入蓄力状态。
        /// </summary>
        private void BeginCharge()
        {
            m_Charging = true;
            m_ChargeTime = 0f;

            Vector3 spawnPosition = GetSpawnPosition();
            Quaternion spawnRotation = GetSpawnRotation();

            m_CurrentBubble = CreateBubble(spawnPosition, spawnRotation);
            ApplyChargeSize();
        }

        /// <summary>
        /// 按住左键：泡泡跟随生成点，并按蓄力进度变大。
        /// </summary>
        private void UpdateCharge()
        {
            m_ChargeTime += Time.deltaTime;

            if (m_CurrentBubble != null)
            {
                m_CurrentBubble.transform.SetPositionAndRotation(GetSpawnPosition(), GetSpawnRotation());
            }

            ApplyChargeSize();
        }

        /// <summary>
        /// 松开左键：泡泡脱离、按蓄力换算速度射出。
        /// </summary>
        private void Release()
        {
            m_Charging = false;

            if (m_CurrentBubble == null)
            {
                return;
            }

            GameObject bubble = m_CurrentBubble;
            m_CurrentBubble = null;

            // 点按与长按在这里统一：蓄力时间越久，速度越快
            float speed = Mathf.Lerp(m_MinLaunchSpeed, m_MaxLaunchSpeed, ChargeProgress);
            Vector3 direction = GetSpawnRotation() * Vector3.forward;

            bubble.transform.SetParent(null);

            Rigidbody body = bubble.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.isKinematic = false;
                body.useGravity = m_BubbleUseGravity;
                body.velocity = direction * speed;

                ApplyBuoyancy(bubble, body);
            }

            // 发射后恢复实体碰撞（蓄力期间是触发器）
            Collider bubbleCollider = bubble.GetComponent<Collider>();
            if (bubbleCollider != null)
            {
                bubbleCollider.isTrigger = false;
            }

            Destroy(bubble, m_BubbleLifeTime);
        }

        /// <summary>
        /// 给泡泡加浮力：越小升得越快，越大升得越慢。
        /// 用 ConstantForce 持续施加向上的力，配合空气阻力，
        /// 泡泡会在飞行一小段后稳定到目标速度匀速上升。
        /// </summary>
        private void ApplyBuoyancy(GameObject bubble, Rigidbody body)
        {
            // 按尺寸在「小泡快、大泡慢」之间插值出目标上升速度
            float sizeProgress = Mathf.InverseLerp(m_MinSize, m_MaxSize, GetCurrentSize());
            float targetRiseSpeed = Mathf.Lerp(m_SmallRiseSpeed, m_LargeRiseSpeed, sizeProgress);

            // 空气阻力：让泡泡飞一段后自然减速，同时让上升稳定为匀速
            body.drag = m_BubbleDrag;
            body.angularDrag = m_BubbleDrag;

            // 匀速时阻力与浮力平衡：F = v * drag * mass
            ConstantForce buoyancy = bubble.GetComponent<ConstantForce>();
            if (buoyancy == null)
            {
                buoyancy = bubble.AddComponent<ConstantForce>();
            }

            buoyancy.force = Vector3.up * (targetRiseSpeed * m_BubbleDrag * body.mass);
        }

        /// <summary>
        /// 把蓄力进度映射成泡泡直径。点按会得到最小尺寸，蓄满得到最大尺寸。
        /// </summary>
        private void ApplyChargeSize()
        {
            if (m_CurrentBubble == null)
            {
                return;
            }

            m_CurrentBubble.transform.localScale = Vector3.one * GetCurrentSize();
        }

        /// <summary>
        /// 当前蓄力对应的泡泡直径（米）。位置推算与缩放都读它，保证两者一致。
        /// </summary>
        private float GetCurrentSize()
        {
            return Mathf.Lerp(m_MinSize, m_MaxSize, ChargeProgress);
        }

        /// <summary>
        /// 生成一个泡泡。优先用预制体，没配就用代码搓一个球。
        /// </summary>
        private GameObject CreateBubble(Vector3 position, Quaternion rotation)
        {
            GameObject bubble;

            if (m_BubblePrefab != null)
            {
                bubble = Instantiate(m_BubblePrefab, position, rotation);
                bubble.name = "Bubble";
            }
            else
            {
                bubble = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                bubble.name = "Bubble";
                bubble.transform.SetPositionAndRotation(position, rotation);
                ApplyFallbackMaterial(bubble);
            }

            // 蓄力期间不参与物理，免得把角色自己顶开
            Rigidbody body = bubble.GetComponent<Rigidbody>();
            if (body == null)
            {
                body = bubble.AddComponent<Rigidbody>();
            }

            body.isKinematic = true;
            body.useGravity = false;

            // 蓄力期间设为触发器，不与角色碰撞
            Collider bubbleCollider = bubble.GetComponent<Collider>();
            if (bubbleCollider != null)
            {
                bubbleCollider.isTrigger = true;

                // 额外保险：忽略与玩家自身的碰撞，避免发射瞬间弹回
                Collider selfCollider = GetComponent<Collider>();
                if (selfCollider != null)
                {
                    Physics.IgnoreCollision(bubbleCollider, selfCollider, true);
                }
            }

            return bubble;
        }

        /// <summary>
        /// 代码生成泡泡时给它上色。找不到 URP 着色器就退回内置 Standard。
        /// </summary>
        private void ApplyFallbackMaterial(GameObject bubble)
        {
            Renderer bubbleRenderer = bubble.GetComponent<Renderer>();
            if (bubbleRenderer == null)
            {
                return;
            }

            int typeIndex = GetCurrentTypeIndex();

            // 材质只建一次并复用，否则每次点按都 new 一个，材质会越攒越多
            if (m_RuntimeMaterials[typeIndex] == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                {
                    shader = Shader.Find("Standard");
                }

                if (shader == null)
                {
                    return;
                }

                Material material = new Material(shader);
                material.color = GetBubbleColor();
                m_RuntimeMaterials[typeIndex] = material;
            }

            bubbleRenderer.sharedMaterial = m_RuntimeMaterials[typeIndex];
        }

        /// <summary>
        /// 生成点位置：配了 Muzzle 就用它，否则按角色朝向自动推算。
        /// </summary>
        private Vector3 GetSpawnPosition()
        {
            // 把局部偏移转到世界方向再叠加：这样 X/Y/Z 分别对应基准点的
            // 右 / 上 / 前，角色转身后偏移也跟着转，调参最直观
            Vector3 basePosition = GetSpawnBasePosition() + GetSpawnRotation() * m_SpawnOffset;

            // 吹泡成长：圆心随半径沿指定方向往外推。
            // 系数为 1 时推移量正好等于半径，泡泡背面始终贴着吹气点，
            // 看起来就是从那一点"吹"出来的，而不是原地膨胀。
            float radius = GetCurrentSize() * 0.5f;
            Vector3 pushDirection = GetSpawnRotation() * m_GrowthPushDirection.normalized;

            return basePosition + pushDirection * (radius * m_GrowthPushFactor);
        }

        /// <summary>
        /// 生成基准点：配了 Muzzle 就是它，否则是角色自身。
        /// </summary>
        private Vector3 GetSpawnBasePosition()
        {
            return m_Muzzle != null ? m_Muzzle.position : transform.position;
        }

        /// <summary>
        /// 生成点朝向：泡泡沿这个方向射出（角色朝向与视角一致）。
        /// </summary>
        private Quaternion GetSpawnRotation()
        {
            if (m_Muzzle != null)
            {
                return m_Muzzle.rotation;
            }

            return transform.rotation;
        }

        /// <summary>
        /// 在 Scene 视图里画出基准点与生成点，方便对着调偏移。
        /// </summary>
        private void OnDrawGizmosSelected()
        {
            Vector3 basePosition = GetSpawnBasePosition();
            Vector3 spawnPosition = GetSpawnPosition();

            Gizmos.color = new Color(0.35f, 0.8f, 1f, 1f);
            Gizmos.DrawWireSphere(basePosition, 0.08f);
            Gizmos.DrawWireSphere(spawnPosition, 0.15f);
            Gizmos.DrawLine(basePosition, spawnPosition);
        }

        /// <summary>
        /// 当前泡泡类型的序号。场景里没挂切换器时按浮力泡泡处理。
        /// </summary>
        private int GetCurrentTypeIndex()
        {
            BubbleTypeSwitcher switcher = GetComponent<BubbleTypeSwitcher>();
            return switcher != null ? (int)switcher.Current : 0;
        }

        /// <summary>
        /// 泡泡颜色：挂了切换器就用该类型的代表色，否则用 Inspector 里配的颜色。
        /// </summary>
        private Color GetBubbleColor()
        {
            BubbleTypeSwitcher switcher = GetComponent<BubbleTypeSwitcher>();
            return switcher != null ? switcher.CurrentColor : m_BubbleColor;
        }
    }
}
