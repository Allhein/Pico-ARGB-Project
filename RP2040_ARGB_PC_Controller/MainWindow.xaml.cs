using HidSharp;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Text.Json;
using System.Windows.Media.Animation;

namespace PicoARGBControl
{
    public partial class MainWindow : Window
    {
        private HidManager _hid = new HidManager();
        private AudioAnalyzer? _audio;
        private readonly DispatcherTimer _presenceTimer;
        private readonly string _configDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Local", "PicoARGBController");
        private readonly string _configPath;
        private readonly List<Button> _modeButtons = new();

        // Commands (matching firmware)
        private const byte CMD_PING = 0xAA;
        private const byte CMD_SET_COLOR = 0x03;
        private const byte CMD_OFF = 0x04;         // ← CORREGIDO: OFF es 0x04
        private const byte CMD_SET_MODE = 0x05;    // ← CORREGIDO: SET_MODE es 0x05  
        private const byte CMD_MUSIC_LEVEL = 0x06;
        private const byte CMD_SET_BRIGHTNESS = 0x07;
        private const byte CMD_SET_EFFECT_SPEED = 0x08;
        private const byte CMD_SET_MUSIC_STYLE = 0x09;
        private const byte MAX_SAFE_BRIGHTNESS = 90;

        // Estado UI actual (modo y color seleccionados desde el panel izquierdo)
        private byte _selectedMode = 1; // 1=Static
        private System.Windows.Media.Color _selectedColor = Colors.Cyan;

        // Throttling para evitar flood HID en modo música
        private DateTime _lastMusicLevelSend = DateTime.MinValue;
        private readonly TimeSpan _musicLevelSendInterval = TimeSpan.FromMilliseconds(55); // enviar ~18 updates/s
        private AudioSourceKind _selectedAudioSource = AudioSourceKind.SystemAudio;
        private bool _updatingColorUi;


        public MainWindow()
        {
            InitializeComponent();
            _configPath = System.IO.Path.Combine(_configDir, "config.json");

            var brush = (LinearGradientBrush)this.Resources["AnimatedRGBBrush"];
            var stop1 = brush.GradientStops[0];
            var stop2 = brush.GradientStops[1];
            var stop3 = brush.GradientStops[2];

            var duration = new Duration(TimeSpan.FromSeconds(2));

            // Color inicial del panel estático
            try { tbStaticHex.Text = "#00ffff"; } catch { }

            // Populate modes panel with colorful buttons (borders will reflect selected color)
            (string Label, byte Mode)[] modes =
            {
                ("Static Color", 1),
                ("Rainbow", 2),
                ("Breathing", 3),
                ("Chase", 4),
                ("Audio Meter", 5),
                ("Cycle", 6),
                ("Off", 0),
            };
            for (int i = 0; i < modes.Length; i++)
            {
                var b = CreateModeButton(modes[i].Label, modes[i].Mode);
                ModesPanel.Children.Add(b);
            }

            var anim1 = new ColorAnimation
            {
                From = Colors.Red,
                To = Colors.Blue,
                Duration = duration,
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };

            var anim2 = new ColorAnimation
            {
                From = Colors.Green,
                To = Colors.Red,
                Duration = duration,
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };

            var anim3 = new ColorAnimation
            {
                From = Colors.Blue,
                To = Colors.Green,
                Duration = duration,
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };

            // Color inicial para Static
            SetSelectedColor(_selectedColor, updateHex: true, applyPreview: false);
            SetSelectedAudioSource(_selectedAudioSource);
            UpdateMusicTuningLabels();
            if (lblBrightness != null) lblBrightness.Text = $"{GetBrightnessPercent()}%";
            if (lblBrightnessFirmware != null) lblBrightnessFirmware.Text = $"FW {GetFirmwareBrightnessPercent()}%";
            UpdateEffectSpeedLabel();
            SetAudioStatus("Audio detenido");
            txtLog.Text = "";

            // Cargar configuración previa y aplicarla
            LoadConfigApplyToUI();
            ShowParamsForMode(_selectedMode);
            ApplyFansFromUI();
            UpdateModeButtonSelection();

            // Siempre activar el acento arcoíris en bordes y fondo de logo
            ApplyAccentFromUI();

            // Intento de autoconexión rápido
            TryAutoConnect();

            // Poll presence every 1s and update UI accordingly
            _presenceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _presenceTimer.Tick += PresenceTimer_Tick;
            _presenceTimer.Start();

            stop1.BeginAnimation(GradientStop.ColorProperty, anim1);
            stop2.BeginAnimation(GradientStop.ColorProperty, anim2);
            stop3.BeginAnimation(GradientStop.ColorProperty, anim3);
        }

        private Button CreateModeButton(string label, byte tag)
        {
            var btn = new Button()
            {
                Tag = tag,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = new SolidColorBrush(Color.FromRgb(17, 23, 35)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(40, 50, 72)),
            };
            btn.Content = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Children =
                {
                    new TextBlock { Text = label, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold },
                    new TextBlock { Text = ModeDescription(tag), Foreground = new SolidColorBrush(Color.FromRgb(152, 165, 189)), FontSize = 11, Margin = new Thickness(0, 2, 0, 0) }
                }
            };
            // aplicar estilo neón si existe en recursos
            try { btn.Style = (Style)FindResource("ModeButton"); } catch { }
            _modeButtons.Add(btn);
            
