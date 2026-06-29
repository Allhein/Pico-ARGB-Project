#include <stdio.h>
#include <string.h>
#include <math.h>
#include "pico/stdlib.h"
#include "hardware/pio.h"
#include "hardware/timer.h"
#include "ws2812.pio.h"
#include "bsp/board.h"
#include "tusb.h"

#define WS2812_PIN 0
#define NUM_LEDS   8
#define USB_VID    0x20A0
#define USB_PID    0x423D
#define LED_PIN    25

typedef struct { uint8_t r,g,b; } rgb_t;
static rgb_t leds[NUM_LEDS];
static PIO pio = pio0;
static uint sm;

static uint8_t mode = 0;
static uint8_t music_level = 0;
static uint8_t base_r=0, base_g=0, base_b=0;
static uint8_t brightness = 100;
static absolute_time_t last_update;
static bool usb_connected = false;

// Estados para efectos
static int breath_phase = 0;
static int breath_level = 0;
static int wheel_offset = 0;
static int wave_offset = 0;
static uint32_t effect_state_time = 0;

enum {
    CMD_SET_COLOR    = 3,
    CMD_OFF          = 4,
    CMD_SET_MODE     = 5,
    CMD_MUSIC_LEVEL  = 6,
    CMD_SET_BRIGHTNESS = 7,
    CMD_PING         = 0xAA
};

uint32_t pack(uint8_t r,uint8_t g,uint8_t b){ return ((uint32_t)g<<16)|((uint32_t)r<<8)|b; }

void ws2812_init(){
    uint offset = pio_add_program(pio,&ws2812_program);
    sm = pio_claim_unused_sm(pio,true);
    ws2812_program_init(pio,sm,offset,WS2812_PIN,800000,false);
}

void show(){
    for(int i=0;i<NUM_LEDS;i++){
        uint8_t r = (leds[i].r * brightness) / 100;
        uint8_t g = (leds[i].g * brightness) / 100;
        uint8_t b = (leds[i].b * brightness) / 100;
        uint32_t p=pack(r, g, b);
        pio_sm_put_blocking(pio,sm,p<<8u);
    }
    sleep_us(60);
}

void set_all(uint8_t r,uint8_t g,uint8_t b){
    for(int i=0;i<NUM_LEDS;i++){ leds[i].r=r; leds[i].g=g; leds[i].b=b; }
    show();
}

// 🔴 FUNCIÓN MEJORADA: Debug completo del buffer
void debug_buffer(const char* prefix, const uint8_t* buffer, uint16_t size) {
    printf("%s - Tamaño: %d - Hex: ", prefix, size);
    for(int i = 0; i < size && i < 32; i++) {
        printf("%02X ", buffer[i]);
    }
    printf("- ASCII: ");
    for(int i = 0; i < size && i < 32; i++) {
        if(buffer[i] >= 32 && buffer[i] <= 126) {
            printf("%c", buffer[i]);
        } else {
            printf(".");
        }
    }
    printf("\n");
}

void connection_effect() {
    printf("🎮 Iniciando efecto de conexión...\n");
    set_all(0, 0, 0);
    sleep_ms(100);
    
    for(int breath = 0; breath < 2; breath++) {
        for(int intensity = 0; intensity <= 100; intensity += 5) {
            set_all(0, 0, intensity);
            sleep_ms(20);
        }
        for(int intensity = 100; intensity >= 0; intensity -= 5) {
            set_all(0, 0, intensity);
            sleep_ms(20);
        }
    }
    set_all(0, 0, 0);
    printf("✅ Efecto de conexión completado\n");
}

void debug_led_blink(uint16_t duration_ms) {
    gpio_put(LED_PIN, 1);
    sleep_ms(duration_ms);
    gpio_put(LED_PIN, 0);
}

void debug_led_multiple_blink(uint8_t count, uint16_t duration_ms) {
    for(int i = 0; i < count; i++) {
        gpio_put(LED_PIN, 1);
        sleep_ms(duration_ms);
        gpio_put(LED_PIN, 0);
        if(i < count - 1) sleep_ms(duration_ms);
    }
}

void rainbow_effect(){
    static float hue = 0;
    
    for(int i = 0; i < NUM_LEDS; i++){
        // Distribuir el arcoíris uniformemente entre los LEDs
        float led_hue = fmod(hue + (i * 360.0f / NUM_LEDS), 360.0f);
        
        // Conversión HSV to RGB mejorada
        float h = led_hue / 60.0f;  // sector 0 to 5
        int sector = (int)h;
        float fraction = h - sector;
        
        float p = 0.0f;
        float q = 1.0f - fraction;
        float t = fraction;
        
        float r, g, b;
        
        switch(sector){
            case 0: r = 1; g = t; b = p; break;    // Rojo a Amarillo
            case 1: r = q; g = 1; b = p; break;    // Amarillo a Verde
            case 2: r = p; g = 1; b = t; break;    // Verde a Cian
            case 3: r = p; g = q; b = 1; break;    // Cian a Azul
            case 4: r = t; g = p; b = 1; break;    // Azul a Magenta
            case 5: r = 1; g = p; b = q; break;    // Magenta a Rojo
            default: r = 1; g = p; b = q; break;
        }
        
        leds[i].r = (uint8_t)(r * 255);
        leds[i].g = (uint8_t)(g * 255);
        leds[i].b = (uint8_t)(b * 255);
    }
    
    hue = fmod(hue + 2.0f, 360.0f);  // Velocidad del efecto
    show();
}

