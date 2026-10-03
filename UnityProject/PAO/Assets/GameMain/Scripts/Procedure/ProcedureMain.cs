using GameFramework.Procedure;
using UnityGameFramework.Runtime;
using ProcedureOwner = GameFramework.Fsm.IFsm<GameFramework.Procedure.IProcedureManager>;

namespace PAO
{
    /// <summary>
    /// 游戏主流程。
    /// 《泡涌》的核心玩法在这里跑：泡泡载体、三条规则、涌现。
    /// </summary>
    public class ProcedureMain : ProcedureBase
    {
        protected override void OnEnter(ProcedureOwner procedureOwner)
        {
            base.OnEnter(procedureOwner);

            Log.Info("[ProcedureMain] 进入游戏");

            // TODO 加载关卡场景：
            // GameEntry.GetComponent<SceneComponent>().LoadScene("Level01", ...);
        }

        protected override void OnUpdate(ProcedureOwner procedureOwner, float elapseSeconds, float realElapseSeconds)
        {
            base.OnUpdate(procedureOwner, elapseSeconds, realElapseSeconds);

            // TODO 玩法逻辑：
            // 玩家的每一次操作都应该是「投喂诱因」，而不是直接控制结果。
            // 局部规则 → 整体涌现，这是本作的核心体验。
        }

        protected override void OnLeave(ProcedureOwner procedureOwner, bool isShutdown)
        {
            base.OnLeave(procedureOwner, isShutdown);

            // TODO 离开时清理：卸载关卡场景、回收实体
        }
    }
}
