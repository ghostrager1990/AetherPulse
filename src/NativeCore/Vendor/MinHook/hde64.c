#include "hde64.h"
#include "table64.h"
#include <string.h>

unsigned int hde64_disasm(const void *code, hde64s *hs)
{
    uint8_t x, c, *p = (uint8_t *)code, cflags, opcode, pref = 0;
    uint8_t *ht = (uint8_t *)hde64_table, m_mod, m_reg, m_rm, disp_size = 0;
    uint8_t op_pre = 0;

    memset(hs, 0, sizeof(hde64s));

    for (x = 16; x; x--) {
        c = *p++;
        if (c == 0x66) {
            hs->p_66 = 1;
            pref |= PRE_66;
        } else if (c == 0x67) {
            hs->p_67 = 1;
            pref |= PRE_67;
        } else if (c == 0xf0) {
            hs->p_lock = 1;
            pref |= PRE_LOCK;
        } else if (c == 0xf2) {
            hs->p_rep = 2;
            pref |= PRE_F2;
        } else if (c == 0xf3) {
            hs->p_rep = 3;
            pref |= PRE_F3;
        } else if (c == 0x26 || c == 0x2e || c == 0x36 || c == 0x3e ||
                   c == 0x64 || c == 0x65) {
            hs->p_seg = c;
            pref |= PRE_SEG;
        } else if ((c & 0xf0) == 0x40) {
            hs->rex = c;
            hs->rex_w = (c & 0x08) >> 3;
            hs->rex_r = (c & 0x04) >> 2;
            hs->rex_x = (c & 0x02) >> 1;
            hs->rex_b = c & 0x01;
            pref |= PRE_REX;
        } else
            break;
    }

    if (pref & PRE_REX) {
        if (pref & PRE_66)
            pref &= ~PRE_66;
    }

    cflags = ht[c];
    if (cflags & C_PREFIX) {
        c = *p++;
        cflags = ht[c];
    }

    opcode = c;
    hs->opcode = c;

    if (opcode == 0x0f) {
        c = *p++;
        hs->opcode2 = c;
        opcode = c;
        if (cflags & C_MODRM)
            cflags = ht[DELTA_OP_MODRM + c];
        else
            cflags = ht[DELTA_OP_ONLY + c];
    }

    if (cflags & C_MODRM) {
        hs->flags |= F_MODRM;
        hs->modrm = c = *p++;
        hs->modrm_mod = m_mod = c >> 6;
        hs->modrm_reg = m_reg = (c & 0x3f) >> 3;
        hs->modrm_rm  = m_rm  = c & 0x07;

        if (m_mod != 3 && m_rm == 4) {
            hs->flags |= F_SIB;
            hs->sib = c = *p++;
            hs->sib_scale = c >> 6;
            hs->sib_index = (c & 0x3f) >> 3;
            hs->sib_base  = c & 0x07;
        }

        switch (m_mod) {
            case 0:
                if (m_rm == 5) {
                    disp_size = 4;
                    hs->flags |= F_RELATIVE;
                }
                if (hs->flags & F_SIB && hs->sib_base == 5)
                    disp_size = 4;
                break;
            case 1:
                disp_size = 1;
                break;
            case 2:
                disp_size = 4;
                break;
        }

        if (disp_size) {
            if (disp_size == 1) {
                hs->flags |= F_DISP8;
                hs->disp.disp8 = *p++;
            } else {
                hs->flags |= F_DISP32;
                memcpy(&hs->disp.disp32, p, 4);
                p += 4;
            }
        }
    }

    if (cflags & C_DATA66) {
        if (hs->rex_w) {
            hs->flags |= F_IMM64;
            memcpy(&hs->imm.imm64, p, 8);
            p += 8;
        } else if (pref & PRE_66) {
            hs->flags |= F_IMM16;
            memcpy(&hs->imm.imm16, p, 2);
            p += 2;
        } else {
            hs->flags |= F_IMM32;
            memcpy(&hs->imm.imm32, p, 4);
            p += 4;
        }
    } else if (cflags & C_DATA1) {
        hs->flags |= F_IMM8;
        hs->imm.imm8 = *p++;
    } else if (cflags & C_DATA2) {
        hs->flags |= F_IMM16;
        memcpy(&hs->imm.imm16, p, 2);
        p += 2;
    } else if (cflags & C_DATA4) {
        hs->flags |= F_IMM32;
        memcpy(&hs->imm.imm32, p, 4);
        p += 4;
    } else if (cflags & C_DATA8) {
        hs->flags |= F_IMM64;
        memcpy(&hs->imm.imm64, p, 8);
        p += 8;
    }

    hs->len = (uint8_t)(p - (uint8_t *)code);
    return hs->len;
}
