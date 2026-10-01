-- ============================================================================
-- main.lua — 游戏入口
-- TapTapGJ21 ｜ 主题: 涌现 (Emergence)
--
-- 按运行模式分发到对应模块（官方范式，见 examples/07-minecraft-voxel-world）:
--   IsServerMode()  → network/Server      (服务端)
--   IsNetworkMode() → network/Client      (联网客户端)
--   否则             → network/Standalone  (单机)
--
-- 当前为单机项目，仅 Standalone 已实现。
-- ============================================================================

---@class GameModule
---@field Start fun()
---@field Stop fun()|nil

---@type GameModule|nil
local Module = nil

function Start()
    ---@type GameModule
    local module

    if IsServerMode() then
        print("[Main] SERVER mode")
        module = require("network.Server")
    elseif IsNetworkMode() then
        print("[Main] CLIENT mode")
        module = require("network.Client")
    else
        print("[Main] STANDALONE mode")
        module = require("network.Standalone")
    end

    Module = module
    module.Start()
end

function Stop()
    if Module ~= nil and Module.Stop ~= nil then
        Module.Stop()
    end
end
