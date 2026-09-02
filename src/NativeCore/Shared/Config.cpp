#include "Config.h"
#include <windows.h>
#include <filesystem>
#include <string>
#include <fstream>
#include <sstream>
#include <algorithm>

namespace fs = std::filesystem;

namespace
{
    std::wstring GetModuleDir()
    {
        wchar_t buffer[MAX_PATH] = { 0 };
        HMODULE hMod = nullptr;
        GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
            reinterpret_cast<LPCWSTR>(&GetModuleDir), &hMod);
        GetModuleFileNameW(hMod, buffer, MAX_PATH);
        fs::path p(buffer);
        return p.parent_path().wstring();
    }

    std::string Trim(const std::string& str)
    {
        size_t first = str.find_first_not_of(" \t\r\n");
        if (first == std::string::npos) return "";
        size_t last = str.find_last_not_of(" \t\r\n");
        return str.substr(first, (last - first + 1));
    }
}

AetherConfig& AetherConfig::Get()
{
    static AetherConfig config;
    return config;
}

bool AetherConfig::Load(const std::wstring& configPath)
{
    std::wstring path = configPath;
    if (path.empty())
    {
        std::filesystem::path p = std::filesystem::path(GetModuleDir()) / L"aetherpulse.ini";
        if (!std::filesystem::exists(p))
        {
            // Also check current directory
            p = std::filesystem::current_path() / L"aetherpulse.ini";
        }
        path = p.wstring();
    }

    if (!std::filesystem::exists(path))
    {
        return false;
    }

    std::ifstream file(path);
    if (!file.is_open())
    {
        return false;
    }

    std::string currentSection;
    std::string line;

    while (std::getline(file, line))
    {
        std::string trimmed = Trim(line);
        if (trimmed.empty() || trimmed[0] == ';' || trimmed[0] == '#')
        {
            continue;
        }

        if (trimmed.front() == '[' && trimmed.back() == ']')
        {
            currentSection = trimmed.substr(1, trimmed.size() - 2);
            std::transform(currentSection.begin(), currentSection.end(), currentSection.begin(), ::tolower);
            continue;
        }

        size_t equalsPos = trimmed.find('=');
        if (equalsPos == std::string::npos)
        {
            continue;
        }

        std::string key = Trim(trimmed.substr(0, equalsPos));
        std::string val = Trim(trimmed.substr(equalsPos + 1));
        std::transform(key.begin(), key.end(), key.begin(), ::tolower);

        if (currentSection == "pacing" || currentSection == "framegeneration")
        {
            if (key == "enablepacing" || key == "enabled") pacing.enablePacing = (val == "1" || val == "true" || val == "True");
            else if (key == "enablehalfintervalpacing" || key == "halfintervalcadence" || key == "halfinterval") pacing.enableHalfIntervalPacing = (val == "1" || val == "true" || val == "True");
            else if (key == "enableantilag2" || key == "antilag2") pacing.enableAntiLag2 = (val == "1" || val == "true" || val == "True");
            else if (key == "hudprotection" || key == "hudpreservationmask") pacing.hudProtection = (val == "1" || val == "true" || val == "True");
            else if (key == "multipliermode" || key == "multiplier" || key == "framegenmultiplier")
            {
                if (val == "adaptive" || val == "0" || val.find("adaptive") != std::string::npos) pacing.multiplierMode = FrameGenMultiplier::Adaptive;
                else if (val == "x1" || val == "1" || val == "1x" || val == "1X") pacing.multiplierMode = FrameGenMultiplier::x1;
                else if (val == "x2" || val == "2" || val == "2x" || val == "2X") pacing.multiplierMode = FrameGenMultiplier::x2;
                else if (val == "x3" || val == "3" || val == "3x" || val == "3X") pacing.multiplierMode = FrameGenMultiplier::x3;
                else if (val == "x4" || val == "4" || val == "4x" || val == "4X") pacing.multiplierMode = FrameGenMultiplier::x4;
                else if (val == "x5" || val == "5" || val == "5x" || val == "5X") pacing.multiplierMode = FrameGenMultiplier::x5;
                else if (val == "x6" || val == "6" || val == "6x" || val == "6X") pacing.multiplierMode = FrameGenMultiplier::x6;
            }
            else if (key == "targetfps") pacing.targetFps = static_cast<uint32_t>(std::stoul(val));
            else if (key == "targetfpscap") pacing.targetFpsCap = static_cast<uint32_t>(std::stoul(val));
            else if (key == "emaalpha") pacing.emaAlpha = std::stof(val);
            else if (key == "spinyieldmicroseconds" || key == "spinyieldus" || key == "spinyieldprecisionus") pacing.spinYieldMicroseconds = static_cast<uint32_t>(std::stoul(val));
            else if (key == "forceflipdiscard" || key == "enforceflipdiscard") pacing.forceFlipDiscard = (val == "1" || val == "true" || val == "True");
            else if (key == "maxframelatency") pacing.maxFrameLatency = static_cast<uint32_t>(std::stoul(val));
        }
        else if (currentSection == "denoiser" || currentSection == "rayregeneration" || currentSection == "rayregen")
        {
            if (key == "enablerayregen" || key == "enabled") denoiser.enableRayRegen = (val == "1" || val == "true" || val == "True");
            else if (key == "neuralradiancecache" || key == "enablenrc") denoiser.neuralRadianceCache = (val == "1" || val == "true" || val == "True");
            else if (key == "denoisereflections" || key == "denoisereflection") denoiser.denoiseReflections = (val == "1" || val == "true" || val == "True");
            else if (key == "denoiseshadows" || key == "denoiseshadowao") denoiser.denoiseShadows = (val == "1" || val == "true" || val == "True");
            else if (key == "glossyradiancefilter" || key == "glossyfilter") denoiser.glossyRadianceFilter = (val == "1" || val == "true" || val == "True");
            else if (key == "roughnessthreshold") denoiser.roughnessThreshold = std::stof(val);
            else if (key == "spatialfilterpasses" || key == "spatialiterations" || key == "spatialwaveletiterations") denoiser.spatialFilterPasses = static_cast<uint32_t>(std::stoul(val));
            else if (key == "temporalweight" || key == "temporalhistoryweight") denoiser.temporalWeight = std::stof(val);
            else if (key == "depthsigma") denoiser.depthSigma = std::stof(val);
            else if (key == "normalsigma" || key == "normalexponent") denoiser.normalSigma = std::stof(val);
            else if (key == "forceautoexposure" || key == "hdrexposureclamping") denoiser.forceAutoExposure = (val == "1" || val == "true" || val == "True");
            else if (key == "colorspacecorrect" || key == "perceptualcolorcorrection") denoiser.colorSpaceCorrect = (val == "1" || val == "true" || val == "True");
            else if (key == "enabledisocclusionfilter" || key == "disocclusionfilter" || key == "disocclusionghostingfilter") denoiser.enableDisocclusionFilter = (val == "1" || val == "true" || val == "True");
        }
        else if (currentSection == "fsr" || currentSection == "upscaling")
        {
            if (key == "mode") fsr.mode = val;
            else if (key == "nativeaa") fsr.nativeAA = (val == "1" || val == "true" || val == "True");
            else if (key == "reactivemask" || key == "reactivemaskoptimization") fsr.reactiveMask = (val == "1" || val == "true" || val == "True");
            else if (key == "enablercasoverride" || key == "enablercas" || key == "overridercas" || key == "rcassharpening") fsr.enableRCASOverride = (val == "1" || val == "true" || val == "True");
            else if (key == "sharpness" || key == "rcassharpness") fsr.sharpness = std::stof(val);
            else if (key == "autolodbias" || key == "autocalculatemiplod" || key == "miplodbiasauto") fsr.autoLODBias = (val == "1" || val == "true" || val == "True");
            else if (key == "texturelodbias" || key == "manualmiplodbias") fsr.textureLODBias = std::stof(val);
            else if (key == "reactivemasksensitivity") fsr.reactiveMaskSensitivity = std::stof(val);
            else if (key == "clampminrenderscale" || key == "drsfloorscale") fsr.clampMinRenderScale = static_cast<uint32_t>(std::stoul(val));
        }
        else if (currentSection == "telemetry")
        {
            if (key == "enablesharedmemory") telemetry.enableSharedMemory = (val == "1" || val == "true" || val == "True");
            else if (key == "updateintervalms")
            {
                try {
                    if (!val.empty()) telemetry.updateIntervalMs = static_cast<uint32_t>(std::stoul(val));
                } catch (...) {
                    telemetry.updateIntervalMs = 16;
                }
            }
        }
        else if (currentSection == "chaining" || currentSection == "compatibility")
        {
            if (key == "enableproxychaining") chaining.enableProxyChaining = (val == "1" || val == "true" || val == "True");
            else if (key == "originaldllpath")
            {
                std::wstring wval(val.begin(), val.end());
                chaining.originalDllPath = wval;
            }
        }
    }

    return true;
}

