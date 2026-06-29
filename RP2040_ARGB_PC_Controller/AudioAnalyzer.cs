using NAudio.CoreAudioApi;
using NAudio.Wave;
using System;
using System.Threading;

namespace PicoARGBControl
{
    public class AudioAnalyzer
    {
        private WasapiLoopbackCapture? _capture;
        public event Action<float>? LevelUpdated; // 0..MaxIntensity

        // Ajustes: sensibilidad (ganancia), intensidad máxima y suavizado
        public double Sensitivity { get; set; } = 1.5;      // 0.5x .. 3.0x (se ajusta desde UI)
        public int MaxIntensity { get; set; } = 100;         // Limitar a 100 para UI y envío
        public double Smoothing { get; set; } = 0.35;        // EMA: 0..1, mayor = más suave
        public double NoiseFloor { get; set; } = 0.015;      // recorte de piso de ruido
        private float _ema = 0f;

        public void Start()
        {
            Stop();
            _capture = new WasapiLoopbackCapture();
            _capture.DataAvailable += Capture_DataAvailable;
            _capture.StartRecording();
        }

        public void Stop()
        {
            try
            {
                if (_capture != null)
                {
                    _capture.DataAvailable -= Capture_DataAvailable;
                    _capture.StopRecording();
                    _capture.Dispose();
                    _capture = null;
                }
            }
            catch { }
        }

        private void Capture_DataAvailable(object? sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded <= 0) return;
            var capture = (WasapiLoopbackCapture?)sender;
            if (capture == null) return;
            var format = capture.WaveFormat;

            // Obtener RMS normalizado [0..1]
            double rms = 0.0;
            if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
            {
                int floats = e.BytesRecorded / 4;
                double sum = 0;
                for (int i = 0; i < floats; i++)
                {
                    float sample = BitConverter.ToSingle(e.Buffer, i * 4);
                    sum += sample * sample;
                }
                rms = Math.Sqrt(sum / Math.Max(1, floats));
            }
            else if (format.BitsPerSample == 16)
            {
                int samples = e.BytesRecorded / 2;
                double sum = 0;
                for (int i = 0; i < samples; i++)
                {
                    short s = BitConverter.ToInt16(e.Buffer, i * 2);
                    double fs = s / 32768.0;
                    sum += fs * fs;
                }
                rms = Math.Sqrt(sum / Math.Max(1, samples));
            }
            else
            {
                long acc = 0;
                for (int i = 0; i < e.BytesRecorded; i++) acc += Math.Abs(e.Buffer[i]);
                double avg = acc / (double)e.BytesRecorded; // ~0..255
                rms = avg / 255.0; // normalizar a 0..1
            }

            // Filtrar piso de ruido y aplicar ganancia/sensibilidad
            double val = Math.Max(0, rms - NoiseFloor);
            val *= Sensitivity; // aplicar sensibilidad
            val = Math.Min(1.0, val);

            // Compresión suave para evitar picos (similar a medidores visuales)
            val = Math.Pow(val, 0.6); // gamma < 1 sube medios y baja picos

            // Suavizado EMA
            double alpha = Math.Max(0.01, Math.Min(0.95, 1.0 - Smoothing));
            _ema = (float)(_ema * (1 - alpha) + val * alpha);

            // Escalar a intensidad máxima solicitada
            float level = (float)(_ema * MaxIntensity);
            if (level < 0) level = 0; if (level > MaxIntensity) level = MaxIntensity;

            LevelUpdated?.Invoke(level);
        }
    }
}
