-- ============================================================================
-- game/Bubble.lua
-- 泡泡实体 — 单个泡泡的数据与表现
--
-- 三种类型: BOUNCE(弹力) / FLOAT(浮力) / BOMB(炸弹)
-- 大小由蓄力决定，大小决定强度
-- ============================================================================

local GameConfig = require("config.GameConfig")
local Logger = require("utils.Logger")

local TAG = "Bubble"

---@class Bubble
---@field node Node
---@field typeName string        泡泡类型 BOUNCE / FLOAT / BOMB
---@field scale number           泡泡大小 (由蓄力决定)
---@field position Vector3
---@field alive boolean
local Bubble = {}
Bubble.__index = Bubble

-- ============================================================
-- 构造
-- ============================================================

---@param typeName string  BOUNCE / FLOAT / BOMB
---@param position Vector3
---@param scale number  蓄力决定的大小
---@return Bubble
function Bubble.new(typeName, position, scale)
    local self = setmetatable({}, Bubble)
    self:init(typeName, position, scale)
    return self
end

---@param typeName string
---@param position Vector3
---@param scale number
function Bubble:init(typeName, position, scale)
    self.typeName = typeName
    self.scale = scale
    self.position = position
    self.alive = true
    self.node = nil

    Logger.Info(TAG, string.format("创建 %s 泡泡 scale=%.2f", typeName, scale))
end

-- ============================================================
-- 配置查询
-- ============================================================

---获取当前泡泡的类型配置
---@return table|nil
function Bubble:GetTypeConfig()
    return GameConfig.Bubble.Types[self.typeName]
end

---强度系数: 大小 → 强度 (归一化到 0~1)
---@return number
function Bubble:GetStrength()
    local cfg = GameConfig.Bubble
    local t = (self.scale - cfg.MIN_SCALE) / (cfg.MAX_SCALE - cfg.MIN_SCALE)
    return Clamp(t, 0.0, 1.0)
end

---是否可以发射
---@return boolean
function Bubble:CanLaunch()
    local cfg = self:GetTypeConfig()
    return cfg ~= nil and cfg.canLaunch == true
end

-- ============================================================
-- 生命周期
-- ============================================================

function Bubble:Destroy()
    if self.node ~= nil then
        self.node:Dispose()
        self.node = nil
    end
    self.alive = false
    Logger.Info(TAG, "泡泡销毁: " .. tostring(self.typeName))
end

return Bubble
