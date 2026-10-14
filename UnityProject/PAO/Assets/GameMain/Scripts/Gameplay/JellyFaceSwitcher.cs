// ---------------------------------------------------------------------------
// PAO / JellyFaceSwitcher —— 水母表情切换（方案2：材质槽换材质）
//
// 原理：
//   水母头身网格上多了「脸部面片」这个独立子物体（jellyfish_face_patch），
//   它自带一套 UV（UVFace）和一张独立的表情贴图槽 _ExpressionMap。
//   切表情 = 把这个面片上的材质换成另一个表情材质。身体、骨骼、动画都不受影响。
//
// 用法：
//   1. 菜单 PAO/水母表情/生成 8 个表情材质
//   2. 在水母根对象挂上本组件
//   3. 菜单 PAO/水母表情/把表情材质装到场景里的 Switcher 上（或手动把 8 个材质拖进 Expressions）
//   4. 运行时调 SetExpression(JellyFace.Dead) / SetExpression(2) / NextExpression()
//
// 说明：
//   * 表情材质必须用 PAO/ModelPBRExpression（生成器已经保证）。
//   * 换材质会触发一次 renderer 重建，一帧的抖动；表情切换频率低可以忽略。
//     真要完全零重建可以改成"8 个材质预热 + 只 SetTexture"，但那样每个实例
//     都会产生材质副本，反而更费内存 —— 这里按方案2的原始设计走。
// ---------------------------------------------------------------------------
using System.Collections.Generic;
using UnityEngine;

namespace PAO
{
    /// <summary>表情枚举。顺序与 T_JellyFace_01..08 对应，新增表情请往后加。</summary>
    public enum JellyFace
    {
        Normal = 0,   // 原始表情
        Cute = 1,     // 萌
        Smug = 2,     // 得意
        Relaxed = 3,  // 悠闲
        Drool = 4,    // 流口水
        Cry = 5,      // 老人流泪
        Dead = 6,     // 死
        Sweat = 7,    // 流汗
        Count = 8,
    }

    [DisallowMultipleComponent]
    public class JellyFaceSwitcher : MonoBehaviour
    {
        [Header("表情材质（顺序 = JellyFace 枚举顺序）")]
        [Tooltip("由菜单 PAO/水母表情/把表情材质装到场景里的 Switcher 上 自动填充")]
        public Material[] expressions = new Material[(int)JellyFace.Count];

        [Header("目标")]
        [Tooltip("留空则自动在子物体里找带 PAO/ModelPBRExpression 材质的 Renderer")]
        public Renderer faceRenderer;

        [Tooltip("这个 Renderer 上哪一号材质槽是脸。自动查找时会被填上")]
        public int faceMaterialIndex = 0;

        [Header("启动行为")]
        public JellyFace startExpression = JellyFace.Normal;
        public bool applyOnStart = true;

        /// <summary>当前表情。</summary>
        public JellyFace Current { get; private set; } = JellyFace.Normal;

        private Material[] _runtimeCache;   // 避免每次切换都重新 GetComponents

        private void Awake()
        {
            if (faceRenderer == null) AutoFindRenderer();
            if (applyOnStart) SetExpression(startExpression, true);
        }

        private void Start()
        {
            // Awake 里没找到（比如对象是运行时实例化的）再补一次
            if (faceRenderer == null) AutoFindRenderer();
        }

        private void AutoFindRenderer()
        {
            var renderers = GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m != null && m.shader != null && m.shader.name == "PAO/ModelPBRExpression")
                    {
                        faceRenderer = r;
                        faceMaterialIndex = i;
                        return;
                    }
                }
            }
            if (renderers.Length > 0)
                Debug.LogWarning($"[JellyFace] 在 {name} 下没找到用 PAO/ModelPBRExpression 的材质，" +
                                 "请手动指定 faceRenderer。", this);
        }

        /// <summary>按枚举切表情。</summary>
        public void SetExpression(JellyFace face, bool force = false)
        {
            if (!force && face == Current) return;
            int idx = (int)face;
            if (expressions == null || idx < 0 || idx >= expressions.Length || expressions[idx] == null)
            {
                Debug.LogWarning($"[JellyFace] 表情 {face}（索引 {idx}）没有对应材质，已忽略。", this);
                return;
            }
            if (faceRenderer == null) AutoFindRenderer();
            if (faceRenderer == null) return;

            var mats = _runtimeCache;
            if (mats == null || mats.Length == 0)
            {
                mats = faceRenderer.materials;      // 实例化一份，避免改到共享资产
                _runtimeCache = mats;
            }
            if (faceMaterialIndex < 0 || faceMaterialIndex >= mats.Length)
            {
                Debug.LogWarning($"[JellyFace] faceMaterialIndex={faceMaterialIndex} 越界" +
                                 $"（共 {mats.Length} 个槽）。", this);
                return;
            }

            mats[faceMaterialIndex] = expressions[idx];
            faceRenderer.materials = mats;          // 写回，Unity 才会重建材质列表
            Current = face;
        }

        /// <summary>按索引切（列表顺序）。</summary>
        public void SetExpression(int index)
        {
            if (expressions == null || index < 0 || index >= expressions.Length) return;
            SetExpression((JellyFace)index);
        }

        /// <summary>按材质名后缀切，例如 "02_Cute" / "T_JellyFace_02_Cute"。</summary>
        public bool SetExpressionByName(string nameOrSuffix)
        {
            if (string.IsNullOrEmpty(nameOrSuffix) || expressions == null) return false;
            for (int i = 0; i < expressions.Length; i++)
            {
                var m = expressions[i];
                if (m == null) continue;
                if (m.name == nameOrSuffix || m.name.EndsWith(nameOrSuffix))
                {
                    SetExpression((JellyFace)i);
                    return true;
                }
            }
            return false;
        }

        /// <summary>循环切下一个。</summary>
        public void NextExpression()
        {
            int n = expressions != null ? expressions.Length : 0;
            if (n <= 0) return;
            int next = ((int)Current + 1) % n;
            SetExpression((JellyFace)next);
        }

        /// <summary>回默认表情。</summary>
        public void ResetExpression() => SetExpression(JellyFace.Normal);

        /// <summary>给其它系统用的只读列表（例如按名字找索引）。</summary>
        public IReadOnlyList<string> ExpressionNames
        {
            get
            {
                var list = new List<string>(expressions != null ? expressions.Length : 0);
                if (expressions != null)
                    foreach (var m in expressions)
                        list.Add(m != null ? m.name : "(空)");
                return list;
            }
        }
    }
}
