#pragma once
#include <windows.h>
#include "hde64.h"

#pragma pack(push, 1)

typedef struct _JMP_REL_SHORT
{
    UINT8  opcode;
    UINT8  operand;
} JMP_REL_SHORT, *PJMP_REL_SHORT;

typedef struct _JMP_REL
{
    UINT8  opcode;
    UINT32 operand;
} JMP_REL, *PJMP_REL, CALL_REL;

typedef struct _JMP_MEMORY
{
    UINT8  opcode0;
    UINT8  opcode1;
    UINT32 dummy;
    UINT64 address;
} JMP_MEMORY, *PJMP_MEMORY;

typedef struct _JMP_ABS
{
    UINT8  opcode0;
    UINT8  opcode1;
    UINT32 dummy;
    UINT64 address;
} JMP_ABS, *PJMP_ABS;

typedef struct _CALL_ABS
{
    UINT8  opcode0;
    UINT8  opcode1;
    UINT32 dummy0;
    UINT8  dummy1;
    UINT8  dummy2;
    UINT64 address;
} CALL_ABS, *PCALL_ABS;

typedef struct _JCC_ABS
{
    UINT8  opcode0;
    UINT8  opcode1;
    UINT8  dummy0;
    UINT8  dummy1;
    UINT32 dummy2;
    UINT64 address;
} JCC_ABS, *PJCC_ABS;

typedef struct _TRAMPOLINE
{
    LPVOID pTarget;
    LPVOID pDetour;
    LPVOID pTrampoline;
    UINT8  relay[sizeof(JMP_ABS)];
    UINT8  oldIPs[8];
    UINT8  newIPs[8];
    UINT   ipCount;
} TRAMPOLINE, *PTRAMPOLINE;

#pragma pack(pop)

#define HDE_DISASM(code, hs) hde64_disasm(code, hs)
#define HDE hde64s

#ifdef __cplusplus
extern "C" {
#endif

BOOL CreateTrampolineFunction(PTRAMPOLINE ct);

#ifdef __cplusplus
}
#endif
