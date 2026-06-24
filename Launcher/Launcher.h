#pragma once

#ifdef LAUNCHER_EXPORTS
#define LAUNCHER_API __declspec(dllexport)
#else
#define LAUNCHER_API __declspec(dllimport)
#endif

extern "C" {
    // 启动游戏（挂起态）→ 注入 Plugins 目录下所有 dll → 恢复游戏线程
    // 返回 0 成功，非 0 失败（失败原因写入 errorMessage）
    LAUNCHER_API int LaunchGameAndInject(
        const wchar_t* gamePath,        // 游戏 exe 完整路径
        const wchar_t* dllPath,         // 未使用（保留接口兼容）
        const wchar_t* commandLineArgs, // 游戏启动参数（可为空）
        wchar_t* errorMessage,          // 失败时写入错误信息
        int errorMessageSize);

    LAUNCHER_API bool ValidateGamePath(const wchar_t* gamePath);
    LAUNCHER_API bool ValidateDllPath(const wchar_t* dllPath);
    LAUNCHER_API int GetDefaultDllPath(wchar_t* dllPath, int dllPathSize);
}
