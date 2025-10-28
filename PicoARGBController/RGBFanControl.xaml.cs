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
        // 1: Static Color, 2: Rainbow, 3: Breathing, 4: Chase, 5: Music, 6: Cycle, 7: Off
        public byte Mode
        {
            get => (byte)GetValue(ModeProperty);
            set => SetValue(ModeProperty, value);
        }
        public static readonly DependencyProperty ModeProperty =
            DependencyProperty.Register("Mode", typeof(byte), typeof(RGBFanControl),
                new PropertyMetadata((byte)2, OnModeChanged));

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

        private void ApplyStaticColor(Color c)
        {
            StopRainbowAnimations();
            StopBreathingAnimation();
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

        private void StartRainbowAnimations(double baseSeconds = 2.0)
        {
            StopRainbowAnimations();
            StopBreathingAnimation();
            var durations = new[] { 1.0, 1.2, 1.4, 1.6, 1.8, 2.0 };
            Color[] froms = { Colors.Red, Colors.Orange, Colors.Yellow, Colors.Green, Colors.Blue, Colors.Violet };
            Color[] tos   = { Colors.Orange, Colors.Yellow, Colors.Green, Colors.Blue, Colors.Violet, Colors.Red };
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
        }

        private void StopBreathingAnimation()
        {
            if (Outer == null) return;
            Outer.BeginAnimation(UIElement.OpacityProperty, null);
        }

        // Permite reactividad con audio en modo Música (5)
        public void SetAudioLevel(double level01)
        {
            if (Mode != 5) return;
            var v = Math.Max(0.0, Math.Min(1.0, level01));
            try
            {
                // Usar opacidad del aro externo para reflejar intensidad
                OuterRing.Opacity = 0.3 + (0.7 * v);
            }
            catch { }
        }

        private void ApplyMode(byte mode)
        {
            StopRainbowAnimations();
            StopBreathingAnimation();

            if (mode == 2)
            {
                StartRainbowAnimations();
            }
            else if (mode == 3)
            {
                StartBreathingAnimation();
            }
            else if (mode == 5)
            {
                // Modo Música: color estático y reactividad externa
                ApplyStaticColor(FanColor);
                try { OuterRing.Opacity = 0.3; } catch { }
            }
            else
            {
                ApplyStaticColor(FanColor);
            }
        }

        // duplicated storyboard-based implementations removed to avoid conflicts
        
    }
}