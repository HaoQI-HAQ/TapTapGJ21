using GameFramework.Procedure;
using UnityGameFramework.Runtime;
using ProcedureOwner = GameFramework.Fsm.IFsm<GameFramework.Procedure.IProcedureManager>;

namespace PAO
{
    /// <summary>
    /// 主菜单流程。
    /// 等待玩家点击「开始游戏」。
    /// </summary>
    public class ProcedureMenu : ProcedureBase
    {
        protected override void OnEnter(ProcedureOwner procedureOwner)
        {
            base.OnEnter(procedureOwner);

            Log.Info("[ProcedureMenu] 进入主菜单");

            // TODO 打开主菜单界面：
            // GameEntry.GetComponent<UIComponent>().OpenUIForm("MenuForm", "MainMenu");
        }

        protected override void OnUpdate(ProcedureOwner procedureOwner, float elapseSeconds, float realElapseSeconds)
        {
            base.OnUpdate(procedureOwner, elapseSeconds, realElapseSeconds);

            // TODO 玩家点击「开始游戏」后切换到游戏流程：
            // ChangeState<ProcedureMain>(procedureOwner);
            //
            // 也可以在这里监听框架事件，例如：
            // GameEntry.GetComponent<EventComponent>().Subscribe(StartGameEventArgs.EventId, OnStartGame);
        }
    }
}
