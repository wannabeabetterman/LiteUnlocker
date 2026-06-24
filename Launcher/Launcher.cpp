// Launcher.cpp —— 注入器
//
// 移植自 FufuLauncher.UnlockerIsland/Launcher/Launcher.cpp
// 删除了 offset.json 联网下载等非必要逻辑，保留核心注入流程。
//
// 注入原理（经典 CreateRemoteThread + LoadLibraryW）：
//   1. CreateProcess 以 CREATE_SUSPENDED 挂起态启动游戏
//   2. 扫描 Launcher.dll 同目录的 Plugins\ 文件夹，找到所有 .dll
//   3. 对每个 dll：在游戏进程开内存 → 写入 dll 路径 → 远程线程调 LoadLibraryW
//   4. ResumeThread 让游戏正式运行（此时插件已注入，DllMain 自动执行）

#include <windows.h>
#include <shlwapi.h>
#include <string>
#include <vector>
#include <sstream>

#include "Launcher.h"

#pragma comment(lib, "shlwapi.lib")

const wchar_t* PLUGINS_SUBDIR_NAME = L"Plugins";

static void SetWin32Error(
    wchar_t* errorMessage, int errorMessageSize,
    const wchar_t* operation, DWORD errorCode)
{
    if (!errorMessage || errorMessageSize <= 0) return;

    wchar_t systemMessage[256] = {};
    FormatMessageW(
        FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS,
        nullptr, errorCode, 0, systemMessage,
        _countof(systemMessage), nullptr);

    std::wstringstream message;
    message << operation << L" (Win32 " << errorCode << L")";
    if (systemMessage[0] != L'\0') message << L": " << systemMessage;
    wcsncpy_s(errorMessage, errorMessageSize, message.str().c_str(), _TRUNCATE);
}

static std::string WStrToUtf8(const std::wstring& w) {
    if (w.empty()) return std::string();
    int n = WideCharToMultiByte(CP_UTF8, 0, w.c_str(), (int)w.size(), NULL, 0, NULL, NULL);
    std::string s(n, 0);
    WideCharToMultiByte(CP_UTF8, 0, w.c_str(), (int)w.size(), &s[0], n, NULL, NULL);
    return s;
}

// 取得 Launcher.dll 自身所在目录（而非 exe 目录）
static std::wstring GetCurrentDllDirectory() {
    HMODULE hModule = NULL;
    GetModuleHandleExW(
        GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
        (LPCWSTR)&GetCurrentDllDirectory, &hModule);
    if (hModule) {
        wchar_t path[MAX_PATH];
        if (GetModuleFileNameW(hModule, path, MAX_PATH) > 0) {
            std::wstring p(path);
            size_t slash = p.find_last_of(L"\\/");
            if (slash != std::wstring::npos) return p.substr(0, slash);
        }
    }
    return L".";
}

