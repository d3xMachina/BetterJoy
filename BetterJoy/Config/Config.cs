using System;
using System.Configuration;
using System.Linq;
using System.Reflection;

namespace BetterJoy.Config;

public abstract class Config
{
    protected readonly Logger? _logger;
    public bool ShowErrors = true;

    protected Config(Logger? logger)
    {
        _logger = logger;
    }

    protected Config(Config config) : this(config._logger) { }
    public abstract void Update();
    public abstract Config Clone();

    private void ParseAs<T>(string value, ref T setting)
    {
        if (setting is Array)
        {
            ParseListAs(value, (dynamic)setting);
        }
        else if (setting is Enum)
        {
            setting = (T)Enum.Parse(typeof(T), value, true);
        }
        else if (setting is string || setting is IConvertible)
        {
            setting = (T)Convert.ChangeType(value, typeof(T));
        }
        else
        {
            var type = typeof(T);
            var method = type.GetMethod("Parse", BindingFlags.Static | BindingFlags.Public, [typeof(string)]);
            setting = (T)method!.Invoke(null, [value])!;
        }
    }

    private void ParseListAs<T>(string value, T[] settings) where T : new() //Note, even though the array is passed by value, edits to it persist to the original
    {
        var tokens = value
            .Split(',', StringSplitOptions.TrimEntries)
            .Take(settings.Length).ToArray();

        tokens = tokens
            .Concat(Enumerable.Repeat(tokens.Last(), settings.Length - tokens.Length))
            .ToArray();

        for (int i = 0; i < settings.Length; i++)
        {
            T temp = settings[i];
            ParseAs(tokens[i], ref temp);
            settings[i] = temp;
        }
    }

    protected void TryUpdateSetting<T>(string key, ref T setting)
    {
        var defaultValue = setting;
        var value = ConfigurationManager.AppSettings[key];

        if (value != null)
        {
            try
            {
                ParseAs(value, ref setting);

                return;
            }
            catch (FormatException) { }
            catch (InvalidCastException) { }
            catch (ArgumentException) { }
        }

        setting = defaultValue;

        if (ShowErrors)
        {
            string defaultValueTxt;
            if (defaultValue is Array array)
            {
                defaultValueTxt = $"{string.Join(",", array.Cast<object>())}";
            }
            else
            {
                defaultValueTxt = $"{defaultValue}";
            }

            _logger?.Log($"Invalid value \"{value}\" for setting {key}! Using safe value \"{defaultValueTxt}\".", Logger.LogLevel.Warning);
        }
    }
}
