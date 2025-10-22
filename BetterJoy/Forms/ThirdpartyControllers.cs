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
        _path = Path.GetDirectoryName(Program.ProgramLocation)
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
            while (!string.IsNullOrEmpty(line = file.ReadLine()))
            {
                var split = line.Split('|');
                if (split.Length < 6)
                {
                    continue;
                }

                controllers.Add(
                    new SController(
                        split[0],
                        split[1],
                        ushort.Parse(split[2]),
                        ushort.Parse(split[3]),
                        split[4],
                        byte.Parse(split[5])
                    )
                );
            }
        }

        return controllers;
    }

    private List<SController> GetActiveThirdpartyControllers()
    {
        var controllers = new List<SController>();

        foreach (SController controller in list_customControllers.Items)
        {
            controllers.Add(controller);
        }

        return controllers;
    }

    private void CopyCustomControllers()
    {
        var controllers = GetActiveThirdpartyControllers();
        Program.UpdateThirdpartyControllers(controllers);
    }

    private static bool ContainsController(ListBox a, SController controller)
    {
        foreach (SController currentController in a.Items)
        {
            if (currentController.Equals(controller))
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
            var controller = new SController(
                device.ManufacturerString,
                device.ProductString,
                device.VendorId,
                device.ProductId,
                device.SerialNumber,
                0 // type not set
            );

            if (!ContainsController(list_customControllers, controller) && !ContainsController(list_allControllers, controller))
            {
                list_allControllers.Items.Add(controller);
                Console.WriteLine($"Found controller {controller}");
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
        var content = "";
        foreach (SController controller in list_customControllers.Items)
        {
            content += controller.Serialise() + Environment.NewLine;
        }

        File.WriteAllText(_path, content);
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
        if (list_allControllers.SelectedItem is SController controller)
        {
            tip_device.Show(controller.ToString(), list_allControllers);
        }
    }

    private void list_customControllers_SelectedValueChanged(object sender, EventArgs e)
    {
        if (list_customControllers.SelectedItem is SController controller)
        {
            tip_device.Show(controller.ToString(), list_customControllers);

            chooseType.SelectedIndex = controller.Type - 1;
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
        if (list_customControllers.SelectedItem is SController controller)
        {
            controller.Type = (byte)(chooseType.SelectedIndex + 1);
        }
    }

    public class SController
    {
        public readonly string Manufacturer;
        public readonly string Product;
        public readonly ushort VendorId;
        public readonly ushort ProductId;
        public readonly string SerialNumber;
        public byte Type;
        private string? _name;

        public SController(string manufacturer, string product, ushort vendorId, ushort productId, string serialNumber, byte type)
        {
            Manufacturer = manufacturer;
            Product = product;
            VendorId = vendorId;
            ProductId = productId;
            SerialNumber = serialNumber;
            Type = type;
        }

        public override bool Equals(object? obj)
        {
            if (obj is not SController controllerObj)
            {
                return false;
            }

            return controllerObj.Manufacturer == Manufacturer &&
                   controllerObj.Product == Product &&
                   controllerObj.VendorId == VendorId &&
                   controllerObj.ProductId == ProductId &&
                   controllerObj.SerialNumber == SerialNumber;
        }

        public override int GetHashCode()
        {
            return Tuple.Create(Manufacturer, Product, VendorId, ProductId, SerialNumber).GetHashCode();
        }

        public override string ToString()
        {
            if (_name == null)
            {
                _name = Manufacturer;

                if (Product != "")
                {
                    _name += $" {Product}";
                }
                else
                {
                    _name = "Unknown";
                }

                _name += $" (V{VendorId:X2} P{ProductId:X2}";

                if (SerialNumber != "")
                {
                    _name += $" S{SerialNumber}";
                }

                _name += ")";
            }

            return _name;
        }

        public string Serialise()
        {
            return $"{Manufacturer}|{Product}|{VendorId}|{ProductId}|{SerialNumber}|{Type}";
        }
    }
}
