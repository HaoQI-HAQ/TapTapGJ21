using UnityEngine;
using UnityEngine.SceneManagement;
using PAO;   // PlayerController 在这个命名空间里

/// <summary>
/// 范围跳转：玩家进入**指定半径**就切到目标场景。
///
/// ============================================================
/// 【为什么改成"按距离判定"而不是"撞碰撞体"】
/// ============================================================
/// 碰撞体方案要同时满足一堆条件才会触发，任何一条不满足都**静默失效**、
/// 不报任何错，非常难查：
///   · 碰撞体必须勾 Is Trigger
///   · 触发流程要求其中之一带 Rigidbody（或对方是 CharacterController）
///   · 物体上如果有多个碰撞体，脚本可能改错那一个
///     （早先的实现会把落板的实心 MeshCollider 改成 Trigger，
///      结果玩家直接从平台上掉下去）
///   · 玩家坐在泡泡里时 CharacterController 被禁用，触发不一定可靠
///
/// 现在改成**每帧算一次距离**：玩家到判定球心的距离小于 Range 就触发。
/// 不依赖任何碰撞体、不依赖刚体、人在泡泡里也照样能用。
/// 范围直接在 Inspector 里调，Scene 视图里能看到那个球。
///
/// 【这个脚本不会碰你的碰撞体】
/// 它不再自动加碰撞体、也不会把实心碰撞体改成 Trigger。
///
/// ============================================================
/// 【用法】
/// ============================================================
///   1. 把脚本挂到任意物体上（有没有碰撞体都行）
///   2. Range 填触发半径（米）—— 默认 3
///   3. Range Offset 是判定球心相对物体的偏移（默认往上 1.5 米）
///   4. Scene Name 填目标场景名，必须和 Build Settings 里完全一致
///
/// 【想同时保留原来的碰撞体方式】
///   勾上 Use Trigger Collider，并保证物体上有一个 Is Trigger 的碰撞体。
///   两条路会同时生效，谁先到算谁，不会重复跳转。
///
/// 【注意】
///   目标场景必须先加进 Build Settings，否则 LoadScene 会失败。
/// ============================================================
/// </summary>
public class tiaozhuan : MonoBehaviour
{
    [Header("目标场景")]
    [Tooltip("要跳转到的场景名，必须和 Build Settings 里的名字完全一致，例如 Map1")]
    public string sceneName = "";

    [Header("触发范围")]
    [Tooltip("触发半径（米）。玩家离判定球心小于这个距离就跳转")]
    public float range = 3f;

    [Tooltip("判定球心相对物体原点的偏移。默认往上 1.5 米，让球心大概在物体中部")]
    public Vector3 rangeOffset = new Vector3(0f, 1.5f, 0f);

    [Tooltip("同时保留「碰到 Trigger 碰撞体」的触发方式。\n" +
             "只有物体上本来就有 Is Trigger 碰撞体时才有意义 —— 脚本不会自己加")]
    public bool useTriggerCollider = true;

    [Tooltip("勾上后，玩家必须**先离开范围**、之后再进入才会触发。\n\n" +
             "这是为了防止「出生点就在范围内 → 一开局立刻跳转」。\n" +
             "想让它一开始就地触发就取消勾选。")]
    public bool requireExitFirst = true;

    [Header("触发设置")]
    [Tooltip("只触发一次。关掉则玩家离开后再进来可以反复触发")]
    public bool once = true;

    [Tooltip("触发后延迟多久跳转（秒）。0 = 立刻跳")]
    public float delay = 0f;

    [Header("调试")]
    [Tooltip("打勾后会在 Console 打印触发日志")]
    public bool showLog = true;

    private bool triggered;

    /// <summary>
    /// 是否已经"就绪"（可以触发）。
    ///
    /// 【为什么要这个】
    /// 如果出生点正好落在范围里，第一帧就会满足"距离 &lt; range"，
    /// 于是**一进游戏立刻跳走** —— 玩家根本没机会玩。
    ///
    /// 所以勾了 requireExitFirst 时，必须观测到**玩家在范围外**一次，
    /// 这个判定区才算"就绪"；之后玩家再走进来才触发。
    /// </summary>
    private bool m_Armed;

    /// <summary>缓存的玩家。第一次用到时找一次，之后复用。</summary>
    private static PlayerController s_Player;

    /// <summary>判定球心的世界坐标。</summary>
    private Vector3 Center
    {
        get { return transform.position + rangeOffset; }
    }

