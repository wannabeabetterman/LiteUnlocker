#pragma once
#include <atomic>
#include <string>
#include <vector>

namespace Hooks {
    bool Init();                    // 安装所有 hook
    void Uninit();                  // 卸载 hook（通常进程退出时不必手动调用）
    bool IsGameUpdateInit();        // 游戏主循环是否已就绪

    // ===== 特征码诊断结果 =====
    struct DiagResult {
        std::string name;           // 特征码名称（如 "GetFrameCount"）
        std::string feature;        // 所属功能（如 "FPS解锁/反检测"）
        bool ok;                    // 是否扫描成功
        std::string note;           // 备注（如 "相对调用" / "绝对地址"）
    };

    // 获取所有特征码的扫描诊断结果（Init 之后才有意义）
    const std::vector<DiagResult>& GetDiagnostics();
}
