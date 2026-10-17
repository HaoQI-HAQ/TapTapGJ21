using UnityEngine;

namespace PAO
{
    /// <summary>
    /// 泡泡宝箱：用**对应类型的泡泡**碰到它才会打开。
    ///
    /// 【现在的定位：收集品，不是通关必需】
    /// 这一关的设定是"三种泡泡一开始就有"，所以宝箱**不再解锁能力**。
    /// 它现在是一个"你得用对的那种泡泡才打得开"的小目标 ——
    /// 打开只是个正反馈，不影响能不能过关。
    ///
    /// 【为什么还用类型匹配的方式开】
    /// 强制"用对的泡泡打它"本身就是一句无声的教学：
    /// 玩家想开这个箱子，就得先摸清那种泡泡怎么用。
    ///
    /// 【开箱流程】
    ///   1. 判断飘进来的泡泡类型对不对（不对就什么都不做）
    ///   2. 切到那种泡泡（可选，算个"确认你会用它"的仪式）
    ///   3. 放开箱音（AudioSource.PlayOneShot）
    ///   4. 把箱子盖弹起来 + 淡出，然后销毁
    ///
    /// 【音乐资源】
    /// 开箱音是一个 AudioClip 字段，**故意留空** ——
    /// 音频资源请自行导入后拖进 Inspector。代码里不内置任何音频文件。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("PAO/关卡/泡泡宝箱（Chest Pickup）")]
    public sealed class ChestAbilityPickup : MonoBehaviour
    {
        [Header("内容")]
        [Tooltip("这个箱子要哪种泡泡才打得开")]
        [SerializeField] private BubbleType m_Ability = BubbleType.Bouncy;

        [Tooltip("开箱时是否自动切到对应泡泡。算个「确认你会用它」的小仪式")]
        [SerializeField] private bool m_SwitchToItOnOpen = true;

        [Header("音效")]
        [Tooltip("开箱音。留空则静音 —— 请自行导入音频资源后拖进来")]
        [SerializeField] private AudioClip m_OpenSound;

        [Tooltip("开箱音音量")]
        [Range(0f, 1f)]
        [SerializeField] private float m_Volume = 0.9f;

        [Header("外观")]
        [Tooltip("箱盖。开箱时会被顶起来。留空则尝试找一个叫 Lid 的子物体")]
        [SerializeField] private Transform m_Lid;

        [Tooltip("箱盖弹起的高度（米）")]
        [SerializeField] private float m_LidRise = 1.2f;

        [Tooltip("开箱后多久销毁自己（秒）")]
        [SerializeField] private float m_DestroyDelay = 1.4f;

        [Header("提示")]
        [Tooltip("打开前是否在箱子上方悬着一行小字，说明要用哪种泡泡")]
        [SerializeField] private bool m_ShowHint = true;

        [Header("调试")]
        [SerializeField] private bool m_Log = true;

        private bool m_Opened;
        private TextMesh m_Hint;

        private void Awake()
        {
            if (m_Lid == null)
            {
                Transform found = transform.Find("Lid");
                if (found != null)
                {
                    m_Lid = found;
                }
            }

            if (m_ShowHint)
            {
                BuildHint();
            }
        }

        /// <summary>
        /// 在箱子上方浮一行小字，告诉玩家该用哪种泡泡。
        ///
        /// 【字体】TextMesh 默认用的是内置 Arial，**不含汉字**，直接写中文会全是方块。
        /// 所以这里用 Font.CreateDynamicFontFromOSFont 现取一个系统中文字体
        /// （微软雅黑 → 黑体，都没有就退回默认，中文显示不好看但不会崩）。
        /// 这只是白盒阶段的临时提示，正式做法应该是 HUD 里给个图标。
        /// </summary>
        private void BuildHint()
        {
            var go = new GameObject("Hint");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 1.9f, 0f);

            m_Hint = go.AddComponent<TextMesh>();
            m_Hint.text = "用「" + BubbleTypeSwitcher.DisplayNameOf(m_Ability) + "」打开";
            m_Hint.characterSize = 0.14f;
            m_Hint.fontSize = 64;
            m_Hint.anchor = TextAnchor.MiddleCenter;
            m_Hint.alignment = TextAlignment.Center;
            m_Hint.color = new Color(0.18f, 0.20f, 0.24f);

            Font cjk = LoadChineseFont();
            if (cjk != null)
            {
                m_Hint.font = cjk;

                // TextMesh 用自定义字体时必须把字体材质也换掉，
                // 只换 font 不换 material 会拿旧图集采样新字形 → 什么都画不出来
                if (cjk.material != null)
                {
                    go.GetComponent<MeshRenderer>().sharedMaterial = cjk.material;
                }
            }

            // 让它一直正对摄像机（场景里的主相机）
            var billboard = go.AddComponent<BillboardToCamera>();
            billboard.Configure();
        }

