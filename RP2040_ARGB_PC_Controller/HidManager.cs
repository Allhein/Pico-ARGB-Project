using HidSharp;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PicoARGBControl
{
    /// <summary>
    /// Simple HID wrapper using HidSharp.
    /// - Connect(vid,pid) opens the first matching device
    /// - SendCommand(commandByte, payload) forms a 64-byte report where [0]=reportId(0) [1]=cmd [2..] payload
    /// - Ping(): sends 0xAA and waits briefly for "PONG" response (handled by internal read)
    /// - StartReading(callback) starts a read loop
    /// </summary>
    public class HidManager
    {
        private HidDevice? _device;
        private HidStream? _stream;
        private CancellationTokenSource? _cts;
        private Action<byte[]>? _onData;
        private StreamWriter? _logWriter;

        public bool IsOpen => _stream != null;
        public HidManager() { }

        public bool IsDevicePresent(int vid, int pid)
        {
            var list = DeviceList.Local;
            return list.GetHidDevices(vid, pid).Any();
        }

        public bool Connect(int vid, int pid)
        {
            var list = DeviceList.Local;
            var dev = list.GetHidDevices(vid, pid).FirstOrDefault();
            if (dev == null) return false;
            _device = dev;

            // Inicializar log file
            try
            {
                _logWriter = new StreamWriter("hid_commands.log", append: true);
                _logWriter.WriteLine($"=== NUEVA SESIÓN {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
                _logWriter.WriteLine($"Conectando a VID: 0x{vid:X4}, PID: 0x{pid:X4}");
                _logWriter.Flush();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"No se pudo crear log file: {ex.Message}");
            }

            // try open
            if (!_device.TryOpen(out _stream)) return false;
            _cts = new CancellationTokenSource();
            return true;
        }

        public void Close()
        {
            try
            {
                _cts?.Cancel();
                _stream?.Close();

                // Cerrar log file
                _logWriter?.WriteLine($"=== SESIÓN FINALIZADA {DateTime.Now:HH:mm:ss} ===\n");
                _logWriter?.Close();
                _logWriter = null;

            }
            catch { }
            _stream = null;
            _device = null;
        }

        public async Task<bool> Ping(int timeoutMs = 500)
        {
            if (_stream == null) return false;
            var tcs = new TaskCompletionSource<bool>();
            var timeoutToken = new CancellationTokenSource(timeoutMs).Token;
            void handler(byte[] bytes)
            {
                try
                {
                    // expect ASCII "PONG"
                    if (bytes != null && bytes.Length >= 4)
                    {
                        //var s = System.Text.Encoding.ASCII.GetString(bytes, 0, Math.Min(4, bytes.Length));
                        //if (s.Contains("PONG")) tcs.TrySetResult(true);
                        // Buscar "PONG" en cualquier posición del buffer
                        for (int i = 0; i <= bytes.Length - 4; i++)
                        {
                            if (bytes[i] == 'P' && bytes[i + 1] == 'O' && bytes[i + 2] == 'N' && bytes[i + 3] == 'G')
                            {
                                tcs.TrySetResult(true);
                                return;
                            }
                        }

                    }
                }
                catch { }
            }

            SetReadCallback(handler);
            //SendCommand(0xAA, null); // PING
            try
            {
                // 🔴 LOG DETALLADO del PING
                LogSendCommand(0xAA, null, "PING");
                SendCommand(0xAA, null);

                // Esperar con timeout
                var delayTask = Task.Delay(timeoutMs, timeoutToken);
                var completedTask = await Task.WhenAny(tcs.Task, delayTask);

                SetReadCallback(null);
                return completedTask == tcs.Task && tcs.Task.Result;
            }
            catch
            {
                SetReadCallback(null);
                return false;
            }
            // var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs));
            //SetReadCallback(null);
            //return completed == tcs.Task && tcs.Task.Result;
        }

        public void StartReading(Action<byte[]>? onData = null)
        {
            _onData = onData;
            if (_stream == null) return;
            var token = _cts?.Token ?? CancellationToken.None;
            Task.Run(async () =>
            {
                var buf = new byte[_device?.GetMaxInputReportLength() ?? 64];
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        int read = await _stream.ReadAsync(buf, 0, buf.Length, token);
                        if (read > 0)
                        {
                            var copy = new byte[read];
                            Array.Copy(buf, copy, read);
                            _onData?.Invoke(copy);

                            // 🔴 LOG de recepción
                            LogReceiveData(copy);
                        }
                    }
                    catch (OperationCanceledException) { break; }
                    catch { /* ignore */ }
                }
            }, token);
        }

        /// <summary>
        /// Low level set callback for inbound raw bytes (called by StartReading when data arrives).
        /// </summary>
        public void SetReadCallback(Action<byte[]>? cb)
        {
            _onData = cb;
        }

        /// <summary>
        /// Send a 64-byte report: [0]=reportId(0) [1]=cmd [2..] payload
        /// </summary>
        public void SendCommand(byte cmd, byte[]? payload)
        {
            if (_stream == null) return;

            // 🔴 LOG del comando antes de enviar
            string commandName = GetCommandName(cmd);

            LogSendCommand(cmd, payload, commandName);
            var length = Math.Max(64, _device?.GetMaxOutputReportLength() ?? 64);
            var report = new byte[length];
            report[0] = 0; // report id 0
            report[1] = cmd;
            if (payload != null)
            {
                Array.Copy(payload, 0, report, 2, Math.Min(payload.Length, report.Length - 2));
            }

            try
            {
                _stream.Write(report, 0, report.Length);
            }
            catch { /* ignore write errors */ }
        }

        /// <summary>
        /// DEBUG: Enviar comando con log detallado
        /// </summary>
        public void SendCommandDebug(byte cmd, byte[]? payload, Action<string> logCallback)
        {
            if (_stream == null) return;

            var length = Math.Max(64, _device?.GetMaxOutputReportLength() ?? 64);
            var report = new byte[length];
            report[0] = 0; // report id 0
            report[1] = cmd;

            if (payload != null)
            {
                Array.Copy(payload, 0, report, 2, Math.Min(payload.Length, report.Length - 2));
            }

            // DEBUG: Mostrar qué se está enviando
            string debugInfo = $"📤 ENVIANDO - ReportID: {report[0]:X2}, Cmd: {report[1]:X2}, ";
            debugInfo += $"Bytes: {BitConverter.ToString(report, 0, Math.Min(8, report.Length))}";
            logCallback?.Invoke(debugInfo);

            try
            {
                _stream.Write(report, 0, report.Length);
            }
            catch { /* ignore */ }
        }

        // 🔴 NUEVO: Logging de comandos enviados
        private void LogSendCommand(byte cmd, byte[]? payload, string commandName)
        {
            try
            {
                if (_logWriter != null)
                {
                    string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                    string payloadStr = payload != null ? BitConverter.ToString(payload) : "NULL";

                    _logWriter.WriteLine($"[{timestamp}] 📤 ENVIADO: Cmd=0x{cmd:X2} ({commandName})");
                    _logWriter.WriteLine($"               Payload: {payloadStr}");

                    // Mostrar estructura completa del reporte
                    var report = new byte[64];
                    report[0] = 0;
                    report[1] = cmd;
                    if (payload != null)
                    {
                        Array.Copy(payload, 0, report, 2, Math.Min(payload.Length, 62));
                    }
                    _logWriter.WriteLine($"               Reporte: {BitConverter.ToString(report, 0, 8)}...");
                    _logWriter.Flush();
                }
            }
            catch { /* Ignorar errores de logging */ }
        }

        // 🔴 NUEVO: Logging de datos recibidos
        private void LogReceiveData(byte[] data)
        {
            try
            {
                if (_logWriter != null && data != null && data.Length > 0)
                {
                    string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                    string hex = BitConverter.ToString(data);
                    string ascii = "";

                    foreach (byte b in data)
                    {
                        if (b >= 32 && b <= 126)
                            ascii += (char)b;
                        else
                            ascii += ".";
                    }

                    _logWriter.WriteLine($"[{timestamp}] 📥 RECIBIDO: {hex}");
                    _logWriter.WriteLine($"               ASCII: {ascii}");
                    _logWriter.Flush();
                }
            }
            catch { /* Ignorar errores de logging */ }
        }

        // 🔴 NUEVO: Obtener nombre del comando para logging
        private string GetCommandName(byte cmd)
        {
            return cmd switch
            {
                0xAA => "PING",
                0x03 => "SET_COLOR",
                0x04 => "OFF",
                0x05 => "SET_MODE",
                0x06 => "MUSIC_LEVEL",
                _ => "DESCONOCIDO"
            };
        }
    }
}
