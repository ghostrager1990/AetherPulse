#pragma once
#include <windows.h>

namespace CrashHandler {
    void Install();
    void Uninstall();
    void Log(const char* format, ...);
}
