using System.Collections.Generic;
using UnityEngine;

namespace PAO.BubbleFX
{
    /// <summary>
    /// 给「会发射出去」的泡泡上 PAO/BubbleBall 材质。
    ///
    /// 用法（在 BubbleLauncher.CreateBubble 里，拿到 bubble 之后）：
    ///
    ///     BubbleBallVisual.Apply(bubble, settings.color);
    ///
    /// 每个颜色一个材质实例并缓存复用，不会每次发射都新建材质造成泄漏。
    /// </summary>
    public static class BubbleBallVisual
    {
        public const string ShaderName = "PAO/BubbleBall";

        // 按颜色缓存：同色的泡泡共用一个材质实例
        private static readonly Dictionary<int, Material> s_Cache = new Dictionary<int, Material>();

        private static Shader s_Shader;

        private static Shader GetShader()
        {
            if (s_Shader == null)
                s_Shader = Shader.Find(ShaderName);
            return s_Shader;
        }

        /// <summary>
        /// 把泡泡球材质套到目标的 Renderer 上。
        /// 找不到 shader 时什么都不做（保留原有材质），并在 Console 提醒一次。
        /// </summary>
        /// <param name="target">泡泡物体（会找它的 Renderer）</param>
        /// <param name="color">泡泡颜色</param>
        public static void Apply(GameObject target, Color color)
        {
            if (target == null)
                return;

            Renderer r = target.GetComponent<Renderer>();
            if (r == null)
                return;

            Material mat = GetMaterial(color);
            if (mat != null)
                r.sharedMaterial = mat;
        }

        /// <summary>取（或创建）某个颜色对应的泡泡材质。</summary>
        public static Material GetMaterial(Color color)
        {
            Shader shader = GetShader();
            if (shader == null)
            {
                Debug.LogWarning("[BubbleBall] 找不到 Shader \"" + ShaderName +
                                 "\"。确认 Assets/GameMain/Art/Shaders/BubbleBall.shader 存在且能编译。");
                return null;
            }

            // 用颜色的 8 位量化值当键：近似色归为一个材质，避免实例过多
            Color32 c32 = color;
            int key = (c32.r << 16) | (c32.g << 8) | c32.b;

            Material cached;
            if (s_Cache.TryGetValue(key, out cached) && cached != null)
                return cached;

            var mat = new Material(shader);
            mat.name = "BubbleBall (Runtime)";
            mat.SetColor("_BaseColor", new Color(color.r, color.g, color.b, 1f));

            s_Cache[key] = mat;
            return mat;
        }

        /// <summary>退出播放时清掉缓存，免得编辑器里越攒越多。</summary>
        public static void ClearCache()
        {
            foreach (KeyValuePair<int, Material> kv in s_Cache)
            {
                if (kv.Value == null)
                    continue;

                if (Application.isPlaying)
                    Object.Destroy(kv.Value);
                else
                    Object.DestroyImmediate(kv.Value);
            }

            s_Cache.Clear();
        }
    }
}
