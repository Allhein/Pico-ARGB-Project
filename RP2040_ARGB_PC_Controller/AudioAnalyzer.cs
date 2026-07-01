using NAudio.CoreAudioApi;
using NAudio.Wave;
using System;

namespace PicoARGBControl
{
    public enum AudioSourceKind
    {
        Disabled = 0,
        SystemAudio = 1,
        Microphone = 2,
    }

    public sealed class AudioAnalyzer : IDisposable
    {
        private IWaveIn? _capture;
        private WaveFormat? _format;
        private readonly object _sync = new();
        private double _envelope;
        private double _recentMax = 0.02;
        private bool _disposed;

        public event Action<float>? LevelUpdated; // 0..MaxIntensity
        public event Action<string>? StatusChanged;
        public event Action<Exception>? AudioError;

        public AudioSourceKind Source { get; private set; } = AudioSourceKind.Disabled;

        public double Sensitivity { get; set; } = 2.0;      // 0.5x .. 3.0x
        public int MaxIntensity { get; set; } = 80;         // UI scale before HID normalization
        public double Smoothing { get; set; } = 0.45;       // 0..1, higher = slower attack
        public double Decay { get; set; } = 0.82;           // 0..1, higher = slower release
        public double NoiseFloor { get; set; } = 0.001;
        public double NoiseGate { get; set; } = 0.0025;
        public bool AutoGain { get; set; } = true;
        public double ResponseCurve { get; set; } = 0.82;   // <1 lifts quiet audio, >1 compresses lows

        public void Start(AudioSourceKind source)
        {
            if (_disposed) {
                return;
            }

            Stop();
            if (_disposed) {
                return;
            }

            Source = source;

            if (source == AudioSourceKind.Disabled)
            {
                StatusChanged?.Invoke("Audio detenido");
                return;
            }

            try
            {
                _capture = CreateCapture(source);
                _format = _capture.WaveFormat;
                _capture.DataAvailable += Capture_DataAvailable;
                _capture.RecordingStopped += Capture_RecordingStopped;
                _capture.StartRecording();
                StatusChanged?.Invoke(source == AudioSourceKind.SystemAudio
                    ? "Escuchando sistema"
                    : "Escuchando micrófono");
            }
            catch (Exception ex)
            {
                CleanupCapture();
                Source = AudioSourceKind.Disabled;
                StatusChanged?.Invoke("Error de audio");
                AudioError?.Invoke(ex);
            }
        }

        public void Stop()
        {
            lock (_sync)
            {
                _envelope = 0;
                _recentMax = 0.02;
            }

            CleanupCapture();
            Source = AudioSourceKind.Disabled;
            LevelUpdated?.Invoke(0);
            StatusChanged?.Invoke("Audio detenido");
        }

        private static IWaveIn CreateCapture(AudioSourceKind source)
        {
            using var enumerator = new MMDeviceEnumerator();

            if (source == AudioSourceKind.SystemAudio)
            {
                var output = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                return new WasapiLoopbackCapture(output);
            }

            if (source == AudioSourceKind.Microphone)
            {
                var input = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
                return new WasapiCapture(input);
            }

            throw new InvalidOperationException("Audio source is disabled.");
        }

        private void Capture_DataAvailable(object? sender, WaveInEventArgs e)
        {
            try
            {
                if (e.BytesRecorded <= 0 || _format == null) {
                    return;
                }

                var metrics = CalculateMetrics(e.Buffer, e.BytesRecorded, _format);
                var normalized = AnalyzeLevel(metrics);
                var smoothed = InterpolateLevel(normalized);
                var level = (float)(smoothed * Clamp(MaxIntensity, 1, 100));
                LevelUpdated?.Invoke(level);
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke("Error de audio");
                AudioError?.Invoke(ex);
            }
        }

        private void Capture_RecordingStopped(object? sender, StoppedEventArgs e)
        {
            if (_disposed) {
                return;
            }

            if (e.Exception != null)
            {
                StatusChanged?.Invoke("Error de audio");
                AudioError?.Invoke(e.Exception);
            }
        }

