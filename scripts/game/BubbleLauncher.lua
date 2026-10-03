-- ============================================================================
-- game/BubbleLauncher.lua
-- 蓄力释放系统 — 左键长按蓄力 → 泡泡变大 → 右键释放
--
-- 核心设计: 一个输入维度(长按时长) 同时控制三件事
--   长按越久 → 泡泡越大 → 能力越强
--
-- ⚠️ 自赋值会丢类型 (见 lua-scripting-guide.md)，用新变量写回
-- ============================================================================

local GameConfig = require("config.GameConfig")
local Logger = require("utils.Logger")
local BubbleManager = require("game.BubbleManager")

local TAG = "BubbleLauncher"

local BubbleLauncher = {}

-- 蓄力状态
local charging_ = false
local chargeTime_ = 0.0

-- ============================================================
-- 蓄力
-- ============================================================

---当前蓄力进度 (0~1)
---@return number
function BubbleLauncher.GetChargeRatio()
    local t = chargeTime_ / GameConfig.Bubble.CHARGE_TIME
    return Clamp(t, 0.0, 1.0)
end

---当前蓄力对应的泡泡大小
---@return number
function BubbleLauncher.GetCurrentScale()
    local cfg = GameConfig.Bubble
    local ratio = BubbleLauncher.GetChargeRatio()
    return cfg.MIN_SCALE + (cfg.MAX_SCALE - cfg.MIN_SCALE) * ratio
end

---@return boolean
function BubbleLauncher.IsCharging()
    return charging_
end

-- ============================================================
-- 每帧更新
-- ============================================================

---@param dt number
---@param aimPosition Vector3  瞄准落点
function BubbleLauncher.Update(dt, aimPosition)
    -- 左键按下 → 开始/继续蓄力
    if input:GetMouseButtonDown(MOUSEB_LEFT) then
        if not charging_ then
            charging_ = true
            chargeTime_ = 0.0
            Logger.Info(TAG, "开始蓄力")
        else
            local newTime = chargeTime_ + dt
            chargeTime_ = math.min(newTime, GameConfig.Bubble.CHARGE_TIME)
        end
    else
        -- 左键松开但没按右键 → 取消蓄力
        if charging_ then
            charging_ = false
            chargeTime_ = 0.0
            Logger.Info(TAG, "取消蓄力")
        end
    end

    -- 右键 → 释放
    if charging_ and input:GetMouseButtonPress(MOUSEB_RIGHT) then
        BubbleLauncher.Release(aimPosition)
    end
end

-- ============================================================
-- 释放
-- ============================================================

---@param aimPosition Vector3
function BubbleLauncher.Release(aimPosition)
    local typeName = BubbleManager.GetType()
    local scale = BubbleLauncher.GetCurrentScale()
    local cfg = BubbleManager.GetTypeConfig()

    local ok, reason = BubbleManager.CanCreate(typeName)
    if not ok then
        Logger.Warn(TAG, "释放失败: " .. tostring(reason))
        charging_ = false
        chargeTime_ = 0.0
        return
    end

    -- 创建泡泡（放在瞄准点）
    BubbleManager.Create(typeName, aimPosition, scale)

    Logger.Key(TAG, string.format("释放 %s 泡泡 scale=%.2f strength=%.2f",
        cfg.name, scale, (scale - GameConfig.Bubble.MIN_SCALE) /
        (GameConfig.Bubble.MAX_SCALE - GameConfig.Bubble.MIN_SCALE)))

    -- 重置蓄力
    charging_ = false
    chargeTime_ = 0.0
end

---重置（切换泡泡时调用）
function BubbleLauncher.Reset()
    charging_ = false
    chargeTime_ = 0.0
end

return BubbleLauncher
