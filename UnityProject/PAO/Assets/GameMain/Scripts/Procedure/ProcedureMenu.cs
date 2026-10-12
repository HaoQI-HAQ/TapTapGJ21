using GameFramework.Procedure;
using UnityGameFramework.Runtime;
using ProcedureOwner = GameFramework.Fsm.IFsm<GameFramework.Procedure.IProcedureManager>;

namespace PAO
{
    /// <summary>
    /// GameFramework 流程脚手架。当前实际主菜单由 Menu.unity 与 StartGameBtn 驱动。
    /// 若以后切换到框架流程，需要先迁移场景按钮入口，避免两个入口并存。
    /// </summary>
    public class ProcedureMenu : ProcedureBase
    {
        protected override void OnEnter(ProcedureOwner procedureOwner)
        {
            base.OnEnter(procedureOwner);

            Log.Info("[ProcedureMenu] 进入主菜单");

            // Menu.unity is the current UI owner. Do not open a second menu here.
        }

        protected override void OnUpdate(ProcedureOwner procedureOwner, float elapseSeconds, float realElapseSeconds)
        {
            base.OnUpdate(procedureOwner, elapseSeconds, realElapseSeconds);

            // Scene buttons perform the current transition to cs.unity.
        }
    }
}
