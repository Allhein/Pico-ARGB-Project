#include "config.h"

namespace firmware {
namespace {

struct DebugBlinkState {
    bool active = false;
    bool led_on = false;
    uint8_t remaining_edges = 0;
    uint16_t on_ms = 0;
    uint16_t off_ms = 0;
    uint32_t next_toggle_ms = 0;
};

DebugBlinkState debug_blink_state;

} // namespace

void debug_init()
{
    gpio_init(DEBUG_LED_PIN);
    gpio_set_dir(DEBUG_LED_PIN, GPIO_OUT);
    gpio_put(DEBUG_LED_PIN, 0);
}

void debug_blink(uint8_t count, uint16_t on_ms, uint16_t off_ms)
{
#if DEBUG_LOG
    if (count == 0 || on_ms == 0) {
        return;
    }

    debug_blink_state.active = true;
    debug_blink_state.led_on = true;
    debug_blink_state.remaining_edges = static_cast<uint8_t>(count * 2);
    debug_blink_state.on_ms = on_ms;
    debug_blink_state.off_ms = (off_ms == 0) ? on_ms : off_ms;
    debug_blink_state.next_toggle_ms = to_ms_since_boot(get_absolute_time()) + on_ms;
    gpio_put(DEBUG_LED_PIN, 1);
#else
    (void)count;
    (void)on_ms;
    (void)off_ms;
#endif
}

void debug_service(uint32_t now_ms)
{
#if DEBUG_LOG
    if (!debug_blink_state.active || now_ms < debug_blink_state.next_toggle_ms) {
        return;
    }

    if (debug_blink_state.remaining_edges > 0) {
        debug_blink_state.remaining_edges--;
    }

    if (debug_blink_state.remaining_edges == 0) {
        debug_blink_state.active = false;
        debug_blink_state.led_on = false;
        gpio_put(DEBUG_LED_PIN, 0);
        return;
    }

    debug_blink_state.led_on = !debug_blink_state.led_on;
    gpio_put(DEBUG_LED_PIN, debug_blink_state.led_on ? 1 : 0);
    debug_blink_state.next_toggle_ms = now_ms + (debug_blink_state.led_on
        ? debug_blink_state.on_ms
        : debug_blink_state.off_ms);
#else
    (void)now_ms;
#endif
}

} // namespace firmware
