// ---------------------------------------------------------------------------
// PAO / PlayerAnimatorSetup —— takoyaki 角色动画状态机的一键落地
//
// ★ 重要：本脚本【只读】FBX 的导入设置，绝不写 ModelImporter 的序列化字段。
//
//   前一版曾用 SerializedObject 往 m_ClipAnimations 里手工补条目来改 clip 名，
//   结果因为没填 firstFrame/lastFrame，每个 clip 变成 0 帧，
//   Unity 原生导入器在 ModelImporter::SplitAnimationClips → RemovedMaskedCurve
//   处越界，整个编辑器 SIGSEGV 崩溃。那个做法已彻底删除，不要再加回来。
//
//   当前策略：
//     * clip 名保持 Blender 导出的原样（"骨架.005|AirLoop" 这种），不碰导入设置；
//     * 状态名用干净名（Idle / Move / ...），引用原 clip，用户看到的状态是干净的；
//     * JumpStart / Land 需要「不循环」，通过【复制】成独立 .anim（副本可自由编辑）
//       来实现，而不是去改 FBX 的导入设置。
//
// 为什么不用手写 .controller 资产：
//   Unity 引用 FBX 内部 AnimationClip 时写的是 64 位 local fileID，由 Unity 内部
//   规则生成，手写极易算错，错了就是一堆 missing 引用。所以统一走官方 Editor API，
//   让 Unity 自己算 ID 并序列化。
//
// 用法：
//   * 菜单 PAO ▸ 角色动画 ▸ ① 一键落地（状态机 + 挂载）  —— 不会自动跑，需手动点；
//   * 菜单 PAO ▸ 角色动画 ▸ ② 自检（只读）              —— 任何时候都能跑，只打日志。
// ---------------------------------------------------------------------------
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace PAO.EditorTools
{
    public static class PlayerAnimatorSetup
    {
        // ---------------- 路径与常量 ----------------

        private const string FbxPath =
            "Assets/GameMain/Art/Models/最终版模型/shuimu_unity_5animations_face.fbx";

        private const string ControllerPath =
            "Assets/GameMain/Art/Models/takoyaki.controller";

        private const string PrefabPath =
            "Assets/GameMain/Art/Models/takoyaki.prefab";

        /// <summary>JumpStart / Land 的独立副本放这里（FBX 里的原件保持不动）。</summary>
        private const string OneShotClipFolder =
            "Assets/GameMain/Art/Models/Animations";

        // 状态名 == 目标 clip 名
        private const string Idle = "Idle";
        private const string Move = "Move";
        private const string AirLoop = "AirLoop";
        private const string JumpStart = "JumpStart";
        private const string Land = "Land";

        private static readonly string[] AllClips = { Idle, Move, AirLoop, JumpStart, Land };

        /// <summary>本身就该循环的，直接用 FBX 里的原件。</summary>
        private static readonly string[] LoopingClips = { Idle, Move, AirLoop };

        /// <summary>一次性动作，需要独立副本才能关掉循环。</summary>
        private static readonly string[] OneShotClips = { JumpStart, Land };

        // 参数名必须与 PlayerAnimatorDriver 里 StringToHash 的字符串一致
        private const string ParamGrounded = "Grounded";
        private const string ParamLifted = "Lifted";
        private const string ParamMoveSpeed = "MoveSpeed";
        private const string ParamJump = "Jump";

        // 阈值也要与 PlayerAnimatorDriver 的 m_MoveSpeedThreshold 保持同量级
        private const float MoveSpeedThreshold = 0.1f;

        private const float BlendSeconds = 0.15f;      // 常规混合时长
        private const float JumpBlendSeconds = 0.08f;  // 起跳响应要更干脆

        /// <summary>
        /// JumpStart 的兜底退出时刻（归一化）。
        /// 正常情况下「离地」条件会先把它切走；这条只用于「跳了但没离地」时脱身，
        /// 所以要早 —— 卡在起跳姿势里比切早了难看得多。
        /// </summary>
        private const float JumpStartFallbackExitTime = 0.35f;

        /// <summary>
        /// Land 播到什么进度开始混出（归一化）。Land 不循环，这个值就是字面意义的
        /// 「播到 85% 那一刻」，留 15% 尾巴给混合。
        /// </summary>
        private const float LandExitTime = 0.85f;

        /// <summary>
        /// 状态机定义的版本号。
        ///
        /// 【改了过渡/状态/参数的逻辑，就把这个数字 +1】——
        /// 否则脚本会认为现有 controller 已经是对的，不会重建，你的改动不生效。
        /// 版本记录存在 EditorPrefs 里（跟机器绑定，不进版本库）。
        ///
        ///   v1  初版
        ///   v2  起跳/落地改为物理事件驱动（原先用动画时长，跟滞空时间对不上）
        ///   v3  试过自动开 loopTime —— 崩了，已移除
        ///   v4  移除全部写 ModelImporter 的代码；Loop Time 改人工勾选
        /// </summary>
        private const int LayoutVersion = 4;

        private static readonly string LayoutVersionKey =
            "PAO.PlayerAnimatorSetup.LayoutVersion." + Application.dataPath.GetHashCode();

        // ==================================================================
        // ① 一键落地
        // ==================================================================
        [MenuItem("PAO/角色动画/① 一键落地（状态机 + 挂载）", false, 1)]
        public static void RunAllMenu() => RunAll(false);

        /// <summary>强制重建 controller（忽略上面那个版本号检查）。</summary>
        [MenuItem("PAO/角色动画/重建状态机（强制）", false, 20)]
        public static void ForceRebuild() => RunAll(false, true);

        private static void RunAll(bool silentIfDone, bool force = false)
        {
            if (!GuardFbxHealthy())
            {
                return;
            }

            var clips = LoadClips();
            if (clips == null || clips.Count == 0)
            {
                Debug.LogError($"[角色动画] 读不到 FBX 里的动画 clip：{FbxPath}");
                return;
            }

            var missing = AllClips.Where(n => !clips.ContainsKey(n)).ToArray();
            if (missing.Length > 0)
            {
                Debug.LogError($"[角色动画] FBX 里缺少这些 clip：{string.Join(" / ", missing)}\n" +
                               $"实际读到 {clips.Count} 段：{string.Join(" / ", clips.Keys)}");
                return;
            }

            bool changed = false;

            // ---- 1. 给一次性动作准备独立副本（关掉循环）----
            changed |= PrepareOneShotClips(clips);

            // ---- 注意：这里【故意不做】给循环动作开 loopTime ----
            //
            // Idle / Move / AirLoop 的 Loop Time 需要改 FBX 导入设置，只能走 ModelImporter。
            // 这条路我试了两次，两次都把编辑器搞崩（SIGSEGV @ ModelImporter::SplitAnimationClips），
            // 所以彻底放弃自动化，改由人工在 FBX 的 Animation 页勾选 —— 见自检里的提示。
            //
            // 【不要再把写 ModelImporter 的代码加回来。】
            // 代价是每导入一次新模型要手工勾 3 个勾，但那是 30 秒的事；
            // 而这是第二次把整个编辑器搞崩，不值当。

            // ---- 2. 生成 controller ----
            changed |= BuildController(clips, silentIfDone, force);

            // ---- 3. 挂载 ----
            changed |= WirePrefab(silentIfDone);

            if (changed)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log("[角色动画] ① 一键落地完成。");
            }
            else if (!silentIfDone)
            {
                Debug.Log("[角色动画] 已经是最新状态，无需改动。");
            }
        }

        // ==================================================================
        // ② 自检（只读）
        // ==================================================================
        [MenuItem("PAO/角色动画/② 自检（只读）", false, 2)]
        public static void VerifyMenu() => Verify();

        public static void Verify()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[角色动画] ==== 自检 ====");

            sb.AppendLine($"  FBX 导入设置  {(GuardFbxHealthy() ? "✓ 正常" : "✗ 异常，见上方报错")}");

            var clips = LoadClips();
            if (clips == null || clips.Count == 0)
            {
                sb.AppendLine($"  FBX        ✗ 读不到 {FbxPath}");
            }
            else
            {
                sb.AppendLine($"  FBX        ✓ {clips.Count} 段 clip");

                var loopWrong = new System.Collections.Generic.List<string>();

                foreach (var name in AllClips)
                {
                    if (clips.TryGetValue(name, out var clip))
                    {
                        // loopTime 必须报出来 —— 「播一次就停」就是这里没对
                        var cs = AnimationUtility.GetAnimationClipSettings(clip);
                        bool want = LoopingClips.Contains(name);
                        bool ok = cs.loopTime == want;
                        string loopMark = ok
                            ? (want ? "loop ✓" : "once ✓")
                            : $"{(cs.loopTime ? "loop" : "once")} ✗ 期望 {(want ? "loop" : "once")}";

                        if (!ok)
                        {
                            loopWrong.Add(name);
                        }

                        sb.AppendLine($"    {name,-10} ✓ {clip.length:F2}s  {loopMark,-12} 源={clip.name}");
                    }
                    else
                    {
                        sb.AppendLine($"    {name,-10} ✗ 缺失");
                    }
                }

                if (loopWrong.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine($"  ⚠ 这些动作的 Loop Time 没设对：{string.Join(" / ", loopWrong)}");
                    sb.AppendLine("    后果：动画播一次就停在最后一帧（站着不动、走路都会停住）。");
                    sb.AppendLine();
                    sb.AppendLine("    【手工修法，30 秒】脚本不代劳 —— 改这里两次把编辑器搞崩过：");
                    sb.AppendLine($"      1. Project 窗口点选 {System.IO.Path.GetFileName(FbxPath)}（选中源文件，不是场景里的实例）");
                    sb.AppendLine("      2. Inspector 顶部切到 Animation 页");
                    sb.AppendLine("      3. 展开 Clip 列表，找到下面这几段，勾上 Loop Time：");
                    sb.AppendLine($"         {string.Join("、", loopWrong)}");
                    sb.AppendLine("         （JumpStart / Land 保持不勾 —— 它们是一次性动作）");
                    sb.AppendLine("      4. 点右下角 Apply");
                    sb.AppendLine("    勾选会写进 .fbx.meta 并随仓库提交，队友不用重复做。");
                }
            }

            // 一次性副本
            foreach (var name in OneShotClips)
            {
                var copy = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{OneShotClipFolder}/{name}.anim");
                sb.AppendLine($"  副本 {name,-10} {(copy != null ? "✓ " + copy.name : "（尚未生成）")}");
            }

            // controller
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
            {
                sb.AppendLine($"  Controller ✗ 不存在：{ControllerPath}");
            }
            else
            {
                var sm = controller.layers.Length > 0 ? controller.layers[0].stateMachine : null;
                sb.AppendLine($"  Controller ✓ {ControllerPath}");
                sb.AppendLine($"    参数 {controller.parameters.Length} 个：" +
                              string.Join(", ", controller.parameters.Select(p => p.name + ":" + p.type)));
                sb.AppendLine($"    状态 {(sm != null ? sm.states.Length : 0)} 个，过渡 {CountTransitions(sm)} 条");
                if (sm != null)
                {
                    foreach (var s in sm.states)
                    {
                        string motion = s.state.motion != null ? s.state.motion.name : "(无 motion)";
                        sb.AppendLine($"      - {s.state.name,-10} → {motion}");
                    }
                }
            }

            // prefab
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                sb.AppendLine($"  Prefab     ✗ 不存在：{PrefabPath}");
            }
            else
            {
                var animator = prefab.GetComponentInChildren<Animator>(true);
                var driver = prefab.GetComponentInChildren<PlayerAnimatorDriver>(true);
                sb.AppendLine($"  Prefab     ✓ {PrefabPath}");
                sb.AppendLine($"    Animator  {(animator != null ? "✓ 在 " + animator.gameObject.name : "✗ 缺失")}");
                if (animator != null)
                {
                    sb.AppendLine($"      controller {(animator.runtimeAnimatorController != null ? "✓ " + animator.runtimeAnimatorController.name : "✗ 未指定")}");
                }
                sb.AppendLine($"    Driver    {(driver != null ? "✓ 在 " + driver.gameObject.name : "✗ 缺失")}");
            }

            // 场景里的预制体覆盖 —— 这是最容易被忽略的一类问题：
            // 预制体改对了，但场景实例上留了一条把字段覆盖成空的记录，运行时用的还是空值。
            sb.Append(CheckSceneOverrides(prefab));

            Debug.Log(sb.ToString());
        }

        /// <summary>
        /// 检查已打开的场景里，takoyaki 预制体实例上有没有把 Animator.m_Controller 覆盖成空的记录。
        /// 有的话预制体改得再对也没用 —— 场景里那份覆盖优先级更高。
        /// </summary>
        private static string CheckSceneOverrides(GameObject prefabAsset)
        {
            var sb = new System.Text.StringBuilder();

            if (prefabAsset == null)
            {
                return string.Empty;
            }

            var animatorInPrefab = prefabAsset.GetComponentInChildren<Animator>(true);
            if (animatorInPrefab == null)
            {
                return string.Empty;
            }

            int checkedScenes = 0;
            int bad = 0;

            for (int sceneIndex = 0; sceneIndex < UnityEngine.SceneManagement.SceneManager.sceneCount; sceneIndex++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(sceneIndex);
                if (!scene.isLoaded)
                {
                    continue;
                }

                checkedScenes++;

                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var inst in root.GetComponentsInChildren<Transform>(true))
                    {
                        var stage = PrefabUtility.GetPrefabInstanceStatus(inst.gameObject);
                        if (stage != PrefabInstanceStatus.Connected)
                        {
                            continue;
                        }

                        GameObject src = PrefabUtility.GetCorrespondingObjectFromSource(inst.gameObject);
                        if (src == null || src != prefabAsset)
                        {
                            continue;
                        }

                        var mods = PrefabUtility.GetPropertyModifications(inst.gameObject);
                        if (mods == null)
                        {
                            continue;
                        }

                        foreach (var mod in mods)
                        {
                            if (mod == null || mod.target == null)
                            {
                                continue;
                            }
                            if (mod.propertyPath != "m_Controller" || !(mod.target is Animator))
                            {
                                continue;
                            }

                            if (mod.objectReference == null)
                            {
                                sb.AppendLine(
                                    $"  ⚠ 场景覆盖  {scene.name} / {inst.name}：Animator.m_Controller " +
                                    "被场景实例覆盖成【空】，预制体里设好的 controller 不会生效。");
                                sb.AppendLine(
                                    "      修法：Hierarchy 选中该物体 → Animator 的 Controller 行右键 → Revert");
                                bad++;
                            }
                        }
                    }
                }
            }

            if (checkedScenes == 0)
            {
                sb.AppendLine("  场景覆盖  (没有已打开的场景，跳过检查)");
            }
            else if (bad == 0)
            {
                sb.AppendLine($"  场景覆盖  ✓ {checkedScenes} 个场景里没有把 controller 覆盖成空的记录");
            }

            return sb.ToString();
        }

        // ==================================================================
        // 安全闸：确认 FBX 导入设置没被写坏
        // ==================================================================
        /// <summary>
        /// 读 .fbx.meta，若发现 clipAnimations 里存在 firstFrame/lastFrame 全为 0 的条目，
        /// 说明导入设置被写坏过（会让 Unity 原生导入器崩溃），此时拒绝继续操作。
        /// 这是那场事故的守门员 —— 只读文本，不碰任何 API。
        /// </summary>
        private static bool GuardFbxHealthy()
        {
            string metaPath = FbxPath + ".meta";
            if (!File.Exists(metaPath))
            {
                Debug.LogError($"[角色动画] 找不到 {metaPath}");
                return false;
            }

            string[] lines = File.ReadAllLines(metaPath);
            bool sawFirst = false;
            int firstFrame = 0;

            foreach (string raw in lines)
            {
                string line = raw.Trim();

                if (line.StartsWith("firstFrame:"))
                {
                    sawFirst = true;
                    int.TryParse(line.Substring("firstFrame:".Length).Trim(), out firstFrame);
                }
                else if (line.StartsWith("lastFrame:") && sawFirst)
                {
                    int lastFrame;
                    int.TryParse(line.Substring("lastFrame:".Length).Trim(), out lastFrame);

                    if (firstFrame == 0 && lastFrame == 0)
                    {
                        Debug.LogError(
                            "[角色动画] 检测到 FBX 导入设置损坏：clipAnimations 里存在 " +
                            "firstFrame=0 / lastFrame=0 的条目（0 帧 clip）。\n" +
                            "这会让 Unity 原生模型导入器崩溃，因此已中止操作。\n" +
                            $"修复方法：把 {metaPath} 里的整段 clipAnimations 覆盖块改成 `clipAnimations: []`，" +
                            "然后删除 Library/ArtifactDB 与 Library/SourceAssetDB 让 Unity 重新导入。");
                        return false;
                    }

                    sawFirst = false;
                }
            }

            return true;
        }

        // ==================================================================
        // clip 读取（只读）
        // ==================================================================

        /// <summary>
        /// 读 FBX 里所有动画 clip。键是去掉导出前缀的干净名（Idle / Move / ...）。
        /// Blender 导出的 take 名形如「骨架.005|Idle」，这里只取最后一段做键，
        /// 但【不修改】clip 本身 —— 状态名用它，motion 仍引用原 clip。
        /// </summary>
        private static Dictionary<string, AnimationClip> LoadClips()
        {
            var all = AssetDatabase.LoadAllAssetsAtPath(FbxPath).OfType<AnimationClip>()
                .Where(c => c != null && !c.name.StartsWith("__preview__"))
                .ToArray();

            if (all.Length == 0)
            {
                return null;
            }

            var map = new Dictionary<string, AnimationClip>();
            foreach (var c in all)
            {
                string key = CleanName(c.name);
                if (!string.IsNullOrEmpty(key) && !map.ContainsKey(key))
                {
                    map.Add(key, c);
                }
            }

            return map;
        }

        /// <summary>「骨架.002|骨架.005|Idle」→「Idle」。</summary>
        private static string CleanName(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return raw;
            }

            string[] parts = raw.Split('|');
            string last = parts[parts.Length - 1].Trim();
            return string.IsNullOrEmpty(last) ? raw : last;
        }

        // ==================================================================
        // 1. 一次性动作的独立副本
        // ==================================================================
        /// <summary>
        /// JumpStart / Land 需要 loop = false。FBX 里的 clip 是子资产、不能直接改设置，
        /// 而写 ModelImporter 正是上次崩溃的原因。所以改成「复制成独立 .anim」——
        /// 副本是普通资产，AnimationClipSettings 可以随便改，且完全不影响 FBX 导入。
        /// </summary>
        private static bool PrepareOneShotClips(Dictionary<string, AnimationClip> clips)
        {
            bool changed = false;

            foreach (string name in OneShotClips)
            {
                if (!clips.TryGetValue(name, out var source) || source == null)
                {
                    Debug.LogWarning($"[角色动画] 找不到 {name} 的源 clip，跳过副本制作。");
                    continue;
                }

                string assetPath = $"{OneShotClipFolder}/{name}.anim";
                var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);

                if (existing != null)
                {
                    var settings = AnimationUtility.GetAnimationClipSettings(existing);
                    if (settings.loopTime)
                    {
                        settings.loopTime = false;
                        AnimationUtility.SetAnimationClipSettings(existing, settings);
                        EditorUtility.SetDirty(existing);
                        Debug.Log($"[角色动画] {name}.anim 关掉 Loop Time");
                        changed = true;
                    }
                    continue;
                }

                if (!AssetDatabase.IsValidFolder(OneShotClipFolder))
                {
                    AssetDatabase.CreateFolder("Assets/GameMain/Art/Models", "Animations");
                }

                // 复制成可独立编辑的资产
                var copy = Object.Instantiate(source);
                copy.name = name;

                var copySettings = AnimationUtility.GetAnimationClipSettings(copy);
                copySettings.loopTime = false;
                AnimationUtility.SetAnimationClipSettings(copy, copySettings);

                AssetDatabase.CreateAsset(copy, assetPath);
                Debug.Log($"[角色动画] 生成一次性副本 {assetPath}（loop=false）");
                changed = true;
            }

            return changed;
        }

        // ==================================================================
        // 【已移除】自动写 ModelImporter 设置 loopTime 的代码
        //
        // 原本这里有一段 FixLoopTimeOnSubClips()：通过 SerializedObject 往
        // FBX 的 m_ClipAnimations 里写 loopTime —— 因为 FBX 子 clip 不能直接改设置，
        // 只有这一条路。
        //
        // 但它【两次】把 Unity 编辑器搞崩：
        //   SIGSEGV @ (Unity) RemovedMaskedCurve
        //             (Unity) ModelImporter::SplitAnimationClips
        // 第一次是漏填 firstFrame/lastFrame（clip 变 0 帧）；
        // 第二次两个字段都填了由「时长×帧率」算出的真实值，仍然崩。
        // 说明这条序列化路径在这个 FBX 上不可靠，不是补几个字段能解决的。
        //
        // 所以：Idle / Move / AirLoop 的 Loop Time 改由【人工】在
        // FBX 的 Inspector ▸ Animation 页勾选。详见自检输出里的提示。
        //
        // 【绝对不要再把这段代码加回来。】
        // ==================================================================

        // ==================================================================
        // 2. 生成 controller
        // ==================================================================
        private static bool BuildController(Dictionary<string, AnimationClip> clips, bool silent, bool force = false)
        {
            var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (existing != null && !force)
            {
                // 结构对 + 定义版本一致，才算"已经是最新"。
                // 只看结构是不够的：改了过渡逻辑但状态数量没变时，结构检查会误判成"已完成"。
                bool structureOk = existing.parameters.Length == 4
                                   && existing.layers.Length > 0
                                   && existing.layers[0].stateMachine != null
                                   && AllClips.All(n => existing.layers[0].stateMachine.states
                                       .Any(s => s.state.name == n));

                bool versionOk = EditorPrefs.GetInt(LayoutVersionKey, 0) == LayoutVersion;

                if (structureOk && versionOk)
                {
                    return false;   // 已经是最新定义，不动它
                }

                if (structureOk && !versionOk)
                {
                    Debug.Log($"[角色动画] 状态机定义升到 v{LayoutVersion}，重建 controller");
                }
                else
                {
                    Debug.Log($"[角色动画] 现有 controller 不完整，重建：{ControllerPath}");
                }
            }
            else if (existing != null)
            {
                Debug.Log($"[角色动画] 强制重建 controller：{ControllerPath}");
            }

            if (existing != null)
            {
                // 删之前先导出成文本备份 —— 这个菜单以前是「先删后建」，
                // 中途一旦出错（比如上次那个导入器崩溃），controller 就凭空没了。
                // 有了这份文本，最坏也能手工粘回去。
                BackupControllerText(existing);
                AssetDatabase.DeleteAsset(ControllerPath);
            }

            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            var sm = controller.layers[0].stateMachine;

            // ---- 参数 ----
            controller.AddParameter(ParamGrounded, AnimatorControllerParameterType.Bool);
            controller.AddParameter(ParamLifted, AnimatorControllerParameterType.Bool);
            controller.AddParameter(ParamMoveSpeed, AnimatorControllerParameterType.Float);
            controller.AddParameter(ParamJump, AnimatorControllerParameterType.Trigger);

            // ---- 状态 ----
            var states = new Dictionary<string, AnimatorState>();
            foreach (string name in AllClips)
            {
                var state = sm.AddState(name);

                // 一次性动作用副本，其余直接用 FBX 原件
                if (OneShotClips.Contains(name))
                {
                    var copy = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{OneShotClipFolder}/{name}.anim");
                    state.motion = copy != null ? copy : clips[name];
                }
                else
                {
                    state.motion = clips[name];
                }

                state.writeDefaultValues = true;
                states[name] = state;
            }

            sm.defaultState = states[Idle];

            // ---- 过渡 ----
            // 两条设计原则：
            //   1. 每个状态都必须有「保证能走出去」的路径，否则会卡死；
            //   2. 状态的切换时刻由【物理事件】决定，不由【动画时长】决定 ——
            //      后者一旦改了跳跃参数或换了动作就会错位（下面有具体的踩坑记录）。

            // 地面：Idle ⇄ Move
            AddInstant(states[Idle], states[Move], BlendSeconds,
                (ParamMoveSpeed, AnimatorConditionMode.Greater, MoveSpeedThreshold),
                (ParamGrounded, AnimatorConditionMode.If, 0),
                (ParamLifted, AnimatorConditionMode.IfNot, 0));

            AddInstant(states[Move], states[Idle], BlendSeconds,
                (ParamMoveSpeed, AnimatorConditionMode.Less, MoveSpeedThreshold),
                (ParamGrounded, AnimatorConditionMode.If, 0),
                (ParamLifted, AnimatorConditionMode.IfNot, 0));

            // 起跳：站定/移动都能起跳。Trigger 只消耗一次，不会连发
            AddInstant(states[Idle], states[JumpStart], JumpBlendSeconds,
                (ParamJump, AnimatorConditionMode.If, 0));

            AddInstant(states[Move], states[JumpStart], JumpBlendSeconds,
                (ParamJump, AnimatorConditionMode.If, 0));

            // 离地（走下坡、被弹飞、掉出平台）也要进滞空，不能只认起跳键
            AddInstant(states[Idle], states[AirLoop], BlendSeconds,
                (ParamGrounded, AnimatorConditionMode.IfNot, 0));

            AddInstant(states[Move], states[AirLoop], BlendSeconds,
                (ParamGrounded, AnimatorConditionMode.IfNot, 0));

            // 被泡泡提起来 → 滞空（驱动里已按未落地处理，这里是双保险）
            AddInstant(states[Idle], states[AirLoop], BlendSeconds,
                (ParamLifted, AnimatorConditionMode.If, 0));

            AddInstant(states[Move], states[AirLoop], BlendSeconds,
                (ParamLifted, AnimatorConditionMode.If, 0));

            // ---- 起跳/落地：由物理事件驱动，绝不用动画时长决定 ----
            //
            // 这里踩过坑，记录一下为什么这么写：
            //   最初 JumpStart → AirLoop 用的是「无条件的定时过渡」（播到 85% 就走），
            //   AirLoop → Land 用的是「HasExitTime + ExitTime=0」。
            //   实测下来是坏的：
            //     · 跳跃高度 1.2m / 重力 -20 → 实际滞空只有 0.693s
            //       而 JumpStart 动作本身就有 0.750s，起跳动作永远播不完；
            //     · 结果 AirLoop 只剩 1.3 帧就被切走，连 0.15s 的混入都走不完，
            //       角色根本没进过滞空姿势；
            //     · Unity 文档里 Exit Time 的语义是「达到该归一化时刻的那一帧才为真，
            //       下一帧就为假」，所以 ExitTime=0 在循环动画上是否即时放行是有歧义的 ——
            //       落地这种必须跟手的切换，不能建在有歧义的语义上。
            //
            //   现在改为：离地就进 AirLoop、落地就进 Land，判据只有 Grounded/Lifted。
            //   这样改跳跃高度、换动作、调动作时长，都不会再错位。

            // 离地 → 滞空
            AddInstant(states[JumpStart], states[AirLoop], BlendSeconds,
                (ParamGrounded, AnimatorConditionMode.IfNot, 0));

            AddInstant(states[JumpStart], states[AirLoop], BlendSeconds,
                (ParamLifted, AnimatorConditionMode.If, 0));

            // 兜底：JumpStart 是「起跳」姿势，万一没能离地（被挡住 / 撞天花板），
            // 不能一直卡在起跳姿势里。等到这个时刻再按当时是否在走动分流。
            AddTimedIf(states[JumpStart], states[Idle], BlendSeconds, JumpStartFallbackExitTime,
                (ParamMoveSpeed, AnimatorConditionMode.Less, MoveSpeedThreshold));

            AddTimedIf(states[JumpStart], states[Move], BlendSeconds, JumpStartFallbackExitTime,
                (ParamMoveSpeed, AnimatorConditionMode.Greater, MoveSpeedThreshold));

            // 落地 → 缓冲。落地必须跟手，所以用纯条件即时切。
            AddInstant(states[AirLoop], states[Land], BlendSeconds,
                (ParamGrounded, AnimatorConditionMode.If, 0),
                (ParamLifted, AnimatorConditionMode.IfNot, 0));

            // 缓冲动作播到尾声再回到地面状态，按是否在走动分流。
            // 这条用 exitTime 是合理的：Land 不循环，Exit Time 就是字面意义的
            // 「播到 85% 那一刻」。
            AddTimedIf(states[Land], states[Idle], BlendSeconds, LandExitTime,
                (ParamMoveSpeed, AnimatorConditionMode.Less, MoveSpeedThreshold));

            AddTimedIf(states[Land], states[Move], BlendSeconds, LandExitTime,
                (ParamMoveSpeed, AnimatorConditionMode.Greater, MoveSpeedThreshold));

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            // 记下这次生成用的是哪一版定义
            EditorPrefs.SetInt(LayoutVersionKey, LayoutVersion);

            Debug.Log($"[角色动画] 已生成 {ControllerPath}" +
                      $"（定义 v{LayoutVersion} / 5 状态 / 4 参数 / {CountTransitions(sm)} 条过渡）");
            return true;
        }

        /// <summary>
        /// 即时过渡：条件满足立刻切，不等动画播完。
        /// 用于 Idle ⇄ Move、以及「离开地面」「被提起」这类必须马上响应的切换。
        /// </summary>
        private static void AddInstant(AnimatorState from, AnimatorState to, float duration,
            params (string param, AnimatorConditionMode mode, float threshold)[] conditions)
        {
            var t = from.AddTransition(to);
            AddConditions(t, conditions);
            t.hasExitTime = false;
            t.exitTime = 0f;
            t.duration = duration;
            t.hasFixedDuration = true;
        }

        /// <summary>
        /// 到点 + 条件：等源动画播到 exitTime 那一刻，条件成立才切。
        /// 只用于【不循环】的一次性动画（Land 的收尾、以及 JumpStart 的兜底）——
        /// 对不循环的动画，exitTime 就是字面意义的「播到百分之几」。
        /// 循环动画不要用这个：Unity 文档说明 Exit Time 只在「正好那一帧」为真，
        /// 对循环状态会变成「每轮只有一帧能过」，很容易拖慢响应。
        /// </summary>
        private static void AddTimedIf(AnimatorState from, AnimatorState to, float duration, float exitTime,
            params (string param, AnimatorConditionMode mode, float threshold)[] conditions)
        {
            var t = from.AddTransition(to);
            AddConditions(t, conditions);
            t.hasExitTime = true;
            t.exitTime = exitTime;
            t.duration = duration;
            t.hasFixedDuration = true;
        }

        private static void AddConditions(AnimatorStateTransition t,
            (string param, AnimatorConditionMode mode, float threshold)[] conditions)
        {
            foreach (var (param, mode, threshold) in conditions)
            {
                if (string.IsNullOrEmpty(param))
                {
                    continue;
                }
                t.AddCondition(mode, threshold, param);
            }
        }

        private static int CountTransitions(AnimatorStateMachine sm)
        {
            if (sm == null)
            {
                return 0;
            }

            int n = 0;
            foreach (var s in sm.states)
            {
                n += s.state.transitions.Length;
            }
            foreach (var sub in sm.stateMachines)
            {
                n += CountTransitions(sub.stateMachine);
            }
            return n;
        }

        // ==================================================================
        // 3. 挂载：Animator 挂模型根，Driver 挂角色根
        // ==================================================================
        private static bool WirePrefab(bool silent)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                Debug.LogError($"[角色动画] 找不到预制体：{PrefabPath}");
                return false;
            }

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
            {
                Debug.LogError($"[角色动画] 找不到 controller：{ControllerPath}");
                return false;
            }

            // 改预制体前先备份，出问题可以立刻还原。
            // 注意：备份只应该在「确实要改」的时候做，否则每点一次菜单就多一个文件。
            // 所以先加载、判断有没有改动，最后真要保存时才备份。
            bool changed = false;
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                // ---- Animator：挂在模型根节点（骨架所在的那个 GameObject）----
                // 优先找挂着 JellyFaceSwitcher 的那个节点 —— 就是 FBX 的根。
                // Generic 骨架靠层级路径绑定，Animator 必须和骨架在同一个层级树下。
                GameObject modelRoot = null;
                var switcher = root.GetComponentInChildren<JellyFaceSwitcher>(true);
                if (switcher != null)
                {
                    modelRoot = switcher.gameObject;
                }
                else
                {
                    var anyAnimator = root.GetComponentInChildren<Animator>(true);
                    modelRoot = anyAnimator != null ? anyAnimator.gameObject : null;
                }

                if (modelRoot == null)
                {
                    Debug.LogError("[角色动画] 定位不到模型根节点（既没有 JellyFaceSwitcher 也没有 Animator）。");
                    return false;
                }

                var animator = modelRoot.GetComponent<Animator>();
                if (animator == null)
                {
                    animator = modelRoot.AddComponent<Animator>();
                    Debug.Log($"[角色动画] 在 {modelRoot.name} 上添加 Animator");

                    // 加了组件就必须保存 —— 不能只靠下面「controller 变了没」来判断，
                    // 否则当 controller 恰好已经是对的时，新加的组件不会被写回预制体。
                    changed = true;
                }

                if (animator.runtimeAnimatorController != controller)
                {
                    animator.runtimeAnimatorController = controller;
                    animator.applyRootMotion = false;   // 位移由 PlayerController 管，动画不许抢
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    Debug.Log($"[角色动画] {modelRoot.name}.Animator.controller → {controller.name}");
                    changed = true;
                }

                // ---- Driver：挂在角色根节点，与 PlayerController 同层 ----
                var driver = root.GetComponent<PlayerAnimatorDriver>();
                if (driver == null)
                {
                    driver = root.AddComponent<PlayerAnimatorDriver>();
                    Debug.Log($"[角色动画] 在 {root.name} 上添加 PlayerAnimatorDriver");
                    changed = true;
                }

                // 用 SerializedObject 写私有字段，顺手把 Animator 引用接上
                var so = new SerializedObject(driver);
                var animProp = so.FindProperty("m_Animator");
                if (animProp != null && animProp.objectReferenceValue != animator)
                {
                    animProp.objectReferenceValue = animator;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    changed = true;
                }

                if (changed)
                {
                    // 确认要改了才备份 —— 否则每点一次菜单都会多一个备份文件
                    BackupPrefab();
                    PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            if (changed)
            {
                Debug.Log($"[角色动画] 预制体已更新：{PrefabPath}");
            }
            else if (!silent)
            {
                Debug.Log("[角色动画] 预制体已经挂好了，未改动。");
            }

            return changed;
        }

        /// <summary>
        /// 把现有 controller 的文本内容备份到 .tmp/。
        /// 目的：重建流程要删掉旧资产，万一之后出错，至少还有原文可以抄回来。
        ///
        /// .controller 本身就是 YAML 文本资产，直接复制文件即可，
        /// 不要走 EditorYamlUtility / 序列化那些内部 API。
        /// </summary>
        private static void BackupControllerText(AnimatorController controller)
        {
            try
            {
                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                string src = Path.Combine(projectRoot, ControllerPath);

                if (!File.Exists(src))
                {
                    return;   // 内存里的对象没落盘过，没有原文可备份，跳过
                }

                string backupDir = Path.Combine(projectRoot, ".tmp", "controller-backup");
                Directory.CreateDirectory(backupDir);

                string stamp = System.DateTime.Now.ToString("yyyyMMdd-HHmmss");
                string dest = Path.Combine(backupDir, $"takoyaki.controller.{stamp}.txt");

                File.Copy(src, dest, true);
                Debug.Log($"[角色动画] 旧 controller 已备份 → {dest}");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[角色动画] controller 备份失败（继续执行）：{e.Message}");
            }
        }

        /// <summary>把预制体备份到工程根目录的 .tmp/ 下（不属于 Assets，不会被 Unity 导入）。</summary>
        private static void BackupPrefab()
        {
            try
            {
                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                string backupDir = Path.Combine(projectRoot, ".tmp", "prefab-backup");
                Directory.CreateDirectory(backupDir);

                string stamp = System.DateTime.Now.ToString("yyyyMMdd-HHmmss");
                string dest = Path.Combine(backupDir, $"takoyaki-{stamp}.prefab");
                File.Copy(PrefabPath, dest, true);
                Debug.Log($"[角色动画] 预制体已备份 → {dest}");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[角色动画] 预制体备份失败（继续执行）：{e.Message}");
            }
        }
    }
}
