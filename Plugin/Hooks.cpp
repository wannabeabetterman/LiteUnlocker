// Hooks.cpp —— FPS 解锁 + FOV 修改 的核心实现
//
// 移植自 FufuLauncher.UnlockerIsland/Core/Hooks.cpp
// 只保留 FPS / FOV 两个功能，删掉了 FreeCam/Paimon/隐藏UI/伤害数字等所有无关逻辑
//
// 反检测手段（与原仓库一致）：
//   1. hk_GetFrameCount 把"游戏自报的帧计数"钳回 60/45/30，
//      让游戏的锁帧检测以为还是低帧，从而不触发它的限制逻辑。
//   2. hk_ChangeFov 中调用 SetFrameCount 把目标帧率设成用户值。
//
// 注意：CheckResistInBeyd（秘境检测）在原仓库里第一行就 return false，
//       实际是禁用的，所以这里不再保留秘境相关分支。

#include <Windows.h>
#include <ctime>
#include <iostream>
#include <string>
#include <type_traits>

#include "MinHook.h"
#include "Scanner.h"
#include "Config.h"
#include "Hooks.h"

// il2cpp 字符串不透明类型：只作为指针传递，不需要其内部结构
struct Il2CppString;

namespace Hooks {

    // ===== 原函数指针类型 =====
    typedef int32_t (WINAPI* tGetFrameCount)();         // 游戏读取当前帧计数
    typedef int32_t (WINAPI* tSetFrameCount)(int);      // 游戏设置目标帧率
    typedef int32_t (WINAPI* tChangeFov)(void*, float); // 游戏设置摄像机 FOV
    typedef int32_t (WINAPI* tSetSyncCount)(bool);      // 关闭垂直同步

    // ===== 队伍界面相关（移除切换动画）=====
    typedef void (WINAPI* tOpenTeam)();          // 打开队伍（被 hook 的入口）
    typedef bool (WINAPI* tCheckCanEnter)();      // 检查当前能否进入队伍
    typedef void (WINAPI* tOpenTeamPage)(bool);   // 直接打开队伍页面（跳过动画）

    // ===== UI 对象操作（隐藏 UID 等）=====
    typedef Il2CppString* (WINAPI* tFindString)(const char*);  // 按路径找字符串对象
    typedef void* (WINAPI* tFindGameObject)(Il2CppString*);    // 按路径找 UI 对象
    typedef void (WINAPI* tSetActive)(void*, bool);           // 设置对象显示/隐藏

    // ===== 原函数地址（由 MinHook 在安装 hook 时填入）=====
    // 注意：这些必须是普通裸指针，因为 MH_CreateHook 的第三参数要求 void**，
    // MinHook 会把原始函数地址直接写到这里。不能用 std::atomic 包装（布局不符）。
    static tGetFrameCount o_GetFrameCount = nullptr;
    static tSetFrameCount o_SetFrameCount = nullptr;
    static tChangeFov     o_ChangeFov     = nullptr;
    static tSetSyncCount  o_SetSyncCount  = nullptr;

    // 队伍界面相关：OpenTeam 被 hook（o_ 存原始地址），另两个只扫描拿地址（p_）
    static tOpenTeam       o_OpenTeam      = nullptr;
    static tCheckCanEnter  p_CheckCanEnter = nullptr;
    static tOpenTeamPage   p_OpenTeamPage  = nullptr;

    // UI 对象操作：只扫描拿地址（隐藏 UID 用，不 hook）
    static tFindString     p_FindString     = nullptr;
    static tFindGameObject p_FindGameObject = nullptr;
    static tSetActive      p_SetActive      = nullptr;

    // 游戏主循环是否已就绪（ChangeFOV 第一次被调用即代表游戏跑起来了）
    static volatile bool g_GameUpdateInit = false;

    // FOV 平滑过渡用的"当前实际生效值"。
    // 改变 cfg.fov_value 后，g_CurrentFov 每帧朝它逼近一点，实现丝滑过渡。
    static float g_CurrentFov = 45.0f;

    // 游戏特征码（与原仓库 Patterns.h 完全一致）
    // 理论上原神 7.0 之前版本可用，游戏更新后可能需要重新逆向
    static const char* PAT_GetFrameCount = "E8 ? ? ? ? 85 C0 7E 0E E8 ? ? ? ? 0F 57 C0 F3 0F 2A C0 EB 08";
    static const char* PAT_SetFrameCount = "E8 ? ? ? ? E8 ? ? ? ? 83 F8 1F 0F 9C 05 ? ? ? ? 48 8B 05";
    static const char* PAT_ChangeFOV     = "40 53 48 83 EC 60 0F 29 74 24 ? 48 8B D9 0F 28 F1 E8 ? ? ? ? 48 85 C0 0F 84 ? ? ? ? E8 ? ? ? ? 48 8B C8";
    static const char* PAT_SetSyncCount  = "E8 ? ? ? ? E8 ? ? ? ? 89 C6 E8 ? ? ? ? 31 C9 89 F2 49 89 C0 E8 ? ? ? ? 48 89 C6 48 8B 0D ? ? ? ? 80 B9 ? ? ? ? ? 74 47 48 8B 3D ? ? ? ? 48 85 DF 74 4C";

