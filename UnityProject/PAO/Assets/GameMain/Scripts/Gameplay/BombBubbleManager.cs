using System.Collections.Generic;
using UnityEngine;

namespace PAO
{
    /// <summary>
    /// 炸弹泡泡管理器：记录投放顺序，按 R 依次引爆。
    ///
    /// 规则：
    ///   每放出一个炸弹泡泡就排进队列，编号从 1 开始
    ///   按一次 R 引爆编号最小的那个，后面的编号自动补位
    ///   同时最多存在设定的数量，超了会先引爆最老的那个腾位置
    ///
    /// 挂在 Player 上（和 BubbleLauncher 同一个物体）。
    /// </summary>
    public class BombBubbleManager : MonoBehaviour
    {
        [Header("数量上限")]
        [Tooltip("同时最多存在几个炸弹泡泡。超了会先引爆最老的那个")]
        [SerializeField] private int m_MaxBombs = 4;

        [Header("引爆")]
        [Tooltip("引爆按键：0 = 鼠标左键，1 = 鼠标右键，2 = 鼠标中键")]
        [SerializeField] private int m_DetonateMouseButton = 1;

        [Tooltip("是否同时保留 R 键引爆（方便调试，不需要就取消勾选）")]
        [SerializeField] private bool m_AlsoUseRKey = true;

        [Header("编号提示")]
        [Tooltip("是否在炸弹泡泡上方显示编号")]
        [SerializeField] private bool m_ShowIndexLabel = true;

        [Tooltip("编号文字大小")]
        [SerializeField] private int m_LabelFontSize = 20;

        [Tooltip("编号显示在泡泡上方多高处（米）")]
        [SerializeField] private float m_LabelHeightOffset = 0.5f;

        // 按投放顺序保存，索引 0 就是编号 1
        private readonly List<Bubble> m_Bombs = new List<Bubble>();

        private Camera m_Camera;
        private GUIStyle m_LabelStyle;
        private GUIStyle m_LabelShadowStyle;

        /// <summary>当前还没引爆的炸弹数量。</summary>
        public int PendingCount
        {
            get { return m_Bombs.Count; }
        }

        private void Awake()
        {
            m_Camera = Camera.main;
        }

        private void Update()
        {
            CleanupDestroyed();

            if (Input.GetMouseButtonDown(m_DetonateMouseButton)
                || (m_AlsoUseRKey && Input.GetKeyDown(KeyCode.R)))
            {
                DetonateOldest();
            }
        }

        /// <summary>
        /// 发射器每放出一个炸弹泡泡就调它登记，编号会自动分配。
        /// </summary>
        public void RegisterBomb(Bubble bomb)
        {
            if (bomb == null)
            {
                return;
            }

            CleanupDestroyed();

            // 满了就先引爆最老的，给新的腾位置
            int limit = Mathf.Max(1, m_MaxBombs);
            while (m_Bombs.Count >= limit)
            {
                if (!DetonateOldest())
                {
                    break;
                }
            }

            m_Bombs.Add(bomb);
            RefreshIndices();
        }

        /// <summary>
        /// 引爆编号最小的那个（队列最前面的）。成功返回 true。
        /// </summary>
        public bool DetonateOldest()
        {
            CleanupDestroyed();

            if (m_Bombs.Count == 0)
            {
                return false;
            }

            Bubble oldest = m_Bombs[0];
            m_Bombs.RemoveAt(0);

            if (oldest != null)
            {
            // 这颗炸弹如果已经被浮粘/弹力泡泡吸收了，就改引爆宿主。
            // 直接炸它自己的话走的是普通爆炸，范围不会放大；
            // 而且它现在正待在宿主体内，炸了也带不走宿主。
            if (oldest.IsAbsorbed && oldest.HostBubble != null)
            {
                oldest.HostBubble.DetonateAbsorbedBomb();
            }
            else
            {
                oldest.Explode();
            }
            }

            RefreshIndices();
            return true;
        }

        /// <summary>
        /// 清掉已经被打破或因寿命消失的炸弹，别让队列里留着空引用，
        /// 否则按 R 会「引爆」一个不存在的东西，白按一次。
        /// </summary>
        private void CleanupDestroyed()
        {
            bool changed = false;

            for (int i = m_Bombs.Count - 1; i >= 0; i--)
            {
                if (m_Bombs[i] == null)
                {
                    m_Bombs.RemoveAt(i);
                    changed = true;
                }
            }

            if (changed)
            {
                RefreshIndices();
            }
        }

        /// <summary>重排编号，保证 UI 上始终是连续的 1 2 3 4。</summary>
        private void RefreshIndices()
        {
            for (int i = 0; i < m_Bombs.Count; i++)
            {
                if (m_Bombs[i] != null)
                {
                    m_Bombs[i].BombIndex = i + 1;
                }
            }
        }

        /// <summary>
        /// 在每个待引爆的炸弹上方画出它的编号。
        /// </summary>
        private void OnGUI()
        {
            if (!m_ShowIndexLabel || m_Bombs.Count == 0)
            {
                return;
            }

            Camera camera = m_Camera != null ? m_Camera : (m_Camera = Camera.main);
            if (camera == null)
            {
                return;
            }

            if (m_LabelStyle == null)
            {
                m_LabelStyle = new GUIStyle(GUI.skin.label);
                m_LabelStyle.fontSize = m_LabelFontSize;
                m_LabelStyle.fontStyle = FontStyle.Bold;
                m_LabelStyle.alignment = TextAnchor.MiddleCenter;
                m_LabelStyle.normal.textColor = new Color(1f, 0.75f, 0.35f);

                m_LabelShadowStyle = new GUIStyle(m_LabelStyle);
                m_LabelShadowStyle.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
            }

            for (int i = 0; i < m_Bombs.Count; i++)
            {
                Bubble bomb = m_Bombs[i];
                if (bomb == null)
                {
                    continue;
                }

                Vector3 worldPoint = bomb.transform.position + Vector3.up * m_LabelHeightOffset;
                Vector3 screenPoint = camera.WorldToScreenPoint(worldPoint);

                // 在摄像机背后就不画
                if (screenPoint.z <= 0f)
                {
                    continue;
                }

                string text = bomb.BombIndex.ToString();
                Vector2 size = m_LabelStyle.CalcSize(new GUIContent(text));

                Rect rect = new Rect(
                    screenPoint.x - size.x * 0.5f,
                    Screen.height - screenPoint.y - size.y * 0.5f,
                    size.x,
                    size.y);

                GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), text, m_LabelShadowStyle);
                GUI.Label(rect, text, m_LabelStyle);
            }
        }
    }
}
