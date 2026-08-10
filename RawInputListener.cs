using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace LurchTracer;

internal sealed class RawInputListener
{
    private const int WmInput = 0x00FF;
    private const uint RidInput = 0x10000003;
    private const uint MouseType = 0;
    private const uint KeyboardType = 1;
    private const uint InputSink = 0x00000100;
    private const ushort KeyBreak = 0x0001;
    private const ushort MouseWheel = 0x0400;

    private readonly HashSet<ushort> heldKeys = new();
    private readonly Action<string> sendJson;
    private readonly double millisecondsPerTick = 1000.0 / Stopwatch.Frequency;

    public RawInputListener(Action<string> sendJson)
    {
        this.sendJson = sendJson;
    }

    public void Register(IntPtr windowHandle)
    {
        RawInputDevice[] devices =
        [
            new() { UsagePage = 0x01, Usage = 0x06, Flags = InputSink, Target = windowHandle },
            new() { UsagePage = 0x01, Usage = 0x02, Flags = InputSink, Target = windowHandle }
        ];

        if (!RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RawInputDevice>()))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "RegisterRawInputDevices failed.");
    }

    public void ProcessMessage(Message message)
    {
        if (message.Msg != WmInput)
            return;

        uint size = 0;
        uint headerSize = (uint)Marshal.SizeOf<RawInputHeader>();
        if (GetRawInputData(message.LParam, RidInput, IntPtr.Zero, ref size, headerSize) == uint.MaxValue || size == 0)
            return;

        IntPtr buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputData(message.LParam, RidInput, buffer, ref size, headerSize) == uint.MaxValue)
                return;

            RawInputHeader header = Marshal.PtrToStructure<RawInputHeader>(buffer);
            IntPtr data = IntPtr.Add(buffer, Marshal.SizeOf<RawInputHeader>());
            double time = Stopwatch.GetTimestamp() * millisecondsPerTick;

            if (header.Type == KeyboardType)
                HandleKeyboard(Marshal.PtrToStructure<RawKeyboard>(data), time);
            else if (header.Type == MouseType)
                HandleMouse(Marshal.PtrToStructure<RawMouse>(data), time);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private void HandleKeyboard(RawKeyboard keyboard, double time)
    {
        string? key = keyboard.VirtualKey switch
        {
            0x57 => "w",
            0x41 => "a",
            0x53 => "s",
            0x44 => "d",
            0x20 => " ",
            _ => null
        };

        if (key is null)
            return;

        bool released = (keyboard.Flags & KeyBreak) != 0;
        if (released) {
            if (!heldKeys.Remove(keyboard.VirtualKey))
                return;
        } else {
            if (!heldKeys.Add(keyboard.VirtualKey))
                return;
        }

        sendJson(JsonSerializer.Serialize(new { type = "key", key, down = !released, time }));
    }

    private void HandleMouse(RawMouse mouse, double time)
    {
        if ((mouse.ButtonFlags & MouseWheel) == 0)
            return;

        short wheelDelta = unchecked((short)mouse.ButtonData);
        if (wheelDelta == 0)
            return;

        sendJson(JsonSerializer.Serialize(new { type = "wheel", delta = wheelDelta, time }));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public IntPtr Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputHeader
    {
        public uint Type;
        public uint Size;
        public IntPtr Device;
        public IntPtr WParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawKeyboard
    {
        public ushort MakeCode;
        public ushort Flags;
        public ushort Reserved;
        public ushort VirtualKey;
        public uint Message;
        public uint ExtraInformation;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct RawMouse
    {
        [FieldOffset(0)] public ushort Flags;
        [FieldOffset(4)] public uint Buttons;
        [FieldOffset(4)] public ushort ButtonFlags;
        [FieldOffset(6)] public ushort ButtonData;
        [FieldOffset(8)] public uint RawButtons;
        [FieldOffset(12)] public int LastX;
        [FieldOffset(16)] public int LastY;
        [FieldOffset(20)] public uint ExtraInformation;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterRawInputDevices(
        [In] RawInputDevice[] devices,
        uint deviceCount,
        uint deviceSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(
        IntPtr rawInput,
        uint command,
        IntPtr data,
        ref uint size,
        uint headerSize);
}
