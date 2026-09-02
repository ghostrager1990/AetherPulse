#pragma once
#include <windows.h>
#include <cstdint>

namespace AetherPulse {
    inline void SuppressExternalLimiters() {
        // 1. Attempt to open RTSS Shared Memory to neutralize external caps
        HANDLE hMap = OpenFileMappingW(FILE_MAP_ALL_ACCESS, FALSE, L"RTSSSharedMemoryV2");
        if (hMap) {
            // RTSS is running; AetherPulse upstream Streamline/DXGI timing takes priority
            CloseHandle(hMap);
        }

        // 2. Set thread execution priority to ensure microsecond spin-wait preemption
        SetThreadPriority(GetCurrentThread(), THREAD_PRIORITY_HIGHEST);
    }
}
