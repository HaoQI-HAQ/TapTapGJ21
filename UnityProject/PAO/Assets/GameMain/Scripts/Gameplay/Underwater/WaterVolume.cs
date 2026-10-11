using UnityEngine;

namespace PAO.Underwater
{
    /// <summary>
    /// 水下效果的**唯一数据源**。场景里只放这一个。
    ///
    /// 【它做什么】
    ///   把「水面在哪、水多深、水什么颜色、焦散多亮、光柱多强」这些参数
    ///   每帧推成全局 shader 变量，让下面三处都能读到同一份数据：
    ///
    ///     · WaterSurface.shader     —— 抬头看见的水面波光
    ///     · UnderwaterFullscreen    —— 焦散投影 + 深度雾 + 体积光
    ///     · 将来任何水下 VFX / 粒子
    ///
    /// 【为什么用全局变量，而不是给每个材质 SetXXX】
    ///   和泡泡潮那套是同一个理由：渲染数据归渲染，玩法数据归玩法。
    ///   材质是 URP Renderer Feature 在编辑器里创建的，场景拿不到它的引用；
    ///   全局变量是这两边唯一干净的通信方式。
    ///   —— 代价是「全局变量会跨场景残留」，所以这里每帧无条件重推一遍，
    ///      不做任何「值没变就跳过」的优化。谁也别想省这个。
    ///
    /// 【性能】
    ///   每帧 20 来次 SetGlobalXxx，纯 CPU 侧，约等于零。
    ///   真正花钱的是它喂给 GPU 的那些 Pass，见每个参数的说明。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("PAO/水下/Water Volume")]
    public sealed class WaterVolume : MonoBehaviour
    {
        // ==================================================================
        // 水面
        // ==================================================================

        [Header("水面")]
        [Tooltip("水面高度（世界坐标 Y，米）。\n" +
                 "这是整套效果的原点 —— 焦散、雾、光柱全从它算起，填错整个画面都会歪。\n" +
                 "用「工具 → PAO → 水下 → 一键搭建水下」会自动按场景包围盒估一个初值。")]
        public float waterLevel = 10f;

        [Tooltip("水面网格的半边长（米）。\n" +
                 "水面是铺在 waterLevel 上的一整块平面，必须比房间大一圈，\n" +
                 "否则玩家抬头会看到水面的边缘，那一瞬间就穿帮了。\n" +
                 "一键搭建会按场景包围盒 ×2.5 自动算。\n" +
                 "【运行时改这个值不会改变画面】—— 水面网格是烘焙好的资产，\n" +
                 "要改大小请直接拖场景里 WaterSurface 子物体的 Scale。")]
        public float waterSize = 60f;

        [Tooltip("水面细分段数。波光本身在片元里算，顶点只负责让平面能跟着相机走；\n" +
                 "所以 32 段足够，调到 256 只会白白多几千个顶点。")]
        [Range(8, 256)] public int waterSegments = 48;

        // ==================================================================
        // 水下滤镜
        // ==================================================================

        [Header("水下滤镜")]
        [Tooltip("水下总体强度 0~1。这是整个效果的**总旋钮** ——\n" +
                 "做演示时想强调一下就拉满，平时玩建议压在 0.5~0.75，\n" +
                 "「水中的效果可以弱一点点」靠的就是它。")]
        [Range(0f, 1f)] public float underwaterStrength = 0.65f;

        [Tooltip("水面往下多少米开始算「完全在水下」（米）。\n" +
                 "比这个高度更靠近水面的地方会平滑过渡回无水状态 —— \n" +
                 "玩家从水里冒出水面时，靠这一段做出干净的过渡，而不是「啪」地一切。")]
        public float fadeInDepth = 1.2f;

        [Tooltip("水面往上多少米仍然有一点水下残留（米），用来做「刚破水」的湿镜头感。\n" +
                 "填 0 = 一过水面立刻干净。")]
        public float fadeOutAbove = 0.4f;

        [Tooltip("浅处的水色（贴着水面看）。偏亮的青绿。")]
        public Color shallowColor = new Color(0.42f, 0.78f, 0.86f, 1f);

        [Tooltip("深处的水色。越深越暗、越偏靛蓝。")]
        public Color deepColor = new Color(0.06f, 0.28f, 0.42f, 1f);

        [Tooltip("雾的浓度（每米衰减系数）。\n" +
                 "【性能无关，观感强相关】这个数直接决定「能看多远」。\n" +
                 "0.03 → 约 60 米看穿；0.08 → 约 25 米；0.15 → 约 12 米。\n" +
                 "水下游戏一般别低于 0.05，否则远处一览无余，反而没有水下感。")]
        [Range(0.001f, 0.5f)] public float fogDensity = 0.055f;

