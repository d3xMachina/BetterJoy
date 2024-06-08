using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace BetterJoy.Controller
{
    public struct OutputControllerXbox360InputState
    {
        // buttons
        public bool ThumbStickLeft;
        public bool ThumbStickRight;

        public bool Y;
        public bool X;
        public bool B;
        public bool A;

        public bool Start;
        public bool Back;

        public bool Guide;

        public bool ShoulderLeft;
        public bool ShoulderRight;

        // dpad
        public bool DpadUp;
        public bool DpadRight;
        public bool DpadDown;
        public bool DpadLeft;

        // axis
        public short AxisLeftX;
        public short AxisLeftY;

        public short AxisRightX;
        public short AxisRightY;

        // triggers
        public byte TriggerLeft;
        public byte TriggerRight;

        public bool IsEqual(OutputControllerXbox360InputState other)
        {
            var buttons = ThumbStickLeft == other.ThumbStickLeft
                          && ThumbStickRight == other.ThumbStickRight
                          && Y == other.Y
                          && X == other.X
                          && B == other.B
                          && A == other.A
                          && Start == other.Start
                          && Back == other.Back
                          && Guide == other.Guide
                          && ShoulderLeft == other.ShoulderLeft
                          && ShoulderRight == other.ShoulderRight;

            var dpad = DpadUp == other.DpadUp
                       && DpadRight == other.DpadRight
                       && DpadDown == other.DpadDown
                       && DpadLeft == other.DpadLeft;

            var axis = AxisLeftX == other.AxisLeftX
                       && AxisLeftY == other.AxisLeftY
                       && AxisRightX == other.AxisRightX
                       && AxisRightY == other.AxisRightY;

            var triggers = TriggerLeft == other.TriggerLeft
                           && TriggerRight == other.TriggerRight;

            return buttons && dpad && axis && triggers;
        }
    }

    public class OutputControllerXbox360
    {
        public delegate void Xbox360FeedbackReceivedEventHandler(Xbox360FeedbackReceivedEventArgs e);

        private readonly IXbox360Controller _xboxController;

        private OutputControllerXbox360InputState _currentState;

        private bool _connected = false;

        public OutputControllerXbox360()
        {
            if (Program.EmClient == null)
            {
                return;
            }

            _xboxController = Program.EmClient.CreateXbox360Controller();
            Init();
        }

        public OutputControllerXbox360(ushort vendorId, ushort productId)
        {
            if (Program.EmClient == null)
            {
                return;
            }

            _xboxController = Program.EmClient.CreateXbox360Controller(vendorId, productId);
            Init();
        }

        public event Xbox360FeedbackReceivedEventHandler FeedbackReceived;

        private void Init()
        {
            if (_xboxController == null)
            {
                return;
            }

            _xboxController.FeedbackReceived += FeedbackReceivedRcv;
            _xboxController.AutoSubmitReport = false;
        }

        private void FeedbackReceivedRcv(object sender, Xbox360FeedbackReceivedEventArgs e)
        {
            FeedbackReceived?.Invoke(e);
        }

        public bool UpdateInput(OutputControllerXbox360InputState newState)
        {
            if (!_connected || _currentState.IsEqual(newState))
            {
                return false;
            }

            DoUpdateInput(newState);

            return true;
        }

        public bool IsConnected()
        {
            return _connected;
        }

        public void Connect()
        {
            if (_xboxController == null)
            {
                return;
            }

            _xboxController.Connect();
            _connected = true;
        }

        public void Disconnect()
        {
            _connected = false;

            try
            {
                _xboxController?.Disconnect();
            }
            catch { } // nothing we can do, might not be connected in the first place
        }

        private void DoUpdateInput(OutputControllerXbox360InputState newState)
        {
            if (_xboxController == null)
            {
                return;
            }

            _xboxController.SetButtonState(Xbox360Button.LeftThumb, newState.ThumbStickLeft);
            _xboxController.SetButtonState(Xbox360Button.RightThumb, newState.ThumbStickRight);

            _xboxController.SetButtonState(Xbox360Button.Y, newState.Y);
            _xboxController.SetButtonState(Xbox360Button.X, newState.X);
            _xboxController.SetButtonState(Xbox360Button.B, newState.B);
            _xboxController.SetButtonState(Xbox360Button.A, newState.A);

            _xboxController.SetButtonState(Xbox360Button.Start, newState.Start);
            _xboxController.SetButtonState(Xbox360Button.Back, newState.Back);
            _xboxController.SetButtonState(Xbox360Button.Guide, newState.Guide);

            _xboxController.SetButtonState(Xbox360Button.Up, newState.DpadUp);
            _xboxController.SetButtonState(Xbox360Button.Right, newState.DpadRight);
            _xboxController.SetButtonState(Xbox360Button.Down, newState.DpadDown);
            _xboxController.SetButtonState(Xbox360Button.Left, newState.DpadLeft);

            _xboxController.SetButtonState(Xbox360Button.LeftShoulder, newState.ShoulderLeft);
            _xboxController.SetButtonState(Xbox360Button.RightShoulder, newState.ShoulderRight);

            _xboxController.SetAxisValue(Xbox360Axis.LeftThumbX, newState.AxisLeftX);
            _xboxController.SetAxisValue(Xbox360Axis.LeftThumbY, newState.AxisLeftY);
            _xboxController.SetAxisValue(Xbox360Axis.RightThumbX, newState.AxisRightX);
            _xboxController.SetAxisValue(Xbox360Axis.RightThumbY, newState.AxisRightY);

            _xboxController.SetSliderValue(Xbox360Slider.LeftTrigger, newState.TriggerLeft);
            _xboxController.SetSliderValue(Xbox360Slider.RightTrigger, newState.TriggerRight);

            _xboxController.SubmitReport();

            _currentState = newState;
        }
    }
}