    void Awake()
    {
        if (range <= 0f)
        {
            range = 3f;
        }

        // 提前检查场景在不在 Build Settings，免得玩家走到跟前才失败
        if (string.IsNullOrEmpty(sceneName))
        {
            Debug.LogWarning("[tiaozhuan] " + name + " 还没填 Scene Name，靠近时不会跳转。", this);
        }
        else if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError("[tiaozhuan] 场景 \"" + sceneName +
                           "\" 不在 Build Settings 里，跳转会失败。请先 File → Build Settings 把它加进去。", this);
        }
    }

    void Update()
    {
        if (once && triggered)
        {
            return;
        }

        PlayerController player = ResolvePlayer();

        if (player == null)
        {
            return;      // 场景里还没有玩家（比如菜单场景）
        }

        // 用玩家身体中部的高度算，避免"球心在脚底、人站上去还差一点"
        Vector3 playerPoint = player.transform.position;
        CharacterController controller = player.GetComponent<CharacterController>();

        if (controller != null)
        {
            playerPoint += Vector3.up * controller.center.y;
        }

        float distance = Vector3.Distance(playerPoint, Center);
        bool outside = distance > range;

        // ---- 就绪判定：先确认玩家在范围外，之后进入才算"进入" ----
        if (!m_Armed)
        {
            if (!requireExitFirst || outside)
            {
                m_Armed = true;

                if (showLog && requireExitFirst)
                {
                    Debug.Log("[tiaozhuan] " + name + " 已就绪（玩家当前在范围外，" +
                              distance.ToString("F2") + "m > " + range.ToString("F2") + "m）。", this);
                }
            }

            if (!m_Armed)
            {
                return;      // 出生点就在范围内，还没就绪 —— 不触发
            }
        }

        if (outside)
        {
            return;
        }

        Fire("玩家进入范围（距离 " + distance.ToString("F2") + "m，范围 " + range.ToString("F2") + "m）");
    }

    /// <summary>碰撞体方式（可选）。物体上本来就有 Trigger 时才会走到这里。</summary>
    void OnTriggerEnter(Collider other)
    {
        if (!useTriggerCollider)
        {
            return;
        }

        if (once && triggered)
        {
            return;
        }

        // 同样要遵守"先离开再进入"的就绪规则，
        // 否则出生点落在触发区里时，一进游戏就会被碰撞事件顶走。
        if (requireExitFirst && !m_Armed)
        {
            return;
        }

        if (other.GetComponentInParent<PlayerController>() == null &&
            other.GetComponentInParent<CharacterController>() == null)
        {
            return;
        }

        Fire("被 " + other.name + " 碰到");
    }

    /// <summary>真正触发一次。范围判定和碰撞体判定都走这里。</summary>
    private void Fire(string reason)
    {
        if (once && triggered)
        {
            return;
        }

        triggered = true;

        if (showLog)
        {
            Debug.Log("[tiaozhuan] " + name + " 触发：" + reason +
                      "，准备跳转到 \"" + sceneName + "\"", this);
        }

        if (delay > 0f)
        {
            Invoke("LoadNow", delay);
        }
        else
        {
            LoadNow();
        }
    }

    /// <summary>真正加载场景。</summary>
    public void LoadNow()
    {
        if (string.IsNullOrEmpty(sceneName))
        {
            Debug.LogError("[tiaozhuan] " + name + " 没有填 Scene Name，跳转取消。", this);
            return;
        }

        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError("[tiaozhuan] 场景 \"" + sceneName +
                           "\" 不在 Build Settings 里，无法加载。", this);
            return;
        }

        // 走章节流程（会弹结算界面、由玩家点"前往下一章"）。
        // 但章节流程有几个前置条件（已在结算 / 正在加载 / 不在游戏场景），
        // 任一不满足就什么都不做 —— 那正是"触发了却没反应"的来源。
        // 所以检查返回值：没接管就自己直接切场景，保证一定跳过去。
        PAO.UI.V2.UIRuntime runtime = PAO.UI.V2.UIRuntime.Instance;

        if (runtime != null && runtime.IsGameplay)
        {
            if (runtime.CompleteChapter(sceneName))
            {
                return;
            }

            if (showLog)
            {
                Debug.Log("[tiaozhuan] 章节流程没有接管（可能已在结算或正在加载），" +
                          "改为直接加载场景 \"" + sceneName + "\"。", this);
            }
        }

        Debug.Log("[tiaozhuan] 加载场景：" + sceneName, this);
        SceneManager.LoadScene(sceneName);
    }

    /// <summary>找玩家。找不到返回 null，不算错误。</summary>
    private static PlayerController ResolvePlayer()
    {
        if (s_Player != null)
        {
            return s_Player;
        }

        s_Player = Object.FindFirstObjectByType<PlayerController>();

        return s_Player;
    }

#if UNITY_EDITOR
    /// <summary>选中时画出触发球，方便直接调范围。</summary>
    void OnDrawGizmosSelected()
    {
        Vector3 center = transform.position + rangeOffset;
        float r = Mathf.Max(0.01f, range);

        Gizmos.color = new Color(0.25f, 0.9f, 1f, 0.85f);
        Gizmos.DrawWireSphere(center, r);

        Gizmos.color = new Color(0.25f, 0.9f, 1f, 0.12f);
        Gizmos.DrawSphere(center, r);

        // 从物体原点到球心连一条线，方便看出偏移方向
        Gizmos.color = new Color(1f, 0.8f, 0.3f, 0.9f);
        Gizmos.DrawLine(transform.position, center);
    }
#endif
}
