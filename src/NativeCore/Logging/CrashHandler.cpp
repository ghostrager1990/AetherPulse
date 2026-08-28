#include "CrashHandler.h"
#include <cstdio>
#include <cstdarg>
#include <psapi.h>

static LPTOP_LEVEL_EXCEPTION_FILTER g_pPrevFilter = nullptr;

static void WriteLog(const char* text) {
    FILE* f = nullptr;
    fopen_s(&f, "aetherpulse_debug.log", "a");
    if (f) {
        fputs(text, f);
        fflush(f);
        fclose(f);
    }
}

namespace CrashHandler {

    void Log(const char* format, ...) {
        char buffer[1024];
        va_list args;
        va_start(args, format);
        vsnprintf(buffer, sizeof(buffer), format, args);
        va_end(args);
        WriteLog(buffer);
    }

    static LONG WINAPI UnhandledFilter(EXCEPTION_POINTERS* pExceptionInfo) {
        if (!pExceptionInfo || !pExceptionInfo->ExceptionRecord) {
            return EXCEPTION_CONTINUE_SEARCH;
        }

        DWORD code = pExceptionInfo->ExceptionRecord->ExceptionCode;
        void* addr = pExceptionInfo->ExceptionRecord->ExceptionAddress;

        char moduleName[MAX_PATH] = "Unknown";
        HMODULE hMod = nullptr;
        if (GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                               (LPCSTR)addr, &hMod) && hMod) {
            GetModuleFileNameA(hMod, moduleName, sizeof(moduleName));
        }

        char crashBuf[2048];
        snprintf(crashBuf, sizeof(crashBuf),
            "\n========== [CRASH DETECTED] ==========\n"
            "Exception Code: 0x%08X\n"
            "Exception Addr: %p (Module: %s)\n"
            "RAX: %p  RBX: %p  RCX: %p  RDX: %p\n"
            "RSI: %p  RDI: %p  RBP: %p  RSP: %p\n"
            "RIP: %p  EFLAGS: 0x%08X\n"
            "======================================\n\n",
            code, addr, moduleName,
            (void*)pExceptionInfo->ContextRecord->Rax,
            (void*)pExceptionInfo->ContextRecord->Rbx,
            (void*)pExceptionInfo->ContextRecord->Rcx,
            (void*)pExceptionInfo->ContextRecord->Rdx,
            (void*)pExceptionInfo->ContextRecord->Rsi,
            (void*)pExceptionInfo->ContextRecord->Rdi,
            (void*)pExceptionInfo->ContextRecord->Rbp,
            (void*)pExceptionInfo->ContextRecord->Rsp,
            (void*)pExceptionInfo->ContextRecord->Rip,
            pExceptionInfo->ContextRecord->EFlags
        );

        WriteLog(crashBuf);

        if (g_pPrevFilter) {
            return g_pPrevFilter(pExceptionInfo);
        }
        return EXCEPTION_CONTINUE_SEARCH;
    }

    void Install() {
        g_pPrevFilter = SetUnhandledExceptionFilter(UnhandledFilter);
        Log("[CrashHandler] Unhandled Exception Filter installed successfully.\n");
    }

    void Uninstall() {
        if (g_pPrevFilter) {
            SetUnhandledExceptionFilter(g_pPrevFilter);
            g_pPrevFilter = nullptr;
        }
    }
}
