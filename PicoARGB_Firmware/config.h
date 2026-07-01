#pragma once

#include <stdint.h>
#include <stdio.h>
#include "pico/stdlib.h"

// Main firmware parameters. Keep these pins stable for the current hardware.
#define DEBUG_LOG 1
#define ENABLE_GAMMA 1

namespace firmware {

constexpr uint WS2812_PIN = 0;
constexpr uint DEBUG_LED_PIN = 25;
constexpr uint NUM_LEDS = 8;

// Do not change VID/PID without updating the host-side controller.
constexpr uint16_t USB_VID = 0x20A0;
constexpr uint16_t USB_PID = 0x423D;

constexpr uint8_t DEFAULT_BRIGHTNESS = 100;
constexpr uint8_t DEFAULT_EFFECT_SPEED = 100;
constexpr uint32_t EFFECT_FRAME_MS = 16;

void debug_init();
void debug_service(uint32_t now_ms);
void debug_blink(uint8_t count, uint16_t on_ms, uint16_t off_ms = 0);

} // namespace firmware

#if DEBUG_LOG
#define LOGF(...) printf(__VA_ARGS__)
#else
#define LOGF(...) do { } while (0)
#endif
