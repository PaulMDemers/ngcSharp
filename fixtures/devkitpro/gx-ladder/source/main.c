#include <gccore.h>
#include <malloc.h>
#include <math.h>
#include <stdlib.h>
#include <string.h>

#define FIFO_SIZE (256 * 1024)
#define TEX_SIZE 16
#define PANEL_W 112.0f
#define PANEL_H 56.0f

static GXRModeObj *rmode;
static void *xfb;
static volatile u8 ready_for_copy;

static u8 *tex_i4;
static u8 *tex_i8;
static u8 *tex_ia4;
static u8 *tex_ia8;
static u8 *tex_rgb565;
static u8 *tex_rgb5a3;
static u8 *tex_rgba8;
static u8 *tex_cmpr;
static u8 *tex_ci4;
static u8 *tex_ci8;
static u16 *tlut_rgb565;
static GXTexObj obj_i4;
static GXTexObj obj_i8;
static GXTexObj obj_ia4;
static GXTexObj obj_ia8;
static GXTexObj obj_rgb565;
static GXTexObj obj_rgb5a3;
static GXTexObj obj_rgba8;
static GXTexObj obj_cmpr;
static GXTexObj obj_ci4;
static GXTexObj obj_ci8;
static GXTlutObj tlut_ci4;
static GXTlutObj tlut_ci8;

static void copy_buffers(u32 count);
static void init_video(void);
static void init_gx(void);
static void init_textures(void);
static void fill_i4(u8 *data);
static void fill_i8(u8 *data);
static void fill_ia4(u8 *data);
static void fill_ia8(u8 *data);
static void fill_rgb565(u8 *data);
static void fill_rgb5a3(u8 *data);
static void fill_rgba8(u8 *data);
static void fill_cmpr(u8 *data);
static void fill_ci4(u8 *data);
static void fill_ci8(u8 *data);
static void fill_tlut_rgb565(u16 *data);
static void draw_scene(void);
static void draw_color_triangle(void);
static void draw_color_quad(void);
static void draw_textured_quad(f32 x, f32 y, GXTexObj *texture);
static void draw_textured_quad_mode(f32 x, f32 y, GXTexObj *texture, u8 tev_mode, GXColor color);
static u16 rgb565(u8 r, u8 g, u8 b);
static u16 rgb5a3(u8 r, u8 g, u8 b);
static void write_u16(u8 *data, int *offset, u16 value);
static void write_u32(u8 *data, int *offset, u32 value);

int main(void)
{
    init_video();
    init_gx();
    init_textures();

    while (SYS_MainLoop())
    {
        GX_SetViewport(0, 0, rmode->fbWidth, rmode->efbHeight, 0, 1);
        GX_InvVtxCache();
        GX_InvalidateTexAll();

        draw_scene();

        GX_DrawDone();
        ready_for_copy = GX_TRUE;
        VIDEO_WaitVSync();
    }

    return 0;
}

static void init_video(void)
{
    VIDEO_Init();
    PAD_Init();

    rmode = VIDEO_GetPreferredMode(NULL);
    xfb = MEM_K0_TO_K1(SYS_AllocateFramebuffer(rmode));

    VIDEO_Configure(rmode);
    VIDEO_SetNextFramebuffer(xfb);
    VIDEO_SetPostRetraceCallback(copy_buffers);
    VIDEO_SetBlack(FALSE);
    VIDEO_Flush();
    VIDEO_WaitVSync();
    if (rmode->viTVMode & VI_NON_INTERLACE)
    {
        VIDEO_WaitVSync();
    }
}

