#pragma once

#include <stdint.h>

namespace firmware {

struct Rgb {
    uint8_t r;
    uint8_t g;
    uint8_t b;
};

void led_driver_init();
void led_set_pixel(uint8_t index, Rgb color);
void led_fill(Rgb color);
void led_clear();
void led_show();

void led_set_brightness(uint8_t percent);
uint8_t led_get_brightness();
uint8_t apply_gamma(uint8_t value);

} // namespace firmware
