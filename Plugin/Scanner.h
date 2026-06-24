#pragma once
#include <string>

// 特征码扫描器：在主模块内存里按字节序列定位游戏函数
// 直接移植自 FufuLauncher.UnlockerIsland/Scanner
namespace Scanner {
    // 在主模块（游戏 exe）中扫描特征码，返回匹配到的起始地址
    void* ScanMainMod(const std::string& signature);

    // 解析 E8/E9 这类相对跳转指令，计算出真正的目标函数地址
    // offset: 相对偏移在指令中的位置（E8 call 一般是 1）
    // instrSize: 整条指令长度（E8 call 一般是 5）
    void* ResolveRelative(void* instruction, int offset = 1, int instrSize = 5);
}