static void init_gx(void)
{
    void *fifo = MEM_K0_TO_K1(memalign(32, FIFO_SIZE));
    memset(fifo, 0, FIFO_SIZE);

    GX_Init(fifo, FIFO_SIZE);

    GXColor clear = {16, 20, 28, 255};
    GX_SetCopyClear(clear, 0x00ffffff);
    GX_SetViewport(0, 0, rmode->fbWidth, rmode->efbHeight, 0, 1);
    GX_SetDispCopyYScale((f32)rmode->xfbHeight / (f32)rmode->efbHeight);
    GX_SetScissor(0, 0, rmode->fbWidth, rmode->efbHeight);
    GX_SetDispCopySrc(0, 0, rmode->fbWidth, rmode->efbHeight);
    GX_SetDispCopyDst(rmode->fbWidth, rmode->xfbHeight);
    GX_SetCopyFilter(rmode->aa, rmode->sample_pattern, GX_TRUE, rmode->vfilter);
    GX_SetFieldMode(rmode->field_rendering, (rmode->viHeight == 2 * rmode->xfbHeight) ? GX_ENABLE : GX_DISABLE);
    GX_SetPixelFmt(GX_PF_RGB8_Z24, GX_ZC_LINEAR);
    GX_SetCullMode(GX_CULL_NONE);
    GX_SetDispCopyGamma(GX_GM_1_0);

    Mtx44 projection;
    guOrtho(projection, 0, (f32)rmode->efbHeight, 0, (f32)rmode->fbWidth, 0, 1);
    GX_LoadProjectionMtx(projection, GX_ORTHOGRAPHIC);

    Mtx identity;
    guMtxIdentity(identity);
    GX_LoadPosMtxImm(identity, GX_PNMTX0);

    GX_ClearVtxDesc();
    GX_SetVtxDesc(GX_VA_POS, GX_DIRECT);
    GX_SetVtxDesc(GX_VA_CLR0, GX_DIRECT);
    GX_SetVtxDesc(GX_VA_TEX0, GX_DIRECT);
    GX_SetVtxAttrFmt(GX_VTXFMT0, GX_VA_POS, GX_POS_XYZ, GX_F32, 0);
    GX_SetVtxAttrFmt(GX_VTXFMT0, GX_VA_CLR0, GX_CLR_RGBA, GX_RGBA8, 0);
    GX_SetVtxAttrFmt(GX_VTXFMT0, GX_VA_TEX0, GX_TEX_ST, GX_F32, 0);
    GX_SetNumChans(1);
    GX_SetNumTexGens(1);
    GX_SetTexCoordGen(GX_TEXCOORD0, GX_TG_MTX2x4, GX_TG_TEX0, GX_IDENTITY);
    GX_SetBlendMode(GX_BM_NONE, GX_BL_ONE, GX_BL_ZERO, GX_LO_CLEAR);
    GX_SetZMode(GX_FALSE, GX_ALWAYS, GX_FALSE);

    GX_CopyDisp(xfb, GX_TRUE);
}

