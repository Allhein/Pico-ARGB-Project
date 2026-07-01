#include "effects.h"

#include <math.h>
#include "config.h"

namespace firmware {

uint8_t effect_speed = DEFAULT_EFFECT_SPEED;

namespace {

enum class SystemAnimation : uint8_t {
    None,
    Startup,
    Connection,
};

uint8_t current_mode = EFFECT_MODE_MUSIC_VU;
uint8_t music_level = 0;
float music_envelope = 0.0f;
uint8_t music_style = MUSIC_STYLE_INTENSITY_WHEEL;

constexpr Rgb SAFE_DEFAULT_BASE_COLOR = {0, 64, 96};
constexpr uint8_t MUSIC_NOISE_GATE = 6;
constexpr float MUSIC_IDLE_GLOW = 0.0f;
constexpr float MUSIC_BLACK_THRESHOLD = 0.5f;

Rgb base_color = SAFE_DEFAULT_BASE_COLOR;
bool host_color_received = false;

SystemAnimation system_animation = SystemAnimation::None;
uint32_t animation_started_ms = 0;
uint32_t last_frame_ms = 0;
uint32_t last_animation_step = 0xffffffffu;

float rainbow_hue = 0.0f;
float breath_phase = 0.0f;
float chase_position = 0.0f;
float chase_glow = 0.0f;
float cycle_hue = 0.0f;

float speed_scale()
{
    return static_cast<float>(effect_speed) / 100.0f;
}

float clamp01(float value)
{
    if (value < 0.0f) {
        return 0.0f;
    }
    if (value > 1.0f) {
        return 1.0f;
    }
    return value;
}

Rgb scale_color(Rgb color, float intensity)
{
    const float safe = clamp01(intensity);
    return {
        static_cast<uint8_t>(color.r * safe),
        static_cast<uint8_t>(color.g * safe),
        static_cast<uint8_t>(color.b * safe),
    };
}

uint8_t lerp_u8(uint8_t a, uint8_t b, float t)
{
    return static_cast<uint8_t>(static_cast<float>(a) + ((static_cast<float>(b) - static_cast<float>(a)) * clamp01(t)));
}

Rgb lerp_color(Rgb a, Rgb b, float t)
{
    return {
        lerp_u8(a.r, b.r, t),
        lerp_u8(a.g, b.g, t),
        lerp_u8(a.b, b.b, t),
    };
}

Rgb audio_meter_color(float level)
{
    struct Stop {
        float at;
        Rgb color;
    };

    constexpr Stop stops[] = {
        {0.00f, {60, 100, 220}},
        {0.22f, {0, 190, 255}},
        {0.45f, {0, 245, 150}},
        {0.68f, {235, 255, 40}},
        {0.84f, {255, 120, 18}},
        {1.00f, {255, 20, 0}},
    };

    const float safe = clamp01(level);
    for (uint i = 1; i < sizeof(stops) / sizeof(stops[0]); i++) {
        if (safe <= stops[i].at) {
            const float span = stops[i].at - stops[i - 1].at;
            const float t = (span <= 0.0f) ? 1.0f : (safe - stops[i - 1].at) / span;
            return lerp_color(stops[i - 1].color, stops[i].color, t);
        }
    }

    return stops[sizeof(stops) / sizeof(stops[0]) - 1].color;
}

Rgb hsv_to_rgb(float hue, float saturation, float value)
{
    while (hue >= 360.0f) {
        hue -= 360.0f;
    }
    while (hue < 0.0f) {
        hue += 360.0f;
    }

    const float c = value * saturation;
    const float x = c * (1.0f - fabsf(fmodf(hue / 60.0f, 2.0f) - 1.0f));
    const float m = value - c;

    float r = 0.0f;
    float g = 0.0f;
    float b = 0.0f;

    if (hue < 60.0f) {
        r = c; g = x; b = 0.0f;
    } else if (hue < 120.0f) {
        r = x; g = c; b = 0.0f;
    } else if (hue < 180.0f) {
        r = 0.0f; g = c; b = x;
    } else if (hue < 240.0f) {
        r = 0.0f; g = x; b = c;
    } else if (hue < 300.0f) {
        r = x; g = 0.0f; b = c;
    } else {
        r = c; g = 0.0f; b = x;
    }

    return {
        static_cast<uint8_t>((r + m) * 255.0f),
        static_cast<uint8_t>((g + m) * 255.0f),
        static_cast<uint8_t>((b + m) * 255.0f),
    };
}

void cancel_system_animation()
{
    system_animation = SystemAnimation::None;
    last_animation_step = 0xffffffffu;
}

void render_static()
{
    if (!host_color_received) {
        led_clear();
        return;
    }

    led_fill(base_color);
    led_show();
}

void render_rainbow(float dt_ms)
{
    rainbow_hue = fmodf(rainbow_hue + (dt_ms * 0.045f * speed_scale()), 360.0f);
    for (uint i = 0; i < NUM_LEDS; i++) {
        const float led_hue = rainbow_hue + (static_cast<float>(i) * 360.0f / NUM_LEDS);
        led_set_pixel(i, hsv_to_rgb(led_hue, 1.0f, 1.0f));
    }
    led_show();
}

void render_breathing(float dt_ms)
{
    breath_phase += dt_ms * 0.0035f * speed_scale();
    const float raw = (sinf(breath_phase) + 1.0f) * 0.5f;
    const float eased = raw * raw * (3.0f - 2.0f * raw);
    led_fill(scale_color(base_color, eased));
    led_show();
}

void render_chase(float dt_ms)
{
    chase_position = fmodf(chase_position + dt_ms * 0.006f * speed_scale(), static_cast<float>(NUM_LEDS));
    chase_glow += dt_ms * 0.008f * speed_scale();

    for (uint i = 0; i < NUM_LEDS; i++) {
        float dist = fabsf(static_cast<float>(i) - chase_position);
        if (dist > NUM_LEDS / 2.0f) {
            dist = NUM_LEDS - dist;
        }

        float intensity = 0.0f;
        if (dist < 0.5f) {
            intensity = 1.0f;
        } else if (dist < 1.5f) {
            intensity = 0.55f;
        } else if (dist < 2.5f) {
            intensity = 0.22f;
        }

        intensity *= 0.82f + (0.18f * sinf(chase_glow));
        led_set_pixel(i, scale_color(base_color, intensity));
    }
    led_show();
}

void update_music_envelope(float dt_ms)
{
    const float target = (music_level <= MUSIC_NOISE_GATE) ? 0.0f : static_cast<float>(music_level);
    const float time_constant = (target > music_envelope) ? 125.0f : 620.0f;
    const float alpha = clamp01(dt_ms / time_constant);
    music_envelope += (target - music_envelope) * alpha;
    if (music_envelope < MUSIC_BLACK_THRESHOLD && target == 0.0f) {
        music_envelope = 0.0f;
    }
}

void render_music_vu(float dt_ms)
{
    update_music_envelope(dt_ms);

    if (music_envelope <= MUSIC_BLACK_THRESHOLD) {
        led_clear();
        return;
    }

    const float level = clamp01(music_envelope / 255.0f);
    if (music_style == MUSIC_STYLE_PULSE_BASE_COLOR) {
        const float intensity = MUSIC_IDLE_GLOW + ((1.0f - MUSIC_IDLE_GLOW) * level * level);
        for (uint i = 0; i < NUM_LEDS; i++) {
            led_set_pixel(i, scale_color(base_color, intensity));
        }
        led_show();
        return;
    }

    const Rgb color = audio_meter_color(level);
    const float intensity = 0.08f + (0.92f * powf(level, 0.65f));
    const Rgb lit_color = scale_color(color, intensity);

    for (uint i = 0; i < NUM_LEDS; i++) {
        led_set_pixel(i, lit_color);
    }
    led_show();
}

void render_color_cycle(float dt_ms)
{
    cycle_hue = fmodf(cycle_hue + (dt_ms * 0.018f * speed_scale()), 360.0f);
    led_fill(hsv_to_rgb(cycle_hue, 1.0f, 1.0f));
    led_show();
}

bool render_system_animation(uint32_t now_ms)
{
    if (system_animation == SystemAnimation::None) {
        return false;
    }

    const uint32_t elapsed = now_ms - animation_started_ms;
    if (system_animation == SystemAnimation::Startup) {
        const uint32_t step = elapsed / 70u;
        if (step >= NUM_LEDS) {
            cancel_system_animation();
            led_clear();
            return true;
        }
        if (step != last_animation_step) {
            last_animation_step = step;
            led_fill({0, 0, 0});
            led_set_pixel(step, {50, 50, 150});
            led_show();
        }
        return true;
    }

    if (system_animation == SystemAnimation::Connection) {
        constexpr uint32_t duration_ms = 1200;
        if (elapsed >= duration_ms) {
            cancel_system_animation();
            led_clear();
            return true;
        }

        const float phase = static_cast<float>(elapsed) / duration_ms;
        const float pulses = sinf(phase * 4.0f * 3.14159265f);
        const float intensity = pulses * pulses;
        led_fill(scale_color({0, 0, 120}, intensity));
        led_show();
        return true;
    }

    return false;
}

} // namespace

void effects_init()
{
    led_set_brightness(DEFAULT_BRIGHTNESS);
    current_mode = EFFECT_MODE_MUSIC_VU;
    music_level = 0;
    music_envelope = 0.0f;
    music_style = MUSIC_STYLE_INTENSITY_WHEEL;
    effect_speed = DEFAULT_EFFECT_SPEED;
    base_color = SAFE_DEFAULT_BASE_COLOR;
    host_color_received = false;
    last_frame_ms = 0;
    led_clear();
}

void effects_request_startup()
{
    system_animation = SystemAnimation::Startup;
    animation_started_ms = to_ms_since_boot(get_absolute_time());
    last_animation_step = 0xffffffffu;
}

void effects_request_connection()
{
    system_animation = SystemAnimation::Connection;
    animation_started_ms = to_ms_since_boot(get_absolute_time());
    last_animation_step = 0xffffffffu;
}

void effects_set_color(uint8_t r, uint8_t g, uint8_t b)
{
    cancel_system_animation();
    base_color = {r, g, b};
    host_color_received = true;

    if (current_mode == EFFECT_MODE_STATIC) {
        render_static();
    }
}

void effects_set_mode(uint8_t mode)
{
    cancel_system_animation();
    current_mode = mode;
    if (current_mode == EFFECT_MODE_OFF) {
        led_clear();
    } else if (current_mode == EFFECT_MODE_STATIC) {
        render_static();
    } else if (current_mode == EFFECT_MODE_MUSIC_VU) {
        music_level = 0;
        music_envelope = 0.0f;
        led_clear();
    }
}

void effects_off()
{
    cancel_system_animation();
    current_mode = EFFECT_MODE_OFF;
    music_level = 0;
    music_envelope = 0.0f;
    led_clear();
}

void effects_set_music_level(uint8_t level)
{
    cancel_system_animation();
    music_level = level;
}

void effects_set_speed(uint8_t speed)
{
    effect_speed = (speed > 100) ? 100 : speed;
}

void effects_set_music_style(uint8_t style)
{
    music_style = (style == MUSIC_STYLE_PULSE_BASE_COLOR)
        ? MUSIC_STYLE_PULSE_BASE_COLOR
        : MUSIC_STYLE_INTENSITY_WHEEL;
}

uint8_t effects_get_mode()
{
    return current_mode;
}

uint8_t effects_get_music_level()
{
    return music_level;
}

void effects_update(uint32_t now_ms)
{
    if (last_frame_ms != 0 && (now_ms - last_frame_ms) < EFFECT_FRAME_MS) {
        return;
    }

    const float dt_ms = (last_frame_ms == 0) ? EFFECT_FRAME_MS : static_cast<float>(now_ms - last_frame_ms);
    last_frame_ms = now_ms;

    if (render_system_animation(now_ms)) {
        return;
    }

    switch (current_mode) {
    case EFFECT_MODE_RAINBOW:
        render_rainbow(dt_ms);
        break;
    case EFFECT_MODE_BREATHING:
        render_breathing(dt_ms);
        break;
    case EFFECT_MODE_CHASE:
        render_chase(dt_ms);
        break;
    case EFFECT_MODE_MUSIC_VU:
        render_music_vu(dt_ms);
        break;
    case EFFECT_MODE_COLOR_CYCLE:
        render_color_cycle(dt_ms);
        break;
    case EFFECT_MODE_STATIC:
    case EFFECT_MODE_OFF:
    default:
        break;
    }

    (void)host_color_received;
}

} // namespace firmware
