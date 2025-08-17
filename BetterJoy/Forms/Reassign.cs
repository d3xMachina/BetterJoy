using BetterJoy.Controller;
using System;
using System.Drawing;
using System.Windows.Forms;
using WindowsInput.Events;
using WindowsInput.Events.Sources;

namespace BetterJoy.Forms;

public partial class Reassign : Form
{
    public event EventHandler<EventArgs>? ActionAssigned;

    private Control? _curAssignment;

    private enum ButtonAction
    {
        None = 0,
        Disabled = 1
    }

    public Reassign()
    {
        InitializeComponent();

        var menuJoyButtons = CreateMenuJoyButtons();

        var menuJoyButtonsNoDisable = CreateMenuJoyButtons();
        var key = Enum.GetName(ButtonAction.Disabled);
        menuJoyButtonsNoDisable.Items.RemoveByKey(key);

        foreach (var c in new[]
                 {
                     btn_capture, btn_home, btn_sl_l, btn_sl_r, btn_sr_l, btn_sr_r, btn_shake, btn_reset_mouse,
                     btn_active_gyro, btn_swap_ab, btn_swap_xy
                 })
        {
            c.Tag = c.Name[4..];
            GetPrettyName(c);

            tip_reassign.SetToolTip(
                c,
                $"Left-click to detect input.{Environment.NewLine}Middle-click to clear to default.{Environment.NewLine}Right-click to see more options."
            );
            c.MouseDown += Remap;
            c.Menu = (c.Parent == gb_inputs) ? menuJoyButtons : menuJoyButtonsNoDisable;
            c.TextAlign = ContentAlignment.MiddleLeft;
        }
    }

    private void Menu_joy_buttons_ItemClicked(object? sender, ToolStripItemClickedEventArgs e)
    {
        if (sender is Control {Tag: SplitButton caller} &&
            e.ClickedItem is {Tag: object clickedItem})
        {
            string prefix = "";

            if (clickedItem is not ButtonAction action)
            {
                prefix = "joy_";
            }
            else if (action != ButtonAction.None)
            {
                prefix = "act_";
            }

            Assign(caller, prefix + $"{(int)clickedItem}");
        }
    }

    private void Remap(object? sender, MouseEventArgs e)
    {
        if (sender is not SplitButton control)
        {
            return;
        }

        switch (e.Button)
        {
            case MouseButtons.Left:
                control.Text = "...";
                _curAssignment = control;
                break;
            case MouseButtons.Middle:
                Assign(control, Settings.GetDefaultValue(control.Tag));
                break;
            case MouseButtons.Right:
                break;
        }
    }

    private void Reassign_Load(object sender, EventArgs e)
    {
        InputCapture.Global.RegisterEvent(GlobalKeyEvent);
        InputCapture.Global.RegisterEvent(GlobalMouseEvent);
    }

    private void GlobalMouseEvent(object? sender, EventSourceEventArgs<MouseEvent> e)
    {
        ButtonCode? button = e.Data.ButtonDown?.Button;

        if (_curAssignment != null && button != null)
        {
            Assign(_curAssignment, "mse_" + (int)button);

            _curAssignment = null;
            e.Next_Hook_Enabled = false;
        }
    }

    private void GlobalKeyEvent(object? sender, EventSourceEventArgs<KeyboardEvent> e)
    {
        KeyCode? key = e.Data.KeyDown?.Key;

        if (_curAssignment != null && key != null)
        {
            Assign(_curAssignment, "key_" + (int)key);

            _curAssignment = null;
            e.Next_Hook_Enabled = false;
        }
    }

    private void Reassign_FormClosing(object sender, FormClosingEventArgs e)
    {
        InputCapture.Global.UnregisterEvent(GlobalKeyEvent);
        InputCapture.Global.UnregisterEvent(GlobalMouseEvent);
    }

    private void GetPrettyName(Control c)
    {
        if (InvokeRequired)
        {
            Invoke(new Action<Control>(GetPrettyName), c);
            return;
        }

        string val = Settings.Value(c.Tag);

        if (val == "0")
        {
            c.Text = "";
        }
        else
        {
            var type =
                    val.StartsWith("act_") ? typeof(ButtonAction) :
                    val.StartsWith("joy_") ? typeof(Joycon.Button) :
                    val.StartsWith("key_") ? typeof(KeyCode) : typeof(ButtonCode);

            c.Text = Enum.GetName(type, int.Parse(val.AsSpan(4)));
        }
    }

    private void btn_apply_Click(object sender, EventArgs e)
    {
        Settings.Save();
    }

    private void btn_ok_Click(object sender, EventArgs e)
    {
        btn_apply_Click(sender, e);
        Close();
    }

    private ContextMenuStrip CreateMenuJoyButtons()
    {
        var menuJoyButtons = new ContextMenuStrip(components);

        foreach (var action in Enum.GetValues<ButtonAction>())
        {
            var name = action.ToString();
            var temp = new ToolStripMenuItem(name)
            {
                Name = name,
                Tag = action
            };
            menuJoyButtons.Items.Add(temp);
        }

        foreach (var button in Enum.GetValues<Joycon.Button>())
        {
            var name = button.ToString();
            var temp = new ToolStripMenuItem(name)
            {
                Name = name,
                Tag = button
            };
            menuJoyButtons.Items.Add(temp);
        }

        menuJoyButtons.ItemClicked += Menu_joy_buttons_ItemClicked;

        return menuJoyButtons;
    }

    private void Assign(Control control, string input)
    {
        Settings.SetValue(control.Tag, input);
        GetPrettyName(control);

        if (control.Parent == gb_actions)
        {
            ActionAssigned?.Invoke(this, EventArgs.Empty);
        }
    }
}