        /// <summary>从操作系统里取一个含中文的字体。取不到返回 null。</summary>
        private static Font LoadChineseFont()
        {
            string[] candidates = { "Microsoft YaHei", "微软雅黑", "SimHei", "黑体", "SimSun" };

            for (int i = 0; i < candidates.Length; i++)
            {
                Font font = Font.CreateDynamicFontFromOSFont(candidates[i], 64);
                if (font != null)
                {
                    return font;
                }
            }

            return null;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (m_Opened)
            {
                return;
            }

            // 判断飘进来的是不是泡泡，以及类型对不对
            Bubble bubble = other.GetComponentInParent<Bubble>();

            if (bubble == null)
            {
                return;
            }

            if (bubble.Type != m_Ability)
            {
                // 类型不对：不打开。给一条日志，方便排查"为什么打不开"
                if (m_Log)
                {
                    Debug.Log("[ChestAbilityPickup] \"" + name + "\" 需要的是 " +
                              BubbleTypeSwitcher.DisplayNameOf(m_Ability) +
                              "，但打过来的是 " + BubbleTypeSwitcher.DisplayNameOf(bubble.Type) +
                              "，箱子没开。", this);
                }

                return;
            }

            Open();
        }

        private void Open()
        {
            m_Opened = true;

            // ---- 打开的效果 ----
            //
            // 【不再是"解锁能力"】
            // 这一关的设定改成"三种泡泡一开始就有"，所以宝箱不再负责解锁。
            // 它现在是个**收集品**：用对的那种泡泡打到它，它就开了，
            // 顺便把你切到那种泡泡上 —— 算个"确认你会用它"的小仪式。
            //
            // （早先这里调过 switcher.UnlockAbility，那套能力锁已经整个删掉了。）
            BubbleTypeSwitcher switcher = BubbleTypeSwitcher.Instance;

            if (switcher != null && m_SwitchToItOnOpen)
            {
                switcher.SetType(m_Ability);
            }

            if (m_Log)
            {
                Debug.Log("[ChestAbilityPickup] \"" + name + "\" 打开了（收集品）：" +
                          BubbleTypeSwitcher.DisplayNameOf(m_Ability) +
                          (switcher == null ? "（场景里没有 BubbleTypeSwitcher，没做切换）" : ""),
                          this);
            }

            // ---- 音效 ----
            PlayOpenSound();

            // ---- 表现：盖子弹起 + 淡出 ----
            if (m_Lid != null)
            {
                Vector3 p = m_Lid.localPosition;
                m_Lid.localPosition = p + new Vector3(0f, m_LidRise, 0f);
                m_Lid.localRotation = Quaternion.Euler(-18f, 0f, 0f);
            }

            if (m_Hint != null)
            {
                Destroy(m_Hint.gameObject);
            }

            // 箱子本体淡出（材质是共享的，所以走 MaterialPropertyBlock 不能改透明度，
            // 这里直接缩小到消失 —— 简单且不会污染别的物体）
            StartCoroutine(ShrinkAndDie());
        }

        private void PlayOpenSound()
        {
            if (m_OpenSound == null)
            {
                if (m_Log)
                {
                    Debug.Log("[ChestAbilityPickup] \"" + name + "\" 没有配开箱音。" +
                              "把音频资源拖到 Inspector 的 Open Sound 上即可。", this);
                }

                return;
            }

            // 用一个独立的 AudioSource 播一次性音效。
            // spatialBlend = 0 表示 2D —— 开箱音通常该是 2D 的，不然走远点就听不见。
            var go = new GameObject("OpenSound");
            go.transform.position = transform.position;

            var source = go.AddComponent<AudioSource>();
            source.clip = m_OpenSound;
            source.volume = m_Volume;
            source.spatialBlend = 0f;
            source.Play();

            // 播完自己销毁，别在场景里留一堆 AudioSource
            Destroy(go, m_OpenSound.length + 0.2f);
        }

        private System.Collections.IEnumerator ShrinkAndDie()
        {
            float duration = Mathf.Max(0.1f, m_DestroyDelay);
            Vector3 startScale = transform.localScale;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                // 等一小会儿再开始缩，让玩家看清"箱子开了"
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01((elapsed - duration * 0.5f) / (duration * 0.5f));
                transform.localScale = Vector3.Lerp(startScale, startScale * 0.01f, t);
                yield return null;
            }

            Destroy(gameObject);
        }

        /// <summary>由搭建脚本在生成时调用，写入解锁的能力和音效。</summary>
        public void Configure(BubbleType ability, AudioClip openSound, Transform lid, bool showHint)
        {
            m_Ability = ability;
            m_OpenSound = openSound;
            m_Lid = lid;
            m_ShowHint = showHint;
        }
    }
}
