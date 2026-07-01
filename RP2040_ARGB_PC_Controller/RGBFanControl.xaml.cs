using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace PicoARGBControl
{
    public partial class RGBFanControl : UserControl
    {
        public RGBFanControl()
        {
            InitializeComponent();
            Loaded += RGBFanControl_Loaded;
        }

        private void RGBFanControl_Loaded(object sender, RoutedEventArgs e)
        {
            // Aplica el estado inicial según las propiedades actuales
            ApplyMode(Mode);
        }

        // DependencyProperty para el color del fan (usado en modos estáticos)
        public Color FanColor
        {
            get => (Color)GetValue(FanColorProperty);
            set => SetValue(FanColorProperty, value);
        }
        public static readonly DependencyProperty FanColorProperty =
            DependencyProperty.Register("FanColor", typeof(Color), typeof(RGBFanControl),
                new PropertyMetadata(Colors.DeepSkyBlue, OnFanColorChanged));

        private static void OnFanColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var ctrl = (RGBFanControl)d;
            ctrl.ApplyStaticColor((Color)e.NewValue);
        }

        // DependencyProperty para el modo visual del fan
        // 0: Off, 1: Static Color, 2: Rainbow, 3: Breathing, 4: Chase, 5: Music, 6: Cycle
        public byte Mode
        {
            get => (byte)GetValue(ModeProperty);
            set => SetValue(ModeProperty, value);
        }
        public static readonly DependencyProperty ModeProperty =
            DependencyProperty.Register("Mode", typeof(byte), typeof(RGBFanControl),
                new PropertyMetadata((byte)2, OnModeChanged));

        public double PreviewBrightness
        {
            get => (double)GetValue(PreviewBrightnessProperty);
            set => SetValue(PreviewBrightnessProperty, value);
        }
        public static readonly DependencyProperty PreviewBrightnessProperty =
            DependencyProperty.Register("PreviewBrightness", typeof(double), typeof(RGBFanControl),
                new PropertyMetadata(1.0, OnPreviewBrightnessChanged));

        public bool UseIntensityColors
        {
            get => (bool)GetValue(UseIntensityColorsProperty);
            set => SetValue(UseIntensityColorsProperty, value);
        }
        public static readonly DependencyProperty UseIntensityColorsProperty =
            DependencyProperty.Register("UseIntensityColors", typeof(bool), typeof(RGBFanControl),
                new PropertyMetadata(true, OnUseIntensityColorsChanged));

        private static void OnUseIntensityColorsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var ctrl = (RGBFanControl)d;
            ctrl.ApplyMode(ctrl.Mode);
        }

        private static void OnPreviewBrightnessChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var ctrl = (RGBFanControl)d;
            ctrl.ApplyBrightness();
        }

        private static void OnModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var ctrl = (RGBFanControl)d;
            ctrl.ApplyMode((byte)e.NewValue);
        }

        // Helpers para acceder a elementos nombrados
        private GradientStop? GS(int i)
        {
            return FindName("gs" + i) as GradientStop;
        }
        private Ellipse Outer => (Ellipse)FindName("OuterRing");

        private static Color AudioMeterColor(double level)
        {
            var v = Math.Max(0.0, Math.Min(1.0, level));
            (double At, Color Color)[] stops =
            {
                (0.00, Color.FromRgb(60, 100, 220)),
                (0.22, Color.FromRgb(0, 190, 255)),
                (0.45, Color.FromRgb(0, 245, 150)),
                (0.68, Color.FromRgb(235, 255, 40)),
                (0.84, Color.FromRgb(255, 120, 18)),
                (1.00, Color.FromRgb(255, 20, 0)),
            };

            for (var i = 1; i < stops.Length; i++)
            {
                if (v <= stops[i].At)
                {
                    var span = stops[i].At - stops[i - 1].At;
                    var t = span <= 0 ? 1.0 : (v - stops[i - 1].At) / span;
                    return Color.FromRgb(
                        (byte)(stops[i - 1].Color.R + ((stops[i].Color.R - stops[i - 1].Color.R) * t)),
                        (byte)(stops[i - 1].Color.G + ((stops[i].Color.G - stops[i - 1].Color.G) * t)),
                        (byte)(stops[i - 1].Color.B + ((stops[i].Color.B - stops[i - 1].Color.B) * t)));
                }
            }

            return stops[^1].Color;
        }

        private void SetRingColor(Color c)
        {
            for (int i = 1; i <= 6; i++)
            {
                var gs = GS(i);
                if (gs != null)
                {
                    gs.Color = c;
                }
            }
            if (Outer != null)
            {
                Outer.Opacity = 1.0;
            }
        }

        private void ApplyStaticColor(Color c)
        {
            StopRainbowAnimations();
            StopBreathingAnimation();
            StopPulseGlowAnimation();
            SetRingColor(c);
            ApplyBrightness();
        }

        private void StartRainbowAnimations(double baseSeconds = 2.0)
        {
            StopRainbowAnimations();
            StopBreathingAnimation();
            StopPulseGlowAnimation();
            var durations = new[] { 1.0, 1.2, 1.4, 1.6, 1.8, 2.0 };
            Color[] froms = {
                Color.FromRgb(0, 245, 212),
                Color.FromRgb(67, 165, 255),
                Color.FromRgb(126, 92, 255),
                Color.FromRgb(255, 79, 216),
                Color.FromRgb(255, 184, 77),
                Color.FromRgb(0, 245, 212)
            };
            Color[] tos = {
                Color.FromRgb(67, 165, 255),
                Color.FromRgb(126, 92, 255),
                Color.FromRgb(255, 79, 216),
                Color.FromRgb(255, 184, 77),
                Color.FromRgb(0, 245, 212),
                Color.FromRgb(67, 165, 255)
            };
            for (int i = 1; i <= 6; i++)
            {
                var gs = GS(i);
                if (gs == null) { continue; }
                var anim = new ColorAnimation
                {
                    From = froms[i-1],
                    To = tos[i-1],
                    Duration = TimeSpan.FromSeconds(durations[i-1] * (baseSeconds/2.0)),
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever
                };
                gs.BeginAnimation(GradientStop.ColorProperty, anim);
            }
            if (Outer != null) Outer.Opacity = 1.0;
            ApplyBrightness();
        }

        private void StopRainbowAnimations()
        {
            for (int i = 1; i <= 6; i++)
            {
                var gs = GS(i);
                if (gs != null)
                {
                    gs.BeginAnimation(GradientStop.ColorProperty, null);
                }
            }
        }

        private void StartBreathingAnimation(double seconds = 2.5)
        {
            StopBreathingAnimation();
            if (Outer == null) return;
            var breath = new DoubleAnimation
            {
                From = 0.35,
                To = 1.0,
                Duration = TimeSpan.FromSeconds(seconds),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            Outer.BeginAnimation(UIElement.OpacityProperty, breath);
            StartPulseGlowAnimation(seconds);
        }

        private void StopBreathingAnimation()
        {
            if (Outer == null) return;
            Outer.BeginAnimation(UIElement.OpacityProperty, null);
        }

        private void StartPulseGlowAnimation(double seconds)
        {
            if (GlowRing == null) return;
            var pulse = new DoubleAnimation
            {
                From = 0.25,
                To = 0.78,
                Duration = TimeSpan.FromSeconds(seconds),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            GlowRing.BeginAnimation(UIElement.OpacityProperty, pulse);
        }

        private void StopPulseGlowAnimation()
        {
            if (GlowRing == null) return;
            GlowRing.BeginAnimation(UIElement.OpacityProperty, null);
        }

        // Permite reactividad con audio en modo Música (5)
        public void SetAudioLevel(double level01)
        {
            if (Mode != 5) return;
            var v = Math.Max(0.0, Math.Min(1.0, level01));
            try
            {
                var brightness = CurrentBrightness();
                if (UseIntensityColors)
                {
                    SetRingColor(AudioMeterColor(v));
                }
                else
                {
                    SetRingColor(FanColor);
                }

                OuterRing.Opacity = (0.22 + (0.78 * v)) * brightness;
                GlowRing.Opacity = (0.18 + (0.70 * v)) * brightness;
            }
            catch { }
        }

        private void ApplyMode(byte mode)
        {
            StopRainbowAnimations();
            StopBreathingAnimation();
            StopPulseGlowAnimation();

            if (mode == 0)
            {
                ApplyStaticColor(Colors.Black);
                if (Outer != null) Outer.Opacity = 0.12;
                if (GlowRing != null) GlowRing.Opacity = 0.08;
            }
            else if (mode == 2)
            {
                StartRainbowAnimations();
            }
            else if (mode == 3)
            {
                StartBreathingAnimation();
            }
            else if (mode == 5)
            {
                // Audio Meter: external audio level drives intensity and optional color scale.
                ApplyStaticColor(UseIntensityColors ? AudioMeterColor(0.0) : FanColor);
                try
                {
                    var brightness = CurrentBrightness();
                    OuterRing.Opacity = 0.12 * brightness;
                    GlowRing.Opacity = 0.08 * brightness;
                }
                catch { }
            }
            else
            {
                ApplyStaticColor(FanColor);
            }
        }

        private double CurrentBrightness()
        {
            return Math.Max(0.08, Math.Min(1.0, PreviewBrightness));
        }

        private void ApplyBrightness()
        {
            try
            {
                var brightness = CurrentBrightness();
                if (FanVisual != null)
                {
                    FanVisual.Opacity = Mode == 0 ? 0.45 : 0.42 + (0.58 * brightness);
                }
                if (GlowRing != null && Mode != 5 && Mode != 0)
                {
                    GlowRing.Opacity = 0.28 + (0.42 * brightness);
                }
            }
            catch { }
        }

        // duplicated storyboard-based implementations removed to avoid conflicts
        
    }
}