void breathing_effect(){
    static float t = 0;
    // Usar función de ease-in-out para transición más suave
    float intensity = (sin(t) + 1.0f) / 2.0f;
    
    // Aplicar curva suavizada
    float smoothed = intensity * intensity * (3.0f - 2.0f * intensity);
    
    for(int i = 0; i < NUM_LEDS; i++){
        leds[i].r = (uint8_t)(base_r * smoothed);
        leds[i].g = (uint8_t)(base_g * smoothed);
        leds[i].b = (uint8_t)(base_b * smoothed);
    }
    show();
    t += 0.05f;  // Más lento para mejor fluidez
}

void chase_effect(){
    static int pos = 0;
    static float glow = 0;
    
    for(int i = 0; i < NUM_LEDS; i++){
        // Calcular distancia circular (para efecto continuo)
        int dist = (i - pos + NUM_LEDS) % NUM_LEDS;
        if(dist > NUM_LEDS/2) dist = NUM_LEDS - dist;
        
        // Efecto de cola suavizado con caída exponencial
        float intensity;
        if(dist == 0) {
            intensity = 1.0f;  // LED principal
        } else if(dist == 1) {
            intensity = 0.6f;  // Primer seguidor
        } else if(dist == 2) {
            intensity = 0.3f;  // Segundo seguidor
        } else {
            intensity = 0.0f;  // Apagado
        }
        
        // Añadir efecto de brillo pulsante
        intensity *= (0.8f + 0.2f * sin(glow));
        
        leds[i].r = (uint8_t)(base_r * intensity);
        leds[i].g = (uint8_t)(base_g * intensity);
        leds[i].b = (uint8_t)(base_b * intensity);
    }
    show();
    pos = (pos + 1) % NUM_LEDS;
    glow += 0.3f;
}

void music_effect(){
    // Convertir nivel de música a número de LEDs encendidos
    int leds_on = (music_level * NUM_LEDS) / 255;
    
    for(int i = 0; i < NUM_LEDS; i++){
        if(i < leds_on) {
            // LEDs encendidos con intensidad completa
            leds[i].r = base_r;
            leds[i].g = base_g;
            leds[i].b = base_b;
        } else if(i == leds_on && leds_on < NUM_LEDS) {
            // LED parcial (efecto de metro)
            float partial = (music_level * NUM_LEDS / 255.0f) - leds_on;
            leds[i].r = (uint8_t)(base_r * partial);
            leds[i].g = (uint8_t)(base_g * partial);
            leds[i].b = (uint8_t)(base_b * partial);
        } else {
            // LEDs apagados
            leds[i].r = 0;
            leds[i].g = 0;
            leds[i].b = 0;
        }
    }
    show();
}

void color_cycle_effect(){
    static float hue = 0;
    static uint32_t last_time = 0;
    
    // Cambiar color cada 200ms
    if(to_ms_since_boot(get_absolute_time()) - last_time > 200){
        hue = fmod(hue + 10.0f, 360.0f);  // Avanzar 10 grados por ciclo
        last_time = to_ms_since_boot(get_absolute_time());
        
        // Convertir HSV a RGB
        float h = hue / 60.0f;
        int sector = (int)h;
        float fraction = h - sector;
        
        float p = 0.0f;
        float q = 1.0f - fraction;
        float t = fraction;
        
        float r, g, b;
        
        switch(sector){
            case 0: r = 1; g = t; b = p; break;
            case 1: r = q; g = 1; b = p; break;
            case 2: r = p; g = 1; b = t; break;
            case 3: r = p; g = q; b = 1; break;
            case 4: r = t; g = p; b = 1; break;
            default: r = 1; g = p; b = q; break;
        }
        
        base_r = (uint8_t)(r * 255);
        base_g = (uint8_t)(g * 255);
        base_b = (uint8_t)(b * 255);
    }
    
    set_all(base_r, base_g, base_b);
}

// ---------------- TinyUSB descriptors ----------------
uint8_t const desc_hid_report[]={
  0x06,0x00,0xFF,0x09,0x01,0xA1,0x01,
  0x15,0x00,0x26,0xFF,0x00,
  0x75,0x08,0x95,0x40,
  0x09,0x01,0x81,0x02,
  0x09,0x01,0x91,0x02,
  0xC0
};

