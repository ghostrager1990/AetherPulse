#pragma once

#include <windows.h>
#include <cstdint>

#define RTSS_SHARED_MEMORY_SIGNATURE 0x53535452 // 'RTSS'
#define RTSS_SHARED_MEMORY_VERSION   0x00020000

#pragma pack(push, 1)

struct RTSS_SHARED_MEMORY_APP_ENTRY
{
    DWORD dwProcessId;
    char  szName[MAX_PATH];
    DWORD dwFlags;
    DWORD dwTime0;
    DWORD dwTime1;
    DWORD dwFrames;
    DWORD dwFrameTime;          // Frame time in microseconds (us)
    DWORD dwFramerate;          // Instantaneous framerate * 10 (e.g. 1660 for 166.0 FPS)
    DWORD dwFramerateLimit;     // Target frame limit (e.g. 180)
    DWORD dwFramerateLimitParam;
};

struct RTSS_SHARED_MEMORY
{
    DWORD dwSignature;          // 'RTSS'
    DWORD dwVersion;            // 0x00020000
    DWORD dwAppEntrySize;       // sizeof(RTSS_SHARED_MEMORY_APP_ENTRY)
    DWORD dwAppArrOffset;       // Offset from start of header to app array
    DWORD dwAppArrSize;         // Array capacity (e.g. 256)
    DWORD dwOSDArrOffset;
    DWORD dwOSDArrSize;
    DWORD dwOSDFrame;
};

#pragma pack(pop)
