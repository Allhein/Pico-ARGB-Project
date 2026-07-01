#include "protocol.h"

#include <string.h>
#include "config.h"
#include "effects.h"
#include "led_driver.h"
#include "tusb.h"

namespace firmware {
namespace {

struct ParsedHidCommand {
    uint8_t command = 0;
    const uint8_t* payload = nullptr;
    uint16_t payload_size = 0;
};

bool usb_connected = false;
uint16_t string_descriptor[32];

uint8_t const hid_report_descriptor[] = {
    0x06, 0x00, 0xFF, 0x09, 0x01, 0xA1, 0x01,
    0x15, 0x00, 0x26, 0xFF, 0x00,
    0x75, 0x08, 0x95, 0x40,
    0x09, 0x01, 0x81, 0x02,
    0x09, 0x01, 0x91, 0x02,
    0xC0
};

tusb_desc_device_t const device_descriptor = {
    .bLength = sizeof(tusb_desc_device_t),
    .bDescriptorType = TUSB_DESC_DEVICE,
    .bcdUSB = 0x0200,
    .bDeviceClass = 0x00,
    .bDeviceSubClass = 0x00,
    .bDeviceProtocol = 0x00,
    .bMaxPacketSize0 = 64,
    .idVendor = USB_VID,
    .idProduct = USB_PID,
    .bcdDevice = 0x0100,
    .iManufacturer = 0x01,
    .iProduct = 0x02,
    .iSerialNumber = 0x03,
    .bNumConfigurations = 0x01,
};

enum {
    ITF_NUM_HID,
    ITF_TOTAL,
};

constexpr uint8_t EPNUM_HID_IN = 0x81;
constexpr uint16_t CONFIG_TOTAL_LEN = TUD_CONFIG_DESC_LEN + TUD_HID_DESC_LEN;

uint8_t const configuration_descriptor[] = {
    TUD_CONFIG_DESCRIPTOR(1, ITF_TOTAL, 0, CONFIG_TOTAL_LEN, TUSB_DESC_CONFIG_ATT_REMOTE_WAKEUP, 100),
    TUD_HID_DESCRIPTOR(ITF_NUM_HID, 4, HID_ITF_PROTOCOL_NONE, sizeof(hid_report_descriptor), EPNUM_HID_IN, 64, 10),
};

const char* const string_descriptors[] = {
    nullptr,
    "OpenRGB Project",
    "Pico ARGB Controller",
    "PICO-AR12-001",
    "HID Interface",
};

ParsedHidCommand parse_hid_command(const uint8_t* buffer, uint16_t size)
{
    ParsedHidCommand parsed;
    if (size == 0) {
        return parsed;
    }

    if (size >= 2 && buffer[0] == 0) {
        parsed.command = buffer[1];
        parsed.payload = &buffer[2];
        parsed.payload_size = size - 2;
    } else {
        parsed.command = buffer[0];
        parsed.payload = &buffer[1];
        parsed.payload_size = size - 1;
    }
    return parsed;
}

void debug_buffer(const char* prefix, const uint8_t* buffer, uint16_t size)
{
#if DEBUG_LOG
    LOGF("%s size=%u hex=", prefix, size);
    for (uint16_t i = 0; i < size && i < 32; i++) {
        LOGF("%02X ", buffer[i]);
    }
    LOGF("ascii=");
    for (uint16_t i = 0; i < size && i < 32; i++) {
        LOGF("%c", (buffer[i] >= 32 && buffer[i] <= 126) ? buffer[i] : '.');
    }
    LOGF("\n");
#else
    (void)prefix;
    (void)buffer;
    (void)size;
#endif
}

uint8_t clamp_percent(uint8_t value)
{
    return (value > 100) ? 100 : value;
}

void send_pong()
{
    if (!tud_hid_ready()) {
        return;
    }

    uint8_t response[64] = {};
    memcpy(response, "PONG", 4);
    tud_hid_report(0, response, sizeof(response));
}

} // namespace

void protocol_log_banner()
{
    LOGF("\n");
    LOGF("=== RP2040 ARGB Controller ===\n");
    LOGF("HID protocol:\n");
    LOGF("  0xAA = PING\n");
    LOGF("  0x03 = SET_COLOR (R,G,B)\n");
    LOGF("  0x04 = OFF\n");
    LOGF("  0x05 = SET_MODE\n");
    LOGF("  0x06 = MUSIC_LEVEL (0-255)\n");
    LOGF("  0x07 = SET_BRIGHTNESS (0-100)\n");
    LOGF("  0x08 = SET_EFFECT_SPEED (0-100)\n");
    LOGF("Lighting modes: 0=OFF, 1=STATIC, 2=RAINBOW, 3=BREATHING, 4=CHASE, 5=MUSIC_VU, 6=COLOR_CYCLE\n");
    LOGF("Main params: WS2812 GPIO=%u, debug LED GPIO=%u, LEDs=%u, gamma=%u\n",
        WS2812_PIN, DEBUG_LED_PIN, NUM_LEDS, ENABLE_GAMMA);
}

} // namespace firmware

uint8_t const* tud_descriptor_device_cb(void)
{
    return reinterpret_cast<uint8_t const*>(&firmware::device_descriptor);
}

uint8_t const* tud_descriptor_configuration_cb(uint8_t index)
{
    (void)index;
    return firmware::configuration_descriptor;
}

