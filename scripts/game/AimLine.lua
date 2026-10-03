-- ============================================================================
-- game/AimLine.lua
-- 瞄准引导线 — 抛物线预测 + 落点标记
--
-- 参考图三: 从主角发出的虚线，末端有落点标记
-- 蓄力时线条随泡泡大小变化
-- ============================================================================

local GameConfig = require("config.GameConfig")
local Logger = require("utils.Logger")

local TAG = "AimLine"

local AimLine = {}

---计算抛物线路径
---@param origin Vector3   起点
---@param direction Vector3 方向 (已归一化)
---@param speed number     初速度
---@return Vector3[]       路径点
function AimLine.CalculatePath(origin, direction, speed)
    local points = {}
    local cfg = GameConfig.AimLine
    local gravity = GameConfig.Player.GRAVITY

    local dt = 0.05  -- 模拟步长
    local pos = origin
    local vel = direction * speed

    for i = 1, cfg.SEGMENTS do
        -- 速度与位置积分
        local newVel = vel + Vector3(0, gravity * dt, 0)
        local newPos = pos + newVel * dt
        vel = newVel
        pos = newPos

        points[#points + 1] = pos

        -- 落地即停
        if pos.y <= 0.0 then
            break
        end
    end

    return points
end

---绘制引导线（调试用 DebugRenderer）
---@param scene Scene
---@param origin Vector3
---@param direction Vector3
---@param speed number
---@param color Color
function AimLine.Draw(scene, origin, direction, speed, color)
    ---@type boolean
    local enabled = GameConfig.AimLine.ENABLED
    if not enabled then
        return
    end

    local debugRenderer = scene:GetComponent("DebugRenderer")
    if debugRenderer == nil then
        return
    end

    local points = AimLine.CalculatePath(origin, direction, speed)

    -- 画虚线
    for i = 1, #points - 1, 2 do
        local a = points[i]
        local b = points[i + 1]
        if a ~= nil and b ~= nil then
            debugRenderer:AddLine(a, b, color, false)
        end
    end

    -- 落点标记
    local endPoint = points[#points]
    if endPoint ~= nil then
        debugRenderer:AddLine(
            endPoint + Vector3(-0.3, 0, 0),
            endPoint + Vector3(0.3, 0, 0),
            color, false)
        debugRenderer:AddLine(
            endPoint + Vector3(0, 0, -0.3),
            endPoint + Vector3(0, 0, 0.3),
            color, false)
    end
end

---计算落点（供释放时使用）
---@param origin Vector3
---@param direction Vector3
---@param speed number
---@return Vector3
function AimLine.GetLandingPoint(origin, direction, speed)
    local points = AimLine.CalculatePath(origin, direction, speed)
    local landing = points[#points]
    if landing ~= nil then
        return landing
    end
    return origin
end

return AimLine
