﻿/// Sharp 80 (c) Matthew Hamilton
/// Licensed Under GPL v3. See license.txt for details.

using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

using Sharp80.DirectX;
using Sharp80.TRS80;
using Sharp80.Views;

namespace Sharp80
{
    public partial class MainForm : Form, IAppWindow
    {
        private static Thread uiThread;
        private static MainForm instance;

        private ProductInfo productInfo;
        private Settings settings;
        private IDialogs dialogs;
        private Computer computer;
        private ScreenDX screen;
        private KeyboardDX keyboard;
        private Task screenTask;
        private Task keyboardPollTask;
        private CancellationTokenSource stopToken;
        private bool isDisposing = false;
        private Rectangle previousClientRect = Rectangle.Empty;

        private const uint SCREEN_REFRESH_RATE_HZ = 30;
        private const uint KEYBOARD_REFRESH_RATE_HZ = 65;
        private const uint DISPLAY_MESSAGE_CYCLE_DURATION = SCREEN_REFRESH_RATE_HZ;  // 1 second

        private static bool suppressCursor = false;
        private static int dialogLevel = 0;
        private static bool cursorHidden = false;

        private bool isActive;
        private Rectangle clientExcess = Rectangle.Empty;

        public static bool IsUiThread => Thread.CurrentThread == uiThread;
        public static MainForm Instance => instance;
        public bool DrawOK => WindowState != FormWindowState.Minimized;
        
        public MainForm()
        {
            instance = this;
            settings = new Settings();
            productInfo = new ProductInfo();
            dialogs = new WinDialogs(this, productInfo, BeforeDialog, AfterDialog);

            uiThread = Thread.CurrentThread;

            KeyPreview = true;
            Text = productInfo.ProductName + " - TRS-80 Model III Emulator";
            BackColor = Color.Black;

            keyboard = new KeyboardDX();

            screen = new ScreenDX(this,
                                  dialogs,
                                  settings,
                                  DISPLAY_MESSAGE_CYCLE_DURATION);

            InitializeComponent();
            SetupClientArea();
        }

        private void Form_Load(object sender, EventArgs e)
        {
            try
            {
                Activated += (s, ee) => { SyncKeyboard(); isActive = true; };
                Deactivate += (s, ee) => { isActive = false; computer.ResetKeyboard(false, false); };

                Views.View.OnUserCommand += ProcessUserCommand;

                HardReset();

                if (settings.FullScreen)
                    ToggleFullScreen();

                if (settings.AutoStartOnReset)
                    AutoStart();

                UpdateDialogLevel();

                stopToken = new CancellationTokenSource();

                screenTask = screen.Start(SCREEN_REFRESH_RATE_HZ, stopToken.Token);
                keyboardPollTask = keyboard.Start(KEYBOARD_REFRESH_RATE_HZ, ProcessKey, stopToken.Token);
            }
            catch (Exception Ex)
            {
                dialogs.ExceptionAlert(Ex);
            }
        }
        
        protected override void OnPaintBackground(PaintEventArgs e) { /* do nothing prevent flicker */ }
        
        public void AdvancedViewChange()
        {
            if (!screen.IsFullScreen)
                Bounds = ConstrainToScreen(Location.X, Location.Y, ClientSize.Height);
        }

        // STARTUP

        private void HardReset()
        {
            try
            {
                if (computer != null)
                {
                    if (!computer.SaveChangedStorage())
                        return;
                    Task.Run(computer.Shutdown);
                }
                var sound = new SoundX(16000);
                if (sound.Stopped)
                    dialogs.AlertUser("Sound failed to start. Continuing without sound.");

                computer = new Computer(screen, sound, new Timer(), settings, dialogs);

                screen.Initialize(computer);

                Views.View.Initialize(productInfo, dialogs, settings, computer, (msg) => screen.StatusMessage = msg);

                if (settings.AutoStartOnReset)
                {
                    computer.Start();
                    Views.View.CurrentMode = ViewMode.Normal;
                }
            }
            catch (Exception Ex)
            {
                dialogs.ExceptionAlert(Ex);
            }
        }
        private void AutoStart()
        {
            var startTimer = new System.Windows.Forms.Timer() { Interval = 500 };
            startTimer.Tick += (s, ee) =>
            {
                startTimer.Stop();
                startTimer.Dispose();
                startTimer = null;
                if (computer.Ready)
                    computer.Start();
                else if (!Disposing)
                    AutoStart();
            };
            startTimer.Start();
        }