        private double AnalyzeLevel(AudioMetrics metrics)
        {
            var blended = (metrics.Rms * 0.65) + (metrics.Peak * 0.35);
            var value = Math.Max(0, blended - NoiseFloor);
            if (value < NoiseGate) {
                return 0;
            }

            const double minDb = -58.0;
            var db = 20.0 * Math.Log10(Math.Max(value, 0.000001));
            var perceptual = Clamp((db - minDb) / -minDb, 0.0, 1.0);

            if (AutoGain)
            {
                if (value > _recentMax) {
                    _recentMax = value;
                } else {
                    _recentMax = (_recentMax * 0.994) + (value * 0.006);
                }
                _recentMax = Clamp(_recentMax, NoiseGate * 3.0, 1.0);
            }

            var agc = AutoGain ? Clamp(value / _recentMax, 0.0, 1.0) : value;
            var combined = (perceptual * 0.72) + (Math.Pow(agc, 0.70) * 0.28);
            var gain = Clamp(Sensitivity, 0.05, 3.0);
            var lifted = 1.0 - Math.Exp(-combined * gain * 1.15);

            return Clamp(Math.Pow(lifted, Clamp(ResponseCurve, 0.45, 1.4)), 0.0, 1.0);
        }

        private double InterpolateLevel(double target)
        {
            lock (_sync)
            {
                var rising = target > _envelope;
                var response = rising
                    ? 1.0 - Clamp(Smoothing, 0.0, 0.95)
                    : 1.0 - Clamp(Decay, 0.0, 0.95);

                response = Clamp(response, 0.02, 0.95);
                _envelope += (target - _envelope) * response;
                _envelope = Clamp(_envelope, 0.0, 1.0);
                return _envelope;
            }
        }

        private readonly struct AudioMetrics
        {
            public AudioMetrics(double rms, double peak)
            {
                Rms = rms;
                Peak = peak;
            }

            public double Rms { get; }
            public double Peak { get; }
        }

        private static AudioMetrics CalculateMetrics(byte[] buffer, int bytesRecorded, WaveFormat format)
        {
            if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
            {
                var samples = bytesRecorded / 4;
                double sum = 0;
                double peak = 0;
                for (var i = 0; i < samples; i++)
                {
                    var sample = Math.Abs(BitConverter.ToSingle(buffer, i * 4));
                    sum += sample * sample;
                    if (sample > peak) {
                        peak = sample;
                    }
                }
                return new AudioMetrics(Math.Sqrt(sum / Math.Max(1, samples)), Clamp(peak, 0.0, 1.0));
            }

            if (format.BitsPerSample == 16)
            {
                var samples = bytesRecorded / 2;
                double sum = 0;
                double peak = 0;
                for (var i = 0; i < samples; i++)
                {
                    var sample = Math.Abs(BitConverter.ToInt16(buffer, i * 2) / 32768.0);
                    sum += sample * sample;
                    if (sample > peak) {
                        peak = sample;
                    }
                }
                return new AudioMetrics(Math.Sqrt(sum / Math.Max(1, samples)), Clamp(peak, 0.0, 1.0));
            }

            if (format.BitsPerSample == 24)
            {
                var samples = bytesRecorded / 3;
                double sum = 0;
                double peak = 0;
                for (var i = 0; i < samples; i++)
                {
                    var index = i * 3;
                    var value = buffer[index] | (buffer[index + 1] << 8) | (buffer[index + 2] << 16);
                    if ((value & 0x800000) != 0) {
                        value |= unchecked((int)0xFF000000);
                    }
                    var sample = Math.Abs(value / 8388608.0);
                    sum += sample * sample;
                    if (sample > peak) {
                        peak = sample;
                    }
                }
                return new AudioMetrics(Math.Sqrt(sum / Math.Max(1, samples)), Clamp(peak, 0.0, 1.0));
            }

            double sumFallback = 0;
            double peakFallback = 0;
            for (var i = 0; i < bytesRecorded; i++)
            {
                var sample = Math.Abs((buffer[i] - 128) / 128.0);
                sumFallback += sample * sample;
                if (sample > peakFallback) {
                    peakFallback = sample;
                }
            }
            return new AudioMetrics(Math.Sqrt(sumFallback / Math.Max(1, bytesRecorded)), Clamp(peakFallback, 0.0, 1.0));
        }

        private void CleanupCapture()
        {
            if (_capture == null) {
                return;
            }

            try
            {
                _capture.DataAvailable -= Capture_DataAvailable;
                _capture.RecordingStopped -= Capture_RecordingStopped;
                _capture.StopRecording();
            }
            catch { }

            try { _capture.Dispose(); } catch { }
            _capture = null;
            _format = null;
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        public void Dispose()
        {
            _disposed = true;
            CleanupCapture();
        }
    }
}
