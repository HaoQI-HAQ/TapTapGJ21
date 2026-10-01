-- ============================================================================
-- game/FlyCamera.lua
-- 自由漫游相机控制 — WASD 移动 + 右键转视角
--
-- ⚠️ 自赋值会丢类型 (见 lua-scripting-guide.md)，故 yaw_/pitch_ 用新变量写回
-- ============================================================================

local GameConfig = require("config.GameConfig")
local SceneManager = require("core.SceneManager")

local FlyCamera = {}

local yaw_ = 0.0
local pitch_ = 0.0

---初始化角度
function FlyCamera.Init()
    yaw_ = GameConfig.Camera.START_YAW
    pitch_ = GameConfig.Camera.START_PITCH
end

---每帧更新
---@param dt number
function FlyCamera.Update(dt)
    local cameraNode = SceneManager.GetCameraNode()
    if cameraNode == nil then
        return
    end

    local cfg = GameConfig.FlyControl

    -- 右键拖动转视角
    if input:GetMouseButtonDown(MOUSEB_RIGHT) then
        local newYaw = yaw_ + input.mouseMoveX * cfg.MOUSE_SENSITIVITY
        local newPitch = pitch_ + input.mouseMoveY * cfg.MOUSE_SENSITIVITY
        yaw_ = newYaw
        pitch_ = Clamp(newPitch, cfg.PITCH_MIN, cfg.PITCH_MAX)
        cameraNode.rotation = Quaternion(pitch_, yaw_, 0)
    end

    -- 键盘移动
    local speed = cfg.SPEED
    if input:GetKeyDown(KEY_SHIFT) then
        speed = speed * cfg.SPEED_BOOST
    end

    local step = dt * speed

    if input:GetKeyDown(KEY_W) then
        cameraNode:Translate(Vector3(0, 0, 1) * step)
    end
    if input:GetKeyDown(KEY_S) then
        cameraNode:Translate(Vector3(0, 0, -1) * step)
    end
    if input:GetKeyDown(KEY_A) then
        cameraNode:Translate(Vector3(-1, 0, 0) * step)
    end
    if input:GetKeyDown(KEY_D) then
        cameraNode:Translate(Vector3(1, 0, 0) * step)
    end
    if input:GetKeyDown(KEY_SPACE) then
        cameraNode:Translate(Vector3(0, 1, 0) * step, TS_WORLD)
    end
    if input:GetKeyDown(KEY_C) then
        cameraNode:Translate(Vector3(0, -1, 0) * step, TS_WORLD)
    end
end

---@return number
function FlyCamera.GetYaw()
    return yaw_
end

---@return number
function FlyCamera.GetPitch()
    return pitch_
end

return FlyCamera
