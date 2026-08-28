#include <windows.h>
#include <tlhelp32.h>
#include <limits.h>
#include "MinHook.h"
#include "buffer.h"
#include "trampoline.h"

#define INITIAL_HOOK_CAPACITY   32
#define INVALID_HOOK_POS        UINT_MAX

typedef struct _HOOK_ENTRY
{
    LPVOID pTarget;
    LPVOID pDetour;
    LPVOID pTrampoline;
    UINT8  backup[8];
    UINT8  patchAbove : 1;
    UINT8  isEnabled  : 1;
    UINT8  queueEnable: 1;
    UINT   nIP : 4;
    UINT8  oldIPs[8];
    UINT8  newIPs[8];
} HOOK_ENTRY, *PHOOK_ENTRY;

static CRITICAL_SECTION g_cs;
static PHOOK_ENTRY      g_pHooks;
static UINT              g_hookCapacity;
static UINT              g_hookCount;
static BOOL              g_isInitialized = FALSE;

static PHOOK_ENTRY FindHookEntry(LPVOID pTarget)
{
    UINT i;
    for (i = 0; i < g_hookCount; ++i)
    {
        if ((ULONG_PTR)pTarget == (ULONG_PTR)g_pHooks[i].pTarget)
            return &g_pHooks[i];
    }
    return NULL;
}

static PHOOK_ENTRY AddHookEntry(VOID)
{
    if (g_hookCount >= g_hookCapacity)
    {
        PHOOK_ENTRY pNewHooks;
        UINT newCapacity = (g_hookCapacity == 0) ? INITIAL_HOOK_CAPACITY : g_hookCapacity * 2;
        pNewHooks = (PHOOK_ENTRY)HeapAlloc(
            GetProcessHeap(), 0, newCapacity * sizeof(HOOK_ENTRY));
        if (pNewHooks == NULL)
            return NULL;

        if (g_pHooks != NULL)
        {
            memcpy(pNewHooks, g_pHooks, g_hookCount * sizeof(HOOK_ENTRY));
            HeapFree(GetProcessHeap(), 0, g_pHooks);
        }

        g_pHooks = pNewHooks;
        g_hookCapacity = newCapacity;
    }

    return &g_pHooks[g_hookCount++];
}

static VOID DeleteHookEntry(UINT pos)
{
    if (pos < g_hookCount - 1)
        g_pHooks[pos] = g_pHooks[g_hookCount - 1];
    g_hookCount--;
}

static MH_STATUS EnableHookLL(UINT pos, BOOL enable)
{
    PHOOK_ENTRY pHook = &g_pHooks[pos];
    DWORD oldProtect;
    SIZE_T patchSize = sizeof(JMP_REL);
    LPVOID pPatchTarget = pHook->pTarget;

    if (enable)
    {
        PJMP_REL pJmp = (PJMP_REL)pHook->pTrampoline;
        PJMP_REL pTarget = (PJMP_REL)pPatchTarget;

        if (!VirtualProtect(pPatchTarget, patchSize, PAGE_EXECUTE_READWRITE, &oldProtect))
            return MH_ERROR_MEMORY_PROTECT;

        memcpy(pHook->backup, pPatchTarget, patchSize);
        pTarget->opcode = 0xE9;
        pTarget->operand = (UINT32)((ULONG_PTR)pHook->pTrampoline - ((ULONG_PTR)pPatchTarget + sizeof(JMP_REL)));

        VirtualProtect(pPatchTarget, patchSize, oldProtect, &oldProtect);
        FlushInstructionCache(GetCurrentProcess(), pPatchTarget, patchSize);
        pHook->isEnabled = TRUE;
    }
    else
    {
        if (!VirtualProtect(pPatchTarget, patchSize, PAGE_EXECUTE_READWRITE, &oldProtect))
            return MH_ERROR_MEMORY_PROTECT;

        memcpy(pPatchTarget, pHook->backup, patchSize);
        VirtualProtect(pPatchTarget, patchSize, oldProtect, &oldProtect);
        FlushInstructionCache(GetCurrentProcess(), pPatchTarget, patchSize);
        pHook->isEnabled = FALSE;
    }

    return MH_OK;
}

