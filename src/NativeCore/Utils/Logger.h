#pragma once
#include <windows.h>
#include <fstream>
#include <string>

inline void LogDebug(const std::string& msg)
{
    std::ofstream f("aetherpulse_debug.log", std::ios::app);
    if (f.is_open())
    {
        f << "[AetherPulse] " << msg << std::endl;
    }
}
