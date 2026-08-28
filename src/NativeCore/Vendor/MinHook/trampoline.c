#include <windows.h>
#include <limits.h>
#include "trampoline.h"
#include "buffer.h"
#include "hde64.h"

#define MEMORY_SLOT_SIZE 64

static BOOL IsCodePadding(LPBYTE pInst, UINT size)
{
    UINT i;
    if (pInst[0] != 0x00 && pInst[0] != 0x90 && pInst[0] != 0xCC)
        return FALSE;

    for (i = 1; i < size; ++i)
    {
        if (pInst[i] != pInst[0])
            return FALSE;
    }
    return TRUE;
}

BOOL CreateTrampolineFunction(PTRAMPOLINE ct)
{
    CALL_ABS call = {
        0xFF, 0x15, 0x00000002,
        0xEB, 0x08,
        0x0000000000000000ULL
    };
    JMP_ABS jmp = {
        0xFF, 0x25, 0x00000000,
        0x0000000000000000ULL
    };
    JCC_ABS jcc = {
        0x70, 0x0E,
        0xFF, 0x25, 0x00000000,
        0x0000000000000000ULL
    };

    UINT8     oldPos   = 0;
    UINT8     newPos   = 0;
    ULONG_PTR jmpDest  = 0;
    BOOL      finished = FALSE;
    UINT8     instBuf[16];

    ct->pTrampoline = AllocateBuffer(ct->pTarget);
    if (ct->pTrampoline == NULL)
        return FALSE;

    do
    {
        HDE       hs;
        UINT      copySize;
        LPVOID    pCopySrc;
        ULONG_PTR pOldInst = (ULONG_PTR)ct->pTarget + oldPos;
        ULONG_PTR pNewInst = (ULONG_PTR)ct->pTrampoline + newPos;

        copySize = HDE_DISASM((LPVOID)pOldInst, &hs);
        if (hs.flags & F_ERROR)
        {
            FreeBuffer(ct->pTrampoline);
            ct->pTrampoline = NULL;
            return FALSE;
        }

        pCopySrc = (LPVOID)pOldInst;
        if (oldPos >= sizeof(JMP_REL))
        {
            jmp.address = pOldInst;
            pCopySrc = &jmp;
            copySize = sizeof(jmp);
            finished = TRUE;
        }
        else if ((hs.modrm & 0xC7) == 0x05)
        {
            PUINT32 pDisp = (PUINT32)(pOldInst + hs.len - ((hs.flags & 0x3C) >> 2) - 4);
            ULONG_PTR dest = pOldInst + hs.len + (INT32)*pDisp;
            ptrdiff_t newDisp = (ptrdiff_t)(dest - (pNewInst + hs.len));

            if (newDisp < INT32_MIN || newDisp > INT32_MAX)
            {
                FreeBuffer(ct->pTrampoline);
                ct->pTrampoline = NULL;
                return FALSE;
            }

            memcpy(instBuf, (LPVOID)pOldInst, copySize);
            *(PUINT32)(instBuf + (pDisp - (PUINT32)pOldInst)) = (UINT32)newDisp;
            pCopySrc = instBuf;
        }
        else if (hs.opcode == 0xE8)
        {
            ULONG_PTR dest = pOldInst + hs.len + (INT32)hs.imm.imm32;
            call.address = dest;
            pCopySrc = &call;
            copySize = sizeof(call);
        }
        else if ((hs.opcode & 0xFD) == 0xE9)
        {
            ULONG_PTR dest = pOldInst + hs.len;
            if (hs.opcode == 0xEB)
                dest += (INT8)hs.imm.imm8;
            else
                dest += (INT32)hs.imm.imm32;

            if ((ULONG_PTR)ct->pTarget <= dest && dest < ((ULONG_PTR)ct->pTarget + sizeof(JMP_REL)))
            {
                if (jmpDest < dest)
                    jmpDest = dest;
            }
            else
            {
                jmp.address = dest;
                pCopySrc = &jmp;
                copySize = sizeof(jmp);
                finished = (pOldInst >= jmpDest);
            }
        }
        else if ((hs.opcode & 0xF0) == 0x70 || (hs.opcode == 0x0F && (hs.opcode2 & 0xF0) == 0x80))
        {
            ULONG_PTR dest = pOldInst + hs.len;
            if ((hs.opcode & 0xF0) == 0x70)
                dest += (INT8)hs.imm.imm8;
            else
                dest += (INT32)hs.imm.imm32;

            if ((ULONG_PTR)ct->pTarget <= dest && dest < ((ULONG_PTR)ct->pTarget + sizeof(JMP_REL)))
            {
                if (jmpDest < dest)
                    jmpDest = dest;
            }
            else
            {
                UINT8 cond = ((hs.opcode != 0x0F) ? hs.opcode : hs.opcode2) & 0x0F;
                jcc.opcode0 = 0x70 | cond;
                jcc.address = dest;
                pCopySrc = &jcc;
                copySize = sizeof(jcc);
            }
        }
        else if (hs.opcode == 0xC2 || hs.opcode == 0xC3 || hs.opcode == 0xCA || hs.opcode == 0xCB || hs.opcode == 0xCF)
        {
            finished = (pOldInst >= jmpDest);
        }

        if (pOldInst < jmpDest && copySize != hs.len)
        {
            FreeBuffer(ct->pTrampoline);
            ct->pTrampoline = NULL;
            return FALSE;
        }

        if ((newPos + copySize) > MEMORY_SLOT_SIZE)
        {
            FreeBuffer(ct->pTrampoline);
            ct->pTrampoline = NULL;
            return FALSE;
        }

        if (ct->ipCount < 8)
        {
            ct->oldIPs[ct->ipCount] = oldPos;
            ct->newIPs[ct->ipCount] = newPos;
            ct->ipCount++;
        }

        memcpy((LPBYTE)ct->pTrampoline + newPos, pCopySrc, copySize);
        newPos += (UINT8)copySize;
        oldPos += hs.len;
    }
    while (!finished);

    if (oldPos < sizeof(JMP_REL) && !IsCodePadding((LPBYTE)ct->pTarget + oldPos, sizeof(JMP_REL) - oldPos))
    {
        FreeBuffer(ct->pTrampoline);
        ct->pTrampoline = NULL;
        return FALSE;
    }

    jmp.address = (ULONG_PTR)ct->pDetour;
    memcpy(ct->relay, &jmp, sizeof(jmp));

    return TRUE;
}
