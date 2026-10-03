using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Interop;
using System.Windows.Threading;

namespace vibeRacingOverlays.App.Overlay
{
    /// <summary>
    /// Buttons of wheels, button boxes and other game controllers, via Windows Raw Input (RIDEV_INPUTSINK: also while
    /// iRacing is in front). Runs on a thread of its own with a message-only window: a wheel sends hundreds of reports a
    /// second (axes), they never touch the UI thread; only a newly pressed button is passed on (<see cref="Pressed"/>,
    /// raised on that thread). Devices are named by USB vendor and product id, so a binding survives reboots and
    /// replugging.
    /// </summary>
    public sealed class ControllerInput : IDisposable
    {
        /// <summary>A button went down: device id ("VID_xxxx&amp;PID_yyyy"), device name, button number.</summary>
        public event Action<string, string, int> Pressed;

        Thread thread;
        Dispatcher dispatcher;
        HwndSource window;
        readonly Dictionary<IntPtr, Device> devices = new Dictionary<IntPtr, Device>();

        sealed class Device
        {
            public string Id, Name;
            public IntPtr Preparsed;   // HidP data (AllocHGlobal), Zero = not a HID device we can read
            public int MaxUsages;
            public HashSet<int> Down = new HashSet<int>();
        }

        public bool Running { get { return thread != null; } }

