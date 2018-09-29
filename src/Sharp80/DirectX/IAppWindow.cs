﻿/// Sharp 80 (c) Matthew Hamilton
/// Licensed Under GPL v3. See license.txt for details.

using System;

namespace Sharp80.DirectX
{
    public interface IAppWindow
    {
        event EventHandler ResizeBegin;
        event EventHandler ResizeEnd;

        bool DrawOK { get; }
        IntPtr Handle { get; }
        System.Drawing.Size ClientSize { get; }

        void AdvancedViewChange();
    }
}
