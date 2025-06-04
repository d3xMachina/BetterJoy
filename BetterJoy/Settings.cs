using System;
using System.Collections.Generic;
using System.IO;
using WindowsInput.Events;

namespace BetterJoy;

public static class Settings
{
    private const int SettingsNum = 13; // currently - ProgressiveScan, StartInTray + special buttons

    // stores dynamic configuration, including
    private static readonly string Path;
    private static readonly Dictionary<string, string> Variables = new();
    private static readonly string[] _actionKeys = ["reset_mouse", "active_gyro", "swap_ab", "swap_xy"];

    static Settings()
    {
        Path = System.IO.Path.GetDirectoryName(Environment.ProcessPath) + "\\settings";
    }

    public static string GetDefaultValue(string s)
    {
        switch (s)
        {
            case "ProgressiveScan":
                return "1";
            case "capture":
                return "key_" + (int)KeyCode.PrintScreen;
            case "reset_mouse":
                return "joy_" + (int)Joycon.Button.Stick;
        }

        return "0";
    }

    // Helper function to count how many lines are in a file
    // https://www.dotnetperls.com/line-count
    private static long CountLinesInFile(string f)
    {
        // Zero based count
        long count = -1;
        using (var r = new StreamReader(f))
        {
            string line;
            while ((line = r.ReadLine()) != null)
            {
                count++;
            }
        }

        return count;
    }

    public static void Init(
        List<KeyValuePair<string, short[]>> caliIMUData,
        List<KeyValuePair<string, ushort[]>> caliSticksData
    )
    {
        foreach (var s in new[]
                 {
                     "ProgressiveScan", "StartInTray", "capture", "home", "sl_l", "sl_r", "sr_l", "sr_r",
                     "shake", "reset_mouse", "active_gyro", "swap_ab", "swap_xy"
                 })
        {
            Variables[s] = GetDefaultValue(s);
        }

        if (File.Exists(Path))
        {
            // Reset settings file if old settings
            if (CountLinesInFile(Path) < SettingsNum)
            {
                File.Delete(Path);
                Init(caliIMUData, caliSticksData);
                return;
            }

            using (var file = new StreamReader(Path))
            {
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
                            Variables[vs[0]] = vs[1];
                        }
                        else
                        {
                            // load in calibration presets
                            if (lineNo == SettingsNum)
                            {
                                // IMU
                                caliIMUData.Clear();
                                for (var i = 0; i < vs.Length; i++)
                                {
                                    var caliArr = vs[i].Split(',');
                                    var newArr = new short[6];
                                    for (var j = 1; j < caliArr.Length; j++)
                                    {
                                        newArr[j - 1] = short.Parse(caliArr[j]);
                                    }

                                    caliIMUData.Add(
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
                                caliSticksData.Clear();
                                for (var i = 0; i < vs.Length; i++)
                                {
                                    var caliArr = vs[i].Split(',');
                                    var newArr = new ushort[12];
                                    for (var j = 1; j < caliArr.Length; j++)
                                    {
                                        newArr[j - 1] = ushort.Parse(caliArr[j]);
                                    }

                                    caliSticksData.Add(
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
        }
        else
        {
            using (var file = new StreamWriter(Path))
            {
                foreach (var k in Variables.Keys)
                {
                    file.WriteLine("{0} {1}", k, Variables[k]);
                }

                // IMU Calibration
                var caliStr = "";
                for (var i = 0; i < caliIMUData.Count; i++)
                {
                    var space = " ";
                    if (i == 0)
                    {
                        space = "";
                    }

                    caliStr += space + caliIMUData[i].Key + "," + string.Join(",", caliIMUData[i].Value);
                }

                file.WriteLine(caliStr);

                // Stick Calibration
                caliStr = "";
                for (var i = 0; i < caliSticksData.Count; i++)
                {
                    var space = " ";
                    if (i == 0)
                    {
                        space = "";
                    }

                    caliStr += space + caliSticksData[i].Key + "," + string.Join(",", caliSticksData[i].Value);
                }

                file.WriteLine(caliStr);
            }
        }
    }

    public static int IntValue(string key)
    {
        if (!Variables.ContainsKey(key))
        {
            return 0;
        }

        return int.Parse(Variables[key]);
    }

    public static string Value(string key)
    {
        if (!Variables.ContainsKey(key))
        {
            return "";
        }

        return Variables[key];
    }

    public static bool SetValue(string key, string value)
    {
        if (!Variables.ContainsKey(key))
        {
            return false;
        }

        Variables[key] = value;
        return true;
    }

    public static void SaveCaliIMUData(List<KeyValuePair<string, short[]>> caliData)
    {
        var txt = File.ReadAllLines(Path);
        if (txt.Length < SettingsNum + 1) // no custom IMU calibrations yet
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
        File.WriteAllLines(Path, txt);
    }

    public static void SaveCaliSticksData(List<KeyValuePair<string, ushort[]>> caliData)
    {
        var txt = File.ReadAllLines(Path);
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
        File.WriteAllLines(Path, txt);
    }

    public static void Save()
    {
        var txt = File.ReadAllLines(Path);
        var no = 0;
        foreach (var k in Variables.Keys)
        {
            txt[no] = $"{k} {Variables[k]}";
            no++;
        }

        File.WriteAllLines(Path, txt);
    }

    public static ReadOnlySpan<string> GetActionsKeys()
    {
        return _actionKeys;
    }
}
