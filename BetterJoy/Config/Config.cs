using System;
using System.Configuration;
using System.Reflection;

namespace BetterJoy.Config;

public abstract class Config
{
    protected MainForm _form;
    public bool ShowErrors = true;

    protected Config(MainForm form)
    {
        _form = form;
    }

    protected Config(Config config) : this(config._form) { }
    public abstract void Update();
    public abstract Config Clone();
        
    protected void UpdateSetting<T>(string key, ref T setting, T defaultValue)
    {
        var value = ConfigurationManager.AppSettings[key];

        if (value != null)
        {
            try
            {
                var type = typeof(T);
                if (type.IsEnum)
                {
                    setting = (T)Enum.Parse(type, value, true);
                }
                else if (type == typeof(string) || type is IConvertible || type.IsValueType)
                {
                    setting = (T)Convert.ChangeType(value, type);
                }
                else
                {
                    var method = type.GetMethod("Parse", BindingFlags.Static | BindingFlags.Public, [typeof(string)]);
                    setting = (T)method!.Invoke(null, [value])!;
                }
                return;
            }
            catch (FormatException) { }
            catch (InvalidCastException) { }
            catch (ArgumentException) { }
        }

        setting = defaultValue;

        if (ShowErrors)
        {
            _form.Log($"Invalid value \"{value}\" for setting {key}! Using default value \"{defaultValue}\".", Logger.LogLevel.Warning);
        }
    }
}
