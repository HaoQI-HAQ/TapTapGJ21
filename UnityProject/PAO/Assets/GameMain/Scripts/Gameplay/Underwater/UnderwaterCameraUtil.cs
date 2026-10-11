using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PAO.Underwater
{
    /// <summary>
    /// 水下效果共用的相机矩阵与全屏绘制工具。
    ///
    /// 【为什么必须统一在一处算矩阵】
    ///   「从屏幕 uv + 深度反推世界坐标」这件事，C# 和 shader 必须对同一套
    ///   矩阵达成一致。URP 的相机颜色目标在不同图形 API 上可能被翻转
    ///   （D3D 与 GL 的 Y 方向相反），一旦两边理解不一致，反推出来的
    ///   世界坐标会整体上下颠倒 —— 表现是「焦散出现在天花板上」，
    ///   不报错、不崩溃，只是效果看着不对劲，极难查。
    ///
    /// 【为什么把全屏绘制也放在这里】
    ///   「画一个全屏三角形」这件事四段都要做，而且必须做得**完全一样**：
    ///   渲染目标设对了但 viewport 没设对，就只画到画面一角。
    ///   集中一处就不会漂。
    ///
    /// =======================================================================
    /// 【关于 Blitter.BlitCameraTexture】
    ///
    ///   本文件里的 Draw() 替代了它的一半用途。分工是：
    ///
    ///     · 纯拷贝（源和目标必然是两张不同的纹理）→ 仍然用 Blitter，
    ///       那是它的标准场景，项目里泡泡 SDF / 泡泡潮也这么用，没有风险。
    ///     · 写回相机颜色 → 用 Draw()，不要用 Blitter。
    ///       在「输入也指向相机颜色」这种用法下 Blitter 会抛
    ///         「Assertion failed: Blitter.BlitCameraTexture」
    ///         「ArgumentNullException: Value cannot be null. Parameter name: value」
    ///       而且每帧刷屏几百条，看不出是哪一段出的问题。
    ///       这个坑实际踩过一遍，别改回去。
    /// =======================================================================
    /// </summary>
    internal static class UnderwaterCameraUtil
    {
        /// <summary>
        /// 与「相机颜色目标」严格对应的逆 View - Projection 矩阵。
        ///
        /// renderIntoTexture: false —— 因为 Unity 的全屏三角形 uv 辅助函数
        /// （GetFullScreenTriangleTexCoord）内部已经用 _ProjectionParams.x
        /// 处理过翻转了，这里再翻一次就会双重翻转，表现是焦散出现在天花板上。
        /// </summary>
        public static Matrix4x4 ComputeInvViewProjection(Camera camera)
        {
            Matrix4x4 proj = GL.GetGPUProjectionMatrix(camera.projectionMatrix, false);
            Matrix4x4 view = camera.worldToCameraMatrix;

            return (proj * view).inverse;
        }

        /// <summary>把逆 VP 和缓冲区尺寸推给所有水下 shader。</summary>
        public static void SetCameraMatrices(CommandBuffer cmd, in RenderingData renderingData)
        {
            if (cmd == null)
            {
                return;
            }

            Camera camera = renderingData.cameraData.camera;
            if (camera == null)
            {
                return;
            }

            cmd.SetGlobalMatrix(UnderwaterShaderIDs.CameraInvVP, ComputeInvViewProjection(camera));

            int width  = Mathf.Max(1, renderingData.cameraData.cameraTargetDescriptor.width);
            int height = Mathf.Max(1, renderingData.cameraData.cameraTargetDescriptor.height);

            cmd.SetGlobalVector(UnderwaterShaderIDs.CameraBufferSize,
                new Vector4(width, height, 1f / width, 1f / height));
        }

        /// <summary>
        /// 用 material 的第 pass 个 Pass，往 destination 画一个全屏三角形。
        ///
        /// 【为什么不在这里绑输入纹理】
        ///   每一段的输入不一样（有的读画面备份、有的读焦散量、有的都读），
        ///   绑定是调用方的事。这里只负责「渲染目标 + viewport + 画」。
        ///   绑错了名字不会有任何报错，只会读到上一帧的垃圾 —— 所以让调用方
        ///   在紧挨着调用的地方绑，一眼能看全。
        /// </summary>
        public static bool Draw(CommandBuffer cmd,
                                RTHandle destination,
                                Material material,
                                int pass,
                                string label)
        {
            if (cmd == null || material == null)
            {
                return false;
            }

            if (destination == null || destination.rt == null)
            {
                Debug.LogError("[水下] " + label + "：渲染目标为空。" +
                               "如果这是「相机颜色目标」，说明这一段注入的时机" +
                               "已经不在相机的渲染流程里 —— 检查 Renderer Feature 上" +
                               "的 causticsEvent / compositeEvent / godRayEvent。");
                return false;
            }

            // SetRenderTarget 同时把 viewport 设成目标 RT 的尺寸。
            // 少了这一步，半分辨率的目标只会画到四分之一块区域。
            CoreUtils.SetRenderTarget(cmd, destination);

            // 一个全屏三角形，3 个顶点，1 个实例。
            // shader 那边用 SV_VertexID + GetFullScreenTriangleVertexPosition 生成。
            cmd.DrawProcedural(Matrix4x4.identity, material, pass,
                               MeshTopology.Triangles, 3, 1);

            return true;
        }
    }
}
