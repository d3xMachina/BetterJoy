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
        switch (setting)
        {
            case Array:
                ParseArrayAs(value, (dynamic)setting);
                break;
            case Enum:
                setting = (T)Enum.Parse(typeof(T), value, true);
                break;
            case IConvertible:
                setting = (T)Convert.ChangeType(value, typeof(T));
                break;
            default:
            {
                var method = typeof(T).GetMethod("Parse", BindingFlags.Static | BindingFlags.Public, [typeof(string)]);
                setting = (T)method!.Invoke(null, [value])!;
                break;
            }
        }
    }

    private void ParseArrayAs<T>(string value, T[] settings)
    {
        var tokens = value.Split(',', StringSplitOptions.TrimEntries);

        for (int i = 0; i < settings.Length; i++)
        {
            var currentToken = i < tokens.Length ? tokens[i] : tokens[^1];
            ParseAs(currentToken, ref settings[i]);
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
