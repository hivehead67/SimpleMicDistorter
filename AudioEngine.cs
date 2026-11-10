using NAudio.Wave;

namespace VoicePitchChanger;

// mic -> effects -> cable + monitor
public class AudioEngine : IDisposable
{
    public const int SampleRate = 48000;
    private static readonly WaveFormat Format = new WaveFormat(SampleRate, 16, 1);

    public EffectChain Effects { get; } = new EffectChain(SampleRate);

    private WaveInEvent _waveIn;
    private WaveOutEvent _cableOut;
    private WaveOutEvent _monitorOut;
    private BufferedWaveProvider _cableBuffer;
    private BufferedWaveProvider _monitorBuffer;
    private float[] _floatBuf = new float[4096];
    private byte[] _outBytes = new byte[8192];

    public bool IsRunning => _waveIn != null;
    public volatile bool MonitorEnabled;
    public float MonitorVolume { get => _monitorOut?.Volume ?? 1f; set { if (_monitorOut != null) _monitorOut.Volume = value; } }

    public static List<string> GetInputDevices()
    {
        var list = new List<string>();
        for (int i = 0; i < WaveIn.DeviceCount; i++) list.Add(WaveIn.GetCapabilities(i).ProductName);
        return list;
    }

    public static List<string> GetOutputDevices()
    {
        var list = new List<string>();
        for (int i = 0; i < WaveOut.DeviceCount; i++) list.Add(WaveOut.GetCapabilities(i).ProductName);
        return list;
    }

    public void Start(int inputDevice, int cableDevice, int monitorDevice, float monitorVolume)
    {
        Stop();

        _cableBuffer = MakeBuffer();
        _monitorBuffer = MakeBuffer();

        _cableOut = new WaveOutEvent { DeviceNumber = cableDevice, DesiredLatency = 80, NumberOfBuffers = 3 };
        _cableOut.Init(_cableBuffer);
        _cableOut.Play();

        _monitorOut = new WaveOutEvent { DeviceNumber = monitorDevice, DesiredLatency = 80, NumberOfBuffers = 3 };
        _monitorOut.Init(_monitorBuffer);
        _monitorOut.Volume = monitorVolume;
        _monitorOut.Play();

        _waveIn = new WaveInEvent
        {
            DeviceNumber = inputDevice,
            WaveFormat = Format,
            BufferMilliseconds = 20,
            NumberOfBuffers = 3
        };
        _waveIn.DataAvailable += OnDataAvailable;
        _waveIn.StartRecording();
    }

    private static BufferedWaveProvider MakeBuffer() => new BufferedWaveProvider(Format)
    {
        BufferDuration = TimeSpan.FromMilliseconds(500),
        DiscardOnBufferOverflow = true,
        ReadFully = true
    };

    private void OnDataAvailable(object sender, WaveInEventArgs e)
    {
        int samples = e.BytesRecorded / 2;
        if (_floatBuf.Length < samples) _floatBuf = new float[samples];
        if (_outBytes.Length < e.BytesRecorded) _outBytes = new byte[e.BytesRecorded];

        for (int i = 0; i < samples; i++)
            _floatBuf[i] = BitConverter.ToInt16(e.Buffer, i * 2) / 32768f;

        Effects.Process(_floatBuf, samples);

        for (int i = 0; i < samples; i++)
        {
            short s = (short)(_floatBuf[i] * 32767f);
            _outBytes[i * 2] = (byte)(s & 0xFF);
            _outBytes[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
        }

        Push(_cableBuffer, e.BytesRecorded);
        if (MonitorEnabled)
            Push(_monitorBuffer, e.BytesRecorded);
        else
            _monitorBuffer?.ClearBuffer();
    }

    private void Push(BufferedWaveProvider buf, int count)
    {
        if (buf == null) return;
        if (buf.BufferedDuration.TotalMilliseconds > 150) buf.ClearBuffer();
        buf.AddSamples(_outBytes, 0, count);
    }

    public void Stop()
    {
        if (_waveIn != null)
        {
            _waveIn.DataAvailable -= OnDataAvailable;
            _waveIn.StopRecording();
            _waveIn.Dispose();
            _waveIn = null;
        }
        _cableOut?.Stop(); _cableOut?.Dispose(); _cableOut = null;
        _monitorOut?.Stop(); _monitorOut?.Dispose(); _monitorOut = null;
        _cableBuffer = null;
        _monitorBuffer = null;
    }

    public void Dispose() => Stop();
}
