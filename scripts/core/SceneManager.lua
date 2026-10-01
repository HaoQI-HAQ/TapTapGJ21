-- ============================================================================
-- core/SceneManager.lua
-- 场景与相机管理 — 负责创建 Scene、光照、相机和视口
--
-- ⚠️ 生命周期: Scene 必须由模块级变量持有，否则会被 GC 回收导致闪退
--    (见 lua-scripting-guide.md「对象生命周期陷阱」)
-- ============================================================================

local GameConfig = require("config.GameConfig")
local Logger = require("utils.Logger")

local TAG = "SceneManager"

local SceneManager = {}

-- ⚠️ 模块级持有，防止 GC
---@type Scene|nil
local scene_ = nil
---@type Node|nil
local cameraNode_ = nil
---@type Camera|nil
local camera_ = nil

---创建场景（Octree + DebugRenderer + 光照预设）
---@return Scene
function SceneManager.Create()
    scene_ = Scene()

    scene_:CreateComponent("Octree")
    scene_:CreateComponent("DebugRenderer")

    -- 光照预设
    local lightGroupFile = cache:GetResource("XMLFile", GameConfig.Lighting.PRESET)
    if lightGroupFile == nil then
        Logger.Error(TAG, "光照预设加载失败: " .. GameConfig.Lighting.PRESET)
    else
        local lightGroup = scene_:CreateChild("LightGroup")
        lightGroup:LoadXML(lightGroupFile:GetRoot())
        Logger.Info(TAG, "光照预设已加载: " .. GameConfig.Lighting.PRESET)
    end

    Logger.Key(TAG, "场景创建完成")
    return scene_
end

---创建相机并设置视口
function SceneManager.SetupCamera()
    if scene_ == nil then
        Logger.Error(TAG, "SetupCamera 调用时场景尚未创建")
        return
    end

    cameraNode_ = scene_:CreateChild("Camera")
    local start = GameConfig.Camera.START_POSITION
    cameraNode_.position = Vector3(start.x, start.y, start.z)

    camera_ = cameraNode_:CreateComponent("Camera")
    camera_.nearClip = GameConfig.Camera.NEAR_CLIP
    camera_.farClip = GameConfig.Camera.FAR_CLIP
    camera_.fov = GameConfig.Camera.FOV

    local viewport = Viewport:new(scene_, camera_)
    renderer:SetViewport(0, viewport)
    renderer.hdrRendering = GameConfig.Rendering.HDR

    Logger.Info(TAG, string.format("相机就位 pos=(%.1f, %.1f, %.1f)",
        start.x, start.y, start.z))
end

---@return Scene|nil
function SceneManager.GetScene()
    return scene_
end

---@return Node|nil
function SceneManager.GetCameraNode()
    return cameraNode_
end

---@return Camera|nil
function SceneManager.GetCamera()
    return camera_
end

---销毁场景（切场景时用）
function SceneManager.Clear()
    scene_ = nil
    cameraNode_ = nil
    camera_ = nil
    Logger.Info(TAG, "场景已清空")
end

return SceneManager
