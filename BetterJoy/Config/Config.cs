﻿using System;
using System.Configuration;
using System.Linq;
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

    protected void ParseAs<T>(string value, Type type, ref T setting)
    {
        if (type.IsArray)
        {
            ParseArrayAs(value, type, ref setting);
        }
        else if (type.IsEnum)
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
    }

    private void ParseArrayAs<T>(string value, Type type, ref T setting)
    {
        var elements = setting as Array;
        if (elements == null)
        {
            throw new InvalidOperationException("setting must be an array.");
        }

        var tokens = value.Split(',');

        for (int i = 0, j = 0; i < elements.Length; ++i)
        {
            var token = tokens[j].Trim();
            object parsedValue = null;

            ParseAs(token, type.GetElementType(), ref parsedValue);
            elements.SetValue(parsedValue, i);

            if (j < tokens.Length - 1)
            {
                ++j;
            }
        }
    }
        
    protected void UpdateSetting<T>(string key, ref T setting, T defaultValue)
    {
        var value = ConfigurationManager.AppSettings[key];
        var type = typeof(T);

        if (value != null)
        {
            try
            {
                ParseAs(value, type, ref setting);

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

            _form.Log($"Invalid value \"{value}\" for setting {key}! Using default value \"{defaultValueTxt}\".", Logger.LogLevel.Warning);
        }
    }
}
