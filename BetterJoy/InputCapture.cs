﻿using System;
using System.Threading;
using WindowsInput.Events.Sources;

namespace BetterJoy;

public class InputCapture : IDisposable
{
    private static readonly Lazy<InputCapture> _instance = new(() => new InputCapture());
    public static InputCapture Global => _instance.Value;

    private readonly IKeyboardEventSource Keyboard;
    private readonly IMouseEventSource Mouse;

    private int _nbKeyboardEvents = 0;
    private int _nbMouseEvents = 0;

    private bool _disposed = false;

    private InputCapture()
    {
        Keyboard = WindowsInput.Capture.Global.KeyboardAsync(false);
        Mouse = WindowsInput.Capture.Global.MouseAsync(false);
    }

    public void RegisterEvent(EventHandler<EventSourceEventArgs<KeyboardEvent>> ev)
    {
        Keyboard.KeyEvent += ev;
        KeyboardEventCountChange(true);
    }

    public void UnregisterEvent(EventHandler<EventSourceEventArgs<KeyboardEvent>> ev)
    {
        Keyboard.KeyEvent -= ev;
        KeyboardEventCountChange(false);
    }

    public void RegisterEvent(EventHandler<EventSourceEventArgs<MouseEvent>> ev)
    {
        Mouse.MouseEvent += ev;
        MouseEventCountChange(true);
    }

    public void UnregisterEvent(EventHandler<EventSourceEventArgs<MouseEvent>> ev)
    {
        Mouse.MouseEvent -= ev;
        MouseEventCountChange(false);
    }

    private void KeyboardEventCountChange(bool newEvent)
    {
        int count = newEvent ? Interlocked.Increment(ref _nbKeyboardEvents) : Interlocked.Decrement(ref _nbKeyboardEvents);
        
        // The property calls invoke, so only do it if necessary
        if (count == 0)
        {
            Keyboard.Enabled = false;
        }
        else if (count == 1)
        {
            Keyboard.Enabled = true;
        }
    }

    private void MouseEventCountChange(bool newEvent)
    {
        int count = newEvent ? Interlocked.Increment(ref _nbMouseEvents) : Interlocked.Decrement(ref _nbMouseEvents);
        
        // The property calls invoke, so only do it if necessary
        if (count == 0)
        {
            Mouse.Enabled = false;
        }
        else if (count == 1)
        {
            Mouse.Enabled = true;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Keyboard.Dispose();
        Mouse.Dispose();
        _disposed = true;
    }
}
