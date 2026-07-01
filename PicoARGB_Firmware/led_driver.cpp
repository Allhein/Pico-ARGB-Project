#include "led_driver.h"

#include "config.h"
#include "hardware/pio.h"
#include "ws2812.pio.h"

namespace firmware {
namespace {

Rgb leds[NUM_LEDS] = {};
PIO ws2812_pio = pio0;
uint ws2812_sm = 0;
uint8_t global_brightness = DEFAULT_BRIGHTNESS;

uint32_t pack_grb(uint8_t r, uint8_t g, uint8_t b)
{
    return (static_cast<uint32_t>(g) << 16)
        | (static_cast<uint32_t>(r) << 8)
        | b;
}

uint8_t scale_brightness(uint8_t value)
{
    const uint16_t scaled = static_cast<uint16_t>(value) * global_brightness;
    return static_cast<uint8_t>(scaled / 100);
}

} // namespace

void led_driver_init()
{
    const uint offset = pio_add_program(ws2812_pio, &ws2812_program);
    ws2812_sm = pio_claim_unused_sm(ws2812_pio, true);
    ws2812_program_init(ws2812_pio, ws2812_sm, offset, WS2812_PIN, 800000, false);
    led_clear();
}

uint8_t apply_gamma(uint8_t value)
{
    if (value == 0) {
        return 0;
    }

#if ENABLE_GAMMA
    const uint16_t corrected = static_cast<uint16_t>(value) * value + 127;
    return static_cast<uint8_t>(corrected / 255);
#else
    return value;
#endif
}

void led_set_brightness(uint8_t percent)
{
    global_brightness = (percent > 100) ? 100 : percent;
}

uint8_t led_get_brightness()
{
    return global_brightness;
}

void led_set_pixel(uint8_t index, Rgb color)
{
    if (index >= NUM_LEDS) {
        return;
    }
    leds[index] = color;
}

void led_fill(Rgb color)
{
    for (uint i = 0; i < NUM_LEDS; i++) {
        leds[i] = color;
    }
}

void led_clear()
{
    led_fill({0, 0, 0});
    led_show();
}

void led_show()
{
    for (uint i = 0; i < NUM_LEDS; i++) {
        const uint8_t r = apply_gamma(scale_brightness(leds[i].r));
        const uint8_t g = apply_gamma(scale_brightness(leds[i].g));
        const uint8_t b = apply_gamma(scale_brightness(leds[i].b));
        pio_sm_put_blocking(ws2812_pio, ws2812_sm, pack_grb(r, g, b) << 8u);
    }

    sleep_us(60);
}

} // namespace firmware
