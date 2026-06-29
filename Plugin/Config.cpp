#include "Config.h"
#include <string>
#include <cstdio>
#include <Windows.h>

ModConfig g_Config;

namespace Config {

    ModConfig& Get() { return g_Config; }

    // 用 Windows 原生 INI API 读取，格式与原 FufuLauncher 完全一致：
    //   [Section]
    //   Value=xxx
    static int ReadInt(LPCSTR section, int defaultVal, LPCSTR file) {
        return GetPrivateProfileIntA(section, "Value", defaultVal, file);
    }

    static float ReadFloat(LPCSTR section, float defaultVal, LPCSTR file) {
        char buf[32];
        char defStr[32];
        snprintf(defStr, sizeof(defStr), "%f", defaultVal);
        GetPrivateProfileStringA(section, "Value", defStr, buf, 32, file);
        return (float)atof(buf);
    }

    // 取得本 dll 所在目录下的 config.ini 路径
    // 注意：用 dll 自身模块的路径，而不是 exe 路径
    std::string GetConfigPath() {
        char path[MAX_PATH];
        HMODULE hm = NULL;
        GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
                           GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                           (LPCSTR)&g_Config, &hm);
        GetModuleFileNameA(hm, path, sizeof(path));

        std::string fullPath = path;
        size_t lastSlash = fullPath.find_last_of("\\/");
        std::string dirPath = (lastSlash != std::string::npos)
                              ? fullPath.substr(0, lastSlash) : ".";
        return dirPath + "\\config.ini";
    }

    void Load() {
        std::string cfgPath = GetConfigPath();
        LPCSTR file = cfgPath.c_str();

        // 每个键固定叫 Value，section 名沿用原仓库
        g_Config.debug_console         = ReadInt("DebugConsole",   0, file);

        g_Config.enable_fps_override   = ReadInt("FpsUnlock",      0, file);
        g_Config.selected_fps          = ReadInt("TargetFps",      60, file);
        g_Config.enable_vsync_override = ReadInt("VSync",          1, file);

        g_Config.enable_fov_override   = ReadInt("FovUnlock",      0, file);
        g_Config.fov_value             = ReadFloat("FovValue",     45.0f, file);
        g_Config.enable_fov_limit_check= ReadInt("FovLimitCheck",  1, file);

        // FOV 平滑过渡速度（0~1，每帧靠近目标的比例）
        float spd = ReadFloat("FovTransitionSpeed", 0.05f, file);
        if (spd < 0.0f) spd = 0.0f;
        if (spd > 1.0f) spd = 1.0f;
        g_Config.fov_transition_speed = spd;

        // UI 体验优化
        g_Config.enable_remove_team_anim = ReadInt("RemoveTeamAnim", 0, file);
        g_Config.hide_uid               = ReadInt("HideUID", 0, file);

        // 视觉效果
        g_Config.disable_fog            = ReadInt("DisableFog", 0, file);
        g_Config.disable_character_fade = ReadInt("DisableCharFade", 0, file);
    }

} // namespace Config