static void init_textures(void)
{
    tex_i4 = memalign(32, TEX_SIZE * TEX_SIZE / 2);
    tex_i8 = memalign(32, TEX_SIZE * TEX_SIZE);
    tex_ia4 = memalign(32, TEX_SIZE * TEX_SIZE);
    tex_ia8 = memalign(32, TEX_SIZE * TEX_SIZE * 2);
    tex_rgb565 = memalign(32, TEX_SIZE * TEX_SIZE * 2);
    tex_rgb5a3 = memalign(32, TEX_SIZE * TEX_SIZE * 2);
    tex_rgba8 = memalign(32, TEX_SIZE * TEX_SIZE * 4);
    tex_cmpr = memalign(32, TEX_SIZE * TEX_SIZE / 2);
    tex_ci4 = memalign(32, TEX_SIZE * TEX_SIZE / 2);
    tex_ci8 = memalign(32, TEX_SIZE * TEX_SIZE);
    tlut_rgb565 = memalign(32, 256 * sizeof(u16));

    fill_i4(tex_i4);
    fill_i8(tex_i8);
    fill_ia4(tex_ia4);
    fill_ia8(tex_ia8);
    fill_rgb565(tex_rgb565);
    fill_rgb5a3(tex_rgb5a3);
    fill_rgba8(tex_rgba8);
    fill_cmpr(tex_cmpr);
    fill_ci4(tex_ci4);
    fill_ci8(tex_ci8);
    fill_tlut_rgb565(tlut_rgb565);

    DCFlushRange(tex_i4, TEX_SIZE * TEX_SIZE / 2);
    DCFlushRange(tex_i8, TEX_SIZE * TEX_SIZE);
    DCFlushRange(tex_ia4, TEX_SIZE * TEX_SIZE);
    DCFlushRange(tex_ia8, TEX_SIZE * TEX_SIZE * 2);
    DCFlushRange(tex_rgb565, TEX_SIZE * TEX_SIZE * 2);
    DCFlushRange(tex_rgb5a3, TEX_SIZE * TEX_SIZE * 2);
    DCFlushRange(tex_rgba8, TEX_SIZE * TEX_SIZE * 4);
    DCFlushRange(tex_cmpr, TEX_SIZE * TEX_SIZE / 2);
    DCFlushRange(tex_ci4, TEX_SIZE * TEX_SIZE / 2);
    DCFlushRange(tex_ci8, TEX_SIZE * TEX_SIZE);
    DCFlushRange(tlut_rgb565, 256 * sizeof(u16));

    GX_InitTexObj(&obj_i4, tex_i4, TEX_SIZE, TEX_SIZE, GX_TF_I4, GX_CLAMP, GX_CLAMP, GX_FALSE);
    GX_InitTexObj(&obj_i8, tex_i8, TEX_SIZE, TEX_SIZE, GX_TF_I8, GX_CLAMP, GX_CLAMP, GX_FALSE);
    GX_InitTexObj(&obj_ia4, tex_ia4, TEX_SIZE, TEX_SIZE, GX_TF_IA4, GX_CLAMP, GX_CLAMP, GX_FALSE);
    GX_InitTexObj(&obj_ia8, tex_ia8, TEX_SIZE, TEX_SIZE, GX_TF_IA8, GX_CLAMP, GX_CLAMP, GX_FALSE);
    GX_InitTexObj(&obj_rgb565, tex_rgb565, TEX_SIZE, TEX_SIZE, GX_TF_RGB565, GX_CLAMP, GX_CLAMP, GX_FALSE);
    GX_InitTexObj(&obj_rgb5a3, tex_rgb5a3, TEX_SIZE, TEX_SIZE, GX_TF_RGB5A3, GX_CLAMP, GX_CLAMP, GX_FALSE);
    GX_InitTexObj(&obj_rgba8, tex_rgba8, TEX_SIZE, TEX_SIZE, GX_TF_RGBA8, GX_CLAMP, GX_CLAMP, GX_FALSE);
    GX_InitTexObj(&obj_cmpr, tex_cmpr, TEX_SIZE, TEX_SIZE, GX_TF_CMPR, GX_CLAMP, GX_CLAMP, GX_FALSE);
    GX_InitTexObjCI(&obj_ci4, tex_ci4, TEX_SIZE, TEX_SIZE, GX_TF_CI4, GX_CLAMP, GX_CLAMP, GX_FALSE, GX_TLUT0);
    GX_InitTexObjCI(&obj_ci8, tex_ci8, TEX_SIZE, TEX_SIZE, GX_TF_CI8, GX_CLAMP, GX_CLAMP, GX_FALSE, GX_TLUT1);

    GX_InitTlutObj(&tlut_ci4, tlut_rgb565, GX_TL_RGB565, 16);
    GX_InitTlutObj(&tlut_ci8, tlut_rgb565, GX_TL_RGB565, 256);
    GX_LoadTlut(&tlut_ci4, GX_TLUT0);
    GX_LoadTlut(&tlut_ci8, GX_TLUT1);
}

