#pragma once

#include "StreamlineTags.h"
#include <windows.h>

#define SL_EXPORT extern "C" __declspec(dllexport)

namespace StreamlineProxy
{
    bool Initialize();
    void Shutdown();
}

// Streamline exported C API entrypoints
extern "C" {
    SL_EXPORT sl::Result slInit(const sl::Preferences* pref, uint64_t sdkVersion);
    SL_EXPORT sl::Result slShutdown();
    SL_EXPORT sl::Result slIsFeatureSupported(sl::Feature feature, const sl::Preferences* pref);
    SL_EXPORT sl::Result slGetFeatureRequirements(sl::Feature feature, sl::FeatureRequirements* requirements);
    SL_EXPORT sl::Result slIsFeatureLoaded(sl::Feature feature, bool& loaded);
    SL_EXPORT sl::Result slSetFeatureLoaded(sl::Feature feature, bool loaded);
    SL_EXPORT sl::Result slSetTag(const sl::ViewportHandle* viewport, const sl::ResourceTag* tags, uint32_t numTags, void* cmdList);
    SL_EXPORT sl::Result slEvaluateFeature(sl::Feature feature, const sl::FrameToken* frame, const void* const* tags, uint32_t numTags, void* cmdList);
    SL_EXPORT sl::Result slAllocateResources(void* cmdList, sl::Feature feature, const sl::ViewportHandle* viewport);
    SL_EXPORT sl::Result slFreeResources(sl::Feature feature, const sl::ViewportHandle* viewport);
}