MH_STATUS WINAPI MH_Initialize(VOID)
{
    if (g_isInitialized)
        return MH_ERROR_ALREADY_INITIALIZED;

    InitializeCriticalSection(&g_cs);
    InitializeBuffer();
    g_isInitialized = TRUE;
    return MH_OK;
}

MH_STATUS WINAPI MH_Uninitialize(VOID)
{
    UINT i;
    if (!g_isInitialized)
        return MH_ERROR_NOT_INITIALIZED;

    EnterCriticalSection(&g_cs);
    for (i = 0; i < g_hookCount; ++i)
    {
        if (g_pHooks[i].isEnabled)
            EnableHookLL(i, FALSE);
    }
    UninitializeBuffer();
    if (g_pHooks != NULL)
    {
        HeapFree(GetProcessHeap(), 0, g_pHooks);
        g_pHooks = NULL;
        g_hookCapacity = 0;
        g_hookCount = 0;
    }
    LeaveCriticalSection(&g_cs);
    DeleteCriticalSection(&g_cs);
    g_isInitialized = FALSE;
    return MH_OK;
}

MH_STATUS WINAPI MH_CreateHook(LPVOID pTarget, LPVOID pDetour, LPVOID *ppOriginal)
{
    MH_STATUS status = MH_OK;
    if (!g_isInitialized)
        return MH_ERROR_NOT_INITIALIZED;

    EnterCriticalSection(&g_cs);
    if (IsExecutableAddress(pTarget) && IsExecutableAddress(pDetour))
    {
        if (FindHookEntry(pTarget) == NULL)
        {
            TRAMPOLINE ct;
            ct.pTarget = pTarget;
            ct.pDetour = pDetour;
            ct.pTrampoline = NULL;
            ct.ipCount = 0;

            if (CreateTrampolineFunction(&ct))
            {
                PHOOK_ENTRY pHook = AddHookEntry();
                if (pHook != NULL)
                {
                    pHook->pTarget = ct.pTarget;
                    pHook->pDetour = ct.pDetour;
                    pHook->pTrampoline = ct.pTrampoline;
                    pHook->patchAbove = 0;
                    pHook->isEnabled = FALSE;
                    pHook->queueEnable = FALSE;
                    pHook->nIP = ct.ipCount;
                    memcpy(pHook->oldIPs, ct.oldIPs, sizeof(ct.oldIPs));
                    memcpy(pHook->newIPs, ct.newIPs, sizeof(ct.newIPs));

                    if (ppOriginal != NULL)
                        *ppOriginal = pHook->pTrampoline;
                }
                else
                {
                    FreeBuffer(ct.pTrampoline);
                    status = MH_ERROR_MEMORY_ALLOC;
                }
            }
            else
            {
                status = MH_ERROR_UNSUPPORTED_FUNCTION;
            }
        }
        else
        {
            status = MH_ERROR_ALREADY_CREATED;
        }
    }
    else
    {
        status = MH_ERROR_NOT_EXECUTABLE;
    }
    LeaveCriticalSection(&g_cs);
    return status;
}

MH_STATUS WINAPI MH_EnableHook(LPVOID pTarget)
{
    MH_STATUS status = MH_OK;
    if (!g_isInitialized)
        return MH_ERROR_NOT_INITIALIZED;

    EnterCriticalSection(&g_cs);
    if (pTarget == MH_ALL_HOOKS)
    {
        UINT i;
        for (i = 0; i < g_hookCount; ++i)
        {
            if (!g_pHooks[i].isEnabled)
            {
                status = EnableHookLL(i, TRUE);
                if (status != MH_OK) break;
            }
        }
    }
    else
    {
        PHOOK_ENTRY pHook = FindHookEntry(pTarget);
        if (pHook != NULL)
        {
            if (!pHook->isEnabled)
                status = EnableHookLL((UINT)(pHook - g_pHooks), TRUE);
            else
                status = MH_ERROR_ENABLED;
        }
        else
        {
            status = MH_ERROR_NOT_CREATED;
        }
    }
    LeaveCriticalSection(&g_cs);
    return status;
}

