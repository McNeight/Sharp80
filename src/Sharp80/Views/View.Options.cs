﻿/// Sharp 80 (c) Matthew Hamilton
/// Licensed Under GPL v3. See license.txt for details.

using System;
using System.Text;

namespace Sharp80.Views
{
    internal class ViewOptions : View
    {
        protected override ViewMode Mode => ViewMode.Options;
        protected override bool ForceRedraw => false;
        protected override bool CanSendKeysToEmulation => false;

        protected override byte[] GetViewBytes()
        {
            return PadScreen(Encoding.ASCII.GetBytes(
                Header("Sharp 80 Options") +
                Indent(string.Format("[F12] Speed                        [{0}]", ClockSpeedToString(Computer.ClockSpeed))) +
                Format() +
                Indent(string.Format("[Alt]+[S] Sound                    {0}",
                    Computer.SoundOn ? "[ON] /  OFF" : " ON  / [OFF]")) +
                (Computer.SoundOn
                    ? Indent(string.Format("[Alt]+[Shift]+[S] Drive Noise      {0}",
                        Computer.DriveNoise ? "[ON] /  OFF" : " ON  / [OFF]"))
                    : Format()) +
                Format() +
                Indent(string.Format("[Alt]+[G] Screen Color             {0}",
                    Settings.GreenScreen ? " WHITE  / [GREEN]" : "[WHITE] /  GREEN")) +
                Format() +
                Indent(string.Format("[Alt]+[A] Auto Start on Reset      {0}",
                    Settings.AutoStartOnReset ? "[ON] /  OFF" : " ON  / [OFF]")) +
                Format() +
                Indent(string.Format("[F5] Z80 Internals Display         {0}",
                    Settings.AdvancedView ? "[ON] /  OFF" : " ON  / [OFF]")) +
                (Settings.AdvancedView
                    ? Indent(string.Format("[Alt]+[H] Disassembly Mode         {0}",
                        !Computer.HistoricDisassemblyMode ? "[NORMAL] /  HISTORIC" : " NORMAL  / [HISTORIC]"))
                    : Format()) +
                Format() +
                Indent(string.Format("[Alt]+[Enter] Full-Screen View     {0}",
                    Settings.FullScreen ? "[ON] /  OFF " : " ON  / [OFF]")) +
                Format()));
        }        
    }
}
