using System.Globalization;

namespace Charac.Client;

/// <summary>Tracks remote VT mouse modes and keeps stale mouse reports out of the shell.</summary>
internal sealed class TerminalMouseTracking
{
    private readonly byte[] _outputSequence = new byte[64];
    private readonly byte[] _inputSequence = new byte[64];
    private int _outputLength;
    private int _inputLength;
    private int _trackingModes;
    private int _forwardMouse;
    private int _alternateScreen;

    public bool HasPendingInput => _inputLength != 0;
    public bool InAlternateScreen => Volatile.Read(ref _alternateScreen) != 0;

    public void ObserveOutput(ReadOnlySpan<byte> data)
    {
        foreach (var value in data)
        {
            if (_outputLength == 0)
            {
                if (value == 0x1b)
                    _outputSequence[_outputLength++] = value;
                continue;
            }
            if (_outputLength == 1 && value != (byte)'[' ||
                _outputLength == 2 && value != (byte)'?')
            {
                _outputLength = value == 0x1b ? 1 : 0;
                continue;
            }
            if (_outputLength == _outputSequence.Length)
            {
                _outputLength = 0;
                continue;
            }
            _outputSequence[_outputLength++] = value;
            if (value is (byte)'h' or (byte)'l')
            {
                UpdateModes(value == (byte)'h');
                _outputLength = 0;
            }
            else if (_outputLength > 3 && value is not ((byte)';') &&
                (value < (byte)'0' || value > (byte)'9'))
                _outputLength = 0;
        }
    }

    public byte[] FilterInput(ReadOnlySpan<byte> data)
    {
        using var output = new MemoryStream(data.Length + _inputLength);
        foreach (var value in data)
        {
            if (_inputLength == 0)
            {
                if (value == 0x1b)
                    _inputSequence[_inputLength++] = value;
                else
                    output.WriteByte(value);
                continue;
            }

            var expected = _inputLength switch
            {
                1 => (byte)'[',
                2 => (byte)'<',
                _ => (byte)0
            };
            if (expected != 0 && value != expected || _inputLength >= 3 &&
                value is not ((byte)'M' or (byte)'m' or (byte)';') &&
                (value < (byte)'0' || value > (byte)'9') ||
                _inputLength == _inputSequence.Length)
            {
                output.Write(_inputSequence.AsSpan(0, _inputLength));
                _inputLength = 0;
                if (value == 0x1b)
                    _inputSequence[_inputLength++] = value;
                else
                    output.WriteByte(value);
                continue;
            }

            _inputSequence[_inputLength++] = value;
            if (value is (byte)'M' or (byte)'m')
            {
                if (Volatile.Read(ref _forwardMouse) != 0)
                    output.Write(_inputSequence.AsSpan(0, _inputLength));
                _inputLength = 0;
            }
        }
        return output.ToArray();
    }

    public byte[] FlushPendingInput()
    {
        var pending = _inputSequence[.._inputLength];
        _inputLength = 0;
        return pending;
    }

    private void UpdateModes(bool enable)
    {
        var text = System.Text.Encoding.ASCII.GetString(_outputSequence.AsSpan(3, _outputLength - 4));
        foreach (var part in text.Split(';'))
        {
            if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var mode))
                continue;
            if (mode == 1049)
            {
                Volatile.Write(ref _alternateScreen, enable ? 1 : 0);
                if (!enable)
                    _trackingModes = 0;
            }
            else
            {
                var bit = mode switch { 1000 => 1, 1002 => 2, 1003 => 4, _ => 0 };
                if (enable)
                    _trackingModes |= bit;
                else
                    _trackingModes &= ~bit;
            }
        }
        Volatile.Write(ref _forwardMouse, _trackingModes != 0 ? 1 : 0);
    }
}
