-- ============================================================================
-- network/Server.lua
-- 服务端模式 — 当前项目为单机，尚未实现
--
-- 切换多人时（.project/settings.json 的 @runtime.multiplayer.enabled = true）:
--   1. 先完整阅读 engine-docs/recipes/network-game-guide.md
--   2. 在此实现服务端权威逻辑（场景复制、远程事件、玩家会话）
--   3. 同步实现 network/Client.lua
-- ============================================================================

local Logger = require("utils.Logger")

local TAG = "Server"

local Server = {}

function Server.Start()
    Logger.Error(TAG, "服务端模式尚未实现 —— 当前项目为单机")
end

function Server.Stop()
end

return Server
