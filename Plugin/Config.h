#pragma once
#include <Windows.h>
#include <string>

// 精简配置：只保留 FPS / FOV 相关字段
// 完全兼容原 FufuLauncher 的 config.ini 格式（用 [Section] + Value=X）
struct ModConfig {
    // --- FPS 解锁 ---
    bool enable_fps_override = false;   // [FpsUnlock]     是否解锁帧率
    int  selected_fps        = 60;      // [TargetFps]     目标帧率
    bool enable_vsync_override = true;  // [VSync]         关闭垂直同步（解锁帧率时通常需要）

    // --- FOV 修改 ---
    bool  enable_fov_override = false;  // [FovUnlock]     是否修改视场角
    float fov_value           = 45.0f;  // [FovValue]      目标 FOV 值
    bool  enable_fov_limit_check = true;// [FovLimitCheck] 仅在 FOV>30 时覆盖（保护过场动画）

    // --- FOV 平滑过渡 ---
    // 切场景/切队伍时 FOV 不瞬切，而是每帧朝目标值逼近一点。
    // speed 是每帧靠近目标的比例（0~1）：
    //   0.0  = 不过渡（瞬间切换，等同原版行为）
    //   0.05 = 缓慢丝滑过渡（默认）
    //   0.2  = 较快过渡
    //   1.0  = 立即到达（等于无过渡）
    float fov_transition_speed = 0.05f; // [FovTransitionSpeed]

    // --- 调试 ---
    bool debug_console = false;         // [DebugConsole]  是否弹控制台输出日志
};

namespace Config {
    ModConfig& Get();
    void Load();
    std::string GetConfigPath();
}
