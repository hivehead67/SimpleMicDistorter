namespace VoicePitchChanger;

// gate -> pitch -> echo -> gain
public class EffectChain
{
    private readonly int _sampleRate;


    public volatile float PitchSemitones;  
    public volatile float GainDb;           
    public volatile float GateThresholdDb = -60f;
    public volatile float EchoMix;
    public volatile float EchoDelayMs = 250f;


    private readonly float[] _pitchBuf;
    private int _pitchWrite;
    private float _pitchPhase;
    private readonly int _window;


    private float _envelope;
    private float _gateGain;


    private readonly float[] _echoBuf;
    private int _echoWrite;

    // for the meter
    public float PeakLevel;

    public EffectChain(int sampleRate)
    {
        _sampleRate = sampleRate;
        _window = sampleRate / 25;
        _pitchBuf = new float[_window * 2 + 4];
        _echoBuf = new float[sampleRate * 2];
    }

    public void Process(float[] samples, int count)
    {
        float ratio = MathF.Pow(2f, PitchSemitones / 12f);
        bool doPitch = MathF.Abs(PitchSemitones) > 0.01f;
        float gain = MathF.Pow(10f, GainDb / 20f);
        float gateThresh = MathF.Pow(10f, GateThresholdDb / 20f);
        bool gateOn = GateThresholdDb > -79.5f;
        float echoMix = EchoMix;
        int echoDelay = Math.Clamp((int)(EchoDelayMs * _sampleRate / 1000f), 1, _echoBuf.Length - 1);

        float attack = 1f - MathF.Exp(-1f / (0.002f * _sampleRate));
        float release = 1f - MathF.Exp(-1f / (0.100f * _sampleRate));
        float peak = 0f;

        for (int i = 0; i < count; i++)
        {
            float x = samples[i];

            
            if (gateOn)
            {
                float a = MathF.Abs(x);
                _envelope += (a - _envelope) * (a > _envelope ? attack : release);
                float target = _envelope > gateThresh ? 1f : 0f;
                _gateGain += (target - _gateGain) * (target > _gateGain ? attack : release);
                x *= _gateGain;
            }

            
            if (doPitch)
                x = PitchShift(x, ratio);
            else
                PitchShift(x, 1f);

            
            if (echoMix > 0.001f)
            {
                int readIdx = _echoWrite - echoDelay;
                if (readIdx < 0) readIdx += _echoBuf.Length;
                float delayed = _echoBuf[readIdx];
                _echoBuf[_echoWrite] = x + delayed * 0.4f;
                x += delayed * echoMix;
            }
            else
            {
                _echoBuf[_echoWrite] = 0f;
            }
            if (++_echoWrite >= _echoBuf.Length) _echoWrite = 0;

            // gain + clamp
            x *= gain;
            if (x > 1f) x = 1f; else if (x < -1f) x = -1f;

            float ax = MathF.Abs(x);
            if (ax > peak) peak = ax;
            samples[i] = x;
        }

        PeakLevel = peak;
    }

    private float PitchShift(float x, float ratio)
    {
        int len = _pitchBuf.Length;
        _pitchBuf[_pitchWrite] = x;

        // cross fade the heads
        _pitchPhase += (1f - ratio) / _window;
        if (_pitchPhase >= 1f) _pitchPhase -= 1f;
        if (_pitchPhase < 0f) _pitchPhase += 1f;

        float p1 = _pitchPhase;
        float p2 = p1 + 0.5f; if (p2 >= 1f) p2 -= 1f;

        float y = ReadDelay(p1 * _window) * MathF.Sin(MathF.PI * p1)
                + ReadDelay(p2 * _window) * MathF.Sin(MathF.PI * p2);

        if (++_pitchWrite >= len) _pitchWrite = 0;
        return y;
    }

    private float ReadDelay(float delay)
    {
        int len = _pitchBuf.Length;
        float pos = _pitchWrite - delay;
        while (pos < 0) pos += len;
        int i0 = (int)pos;
        float frac = pos - i0;
        int i1 = i0 + 1; if (i1 >= len) i1 = 0;
        if (i0 >= len) i0 -= len;
        return _pitchBuf[i0] + (_pitchBuf[i1] - _pitchBuf[i0]) * frac;
    }
}
