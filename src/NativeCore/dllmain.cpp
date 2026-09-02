#include "DXGI/DXGIProxy.h"
#include "Telemetry/TelemetryCore.h"
#include "FramePacer.h"
#include <windows.h>
#include <string>
#include <vector>

BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID lpReserved)
{
    switch (ul_reason_for_call)
    {
        case DLL_PROCESS_ATTACH:
        {
            DisableThreadLibraryCalls(hModule);

            wchar_t currentExePath[MAX_PATH] = { 0 };
            GetModuleFileNameW(NULL, currentExePath, MAX_PATH);
            std::wstring fullPath(currentExePath);
            size_t slashPos = fullPath.find_last_of(L"\\/");
            std::wstring exeName = (slashPos != std::wstring::npos) ? fullPath.substr(slashPos + 1) : fullPath;

            const std::vector<std::wstring> blacklist = {
                L"aetherpulse.exe", L"dwm.exe", L"explorer.exe", L"devenv.exe",
                L"taskmgr.exe", L"conhost.exe", L"svchost.exe", L"systemsettings.exe",
                L"antigravity.exe", L"code.exe", L"chrome.exe", L"msedge.exe",
                L"cmd.exe", L"powershell.exe", L"pwsh.exe", L"msbuild.exe", L"cmake.exe",
                L"dotnet.exe", L"vshost.exe", L"wsl.exe", L"epicgameslauncher.exe",
                L"steam.exe", L"galaxyclient.exe"
            };

            for (const auto& blocked : blacklist)
            {
                if (_wcsicmp(exeName.c_str(), blocked.c_str()) == 0)
                {
                    return TRUE;
                }
            }

            wchar_t iniPath[MAX_PATH] = { 0 };
            if (GetEnvironmentVariableW(L"ProgramData", iniPath, MAX_PATH) > 0)
            {
                wcscat_s(iniPath, L"\\AetherPulse\\aetherpulse.ini");
            }
            else
            {
                wcscpy_s(iniPath, L"C:\\ProgramData\\AetherPulse\\aetherpulse.ini");
            }

            wchar_t targetExe[MAX_PATH] = { 0 };
            GetPrivateProfileStringW(L"Target", L"TargetExeName", L"", targetExe, MAX_PATH, iniPath);

            if (targetExe[0] != L'\0' && _wcsicmp(targetExe, L"Auto") != 0 && _wcsicmp(targetExe, L"All") != 0 && _wcsicmp(targetExe, L"None") != 0)
            {
                if (_wcsicmp(exeName.c_str(), targetExe) != 0)
                {
                    return TRUE;
                }
            }

            GetAetherPulsePacer()->Initialize();
            break;
        }
        case DLL_PROCESS_DETACH:
        {
            ShutdownDXGIProxyAndHooks();
            GetAetherPulsePacer()->Shutdown();
            break;
        }
    }
    return TRUE;
}