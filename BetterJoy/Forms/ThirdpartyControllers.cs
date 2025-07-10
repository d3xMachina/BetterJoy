#nullable disable
using BetterJoy.Controller;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;

namespace BetterJoy.Forms;

public partial class _3rdPartyControllers : Form
{
    private static readonly string _path;

    static _3rdPartyControllers()
    {
        _path = Path.GetDirectoryName(Environment.ProcessPath)
               + "\\3rdPartyControllers";
    }

    public _3rdPartyControllers()
    {
        InitializeComponent();
        list_allControllers.HorizontalScrollbar = true;
        list_customControllers.HorizontalScrollbar = true;

        list_allControllers.Sorted = true;
        list_customControllers.Sorted = true;

        foreach (Joycon.ControllerType type in Enum.GetValues<Joycon.ControllerType>())
        {
            chooseType.Items.Add(Joycon.GetControllerName(type));
        }

        chooseType.FormattingEnabled = true;
        group_props.Controls.Add(chooseType);
        group_props.Enabled = false;

        GetSavedThirdpartyControllers().ForEach(controller => list_customControllers.Items.Add(controller));
        RefreshControllerList();
    }

    public static List<SController> GetSavedThirdpartyControllers()
    {
        var controllers = new List<SController>();

        if (File.Exists(_path))
        {
            using var file = new StreamReader(_path);
            var line = string.Empty;
            while ((line = file.ReadLine()) != null && line != string.Empty)
            {
                var split = line.Split('|');
                //won't break existing config file
                var serialNumber = "";
                if (split.Length > 4)
                {
                    serialNumber = split[4];
                }

                controllers.Add(
                    new SController(
                        split[0],
                        ushort.Parse(split[1]),
                        ushort.Parse(split[2]),
                        byte.Parse(split[3]),
                        serialNumber
                    )
                );
            }
        }

        return controllers;
    }

    private List<SController> GetActiveThirdpartyControllers()
    {
        var controllers = new List<SController>();

        foreach (SController v in list_customControllers.Items)
        {
            controllers.Add(v);
        }

        return controllers;
    }

    private void CopyCustomControllers()
    {
        var controllers = GetActiveThirdpartyControllers();
        Program.UpdateThirdpartyControllers(controllers);
    }

    private static bool ContainsText(ListBox a, string manu)
    {
        foreach (SController v in a.Items)
        {
            if (v == null)
            {
                continue;
            }

            if (v.Name == null)
            {
                continue;
            }

            if (v.Name.Equals(manu))
            {
                return true;
            }
        }

        return false;
    }

    private void RefreshControllerList()
    {
        list_allControllers.Items.Clear();
        var devices = HIDApi.Manager.EnumerateDevices(0x0, 0x0);

        // Add devices to the list
        foreach (var device in devices)
        {
            if (device.SerialNumber == null)
            {
                continue;
            }

            var name = device.ProductString;
            if (string.IsNullOrWhiteSpace(name))
            {
                name = "Unknown";
            }

            name += $" (P{device.VendorId:X2} V{device.ProductId:X2}";
            if (!string.IsNullOrWhiteSpace(device.SerialNumber))
            {
                name += $" S{device.SerialNumber}";
            }
            name += ")";

            if (!ContainsText(list_customControllers, name) && !ContainsText(list_allControllers, name))
            {
                list_allControllers.Items.Add(
                    new SController(name, device.VendorId, device.ProductId, 0, device.SerialNumber)
                );
                // 0 type is undefined
                Console.WriteLine("Found controller " + name);
            }
        }
    }

    private void btn_add_Click(object sender, EventArgs e)
    {
        if (list_allControllers.SelectedItem != null)
        {
            list_customControllers.Items.Add(list_allControllers.SelectedItem);
            list_allControllers.Items.Remove(list_allControllers.SelectedItem);

            list_allControllers.ClearSelected();
        }
    }

    private void btn_remove_Click(object sender, EventArgs e)
    {
        if (list_customControllers.SelectedItem != null)
        {
            list_allControllers.Items.Add(list_customControllers.SelectedItem);
            list_customControllers.Items.Remove(list_customControllers.SelectedItem);

            list_customControllers.ClearSelected();
        }
    }

    private void btn_apply_Click(object sender, EventArgs e)
    {
        var sc = "";
        foreach (SController v in list_customControllers.Items)
        {
            sc += v.Serialise() + Environment.NewLine;
        }

        File.WriteAllText(_path, sc);
        CopyCustomControllers();
    }

    private void btn_applyAndClose_Click(object sender, EventArgs e)
    {
        btn_apply_Click(sender, e);
        Close();
    }

    private void _3rdPartyControllers_FormClosing(object sender, FormClosingEventArgs e)
    {
        btn_apply_Click(sender, e);
    }

    private void btn_refresh_Click(object sender, EventArgs e)
    {
        RefreshControllerList();
    }

    private void list_allControllers_SelectedValueChanged(object sender, EventArgs e)
    {
        if (list_allControllers.SelectedItem != null)
        {
            tip_device.Show((list_allControllers.SelectedItem as SController).Name, list_allControllers);
        }
    }

    private void list_customControllers_SelectedValueChanged(object sender, EventArgs e)
    {
        if (list_customControllers.SelectedItem != null)
        {
            var v = list_customControllers.SelectedItem as SController;
            tip_device.Show(v.Name, list_customControllers);

            chooseType.SelectedIndex = v.Type - 1;

            group_props.Enabled = true;
        }
        else
        {
            chooseType.SelectedIndex = -1;
            group_props.Enabled = false;
        }
    }

    private void list_customControllers_MouseDown(object sender, MouseEventArgs e)
    {
        if (e.Y > list_customControllers.ItemHeight * list_customControllers.Items.Count)
        {
            list_customControllers.SelectedItems.Clear();
        }
    }

    private void list_allControllers_MouseDown(object sender, MouseEventArgs e)
    {
        if (e.Y > list_allControllers.ItemHeight * list_allControllers.Items.Count)
        {
            list_allControllers.SelectedItems.Clear();
        }
    }

    private void chooseType_SelectedValueChanged(object sender, EventArgs e)
    {
        if (list_customControllers.SelectedItem != null)
        {
            var v = list_customControllers.SelectedItem as SController;
            v.Type = (byte)(chooseType.SelectedIndex + 1);
        }
    }

    public class SController
    {
        public readonly string Name;
        public readonly ushort ProductId;
        public readonly string SerialNumber;
        public byte Type; // 1 is pro, 2 is left joy, 3 is right joy, 4 is snes, 5 is n64
        public readonly ushort VendorId;

        public SController(string name, ushort vendorId, ushort productId, byte type, string serialNumber)
        {
            ProductId = productId;
            VendorId = vendorId;
            Type = type;
            SerialNumber = serialNumber;
            Name = name;
        }

        public override bool Equals(object obj)
        {
            //Check for null and compare run-time types.
            if (obj == null || !GetType().Equals(obj.GetType()))
            {
                return false;
            }

            var s = (SController)obj;
            return s.ProductId == ProductId && s.VendorId == VendorId && s.SerialNumber == SerialNumber;
        }

        public override int GetHashCode()
        {
            return Tuple.Create(ProductId, VendorId, SerialNumber).GetHashCode();
        }

        public override string ToString()
        {
            return Name ?? $"Unidentified Device ({ProductId})";
        }

        public string Serialise()
        {
            return $"{Name}|{VendorId}|{ProductId}|{Type}|{SerialNumber}";
        }
    }
}
