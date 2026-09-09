using System.Collections.Generic;
using System.Globalization;
using System.Text;

public sealed class WorldGenBenchJson
{
    private readonly StringBuilder _text = new();
    private readonly List<char> _open = new();
    private readonly List<bool> _empty = new();

    public WorldGenBenchJson()
    {
        Push('{');
    }

    public static string Escape(string value)
    {
        if (value == null)
            return "null";

        var text = new StringBuilder(value.Length + 2);
        text.Append('"');

        foreach (char symbol in value)
        {
            switch (symbol)
            {
                case '"':
                    text.Append("\\\"");
                    break;
                case '\\':
                    text.Append("\\\\");
                    break;
                case '\n':
                    text.Append("\\n");
                    break;
                case '\r':
                    text.Append("\\r");
                    break;
                case '\t':
                    text.Append("\\t");
                    break;
                default:
                    if (symbol < ' ')
                        text.Append("\\u").Append(((int)symbol).ToString("x4", CultureInfo.InvariantCulture));
                    else
                        text.Append(symbol);

                    break;
            }
        }

        text.Append('"');
        return text.ToString();
    }

    public static string Number(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return "null";

        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    public WorldGenBenchJson Text(string key, string value)
    {
        Key(key);
        _text.Append(value == null ? "null" : Escape(value));
        return this;
    }

    public WorldGenBenchJson Value(string key, double value)
    {
        Key(key);
        _text.Append(Number(value));
        return this;
    }

    public WorldGenBenchJson Value(string key, long value)
    {
        Key(key);
        _text.Append(value.ToString(CultureInfo.InvariantCulture));
        return this;
    }

    public WorldGenBenchJson Value(string key, bool value)
    {
        Key(key);
        _text.Append(value ? "true" : "false");
        return this;
    }

    public WorldGenBenchJson Null(string key)
    {
        Key(key);
        _text.Append("null");
        return this;
    }

    public WorldGenBenchJson Object(string key)
    {
        Key(key);
        Push('{');
        return this;
    }

    public WorldGenBenchJson Array(string key)
    {
        Key(key);
        Push('[');
        return this;
    }

    public WorldGenBenchJson Item(double value)
    {
        Separator();
        _text.Append(Number(value));
        return this;
    }

    public WorldGenBenchJson Item(string value)
    {
        Separator();
        _text.Append(Escape(value));
        return this;
    }

    public WorldGenBenchJson ItemObject()
    {
        Separator();
        Push('{');
        return this;
    }

    public WorldGenBenchJson End()
    {
        int last = _open.Count - 1;
        _text.Append(_open[last] == '{' ? '}' : ']');
        _open.RemoveAt(last);
        _empty.RemoveAt(last);
        return this;
    }

    public override string ToString()
    {
        while (_open.Count > 0)
            End();

        return _text.ToString();
    }

    private void Push(char symbol)
    {
        _text.Append(symbol);
        _open.Add(symbol);
        _empty.Add(true);
    }

    private void Key(string key)
    {
        Separator();
        _text.Append(Escape(key)).Append(':');
    }

    private void Separator()
    {
        int last = _open.Count - 1;

        if (last < 0)
            return;

        if (_empty[last])
            _empty[last] = false;
        else
            _text.Append(',');
    }
}