            btn.Click += (s, e) =>
            {
                byte mode = (byte)((Button)s).Tag;
                _selectedMode = mode;
                UpdateModeButtonSelection();

                // Mostrar/ocultar subpaneles según el modo
                ShowParamsForMode(mode);

                // Música: encender captura sólo si está marcado y el modo es Music
                if (mode == 5 && chkMusic.IsChecked == true) StartAudioCapture(); else StopAudioCapture();

                // Previsualizar fans localmente (sin enviar a RP2040 aún)
                ApplyFansFromUI();

                // Acento visual: siempre arcoíris dinámico (no editable)
                ApplyAccentFromUI();

                Log($"🎛 Modo seleccionado: {label} ({mode})");
            };
            return btn;
        }

        private string ModeDescription(byte mode) => mode switch
        {
            0 => "All LEDs off",
            1 => "Solid color",
            2 => "Animated spectrum",
            3 => "Smooth pulse",
            4 => "Moving trail",
            5 => "Razer-style audio meter",
            6 => "Continuous color shift",
            _ => "Firmware mode"
        };

        private void UpdateModeButtonSelection()
        {
            foreach (var button in _modeButtons)
            {
                var isSelected = button.Tag is byte mode && mode == _selectedMode;
                button.Background = new SolidColorBrush(isSelected ? Color.FromRgb(28, 40, 61) : Color.FromRgb(17, 23, 35));
                button.BorderBrush = new SolidColorBrush(isSelected ? Color.FromRgb(0, 245, 212) : Color.FromRgb(40, 50, 72));
            }
        }

        private void SetConnectionStatus(string status, string hint, Color accent, bool enabled)
        {
            lblStatus.Text = status;
            lblStatus.Foreground = new SolidColorBrush(accent);
            lblHint.Text = hint;
            if (StatusDot != null)
            {
                StatusDot.Fill = new SolidColorBrush(accent);
                StatusDot.Effect = new DropShadowEffect { Color = accent, BlurRadius = 10, ShadowDepth = 0, Opacity = 0.65 };
            }
            if (StatusPill != null)
            {
                StatusPill.BorderBrush = new SolidColorBrush(Color.FromArgb(190, accent.R, accent.G, accent.B));
            }
            btnConnect.IsEnabled = enabled;
        }

        private void PresenceTimer_Tick(object? sender, EventArgs e)
        {
            try
            {
                var vidHex = txtVid.Text.Trim();
                var pidHex = txtPid.Text.Trim();
                if (!int.TryParse(vidHex, System.Globalization.NumberStyles.HexNumber, null, out int vid) ||
                    !int.TryParse(pidHex, System.Globalization.NumberStyles.HexNumber, null, out int pid))
                {
                    SetConnectionStatus("Error", "VID/PID inválidos", Color.FromRgb(255, 184, 77), true);
                    return;
                }

                bool present = _hid.IsDevicePresent(vid, pid);
                bool connected = _hid.IsOpen;

                // ✅ ACTUALIZACIÓN: Mostrar estados separados
                if (connected)
                {
                    SetConnectionStatus("Conectado", "Comunicación activa con el dispositivo", Color.FromRgb(0, 245, 144), true);
                    btnConnect.Content = "Desconectar";
                }
                else if (present)
                {
                    SetConnectionStatus("Presente", "Dispositivo detectado - Listo para conectar", Color.FromRgb(67, 165, 255), true);
                    btnConnect.Content = "Conectar";
                }
                else
                {
                    SetConnectionStatus("No detectado", "Esperando controlador...", Color.FromRgb(255, 93, 115), false);
                    btnConnect.Content = "Conectar";
                }
            }
            catch { }
        }

        private void Log(string s)
        {
            Dispatcher.Invoke(() =>
            {
                txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {s}\r\n");
                txtLog.ScrollToEnd();
            });
        }

        private async void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            if (_hid.IsOpen)
            {
                DisconnectDevice();
                return;
            }

            if (!int.TryParse(txtVid.Text, System.Globalization.NumberStyles.HexNumber, null, out int vid) ||
                !int.TryParse(txtPid.Text, System.Globalization.NumberStyles.HexNumber, null, out int pid))
            {
                MessageBox.Show("VID/PID inválidos.");
                return;
            }

            btnConnect.IsEnabled = false;
            SetConnectionStatus("Conectando", "Abriendo dispositivo HID...", Color.FromRgb(255, 184, 77), false);

