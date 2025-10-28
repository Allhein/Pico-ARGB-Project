// usb_descriptors.c
// Minimal, static USB descriptors for RP2040 Pico
// CDC (serial) + Vendor interface (OpenRGB/Aura)
// Assumes CFG_TUD_ENDPOINT0_SIZE = 64 and tusb_config.h set:
//   CFG_TUD_HID = 0, CFG_TUD_CDC = 1, CFG_TUD_VENDOR = 1

#include "tusb.h"
#include <string.h>

// -----------------------------------------------------------------------------
// Device descriptor
// -----------------------------------------------------------------------------
uint8_t const* tud_descriptor_device_cb(void)
{
    static tusb_desc_device_t const desc = {
        .bLength            = sizeof(tusb_desc_device_t),
        .bDescriptorType    = TUSB_DESC_DEVICE,
        .bcdUSB             = 0x0200,
        .bDeviceClass       = 0x00, // per-interface
        .bDeviceSubClass    = 0x00,
        .bDeviceProtocol    = 0x00,
        .bMaxPacketSize0    = 64,
        .idVendor           = 0x1209, // replace with your VID if needed
        .idProduct          = 0xA412, // replace with your PID if needed
        .bcdDevice          = 0x0102,
        .iManufacturer      = 0x01,
        .iProduct           = 0x02,
        .iSerialNumber      = 0x03,
        .bNumConfigurations = 0x01
    };
    return (uint8_t const*)&desc;
}

// -----------------------------------------------------------------------------
// Configuration descriptor: CDC (2 ifaces) + Vendor (1 iface) => bNumInterfaces = 3
// Endpoints chosen:
//   CDC notif IN  0x81 (8 bytes)
//   CDC data OUT  0x02
//   CDC data IN   0x82
//   Vendor OUT    0x03
//   Vendor IN     0x83
// -----------------------------------------------------------------------------
uint8_t const* tud_descriptor_configuration_cb(uint8_t index)
{
    (void) index;

    static uint8_t const desc[] = {
        // Configuration (value, #interfaces, string_index, total_len, attr, power)
        TUD_CONFIG_DESCRIPTOR(1, 3, 0,
            TUD_CONFIG_DESC_LEN + TUD_CDC_DESC_LEN + TUD_VENDOR_DESC_LEN,
            TUSB_DESC_CONFIG_ATT_REMOTE_WAKEUP, 100),

        // CDC: itf 0 = control, itf 1 = data
        TUD_CDC_DESCRIPTOR(0, 4, 0x81, 8, 0x02, 0x82, 64),

        // Vendor: itf 2
        TUD_VENDOR_DESCRIPTOR(2, 5, 0x03, 0x83, 64)
    };

    return desc;
}

// -----------------------------------------------------------------------------
// String descriptors
//   index 0 : supported language (0x0409)
//   index 1 : manufacturer
//   index 2 : product
//   index 3 : serial
//   index 4 : CDC interface string
//   index 5 : Vendor interface string
// -----------------------------------------------------------------------------
uint16_t const* tud_descriptor_string_cb(uint8_t index, uint16_t langid)
{
    (void) langid;

    static uint16_t desc_str[32];

    if (index == 0) {
        // LangID (English - United States)
        desc_str[0] = (TUSB_DESC_STRING << 8) | (2 + 2);
        desc_str[1] = 0x0409;
        return desc_str;
    }

    const char* strings[] = {
        [1] = "Allhein Code",     // iManufacturer
        [2] = "Pico AR12 Controller",  // iProduct
        [3] = "AR12-PICO-001",         // iSerialNumber
        [4] = "Pico CDC",              // CDC interface string
        [5] = "Pico Vendor"            // Vendor interface string
    };

    if (index >= sizeof(strings)/sizeof(strings[0]) || strings[index] == NULL) return NULL;

    const char* str = strings[index];
    int len = (int)strlen(str);
    if (len > 31) len = 31;

    // first byte: descriptor header (bLength & bDescriptorType packed)
    desc_str[0] = (TUSB_DESC_STRING << 8) | (2 * len + 2);
    for (int i = 0; i < len; i++) {
        desc_str[i + 1] = (uint16_t)str[i];
    }
    return desc_str;
}

// -----------------------------------------------------------------------------
// Optional control callback for vendor class (stub)
// Return true if handled
// -----------------------------------------------------------------------------
bool tud_vendor_control_xfer_cb(uint8_t rhport, uint8_t stage, tusb_control_request_t const* request)
{
    (void) rhport;
    (void) stage;
    (void) request;
    return false;
}

// -----------------------------------------------------------------------------
// Minimal vendor RX callback (implementation in main firmware typically)
// This file provides a weak default; replace/implement in application as needed.
// -----------------------------------------------------------------------------
void __attribute__((weak)) tud_vendor_rx_cb(uint8_t itf, uint8_t const* buffer, uint16_t bufsize)
{
    // Default: ignore. Application implements actual handling.
    (void) itf;
    (void) buffer;
    (void) bufsize;
}

// -----------------------------------------------------------------------------
// End of file
// -----------------------------------------------------------------------------
