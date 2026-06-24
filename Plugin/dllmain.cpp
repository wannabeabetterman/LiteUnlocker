// dllmain.cpp —— DLL 入口
//
// 移植自 FufuLauncher.UnlockerIsland/Core/dllmain.cpp
// 重要改动：
//   1. 删除了原仓库的联网授权验证（fu1.fun/Unlock.json 心跳 +
//      TerminateProcess 远程杀进程）——那是原作者的远程控制后门，
//      自用版本不需要也不应该有。
//   2. 删除了截图上报、HWID 计算等。
//   3. 只保留：读配置 → 装钩子 → 监听 config.ini 变更热重载。

#include <Windows.h>
#include <thread>
#include <iostream>
#include <cstdio>
#include <string>
#include <fstream>

#include "Config.h"
#include "Hooks.h"

static std::string g_LogPath;

static void Log(const std::string& msg) {
    std::ofstream ofs(g_LogPath, std::ios::app);
    if (ofs.is_open()) {
        SYSTEMTIME st;
        GetLocalTime(&st);
        char buf[64];
        sprintf_s(buf, "[%02d:%02d:%02d] ", st.wHour, st.wMinute, st.wSecond);
        ofs << buf << msg << std::endl;
    }
}

static void OpenConsole(const char* title) {
    if (AllocConsole()) {
        FILE* f;
        freopen_s(&f, "CONOUT$", "w", stdout);
        freopen_s(&f, "CONOUT$", "w", stderr);
        freopen_s(&f, "CONIN$", "r", stdin);
        SetConsoleTitleA(title);
        std::cout << "[Unlocker] 控制台已开启\n";
    }
}

LONG WINAPI CrashHandler(EXCEPTION_POINTERS* p) {
    Log(std::string("[CRASH] Exception Code: 0x") +
        std::to_string(p->ExceptionRecord->ExceptionCode));
    return EXCEPTION_CONTINUE_SEARCH;
}

// 获取某文件的最后修改时间（用于检测 config.ini 是否被改）
static FILETIME GetFileLastWriteTime(const std::string& path) {
    FILETIME t = { 0, 0 };
    WIN32_FILE_ATTRIBUTE_DATA info;
    if (GetFileAttributesExA(path.c_str(), GetFileExInfoStandard, &info)) {
        t = info.ftLastWriteTime;
    }
    return t;
}

// 主工作线程：DLL 被注入后从这里开始跑
static void MainWorker(HMODULE hMod) {
    // 日志文件放在 dll 同目录
    char dllPath[MAX_PATH];
    GetModuleFileNameA(hMod, dllPath, MAX_PATH);
    g_LogPath = dllPath;
    size_t lastSlash = g_LogPath.find_last_of("\\/");
    if (lastSlash != std::string::npos)
        g_LogPath = g_LogPath.substr(0, lastSlash + 1);
    g_LogPath += "unlocker.log";
    Log("=== DLL 已加载 ===");

    // 1. 读配置
    Config::Load();

    // 2. 按需开控制台（调试用，发布时建议关）
    if (Config::Get().debug_console) {
        OpenConsole("SimpleUnlocker");
    }

    SetUnhandledExceptionFilter(CrashHandler);

    // 3. 安装钩子
    std::cout << "[Unlocker] 正在初始化 Hooks...\n";
    if (!Hooks::Init()) {
        std::cout << "[Unlocker] [ERR] Hooks::Init 失败!\n";
        Log("Hooks::Init 失败");
        return;
    }

    // 4. 等待游戏主循环就绪（ChangeFOV 第一次被调用）
    std::cout << "[Unlocker] 等待游戏主循环...\n";
    int waitCount = 0;
    while (!Hooks::IsGameUpdateInit()) {
        Sleep(1000);
        if (++waitCount > 60) {  // 60 秒还没就绪，可能游戏没正常跑
            Log("等待游戏主循环超时");
            break;
        }
    }
    if (Hooks::IsGameUpdateInit()) {
        Log("游戏主循环已就绪，功能已生效");
        std::cout << "[Unlocker] 功能已生效！\n";
        std::cout << "  FPS 解锁: " << (Config::Get().enable_fps_override ? "ON" : "OFF")
                  << "  目标: " << Config::Get().selected_fps << "\n";
        std::cout << "  FOV 修改: " << (Config::Get().enable_fov_override ? "ON" : "OFF")
                  << "  目标: " << Config::Get().fov_value << "\n";
    }

    // 5. 热重载：监控 config.ini，改了就重新读取（无需重启游戏）
    std::string configPath = Config::GetConfigPath();
    FILETIME lastWrite = GetFileLastWriteTime(configPath);

    while (true) {
        FILETIME currentWrite = GetFileLastWriteTime(configPath);
        if (CompareFileTime(&lastWrite, &currentWrite) != 0) {
            Sleep(100);
            Config::Load();
            Log("config.ini 已变更，配置已热重载");
            std::cout << "[Unlocker] 配置已热重载\n";
            lastWrite = GetFileLastWriteTime(configPath);
        }
        Sleep(500);
    }
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call) {
    if (ul_reason_for_call == DLL_PROCESS_ATTACH) {
        DisableThreadLibraryCalls(hModule);
        // 开一个独立线程跑主逻辑，避免在 DllMain 里阻塞（DllMain 持加载器锁）
        std::thread(MainWorker, hModule).detach();
    }
    return TRUE;
}
