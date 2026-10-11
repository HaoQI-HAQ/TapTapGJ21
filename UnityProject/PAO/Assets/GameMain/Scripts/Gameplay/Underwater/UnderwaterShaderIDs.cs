using UnityEngine;

namespace PAO.Underwater
{
    /// <summary>
    /// 调试可视化模式。数值直接传给 shader 的 _PAO_DebugMode。
    ///
    /// 【为什么要做成一个显式的枚举，而不是一个 0~5 的 int】
    ///   排查时最怕的是「记错了哪个数字对应哪个模式」——
    ///   看到一张莫名其妙的图，然后怀疑自己看的是不是这一项。
    ///   名字写进 Inspector 的下拉框里，就不会记错。
    /// </summary>
    public enum UnderwaterDebugMode
    {
        /// <summary>正常渲染。</summary>
        Off = 0,

        /// <summary>焦散量。应该看到一张会动的亮网；全黑 = 图案没算出来。</summary>
        Caustics = 1,

        /// <summary>表面匹配度。白 = 这个像素还是几何表面；全黑 = 被透明物体盖住。</summary>
        SurfaceMatch = 2,

        /// <summary>视距（米 / 20）。应该是一层随距离变亮的灰。</summary>
        ViewDistance = 3,

        /// <summary>世界坐标的平铺格子。应该看到规则格子；糊掉 = 世界坐标重建错了。</summary>
        WorldGrid = 4,

        /// <summary>水面光柱强度（体积光那一趟的原始值）。应该看到水面上的亮网。</summary>
        GodRayBeam = 5,
    }

    /// <summary>
    /// C# 与 shader 之间**唯一**的名字契约。
    ///
    /// 【为什么单独开一个文件】
    ///   全局 shader 变量最容易出的错是「C# 写 _PAO_WaterLevel，shader 读
    ///   _PAO_WaterHeight」—— 编译器不会报错，运行时也不报错，只是效果不出来。
    ///   这类 bug 能查一整天。所以名字只写在这里一份，两边都用它。
    ///
    /// 【为什么全部带 _PAO_ 前缀】
    ///   这些都是**全局**变量，会和 URP 自己的、以及将来接入的任何插件抢名字。
    ///   URP 内部已经占用了大量 _Camera / _ZBuffer / _Blit 开头的名字，
    ///   撞上就是「莫名其妙渲染错」，而且极难定位。加前缀是最便宜的保险。
    ///
    /// 【与 shader 的对应关系】
    ///   shader 里（UnderwaterInclude.hlsl 顶部）有一份同样名字的声明。
    ///   **改任何一个名字，两边必须同时改。**
    /// </summary>
    public static class UnderwaterShaderIDs
    {
        // ---- 相机（每帧由 UnderwaterPass 送）----
        public static readonly int CameraInvVP = Shader.PropertyToID("_PAO_CameraInvVP");
        public static readonly int CameraBufferSize = Shader.PropertyToID("_PAO_CameraBufferSize");

        // ---- Pass 之间的中间结果 ----
        //
        // Pass 0（焦散）写 _PAO_CausticsTexture，Pass 1 和 Pass 2 读它。
        //
        // 后三趟的**输入画面**走 _PAO_ColorTexture，不走 _BlitTexture ——
        // 因为它们的输出目标是相机颜色本身，输入再指向它就变成
        // 「同一张 RT 既读又写」：UV 的 Y 翻转约定不成立，而且在部分平台上
        // 是未定义行为。更要紧的是 C# 那边的 Blitter.BlitCameraTexture
        // 在这种用法下会直接抛异常并每帧刷屏。见 UnderwaterPass 的说明。
        public static readonly int CausticsTexture = Shader.PropertyToID("_PAO_CausticsTexture");
        public static readonly int ColorTexture = Shader.PropertyToID("_PAO_ColorTexture");

        // ---- 水面 ----
        // xyzw = (水面高度 Y, 0, 0, 0)
        public static readonly int WaterPlane = Shader.PropertyToID("_PAO_WaterPlane");

