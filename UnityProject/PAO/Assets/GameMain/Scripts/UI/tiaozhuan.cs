using UnityEngine;
using UnityEngine.SceneManagement;
using PAO;   // PlayerController 在这个命名空间里

/// <summary>
/// 触碰跳转：物体身上挂这个脚本，玩家碰到就切到指定场景。
///
/// 【用法】
///   1. 把本脚本挂到任意物体上
///   2. 挂上后会自动加一个 BoxCollider（Trigger），在 Inspector 里调大小和位置
///   3. 填 Scene Name = 目标场景名，必须和 Build Settings 里的名字完全一致（例如 Map1）
///   4. 玩家碰到就跳转
///
/// 【注意】
///   目标场景必须先加进 Build Settings（File → Build Settings），
///   否则 LoadScene 会失败。脚本在开始时就会检查并给出明确提示。
/// </summary>
public class tiaozhuan : MonoBehaviour
{
    [Header("目标场景")]
    [Tooltip("要跳转到的场景名，必须和 Build Settings 里的名字完全一致，例如 Map1")]
    public string sceneName = "";

    [Header("触发设置")]
    [Tooltip("只触发一次。关掉则玩家离开后再进来可以反复触发")]
    public bool once = true;

    [Tooltip("碰到后延迟多久跳转（秒）。0 = 立刻跳")]
    public float delay = 0f;

    [Header("调试")]
    [Tooltip("打勾后会在 Console 打印触发日志，方便确认有没有碰到")]
    public bool showLog = true;

    private bool triggered;

    void Reset()
    {
        // 刚挂上脚本时给一个合适的范围，免得碰撞体小到看不见
        BoxCollider box = GetComponent<BoxCollider>();
        if (box == null)
        {
            box = gameObject.AddComponent<BoxCollider>();
        }

        box.isTrigger = true;
        box.size = new Vector3(3f, 3f, 3f);
        box.center = new Vector3(0f, 1.5f, 0f);
    }

    void Awake()
    {
        // 兜底：没加碰撞体、或者忘了勾 Is Trigger 时自动补上。
        // 少了这两样，OnTriggerEnter 永远不会被调用，而且不会有任何报错。
        Collider col = GetComponent<Collider>();

        if (col == null)
        {
            BoxCollider box = gameObject.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(3f, 3f, 3f);
            box.center = new Vector3(0f, 1.5f, 0f);

            Debug.LogWarning("[tiaozhuan] " + name + " 上没有 Collider，已自动补一个 Trigger BoxCollider。");
        }
        else if (!col.isTrigger)
        {
            col.isTrigger = true;
            Debug.LogWarning("[tiaozhuan] " + name + " 的 Collider 没勾 Is Trigger，已自动勾上。");
        }

        // 提前检查场景在不在 Build Settings，免得碰到时才失败
        if (!string.IsNullOrEmpty(sceneName) && !Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError("[tiaozhuan] 场景 \"" + sceneName +
                           "\" 不在 Build Settings 里，跳转会失败。请先 File → Build Settings 把它加进去。", this);
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (once && triggered)
        {
            return;
        }

        // 认玩家：工程里没有自定义 Tag，所以按组件判断。
        // 玩家的 CharacterController 能触发 OnTriggerEnter，所以这里只检查进来的对象。
        if (other.GetComponentInParent<PlayerController>() == null &&
            other.GetComponentInParent<CharacterController>() == null)
        {
            return;
        }

        triggered = true;

        if (showLog)
        {
            Debug.Log("[tiaozhuan] " + name + " 被 " + other.name + " 碰到，准备跳转到 " + sceneName, this);
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

        Debug.Log("[tiaozhuan] 加载场景：" + sceneName, this);
        SceneManager.LoadScene(sceneName);
    }

#if UNITY_EDITOR
    // 选中物体时在 Scene 视图画出触发范围，方便摆位置
    void OnDrawGizmosSelected()
    {
        Collider col = GetComponent<Collider>();
        if (col == null)
        {
            return;
        }

        Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.5f);
        Gizmos.matrix = transform.localToWorldMatrix;

        BoxCollider box = col as BoxCollider;
        if (box != null)
        {
            Gizmos.DrawWireCube(box.center, box.size);
            return;
        }

        SphereCollider sphere = col as SphereCollider;
        if (sphere != null)
        {
            Gizmos.DrawWireSphere(sphere.center, sphere.radius);
            return;
        }

        CapsuleCollider capsule = col as CapsuleCollider;
        if (capsule != null)
        {
            Gizmos.DrawWireSphere(capsule.center, capsule.radius);
        }
    }
#endif
}
