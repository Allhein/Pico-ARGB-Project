#pragma once

#include <stdint.h>
#include "led_driver.h"

namespace firmware {

// Lighting modes understood by the firmware and host app.
enum EffectMode : uint8_t {
    EFFECT_MODE_OFF = 0,
    EFFECT_MODE_STATIC = 1,
    EFFECT_MODE_RAINBOW = 2,
    EFFECT_MODE_BREATHING = 3,
    EFFECT_MODE_CHASE = 4,
    EFFECT_MODE_MUSIC_VU = 5,
    EFFECT_MODE_COLOR_CYCLE = 6,
};

enum MusicStyle : uint8_t {
    MUSIC_STYLE_PULSE_BASE_COLOR = 0,
    MUSIC_STYLE_INTENSITY_WHEEL = 1,
};

extern uint8_t effect_speed;

void effects_init();
void effects_update(uint32_t now_ms);
void effects_request_startup();
void effects_request_connection();

void effects_set_color(uint8_t r, uint8_t g, uint8_t b);
void effects_set_mode(uint8_t mode);
void effects_off();
void effects_set_music_level(uint8_t level);
void effects_set_speed(uint8_t speed);
void effects_set_music_style(uint8_t style);

uint8_t effects_get_mode();
uint8_t effects_get_music_level();

} // namespace firmware
