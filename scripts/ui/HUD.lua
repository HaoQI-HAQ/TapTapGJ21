-- ============================================================================
-- ui/HUD.lua
-- 游戏界面 — 极简 HUD (见策划案 11.2)
--
-- 显示: FPS / 当前泡泡 / 蓄力状态 / 炸弹数
-- ============================================================================

local UI = require("urhox-libs/UI")
local GameConfig = require("config.GameConfig")
local Logger = require("utils.Logger")

local BubbleManager = require("game.BubbleManager")
local BubbleLauncher = require("game.BubbleLauncher")

local TAG = "HUD"

local HUD = {}

---@type Label|nil
local fpsLabel_ = nil
---@type Label|nil
local typeLabel_ = nil
---@type Label|nil
local chargeLabel_ = nil
---@type Widget|nil
local uiRoot_ = nil

local fpsTimer_ = 0.0

-- ============================================================
-- 初始化
-- ============================================================

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

function HUD.Create()
    fpsLabel_ = UI.Label {
        text = "FPS: --",
        fontSize = 12,
        fontColor = { 255, 255, 200, 200 },
        position = "absolute",
        top = 10,
        right = 10,
    }

    typeLabel_ = UI.Label {
        text = "泡泡: 弹力",
        fontSize = 16,
        fontColor = { 200, 230, 255, 230 },
        position = "absolute",
        top = 10,
        left = 10,
    }

    chargeLabel_ = UI.Label {
        text = "",
        fontSize = 14,
        fontColor = { 255, 220, 120, 240 },
        position = "absolute",
        top = 36,
        left = 10,
    }

    uiRoot_ = UI.Panel {
        id = "gameUI",
        width = "100%",
        height = "100%",
        pointerEvents = "box-none",
        children = {
            UI.Label {
                text = "WASD 移动 | Space 跳跃 | 左键长按蓄力 | 右键释放 | 1/2/3 切换泡泡",
                fontSize = 12,
                fontColor = { 255, 255, 200, 200 },
                position = "absolute",
                top = 10,
                left = 0,
                right = 0,
                textAlign = "center",
            },
            typeLabel_,
            chargeLabel_,
            fpsLabel_,
        }
    }

    UI.SetRoot(uiRoot_)
    Logger.Info(TAG, "界面构建完成")
end

-- ============================================================
-- 每帧更新
-- ============================================================

---@param dt number
function HUD.Update(dt)
    fpsTimer_ = fpsTimer_ + dt
    if fpsTimer_ < GameConfig.UI.FPS_UPDATE_INTERVAL or dt <= 0 then
        return
    end
    fpsTimer_ = fpsTimer_ - GameConfig.UI.FPS_UPDATE_INTERVAL

    if fpsLabel_ ~= nil then
        fpsLabel_:SetText(string.format("FPS: %d", math.floor(1.0 / dt)))
    end

    -- 当前泡泡类型
    if typeLabel_ ~= nil then
        local cfg = BubbleManager.GetTypeConfig()
        if cfg ~= nil then
            local count = BubbleManager.Count()
            typeLabel_:SetText(string.format("泡泡: %s  [%d]", cfg.name, count))
        end
    end

    -- 蓄力状态
    if chargeLabel_ ~= nil then
        if BubbleLauncher.IsCharging() then
            local ratio = BubbleLauncher.GetChargeRatio()
            local bars = math.floor(ratio * 10)
            local bar = string.rep("█", bars) .. string.rep("░", 10 - bars)
            chargeLabel_:SetText(string.format("蓄力 %s %d%%", bar, math.floor(ratio * 100)))
        else
            chargeLabel_:SetText("")
        end
    end
end

-- ============================================================
-- 清理
-- ============================================================

function HUD.Shutdown()
    UI.Shutdown()
    fpsLabel_ = nil
    typeLabel_ = nil
    chargeLabel_ = nil
    uiRoot_ = nil
    Logger.Info(TAG, "UI 已关闭")
end

return HUD
