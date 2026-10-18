using UnityEngine;
using PAO.BubbleTide;

namespace PAO
{
    /// <summary>
    /// 第一章「醒来」的潮水可视面（本地替代方案）。
    ///
    /// 【为什么不直接用 BubbleTide 的渲染 Feature】
    /// 那个 Feature 在 URP 14 下会黑屏（见 wengjie 搭建记录），目前没有装进 Renderer。
    /// 但潮水的**玩法**（推挤 / 终点淹没 / 失败判定）完全不依赖渲染 Feature ——
    /// 它只依赖 BubbleTideDirector 烤出来的场。所以这里只补一张“看得见的水面”，
    /// 用 Director.Progress（0 = 井底，1 = 淹到终点）把一张半透明水面从井底抬到塔顶。
    ///
    /// 这样：玩法是真的，画面也是真的，而且不会动全工程的 URP Renderer。
    ///
    /// 只挂在 Map0 的 C_TideSurface 上，不影响任何别的场景。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("PAO/关卡/第一章潮水面（Map0）")]
    public sealed class Map0TideSurface : MonoBehaviour
    {
        [Header("水位范围")]
        [Tooltip("潮水刚开始时水面的世界 Y")]
        [SerializeField] private float m_BottomY = 4.2f;

        [Tooltip("终点被淹时水面的世界 Y（比塔顶略高，视觉上把祭坛盖住）")]
        [SerializeField] private float m_TopY = 37.5f;

        [Tooltip("水面中心（水平位置），一般就是井心")]
        [SerializeField] private Vector2 m_Center = new Vector2(0f, 140f);

        [Header("调试")]
        [SerializeField] private bool m_Log = false;

        private BubbleTideDirector m_Director;
        private float m_LastProgress = -1f;

        private void Start()
        {
            m_Director = BubbleTideDirector.Instance;
            if (m_Director == null)
            {
                Debug.LogWarning("[Map0TideSurface] 场景里没有 BubbleTideDirector，水面会停在井底。", this);
            }
            Apply(0f);
        }

        private void LateUpdate()
        {
            if (m_Director == null)
            {
                m_Director = BubbleTideDirector.Instance;
            }

            float progress = 0f;
            if (m_Director != null)
            {
                // Director.Progress 在 Deadline 烤出来之前返回 0，正好对应“水还没涨”
                progress = m_Director.Progress;
            }

            Apply(progress);
        }

        private void Apply(float progress)
        {
            if (Mathf.Abs(progress - m_LastProgress) < 0.0005f)
            {
                return;
            }

            m_LastProgress = progress;
            float y = Mathf.Lerp(m_BottomY, m_TopY, Mathf.Clamp01(progress));
            transform.position = new Vector3(m_Center.x, y, m_Center.y);

            if (m_Log)
            {
                Debug.Log(string.Format("[Map0TideSurface] 水位 y={0:F2}（进度 {1:P0}）", y, progress), this);
            }
        }

        /// <summary>由搭建脚本写入水位范围。</summary>
        public void Configure(float bottomY, float topY, Vector2 center)
        {
            m_BottomY = bottomY;
            m_TopY = topY;
            m_Center = center;
            Apply(0f);
        }
    }
}
