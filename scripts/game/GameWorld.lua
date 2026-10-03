-- ============================================================================
-- game/GameWorld.lua
-- 玩法内容 — 组装玩家、泡泡、关卡
--
-- 🎯 这是「泡涌」玩法的落地区域。
-- ============================================================================

local GameConfig = require("config.GameConfig")
local Logger = require("utils.Logger")
local SceneManager = require("core.SceneManager")

local PlayerController = require("game.PlayerController")
local BubbleManager = require("game.BubbleManager")
local BubbleLauncher = require("game.BubbleLauncher")

local TAG = "GameWorld"

local GameWorld = {}

-- ============================================================
-- 创建
-- ============================================================

function GameWorld.Create()
    local scene = SceneManager.GetScene()
    if scene == nil then
        Logger.Error(TAG, "Create 调用时场景尚未创建")
        return
    end

    -- 1. 主角
    local cameraNode = SceneManager.GetCameraNode()
    if cameraNode == nil then
        Logger.Error(TAG, "相机节点不存在，无法创建主角")
        return
    end
    PlayerController.Init(scene, cameraNode)

    -- 2. 临时地面（供测试，后续换成关卡内容）
    GameWorld.CreateTempGround(scene)

    Logger.Key(TAG, "玩法内容创建完成")
end

---临时地面（灰盒测试用）
---@param scene Scene
function GameWorld.CreateTempGround(scene)
    local groundNode = scene:CreateChild("Ground")
    groundNode.position = Vector3(0, -0.5, 0)
    groundNode.scale = Vector3(60, 1, 60)

    local model = groundNode:CreateComponent("StaticModel")
    model:SetModel(cache:GetResource("Model", "Models/Box.mdl"))

    local mat = Material:new()
    mat:SetTechnique(0, cache:GetResource("Technique", "Techniques/PBR/PBRNoTexture.xml"))
    mat:SetShaderParameter("MatDiffColor", Variant(Color(0.25, 0.28, 0.35, 1.0)))
    mat:SetShaderParameter("MatSpecColor", Variant(Color(0.3, 0.3, 0.3, 1.0)))
    mat:SetShaderParameter("Metallic", Variant(0.0))
    mat:SetShaderParameter("Roughness", Variant(0.9))
    model:SetMaterial(mat)
    model.castShadows = false

    Logger.Info(TAG, "临时地面已创建")
end

-- ============================================================
-- 每帧更新
-- ============================================================

---@param dt number
function GameWorld.Update(dt)
    -- 主角
    PlayerController.Update(dt)

    -- 泡泡世界规则
    BubbleManager.Update(dt)

    -- 蓄力释放
    GameWorld.HandleBubbleInput(dt)

    -- 切换泡泡类型
    GameWorld.HandleTypeSwitch()
end

---泡泡输入处理
---@param dt number
function GameWorld.HandleBubbleInput(dt)
    local playerPos = PlayerController.GetPosition()
    if playerPos == nil then
        return
    end

    -- 瞄准点 = 主角前方（后续接入鼠标射线检测）
    local aimPos = playerPos + Vector3(0, 0, 5)

    BubbleLauncher.Update(dt, aimPos)
end

---切换泡泡类型 (1/2/3 键)
function GameWorld.HandleTypeSwitch()
    if input:GetKeyPress(KEY_1) then
        BubbleManager.SetType("BOUNCE")
        BubbleLauncher.Reset()
    elseif input:GetKeyPress(KEY_2) then
        BubbleManager.SetType("FLOAT")
        BubbleLauncher.Reset()
    elseif input:GetKeyPress(KEY_3) then
        BubbleManager.SetType("BOMB")
        BubbleLauncher.Reset()
    end
end

-- ============================================================
-- 清理
-- ============================================================

function GameWorld.Clear()
    BubbleManager.Clear()
    PlayerController.Reset()
    Logger.Info(TAG, "玩法内容已清空")
end

return GameWorld
