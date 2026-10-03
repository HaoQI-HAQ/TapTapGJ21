-- ============================================================================
-- game/PlayerController.lua
-- 3D 主角控制器 — 移动 / 跳跃 / 第三人称相机
--
-- ⚠️ 自赋值会丢类型 (见 lua-scripting-guide.md)，位置用新变量写回
-- ============================================================================

local GameConfig = require("config.GameConfig")
local Logger = require("utils.Logger")

local TAG = "PlayerController"

local PlayerController = {}

---@type Node|nil
local playerNode_ = nil
---@type Node|nil
local cameraNode_ = nil

local yaw_ = 0.0
local pitch_ = 0.0
local velocityY_ = 0.0
local onGround_ = true

-- ============================================================
-- 初始化
-- ============================================================

---@param scene Scene
---@param cameraNode Node
function PlayerController.Init(scene, cameraNode)
    cameraNode_ = cameraNode

    playerNode_ = scene:CreateChild("Player")
    playerNode_.position = Vector3(0, 2, 0)

    -- TODO: 接入模型与碰撞体
    -- local body = playerNode_:CreateComponent("RigidBody")
    -- local shape = playerNode_:CreateComponent("CollisionShape")
    -- shape:SetCapsule(GameConfig.Player.RADIUS, GameConfig.Player.HEIGHT)

    Logger.Key(TAG, "主角已创建")
end

---@return Node|nil
function PlayerController.GetNode()
    return playerNode_
end

---@return Vector3|nil
function PlayerController.GetPosition()
    if playerNode_ == nil then
        return nil
    end
    return playerNode_.position
end

-- ============================================================
-- 每帧更新
-- ============================================================

---@param dt number
function PlayerController.Update(dt)
    if playerNode_ == nil then
        return
    end

    PlayerController.HandleMove(dt)
    PlayerController.HandleCamera(dt)
end

---移动
---@param dt number
function PlayerController.HandleMove(dt)
    local node = playerNode_
    if node == nil then
        return
    end

    local cfg = GameConfig.Player

    -- 水平移动 (相对相机朝向)
    local moveDir = Vector3(0, 0, 0)
    if input:GetKeyDown(KEY_W) then
        moveDir = moveDir + Vector3(0, 0, 1)
    end
    if input:GetKeyDown(KEY_S) then
        moveDir = moveDir + Vector3(0, 0, -1)
    end
    if input:GetKeyDown(KEY_A) then
        moveDir = moveDir + Vector3(-1, 0, 0)
    end
    if input:GetKeyDown(KEY_D) then
        moveDir = moveDir + Vector3(1, 0, 0)
    end

    -- 按相机 yaw 旋转移动方向
    if moveDir:Length() > 0.01 then
        local rotated = Quaternion(yaw_, Vector3.UP) * moveDir:Normalized()
        local newPos = node.position + rotated * cfg.MOVE_SPEED * dt
        node.position = newPos
    end

    -- 跳跃
    if input:GetKeyPress(KEY_SPACE) and onGround_ then
        velocityY_ = cfg.JUMP_SPEED
        onGround_ = false
    end

    -- 重力
    if not onGround_ then
        velocityY_ = velocityY_ + cfg.GRAVITY * dt
        local newPos = node.position + Vector3(0, velocityY_ * dt, 0)
        node.position = newPos

        -- TODO: 落地检测（接入物理后改为碰撞检测）
        if newPos.y <= 0.0 then
            node.position = Vector3(newPos.x, 0.0, newPos.z)
            velocityY_ = 0.0
            onGround_ = true
        end
    end
end

---第三人称相机
---@param dt number
function PlayerController.HandleCamera(dt)
    local node = playerNode_
    local cam = cameraNode_
    if node == nil or cam == nil then
        return
    end

    local cfg = GameConfig.Camera

    -- 鼠标控制视角
    if input:GetMouseButtonDown(MOUSEB_RIGHT) then
        local newYaw = yaw_ + input.mouseMoveX * cfg.MOUSE_SENSITIVITY
        local newPitch = pitch_ + input.mouseMoveY * cfg.MOUSE_SENSITIVITY
        yaw_ = newYaw
        pitch_ = Clamp(newPitch, cfg.PITCH_MIN, cfg.PITCH_MAX)
    end

    -- 相机跟随
    local playerPos = node.position
    local offset = Vector3(cfg.OFFSET.x, cfg.OFFSET.y, cfg.OFFSET.z)
    local rotated = Quaternion(yaw_, Vector3.UP) * offset
    local camPos = playerPos + rotated
    cam.position = camPos

    local lookAt = playerPos + Vector3(0, cfg.LOOK_AT_HEIGHT, 0)
    cam:LookAt(lookAt)
end

---@return number
function PlayerController.GetYaw()
    return yaw_
end

---重置
function PlayerController.Reset()
    yaw_ = 0.0
    pitch_ = 0.0
    velocityY_ = 0.0
    onGround_ = true
end

return PlayerController