        [Tooltip("「越深越暗」的额外压暗系数。0 = 只有距离雾，10 = 深处几乎全黑。\n" +
                 "只在深度跨度大的关卡调高，小房间里保持 1 左右就够。")]
        [Range(0f, 10f)] public float depthDarkening = 1.5f;

        // ==================================================================
        // 焦散
        // ==================================================================

        [Header("焦散（地面光斑）")]
        [Tooltip("焦散总开关。关掉后全屏 Pass 里这段直接被分支跳过，省一点 ALU。")]
        public bool causticsEnabled = true;

        [Tooltip("焦散强度。参考图里地面上那种游动的亮网 —— \n" +
                 "它最忌讳「太亮」，一超过 1 整个地面就会糊成白斑，失去「水」的感觉。\n" +
                 "0.8 ~ 1.6 是比较像的区间。")]
        [Range(0f, 4f)] public float causticsIntensity = 1.15f;

        [Tooltip("焦散的世界尺寸（米）。数越小，亮纹越密。\n" +
                 "参考图里一组亮纹大概 1~2 米宽，所以 3 左右合适。")]
        [Range(0.5f, 20f)] public float causticsScale = 3.2f;

        [Tooltip("焦散游动速度。水面晃动越快，光斑跳得越凶。")]
        [Range(0f, 3f)] public float causticsSpeed = 0.55f;

        [Tooltip("焦散颜色。和 shallowColor 分开，是因为焦散经常想偏白一点、\n" +
                 "甚至过曝到让 Bloom 咬一口，做出「光斑在发亮」的感觉。")]
        public Color causticsColor = new Color(0.85f, 1f, 0.98f, 1f);

        [Tooltip("焦散往垂直表面上的溢出强度。\n" +
                 "物理上焦散只该出现在朝上的面，但真实水下录像里墙面也有一点点 —— \n" +
                 "这个值就是那个「不物理但好看」的量。0 = 严格只打地板。")]
        [Range(0f, 1f)] public float causticsWallBleed = 0.25f;

        [Tooltip("焦散在多少米外开始衰减（米）。防止远处地板上的亮纹糊成一片噪点。\n" +
                 "0 = 不衰减。")]
        public float causticsFadeDistance = 60f;

        [Tooltip("【可选】焦散图案贴图。\n" +
                 "\n" +
                 "留空 = 用 shader 里程序化生成的细胞噪声（天生无缝，够用）。\n" +
                 "放一张图 = 自动切换成「两层滚动 + 呼吸」的贴图版本。\n" +
                 "\n" +
                 "要求：\n" +
                 "  · 灰度图。亮的地方是光斑，暗的地方是阴影\n" +
                 "  · **必须无缝平铺** —— 这是唯一的硬要求\n" +
                 "  · 512×512 就够。它会在世界里反复平铺，做大了纯浪费显存\n" +
                 "  · 导入设置：sRGB 关掉（它是图案不是颜色），Wrap Mode 设 Repeat\n" +
                 "\n" +
                 "参考图里那种有粗有细、有疏有密的网状是程序化写不出来的 ——\n" +
                 "这一点确实需要美术的判断。")]
        public Texture2D causticsTexture;

        // ==================================================================
        // 体积光
        // ==================================================================

        [Header("体积光（斜着照下来的光柱）")]
        [Tooltip("体积光总开关。这是整套里最贵的一项，改成移动端时第一个关它。")]
        public bool godRaysEnabled = true;

        [Tooltip("光柱方向。指向太阳（光从哪来就填哪边）。\n" +
                 "参考图里的光柱是接近垂直、略微倾斜的。")]
        public Vector3 sunDirection = new Vector3(-0.25f, 1f, -0.35f);

        [Tooltip("光柱强度。这个数会乘进 HDR，超过 1 就会触发 Bloom，\n" +
                 "光柱边缘那圈柔光是 Bloom 白送的。")]
        [Range(0f, 8f)] public float godRayIntensity = 2.2f;

        [Tooltip("光柱的角度扩散（0~1）。越大，光柱越发散、越像「整片天光洒下来」；\n" +
                 "越小越像几道硬边光柱。0.5 左右比较像参考图。")]
        [Range(0f, 1f)] public float godRaySpread = 0.5f;

        [Tooltip("光线步进的最大距离（米）。水越浑，这个值应该越小 —— \n" +
                 "反正走再远也看不见，白算。")]
        [Range(4f, 200f)] public float godRayDistance = 45f;

