#include <windows.h>
#include "buffer.h"

#define MEMORY_BLOCK_SIZE 0x10000
#define MAX_MEMORY_RANGE 0x20000000

typedef struct _MEMORY_SLOT
{
    union
    {
        struct _MEMORY_SLOT *pNext;
        UINT8  buffer[MEMORY_SLOT_SIZE];
    };
} MEMORY_SLOT, *PMEMORY_SLOT;

typedef struct _MEMORY_BLOCK
{
    struct _MEMORY_BLOCK *pNext;
    PMEMORY_SLOT pFree;
    UINT   usedCount;
} MEMORY_BLOCK, *PMEMORY_BLOCK;

static PMEMORY_BLOCK g_pMemoryBlocks;

VOID InitializeBuffer(VOID)
{
}

VOID UninitializeBuffer(VOID)
{
    PMEMORY_BLOCK pBlock = g_pMemoryBlocks;
    g_pMemoryBlocks = NULL;

    while (pBlock)
    {
        PMEMORY_BLOCK pNext = pBlock->pNext;
        VirtualFree(pBlock, 0, MEM_RELEASE);
        pBlock = pNext;
    }
}

static PMEMORY_BLOCK GetMemoryBlock(LPVOID pOrigin)
{
    PMEMORY_BLOCK pBlock;
    ULONG_PTR minAddr;
    ULONG_PTR maxAddr;

    SYSTEM_INFO si;
    GetSystemInfo(&si);
    minAddr = (ULONG_PTR)si.lpMinimumApplicationAddress;
    maxAddr = (ULONG_PTR)si.lpMaximumApplicationAddress;

    if ((ULONG_PTR)pOrigin > MAX_MEMORY_RANGE)
        minAddr = (ULONG_PTR)pOrigin - MAX_MEMORY_RANGE;

    if (maxAddr > (ULONG_PTR)pOrigin + MAX_MEMORY_RANGE)
        maxAddr = (ULONG_PTR)pOrigin + MAX_MEMORY_RANGE;

    for (pBlock = g_pMemoryBlocks; pBlock != NULL; pBlock = pBlock->pNext)
    {
        if ((ULONG_PTR)pBlock >= minAddr && (ULONG_PTR)pBlock < maxAddr)
        {
            if (pBlock->pFree != NULL)
                return pBlock;
        }
    }

    {
        ULONG_PTR allocAddr = (ULONG_PTR)pOrigin;
        allocAddr -= allocAddr % si.dwAllocationGranularity;
        allocAddr += si.dwAllocationGranularity;

        while (allocAddr < maxAddr)
        {
            pBlock = (PMEMORY_BLOCK)VirtualAlloc(
                (LPVOID)allocAddr, MEMORY_BLOCK_SIZE, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);
            if (pBlock != NULL)
                break;
            allocAddr += si.dwAllocationGranularity;
        }

        if (pBlock == NULL)
        {
            allocAddr = (ULONG_PTR)pOrigin;
            allocAddr -= allocAddr % si.dwAllocationGranularity;
            while (allocAddr > minAddr)
            {
                allocAddr -= si.dwAllocationGranularity;
                pBlock = (PMEMORY_BLOCK)VirtualAlloc(
                    (LPVOID)allocAddr, MEMORY_BLOCK_SIZE, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);
                if (pBlock != NULL)
                    break;
            }
        }

        if (pBlock != NULL)
        {
            PMEMORY_SLOT pSlot = (PMEMORY_SLOT)((LPBYTE)pBlock + sizeof(MEMORY_BLOCK));
            pBlock->pNext = g_pMemoryBlocks;
            pBlock->pFree = pSlot;
            pBlock->usedCount = 0;
            g_pMemoryBlocks = pBlock;

            while ((LPBYTE)pSlot + sizeof(MEMORY_SLOT) <= (LPBYTE)pBlock + MEMORY_BLOCK_SIZE)
            {
                pSlot->pNext = (PMEMORY_SLOT)((LPBYTE)pSlot + sizeof(MEMORY_SLOT));
                pSlot = pSlot->pNext;
            }
            ((PMEMORY_SLOT)((LPBYTE)pSlot - sizeof(MEMORY_SLOT)))->pNext = NULL;
        }
    }

    return pBlock;
}

LPVOID AllocateBuffer(LPVOID pOrigin)
{
    PMEMORY_BLOCK pBlock = GetMemoryBlock(pOrigin);
    if (pBlock != NULL && pBlock->pFree != NULL)
    {
        PMEMORY_SLOT pSlot = pBlock->pFree;
        pBlock->pFree = pSlot->pNext;
        pBlock->usedCount++;
        return pSlot;
    }
    return NULL;
}

VOID FreeBuffer(LPVOID pBuffer)
{
    PMEMORY_BLOCK pBlock = g_pMemoryBlocks;
    PMEMORY_BLOCK pPrev = NULL;
    ULONG_PTR p = (ULONG_PTR)pBuffer;

    while (pBlock != NULL)
    {
        if (p >= (ULONG_PTR)pBlock && p < (ULONG_PTR)pBlock + MEMORY_BLOCK_SIZE)
        {
            PMEMORY_SLOT pSlot = (PMEMORY_SLOT)pBuffer;
            pSlot->pNext = pBlock->pFree;
            pBlock->pFree = pSlot;
            pBlock->usedCount--;

            if (pBlock->usedCount == 0)
            {
                if (pPrev)
                    pPrev->pNext = pBlock->pNext;
                else
                    g_pMemoryBlocks = pBlock->pNext;
                VirtualFree(pBlock, 0, MEM_RELEASE);
            }
            return;
        }
        pPrev = pBlock;
        pBlock = pBlock->pNext;
    }
}

BOOL IsExecutableAddress(LPVOID pAddress)
{
    MEMORY_BASIC_INFORMATION mi;
    VirtualQuery(pAddress, &mi, sizeof(mi));
    return (mi.State == MEM_COMMIT && (mi.Protect & (PAGE_EXECUTE | PAGE_EXECUTE_READ | PAGE_EXECUTE_READWRITE | PAGE_EXECUTE_WRITECOPY)));
}
