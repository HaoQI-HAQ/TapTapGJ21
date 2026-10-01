-- ============================================================================
-- game/GameWorld.lua
-- 玩法内容 — 游戏对象的创建与每帧更新
--
-- 🎯 这是「涌现」玩法的落地区域。
--    当前为空场景，待玩法定稿后在此填充。
-- ============================================================================

local Logger = require("utils.Logger")
local SceneManager = require("core.SceneManager")

local TAG = "GameWorld"

local GameWorld = {}

-- ============================================================
-- 创建游戏对象
-- ============================================================
-- 参考写法：
--   local scene = SceneManager.GetScene()
--   local node = scene:CreateChild("Name")
--   node.position = Vector3(0, 0, 0)
--   local model = node:CreateComponent("StaticModel")
--   model:SetModel(cache:GetResource("Model", "Models/Box.mdl"))
--
-- 程序化材质只用这三个 Technique（见 AGENTS.md 规则 #9.4）：
--   "Techniques/PBR/PBRNoTexture.xml"       不透明
--   "Techniques/PBR/PBRNoTextureAlpha.xml"  透明
--   "Techniques/NoTextureUnlit.xml"         无光照
--
-- ⚠️ 内置模型尺寸不要猜，用 boundingBox 获取（规则 #9.2）：
--   local size = model.boundingBox.size
-- ============================================================

function GameWorld.Create()
    local scene = SceneManager.GetScene()
    if scene == nil then
        Logger.Error(TAG, "Create 调用时场景尚未创建")
        return
    end

    -- TODO: 在此创建游戏对象

    Logger.Key(TAG, "玩法内容创建完成（当前为空）")
end

-- ============================================================
-- 每帧更新
-- ============================================================
---@param dt number
function GameWorld.Update(dt)
    -- TODO: 在此填充每帧逻辑
end

-- ============================================================
-- 清理
-- ============================================================
function GameWorld.Clear()
    -- TODO: 在此释放玩法相关的对象
    Logger.Info(TAG, "玩法内容已清空")
end

return GameWorld
