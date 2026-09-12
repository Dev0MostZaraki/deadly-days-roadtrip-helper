-- Deadly Days: Roadtrip Companion Bridge
-- Read-only UE4SS telemetry probe. It does not change game values, invoke gameplay functions,
-- automate input, or write to Deadly Days save/config files.
--
-- Output: %LOCALAPPDATA%\DeadlyDaysCompanion\Bridge\telemetry.jsonl
-- The external companion creates this directory before the mod needs it.

local MOD_VERSION = "0.1.0"
local DISCOVERY_LIMIT = 1800
local discovery_running = false

local local_app_data = os.getenv("LOCALAPPDATA") or "."
local bridge_dir = local_app_data .. "\\DeadlyDaysCompanion\\Bridge"
local telemetry_path = bridge_dir .. "\\telemetry.jsonl"

local interesting_tokens = {
    "item", "weapon", "character", "inventory", "reward", "airdrop", "backpack",
    "sticker", "craft", "recipe", "powerup", "throwable", "grenade", "firework",
    "president", "knabe", "perk", "upgrade", "rarity", "loot", "widget"
}

local function now_iso()
    return os.date("!%Y-%m-%dT%H:%M:%SZ")
end

local function json_escape(value)
    local s = tostring(value or "")
    s = s:gsub("\\", "\\\\")
         :gsub('"', '\\"')
         :gsub("\b", "\\b")
         :gsub("\f", "\\f")
         :gsub("\n", "\\n")
         :gsub("\r", "\\r")
         :gsub("\t", "\\t")
    return s
end

local function write_line(json)
    local f = io.open(telemetry_path, "a")
    if not f then
        print("[DDRCompanionBridge] telemetry file unavailable: " .. telemetry_path .. "\n")
        return false
    end
    f:write(json, "\n")
    f:flush()
    f:close()
    return true
end

local function emit_simple(event_type, extra)
    local suffix = extra and ("," .. extra) or ""
    write_line(string.format(
        '{"type":"%s","timestamp":"%s","bridgeVersion":"%s"%s}',
        json_escape(event_type), now_iso(), MOD_VERSION, suffix
    ))
end

local function is_interesting(full_name)
    local n = string.lower(full_name or "")
    for _, token in ipairs(interesting_tokens) do
        if string.find(n, token, 1, true) then
            return true
        end
    end
    return false
end

local function safe_full_name(obj)
    if obj == nil then return nil end
    local ok_valid, valid = pcall(function() return obj:IsValid() end)
    if not ok_valid or not valid then return nil end
    local ok, name = pcall(function() return obj:GetFullName() end)
    if not ok then return nil end
    return tostring(name)
end

local function discovery_snapshot(reason)
    if discovery_running then
        emit_simple("discovery-skipped", '"reason":"already-running"')
        return
    end
    discovery_running = true
    print("[DDRCompanionBridge] Starting read-only UObject discovery...\n")

    local objects = {}
    local total_seen = 0
    local matched = 0

    local ok, err = pcall(function()
        ForEachUObject(function(Object, ChunkIndex, ObjectIndex)
            total_seen = total_seen + 1
            if matched >= DISCOVERY_LIMIT then return end

            local full_name = safe_full_name(Object)
            if full_name and is_interesting(full_name) then
                matched = matched + 1
                objects[#objects + 1] = string.format(
                    '{"name":"%s","chunk":%d,"index":%d}',
                    json_escape(full_name), tonumber(ChunkIndex) or -1, tonumber(ObjectIndex) or -1
                )
            end
        end)
    end)

    if not ok then
        emit_simple("discovery-error", '"message":"' .. json_escape(err) .. '"')
        discovery_running = false
        return
    end

    local json = string.format(
        '{"type":"discovery","timestamp":"%s","bridgeVersion":"%s","reason":"%s","totalSeen":%d,"matched":%d,"truncated":%s,"objects":[%s]}',
        now_iso(), MOD_VERSION, json_escape(reason or "manual"), total_seen, matched,
        matched >= DISCOVERY_LIMIT and "true" or "false", table.concat(objects, ",")
    )
    write_line(json)
    discovery_running = false
    print(string.format("[DDRCompanionBridge] Discovery complete: %d candidate objects from %d UObjects.\n", matched, total_seen))
end

local function observe_widget(obj)
    local name = safe_full_name(obj)
    if not name or not is_interesting(name) then return end
    emit_simple("new-widget", '"name":"' .. json_escape(name) .. '"')
end

print("[DDRCompanionBridge] Loaded v" .. MOD_VERSION .. " (read-only)\n")
emit_simple("bridge-start", '"mode":"read-only"')

-- Heartbeat lets the external companion distinguish a loaded bridge from a stale telemetry file.
local heartbeat_handle = nil
if LoopInGameThreadWithDelay then
    heartbeat_handle = LoopInGameThreadWithDelay(1000, function()
        emit_simple("heartbeat")
    end)
elseif LoopAsync then
    LoopAsync(1000, function()
        emit_simple("heartbeat")
        return false
    end)
end

-- F7 performs one deliberately on-demand UObject pass. Full GUObject scans are expensive, so we
-- do not run them continuously. The resulting candidate names are used to identify Deadly Days'
-- game-specific inventory/reward/character classes before we add precise hooks.
RegisterKeyBind(0x76, function()
    discovery_snapshot("F7")
end)

-- UserWidget creation is much narrower than observing all Actors and is especially useful for
-- finding the game's inventory/airdrop UI classes without polling GUObjectArray continuously.
local notify_ok, notify_err = pcall(function()
    NotifyOnNewObject("/Script/UMG.UserWidget", function(ConstructedObject)
        observe_widget(ConstructedObject)
    end)
end)
if not notify_ok then
    emit_simple("capability", '"name":"NotifyOnNewObject.UserWidget","available":false,"message":"' .. json_escape(notify_err) .. '"')
else
    emit_simple("capability", '"name":"NotifyOnNewObject.UserWidget","available":true')
end

-- Map changes are useful synchronization points. We only emit an event; no gameplay call is made.
local map_ok, map_err = pcall(function()
    RegisterLoadMapPostHook(function(Engine, World)
        local world_name = safe_full_name(World)
        emit_simple("map-loaded", '"world":"' .. json_escape(world_name or "unknown") .. '"')
    end)
end)
if not map_ok then
    emit_simple("capability", '"name":"RegisterLoadMapPostHook","available":false,"message":"' .. json_escape(map_err) .. '"')
end
