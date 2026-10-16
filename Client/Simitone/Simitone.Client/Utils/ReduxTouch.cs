using FSO.Common;
using System;

namespace Simitone.Client.Utils
{
    /// <summary>
    /// SimsRedux: touch or PC controls. On the website the game looks at the device it's played on:
    /// a phone or tablet (a touch screen and no mouse) gets the touch layout and controls, a computer
    /// gets the PC layout. The Controls setting can force either one (a tablet with a mouse and
    /// keyboard attached, say).
    ///
    /// The switch itself is FSOEnvironment.SoftwareKeyboard, the same one the phone apps use. It's
    /// only changed when a new screen opens (a lot, the neighbourhood, Create-A-Family), because
    /// each screen builds its layout for one or the other.
    /// </summary>
    public static class ReduxTouch
    {
        public const int Auto = 0, Touch = 1, Mouse = 2;

        /// <summary>True where the player can choose (the website). Elsewhere the platform decides on its own.</summary>
        public static bool Managed;

        /// <summary>What the website found: a touch screen with no mouse (a phone or tablet).</summary>
        public static Func<bool> DeviceIsTouch;

        /// <summary>The Controls setting: 0 = Auto (the default), 1 = Touch, 2 = PC (mouse and keyboard).</summary>
        public static int Setting
        {
            get { return Math.Max(0, Math.Min(2, ReduxSettings.GetInt("controls", Auto))); }
            set { ReduxSettings.Set("controls", Math.Max(0, Math.Min(2, value))); }
        }

        public static bool DetectedTouch
        {
            get
            {
                try { return DeviceIsTouch != null && DeviceIsTouch(); }
                catch { return false; }
            }
        }

        /// <summary>Whether touch controls are wanted right now (from the setting, or the device when on Auto).</summary>
        public static bool Wanted
        {
            get
            {
                var s = Setting;
                if (s == Touch) return true;
                if (s == Mouse) return false;
                return DetectedTouch;
            }
        }

        /// <summary>Whether the screen being shown now uses touch controls.</summary>
        public static bool Active { get { return FSOEnvironment.SoftwareKeyboard; } }

        /// <summary>Puts the wanted controls in place. Called just before a new screen is built.</summary>
        public static void Apply()
        {
            if (!Managed) return;
            var want = Wanted;
            if (FSOEnvironment.SoftwareKeyboard != want)
                Console.WriteLine("[SimsRedux] controls: " + (want ? "touch" : "PC"));
            FSOEnvironment.SoftwareKeyboard = want;
        }

        /// <summary>Names for the setting's choices, in order.</summary>
        public static readonly string[] Choices = { "Auto", "Touch", "PC" };
    }
}