tusb_desc_device_t const desc_device={
  .bLength=sizeof(tusb_desc_device_t),
  .bDescriptorType=TUSB_DESC_DEVICE,
  .bcdUSB=0x0200,
  .bDeviceClass=0x00,
  .bDeviceSubClass=0x00,
  .bDeviceProtocol=0x00,
  .bMaxPacketSize0=64,
  .idVendor=USB_VID,
  .idProduct=USB_PID,
  .bcdDevice=0x0100,
  .iManufacturer=0x01,
  .iProduct=0x02,
  .iSerialNumber=0x03,
  .bNumConfigurations=0x01
};

enum { ITF_NUM_HID, ITF_TOTAL };
#define CONFIG_TOTAL_LEN (TUD_CONFIG_DESC_LEN + TUD_HID_DESC_LEN)
#define EPNUM_HID_IN 0x81

uint8_t const desc_configuration[]={
  TUD_CONFIG_DESCRIPTOR(1,ITF_TOTAL,0,CONFIG_TOTAL_LEN,TUSB_DESC_CONFIG_ATT_REMOTE_WAKEUP,100),
  TUD_HID_DESCRIPTOR(ITF_NUM_HID,4,HID_ITF_PROTOCOL_NONE,sizeof(desc_hid_report),EPNUM_HID_IN,64,10)
};

const char* string_desc_arr[]={
  (const char[]){0x09,0x04},
  "OpenRGB Project","Pico ARGB Controller","PICO-AR12-001","HID Interface"
};
static uint16_t _desc_str[32];

uint8_t const* tud_descriptor_device_cb(void){return (uint8_t const*)&desc_device;}
uint8_t const* tud_descriptor_configuration_cb(uint8_t index){(void)index;return desc_configuration;}
uint16_t const* tud_descriptor_string_cb(uint8_t index,uint16_t langid){
    (void)langid;
    uint8_t chr_count;
    if(index==0){memcpy(&_desc_str[1],string_desc_arr[0],2);chr_count=1;}
    else{
        if(index>=sizeof(string_desc_arr)/sizeof(string_desc_arr[0]))return NULL;
        const char* str=string_desc_arr[index];
        chr_count=strlen(str);
        if(chr_count>31)chr_count=31;
        for(uint8_t i=0;i<chr_count;i++)_desc_str[1+i]=str[i];
    }
    _desc_str[0]=(TUSB_DESC_STRING<<8)|(2*chr_count+2);
    return _desc_str;
}
uint8_t const* tud_hid_descriptor_report_cb(uint8_t instance){(void)instance;return desc_hid_report;}

// ---------------- HID callbacks ----------------
uint16_t tud_hid_get_report_cb(uint8_t instance, uint8_t report_id, hid_report_type_t report_type, uint8_t* buffer, uint16_t reqlen){
    printf("🔍 GET_REPORT - Instancia: %d, Report ID: %d, Tipo: %d, ReqLen: %d\n", 
           instance, report_id, report_type, reqlen);
    memset(buffer, 0, reqlen);
    return reqlen;
}

