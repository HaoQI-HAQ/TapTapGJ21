-- ============================================================================
-- ui/HUD.lua
-- 游戏界面 — 基于 urhox-libs/UI (Yoga Flexbox + NanoVG)
--
-- 动态控件用「保留 local 引用」模式，后续直接调方法 (见 recipes/ui.md §11)
-- ============================================================================

local UI = require("urhox-libs/UI")
local GameConfig = require("config.GameConfig")
local Logger = require("utils.Logger")

local TAG = "HUD"

local HUD = {}

---@type Label|nil
local fpsLabel_ = nil
local fpsTimer_ = 0.0
---@type Widget|nil
local uiRoot_ = nil

---初始化 UI 系统（必须在创建控件前调用）
function HUD.Init()
    UI.Init({
        fonts = {
            { family = "sans", weights = {
                normal = GameConfig.UI.FONT_NORMAL,
            } }
        },
        scale = UI.Scale.DEFAULT,
    })
    Logger.Info(TAG, "UI 系统已初始化")
end

---构建界面
function HUD.Create()
    fpsLabel_ = UI.Label {
        text = "FPS: --",
        fontSize = 12,
        fontColor = { 255, 255, 200, 200 },
        position = "absolute",
        top = 10,
        right = 10,
    }

    uiRoot_ = UI.Panel {
        id = "gameUI",
        width = "100%",
        height = "100%",
        pointerEvents = "box-none",
        children = {
            UI.Label {
                text = "WASD: Move | Mouse Right: Look | Space: Up | C: Down | Tab: Debug",
                fontSize = 12,
                fontColor = { 255, 255, 200, 200 },
                position = "absolute",
                top = 10,
                left = 0,
                right = 0,
                textAlign = "center",
            },
            fpsLabel_,
        }
    }

    UI.SetRoot(uiRoot_)
    Logger.Info(TAG, "界面构建完成")
end

---每帧更新（FPS 节流刷新，避免每帧文本测量开销）
---@param dt number
function HUD.Update(dt)
    local newTimer = fpsTimer_ + dt
    fpsTimer_ = newTimer
    if fpsLabel_ ~= nil and newTimer >= GameConfig.UI.FPS_UPDATE_INTERVAL and dt > 0 then
        fpsLabel_:SetText(string.format("FPS: %d", math.floor(1.0 / dt)))
        fpsTimer_ = newTimer - GameConfig.UI.FPS_UPDATE_INTERVAL
    end
end

---清理
function HUD.Shutdown()
    UI.Shutdown()
    fpsLabel_ = nil
    uiRoot_ = nil
    Logger.Info(TAG, "UI 已关闭")
end

return HUD
