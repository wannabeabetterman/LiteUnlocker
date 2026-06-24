#pragma once
#include <atomic>

namespace Hooks {
    bool Init();                    // 安装所有 hook
    void Uninit();                  // 卸载 hook（通常进程退出时不必手动调用）
    bool IsGameUpdateInit();        // 游戏主循环是否已就绪
}