        // rgb = 浅处水色, a = 1
        public static readonly int ShallowColor = Shader.PropertyToID("_PAO_ShallowColor");

        // rgb = 深处水色, a = 1
        public static readonly int DeepColor = Shader.PropertyToID("_PAO_DeepColor");

        // x = 水面之上的残留高度, y = 水面之下的淡入深度
        public static readonly int FadeRange = Shader.PropertyToID("_PAO_FadeRange");

        public static readonly int FogDensity = Shader.PropertyToID("_PAO_FogDensity");
        public static readonly int DepthDarkening = Shader.PropertyToID("_PAO_DepthDarkening");
        public static readonly int UnderwaterStrength = Shader.PropertyToID("_PAO_UnderwaterStrength");

        // ---- 焦散 ----
        public static readonly int CausticsEnabled = Shader.PropertyToID("_PAO_CausticsEnabled");
        public static readonly int CausticsIntensity = Shader.PropertyToID("_PAO_CausticsIntensity");
        public static readonly int CausticsScale = Shader.PropertyToID("_PAO_CausticsScale");
        public static readonly int CausticsSpeed = Shader.PropertyToID("_PAO_CausticsSpeed");
        public static readonly int CausticsColor = Shader.PropertyToID("_PAO_CausticsColor");
        public static readonly int CausticsWallBleed = Shader.PropertyToID("_PAO_CausticsWallBleed");
        public static readonly int CausticsFadeDistance = Shader.PropertyToID("_PAO_CausticsFadeDistance");

        // 焦散图案贴图（**可选**）。留空时 shader 自动退回程序化版本。
        //
        // _ST 的 xy = 贴图尺寸（像素），用来让 shader 判断「到底有没有图」。
        // 【为什么需要这个显式标记】空纹理在 HLSL 里也采得到值 ——
        // Unity 会给每个没绑定的纹理挂一张默认图（黑或灰），
        // 所以光看采样结果分不出「没图」和「图是黑的」。
        public static readonly int CausticsTex = Shader.PropertyToID("_PAO_CausticsTex");
        public static readonly int CausticsTexST = Shader.PropertyToID("_PAO_CausticsTex_ST");

        // ---- 调试（由 UnderwaterRenderFeature 上的 debugMode 推）----
        public static readonly int DebugMode = Shader.PropertyToID("_PAO_DebugMode");

        // ---- 体积光 ----
        public static readonly int GodRayEnabled = Shader.PropertyToID("_PAO_GodRayEnabled");
        public static readonly int SunDirection = Shader.PropertyToID("_PAO_SunDirection");
        public static readonly int GodRayIntensity = Shader.PropertyToID("_PAO_GodRayIntensity");
        public static readonly int GodRaySpread = Shader.PropertyToID("_PAO_GodRaySpread");
        public static readonly int GodRayDistance = Shader.PropertyToID("_PAO_GodRayDistance");
        public static readonly int GodRaySteps = Shader.PropertyToID("_PAO_GodRaySteps");
        public static readonly int GodRayNoiseScale = Shader.PropertyToID("_PAO_GodRayNoiseScale");
        public static readonly int GodRaySpeed = Shader.PropertyToID("_PAO_GodRaySpeed");

        // ---- 水面波光 ----
        //
        // 【这里为什么是空的】
        //   水面波光的频率 / 亮度 / 亮边走的是**水面材质自己的属性**，
        //   不走全局变量 —— 因为只有那一块网格用它们。
        //   水面高度、水色、焦散才是全局的，因为全屏 Pass 也要用同一份。
        //
        //   水面的初始值由编辑器工具烤进 WaterSurface.mat，见 WaterVolume.PushGlobals。

        /// <summary>把水面高度与两个水色打包推下去。三个变量总是一起用，所以合成一个入口。</summary>
        public static void SetWaterPlane(float level, Color shallow, Color deep)
        {
            Shader.SetGlobalVector(WaterPlane, new Vector4(level, 0f, 0f, 0f));
            Shader.SetGlobalColor(ShallowColor, shallow);
            Shader.SetGlobalColor(DeepColor, deep);
        }
    }
}
