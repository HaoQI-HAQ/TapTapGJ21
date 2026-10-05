using System;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;

namespace PAO.BubbleFX
{
    /// <summary>
    /// 传给 GPU 的「一颗泡泡」的数据。布局与 BubbleSDF.shader 里的
    /// HLSL struct ShapeData 逐字段对应，共 **80 字节 = 20 个 float**。
    ///
    /// ── 必须是 struct，不能是 class ──
    /// ComputeBuffer.SetData 只接受 blittable 的值类型数组。用 class 会抛
    /// 「Array passed to ComputeBuffer.SetData(array) must be blittable.
    ///   BubblePayload is not blittable because it is not of value type」。
    ///
    /// ── 布局：只用 float4 和散装 float，绝不用 float3/Vector3 ──
    /// float 是 4 字节自对齐，结构里**没有任何字段之间会产生填充**，
    /// 所以 20 × 4 = 80 字节是确定的，托管侧与 HLSL 必然一致。
    /// 一旦混入 Vector3 / float3（12 字节），后面跟的字段就要按 4 字节对齐补位，
    /// 托管与 HLSL 的补齐规则还可能不一致 —— 之前踩过这个坑。
    ///
    /// ── 字段数为什么是 20 不是 18 ──
    /// 数的时候容易漏，特意写清楚每组的个数：
    ///   位置组 4 个（x, y, z, shapeType）
    ///   尺寸组 4 个（x, y, z, operation）  <-- 这组是 4 个，不是 3 个
    ///   颜色组 4 个（r, g, b, a）
    ///   custom 组 4 个（shellRatio, fuseStrength, distortion, distortionFreq）
    ///   预留组 4 个（blendStrength, numChildren, phase, pad0）
    ///   合计 20 个 float = 80 字节
    /// 而且字段顺序是按 HLSL 结构排的，不是按名字字母序 —— 别用 IDE 的排序功能。
    ///
    /// ── 字段名不用 m_ 前缀 ──
    /// struct 一旦被 Unity 序列化，m_XXX 这类名字容易和 MonoBehaviour 基类的
    /// 字段撞（会报 "The same field name is serialized multiple times"）。
    /// 本结构也不加 [System.Serializable]，它是纯 GPU 裸数据，
    /// 可调参数都在 SDFBubble 的 Inspector 字段上。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct BubblePayload
    {
        /// <summary>ComputeBuffer 的 stride，必须与 HLSL struct ShapeData 一致。</summary>
        public const int Stride = 80;

        /// <summary>本结构应有的字段个数，用于 <see cref="ValidateLayout"/> 自检。</summary>
        public const int FieldCount = 20;

        /// <summary>一份全零 / 默认值的 payload。</summary>
        public static BubblePayload Empty
        {
            get { return new BubblePayload(); }
        }

        /// <summary>
        /// 自检：字段个数对不对、托管侧大小对不对。
        ///
        /// 建 ComputeBuffer 之前调用，不对就在这里报人话，
        /// 而不是等 <c>SetData</c> 抛一个看不懂的 stride 异常。
        ///
        /// 两重检查，因为两者能抓到不同的问题：
        ///   1. 字段个数 —— 抓「加了/删了字段」，这条最可靠
        ///   2. 托管侧大小 —— 抓「字段类型换成了非 float」
        ///      （注意 Mono 的 Marshal.SizeOf 对混合大小字段可能给出误导值，
        ///       但对纯 float 结构是准的；本结构就是纯 float）
        /// </summary>
        public static bool ValidateLayout(out int sizeBytes, out string error)
        {
            FieldInfo[] fields = typeof(BubblePayload).GetFields(BindingFlags.Public | BindingFlags.Instance);

            if (fields.Length != FieldCount)
            {
                sizeBytes = 0;
                error = "BubblePayload 有 " + fields.Length + " 个字段，但应该是 " + FieldCount + " 个。\n" +
                        "改字段时请同步：\n" +
                        "  1. HLSL 里的 struct ShapeData\n" +
                        "  2. 本文件的 Stride 常量\n" +
                        "  3. 本文件的 FieldCount 常量\n" +
                        "  4. SDFBubble.Refresh() 里的赋值";
                return false;
            }

            sizeBytes = Marshal.SizeOf(typeof(BubblePayload));

            if (sizeBytes != Stride)
            {
                error = "BubblePayload 托管侧大小是 " + sizeBytes + " 字节，但 Stride 声明为 " +
                        Stride + " 字节，与 HLSL struct ShapeData 对不上。\n" +
                        "通常是往结构里加了 Vector3 / float3 / bool / int 之类的字段。\n" +
                        "请改回「纯 float」的布局，或同步调整 HLSL 那边。";
                return false;
            }

            error = null;
            return true;
        }

        // ---- 偏移 0：世界坐标球心 + 形状类型（4 个）----
        public float positionX;
        public float positionY;
        public float positionZ;
        public float shapeType;     // 1 = 球

        // ---- 偏移 16：世界尺寸（直径）+ 组合方式（4 个）----
        public float sizeX;
        public float sizeY;
        public float sizeZ;
        public float operation;     // 预留

        // ---- 偏移 32：颜色 rgb + 不透明度（4 个）----
        public float colorR;
        public float colorG;
        public float colorB;
        public float colorA;

        // ---- 偏移 48：壳厚 / 融合 / 抖动幅度 / 抖动频率（4 个）----
        public float shellRatio;
        public float fuseStrength;
        public float distortion;
        public float distortionFreq;

        // ---- 偏移 64：预留 + 相位（4 个）----
        public float blendStrength; // 预留
        public float numChildren;   // 预留
        public float phase;         // 每颗泡泡的相位（错开彩虹的起始色）
        public float pad0;
    }
}
