using System.Text;

namespace AutoCorrect.Core.Autocorrect;

/// <summary>Accumulates the in-progress word for as-you-type matching.</summary>
public sealed class WordBuffer
{
    private readonly StringBuilder _sb = new();
    private readonly object _gate = new();

    public int Length
    {
        get { lock (_gate) return _sb.Length; }
    }

    public string Current
    {
        get { lock (_gate) return _sb.ToString(); }
    }

    public void Append(char c)
    {
        lock (_gate)
            _sb.Append(c);
    }

    public void Backspace()
    {
        lock (_gate)
        {
            if (_sb.Length > 0)
                _sb.Length--;
        }
    }

    public void Clear()
    {
        lock (_gate)
            _sb.Clear();
    }

    /// <summary>Return current word and clear the buffer.</summary>
    public string Take()
    {
        lock (_gate)
        {
            var w = _sb.ToString();
            _sb.Clear();
            return w;
        }
    }
}
