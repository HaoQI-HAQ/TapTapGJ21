-- ============================================================================
-- utils/Logger.lua
-- 统一日志输出 — 开发期多打日志，稳定后通过 GameConfig.Debug.VERBOSE 一键关闭
-- ============================================================================

local GameConfig = require("config.GameConfig")

local Logger = {}

local function isVerbose()
    return GameConfig.Debug ~= nil and GameConfig.Debug.VERBOSE == true
end

---普通日志（受 VERBOSE 开关控制）
---@param tag string 模块名，如 "Scene"
---@param ... any
function Logger.Info(tag, ...)
    if not isVerbose() then
        return
    end
    print(string.format("[%s] %s", tag, table.concat({ ... }, " ")))
end

---重要节点日志（始终输出）
---@param tag string
---@param ... any
function Logger.Key(tag, ...)
    print(string.format("[%s] %s", tag, table.concat({ ... }, " ")))
end

---警告（始终输出）
---@param tag string
---@param ... any
function Logger.Warn(tag, ...)
    print(string.format("[WARN][%s] %s", tag, table.concat({ ... }, " ")))
end

---错误（始终输出）
---@param tag string
---@param ... any
function Logger.Error(tag, ...)
    print(string.format("[ERROR][%s] %s", tag, table.concat({ ... }, " ")))
end

return Logger