// 经典 DLL 注入：把 dllPath 注入到 hProcess 里
static bool InjectDll(HANDLE hProcess, const std::wstring& dllPath) {
    if (GetFileAttributesW(dllPath.c_str()) == INVALID_FILE_ATTRIBUTES) return false;

    // 用短路径，避免中文/空格路径在远程进程里出问题
    std::wstring injectPath = dllPath;
    DWORD shortLen = GetShortPathNameW(dllPath.c_str(), nullptr, 0);
    if (shortLen > 0) {
        std::vector<wchar_t> shortPath(shortLen);
        if (GetShortPathNameW(dllPath.c_str(), shortPath.data(), shortLen) > 0) {
            injectPath = shortPath.data();
        }
    }

    // 1. 在目标进程分配内存，写入 dll 路径字符串
    size_t size = (injectPath.length() + 1) * sizeof(wchar_t);
    LPVOID remoteMem = VirtualAllocEx(hProcess, nullptr, size, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    if (!remoteMem) return false;

    if (!WriteProcessMemory(hProcess, remoteMem, injectPath.c_str(), size, nullptr)) {
        VirtualFreeEx(hProcess, remoteMem, 0, MEM_RELEASE);
        return false;
    }

    // 2. 远程线程调用 kernel32!LoadLibraryW 加载 dll
    LPTHREAD_START_ROUTINE loadLibrary = (LPTHREAD_START_ROUTINE)
        GetProcAddress(GetModuleHandleW(L"kernel32.dll"), "LoadLibraryW");
    HANDLE hThread = CreateRemoteThread(hProcess, nullptr, 0, loadLibrary, remoteMem, 0, nullptr);
    if (!hThread) {
        VirtualFreeEx(hProcess, remoteMem, 0, MEM_RELEASE);
        return false;
    }

    // 3. 等待加载完成
    WaitForSingleObject(hThread, INFINITE);
    DWORD exitCode = 0;
    GetExitCodeThread(hThread, &exitCode);
    VirtualFreeEx(hProcess, remoteMem, 0, MEM_RELEASE);
    CloseHandle(hThread);

    return exitCode != 0;
}

// 递归扫描目录，把所有 .dll 都注入进去
static void RecursiveScanAndInject(HANDLE hProcess, const std::wstring& directory, int& count) {
    std::wstring search = directory + L"\\*";
    WIN32_FIND_DATAW fd;
    HANDLE hFind = FindFirstFileW(search.c_str(), &fd);
    if (hFind == INVALID_HANDLE_VALUE) return;

    do {
        if (wcscmp(fd.cFileName, L".") == 0 || wcscmp(fd.cFileName, L"..") == 0) continue;
        std::wstring full = directory + L"\\" + fd.cFileName;

        if (fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) {
            RecursiveScanAndInject(hProcess, full, count);
        } else {
            size_t len = wcslen(fd.cFileName);
            if (len > 4) {
                const wchar_t* ext = fd.cFileName + len - 4;
                if (_wcsicmp(ext, L".dll") == 0) {
                    if (InjectDll(hProcess, full)) {
                        count++;
                    }
                }
            }
        }
    } while (FindNextFileW(hFind, &fd) != 0);

    FindClose(hFind);
}

static void InjectPlugins(HANDLE hProcess) {
    std::wstring dllDir = GetCurrentDllDirectory();
    std::wstring pluginsDir = dllDir + L"\\" + PLUGINS_SUBDIR_NAME;

    if (GetFileAttributesW(pluginsDir.c_str()) == INVALID_FILE_ATTRIBUTES) {
        CreateDirectoryW(pluginsDir.c_str(), NULL);
    }

    int total = 0;
    RecursiveScanAndInject(hProcess, pluginsDir, total);
}

// 导出的核心函数：启动游戏 + 注入
extern "C" LAUNCHER_API int LaunchGameAndInject(
    const wchar_t* gamePath, const wchar_t* dllPath,
    const wchar_t* commandLineArgs, wchar_t* errorMessage, int errorMessageSize)
{
    if (!ValidateGamePath(gamePath)) {
        if (errorMessage) wcsncpy_s(errorMessage, errorMessageSize, L"游戏路径无效", _TRUNCATE);
        return 1;
    }

    std::wstring workingDir = std::wstring(gamePath).substr(0, std::wstring(gamePath).find_last_of(L"\\/"));
    std::wstring cmdArgs = commandLineArgs ? commandLineArgs : L"";

    wchar_t* pCmdLine = nullptr;
    if (!cmdArgs.empty()) {
        pCmdLine = new wchar_t[cmdArgs.size() + 1];
        wcscpy_s(pCmdLine, cmdArgs.size() + 1, cmdArgs.c_str());
    }

    // 1. 挂起态启动游戏（关键：注入必须在游戏真正运行前完成）
    STARTUPINFOW si = { sizeof(si) };
    PROCESS_INFORMATION pi = {};
    if (!CreateProcessW(gamePath, pCmdLine, nullptr, nullptr, FALSE,
                        CREATE_SUSPENDED, nullptr, workingDir.c_str(), &si, &pi)) {
        const DWORD errorCode = GetLastError();
        SetWin32Error(errorMessage, errorMessageSize, L"CreateProcessW failed", errorCode);
        if (pCmdLine) delete[] pCmdLine;
        return 3;
    }

    // 2. 注入 Plugins 目录下所有 dll
    InjectPlugins(pi.hProcess);

    // 3. 恢复游戏主线程，游戏正式运行
    ResumeThread(pi.hThread);
    CloseHandle(pi.hThread);
    CloseHandle(pi.hProcess);
    if (pCmdLine) delete[] pCmdLine;

    return 0;
}

extern "C" LAUNCHER_API bool ValidateGamePath(const wchar_t* gamePath) {
    return gamePath && PathFileExistsW(gamePath);
}

extern "C" LAUNCHER_API bool ValidateDllPath(const wchar_t* dllPath) {
    return dllPath && PathFileExistsW(dllPath);
}

extern "C" LAUNCHER_API int GetDefaultDllPath(wchar_t* dllPath, int dllPathSize) {
    if (dllPath && dllPathSize > 0) dllPath[0] = L'\0';
    return 0;
}