    // 队队界面相关特征码（均为绝对地址，直接指向函数开头，无需 ResolveRelative）
    static const char* PAT_OpenTeam      = "48 83 EC ? 80 3D ? ? ? ? 00 75 ? 48 8B 0D ? ? ? ? 80 B9 ? ? ? ? 00 0F 84 ? ? ? ? B9 ? ? ? ? E8 ? ? ? ? 84 C0 75";
    static const char* PAT_CheckCanEnter = "56 48 81 ec 80 00 00 00 80 3d ? ? ? ? 00 0f 84 ? ? ? ? 80 3d ? ? ? ? 00";
    static const char* PAT_OpenTeamPage  = "56 57 53 48 83 ec 20 89 cb 80 3d ? ? ? ? 00 74 7a 80 3d ? ? ? ? 00 48 8b 05";

    // UI 对象操作特征码（绝对地址）
    static const char* PAT_FindString     = "56 48 83 ec 20 48 89 ce e8 ? ? ? ? 48 89 f1 89 c2 48 83 c4 20 5e e9 ? ? ? ? cc cc cc cc";
    static const char* PAT_FindGameObject = "40 53 48 83 EC ? 48 89 4C 24 ? 48 8D 54 24 ? 48 8D 4C 24 ? E8 ? ? ? ? 48 8B 08 48 85 C9 75 ? 48 8D 48 ? E8 ? ? ? ? 48 8B 4C 24 ? 48 8B D8 48 85 C9 74 ? 48 83 7C 24 ? 00 76";
    static const char* PAT_SetActive      = "E8 ? ? ? ? 48 8B 56 ? 48 85 D2 0F 84 ? ? ? ? 80 3D ? ? ? ? 0 0F 85 ? ? ? ? 48 89 D1 E8 ? ? ? ? 48 85 C0 0F 84 ? ? ? ? 48 89 C1";

    // 屏幕上 UID 水印的 UI 路径（原仓库 GameStrings::UIDPathWatermark）
    static const char* UID_PATH_WATERMARK = "/BetaWatermarkCanvas(Clone)/Panel/TxtUID";

    // hk_ChangeFov 会把 ChangeFov 当作主循环调用隐藏 UID 逻辑；
    // 函数实现在后面，所以这里先做前置声明。
    static void UpdateHideUID();

    // SEH 保护下调用原函数，防止目标地址无效时整个崩掉
    template <typename Fn, typename... Args>
    static auto SafeInvoke(Fn fn, Args&&... args) -> decltype(fn(args...)) {
        // 仅对函数指针做空指针检查（lambda/仿函数总是可调用的，无法用 ! 判空）
        if constexpr (std::is_pointer_v<Fn> || std::is_member_pointer_v<Fn>) {
            if (!fn) return (decltype(fn(args...)))0;
        }
        __try {
            return fn(std::forward<Args>(args)...);
        } __except (EXCEPTION_EXECUTE_HANDLER) {
            return (decltype(fn(args...)))0;
        }
    }

    // =========================================================
    //  反检测 Hook：GetFrameCount
    // =========================================================
    // 游戏内部会读这个"帧计数"来判断当前帧率档位（30/45/60），
    // 进而决定是否要触发锁帧逻辑。我们把它钳回标准档位，
    // 让游戏的检测系统以为帧率没被改过。
    int32_t WINAPI hk_GetFrameCount() {
        if (!o_GetFrameCount) return 60;
        int32_t ret = 60;
        SafeInvoke([&] { ret = o_GetFrameCount(); });

        // 钳位到游戏认可的标准档位：>=60 报 60，>=45 报 45，>=30 报 30
        if (ret >= 60) return 60;
        if (ret >= 45) return 45;
        if (ret >= 30) return 30;
        return ret;
    }

