-- ============================================================================
-- game/BubbleManager.lua
-- 泡泡管理器 — 统一管理三种泡泡的创建、销毁与查询
--
-- 设计要点:
--   三种泡泡共享同一套底层机制，不是三套独立系统 (机制咬合)
--   炸弹泡泡有数量限制 (一次只能控制一枚)
-- ============================================================================

local GameConfig = require("config.GameConfig")
local Logger = require("utils.Logger")
local Bubble = require("game.Bubble")

local TAG = "BubbleManager"

local BubbleManager = {}

---@type Bubble[]
local bubbles_ = {}

-- 当前选中的泡泡类型
local currentType_ = "BOUNCE"

-- ============================================================
-- 类型切换
-- ============================================================

---@param typeName string  BOUNCE / FLOAT / BOMB
function BubbleManager.SetType(typeName)
    if GameConfig.Bubble.Types[typeName] == nil then
        Logger.Error(TAG, "未知泡泡类型: " .. tostring(typeName))
        return
    end
    currentType_ = typeName
    Logger.Info(TAG, "切换泡泡: " .. typeName)
end

---@return string
function BubbleManager.GetType()
    return currentType_
end

---@return table|nil
function BubbleManager.GetTypeConfig()
    return GameConfig.Bubble.Types[currentType_]
end

-- ============================================================
-- 创建 / 销毁
-- ============================================================

---检查能否创建该类型泡泡（炸弹有数量限制）
---@param typeName string
---@return boolean ok
---@return string|nil reason
function BubbleManager.CanCreate(typeName)
    local cfg = GameConfig.Bubble.Types[typeName]
    if cfg == nil then
        return false, "未知类型"
    end

    -- 炸弹泡泡: 一次只能控制一枚
    if cfg.maxActive ~= nil then
        local count = 0
        for i = 1, #bubbles_ do
            local b = bubbles_[i]
            if b.alive and b.typeName == typeName then
                count = count + 1
            end
        end
        if count >= cfg.maxActive then
            return false, string.format("%s 泡泡已达上限 (%d)", cfg.name, cfg.maxActive)
        end
    end

    return true, nil
end

---创建一个泡泡
---@param typeName string
---@param position Vector3
---@param scale number
---@return Bubble|nil
function BubbleManager.Create(typeName, position, scale)
    local ok, reason = BubbleManager.CanCreate(typeName)
    if not ok then
        Logger.Warn(TAG, "无法创建: " .. tostring(reason))
        return nil
    end

    local bubble = Bubble.new(typeName, position, scale)
    bubbles_[#bubbles_ + 1] = bubble

    Logger.Info(TAG, string.format("泡泡数: %d", #bubbles_))
    return bubble
end

---销毁指定泡泡
---@param bubble Bubble
function BubbleManager.Destroy(bubble)
    bubble:Destroy()
    for i = #bubbles_, 1, -1 do
        if bubbles_[i] == bubble then
            table.remove(bubbles_, i)
            break
        end
    end
end

---清空所有泡泡
function BubbleManager.Clear()
    for i = 1, #bubbles_ do
        bubbles_[i]:Destroy()
    end
    bubbles_ = {}
    Logger.Info(TAG, "所有泡泡已清空")
end

-- ============================================================
-- 查询
-- ============================================================

---@return Bubble[]
function BubbleManager.GetAll()
    return bubbles_
end

---@return number
function BubbleManager.Count()
    return #bubbles_
end

---查找指定类型的所有泡泡
---@param typeName string
---@return Bubble[]
function BubbleManager.FindByType(typeName)
    local result = {}
    for i = 1, #bubbles_ do
        local b = bubbles_[i]
        if b.alive and b.typeName == typeName then
            result[#result + 1] = b
        end
    end
    return result
end

-- ============================================================
-- 每帧更新
-- ============================================================

---@param dt number
function BubbleManager.Update(dt)
    -- 清理已销毁的泡泡
    for i = #bubbles_, 1, -1 do
        if not bubbles_[i].alive then
            table.remove(bubbles_, i)
        end
    end

    -- TODO: 世界规则 (分裂 / 融合 / 膨胀) 在此计算
end

return BubbleManager
