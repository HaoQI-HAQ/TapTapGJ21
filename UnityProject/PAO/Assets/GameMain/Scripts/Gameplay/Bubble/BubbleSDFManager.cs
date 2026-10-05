using System.Collections.Generic;
using UnityEngine;

namespace PAO.BubbleFX
{
    /// <summary>
    /// 泡泡 SDF 场景注册表。
    ///
    /// 所有 SDFBubble 在 OnEnable 时注册、OnDisable 时注销。
    /// 每帧渲染前调用 CollectVisible 拿到「视野内」的泡泡数据，
    /// 直接写进一块复用的数组里 —— 视野外的泡泡会被剔除，
    /// 这既省 GPU 里那个 O(N) 循环，也省掉每帧 GC。
    ///
    /// 约定：CollectVisible 会在渲染线程上被调用，
    /// 而 SDFBubble.Update 在主线程的帧循环里，
    /// 所以调用时本帧数据一定已经刷新过。
    /// </summary>
    public static class BubbleSDFManager
    {
        private static readonly List<SDFBubble> s_Bubbles        = new List<SDFBubble>(64);
        private static readonly List<SDFBubble> s_VisibleScratch = new List<SDFBubble>(64);

        private static BubblePayload[] s_Buffer = new BubblePayload[64];
        private static int               s_BufferCount;

        /// <summary>当前登记在册的泡泡数（含视野外的）。</summary>
        public static int RegisteredCount
        {
            get { return s_Bubbles.Count; }
        }

        /// <summary>上次收集到的可见泡泡数 —— 也就是 GPU 要遍历的形状数。</summary>
        public static int VisibleCount
        {
            get { return s_BufferCount; }
        }

        /// <summary>可见泡泡的数据数组，有效长度为 VisibleCount。</summary>
        public static BubblePayload[] VisibleData
        {
            get { return s_Buffer; }
        }

        internal static void Register(SDFBubble bubble)
        {
            if (bubble == null || s_Bubbles.Contains(bubble))
                return;

            s_Bubbles.Add(bubble);
        }

        internal static void Unregister(SDFBubble bubble)
        {
            if (bubble == null)
                return;

            s_Bubbles.Remove(bubble);
        }

        /// <summary>
        /// 收集相机视野内的泡泡。返回的数组在下一帧会被覆盖，不要长期持有。
        /// </summary>
        /// <param name="camera">用于剔除的相机；传 null 表示不剔除，全部提交。</param>
        public static BubblePayload[] CollectVisible(Camera camera)
        {
            s_BufferCount = 0;

            if (s_Bubbles.Count == 0)
                return s_Buffer;

            s_VisibleScratch.Clear();

            if (camera == null)
            {
                for (int i = 0; i < s_Bubbles.Count; i++)
                {
                    SDFBubble b = s_Bubbles[i];
                    if (b != null && b.isActiveAndEnabled)
                        s_VisibleScratch.Add(b);
                }
            }
            else
            {
                // 每帧只算一次视锥平面，别丢进循环里
                Plane[] planes = GeometryUtility.CalculateFrustumPlanes(camera);

                for (int i = 0; i < s_Bubbles.Count; i++)
                {
                    SDFBubble b = s_Bubbles[i];
                    if (b == null || !b.isActiveAndEnabled)
                        continue;

                    if (GeometryUtility.TestPlanesAABB(planes, b.WorldBounds))
                        s_VisibleScratch.Add(b);
                }
            }

            int count = s_VisibleScratch.Count;
            if (count == 0)
                return s_Buffer;

            if (s_Buffer.Length < count)
            {
                // 扩容留一倍余量，避免泡泡逐个生成时每帧都重新分配
                int newSize = Mathf.NextPowerOfTwo(Mathf.Max(count, 64));
                s_Buffer = new BubblePayload[newSize];
            }

            // BubblePayload 是值类型，整块赋值即可 ——
            // 也正因为是值类型，这块数组才能直接喂给 ComputeBuffer.SetData。
            for (int i = 0; i < count; i++)
                s_Buffer[i] = s_VisibleScratch[i].Payload;

            s_BufferCount = count;
            return s_Buffer;
        }
    }
}