static void fill_i4(u8 *data)
{
    int offset = 0;
    for (int by = 0; by < TEX_SIZE; by += 8)
    {
        for (int bx = 0; bx < TEX_SIZE; bx += 8)
        {
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x += 2)
                {
                    u8 a = (u8)(((bx + x) ^ (by + y)) & 0x0f);
                    u8 b = (u8)(((bx + x + 1) ^ (by + y)) & 0x0f);
                    data[offset++] = (u8)((a << 4) | b);
                }
            }
        }
    }
}

static void fill_i8(u8 *data)
{
    int offset = 0;
    for (int by = 0; by < TEX_SIZE; by += 4)
    {
        for (int bx = 0; bx < TEX_SIZE; bx += 8)
        {
            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    data[offset++] = (u8)(((bx + x) * 11 + (by + y) * 7) & 0xff);
                }
            }
        }
    }
}

static void fill_ia4(u8 *data)
{
    int offset = 0;
    for (int by = 0; by < TEX_SIZE; by += 4)
    {
        for (int bx = 0; bx < TEX_SIZE; bx += 8)
        {
            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    u8 intensity = (u8)(((bx + x) * 15) / (TEX_SIZE - 1));
                    u8 alpha = ((by + y) < 8) ? 0x0f : 0x08;
                    data[offset++] = (u8)((alpha << 4) | intensity);
                }
            }
        }
    }
}

static void fill_ia8(u8 *data)
{
    int offset = 0;
    for (int by = 0; by < TEX_SIZE; by += 4)
    {
        for (int bx = 0; bx < TEX_SIZE; bx += 4)
        {
            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 4; x++)
                {
                    u8 intensity = (u8)(((bx + x) * 255) / (TEX_SIZE - 1));
                    u8 alpha = (u8)(255 - (((by + y) * 255) / (TEX_SIZE - 1)));
                    write_u16(data, &offset, (u16)((intensity << 8) | alpha));
                }
            }
        }
    }
}

static void fill_rgb565(u8 *data)
{
    int offset = 0;
    for (int by = 0; by < TEX_SIZE; by += 4)
    {
        for (int bx = 0; bx < TEX_SIZE; bx += 4)
        {
            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 4; x++)
                {
                    u16 color = rgb565((u8)((bx + x) * 16), (u8)((by + y) * 16), 224);
                    data[offset++] = (u8)(color >> 8);
                    data[offset++] = (u8)color;
                }
            }
        }
    }
}

static void fill_rgb5a3(u8 *data)
{
    int offset = 0;
    for (int by = 0; by < TEX_SIZE; by += 4)
    {
        for (int bx = 0; bx < TEX_SIZE; bx += 4)
        {
            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 4; x++)
                {
                    u8 checker = (u8)((((bx + x) / 4) ^ ((by + y) / 4)) & 1);
                    u16 color = checker ? rgb5a3(255, 220, 40) : rgb5a3(40, 180, 255);
                    data[offset++] = (u8)(color >> 8);
                    data[offset++] = (u8)color;
                }
            }
        }
    }
}

static void fill_rgba8(u8 *data)
{
    int offset = 0;
    for (int by = 0; by < TEX_SIZE; by += 4)
    {
        for (int bx = 0; bx < TEX_SIZE; bx += 4)
        {
            int block = offset;
            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 4; x++)
                {
                    u8 r = (u8)((bx + x) * 16);
                    u8 a = (u8)(255 - ((by + y) * 8));
                    data[offset++] = a;
                    data[offset++] = r;
                }
            }

            offset = block + 32;
            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 4; x++)
                {
                    u8 g = (u8)((by + y) * 16);
                    u8 b = (u8)(255 - ((bx + x) * 8));
                    data[offset++] = g;
                    data[offset++] = b;
                }
            }
        }
    }
}

