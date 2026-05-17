#include <stdint.h>

#define XFB_WIDTH 4
#define XFB_HEIGHT 2
#define XFB_ADDRESS 0x81200000u
#define VI_XFB_TOP_LEFT 0xCC00201Cu

static uint8_t clamp8(int value)
{
	if (value < 0) return 0;
	if (value > 255) return 255;
	return (uint8_t)value;
}

static uint32_t pack_yuyv(uint8_t r0, uint8_t g0, uint8_t b0, uint8_t r1, uint8_t g1, uint8_t b1)
{
	int y0 = ( 66 * r0 + 129 * g0 +  25 * b0 + 128) >> 8;
	int y1 = ( 66 * r1 + 129 * g1 +  25 * b1 + 128) >> 8;
	int cb0 = (-38 * r0 -  74 * g0 + 112 * b0 + 128) >> 8;
	int cb1 = (-38 * r1 -  74 * g1 + 112 * b1 + 128) >> 8;
	int cr0 = (112 * r0 -  94 * g0 -  18 * b0 + 128) >> 8;
	int cr1 = (112 * r1 -  94 * g1 -  18 * b1 + 128) >> 8;
	uint8_t y0b = clamp8(y0 + 16);
	uint8_t y1b = clamp8(y1 + 16);
	uint8_t cb = clamp8(((cb0 + cb1) >> 1) + 128);
	uint8_t cr = clamp8(((cr0 + cr1) >> 1) + 128);

	return ((uint32_t)y0b << 24) | ((uint32_t)cb << 16) | ((uint32_t)y1b << 8) | cr;
}

int main(void)
{
	volatile uint32_t *xfb = (volatile uint32_t *)XFB_ADDRESS;
	volatile uint32_t *vi_xfb = (volatile uint32_t *)VI_XFB_TOP_LEFT;

	xfb[0] = pack_yuyv(255, 0, 0, 255, 255, 255);
	xfb[1] = pack_yuyv(0, 255, 0, 0, 0, 255);
	xfb[2] = pack_yuyv(255, 255, 0, 0, 255, 255);
	xfb[3] = pack_yuyv(255, 0, 255, 0, 0, 0);
	*vi_xfb = XFB_ADDRESS >> 5;

	for (;;) {
	}
}