void tud_hid_set_report_cb(uint8_t instance, uint8_t report_id, hid_report_type_t report_type, uint8_t const* buffer, uint16_t bufsize){
    printf("\n🎯 HID SET_REPORT RECIBIDO:\n");
    printf("📍 Instancia: %d, Report ID: %d, Tipo: %d, Tamaño: %d\n", 
           instance, report_id, report_type, bufsize);
    
    // 🔴 DEBUG MEJORADO: Mostrar buffer completo
    debug_buffer("📦 BUFFER COMPLETO", buffer, bufsize);
    
    if(bufsize < 1) {
        printf("❌ Buffer vacío\n");
        return;
    }
    
    // ⚡ PRUEBA: Diferentes formas de interpretar el comando
    uint8_t cmd;
    
    // Intentar diferentes posiciones del comando
    if(bufsize >= 2 && buffer[0] == 0) {
        // Formato 1: [0]=ReportID, [1]=Comando
        cmd = buffer[1];
        printf("🔹 Comando en posición [1]: 0x%02X\n", cmd);
    } else if(bufsize >= 1) {
        // Formato 2: [0]=Comando directamente
        cmd = buffer[0];
        printf("🔹 Comando en posición [0]: 0x%02X\n", cmd);
    } else {
        printf("❌ No se puede extraer comando\n");
        return;
    }
    
    debug_led_blink(50);
    
    // 🔄 PROCESAR COMANDOS CON DIFERENTES OFFSETS
    switch(cmd){
        case CMD_SET_COLOR:
            printf("🎨 SET_COLOR detectado\n");
            if(bufsize >= 5 && buffer[0] == 0) {
                // Formato: [0]=0, [1]=CMD, [2]=R, [3]=G, [4]=B
                base_r = buffer[2]; base_g = buffer[3]; base_b = buffer[4];
                printf("🎯 Color (offset 2-4): R=%d, G=%d, B=%d\n", base_r, base_g, base_b);
            } else if(bufsize >= 4) {
                // Formato: [0]=CMD, [1]=R, [2]=G, [3]=B
                base_r = buffer[1]; base_g = buffer[2]; base_b = buffer[3];
                printf("🎯 Color (offset 1-3): R=%d, G=%d, B=%d\n", base_r, base_g, base_b);
            } else {
                printf("❌ SET_COLOR: Buffer insuficiente\n");
                break;
            }
            set_all(base_r, base_g, base_b);
            debug_led_multiple_blink(2, 100);
            break;
            
        case CMD_OFF:
            printf("🔴 OFF detectado\n");
            mode = 0;
            set_all(0, 0, 0);
            debug_led_multiple_blink(3, 100);
            break;
            
        case CMD_SET_MODE:
            printf("🔄 SET_MODE detectado\n");
            if(bufsize >= 3 && buffer[0] == 0) {
                mode = buffer[2];
                printf("🎯 Modo (offset 2): %d\n", mode);
            } else if(bufsize >= 2) {
                mode = buffer[1];
                printf("🎯 Modo (offset 1): %d\n", mode);
            } else {
                printf("❌ SET_MODE: Buffer insuficiente\n");
                break;
            }
            debug_led_multiple_blink(1, 200);
            break;
            
        case CMD_MUSIC_LEVEL:
            printf("🎵 MUSIC_LEVEL detectado\n");
            if(bufsize >= 3 && buffer[0] == 0) {
                music_level = buffer[2];
                printf("🎯 Nivel música (offset 2): %d\n", music_level);
            } else if(bufsize >= 2) {
                music_level = buffer[1];
                printf("🎯 Nivel música (offset 1): %d\n", music_level);
            } else {
                printf("❌ MUSIC_LEVEL: Buffer insuficiente\n");
                break;
            }
            debug_led_blink(30);
            break;
            
        case CMD_PING:
            printf("🎯 PING detectado\n");
            if (tud_hid_ready()) {
                uint8_t pong[64] = {0};
                memcpy(pong, "PONG", 4);
                tud_hid_report(0, pong, sizeof(pong));
                printf("📤 PONG enviado\n");
            }
            debug_led_multiple_blink(4, 80);
            break;
            
        default:
            printf("❌ Comando desconocido: 0x%02X\n", cmd);
            printf("💡 Comandos esperados: 0xAA(PING), 0x03(SET_COLOR), 0x04(OFF), 0x05(SET_MODE), 0x06(MUSIC_LEVEL)\n");
            debug_led_multiple_blink(5, 50);
            break;
    }
    printf("=== FIN COMANDO ===\n\n");
}

void tud_mount_cb(void) {
    printf("🔌 USB conectado - Dispositivo montado\n");
    usb_connected = true;
    connection_effect();
}

void tud_umount_cb(void) {
    printf("🔌 USB desconectado\n");
    usb_connected = false;
    set_all(0, 0, 0);
}

// ---------------- main ----------------
int main(){
    stdio_init_all();
    board_init();
    
    gpio_init(LED_PIN);
    gpio_set_dir(LED_PIN, GPIO_OUT);
    
    ws2812_init();
    tusb_init();

    printf("\n");
    printf("=== RP2040 ARGB Controller - DEBUG AVANZADO ===\n");
    printf("🔍 Esperando comandos HID...\n");
    printf("💡 Comandos esperados:\n");
    printf("   0xAA = PING\n");
    printf("   0x03 = SET_COLOR\n"); 
    printf("   0x04 = OFF\n");
    printf("   0x05 = SET_MODE\n");
    printf("   0x06 = MUSIC_LEVEL\n");

    debug_led_multiple_blink(3, 200);
    
    // Efecto de inicio
    for(int i = 0; i < NUM_LEDS; i++) {
        set_all(0, 0, 0);
        leds[i].r = 50; leds[i].g = 50; leds[i].b = 150;
        show();
        sleep_ms(100);
    }
    set_all(0, 0, 0);
    printf("✅ Firmware listo - Esperando comandos...\n\n");
    
    last_update = get_absolute_time();

    while(true){
        tud_task();

        if(absolute_time_diff_us(last_update,get_absolute_time())>30000){
            last_update = get_absolute_time();
            switch(mode){
                case 2: rainbow_effect(); break;
                case 3: breathing_effect(); break;
                case 4: chase_effect(); break;
                case 5: music_effect(); break;
                case 6: color_cycle_effect(); break;
            }
        }
        tight_loop_contents();
    }
    return 0;
}