static void fill_cmpr(u8 *data)
{
    int offset = 0;
    for (int by = 0; by < TEX_SIZE; by += 8)
    {
        for (int bx = 0; bx < TEX_SIZE; bx += 8)
        {
            for (int sub = 0; sub < 4; sub++)
            {
                int sx = bx + ((sub & 1) * 4);
                int sy = by + ((sub >> 1) * 4);
                u16 color0 = rgb565((u8)(240 - sy * 4), (u8)(80 + sx * 7), 48);
                u16 color1 = rgb565(24, (u8)(220 - sx * 5), (u8)(240 - sy * 4));
                u32 selectors = 0;

                if (color0 <= color1)
                {
                    u16 temp = color0;
                    color0 = color1;
                    color1 = temp;
                }

                for (int y = 0; y < 4; y++)
                {
                    for (int x = 0; x < 4; x++)
                    {
                        u32 selector = (u32)(((sx + x) / 2 + (sy + y) / 2) & 3);
                        selectors |= selector << (30 - 2 * (y * 4 + x));
                    }
                }

                write_u16(data, &offset, color0);
                write_u16(data, &offset, color1);
                write_u32(data, &offset, selectors);
            }
        }
    }
}

static void fill_ci4(u8 *data)
{
    int offset = 0;
    for (int by = 0; by < TEX_SIZE; by += 8)
    {
        for (int bx = 0; bx < TEX_SIZE; bx += 8)
        {
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x += 2)
                {
                    u8 a = (u8)(((bx + x) + (by + y) * 3) & 0x0f);
                    u8 b = (u8)(((bx + x + 1) + (by + y) * 3) & 0x0f);
                    data[offset++] = (u8)((a << 4) | b);
                }
            }
        }
    }
}

static void fill_ci8(u8 *data)
{
    int offset = 0;
    for (int by = 0; by < TEX_SIZE; by += 4)
    {
        for (int bx = 0; bx < TEX_SIZE; bx += 8)
        {
            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    data[offset++] = (u8)((bx + x) * 11 + (by + y) * 13);
                }
            }
        }
    }
}

static void fill_tlut_rgb565(u16 *data)
{
    for (int i = 0; i < 256; i++)
    {
        u8 r = (u8)((i & 0x0f) * 17);
        u8 g = (u8)(((i >> 4) & 0x0f) * 17);
        u8 b = (u8)(255 - ((i & 0x0f) * 9));
        data[i] = rgb565(r, g, b);
    }
}

static void draw_scene(void)
{
    draw_color_triangle();
    draw_color_quad();
    draw_textured_quad(44, 218, &obj_i4);
    draw_textured_quad(188, 218, &obj_i8);
    draw_textured_quad(332, 218, &obj_ia4);
    draw_textured_quad(476, 218, &obj_ia8);
    draw_textured_quad(44, 306, &obj_rgb565);
    draw_textured_quad(188, 306, &obj_rgb5a3);
    draw_textured_quad(332, 306, &obj_rgba8);
    draw_textured_quad(476, 306, &obj_cmpr);
    draw_textured_quad(44, 394, &obj_ci4);
    draw_textured_quad(188, 394, &obj_ci8);
    draw_textured_quad_mode(332, 394, &obj_rgb565, GX_REPLACE, (GXColor){255, 64, 64, 255});
    draw_textured_quad_mode(476, 394, &obj_rgb565, GX_MODULATE, (GXColor){255, 64, 64, 255});
}

static void draw_color_triangle(void)
{
    GX_SetTevOrder(GX_TEVSTAGE0, GX_TEXCOORDNULL, GX_TEXMAP_NULL, GX_COLOR0A0);
    GX_SetTevOp(GX_TEVSTAGE0, GX_PASSCLR);

    GX_Begin(GX_TRIANGLES, GX_VTXFMT0, 3);
    GX_Position3f32(96, 72, 0);
    GX_Color4u8(255, 40, 40, 255);
    GX_TexCoord2f32(0, 0);
    GX_Position3f32(32, 184, 0);
    GX_Color4u8(40, 255, 80, 255);
    GX_TexCoord2f32(0, 1);
    GX_Position3f32(160, 184, 0);
    GX_Color4u8(80, 120, 255, 255);
    GX_TexCoord2f32(1, 1);
    GX_End();
}

