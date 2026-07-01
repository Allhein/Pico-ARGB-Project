#include "bsp/board.h"
#include "pico/stdlib.h"
#include "tusb.h"

#include "config.h"
#include "effects.h"
#include "led_driver.h"
#include "protocol.h"

int main()
{
    stdio_init_all();
    board_init();

    firmware::debug_init();
    firmware::led_driver_init();
    firmware::effects_init();
    tusb_init();

    firmware::protocol_log_banner();
    firmware::effects_request_startup();

    while (true) {
        tud_task();

        const uint32_t now_ms = to_ms_since_boot(get_absolute_time());
        firmware::debug_service(now_ms);
        firmware::effects_update(now_ms);

        tight_loop_contents();
    }

    return 0;
}