uint16_t const* tud_descriptor_string_cb(uint8_t index, uint16_t langid)
{
    (void)langid;

    uint8_t chr_count = 0;
    if (index == 0) {
        firmware::string_descriptor[1] = 0x0409;
        chr_count = 1;
    } else {
        if (index >= sizeof(firmware::string_descriptors) / sizeof(firmware::string_descriptors[0])) {
            return nullptr;
        }

        const char* str = firmware::string_descriptors[index];
        chr_count = static_cast<uint8_t>(strlen(str));
        if (chr_count > 31) {
            chr_count = 31;
        }
        for (uint8_t i = 0; i < chr_count; i++) {
            firmware::string_descriptor[1 + i] = static_cast<uint16_t>(str[i]);
        }
    }

    firmware::string_descriptor[0] = static_cast<uint16_t>((TUSB_DESC_STRING << 8) | (2 * chr_count + 2));
    return firmware::string_descriptor;
}

uint8_t const* tud_hid_descriptor_report_cb(uint8_t instance)
{
    (void)instance;
    return firmware::hid_report_descriptor;
}

uint16_t tud_hid_get_report_cb(uint8_t instance, uint8_t report_id, hid_report_type_t report_type, uint8_t* buffer, uint16_t reqlen)
{
    (void)instance;
    (void)report_id;
    (void)report_type;
    memset(buffer, 0, reqlen);
    return reqlen;
}

void tud_hid_set_report_cb(uint8_t instance, uint8_t report_id, hid_report_type_t report_type, uint8_t const* buffer, uint16_t bufsize)
{
    (void)instance;
    (void)report_id;
    (void)report_type;

    firmware::debug_buffer("HID SET_REPORT", buffer, bufsize);
    const firmware::ParsedHidCommand parsed = firmware::parse_hid_command(buffer, bufsize);
    if (parsed.payload == nullptr && parsed.command == 0) {
        LOGF("Empty HID report\n");
        return;
    }

    firmware::debug_blink(1, 20);

    switch (parsed.command) {
    case firmware::CMD_SET_COLOR:
        if (parsed.payload_size >= 3) {
            firmware::effects_set_color(parsed.payload[0], parsed.payload[1], parsed.payload[2]);
            LOGF("SET_COLOR r=%u g=%u b=%u\n", parsed.payload[0], parsed.payload[1], parsed.payload[2]);
        } else {
            LOGF("SET_COLOR ignored: payload too small\n");
        }
        break;

    case firmware::CMD_OFF:
        firmware::effects_off();
        LOGF("OFF\n");
        break;

    case firmware::CMD_SET_MODE:
        if (parsed.payload_size >= 1) {
            firmware::effects_set_mode(parsed.payload[0]);
            LOGF("SET_MODE mode=%u\n", parsed.payload[0]);
        } else {
            LOGF("SET_MODE ignored: payload too small\n");
        }
        break;

    case firmware::CMD_MUSIC_LEVEL:
        if (parsed.payload_size >= 1) {
            firmware::effects_set_music_level(parsed.payload[0]);
            LOGF("MUSIC_LEVEL level=%u\n", parsed.payload[0]);
        } else {
            LOGF("MUSIC_LEVEL ignored: payload too small\n");
        }
        break;

    case firmware::CMD_SET_BRIGHTNESS:
        if (parsed.payload_size >= 1) {
            const uint8_t brightness = firmware::clamp_percent(parsed.payload[0]);
            firmware::led_set_brightness(brightness);
            firmware::led_show();
            LOGF("SET_BRIGHTNESS brightness=%u\n", brightness);
        } else {
            LOGF("SET_BRIGHTNESS ignored: payload too small\n");
        }
        break;

    case firmware::CMD_SET_EFFECT_SPEED:
        if (parsed.payload_size >= 1) {
            firmware::effects_set_speed(firmware::clamp_percent(parsed.payload[0]));
            LOGF("SET_EFFECT_SPEED speed=%u\n", firmware::effect_speed);
        } else {
            LOGF("SET_EFFECT_SPEED ignored: payload too small\n");
        }
        break;

    case firmware::CMD_SET_MUSIC_STYLE:
        if (parsed.payload_size >= 1) {
            firmware::effects_set_music_style(parsed.payload[0]);
            LOGF("SET_MUSIC_STYLE style=%u\n", parsed.payload[0]);
        } else {
            LOGF("SET_MUSIC_STYLE ignored: payload too small\n");
        }
        break;

    case firmware::CMD_PING:
        firmware::send_pong();
        LOGF("PING -> PONG\n");
        break;

    default:
        LOGF("Unknown command 0x%02X\n", parsed.command);
        break;
    }
}

void tud_mount_cb(void)
{
    firmware::usb_connected = true;
    firmware::effects_request_connection();
    firmware::debug_blink(2, 40);
    LOGF("USB mounted\n");
}

void tud_umount_cb(void)
{
    firmware::usb_connected = false;
    firmware::effects_off();
    LOGF("USB unmounted\n");
}

bool tud_vendor_control_xfer_cb(uint8_t rhport, uint8_t stage, tusb_control_request_t const* request)
{
    (void)rhport;
    (void)stage;
    (void)request;
    return false;
}

void tud_vendor_rx_cb(uint8_t itf, uint8_t const* buffer, uint16_t bufsize)
{
    (void)itf;
    (void)buffer;
    (void)bufsize;
}
