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

        /// <summary>The website address can force it (?controls=touch or ?controls=pc): "touch", "pc" or "".</summary>
        public static Func<string> Forced;

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
                // the address wins over the saved setting (a way back if the setting is ever wrong for the device)
                string forced = "";
                try { forced = (Forced?.Invoke() ?? "").ToLowerInvariant(); } catch { }
                if (forced == "touch") return true;
                if (forced == "pc" || forced == "mouse") return false;
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
            var detected = DetectedTouch;
            if (FSOEnvironment.SoftwareKeyboard != want || !Logged)
                Console.WriteLine("[SimsRedux] controls: " + (want ? "touch" : "PC") + " (setting " + Choices[Setting] + ", device looks like " + (detected ? "a phone/tablet" : "a computer") + ")");
            Logged = true;
            if (FSOEnvironment.SoftwareKeyboard != want || !Announced) PendingNotice = true;
            FSOEnvironment.SoftwareKeyboard = want;
        }

        private static bool Logged, Announced, PendingNotice;

        /// <summary>
        /// Called by the game screen each frame: once the screen is up, a corner notice says which
        /// controls are on (the first time, and after they change).
        /// </summary>
        public static void Announce()
        {
            if (!Managed || !PendingNotice) return;
            PendingNotice = false;
            if (!Announced && !Active && Setting == Auto) { Announced = true; return; } // a computer on Auto: nothing to say
            Announced = true;
            try
            {
                var how = Setting == Auto ? " (picked for this device)" : "";
                if (Active)
                    Simitone.Client.UI.Controls.UIReduxToasts.Show("Touch Controls",
                        "Touch controls are on" + how + ". Tap to choose, drag to move the camera, pinch to zoom. "
                        + "Options has a PC Controls button for a mouse and keyboard.");
                else
                    Simitone.Client.UI.Controls.UIReduxToasts.Show("PC Controls",
                        "PC controls are on" + how + ". Options > Interface > Controls switches back to touch.");
            }
            catch { }
        }

        /// <summary>Names for the setting's choices, in order.</summary>
        public static readonly string[] Choices = { "Auto", "Touch", "PC" };
    }
}