        [Tooltip("每道光线走多少步。**性能的主要旋钮**。\n" +
                 "12 步配 1/2 分辨率在 PC 上几乎免费；出现层状 banding 就往上加，\n" +
                 "出现噪点就往下减（噪点是步数太少 + 噪声太强）。")]
        [Range(4, 64)] public int godRaySteps = 14;

        [Tooltip("光柱噪波的世界尺寸（米）。控制光柱里那些「一缕一缕」的粗细。")]
        [Range(0.5f, 30f)] public float godRayNoiseScale = 6f;

        [Tooltip("光柱闪烁速度。水下光柱是活的，完全静止会像贴图。")]
        [Range(0f, 3f)] public float godRaySpeed = 0.25f;

        // ==================================================================
        // 水面波光
        // ==================================================================

        [Header("水面波光（从下方往上看）")]
        [Tooltip("水面亮度。参考图里水面是整张画面最亮的地方 —— \n" +
                 "这个值超过 1 就会被 Bloom 咬一口，形成那圈柔和的光晕。")]
        [Range(0f, 8f)] public float surfaceBrightness = 2.4f;

        [Tooltip("水面的涟漪频率。")]
        [Range(0.5f, 60f)] public float surfaceRippleScale = 14f;

        [Tooltip("水面涟漪的游动速度。")]
        [Range(0f, 4f)] public float surfaceRippleSpeed = 0.8f;

        [Tooltip("水面在掠射角处的亮边强度（从正下方看水面时，边缘那一圈最亮）。")]
        [Range(0f, 4f)] public float surfaceRimIntensity = 1.4f;

        [Tooltip("水面颜色。")]
        public Color surfaceColor = new Color(0.72f, 0.95f, 1f, 1f);

        // ==================================================================
        // 推给 shader
        // ==================================================================

        // 【注意】这些名字在 UnderwaterShaderIDs 里统一定义，C# 和 shader 两边
        // 都从那一份来。改名字只改那一处，不要在这里写字面量。

        private void OnEnable()
        {
            PushGlobals();
        }

        private void Update()
        {
            // Update 而不是 LateUpdate：渲染发生在 LateUpdate 之后，
            // 两种都赶得上；用 Update 是为了让 Scene 视图里拖参数时的反馈更跟手。
            PushGlobals();
        }

        private void OnValidate()
        {
            // 编辑器里拖 Inspector 时立刻生效，不用等下一帧
            PushGlobals();
        }

