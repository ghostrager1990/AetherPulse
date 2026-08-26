#pragma once
#include <windows.h>

#define MEMORY_SLOT_SIZE 64

#ifdef __cplusplus
extern "C" {
#endif

VOID InitializeBuffer(VOID);
VOID UninitializeBuffer(VOID);
LPVOID AllocateBuffer(LPVOID pOrigin);
VOID FreeBuffer(LPVOID pBuffer);
BOOL IsExecutableAddress(LPVOID pAddress);

#ifdef __cplusplus
}
#endif