        public void Start()
        {
            if (thread != null) return;
            var ready = new ManualResetEventSlim();
            thread = new Thread(() =>
            {
                dispatcher = Dispatcher.CurrentDispatcher;
                // a message-only window (parent HWND_MESSAGE) receives the raw input
                window = new HwndSource(new HwndSourceParameters("vibeRacingOverlays controller input") { ParentWindow = new IntPtr(-3), WindowStyle = 0 });
                window.AddHook(Hook);
                var rid = new[]
                {
                    new RAWINPUTDEVICE { UsagePage = 1, Usage = 4, Flags = RIDEV_INPUTSINK, Target = window.Handle },   // joystick (wheels, button boxes)
                    new RAWINPUTDEVICE { UsagePage = 1, Usage = 5, Flags = RIDEV_INPUTSINK, Target = window.Handle },   // game pad
                    new RAWINPUTDEVICE { UsagePage = 1, Usage = 8, Flags = RIDEV_INPUTSINK, Target = window.Handle },   // multi-axis controller
                };
                RegisterRawInputDevices(rid, (uint)rid.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
                ready.Set();
                Dispatcher.Run();
            }) { IsBackground = true, Name = "Controller input" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            ready.Wait(3000);
        }

        public void Stop()
        {
            if (thread == null) return;
            var d = dispatcher;
            if (d != null)
                d.Invoke(() =>
                {
                    var rid = new[]
                    {
                        new RAWINPUTDEVICE { UsagePage = 1, Usage = 4, Flags = RIDEV_REMOVE },
                        new RAWINPUTDEVICE { UsagePage = 1, Usage = 5, Flags = RIDEV_REMOVE },
                        new RAWINPUTDEVICE { UsagePage = 1, Usage = 8, Flags = RIDEV_REMOVE },
                    };
                    RegisterRawInputDevices(rid, (uint)rid.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
                    window.Dispose();
                    foreach (var dev in devices.Values) if (dev.Preparsed != IntPtr.Zero) Marshal.FreeHGlobal(dev.Preparsed);
                    devices.Clear();
                    Dispatcher.CurrentDispatcher.InvokeShutdown();
                });
            thread.Join(2000);
            thread = null;
            dispatcher = null;
        }

        public void Dispose() { Stop(); }

        IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_INPUT)
            {
                try { Read(lParam); }
                catch (Exception) { }   // a device we can't read is simply ignored
            }
            return IntPtr.Zero;
        }

        void Read(IntPtr lParam)
        {
            uint size = 0, header = (uint)Marshal.SizeOf<RAWINPUTHEADER>();
            GetRawInputData(lParam, RID_INPUT, IntPtr.Zero, ref size, header);
            if (size == 0) return;
            IntPtr buf = Marshal.AllocHGlobal((int)size);
            try
            {
                if (GetRawInputData(lParam, RID_INPUT, buf, ref size, header) != size) return;
                var h = Marshal.PtrToStructure<RAWINPUTHEADER>(buf);
                if (h.Type != RIM_TYPEHID) return;
                var dev = DeviceOf(h.Device);
                if (dev == null || dev.Preparsed == IntPtr.Zero) return;
                int sizeHid = Marshal.ReadInt32(buf, (int)header), count = Marshal.ReadInt32(buf, (int)header + 4);
                IntPtr data = buf + (int)header + 8;
                var usages = new ushort[dev.MaxUsages];
                for (int i = 0; i < count; i++)
                {
                    uint len = (uint)usages.Length;
                    if (HidP_GetUsages(HidP_Input, 9 /* buttons */, 0, usages, ref len, dev.Preparsed, data + i * sizeHid, (uint)sizeHid) != HIDP_STATUS_SUCCESS) continue;
                    var now = new HashSet<int>();
                    for (int k = 0; k < len; k++) now.Add(usages[k]);
                    foreach (int b in now) if (!dev.Down.Contains(b) && Pressed != null) Pressed(dev.Id, dev.Name, b);
                    dev.Down = now;
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        Device DeviceOf(IntPtr handle)
        {
            Device dev;
            if (devices.TryGetValue(handle, out dev)) return dev;
            dev = new Device();
            devices[handle] = dev;

            uint n = 0;
            GetRawInputDeviceInfo(handle, RIDI_DEVICENAME, IntPtr.Zero, ref n);
            var nameBuf = Marshal.AllocHGlobal((int)(n + 1) * 2);
            string path;
            try { GetRawInputDeviceInfo(handle, RIDI_DEVICENAME, nameBuf, ref n); path = Marshal.PtrToStringUni(nameBuf) ?? ""; }
            finally { Marshal.FreeHGlobal(nameBuf); }
            var m = Regex.Match(path, @"VID_([0-9A-Fa-f]{4}).*?PID_([0-9A-Fa-f]{4})");
            dev.Id = m.Success ? "VID_" + m.Groups[1].Value.ToUpperInvariant() + "&PID_" + m.Groups[2].Value.ToUpperInvariant() : path;
            dev.Name = ProductName(path) ?? dev.Id;

            uint p = 0;
            GetRawInputDeviceInfo(handle, RIDI_PREPARSEDDATA, IntPtr.Zero, ref p);
            if (p == 0) return dev;
            dev.Preparsed = Marshal.AllocHGlobal((int)p);
            if (GetRawInputDeviceInfo(handle, RIDI_PREPARSEDDATA, dev.Preparsed, ref p) == unchecked((uint)-1))
            {
                Marshal.FreeHGlobal(dev.Preparsed);
                dev.Preparsed = IntPtr.Zero;
                return dev;
            }
            dev.MaxUsages = (int)Math.Max(1, HidP_MaxUsageListLength(HidP_Input, 9, dev.Preparsed));
            return dev;
        }

        /// <summary>The device's own name ("Fanatec Podium...") from its HID product string; null when it can't be read.</summary>
        static string ProductName(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            IntPtr file = CreateFile(path, 0, 3 /* share read | write */, IntPtr.Zero, 3 /* open existing */, 0, IntPtr.Zero);
            if (file == IntPtr.Zero || file == new IntPtr(-1)) return null;
            try
            {
                var sb = new StringBuilder(128);
                return HidD_GetProductString(file, sb, (uint)(sb.Capacity * 2)) ? sb.ToString().Trim() : null;
            }
            finally { CloseHandle(file); }
        }

        // ---------------------------------------------------------------- Win32

        const int WM_INPUT = 0x00FF;
        const uint RIDEV_INPUTSINK = 0x00000100, RIDEV_REMOVE = 0x00000001, RID_INPUT = 0x10000003, RIM_TYPEHID = 2;
        const uint RIDI_DEVICENAME = 0x20000007, RIDI_PREPARSEDDATA = 0x20000005;
        const int HidP_Input = 0, HIDP_STATUS_SUCCESS = 0x00110000;

        [StructLayout(LayoutKind.Sequential)]
        struct RAWINPUTDEVICE { public ushort UsagePage, Usage; public uint Flags; public IntPtr Target; }

        [StructLayout(LayoutKind.Sequential)]
        struct RAWINPUTHEADER { public uint Type, Size; public IntPtr Device, WParam; }

        [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint count, uint size);
        [DllImport("user32.dll")] static extern uint GetRawInputData(IntPtr rawInput, uint command, IntPtr data, ref uint size, uint headerSize);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, IntPtr data, ref uint size);
        [DllImport("hid.dll")] static extern int HidP_GetUsages(int reportType, ushort usagePage, ushort linkCollection, [Out] ushort[] usages, ref uint usageLength, IntPtr preparsed, IntPtr report, uint reportLength);
        [DllImport("hid.dll")] static extern uint HidP_MaxUsageListLength(int reportType, ushort usagePage, IntPtr preparsed);
        [DllImport("hid.dll", CharSet = CharSet.Unicode)] static extern bool HidD_GetProductString(IntPtr device, StringBuilder buffer, uint bufferLength);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    }
}
