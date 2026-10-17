using UnityEngine;

namespace PAO
{
    /// <summary>
    /// 地形版本戳。挂在关卡白盒根物体下的一个空物体上，只做一件事：
    /// 记住"这份地形是搭建脚本哪个版本生成的"。
    ///
    /// 【为什么需要它】
    /// 搭建脚本是幂等的（重跑会先删再建），但**自动执行钩子**只在
    /// "根物体不存在"时才搭。地形改了之后根物体还在，钩子就不会重建 ——
    /// 结果是你改了脚本、切回 Unity，看到的还是旧布局，还以为脚本没生效。
    ///
    /// 有了版本戳，钩子改成判断"戳上的版本 == 脚本当前版本"，
    /// 版本对不上就自动重搭一次。改地形时只要把脚本里的
    /// <c>kLayoutVersion</c> 加一即可。
    ///
    /// 【为什么放在运行时程序集而不是 Editor 程序集】
    /// 场景需要序列化这个组件。如果类定义在 Editor 程序集里，
    /// 打包时组件类型不存在，场景里就会留下一个"Missing Script"。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LayoutVersionStamp : MonoBehaviour
    {
        [Tooltip("搭建这份地形时，脚本里的地形版本号")]
        public int Version;
    }
}
