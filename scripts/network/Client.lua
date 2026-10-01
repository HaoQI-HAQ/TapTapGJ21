-- ============================================================================
-- network/Client.lua
-- 联网客户端模式 — 当前项目为单机，尚未实现
--
-- 切换多人时（.project/settings.json 的 @runtime.multiplayer.enabled = true）:
--   1. 先完整阅读 engine-docs/recipes/network-game-guide.md
--   2. 在此实现客户端逻辑（输入上报、状态插值、远程事件处理）
--   3. 确保 network/Server.lua 已实现
-- ============================================================================

local Logger = require("utils.Logger")

local TAG = "Client"

local Client = {}

function Client.Start()
    Logger.Error(TAG, "联网客户端模式尚未实现 —— 当前项目为单机")
end

function Client.Stop()
end

return Client