MH_STATUS WINAPI MH_DisableHook(LPVOID pTarget)
{
    MH_STATUS status = MH_OK;
    if (!g_isInitialized)
        return MH_ERROR_NOT_INITIALIZED;

    EnterCriticalSection(&g_cs);
    if (pTarget == MH_ALL_HOOKS)
    {
        UINT i;
        for (i = 0; i < g_hookCount; ++i)
        {
            if (g_pHooks[i].isEnabled)
            {
                status = EnableHookLL(i, FALSE);
                if (status != MH_OK) break;
            }
        }
    }
    else
    {
        PHOOK_ENTRY pHook = FindHookEntry(pTarget);
        if (pHook != NULL)
        {
            if (pHook->isEnabled)
                status = EnableHookLL((UINT)(pHook - g_pHooks), FALSE);
            else
                status = MH_ERROR_DISABLED;
        }
        else
        {
            status = MH_ERROR_NOT_CREATED;
        }
    }
    LeaveCriticalSection(&g_cs);
    return status;
}

MH_STATUS WINAPI MH_CreateHookApi(
    LPCWSTR pszModule, LPCSTR pszProcName, LPVOID pDetour, LPVOID *ppOriginal)
{
    HMODULE hModule = GetModuleHandleW(pszModule);
    LPVOID pTarget;
    if (hModule == NULL)
        return MH_ERROR_MODULE_NOT_FOUND;

    pTarget = (LPVOID)GetProcAddress(hModule, pszProcName);
    if (pTarget == NULL)
        return MH_ERROR_FUNCTION_NOT_FOUND;

    return MH_CreateHook(pTarget, pDetour, ppOriginal);
}

MH_STATUS WINAPI MH_CreateHookApiEx(
    LPCWSTR pszModule, LPCSTR pszProcName, LPVOID pDetour, LPVOID *ppOriginal, LPVOID *ppTarget)
{
    HMODULE hModule = GetModuleHandleW(pszModule);
    LPVOID pTarget;
    if (hModule == NULL)
        return MH_ERROR_MODULE_NOT_FOUND;

    pTarget = (LPVOID)GetProcAddress(hModule, pszProcName);
    if (pTarget == NULL)
        return MH_ERROR_FUNCTION_NOT_FOUND;

    if (ppTarget != NULL)
        *ppTarget = pTarget;

    return MH_CreateHook(pTarget, pDetour, ppOriginal);
}

MH_STATUS WINAPI MH_QueueEnableHook(LPVOID pTarget)
{
    return MH_EnableHook(pTarget);
}

MH_STATUS WINAPI MH_QueueDisableHook(LPVOID pTarget)
{
    return MH_DisableHook(pTarget);
}

MH_STATUS WINAPI MH_ApplyQueued(VOID)
{
    return MH_OK;
}

const char * WINAPI MH_StatusToString(MH_STATUS status)
{
    switch (status)
    {
    case MH_UNKNOWN: return "MH_UNKNOWN";
    case MH_OK: return "MH_OK";
    case MH_ERROR_ALREADY_INITIALIZED: return "MH_ERROR_ALREADY_INITIALIZED";
    case MH_ERROR_NOT_INITIALIZED: return "MH_ERROR_NOT_INITIALIZED";
    case MH_ERROR_ALREADY_CREATED: return "MH_ERROR_ALREADY_CREATED";
    case MH_ERROR_NOT_CREATED: return "MH_ERROR_NOT_CREATED";
    case MH_ERROR_ENABLED: return "MH_ERROR_ENABLED";
    case MH_ERROR_DISABLED: return "MH_ERROR_DISABLED";
    case MH_ERROR_NOT_EXECUTABLE: return "MH_ERROR_NOT_EXECUTABLE";
    case MH_ERROR_UNSUPPORTED_FUNCTION: return "MH_ERROR_UNSUPPORTED_FUNCTION";
    case MH_ERROR_MEMORY_ALLOC: return "MH_ERROR_MEMORY_ALLOC";
    case MH_ERROR_MEMORY_PROTECT: return "MH_ERROR_MEMORY_PROTECT";
    case MH_ERROR_MODULE_NOT_FOUND: return "MH_ERROR_MODULE_NOT_FOUND";
    case MH_ERROR_FUNCTION_NOT_FOUND: return "MH_ERROR_FUNCTION_NOT_FOUND";
    }
    return "UNKNOWN";
}