        // KEYBOARD HANDLING

        protected override void OnKeyDown(KeyEventArgs e)
        {
            // Prevent stupid ding noise
            if (e.KeyCode < Keys.F1 || e.KeyCode > Keys.F19 || (!e.Alt && !e.Control))
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                base.OnKeyDown(e);
            }
        }
        private void ProcessUserCommand(UserCommand Command)
        {
            switch (Command)
            {
                case UserCommand.ToggleFullScreen:
                    ToggleFullScreen();
                    break;
                case UserCommand.Window:
                    if (screen.IsFullScreen)
                        ToggleFullScreen();
                    break;
                case UserCommand.ZoomIn:
                    Zoom(true);
                    break;
                case UserCommand.ZoomOut:
                    Zoom(false);
                    break;
                case UserCommand.HardReset:
                    HardReset();
                    break;
                case UserCommand.Exit:
                    Close();
                    break;
            }
        }
        private void ProcessKey(KeyState Key)
        {
            if (isActive)
            {
                try
                {
                    if (Key.Key == KeyCode.Capital && Key.Released)
                        TurnCapsLockOff();
                    Views.View.ProcessKey(Key);
                }
                catch (Exception Ex)
                {
                    dialogs.ExceptionAlert(Ex);
                }
            }
        }
        private void SyncKeyboard()
        {
            keyboard.Refresh();
            computer?.ResetKeyboard(keyboard.RightShiftPressed, keyboard.LeftShiftPressed);
        }

        // CURSOR & DIALOG MANAGEMENT

        private bool SuppressCursor
        {
            get { return suppressCursor; }
            set
            {
                suppressCursor = value;
                UpdateDialogLevel();
            }
        }
        private void BeforeDialog()
        {
            // Force show cursor
            dialogLevel++;
            UpdateDialogLevel();
        }
        private void AfterDialog()
        {
            // No force show cursor
            dialogLevel--;
            System.Diagnostics.Debug.Assert(dialogLevel >= 0);
            UpdateDialogLevel();
        }
        private void UpdateDialogLevel()
        {
            if (dialogLevel < 0)
                throw new Exception("Dialog level less than zero.");

            if (suppressCursor && dialogLevel == 0)
            {
                if (!cursorHidden)
                {
                    Cursor.Hide();
                    cursorHidden = true;
                }
            }
            else if (cursorHidden)
            {
                Cursor.Show();
                cursorHidden = false;
            }
            keyboard.Enabled = dialogLevel == 0;
        }