    // =========================================================
    //  FOV + FPS 主 Hook：ChangeFov
    // =========================================================
    // 这个函数游戏每帧都会调用，原作者把它当主循环用。
    // 我们精简后：在这里做"设置目标帧率"和"改写 FOV 值"两件事。
    int32_t WINAPI hk_ChangeFov(void* __this, float value) {
        g_GameUpdateInit = true;
        auto& cfg = Config::Get();

        // 主循环：定期隐藏 UID 水印（函数内部有 2 秒节流）
        UpdateHideUID();

        // --- FPS 解锁 ---
        // 关闭垂直同步（否则显示器刷新率会卡住帧率上限）
        if (cfg.enable_vsync_override) {
            if (o_SetSyncCount) SafeInvoke(o_SetSyncCount, false);
        }

        // 把游戏的目标帧率设成用户期望值（如 120/144/240）
        if (cfg.enable_fps_override) {
            if (o_SetFrameCount) SafeInvoke(o_SetFrameCount, cfg.selected_fps);
        }

        // --- FOV 修改（带平滑过渡）---
        // 仅在 value>30 时覆盖，避免影响过场动画等特殊镜头
        bool pass_check = !cfg.enable_fov_limit_check || (value > 30.0f);
        if (pass_check && cfg.enable_fov_override) {
            // 平滑过渡：g_CurrentFov 每帧朝目标值靠近 speed 比例
            // speed=0 时不过渡（保持原值），speed>=1 时立即到达（等同原版瞬切）
            if (cfg.fov_transition_speed <= 0.0f) {
                g_CurrentFov = cfg.fov_value;          // 不过渡，直接锁定
            } else {
                if (cfg.fov_transition_speed >= 1.0f) {
                    g_CurrentFov = cfg.fov_value;      // 立即到达
                } else {
                    // 线性插值：当前值 += (目标 - 当前) * 比例
                    g_CurrentFov += (cfg.fov_value - g_CurrentFov) * cfg.fov_transition_speed;
                }
            }
            value = g_CurrentFov;
        } else {
            // 当前是特殊镜头（过场/配队特写等），FOV 没被覆盖。
            // 把插值基准对齐到游戏当前值，避免回到正常镜头时从一个错误值开始过渡。
            g_CurrentFov = value;
        }

        // 调用原始函数，传入（可能已被改写的）value
        return o_ChangeFov ? SafeInvoke(o_ChangeFov, __this, value) : 0;
    }

    // =========================================================
    //  移除队伍切换动画 Hook：OpenTeam
    // =========================================================
    // 游戏打开队伍界面时本会播放一段"角色展示"过渡动画。
    // 开启后直接调用 OpenTeamPage 跳过动画，立即显示队伍页面。
    void WINAPI hk_OpenTeam() {
        if (Config::Get().enable_remove_team_anim) {
            if (p_CheckCanEnter && p_OpenTeamPage) {
                bool canEnter = false;
                SafeInvoke([&] { canEnter = p_CheckCanEnter(); });
                if (canEnter) {
                    // false = 不播放动画
                    SafeInvoke([&] { p_OpenTeamPage(false); });
                    return;
                }
            }
        }
        // 功能关闭或无法跳过时，走原始流程
        if (o_OpenTeam) SafeInvoke(o_OpenTeam);
    }

    // =========================================================
    //  隐藏 UID 水印（直播/截图隐私保护）
    // =========================================================
    // 找到屏幕右上角 UID 水印的 UI 对象，将其隐藏。
    // 只改本地显示，服务器侧真实 UID 不变，联机队友仍能看到正确 UID。
    // 非 hook，靠主循环定期轮询调用（节流 2 秒一次，避免每帧查找）。
    static float g_LastHideUidCheck = 0.0f;
    void UpdateHideUID() {
        if (!Config::Get().hide_uid) return;
        if (!p_FindString || !p_FindGameObject || !p_SetActive) return;

        // 节流：每 2 秒检查一次（原仓库同样的设计）
        float now = (float)clock() / CLOCKS_PER_SEC;
        if (now - g_LastHideUidCheck < 2.0f) return;
        g_LastHideUidCheck = now;

        SafeInvoke([&] {
            auto strObj = p_FindString(UID_PATH_WATERMARK);
            if (strObj) {
                void* obj = p_FindGameObject(strObj);
                if (obj) {
                    p_SetActive(obj, false);   // 隐藏该 UI 对象
                }
            }
        });
    }