            try
            {
                // Verificar presencia antes de conectar
                if (!_hid.IsDevicePresent(vid, pid))
                {
                    SetConnectionStatus("No detectado", "Dispositivo no encontrado", Color.FromRgb(255, 93, 115), true);
                    btnConnect.IsEnabled = true;
                    Log("Dispositivo no detectado.");
                    return;
                }

                Log($"Intentando conectar con VID: 0x{vid:X4}, PID: 0x{pid:X4}");

                var ok = await Task.Run(() => _hid.Connect((int)vid, (int)pid));
                if (!ok)
                {
                    SetConnectionStatus("Error", "No se pudo abrir el dispositivo", Color.FromRgb(255, 184, 77), true);
                    btnConnect.IsEnabled = true;
                    Log("No se pudo abrir el dispositivo");
                    return;
                }

                Log("✅ Dispositivo HID abierto correctamente");
                Log("Enviando PING...");

                bool pingSuccess = false;
                for (int i = 0; i < 3; i++)
                {
                    Log($"Intento de PING {i + 1}/3...");
                    pingSuccess = await Task.Run(() => _hid.Ping(500));
                    if (pingSuccess) { Log("✅ PONG recibido"); break; }
                    else { Log("⚠ No se recibió PONG"); await Task.Delay(100); }
                }

                _hid.SetReadCallback(bytes =>
                {
                    if (bytes != null && bytes.Length > 0)
                    {
                        string hex = BitConverter.ToString(bytes);
                        Log($"📥 RX: {hex}");
                        string dataStr = Encoding.ASCII.GetString(bytes);
                        if (dataStr.Contains("PONG")) Log("✅ PONG detectado");
                    }
                });

                if (pingSuccess)
                {
                    SetConnectionStatus("Conectado", "Comunicación bidireccional activa", Color.FromRgb(0, 245, 144), true);
                }
                else
                {
                    SetConnectionStatus("Conectado", "Sin PONG del firmware", Color.FromRgb(255, 184, 77), true);
                }

                SendBrightness(GetBrightnessPercent(), logIfDisconnected: false);
                SendEffectSpeed(GetEffectSpeedPercent(), logIfDisconnected: false);
                btnConnect.Content = "Desconectar";
            }
            catch (Exception ex)
            {
                Log($"❌ Error de conexión: {ex.Message}");
                MessageBox.Show($"Error de conexión: {ex.Message}");
                DisconnectDevice();
            }
            finally
            {
                btnConnect.IsEnabled = true;
            }
        }

        private void DisconnectDevice()
        {
            _hid.Close();
            btnConnect.Content = "Conectar";
            SetConnectionStatus("Desconectado", "Dispositivo desconectado", Color.FromRgb(255, 93, 115), true);
            Log("🔌 Desconectado del dispositivo.");
            StopAudioCapture();
        }

        private void BtnOff_Click(object sender, RoutedEventArgs e)
        {
            if (!_hid.IsOpen)
            {
                Log("❌ Dispositivo no conectado. No se puede apagar.");
                return;
            }
            Log("🔴 Enviando OFF...");
            SendOff();
            Log("✅ Comando OFF enviado");
        }

        private void BtnSendMode_Click(object sender, RoutedEventArgs e)
        {
            var mode = GetSelectedMode();
            var c = GetStaticColor();
            if (_hid.IsOpen)
            {
                SendMode(mode);
                if (mode == 5) SendMusicStyle(UseIntensityColors(), logIfDisconnected: false);
                if (mode == 1 || (mode == 5 && !UseIntensityColors())) SendHidColor(c.R, c.G, c.B);
                Log($"📤 Enviado modo {mode} {(mode == 1 || (mode == 5 && !UseIntensityColors()) ? $"con color {c.R} {c.G} {c.B}" : "")}");
            }
            else
            {
                Log("⚠ Dispositivo no conectado");
            }
        }

        private void SendMode(byte mode)
        {
            if (!_hid.IsOpen) { Log("❌ No conectado"); return; }
            if (mode == 0)
            {
                SendOff();
                return;
            }

            Log($"🔄 SET_MODE: {mode}");
            _hid.SendCommand(CMD_SET_MODE, new byte[] { mode });
            Log($"✅ Modo {mode} enviado");
        }

        private void SendOff()
        {
            _hid.SendCommand(CMD_OFF, null);
        }

        private void SendHidColor(byte r, byte g, byte b)
        {
            if (!_hid.IsOpen) { Log("❌ No conectado"); return; }
            if (r == 0 && g == 0 && b == 0)
            {
                Log("⚫ Negro seleccionado: enviando OFF");
                SendOff();
                SendMusicLevel(0, logIfDisconnected: false);
                ApplyFansFromUI();
                return;
            }

            Log($"🎨 SET_COLOR R:{r} G:{g} B:{b}");
            _hid.SendCommand(CMD_SET_COLOR, new byte[] { r, g, b });
            ApplyFansFromUI();
        }

        private bool UseIntensityColors()
        {
            return chkIntensityColors?.IsChecked != false;
        }

        private void SendMusicStyle(bool useIntensityColors, bool logIfDisconnected = true)
        {
            if (!_hid.IsOpen)
            {
                if (logIfDisconnected) Log("⚠ Dispositivo no conectado");
                return;
            }

            var style = useIntensityColors ? (byte)1 : (byte)0;
            _hid.SendCommand(CMD_SET_MUSIC_STYLE, new byte[] { style });
            Log(useIntensityColors
                ? "🎚 Audio Wheel: colores por intensidad global"
                : "🎚 Audio Meter: pulse color base");
        }

        // Audio capture
        private void StartAudioCapture()
        {
            var source = GetSelectedAudioSource();
            _selectedAudioSource = source;

            if (source == AudioSourceKind.Disabled)
            {
                StopAudioCapture();
                return;
            }

            StopAudioCapture(logStopped: false);

            var analyzer = new AudioAnalyzer();
            ApplyAudioSettings(analyzer);
            _audio = analyzer;

            analyzer.StatusChanged += status =>
            {
                Dispatcher.BeginInvoke(() => SetAudioStatus(status));
            };
            analyzer.AudioError += ex =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    SetAudioStatus("Error de audio");
                    SendMusicLevel(0, logIfDisconnected: false);
                    Log($"❌ Error audio: {ex.Message}");
                });
            };
            analyzer.LevelUpdated += lvl => HandleAudioLevel(analyzer, lvl);

            _ = Task.Run(() => analyzer.Start(source));
            Log(source == AudioSourceKind.SystemAudio
                ? "🎵 Escuchando audio del sistema"
                : "🎙 Escuchando micrófono");
        }

        private void StopAudioCapture(bool logStopped = true)
        {
            var analyzer = _audio;
            _audio = null;
            analyzer?.Dispose();

            Dispatcher.Invoke(() =>
            {
                pbLevel.Value = 0;
                lblLevel.Text = "0";
                SetAudioStatus("Audio detenido");
                SetFanAudioLevel(0);
                SendMusicLevel(0, logIfDisconnected: false);
            });

            if (logStopped) {
                Log("🔇 Captura de audio detenida");
            }
        }

        private void HandleAudioLevel(AudioAnalyzer analyzer, float level)
        {
            var normalized = Math.Max(0.0, Math.Min(1.0, level / 100.0));
            var musicLevel = ScaleMusicLevelToByte(level);

            Dispatcher.BeginInvoke(() =>
            {
                pbLevel.Maximum = 100;
                pbLevel.Value = Math.Min(level, 100);
                lblLevel.Text = $"{musicLevel}";
                if (GetSelectedMode() == 5) {
                    SetFanAudioLevel(normalized);
                }
            });

            if (_hid.IsOpen)
            {
                var now = DateTime.UtcNow;
                if (now - _lastMusicLevelSend >= _musicLevelSendInterval)
                {
                    SendMusicLevel(musicLevel, logIfDisconnected: false);
                }
            }
        }

        private void SendMusicLevel(byte level, bool logIfDisconnected = true)
        {
            if (!_hid.IsOpen)
            {
                if (logIfDisconnected) Log("⚠ Dispositivo no conectado");
                return;
            }

            _lastMusicLevelSend = DateTime.UtcNow;
            _hid.SendCommand(CMD_MUSIC_LEVEL, new byte[] { level });
        }

        private void SetFanAudioLevel(double normalized)
        {
            try
            {
                FanTL?.SetAudioLevel(normalized);
                FanTR?.SetAudioLevel(normalized);
                FanBL?.SetAudioLevel(normalized);
                FanBR?.SetAudioLevel(normalized);
            }
            catch { }
        }

        private void ChkMusic_Checked(object sender, RoutedEventArgs e) => StartAudioCapture();
        private void ChkMusic_Unchecked(object sender, RoutedEventArgs e) => StopAudioCapture();

        private void MusicTuning_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateMusicTuningLabels();
            if (_audio != null)
            {
                ApplyAudioSettings(_audio);
                pbLevel.Maximum = 100;
            }
        }

        private void AudioSource_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedAudioSource = GetSelectedAudioSource();

            if (_selectedAudioSource == AudioSourceKind.Disabled)
            {
                if (chkMusic != null) {
                    chkMusic.IsChecked = false;
                }
                StopAudioCapture();
                return;
            }

            if (chkMusic != null && chkMusic.IsChecked == true) {
                StartAudioCapture();
            } else {
                SetAudioStatus("Audio detenido");
            }
        }

        private AudioSourceKind GetSelectedAudioSource()
        {
            return cmbAudioSource?.SelectedIndex switch
            {
                1 => AudioSourceKind.SystemAudio,
                2 => AudioSourceKind.Microphone,
                _ => AudioSourceKind.Disabled,
            };
        }

        private void SetSelectedAudioSource(AudioSourceKind source)
        {
            _selectedAudioSource = source;
            if (cmbAudioSource == null) {
                return;
            }

            cmbAudioSource.SelectedIndex = source switch
            {
                AudioSourceKind.SystemAudio => 1,
                AudioSourceKind.Microphone => 2,
                _ => 0,
            };
        }

        private void ApplyAudioSettings(AudioAnalyzer analyzer)
        {
            analyzer.Sensitivity = Math.Max(0.05, sldSensitivity.Value / 100.0);
            analyzer.MaxIntensity = Math.Max(1, Math.Min(100, (int)Math.Round(sldMaxIntensity.Value)));
            analyzer.Smoothing = Math.Max(0.0, Math.Min(0.95, sldSmoothing.Value / 100.0));
            analyzer.Decay = Math.Max(0.0, Math.Min(0.95, sldDecay.Value / 100.0));
            analyzer.AutoGain = true;
            analyzer.ResponseCurve = 0.82;
        }

        private void UpdateMusicTuningLabels()
        {
            if (lblSensitivity != null && sldSensitivity != null) lblSensitivity.Text = $"{(int)Math.Round(sldSensitivity.Value)}%";
            if (lblMaxIntensity != null && sldMaxIntensity != null) lblMaxIntensity.Text = $"{(int)Math.Round(sldMaxIntensity.Value)}";
            if (lblSmoothing != null && sldSmoothing != null) lblSmoothing.Text = $"{(int)Math.Round(sldSmoothing.Value)}%";
            if (lblDecay != null && sldDecay != null) lblDecay.Text = $"{(int)Math.Round(sldDecay.Value)}%";
        }

        private void SetAudioStatus(string status)
        {
            if (lblAudioStatus == null) {
                return;
            }

            lblAudioStatus.Text = status;
            lblAudioStatus.Foreground = status switch
            {
                "Escuchando sistema" => new SolidColorBrush(Color.FromRgb(0, 245, 144)),
                "Escuchando micrófono" => new SolidColorBrush(Color.FromRgb(67, 165, 255)),
                "Error de audio" => new SolidColorBrush(Color.FromRgb(255, 93, 115)),
                _ => new SolidColorBrush(Color.FromRgb(152, 165, 189)),
            };
        }

        private byte ScaleMusicLevelToByte(float level)
        {
            double normalized = Math.Max(0.0, Math.Min(1.0, level / 100.0));
            return (byte)Math.Round(normalized * 255.0);
        }

        private void MusicStyle_Changed(object sender, RoutedEventArgs e)
        {
            ShowParamsForMode(GetSelectedMode());
            ApplyFansFromUI();
            if (_hid.IsOpen && GetSelectedMode() == 5)
            {
                SendMusicStyle(UseIntensityColors(), logIfDisconnected: false);
                if (!UseIntensityColors()) {
                    var c = GetStaticColor();
                    SendHidColor(c.R, c.G, c.B);
                }
            }
        }

        private byte GetBrightnessPercent()
        {
            try { return (byte)Math.Max(0, Math.Min(100, (int)Math.Round(sldBrightness.Value))); }
            catch { return 100; }
        }

        private byte GetFirmwareBrightnessPercent()
        {
            return (byte)Math.Round(GetBrightnessPercent() * (MAX_SAFE_BRIGHTNESS / 100.0));
        }

        private void BrightnessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            byte brightness = GetBrightnessPercent();
            if (lblBrightness != null) lblBrightness.Text = $"{brightness}%";
            if (lblBrightnessFirmware != null) lblBrightnessFirmware.Text = $"FW {GetFirmwareBrightnessPercent()}%";
            ApplyPreviewBrightness();
            SendBrightness(brightness, logIfDisconnected: false);
        }

        private void SendBrightness(byte uiBrightness, bool logIfDisconnected = true)
        {
            if (!_hid.IsOpen)
            {
                if (logIfDisconnected) Log("⚠ Dispositivo no conectado");
                return;
            }

            var firmwareBrightness = (byte)Math.Round(uiBrightness * (MAX_SAFE_BRIGHTNESS / 100.0));
            _hid.SendCommand(CMD_SET_BRIGHTNESS, new byte[] { firmwareBrightness });
            Log($"💡 SET_BRIGHTNESS: UI {uiBrightness}% -> FW {firmwareBrightness}%");
        }

        private byte GetEffectSpeedPercent()
        {
            try { return (byte)Math.Max(0, Math.Min(100, (int)Math.Round(sldEffectSpeed.Value))); }
            catch { return 100; }
        }

        private void EffectSpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateEffectSpeedLabel();
            SendEffectSpeed(GetEffectSpeedPercent(), logIfDisconnected: false);
        }

        private void UpdateEffectSpeedLabel()
        {
            if (lblEffectSpeed != null) lblEffectSpeed.Text = $"{GetEffectSpeedPercent()}%";
        }

        private void SendEffectSpeed(byte speed, bool logIfDisconnected = true)
        {
            if (!_hid.IsOpen)
            {
                if (logIfDisconnected) Log("⚠ Dispositivo no conectado");
                return;
            }

            _hid.SendCommand(CMD_SET_EFFECT_SPEED, new byte[] { speed });
            Log($"🏃 SET_EFFECT_SPEED: {speed}%");
        }

        private bool TryParseHexColor(string hex, out Color color)
        {
            color = default;
            try
            {
                if (string.IsNullOrWhiteSpace(hex)) return false;
                hex = hex.Trim();
                if (!hex.StartsWith("#")) hex = "#" + hex;
                var parsed = ColorConverter.ConvertFromString(hex);
                if (parsed is not Color c) return false;

                color = Color.FromRgb(c.R, c.G, c.B);
                return true;
            }
            catch { return false; }
        }

        private Color ColorFromHex(string hex, Color fallback)
        {
            return TryParseHexColor(hex, out var color) ? color : fallback;
        }

        private void MarkHexValidity(bool isValid)
        {
            if (tbStaticHex == null) {
                return;
            }

            tbStaticHex.BorderBrush = isValid
                ? (TryFindResource("PanelBorderBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(38, 48, 68)))
                : new SolidColorBrush(Color.FromRgb(255, 93, 115));
            tbStaticHex.ToolTip = isValid ? "HEX color" : "HEX inválido. Se conserva el último color válido.";
        }

        private Color GetStaticColor() => _selectedColor;

        private void SetSelectedColor(Color color, bool updateHex, bool applyPreview = true)
        {
            _updatingColorUi = true;
            _selectedColor = color;

            if (sldRed != null) sldRed.Value = color.R;
            if (sldGreen != null) sldGreen.Value = color.G;
            if (sldBlue != null) sldBlue.Value = color.B;
            if (lblRed != null) lblRed.Text = color.R.ToString();
            if (lblGreen != null) lblGreen.Text = color.G.ToString();
            if (lblBlue != null) lblBlue.Text = color.B.ToString();
            if (updateHex && tbStaticHex != null) tbStaticHex.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            MarkHexValidity(true);

            var preview = StaticColorPreview ?? (FindName("StaticColorPreview") as Border);
            if (preview != null) {
                preview.Background = new SolidColorBrush(color);
            }

            _updatingColorUi = false;

            if (applyPreview) {
                ApplyFansFromUI();
            }
        }

        private void TbStaticHex_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_updatingColorUi) return;
            if (!TryParseHexColor(tbStaticHex.Text, out var c))
            {
                MarkHexValidity(false);
                return;
            }

            SetSelectedColor(c, updateHex: false);
        }

        private void RgbSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updatingColorUi || sldRed == null || sldGreen == null || sldBlue == null) return;
            var c = Color.FromRgb((byte)sldRed.Value, (byte)sldGreen.Value, (byte)sldBlue.Value);
            SetSelectedColor(c, updateHex: true);
        }

        private void QuickColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string hex) {
                SetSelectedColor(ColorFromHex(hex, _selectedColor), updateHex: true);
            }
        }

        private void ApplyFans(byte mode, Color c)
        {
            try
            {
                FanTL.Mode = mode; FanTR.Mode = mode; FanBL.Mode = mode; FanBR.Mode = mode;
                FanTL.FanColor = c; FanTR.FanColor = c; FanBL.FanColor = c; FanBR.FanColor = c;
                FanTL.UseIntensityColors = UseIntensityColors(); FanTR.UseIntensityColors = UseIntensityColors(); FanBL.UseIntensityColors = UseIntensityColors(); FanBR.UseIntensityColors = UseIntensityColors();
                ApplyPreviewBrightness();
            }
            catch { }
        }

        private byte GetSelectedMode() => _selectedMode;

        private void ApplyFansFromUI()
        {
            var mode = GetSelectedMode();
            var c = (mode == 1) ? GetStaticColor() : _selectedColor;

            // Ejecutar en UI thread por seguridad
            Dispatcher.Invoke(() =>
            {
                try
                {
                    if (FanTL != null) { FanTL.FanColor = c; FanTL.Mode = mode; }
                    if (FanTR != null) { FanTR.FanColor = c; FanTR.Mode = mode; }
                    if (FanBL != null) { FanBL.FanColor = c; FanBL.Mode = mode; }
                    if (FanBR != null) { FanBR.FanColor = c; FanBR.Mode = mode; }
                    if (FanTL != null) FanTL.UseIntensityColors = UseIntensityColors();
                    if (FanTR != null) FanTR.UseIntensityColors = UseIntensityColors();
                    if (FanBL != null) FanBL.UseIntensityColors = UseIntensityColors();
                    if (FanBR != null) FanBR.UseIntensityColors = UseIntensityColors();
                    ApplyPreviewBrightness();
                }
                catch (Exception ex)
                {
                    // Registrar excepción real para debugging
                    Log($"⚠ Error ApplyFansFromUI: {ex.Message}");
                }
            });
        }


        // Acento RGB dinámico fijo
        private LinearGradientBrush? _accentBrush;
        private Storyboard? _accentRainbow;
        private Storyboard? _accentPulse;

        private void ApplyAccentFromUI()
        {
            var template = TryFindResource("NeonStroke") as LinearGradientBrush;
            if (template == null) return;

            // Clonar para no tocar el recurso global
            var stroke = template.Clone();
            _accentBrush = stroke;

            Dispatcher.Invoke(() =>
            {
                try
                {
                    if (LogoRing != null) LogoRing.BorderBrush = (Brush)this.Resources["AnimatedRGBBrush"];
                    // if (RootFrame != null) RootFrame.BorderBrush = stroke;
                }
                catch (Exception ex) { Log($"⚠ ApplyAccentFromUI: {ex.Message}"); }
            });

            StopAccentRainbow();
            StopAccentPulse();
            StartAccentRainbow(stroke);
        }


        private void StartAccentRainbow(LinearGradientBrush stroke)
        {
            _accentRainbow = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
            var g0 = stroke.GradientStops[0];
            var g1 = stroke.GradientStops[1];

            var rainbowColors = new[]
            {
                Color.FromRgb(255, 0, 0),
                Color.FromRgb(255, 127, 0),
                Color.FromRgb(255, 255, 0),
                Color.FromRgb(127, 255, 0),
                Color.FromRgb(0, 255, 0),
                Color.FromRgb(0, 255, 127),
                Color.FromRgb(0, 255, 255),
                Color.FromRgb(0, 127, 255),
                Color.FromRgb(0, 0, 255),
                Color.FromRgb(127, 0, 255),
                Color.FromRgb(255, 0, 255),
                Color.FromRgb(255, 0, 127),
                Color.FromRgb(255, 0, 0)
            };

            var anim0 = new ColorAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(2.5), RepeatBehavior = RepeatBehavior.Forever };
            for (int i = 0; i < rainbowColors.Length; i++)
            {
                var keyTime = KeyTime.FromPercent((double)i / (rainbowColors.Length - 1));
                anim0.KeyFrames.Add(new LinearColorKeyFrame(rainbowColors[i], keyTime));
            }
            Storyboard.SetTarget(anim0, g0);
            Storyboard.SetTargetProperty(anim0, new PropertyPath(GradientStop.ColorProperty));

            var anim1 = new ColorAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(2.5), RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(0.4) };
            for (int i = 0; i < rainbowColors.Length; i++)
            {
                var colorIndex = (i + 3) % (rainbowColors.Length - 1);
                var keyTime = KeyTime.FromPercent((double)i / (rainbowColors.Length - 1));
                anim1.KeyFrames.Add(new LinearColorKeyFrame(rainbowColors[colorIndex], keyTime));
            }
            Storyboard.SetTarget(anim1, g1);
            Storyboard.SetTargetProperty(anim1, new PropertyPath(GradientStop.ColorProperty));

            var move0 = new DoubleAnimation { From = 0.0, To = 1.0, Duration = TimeSpan.FromSeconds(2), AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
            Storyboard.SetTarget(move0, g0);
            Storyboard.SetTargetProperty(move0, new PropertyPath(GradientStop.OffsetProperty));
            var move1 = new DoubleAnimation { From = 1.0, To = 0.0, Duration = TimeSpan.FromSeconds(2), AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
            Storyboard.SetTarget(move1, g1);
            Storyboard.SetTargetProperty(move1, new PropertyPath(GradientStop.OffsetProperty));

            _accentRainbow.Children.Add(anim0);
            _accentRainbow.Children.Add(anim1);
            _accentRainbow.Children.Add(move0);
            _accentRainbow.Children.Add(move1);
            _accentRainbow.Begin();
        }

        private void StopAccentRainbow() { _accentRainbow?.Stop(); _accentRainbow = null; }
        private void StartAccentPulse(LinearGradientBrush stroke, Color baseColor)
        {
            _accentPulse = new Storyboard { RepeatBehavior = RepeatBehavior.Forever, AutoReverse = true };
            var g0 = stroke.GradientStops[0];
            var g1 = stroke.GradientStops[1];
            var brighter = Color.FromRgb((byte)Math.Min(255, baseColor.R + 80), (byte)Math.Min(255, baseColor.G + 80), (byte)Math.Min(255, baseColor.B + 80));
            var pulse0 = new ColorAnimation { From = baseColor, To = brighter, Duration = TimeSpan.FromSeconds(1.2) };
            Storyboard.SetTarget(pulse0, g0);
            Storyboard.SetTargetProperty(pulse0, new PropertyPath(GradientStop.ColorProperty));
            var pulse1 = new ColorAnimation { From = g1.Color, To = brighter, Duration = TimeSpan.FromSeconds(1.2) };
            Storyboard.SetTarget(pulse1, g1);
            Storyboard.SetTargetProperty(pulse1, new PropertyPath(GradientStop.ColorProperty));
            _accentPulse.Children.Add(pulse0);
            _accentPulse.Children.Add(pulse1);
            _accentPulse.Begin();
        }
        private void StopAccentPulse() { _accentPulse?.Stop(); _accentPulse = null; }

        // Config persistente
        private class AppConfig
        {
            public byte Mode { get; set; }
            public byte R { get; set; }
            public byte G { get; set; }
            public byte B { get; set; }
            public byte Brightness { get; set; } = 100;
            public byte EffectSpeed { get; set; } = 100;
            public AudioSourceKind AudioSource { get; set; } = AudioSourceKind.SystemAudio;
            public double Sensitivity { get; set; } = 200;
            public double MaxIntensity { get; set; } = 80;
            public double Smoothing { get; set; } = 45;
            public double Decay { get; set; } = 82;
            public bool UseIntensityColors { get; set; } = true;
        }
        private void EnsureConfigDir() { try { if (!Directory.Exists(_configDir)) Directory.CreateDirectory(_configDir); } catch (Exception ex) { Log($"⚠ No se pudo crear config: {ex.Message}"); } }
        private void SaveConfig(byte mode, byte r, byte g, byte b)
        {
            try
            {
                EnsureConfigDir();
                var cfg = new AppConfig
                {
                    Mode = mode,
                    R = r,
                    G = g,
                    B = b,
                    Brightness = GetBrightnessPercent(),
                    EffectSpeed = GetEffectSpeedPercent(),
                    AudioSource = GetSelectedAudioSource(),
                    Sensitivity = sldSensitivity.Value,
                    MaxIntensity = sldMaxIntensity.Value,
                    Smoothing = sldSmoothing.Value,
                    Decay = sldDecay.Value,
                    UseIntensityColors = UseIntensityColors(),
                };
                var json = JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_configPath, json);
                Log($"💾 Config guardada en {_configPath}");
            }
            catch (Exception ex) { Log($"⚠ Error guardando config: {ex.Message}"); }
        }
        private bool LoadConfig(out AppConfig? cfg)
        {
            cfg = null;
            try
            {
                if (File.Exists(_configPath))
                {
                    var json = File.ReadAllText(_configPath);
                    cfg = JsonSerializer.Deserialize<AppConfig>(json);
                    return cfg != null;
                }
            }
            catch (Exception ex) { Log($"⚠ Error leyendo config: {ex.Message}"); }
            return false;
        }
        private void LoadConfigApplyToUI()
        {
            if (LoadConfig(out var cfg) && cfg != null)
            {
                _selectedMode = cfg.Mode;
                var c = Color.FromRgb(cfg.R, cfg.G, cfg.B);
                _selectedColor = c;
                if (sldBrightness != null) sldBrightness.Value = Math.Max(0, Math.Min(100, (int)cfg.Brightness));
                if (sldEffectSpeed != null) sldEffectSpeed.Value = Math.Max(0, Math.Min(100, (int)cfg.EffectSpeed));
                if (sldSensitivity != null) sldSensitivity.Value = Math.Max(50, Math.Min(300, cfg.Sensitivity));
                if (sldMaxIntensity != null) sldMaxIntensity.Value = Math.Max(50, Math.Min(100, cfg.MaxIntensity));
                if (sldSmoothing != null) sldSmoothing.Value = Math.Max(0, Math.Min(95, cfg.Smoothing));
                if (sldDecay != null) sldDecay.Value = Math.Max(0, Math.Min(95, cfg.Decay));
                if (chkIntensityColors != null) chkIntensityColors.IsChecked = cfg.UseIntensityColors;
                SetSelectedAudioSource(cfg.AudioSource);
                UpdateMusicTuningLabels();
                UpdateEffectSpeedLabel();
                SetSelectedColor(c, updateHex: true, applyPreview: false);
                ShowParamsForMode(_selectedMode);
                ApplyFans(_selectedMode, c);
                UpdateModeButtonSelection();
                Log("✅ Config previa cargada");
            }
        }

        private async void TryAutoConnect()
        {
            try
            {
                var vidHex = txtVid.Text.Trim();
                var pidHex = txtPid.Text.Trim();
                if (!int.TryParse(vidHex, System.Globalization.NumberStyles.HexNumber, null, out int vid) ||
                    !int.TryParse(pidHex, System.Globalization.NumberStyles.HexNumber, null, out int pid)) return;

                if (_hid.IsDevicePresent(vid, pid) && !_hid.IsOpen)
                {
                    Log("🔌 Intentando autoconectar...");
                    await Task.Run(() => _hid.Connect(vid, pid));
                    if (_hid.IsOpen) Log("✅ Autoconexión exitosa");
                }

                if (_hid.IsOpen && LoadConfig(out var cfg) && cfg != null)
                {
                    SendMode(cfg.Mode);
                    SendBrightness(GetBrightnessPercent(), logIfDisconnected: false);
                    SendEffectSpeed(GetEffectSpeedPercent(), logIfDisconnected: false);
                    if (cfg.Mode == 5) {
                        SendMusicStyle(cfg.UseIntensityColors, logIfDisconnected: false);
                    }
                    if (cfg.Mode == 1 || (cfg.Mode == 5 && !cfg.UseIntensityColors)) {
                        SendHidColor(cfg.R, cfg.G, cfg.B);
                    }
                    if (cfg.Mode == 5 && GetSelectedAudioSource() == AudioSourceKind.Disabled) {
                        SendMusicLevel(0, logIfDisconnected: false);
                    }
                }
            }
            catch { }
        }

        private void BtnSaveApply_Click(object sender, RoutedEventArgs e)
        {
            var mode = GetSelectedMode();
            var c = GetStaticColor();
            SaveConfig(mode, c.R, c.G, c.B);
            ApplyFans(mode, c);
            if (_hid.IsOpen)
            {
                SendMode(mode);
                SendBrightness(GetBrightnessPercent(), logIfDisconnected: false);
                SendEffectSpeed(GetEffectSpeedPercent(), logIfDisconnected: false);
                if (mode == 5) SendMusicStyle(UseIntensityColors(), logIfDisconnected: false);
                if (mode == 1 || (mode == 5 && !UseIntensityColors())) SendHidColor(c.R, c.G, c.B);
            }
            else
            {
                Log("ℹ Config guardada. Se aplicará al conectar.");
            }
        }

        // Mostrar subpaneles según modo
        private void ShowParamsForMode(byte mode)
        {
            try
            {
                StaticParamsPanel.Visibility = (mode == 1 || (mode == 5 && !UseIntensityColors())) ? Visibility.Visible : Visibility.Collapsed;
                MusicParamsPanel.Visibility = (mode == 5) ? Visibility.Visible : Visibility.Collapsed;
                NoParamsPanel.Visibility = (mode == 0 || mode == 2 || mode == 3 || mode == 4 || mode == 6) ? Visibility.Visible : Visibility.Collapsed;
                UpdateModeButtonSelection();
            }
            catch { }
        }

        private void ApplyPreviewBrightness()
        {
            var brightness = GetBrightnessPercent() / 100.0;
            try
            {
                if (FanTL != null) FanTL.PreviewBrightness = brightness;
                if (FanTR != null) FanTR.PreviewBrightness = brightness;
                if (FanBL != null) FanBL.PreviewBrightness = brightness;
                if (FanBR != null) FanBR.PreviewBrightness = brightness;
            }
            catch { }
        }

        protected override void OnClosed(EventArgs e)
        {
            _hid?.Close();
            _audio?.Dispose();
            base.OnClosed(e);
        }
    }
}