        /// <summary>
        /// 把当前所有参数推成全局 shader 变量。
        /// 公开出来是为了让编辑器工具能在不进 Play 的时候手动推一次。
        /// </summary>
        public void PushGlobals()
        {
            // ---- 水面 ----
            UnderwaterShaderIDs.SetWaterPlane(
                waterLevel,
                new Color(shallowColor.r, shallowColor.g, shallowColor.b, 1f),
                new Color(deepColor.r, deepColor.g, deepColor.b, 1f));

            Shader.SetGlobalFloat(UnderwaterShaderIDs.FogDensity, fogDensity);
            Shader.SetGlobalFloat(UnderwaterShaderIDs.DepthDarkening, depthDarkening);

            float strength = Mathf.Clamp01(underwaterStrength);
            Shader.SetGlobalFloat(UnderwaterShaderIDs.UnderwaterStrength, strength);

            // 淡入区间：相机在 waterLevel + fadeOutAbove 之上完全无水，
            // 在 waterLevel - fadeInDepth 之下完全在水下。
            Shader.SetGlobalVector(UnderwaterShaderIDs.FadeRange,
                new Vector4(Mathf.Max(0f, fadeOutAbove), Mathf.Max(0.01f, fadeInDepth), 0f, 0f));

            // ---- 焦散 ----
            Shader.SetGlobalFloat(UnderwaterShaderIDs.CausticsEnabled,
                (causticsEnabled && strength > 0.001f) ? 1f : 0f);
            Shader.SetGlobalFloat(UnderwaterShaderIDs.CausticsIntensity, causticsIntensity);
            Shader.SetGlobalFloat(UnderwaterShaderIDs.CausticsScale, Mathf.Max(0.01f, causticsScale));
            Shader.SetGlobalFloat(UnderwaterShaderIDs.CausticsSpeed, causticsSpeed);
            Shader.SetGlobalFloat(UnderwaterShaderIDs.CausticsWallBleed, Mathf.Clamp01(causticsWallBleed));
            Shader.SetGlobalFloat(UnderwaterShaderIDs.CausticsFadeDistance,
                causticsFadeDistance > 0f ? causticsFadeDistance : 1e9f);
            Shader.SetGlobalColor(UnderwaterShaderIDs.CausticsColor,
                new Color(causticsColor.r, causticsColor.g, causticsColor.b, 1f));

            // 焦散图案贴图。没指定时推一张全零的 _ST，
            // shader 靠它判断「到底有没有图」，从而退回程序化版本。
            //
            // 【为什么不用「Shader.GetGlobalTexture 是不是 null」来判断】
            //   全局纹理一旦被设过就不会变回 null（哪怕物体销毁了，
            //   它也只是变成 Unity 的假 null）。用 _ST 这个显式标记最可靠，
            //   而且切图/清空都能立刻反映出来。
            if (causticsTexture != null)
            {
                Shader.SetGlobalTexture(UnderwaterShaderIDs.CausticsTex, causticsTexture);
                Shader.SetGlobalVector(UnderwaterShaderIDs.CausticsTexST,
                    new Vector4(causticsTexture.width, causticsTexture.height, 0f, 0f));
            }
            else
            {
                Shader.SetGlobalVector(UnderwaterShaderIDs.CausticsTexST, Vector4.zero);
            }

            // ---- 体积光 ----
            Vector3 sun = sunDirection;
            if (sun.sqrMagnitude < 1e-6f)
            {
                sun = new Vector3(-0.25f, 1f, -0.35f);
            }

            sun.Normalize();

            Shader.SetGlobalFloat(UnderwaterShaderIDs.GodRayEnabled,
                (godRaysEnabled && strength > 0.001f) ? 1f : 0f);
            Shader.SetGlobalVector(UnderwaterShaderIDs.SunDirection,
                new Vector4(sun.x, sun.y, sun.z, 0f));
            Shader.SetGlobalFloat(UnderwaterShaderIDs.GodRayIntensity, godRayIntensity);
            Shader.SetGlobalFloat(UnderwaterShaderIDs.GodRaySpread, Mathf.Clamp01(godRaySpread));
            Shader.SetGlobalFloat(UnderwaterShaderIDs.GodRayDistance, Mathf.Max(0.1f, godRayDistance));
            Shader.SetGlobalFloat(UnderwaterShaderIDs.GodRaySteps, Mathf.Max(1f, godRaySteps));
            Shader.SetGlobalFloat(UnderwaterShaderIDs.GodRayNoiseScale, Mathf.Max(0.01f, godRayNoiseScale));
            Shader.SetGlobalFloat(UnderwaterShaderIDs.GodRaySpeed, godRaySpeed);

            // ---- 水面波光 ----
            //
            // 【注意：这里推的五个值是「烤给水面材质的默认值」，不是运行时数据】
            //   水面波光的频率 / 亮度 / 亮边走的是材质属性，不走全局变量 ——
            //   因为只有水面那一块网格用它们，没必要污染全局命名空间。
            //
            //   编辑器工具在建水面的时候会把这一份烤进 WaterSurface.mat。
            //   所以**运行时改这几个值不会立刻生效**，想调请直接改材质。
            //   这样做是为了让「改哪个地方生效」永远是确定的。
        }

        // ==================================================================
        // 给别的系统查
        // ==================================================================

        /// <summary>相机是不是在水下（按「完全在水下」那条线判断，不含过渡带）。</summary>
        public bool IsCameraUnderwater(Camera cam)
        {
            if (cam == null)
            {
                return false;
            }

            return cam.transform.position.y < waterLevel - fadeInDepth;
        }

        /// <summary>世界坐标点到水面的高度差（米）。正值 = 在水面之上。</summary>
        public float HeightAboveWater(Vector3 worldPos)
        {
            return worldPos.y - waterLevel;
        }

        private void OnDrawGizmosSelected()
        {
            // 把水面画出来 —— 这个效果最常见的故障就是「水面高度填错了」，
            // 而填错时画面只是怪，不会报错。有个 Gizmo 能一眼看出来。
            Gizmos.color = new Color(0.4f, 0.9f, 1f, 0.9f);

            Vector3 center = new Vector3(transform.position.x, waterLevel, transform.position.z);
            var size = new Vector3(waterSize * 2f, 0f, waterSize * 2f);
            Gizmos.DrawWireCube(center, size);

            // 水下过渡带（实心一点的那一层）
            Gizmos.color = new Color(0.2f, 0.6f, 0.9f, 0.35f);
            Gizmos.DrawWireCube(
                new Vector3(center.x, waterLevel - fadeInDepth * 0.5f, center.z),
                new Vector3(waterSize * 2f, fadeInDepth, waterSize * 2f));

            // 光柱方向
            Gizmos.color = new Color(1f, 0.95f, 0.6f, 0.9f);
            Vector3 sun = sunDirection.sqrMagnitude < 1e-6f ? Vector3.up : sunDirection.normalized;
            Gizmos.DrawLine(center, center + sun * (waterSize * 0.6f));
        }
    }
}