    // =========================================================
    //  初始化：扫描 + 安装 hook
    // =========================================================
    bool Init() {
        std::cout << "[Unlocker] 开始初始化 Hooks...\n";

        if (MH_Initialize() != MH_OK) {
            std::cout << "[Unlocker] [ERR] MH_Initialize 失败\n";
            return false;
        }

        // --- 1. GetFrameCount（相对调用 E8，需解析相对地址）---
        void* scanGet = Scanner::ScanMainMod(PAT_GetFrameCount);
        if (scanGet) {
            void* target = Scanner::ResolveRelative(scanGet, 1, 5);
            if (target) {
                MH_CreateHook(target, &hk_GetFrameCount, reinterpret_cast<void**>(&o_GetFrameCount));
                std::cout << "[Unlocker] [OK] GetFrameCount hooked\n";
            } else {
                std::cout << "[Unlocker] [WARN] GetFrameCount 特征码解析失败\n";
            }
        } else {
            std::cout << "[Unlocker] [WARN] 未找到 GetFrameCount 特征码\n";
        }

        // --- 2. SetFrameCount（只扫描拿地址，不 hook）---
        void* scanSet = Scanner::ScanMainMod(PAT_SetFrameCount);
        if (scanSet) {
            void* target = Scanner::ResolveRelative(scanSet, 1, 5);
            if (target) {
                o_SetFrameCount = reinterpret_cast<tSetFrameCount>(target);
                std::cout << "[Unlocker] [OK] SetFrameCount resolved\n";
            }
        } else {
            std::cout << "[Unlocker] [WARN] 未找到 SetFrameCount 特征码\n";
        }

        // --- 3. ChangeFOV（直接绝对地址 hook）---
        void* scanFov = Scanner::ScanMainMod(PAT_ChangeFOV);
        if (scanFov) {
            MH_CreateHook(scanFov, &hk_ChangeFov, reinterpret_cast<void**>(&o_ChangeFov));
            std::cout << "[Unlocker] [OK] ChangeFOV hooked\n";
        } else {
            std::cout << "[Unlocker] [WARN] 未找到 ChangeFOV 特征码\n";
        }

        // --- 4. SetSyncCount（关闭垂直同步，只扫描拿地址）---
        void* scanSync = Scanner::ScanMainMod(PAT_SetSyncCount);
        if (scanSync) {
            void* target = Scanner::ResolveRelative(scanSync, 1, 5);
            if (target) {
                o_SetSyncCount = reinterpret_cast<tSetSyncCount>(target);
                std::cout << "[Unlocker] [OK] SetSyncCount resolved\n";
            }
        }

        // --- 5. OpenTeam（移除队伍切换动画）---
        // 辅助函数：CheckCanEnter、OpenTeamPage（只扫描拿地址，不 hook）
        void* scanCheck = Scanner::ScanMainMod(PAT_CheckCanEnter);
        if (scanCheck) {
            p_CheckCanEnter = reinterpret_cast<tCheckCanEnter>(scanCheck);
            std::cout << "[Unlocker] [OK] CheckCanEnter resolved\n";
        }
        void* scanPage = Scanner::ScanMainMod(PAT_OpenTeamPage);
        if (scanPage) {
            p_OpenTeamPage = reinterpret_cast<tOpenTeamPage>(scanPage);
            std::cout << "[Unlocker] [OK] OpenTeamPage resolved\n";
        }
        // 入口函数 OpenTeam（绝对地址 hook）
        void* scanTeam = Scanner::ScanMainMod(PAT_OpenTeam);
        if (scanTeam) {
            MH_CreateHook(scanTeam, &hk_OpenTeam, reinterpret_cast<void**>(&o_OpenTeam));
            std::cout << "[Unlocker] [OK] OpenTeam hooked\n";
        } else {
            std::cout << "[Unlocker] [WARN] 未找到 OpenTeam 特征码\n";
        }

        // --- 6. UI 对象操作（隐藏 UID 用，绝对地址，只扫描不 hook）---
        void* scanFindStr = Scanner::ScanMainMod(PAT_FindString);
        if (scanFindStr) {
            p_FindString = reinterpret_cast<tFindString>(scanFindStr);
            std::cout << "[Unlocker] [OK] FindString resolved\n";
        }
        void* scanFindObj = Scanner::ScanMainMod(PAT_FindGameObject);
        if (scanFindObj) {
            p_FindGameObject = reinterpret_cast<tFindGameObject>(scanFindObj);
            std::cout << "[Unlocker] [OK] FindGameObject resolved\n";
        }
        void* scanSetActive = Scanner::ScanMainMod(PAT_SetActive);
        if (scanSetActive) {
            // SetActive 特征码是 E8 相对调用型，需解析
            void* target = Scanner::ResolveRelative(scanSetActive, 1, 5);
            if (target) {
                p_SetActive = reinterpret_cast<tSetActive>(target);
                std::cout << "[Unlocker] [OK] SetActive resolved\n";
            }
        }

        // 一次性启用所有已创建的 hook
        MH_EnableHook(MH_ALL_HOOKS);
        std::cout << "[Unlocker] Hooks 初始化完成\n";
        return true;
    }

    void Uninit() {
        MH_DisableHook(MH_ALL_HOOKS);
        MH_Uninitialize();
    }

    bool IsGameUpdateInit() { return g_GameUpdateInit; }

} // namespace Hooks