        // WINDOW SIZING

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == MessageEventArgs.WM_SIZING)
                ConstrainAspectRatio(m);
            base.WndProc(ref m);
        }
        private void SetupClientArea()
        {
            // Store some invariant values for future use
            var pts = PointToScreen(new Point(0, 0));
            clientExcess = new Rectangle(pts.X - Left,
                                         pts.Y - Top,
                                         Size.Width - ClientSize.Width,
                                         Size.Height - ClientSize.Height);

            // set screen startup position

            Rectangle workingArea = Screen.FromHandle(Handle).WorkingArea;

            int w = settings.WindowWidth;
            int h = settings.WindowHeight;

            // if not contained within screen working area, punt
            if (w <= 0 || h <= 0 || w + clientExcess.Width > workingArea.Width || h + clientExcess.Height > workingArea.Height)
            {
                w = (int)(screen.AdvancedView ? ScreenMetrics.WINDOWED_WIDTH_ADVANCED : ScreenMetrics.WINDOWED_WIDTH_NORMAL);
                h = (int)ScreenMetrics.WINDOWED_HEIGHT;
            }
            SetClientRect(new Rectangle(settings.WindowX, settings.WindowY, w, h));
        }
        private void ToggleFullScreen()
        {
            if (screen.IsFullScreen)
                SetWindowed();
            else
                SetFullScreen();
        }
        private void SetWindowed()
        {
            // make sure it really becomes windowed and doesn't snap back to full screen.
            var wa = Screen.GetWorkingArea(this);
            if (previousClientRect.Height > wa.Height * 3 / 4)
                previousClientRect = new Rectangle(previousClientRect.X, previousClientRect.Y, 0, wa.Height * 3 / 4);

            settings.FullScreen = screen.IsFullScreen = false;
            SetWindowStyle(false);
            SetClientRect();
            SuppressCursor = false;
        }
        private void SetFullScreen()
        {
            previousClientRect = new Rectangle(PointToScreen(Point.Empty), ClientSize);

            settings.FullScreen = screen.IsFullScreen = true;
            SetWindowStyle(true);
            Bounds = Screen.GetBounds(this);
            SuppressCursor = true;
        }
        private void SetWindowStyle(bool FullScreen)
        {
            if (FullScreen)
            {
                FormBorderStyle = FormBorderStyle.None;
                WindowState = FormWindowState.Maximized;
            }
            else
            {
                FormBorderStyle = FormBorderStyle.Sizable;
                WindowState = FormWindowState.Normal;
            }
        }
        private void Zoom(bool In)
        {
            if (WindowState == FormWindowState.Minimized)
                return;
            if (screen.IsFullScreen)
            {
                if (!In)
                    SetWindowed();
                return;
            }
            float aspectRatio = (screen.AdvancedView ? ScreenMetrics.WINDOWED_WIDTH_ADVANCED : ScreenMetrics.WINDOWED_WIDTH_NORMAL) / ScreenMetrics.WINDOWED_HEIGHT;
            float zoom = In ? 1.2f : 1f / 1.2f;
            float newH = zoom * ClientSize.Height;
            newH = Math.Max(newH, ScreenMetrics.WINDOWED_HEIGHT / 2);
            float newW = newH * aspectRatio;

            Rectangle r = new Rectangle((int)(Location.X + clientExcess.Left - (newW - ClientSize.Width) / 2),
                                        (int)(Location.Y + clientExcess.Top - (newH - ClientSize.Height) / 2),
                                        (int)newW,
                                        (int)newH);

            SetClientRect(r);
        }
        private void SetClientRect() => SetClientRect(previousClientRect);
        
        /// <summary>
        /// Width is always recomupted
        /// </summary>
        private void SetClientRect(Rectangle Input)
        {
            Rectangle bounds = Input;

            int h;
            if (Input.Height < ScreenMetrics.WINDOWED_HEIGHT / 2)
                h = (int)ScreenMetrics.WINDOWED_HEIGHT;
            else
                h = Input.Height;
            int w = (int)(h * (screen.AdvancedView ? ScreenMetrics.WINDOWED_ASPECT_RATIO_ADVANCED : ScreenMetrics.WINDOWED_ASPECT_RATIO_NORMAL));

            Input = new Rectangle(Input.X,
                                  Input.Y,
                                  w,
                                  h);

            bounds = new Rectangle(Input.X - clientExcess.Left,
                                   Input.Y - clientExcess.Top,
                                   Input.Width + clientExcess.Width,
                                   Input.Height + clientExcess.Height);

            Rectangle workingArea;
            if (!(workingArea = Screen.GetWorkingArea(this)).Contains(bounds))
            {
                if (bounds.Width > workingArea.Width || bounds.Height > workingArea.Height)
                {
                    SetFullScreen();
                    return;
                }
                if (bounds.Left < workingArea.Left)
                    bounds.Offset(workingArea.Left - bounds.Left, 0);
                if (bounds.Right > workingArea.Right)
                    bounds.Offset(workingArea.Right - bounds.Right, 0);
                if (bounds.Top < workingArea.Top)
                    bounds.Offset(0, workingArea.Top - bounds.Top);
                if (bounds.Bottom > workingArea.Bottom)
                    bounds.Offset(0, workingArea.Bottom - bounds.Bottom);
            }
            Bounds = bounds;
        }
        /// <summary>
        /// Returns proposed screen bounds
        /// </summary>
        private Rectangle ConstrainToScreen(int X, int Y, int ClientHeight)
        {
            var scn = Screen.FromHandle(Handle);

            // screen area adjusted for difference between size and client size
            int screenWidth = scn.WorkingArea.Width;
            int screenHeight = scn.WorkingArea.Height;
            int adjScreenWidth = screenWidth - clientExcess.Width;
            int adjScreenHeight = screenHeight - clientExcess.Height;

            float h = ClientHeight;
            float aspectRatio = screen.AdvancedView ? ScreenMetrics.WINDOWED_ASPECT_RATIO_ADVANCED : ScreenMetrics.WINDOWED_ASPECT_RATIO_NORMAL;
            float w = h * aspectRatio;

            if (w > adjScreenWidth)
            {
                w = adjScreenWidth;
                h = w / aspectRatio;
            }
            if (h > adjScreenHeight)
            {
                h = adjScreenHeight;
                w = h * aspectRatio;
            }

            // adjust all for client borders
            float x = X;
            float y = Y;
            w += clientExcess.Width;
            h += clientExcess.Height;

            // constrain to be on screen
            x = Math.Min(Math.Max(x, 0), screenWidth - w);
            y = Math.Min(Math.Max(y, 0), screenWidth - h);

            return new Rectangle((int)x,
                                 (int)y,
                                 (int)w,
                                 (int)h);
        }
        private void ConstrainAspectRatio(Message Msg)
        {
            float ratio = screen.AdvancedView ? ScreenMetrics.WINDOWED_ASPECT_RATIO_ADVANCED : ScreenMetrics.WINDOWED_ASPECT_RATIO_NORMAL;

            if (Msg.Msg == MessageEventArgs.WM_SIZING)
            {
                var rc = (MessageEventArgs.RECT)Marshal.PtrToStructure(Msg.LParam, typeof(MessageEventArgs.RECT));
                int res = Msg.WParam.ToInt32();
                if (res == MessageEventArgs.WMSZ_LEFT || res == MessageEventArgs.WMSZ_RIGHT)
                {
                    // Left or right resize - adjust height (bottom)
                    rc.Bottom = rc.Top + (int)(ClientSize.Width / ratio);
                }
                else if (res == MessageEventArgs.WMSZ_TOP || res == MessageEventArgs.WMSZ_BOTTOM)
                {
                    // Up or down resize - adjust width (right)
                    rc.Right = rc.Left + (int)(ClientSize.Height * ratio);
                }
                else if (res == MessageEventArgs.WMSZ_RIGHT + MessageEventArgs.WMSZ_BOTTOM)
                {
                    // Lower-right corner resize -> adjust height
                    rc.Bottom = rc.Top + (int)(ClientSize.Width / ratio);
                }
                else if (res == MessageEventArgs.WMSZ_LEFT + MessageEventArgs.WMSZ_TOP)
                {
                    // Upper-left corner -> adjust width (left)
                    rc.Left = rc.Right - (int)(ClientSize.Height * ratio);
                }
                Marshal.StructureToPtr(rc, Msg.LParam, true);
            }
        }

        // SHUTDOWN

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (computer.SaveChangedStorage())
            {
                settings.WindowX = Location.X;
                settings.WindowY = Location.Y;
                settings.WindowWidth = ClientSize.Width;
                settings.WindowHeight = ClientSize.Height;
                settings.Save();
                Task.Run(Stop);
            }
            else
            {
                e.Cancel = true;
            }
            base.OnFormClosing(e);
        }
        private async Task Stop()
        {
            try
            {
                stopToken.Cancel();
                await keyboardPollTask;
                await computer.Shutdown();
                await screenTask;
                screen.Dispose();
                keyboard.Dispose();
                Dispose();
                stopToken.Dispose();
                Application.Exit();
            }
            catch (AggregateException e)
            {
                foreach (var ee in e.InnerExceptions)
                    if (!(ee is TaskCanceledException))
                    {
                        // should do something
                    }
                Application.Exit();
            }
        }

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (!isDisposing && !IsDisposed)
            {
                try
                {
                    isDisposing = true;

                    if (disposing)
                        components?.Dispose();

                    base.Dispose(disposing);

                    isDisposing = false;
                }
                catch (Exception)
                {
                    // Too late to do anything about it
                }
            }
        }
 
        // MISC

        private void TurnCapsLockOff()
        {
            const int KEYEVENTF_EXTENDEDKEY = 0x01;
            const int KEYEVENTF_KEYUP = 0x02;

            // Annoying that it turns on when doing virtual shift-zero.
            if (IsKeyLocked(Keys.CapsLock))
            {
                NativeMethods.keybd_event(0x14, 0x45, KEYEVENTF_EXTENDEDKEY, (UIntPtr)0);
                NativeMethods.keybd_event(0x14, 0x45, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP,
                    (UIntPtr)0);
            }
        }
    }
}