using GameFramework.Procedure;
using UnityGameFramework.Runtime;
using ProcedureOwner = GameFramework.Fsm.IFsm<GameFramework.Procedure.IProcedureManager>;

namespace PAO
{
    /// <summary>
    /// 启动流程。
    /// 游戏入口流程，负责全局初始化，完成后进入主菜单。
    /// </summary>
    public class ProcedureLaunch : ProcedureBase
    {
        protected override void OnEnter(ProcedureOwner procedureOwner)
        {
            base.OnEnter(procedureOwner);

            Log.Info("[ProcedureLaunch] 游戏启动");

            // TODO 全局初始化写在这里：
            // 1. 读取全局配置：GameEntry.GetComponent<ConfigComponent>()
            // 2. 初始化本地化：GameEntry.GetComponent<LocalizationComponent>()
            // 3. 初始化资源：GameEntry.GetComponent<ResourceComponent>().InitResources(...)
            // 4. 检查资源版本 / 热更（赛期可跳过）
            //
            // 赛期先直接进菜单，后续按需往这里加。

            ChangeState<ProcedureMenu>(procedureOwner);
        }
    }
}
