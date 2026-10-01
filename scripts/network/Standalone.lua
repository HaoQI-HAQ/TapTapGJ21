-- ============================================================================
-- network/Standalone.lua
-- 单机模式主逻辑 — 组装各模块并驱动主循环
--
-- 模块分层:
--   config/ 配置  |  core/ 核心系统  |  game/ 玩法  |  ui/ 界面  |  utils/ 工具
-- ============================================================================

local GameConfig = require("config.GameConfig")
local Logger = require("utils.Logger")

local SceneManager = require("core.SceneManager")
local FlyCamera = require("game.FlyCamera")
local GameWorld = require("game.GameWorld")
local HUD = require("ui.HUD")

local TAG = "Standalone"

local Standalone = {}

local debugDraw_ = false

-- ============================================================
-- 生命周期
-- ============================================================

function Standalone.Start()
    Logger.Key(TAG, "启动: " .. GameConfig.Title)

    -- 1. UI 系统（需先于界面构建）
    HUD.Init()

    -- 2. 场景与相机
    SceneManager.Create()
    SceneManager.SetupCamera()

    -- 3. 玩法内容
    GameWorld.Create()

    -- 4. 界面
    HUD.Create()

    -- 5. 控制器
    FlyCamera.Init()

    -- 6. 事件
    Standalone.SubscribeEvents()

    debugDraw_ = GameConfig.Debug.DEBUG_DRAW

    Logger.Key(TAG, "启动完成")
end

function Standalone.Stop()
    Logger.Key(TAG, "关闭")

    GameWorld.Clear()
    HUD.Shutdown()
    SceneManager.Clear()
end

-- ============================================================
-- 事件
-- ============================================================

function Standalone.SubscribeEvents()
    SubscribeToEvent("Update", "HandleUpdate")
    SubscribeToEvent("PostRenderUpdate", "HandlePostRenderUpdate")
end

---@param eventType string
---@param eventData UpdateEventData
function HandleUpdate(eventType, eventData)
    local dt = eventData["TimeStep"]:GetFloat()

    FlyCamera.Update(dt)
    GameWorld.Update(dt)
    HUD.Update(dt)

    if input:GetKeyPress(KEY_TAB) then
        debugDraw_ = not debugDraw_
        Logger.Info(TAG, "调试绘制: " .. tostring(debugDraw_))
    end
end

---@param eventType string
---@param eventData PostRenderUpdateEventData
function HandlePostRenderUpdate(eventType, eventData)
    if not debugDraw_ then
        return
    end

    local scene = SceneManager.GetScene()
    if scene == nil then
        return
    end

    local debugRenderer = scene:GetComponent("DebugRenderer")
    if debugRenderer == nil then
        return
    end

    -- 世界坐标轴: X 红 / Y 绿 / Z 蓝
    debugRenderer:AddLine(Vector3(0, 0, 0), Vector3(5, 0, 0), Color(1, 0, 0), false)
    debugRenderer:AddLine(Vector3(0, 0, 0), Vector3(0, 5, 0), Color(0, 1, 0), false)
    debugRenderer:AddLine(Vector3(0, 0, 0), Vector3(0, 0, 5), Color(0, 0, 1), false)
end

return Standalone
