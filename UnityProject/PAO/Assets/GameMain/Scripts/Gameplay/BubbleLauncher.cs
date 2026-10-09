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
    /// 【三种泡泡】
    /// 参数完全独立，在 Inspector 里分成三组：浮粘 / 弹力 / 炸弹。
    /// 当前用哪一种由 BubbleTypeSwitcher 决定（按 1/2/3 或滚轮切换）。
    /// 各类型的具体行为写在 Bubble.cs 里。
    ///
    /// 【场景里怎么搭】
    /// 挂在 Player 上即可，零配置可跑。
    /// 想控制生成位置就建一个空物体当 Muzzle（比如手部），拖进对应字段。
    /// </summary>
    public class BubbleLauncher : MonoBehaviour
    {
        [Header("蓄力")]
        [Tooltip("蓄满力需要的时间（秒）。点按即为不足这个时间就松手")]
        [SerializeField] private float m_MaxChargeTime = 2f;

        [Header("滞留")]
        [Tooltip("蓄力直径达到这个值（米）时，松手不再发射，而是把泡泡留在发射口等第二次点击")]
        [SerializeField] private float m_HoldThreshold = 1.0f;

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

        [Header("① 浮粘泡泡参数")]
        [SerializeField] private StickyBubbleSettings m_StickySettings = new StickyBubbleSettings();

        [Header("② 弹力泡泡参数（行为待实现）")]
        [SerializeField] private BouncyBubbleSettings m_BouncySettings = new BouncyBubbleSettings();

        [Header("③ 炸弹泡泡参数（行为待实现）")]
        [SerializeField] private BombBubbleSettings m_BombSettings = new BombBubbleSettings();

        // ---------- 运行时状态 ----------
        private GameObject m_CurrentBubble;     // 正在蓄力的泡泡
        private GameObject m_HeldBubble;        // 滞留在发射口的泡泡（等第二次点击发射）
        private float m_HeldChargeProgress;    // 它当初的蓄力进度，发射时换算速度用
        private BubbleType m_HeldType;         // 它是哪种泡泡
        private float m_ChargeTime;             // 已蓄力时间
        private bool m_Charging;

        // 按泡泡类型缓存的材质，避免每次生成都新建造成泄漏
        private readonly Material[] m_RuntimeMaterials = new Material[3];

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
            // 有泡泡滞留在发射口时，左键不再是蓄力，而是「发射它」
            if (m_HeldBubble != null)
            {
                if (Input.GetMouseButtonDown(0))
                {
                    LaunchHeldBubble();
                }

                return;
            }

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

            // 滞留在发射口的泡泡也一并清掉
            if (m_HeldBubble != null)
            {
                Destroy(m_HeldBubble);
                m_HeldBubble = null;
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

            m_CurrentBubble = CreateBubble(GetSpawnPosition(), GetSpawnRotation());
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
        /// 松开左键：够小就直接射出去；
        /// 蓄到 m_HoldThreshold 以上的，改为留在发射口，等第二次点击才发射。
        /// </summary>
        private void Release()
        {
            m_Charging = false;

            if (m_CurrentBubble == null)
            {
                return;
            }

            BubbleSettings settings = GetSettings();
            GameObject bubble = m_CurrentBubble;
            m_CurrentBubble = null;

            float progress = ChargeProgress;
            BubbleType type = GetCurrentType();
            float size = GetCurrentSize();

            // 够大：不发射，留在发射口。
            // 速度给零 —— 但它已经是「活的」泡泡了，浮力、粘性、弹力都在。
            if (size >= m_HoldThreshold)
            {
                m_HeldBubble = bubble;
                m_HeldChargeProgress = progress;
                m_HeldType = type;

                ActivateBubble(bubble, settings, type, progress, size, Vector3.zero, true);
                return;
            }

            // 不够大：照旧射出去，蓄力越久越快
            float speed = Mathf.Lerp(settings.minLaunchSpeed, settings.maxLaunchSpeed, progress);
            Vector3 direction = GetSpawnRotation() * Vector3.forward;

            ActivateBubble(bubble, settings, type, progress, size, direction * speed, false);
        }

        /// <summary>
        /// 发射滞留在发射口的泡泡：按当初蓄的力给速度。
        /// </summary>
        public void LaunchHeldBubble()
        {
            if (m_HeldBubble == null)
            {
                return;
            }

            GameObject bubble = m_HeldBubble;
            m_HeldBubble = null;

            BubbleSettings settings = GetHeldSettings();
            float speed = Mathf.Lerp(settings.minLaunchSpeed, settings.maxLaunchSpeed, m_HeldChargeProgress);
            Vector3 direction = GetSpawnRotation() * Vector3.forward;

            Rigidbody body = bubble != null ? bubble.GetComponent<Rigidbody>() : null;
            if (body != null)
            {
                body.isKinematic = false;
                body.useGravity = settings.useGravity;
                body.velocity = direction * speed;

                // 滞留期间浮力被清零过，这里要补回来，
                // 否则发射出去的泡泡再也不会上升了
                ApplyBuoyancy(bubble, body, settings, GetHeldSize());
            }

            // 通知泡泡：它从「滞留」变成「飞行」了
            Bubble behaviour = bubble != null ? bubble.GetComponent<Bubble>() : null;
            if (behaviour != null)
            {
                behaviour.OnLaunchedFromHold();
            }
        }

        /// <summary>滞留中的泡泡（没有则为 null）。UI 想提示可以读它。</summary>
        public bool HasHeldBubble
        {
            get { return m_HeldBubble != null; }
        }

        /// <summary>取滞留泡泡对应的那套参数。</summary>
        /// <summary>滞留泡泡当初的直径（米）。发射时补浮力要用它。</summary>
        private float GetHeldSize()
        {
            BubbleSettings settings = GetHeldSettings();
            return Mathf.Lerp(settings.minSize, settings.maxSize, m_HeldChargeProgress);
        }

        private BubbleSettings GetHeldSettings()
        {
            switch (m_HeldType)
            {
                case BubbleType.Bouncy:
                    return m_BouncySettings;

                case BubbleType.Bomb:
                    return m_BombSettings;

                default:
                    return m_StickySettings;
            }
        }

        /// <summary>
        /// 把泡泡从「生成物」变成「真正的泡泡」：脱离、激活物理、上浮力、开碰撞、登记、计时。
        /// 发射与滞留共用这里，区别只在 velocity。
        /// </summary>
        private void ActivateBubble(GameObject bubble, BubbleSettings settings, BubbleType type, float progress, float size, Vector3 velocity, bool isHeld)
        {
            if (bubble == null)
            {
                return;
            }

            bubble.transform.SetParent(null);

            Bubble behaviour = bubble.GetComponent<Bubble>();
            if (behaviour != null)
            {
                behaviour.SizeProgress = progress;
                behaviour.IsHeld = isHeld;

                // 滞留时它要负责把玩家提起来（只有浮粘泡泡会真提）
                behaviour.SetLiftTarget(GetComponent<PlayerController>());

                // 记下泡泡此刻相对玩家的位置。之后它就锁在这儿 ——
                // 松手时在哪就一直在哪，不会再跳到别的地方去。
                if (isHeld)
                {
                    Vector3 localOffset = transform.InverseTransformPoint(behaviour.transform.position);
                    behaviour.SetHeldOffset(localOffset);
                }
            }

            Rigidbody body = bubble.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.isKinematic = false;
                body.useGravity = settings.useGravity;
                body.velocity = velocity;

                ApplyBuoyancy(bubble, body, settings, size);
            }

            // 发射后恢复实体碰撞（蓄力期间是触发器），否则撞不到别的泡泡
            Collider bubbleCollider = bubble.GetComponent<Collider>();
            if (bubbleCollider != null)
            {
                bubbleCollider.isTrigger = false;
            }

            // 炸弹泡泡登记到管理器，排队等着引爆
            if (type == BubbleType.Bomb && behaviour != null)
            {
                BombBubbleManager bombManager = GetComponent<BombBubbleManager>();
                if (bombManager != null)
                {
                    bombManager.RegisterBomb(behaviour);
                }
            }

            // 交给泡泡自己计时，这样进了地形容器后还能取消掉，
            // 否则它会在容器里凭空消失
            if (behaviour != null)
            {
                behaviour.ScheduleLifeEnd(settings.lifeTime);
            }
            else
            {
                Destroy(bubble, settings.lifeTime);
            }
        }

        /// <summary>
        /// 给泡泡加浮力：越小升得越快，越大升得越慢。
        /// 用 ConstantForce 持续施加向上的力，配合空气阻力，
        /// 泡泡会在飞行一小段后稳定到目标速度匀速上升。
        /// </summary>
        private void ApplyBuoyancy(GameObject bubble, Rigidbody body, BubbleSettings settings, float size)
        {
            // 按尺寸在「小泡快、大泡慢」之间插值出目标上升速度
            // 用调用方传进来的尺寸，而不是 GetCurrentSize()：
            // 滞留的泡泡发射时蓄力早就重置了，现取会算错浮力
            float sizeProgress = Mathf.InverseLerp(settings.minSize, settings.maxSize, size);
            float targetRiseSpeed = Mathf.Lerp(settings.smallRiseSpeed, settings.largeRiseSpeed, sizeProgress);

            // 空气阻力：让泡泡飞一段后自然减速，同时让上升稳定为匀速
            body.drag = settings.drag;
            body.angularDrag = settings.drag;

            // 匀速时阻力与浮力平衡：F = v * drag * mass
            ConstantForce buoyancy = bubble.GetComponent<ConstantForce>();
            if (buoyancy == null)
            {
                buoyancy = bubble.AddComponent<ConstantForce>();
            }

            buoyancy.force = Vector3.up * (targetRiseSpeed * settings.drag * body.mass);
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
            BubbleSettings settings = GetSettings();
            return Mathf.Lerp(settings.minSize, settings.maxSize, ChargeProgress);
        }

        /// <summary>
        /// 生成一个泡泡。优先用该类型的预制体，没配就用代码搓一个球。
        /// 同时挂上 Bubble 组件并把类型与参数注入进去。
        /// </summary>
        private GameObject CreateBubble(Vector3 position, Quaternion rotation)
        {
            BubbleSettings settings = GetSettings();

            GameObject bubble;

            if (settings.prefab != null)
            {
                bubble = Instantiate(settings.prefab, position, rotation);
                bubble.name = "Bubble";
            }
            else
            {
                bubble = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                bubble.name = "Bubble";
                bubble.transform.SetPositionAndRotation(position, rotation);
                ApplyFallbackMaterial(bubble, settings);
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

            // 挂上行为组件并注入类型，让它知道自己是哪种泡泡
            Bubble behaviour = bubble.GetComponent<Bubble>();
            if (behaviour == null)
            {
                behaviour = bubble.AddComponent<Bubble>();
            }

            behaviour.Setup(GetCurrentType(), settings);

            // 登记一下被忽略的那个玩家碰撞体。
            // 泡泡粘住变成地形时，要靠它把碰撞恢复回来，人才能踩上去。
            Collider playerCollider = GetComponent<Collider>();
            if (playerCollider != null)
            {
                behaviour.SetIgnoredCollider(playerCollider);
            }

            return bubble;
        }

        /// <summary>
        /// 代码生成泡泡时给它上色。找不到 URP 着色器就退回内置 Standard。
        /// </summary>
        private void ApplyFallbackMaterial(GameObject bubble, BubbleSettings settings)
        {
            Renderer bubbleRenderer = bubble.GetComponent<Renderer>();
            if (bubbleRenderer == null)
            {
                return;
            }

            int typeIndex = GetCurrentTypeIndex();

            // 每种类型只建一次材质并复用，否则每次点按都 new 一个，材质会越攒越多
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
                material.color = settings.color;
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
            // 只取角色自身的朝向，不取 Muzzle 的。
            // 喷嘴模型常为了摆正外观而自带旋转（例如绕 X 转 90°），
            // 若跟着它走，整条弹道会被带偏——表现就是泡泡一出膛就朝下掉。
            return transform.rotation;
        }

        /// <summary>
        /// 当前泡泡类型。场景里没挂切换器时按浮粘泡泡处理。
        /// </summary>
        private BubbleType GetCurrentType()
        {
            BubbleTypeSwitcher switcher = GetComponent<BubbleTypeSwitcher>();
            return switcher != null ? switcher.Current : BubbleType.Sticky;
        }

        /// <summary>
        /// 当前类型的序号，用于取缓存的材质。
        /// </summary>
        private int GetCurrentTypeIndex()
        {
            return (int)GetCurrentType();
        }

        /// <summary>
        /// 取当前类型对应的那组参数。三种泡泡的参数互不影响。
        /// </summary>
        private BubbleSettings GetSettings()
        {
            switch (GetCurrentType())
            {
                case BubbleType.Bouncy:
                    return m_BouncySettings;

                case BubbleType.Bomb:
                    return m_BombSettings;

                default:
                    return m_StickySettings;
            }
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
    }
}
