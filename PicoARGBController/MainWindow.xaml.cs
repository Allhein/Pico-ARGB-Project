using HidSharp;
using NAudio.Wave;
using System;
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

        // Commands (matching firmware)
        private const byte CMD_PING = 0xAA;
        private const byte CMD_SET_COLOR = 0x03;
        private const byte CMD_OFF = 0x04;         // ← CORREGIDO: OFF es 0x04
        private const byte CMD_SET_MODE = 0x05;    // ← CORREGIDO: SET_MODE es 0x05  
        private const byte CMD_MUSIC_LEVEL = 0x06;

        // Estado UI actual (modo y color seleccionados desde el panel izquierdo)
        private byte _selectedMode = 1; // 1=Static
        private System.Windows.Media.Color _selectedColor = Colors.Cyan;

        // Throttling para evitar flood HID en modo música
        private DateTime _lastMusicColorSend = DateTime.MinValue;
        private readonly TimeSpan _musicSendInterval = TimeSpan.FromMilliseconds(120); // enviar ~8-9 updates/s


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
            string[] modes = { "Static Color", "Rainbow", "Breathing", "Chase", "Music", "Cycle", "Off" };
            for (int i = 0; i < modes.Length; i++)
            {
                var b = CreateModeButton(modes[i], (byte)(i + 1));
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
            UpdateStaticPreview(_selectedColor);
            txtLog.Text = "";

            // Cargar configuración previa y aplicarla
            LoadConfigApplyToUI();
            ApplyFansFromUI();

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
                Content = label,
                Margin = new Thickness(6, 6, 6, 0),
                Padding = new Thickness(10),
                Tag = tag,
                Width = 280,
                Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
                Foreground = Brushes.White,
            };
            // aplicar estilo neón si existe en recursos
            try { btn.Style = (Style)FindResource("NeonButton"); } catch { }
            
            btn.Click += (s, e) =>
            {
                byte mode = (byte)((Button)s).Tag;
                _selectedMode = mode;

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

        private async void PresenceTimer_Tick(object? sender, EventArgs e)
        {
            try
            {
                var vidHex = txtVid.Text.Trim();
                var pidHex = txtPid.Text.Trim();
                if (!int.TryParse(vidHex, System.Globalization.NumberStyles.HexNumber, null, out int vid) ||
                    !int.TryParse(pidHex, System.Globalization.NumberStyles.HexNumber, null, out int pid))
                {
                    lblStatus.Text = "VID/PID inválidos";
                    lblStatus.Foreground = Brushes.Orange;
                    return;
                }

                bool present = _hid.IsDevicePresent(vid, pid);
                bool connected = _hid.IsOpen;

                // ✅ ACTUALIZACIÓN: Mostrar estados separados
                if (connected)
                {
                    lblStatus.Text = "Conectado";
                    lblStatus.Foreground = Brushes.LightGreen;
                    lblHint.Text = "Comunicación activa con el dispositivo";
                    btnConnect.Content = "Desconectar";
                    btnConnect.IsEnabled = true;
                }
                else if (present)
                {
                    lblStatus.Text = "Presente";
                    lblStatus.Foreground = Brushes.LightBlue;
                    lblHint.Text = "Dispositivo detectado - Listo para conectar";
                    btnConnect.Content = "Conectar";
                    btnConnect.IsEnabled = true;
                }
                else
                {
                    lblStatus.Text = "No detectado";
                    lblStatus.Foreground = Brushes.LightCoral;
                    lblHint.Text = "Esperando controlador...";
                    btnConnect.Content = "Conectar";
                    btnConnect.IsEnabled = false;
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
            lblStatus.Text = "Conectando...";
            lblStatus.Foreground = Brushes.Yellow;

            try
            {
                // Verificar presencia antes de conectar
                if (!_hid.IsDevicePresent(vid, pid))
                {
                    lblStatus.Text = "Dispositivo no encontrado";
                    lblStatus.Foreground = Brushes.LightCoral;
                    btnConnect.IsEnabled = true;
                    Log("Dispositivo no detectado.");
                    return;
                }

                Log($"Intentando conectar con VID: 0x{vid:X4}, PID: 0x{pid:X4}");

                var ok = await Task.Run(() => _hid.Connect((int)vid, (int)pid));
                if (!ok)
                {
                    lblStatus.Text = "Error de conexión";
                    lblStatus.Foreground = Brushes.Orange;
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
                    lblStatus.Text = "Conectado ✓";
                    lblStatus.Foreground = Brushes.LightGreen;
                    lblHint.Text = "Comunicación bidireccional activa";
                }
                else
                {
                    lblStatus.Text = "Conectado ⚠";
                    lblStatus.Foreground = Brushes.Yellow;
                    lblHint.Text = "Conectado pero sin respuesta del firmware";
                }

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
            lblStatus.Text = "Desconectado";
            lblStatus.Foreground = Brushes.LightCoral;
            lblHint.Text = "Dispositivo desconectado";
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
            _hid.SendCommand(CMD_OFF, null);
            Log("✅ Comando OFF enviado");
        }

        private void BtnSendMode_Click(object sender, RoutedEventArgs e)
        {
            var mode = GetSelectedMode();
            var c = GetStaticColor();
            if (_hid.IsOpen)
            {
                SendMode(mode);
                if (mode == 1) SendHidColor(c.R, c.G, c.B);
                Log($"📤 Enviado modo {mode} {(mode == 1 ? $"con color {c.R} {c.G} {c.B}" : "")}");
            }
            else
            {
                Log("⚠ Dispositivo no conectado");
            }
        }

        private void SendMode(byte mode)
        {
            if (!_hid.IsOpen) { Log("❌ No conectado"); return; }
            Log($"🔄 SET_MODE: {mode}");
            _hid.SendCommand(CMD_SET_MODE, new byte[] { mode });
            Log($"✅ Modo {mode} enviado");
        }

        private void SendHidColor(byte r, byte g, byte b)
        {
            if (!_hid.IsOpen) { Log("❌ No conectado"); return; }
            Log($"🎨 SET_COLOR R:{r} G:{g} B:{b}");
            _hid.SendCommand(CMD_SET_COLOR, new byte[] { r, g, b });
            ApplyFansFromUI();
        }

        // Audio capture
        private void StartAudioCapture()
        {
            if (_audio != null) return;
            try
            {
                _audio = new AudioAnalyzer();
                _audio.Sensitivity = sldSensitivity.Value / 100.0;
                _audio.MaxIntensity = (int)sldMaxIntensity.Value;
                pbLevel.Maximum = _audio.MaxIntensity;
                lblSensitivity.Text = $"{(int)sldSensitivity.Value}%";
                lblMaxIntensity.Text = _audio.MaxIntensity.ToString();

                _audio.LevelUpdated += (lvl) =>
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        pbLevel.Value = lvl;
                        lblLevel.Text = ((int)lvl).ToString();
                        if (GetSelectedMode() == 5)
                        {
                            double v = Math.Max(0.0, Math.Min(1.0, lvl / Math.Max(1.0, _audio.MaxIntensity)));
                            try
                            {
                                FanTL?.SetAudioLevel(v);
                                FanTR?.SetAudioLevel(v);
                                FanBL?.SetAudioLevel(v);
                                FanBR?.SetAudioLevel(v);
                            }
                            catch { }
                        }
                    });

                    if (_hid.IsOpen)
                    {
                        _hid.SendCommand(CMD_MUSIC_LEVEL, new byte[] { (byte)Math.Min(100, (int)lvl) });

                        if (GetSelectedMode() == 5)
                        {
                            var now = DateTime.UtcNow;
                            if (now - _lastMusicColorSend >= _musicSendInterval)
                            {
                                _lastMusicColorSend = now;

                                // Base color (el usuario define el tono guardado en _selectedColor o tb static color)
                                var baseColor = (_selectedMode == 1) ? GetStaticColor() : _selectedColor;

                                // Calcula intensidad: mínimo 25% (tenue) hasta 100%
                                double intensity = 0.25 + (0.75 * Math.Max(0.0, Math.Min(1.0, lvl / Math.Max(1.0, _audio.MaxIntensity))));

                                byte rs = (byte)Math.Min(255, (int)(baseColor.R * intensity));
                                byte gs = (byte)Math.Min(255, (int)(baseColor.G * intensity));
                                byte bs = (byte)Math.Min(255, (int)(baseColor.B * intensity));

                                _hid.SendCommand(CMD_SET_COLOR, new byte[] { rs, gs, bs });
                            }
                        }
                    }
                };
                _audio.Start();
                Log("🎵 Captura de audio iniciada");
            }
            catch (Exception ex) { Log($"❌ Error audio: {ex.Message}"); }
        }

        private void StopAudioCapture()
        {
            _audio?.Stop();
            _audio = null;
            Dispatcher.Invoke(() => { pbLevel.Value = 0; lblLevel.Text = "0"; });
            Log("🔇 Captura de audio detenida");
        }

        private void ChkMusic_Checked(object sender, RoutedEventArgs e) => StartAudioCapture();
        private void ChkMusic_Unchecked(object sender, RoutedEventArgs e) => StopAudioCapture();

        private void MusicTuning_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_audio != null)
            {
                _audio.Sensitivity = sldSensitivity.Value / 100.0;
                _audio.MaxIntensity = (int)sldMaxIntensity.Value;
                pbLevel.Maximum = _audio.MaxIntensity;
                lblSensitivity.Text = $"{(int)sldSensitivity.Value}%";
                lblMaxIntensity.Text = _audio.MaxIntensity.ToString();
            }
        }

        private Color ColorFromHex(string hex, Color fallback)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(hex)) return fallback;
                hex = hex.Trim();
                if (!hex.StartsWith("#")) hex = "#" + hex;
                var c = (Color)ColorConverter.ConvertFromString(hex);
                return c;
            }
            catch { return fallback; }
        }

        private Color GetStaticColor()
        {
            return ColorFromHex(tbStaticHex.Text, _selectedColor);
        }

        private void UpdateStaticPreview(Color c)
        {
            _selectedColor = c;
            var preview = this.StaticColorPreview ?? (FindName("StaticColorPreview") as Border);
            if (preview == null) { return; }
            preview.Background = new SolidColorBrush(c);
        }

        private void TbStaticHex_TextChanged(object sender, TextChangedEventArgs e)
        {
            var c = GetStaticColor();
            UpdateStaticPreview(c);
            ApplyFansFromUI();
        }

        private void ApplyFans(byte mode, Color c)
        {
            try
            {
                FanTL.Mode = mode; FanTR.Mode = mode; FanBL.Mode = mode; FanBR.Mode = mode;
                FanTL.FanColor = c; FanTR.FanColor = c; FanBL.FanColor = c; FanBR.FanColor = c;
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
        private class AppConfig { public byte Mode { get; set; } public byte R { get; set; } public byte G { get; set; } public byte B { get; set; } }
        private void EnsureConfigDir() { try { if (!Directory.Exists(_configDir)) Directory.CreateDirectory(_configDir); } catch (Exception ex) { Log($"⚠ No se pudo crear config: {ex.Message}"); } }
        private void SaveConfig(byte mode, byte r, byte g, byte b)
        {
            try
            {
                EnsureConfigDir();
                var cfg = new AppConfig { Mode = mode, R = r, G = g, B = b };
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
                try { tbStaticHex.Text = $"#{c.R:X2}{c.G:X2}{c.B:X2}"; } catch { }
                UpdateStaticPreview(c);
                ShowParamsForMode(_selectedMode);
                ApplyFans(_selectedMode, c);
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
                    SendHidColor(cfg.R, cfg.G, cfg.B);
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
                if (mode == 1) SendHidColor(c.R, c.G, c.B);
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
                StaticParamsPanel.Visibility = (mode == 1) ? Visibility.Visible : Visibility.Collapsed;
                MusicParamsPanel.Visibility = (mode == 5) ? Visibility.Visible : Visibility.Collapsed;
                NoParamsPanel.Visibility = (mode == 2 || mode == 3 || mode == 4 || mode == 6 || mode == 7) ? Visibility.Visible : Visibility.Collapsed;
            }
            catch { }
        }

        protected override void OnClosed(EventArgs e)
        {
            _hid?.Close();
            _audio?.Stop();
            base.OnClosed(e);
        }
    }
}