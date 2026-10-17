using UnityEngine;

namespace PAO
{
    /// <summary>
    /// 让物体始终正对摄像机 —— 给场景里那些"贴在空中的文字提示"用。
    ///
    /// 【为什么需要它】
    /// 三维场景里立一块平面文字，玩家从侧面看就变成一条线看不见了。
    /// 每帧转向摄像机就能一直可读。
    ///
    /// 【为什么不用 LookAt 直接对着相机】
    /// LookAt 会让物体的"正面"朝向相机，但文字的正面朝向要按它的本地轴来定；
    /// 这里保持物体的 up 方向不变（只绕 Y 轴转），文字才不会歪着或倒过来。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("PAO/通用/朝向摄像机（Billboard）")]
    public sealed class BillboardToCamera : MonoBehaviour
    {
        [Tooltip("只在水平方向旋转（推荐，文字不会歪）。关掉则完全面向相机")]
        [SerializeField] private bool m_YawOnly = true;

        [Tooltip("要让哪台相机看它。留空则用 Camera.main")]
        [SerializeField] private Camera m_Target;

        /// <summary>由代码创建时调用，用默认设置。</summary>
        public void Configure(bool yawOnly = true, Camera target = null)
        {
            m_YawOnly = yawOnly;
            m_Target = target;
        }

        private void LateUpdate()
        {
            Camera cam = m_Target != null ? m_Target : Camera.main;

            if (cam == null)
            {
                return;
            }

            Transform t = transform;
            Vector3 toCamera = cam.transform.position - t.position;

            if (m_YawOnly)
            {
                // 只在水平面转：文字保持直立，不会因为相机俯仰而歪掉
                toCamera.y = 0f;

                if (toCamera.sqrMagnitude < 1e-6f)
                {
                    return;   // 相机几乎在正上方，方向没意义
                }

                t.rotation = Quaternion.LookRotation(-toCamera.normalized, Vector3.up);
            }
            else
            {
                t.LookAt(cam.transform, Vector3.up);
                t.Rotate(0f, 180f, 0f);   // 让文字的正面朝向相机
            }
        }
    }
}
