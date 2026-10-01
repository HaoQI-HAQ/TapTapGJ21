-- ============================================================================
-- TapTapGJ21 — 空 3D 场景项目
-- 主题: 涌现 (Emergence)
--
-- 基于 templates/scaffold-3d-scene.lua，已移除全部示例内容（地面 / 方块 / 球体）。
-- 当前状态: 空场景 + 自由漫游相机，等待填充玩法。
--
-- 操作: WASD 移动 | 鼠标右键 转视角 | Space 上升 | C 下降 | Shift 加速 | Tab 调试
-- ============================================================================

-- 引入 UI 系统 (Yoga Flexbox + NanoVG)
local UI = require("urhox-libs/UI")

-- ============================================================================
-- 1. 全局变量
-- ============================================================================
---@type Scene|nil
local scene_ = nil
---@type Node|nil
local cameraNode_ = nil
local yaw_ = 0.0
local pitch_ = 0.0
local debugDraw_ = false

---@type Widget|nil
local fpsLabel_ = nil
local fpsTimer_ = 0

local CONFIG = {
    Title = "TapTapGJ21",
    CameraSpeed = 20.0,
    MouseSensitivity = 0.1,
    CameraNearClip = 0.1,
    CameraFarClip = 1000.0,
}

-- ============================================================================
-- 2. 生命周期
-- ============================================================================

function Start()
    graphics.windowTitle = CONFIG.Title

    InitUI()
    CreateScene()
    SetupCameraAndViewport()
    CreateGameContent()
    CreateUI()
    SubscribeToEvents()

    print("=== Scene Started: " .. CONFIG.Title .. " ===")
end

function Stop()
    UI.Shutdown()
end

-- ============================================================================
-- 3. 场景与相机
-- ============================================================================

function CreateScene()
    scene_ = Scene()

    scene_:CreateComponent("Octree")
    scene_:CreateComponent("DebugRenderer")

    -- 预设光照环境（定向光 + 环境光 + 雾效）
    local lightGroupFile = cache:GetResource("XMLFile", "LightGroup/Daytime.xml")
    local lightGroup = scene_:CreateChild("LightGroup")
    lightGroup:LoadXML(lightGroupFile:GetRoot())

    print("✅ Scene created (empty)")
end

function SetupCameraAndViewport()
    cameraNode_ = scene_:CreateChild("Camera")
    cameraNode_.position = Vector3(0, 2, -10)

    local camera = cameraNode_:CreateComponent("Camera")
    camera.nearClip = CONFIG.CameraNearClip
    camera.farClip = CONFIG.CameraFarClip
    camera.fov = 75.0

    local viewport = Viewport:new(scene_, camera)
    renderer:SetViewport(0, viewport)

    renderer.hdrRendering = true

    yaw_ = 0.0
    pitch_ = 0.0
end

-- ============================================================================
-- 4. 游戏内容（待填充）
-- ============================================================================

function CreateGameContent()
    -- 空场景：此处留待填充玩法内容。
    --
    -- 创建物体的参考写法：
    --   local node = scene_:CreateChild("Name")
    --   node.position = Vector3(0, 0, 0)
    --   local model = node:CreateComponent("StaticModel")
    --   model:SetModel(cache:GetResource("Model", "Models/Box.mdl"))
    --
    -- 程序化材质只用 PBRNoTexture 系列：
    --   "Techniques/PBR/PBRNoTexture.xml"       (不透明)
    --   "Techniques/PBR/PBRNoTextureAlpha.xml"  (透明)
    --   "Techniques/NoTextureUnlit.xml"         (无光照)
end

-- ============================================================================
-- 5. UI
-- ============================================================================

local uiRoot_ = nil

function InitUI()
    UI.Init({
        fonts = {
            { family = "sans", weights = {
                normal = "Fonts/MiSans-Regular.ttf",
            } }
        },
        scale = UI.Scale.DEFAULT,
    })
end

function CreateUI()
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
end

-- ============================================================================
-- 6. 事件
-- ============================================================================

function SubscribeToEvents()
    SubscribeToEvent("Update", "HandleUpdate")
    SubscribeToEvent("PostRenderUpdate", "HandlePostRenderUpdate")
end

---@param eventType string
---@param eventData UpdateEventData
function HandleUpdate(eventType, eventData)
    local dt = eventData["TimeStep"]:GetFloat()

    HandleCameraMovement(dt)
    UpdateGameLogic(dt)

    if input:GetKeyPress(KEY_TAB) then
        debugDraw_ = not debugDraw_
    end

    fpsTimer_ = fpsTimer_ + dt
    if fpsLabel_ ~= nil and fpsTimer_ >= 0.25 and dt > 0 then
        fpsLabel_:SetText(string.format("FPS: %d", math.floor(1.0 / dt)))
        fpsTimer_ = fpsTimer_ - 0.25
    end
end

---@param eventType string
---@param eventData PostRenderUpdateEventData
function HandlePostRenderUpdate(eventType, eventData)
    if not debugDraw_ then
        return
    end

    local debugRenderer = scene_:GetComponent("DebugRenderer")
    if debugRenderer == nil then
        return
    end

    -- 世界坐标轴
    debugRenderer:AddLine(Vector3(0, 0, 0), Vector3(5, 0, 0), Color(1, 0, 0), false)
    debugRenderer:AddLine(Vector3(0, 0, 0), Vector3(0, 5, 0), Color(0, 1, 0), false)
    debugRenderer:AddLine(Vector3(0, 0, 0), Vector3(0, 0, 5), Color(0, 0, 1), false)
end

-- ============================================================================
-- 7. 游戏逻辑（待填充）
-- ============================================================================

function UpdateGameLogic(dt)
    -- 空场景：此处留待填充每帧逻辑。
end

-- ============================================================================
-- 8. 相机控制
-- ============================================================================

function HandleCameraMovement(dt)
    -- 右键拖动转视角
    if input:GetMouseButtonDown(MOUSEB_RIGHT) then
        local newYaw = yaw_ + input.mouseMoveX * CONFIG.MouseSensitivity
        local newPitch = pitch_ + input.mouseMoveY * CONFIG.MouseSensitivity
        yaw_ = newYaw
        pitch_ = Clamp(newPitch, -90.0, 90.0)

        cameraNode_.rotation = Quaternion(pitch_, yaw_, 0)
    end

    local moveSpeed = CONFIG.CameraSpeed
    if input:GetKeyDown(KEY_SHIFT) then
        moveSpeed = moveSpeed * 2.0
    end

    if input:GetKeyDown(KEY_W) then
        cameraNode_:Translate(Vector3(0, 0, 1) * dt * moveSpeed)
    end
    if input:GetKeyDown(KEY_S) then
        cameraNode_:Translate(Vector3(0, 0, -1) * dt * moveSpeed)
    end
    if input:GetKeyDown(KEY_A) then
        cameraNode_:Translate(Vector3(-1, 0, 0) * dt * moveSpeed)
    end
    if input:GetKeyDown(KEY_D) then
        cameraNode_:Translate(Vector3(1, 0, 0) * dt * moveSpeed)
    end
    if input:GetKeyDown(KEY_SPACE) then
        cameraNode_:Translate(Vector3(0, 1, 0) * dt * moveSpeed, TS_WORLD)
    end
    if input:GetKeyDown(KEY_C) then
        cameraNode_:Translate(Vector3(0, -1, 0) * dt * moveSpeed, TS_WORLD)
    end
end
