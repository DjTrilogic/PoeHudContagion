// ReSharper disable IdentifierTypo

using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Contagion.Utilities
{
    public static class Keyboard
    {
        [DllImport("USER32.dll", CharSet = CharSet.None, ExactSpelling = false)]
        private static extern short GetKeyState(int nVirtKey);

        public static bool IsKeyDown(int nVirtKey)
        {
            return GetKeyState(nVirtKey) < 0;
        }

        [DllImport("user32.dll", CharSet = CharSet.None, ExactSpelling = false)]
        private static extern uint keybd_event(byte bVk, byte bScan, int dwFlags, int dwExtraInfo);

        public static void KeyDown(Keys key)
        {
            keybd_event((byte)key, 0, 1, 0);
        }

        public static void KeyPress(Keys key)
        {
            KeyDown(key);
            Thread.Sleep(1);
            KeyUp(key);
        }

        public static void KeyUp(Keys key)
        {
            keybd_event((byte)key, 0, 3, 0);
        }
    }
}