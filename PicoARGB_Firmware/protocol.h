#pragma once

#include <stdint.h>

namespace firmware {

// HID protocol commands. Existing values are kept for host compatibility.
enum HidCommand : uint8_t {
    CMD_SET_COLOR = 0x03,
    CMD_OFF = 0x04,
    CMD_SET_MODE = 0x05,
    CMD_MUSIC_LEVEL = 0x06,
    CMD_SET_BRIGHTNESS = 0x07,
    CMD_SET_EFFECT_SPEED = 0x08,
    CMD_SET_MUSIC_STYLE = 0x09,
    CMD_PING = 0xAA,
};

void protocol_log_banner();

} // namespace firmware