static void draw_color_quad(void)
{
    GX_SetTevOrder(GX_TEVSTAGE0, GX_TEXCOORDNULL, GX_TEXMAP_NULL, GX_COLOR0A0);
    GX_SetTevOp(GX_TEVSTAGE0, GX_PASSCLR);

    GX_Begin(GX_QUADS, GX_VTXFMT0, 4);
    GX_Position3f32(220, 72, 0);
    GX_Color4u8(255, 255, 255, 255);
    GX_TexCoord2f32(0, 0);
    GX_Position3f32(420, 72, 0);
    GX_Color4u8(255, 200, 40, 255);
    GX_TexCoord2f32(1, 0);
    GX_Position3f32(420, 184, 0);
    GX_Color4u8(255, 80, 180, 255);
    GX_TexCoord2f32(1, 1);
    GX_Position3f32(220, 184, 0);
    GX_Color4u8(40, 220, 255, 255);
    GX_TexCoord2f32(0, 1);
    GX_End();
}

static void draw_textured_quad(f32 x, f32 y, GXTexObj *texture)
{
    draw_textured_quad_mode(x, y, texture, GX_DECAL, (GXColor){255, 255, 255, 255});
}

static void draw_textured_quad_mode(f32 x, f32 y, GXTexObj *texture, u8 tev_mode, GXColor color)
{
    GX_LoadTexObj(texture, GX_TEXMAP0);
    GX_SetTevOrder(GX_TEVSTAGE0, GX_TEXCOORD0, GX_TEXMAP0, GX_COLOR0A0);
    GX_SetTevOp(GX_TEVSTAGE0, tev_mode);

    GX_Begin(GX_QUADS, GX_VTXFMT0, 4);
    GX_Position3f32(x, y, 0);
    GX_Color4u8(color.r, color.g, color.b, color.a);
    GX_TexCoord2f32(0, 0);
    GX_Position3f32(x + PANEL_W, y, 0);
    GX_Color4u8(color.r, color.g, color.b, color.a);
    GX_TexCoord2f32(1, 0);
    GX_Position3f32(x + PANEL_W, y + PANEL_H, 0);
    GX_Color4u8(color.r, color.g, color.b, color.a);
    GX_TexCoord2f32(1, 1);
    GX_Position3f32(x, y + PANEL_H, 0);
    GX_Color4u8(color.r, color.g, color.b, color.a);
    GX_TexCoord2f32(0, 1);
    GX_End();
}

static u16 rgb565(u8 r, u8 g, u8 b)
{
    return (u16)(((r >> 3) << 11) | ((g >> 2) << 5) | (b >> 3));
}

static u16 rgb5a3(u8 r, u8 g, u8 b)
{
    return (u16)(0x8000 | ((r >> 3) << 10) | ((g >> 3) << 5) | (b >> 3));
}

static void write_u16(u8 *data, int *offset, u16 value)
{
    data[(*offset)++] = (u8)(value >> 8);
    data[(*offset)++] = (u8)value;
}

static void write_u32(u8 *data, int *offset, u32 value)
{
    data[(*offset)++] = (u8)(value >> 24);
    data[(*offset)++] = (u8)(value >> 16);
    data[(*offset)++] = (u8)(value >> 8);
    data[(*offset)++] = (u8)value;
}

static void copy_buffers(u32 count __attribute__((unused)))
{
    if (ready_for_copy == GX_TRUE)
    {
        GX_SetZMode(GX_FALSE, GX_ALWAYS, GX_FALSE);
        GX_SetColorUpdate(GX_TRUE);
        GX_CopyDisp(xfb, GX_TRUE);
        GX_Flush();
        ready_for_copy = GX_FALSE;
    }
}
