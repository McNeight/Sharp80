﻿/// Sharp 80 (c) Matthew Hamilton
/// Licensed Under GPL v3. See license.txt for details.

using System;
using System.Linq;
using System.Text;
using Sharp80.TRS80;

namespace Sharp80.Views
{
    internal class ViewPrinter : View
    {
        private const int NUM_DISPLAY_LINES = 10;

        protected override ViewMode Mode => ViewMode.Printer;
        protected override bool CanSendKeysToEmulation => false;
        protected override bool ForceRedraw => FrameReqNum % 30 == 0;

        private string[] Lines = new string[0];
        private int curLine = 0;

        protected override void Activate()
        {
            RefreshPrinterContent();
            base.Activate();
        }
        protected override bool processKey(KeyState Key)
        {
            if (Key.Pressed && Key.IsUnmodified)
            {
                if (Computer.PrinterHasContent)
                {
                    switch (Key.Key)
                    {
                        case KeyCode.S:
                            ShowPrinterOutput();
                            break;
                        case KeyCode.D:
                            Computer.PrinterReset();
                            curLine = 0;
                            break;
                        case KeyCode.Up:
                            if (CanScrollUp)
                                curLine--;
                            break;
                        case KeyCode.Down:
                            if (CanScrollDown)
                                curLine++;
                            break;
                    }
                }
            }
            Invalidate();
            return base.processKey(Key);
        }
        protected override byte[] GetViewBytes()
        {
            string printContent;
            string options;
            if (Computer.PrinterHasContent)
            {
                printContent = ViewPrinterOutput();
                options = Format("[S] Save to Text File") +
                          Format("[D] Delete Print Job");

                if (CanScroll)
                    options += Format("[Up Arrow] / [Down Arrow] Scroll Output");

            }
            else
            {
                printContent = Format() +
                               Format() +
                               Indent("No printer output.") +
                               Format().Repeat(NUM_DISPLAY_LINES - 3);
                options = String.Empty;
            }
            return PadScreen(Encoding.ASCII.GetBytes(
                Header("Sharp 80 Z80 Printer Status") +
                printContent +
                Separator() +
                options));

        }

        private bool CanScroll => Lines.Count() > NUM_DISPLAY_LINES;
        private bool CanScrollUp => CanScroll && curLine > 0;
        private bool CanScrollDown => CanScroll && curLine < Lines.Count() - NUM_DISPLAY_LINES - 1;

        private string[] GetPrinterOutput()
        {
            return Computer.PrinterContent.Split(new string[] { "\r\n" }, StringSplitOptions.None).Select(l => Format(l.Truncate(ScreenMetrics.NUM_SCREEN_CHARS_X))).ToArray();
        }
        private string ViewPrinterOutput()
        {
            return String.Join(String.Empty,
                               Lines.Skip(curLine).Take(NUM_DISPLAY_LINES).Select(l => Format(l))) +
                               Format().Repeat(NUM_DISPLAY_LINES - Lines.Count());
        }
        private void RefreshPrinterContent()
        {
            Lines = GetPrinterOutput();
            curLine = Math.Min(curLine, Lines.Count() - NUM_DISPLAY_LINES + 1);
        }
        private bool ShowPrinterOutput()
        {
            if (Computer.PrinterHasContent)
            {
                System.IO.Directory.CreateDirectory(Storage.DefaultPrintDir);
                string filePath = System.IO.Path.Combine(Storage.DefaultPrintDir, "Printer.txt").MakeUniquePath();
                System.IO.File.WriteAllText(filePath, Computer.PrinterContent);
                Dialogs.ShowTextFile(filePath);
                return true;
            }
            else
            {
                Dialogs.AlertUser("Nothing printed yet.");
                return false;
            }
        }
    }
}
