#include "Scanner.h"
#include <Windows.h>
#include <Psapi.h>
#include <sstream>
#include <vector>

namespace Scanner {

    // 把 "E8 ? ? ? ? 85 C0" 这样的字符串解析成字节/通配符数组
    // -1 表示通配符（?），其余为具体字节值
    static std::vector<int> ParsePattern(const std::string& signature) {
        std::vector<int> pattern;
        std::stringstream ss(signature);
        std::string word;
        while (ss >> word) {
            if (word == "?" || word == "??") {
                pattern.push_back(-1);
            } else {
                try {
                    pattern.push_back(std::stoi(word, nullptr, 16));
                } catch (...) {
                    pattern.push_back(-1);
                }
            }
        }
        return pattern;
    }

    void* ScanMainMod(const std::string& signature) {
        // GetModuleHandle(NULL) 返回的是进程的主模块（即游戏 exe）
        // 原神这类游戏的帧率/FOV 函数都在主模块里
        HMODULE hModule = GetModuleHandleA(nullptr);
        if (!hModule) return nullptr;

        MODULEINFO modInfo = { 0 };
        if (!GetModuleInformation(GetCurrentProcess(), hModule, &modInfo, sizeof(MODULEINFO))) {
            return nullptr;
        }

        auto pattern = ParsePattern(signature);
        if (pattern.empty()) return nullptr;

        uintptr_t startAddr = (uintptr_t)modInfo.lpBaseOfDll;
        uintptr_t endAddr = startAddr + modInfo.SizeOfImage;
        uintptr_t current = startAddr;
        const size_t pSize = pattern.size();

        // 按内存区域逐段扫描（避免读到未提交/不可读页）
        while (current < endAddr) {
            MEMORY_BASIC_INFORMATION mbi;
            if (!VirtualQuery((LPCVOID)current, &mbi, sizeof(mbi))) {
                break;
            }

            bool isGood = (mbi.State == MEM_COMMIT) &&
                          ((mbi.Protect & PAGE_GUARD) == 0) &&
                          ((mbi.Protect & PAGE_EXECUTE_READ) ||
                           (mbi.Protect & PAGE_EXECUTE_READWRITE) ||
                           (mbi.Protect & PAGE_READWRITE) ||
                           (mbi.Protect & PAGE_READONLY));

            if (isGood) {
                size_t regionSize = mbi.RegionSize;
                if ((uintptr_t)mbi.BaseAddress + regionSize > endAddr) {
                    regionSize = endAddr - (uintptr_t)mbi.BaseAddress;
                }

                if (regionSize >= pSize) {
                    const uint8_t* pStart = (const uint8_t*)mbi.BaseAddress;
                    // 暴力逐字节匹配（带通配符）
                    for (size_t i = 0; i <= regionSize - pSize; ++i) {
                        bool found = true;
                        for (size_t j = 0; j < pSize; ++j) {
                            if (pattern[j] != -1 && pattern[j] != pStart[i + j]) {
                                found = false;
                                break;
                            }
                        }
                        if (found) {
                            return (void*)(pStart + i);
                        }
                    }
                }
            }

            uintptr_t nextAddr = (uintptr_t)mbi.BaseAddress + mbi.RegionSize;
            if (nextAddr <= current) break;
            current = nextAddr;
        }

        return nullptr;
    }

    void* ResolveRelative(void* instruction, int offset, int instrSize) {
        if (!instruction) return nullptr;
        // 用 SEH 包一层，防止读到无效地址时崩溃
        __try {
            uintptr_t instrAddr = (uintptr_t)instruction;
            int32_t relative = *(int32_t*)(instrAddr + offset);
            uintptr_t target = instrAddr + instrSize + relative;
            return (void*)target;
        } __except (EXCEPTION_EXECUTE_HANDLER) {
            return nullptr;
        }
    }

} // namespace Scanner
