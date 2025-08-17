using BetterJoy.Controller;
using System;
using System.Collections.Generic;
using System.IO;
using WindowsInput.Events;

namespace BetterJoy;

public static class Settings
{
    private const int SettingsNum = 13; // currently - ProgressiveScan, StartInTray + special buttons

    // stores dynamic configuration, including
    private static readonly string _path;
    private static readonly Dictionary<string, string> _variables = [];
    private static readonly string[] _actionKeys = ["reset_mouse", "active_gyro", "swap_ab", "swap_xy"];

    static Settings()
    {
        _path = Path.GetDirectoryName(Environment.ProcessPath) + "\\settings";
    }

    public static string GetDefaultValue(object? obj) => obj is string key ? GetDefaultValue(key) : "0";
    
    public static string GetDefaultValue(string key)
    {
        return key switch
        {
            "ProgressiveScan" => "1",
            "capture" => "key_" + (int)KeyCode.PrintScreen,
            "reset_mouse" => "joy_" + (int)Joycon.Button.Stick,
            _ => "0",
        };
    }

    // Helper function to count how many lines are in a file
    // https://www.dotnetperls.com/line-count
    private static long CountLinesInFile(string f)
    {
        // Zero based count
        long count = -1;
        using (var r = new StreamReader(f))
        {
            while (r.ReadLine() != null)
            {
                count++;
            }
        }

        return count;
    }

    public static void Init(
        List<KeyValuePair<string, short[]>> calibrationMotionData,
        List<KeyValuePair<string, ushort[]>> calibrationSticksData
    )
    {
        foreach (var s in new[]
                 {
                     "ProgressiveScan", "StartInTray", "capture", "home", "sl_l", "sl_r", "sr_l", "sr_r",
                     "shake", "reset_mouse", "active_gyro", "swap_ab", "swap_xy"
                 })
        {
            _variables[s] = GetDefaultValue(s);
        }

        if (File.Exists(_path))
        {
            // Reset settings file if old settings
            if (CountLinesInFile(_path) < SettingsNum)
            {
                File.Delete(_path);
                Init(calibrationMotionData, calibrationSticksData);
                return;
            }

            using var file = new StreamReader(_path);
            var line = string.Empty;
            var lineNo = 0;
            while ((line = file.ReadLine()) != null)
            {
                var vs = line.Split();
                try
                {
                    if (lineNo < SettingsNum)
                    {
                        // load in basic settings
                        _variables[vs[0]] = vs[1];
                    }
                    else
                    {
                        // load in calibration presets
                        if (lineNo == SettingsNum)
                        {
                            // Motion
                            calibrationMotionData.Clear();
                            for (var i = 0; i < vs.Length; i++)
                            {
                                var caliArr = vs[i].Split(',');
                                var newArr = new short[6];
                                for (var j = 1; j < caliArr.Length; j++)
                                {
                                    newArr[j - 1] = short.Parse(caliArr[j]);
                                }

                                calibrationMotionData.Add(
                                    new KeyValuePair<string, short[]>(
                                        caliArr[0],
                                        newArr
                                    )
                                );
                            }
                        }
                        else if (lineNo == SettingsNum + 1)
                        {
                            // Sticks
                            calibrationSticksData.Clear();
                            for (var i = 0; i < vs.Length; i++)
                            {
                                var caliArr = vs[i].Split(',');
                                var newArr = new ushort[12];
                                for (var j = 1; j < caliArr.Length; j++)
                                {
                                    newArr[j - 1] = ushort.Parse(caliArr[j]);
                                }

                                calibrationSticksData.Add(
                                    new KeyValuePair<string, ushort[]>(
                                        caliArr[0],
                                        newArr
                                    )
                                );
                            }
                        }
                    }
                }
                catch { }

                lineNo++;
            }
        }
        else
        {
            using var file = new StreamWriter(_path);
            foreach (var k in _variables.Keys)
            {
                file.WriteLine("{0} {1}", k, _variables[k]);
            }

            // Motion Calibration
            var caliStr = "";
            for (var i = 0; i < calibrationMotionData.Count; i++)
            {
                var space = " ";
                if (i == 0)
                {
                    space = "";
                }

                caliStr += space + calibrationMotionData[i].Key + "," + string.Join(",", calibrationMotionData[i].Value);
            }

            file.WriteLine(caliStr);

            // Stick Calibration
            caliStr = "";
            for (var i = 0; i < calibrationSticksData.Count; i++)
            {
                var space = " ";
                if (i == 0)
                {
                    space = "";
                }

                caliStr += space + calibrationSticksData[i].Key + "," + string.Join(",", calibrationSticksData[i].Value);
            }

            file.WriteLine(caliStr);
        }
    }

    public static int IntValue(string key) => _variables.TryGetValue(key, out string? value) ? int.Parse(value) : 0;

    public static string Value(object? obj) => obj is string key ? Value(key) : "";
    
    public static string Value(string key) => _variables.GetValueOrDefault(key, "");
    
    public static bool SetValue(object? obj, string value) => 
        obj is string key && 
        SetValue(key, value);

    public static bool SetValue(string key, string value)
    {
        if (!_variables.ContainsKey(key))
        {
            return false;
        }

        _variables[key] = value;
        return true;
    }

    public static void SaveCalibrationMotionData(List<KeyValuePair<string, short[]>> caliData)
    {
        var txt = File.ReadAllLines(_path);
        if (txt.Length < SettingsNum + 1) // no custom motion calibrations yet
        {
            Array.Resize(ref txt, txt.Length + 1);
        }

        var caliStr = "";
        for (var i = 0; i < caliData.Count; i++)
        {
            var space = " ";
            if (i == 0)
            {
                space = "";
            }

            caliStr += space + caliData[i].Key + "," + string.Join(",", caliData[i].Value);
        }

        txt[SettingsNum] = caliStr;
        File.WriteAllLines(_path, txt);
    }

    public static void SaveCaliSticksData(List<KeyValuePair<string, ushort[]>> caliData)
    {
        var txt = File.ReadAllLines(_path);
        if (txt.Length < SettingsNum + 2) // no custom sticks calibrations yet
        {
            Array.Resize(ref txt, txt.Length + 1);
        }

        var caliStr = "";
        for (var i = 0; i < caliData.Count; i++)
        {
            var space = " ";
            if (i == 0)
            {
                space = "";
            }

            caliStr += space + caliData[i].Key + "," + string.Join(",", caliData[i].Value);
        }

        txt[SettingsNum + 1] = caliStr;
        File.WriteAllLines(_path, txt);
    }

    public static void Save()
    {
        var txt = File.ReadAllLines(_path);
        var no = 0;
        foreach (var k in _variables.Keys)
        {
            txt[no] = $"{k} {_variables[k]}";
            no++;
        }

        File.WriteAllLines(_path, txt);
    }

    public static ReadOnlySpan<string> GetActionsKeys()
    {
        return _actionKeys;
    }
}
