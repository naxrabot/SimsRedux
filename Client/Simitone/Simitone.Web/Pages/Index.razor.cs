using System.Linq;
using System;
using Microsoft.JSInterop;
using Microsoft.Xna.Framework;

namespace Simitone.Web.Pages
{
    public partial class Index
    {
        Game _game;

        protected override void OnAfterRender(bool firstRender)
        {
            base.OnAfterRender(firstRender);
            if (firstRender)
            {
                JsRuntime.InvokeAsync<object>("initRenderJS", DotNetObjectReference.Create(this));
            }
        }

        static bool _crashed;

        /// <summary>
        /// The page's own timer (it keeps going while the tab is in the background, when the game's
        /// frames pause): the lot we're on knows our game is still running - unless it has stopped.
        /// </summary>
        [JSInvokable("SimsReduxAlive")]
        public static void SimsReduxAlive()
        {
            if (_crashed) return;
            try { Simitone.Client.Utils.ReduxGuest.Current?.SendAlive(); } catch { }
        }
        // ---- the phone's keyboard (touch controls) ----
        static FSO.Client.UI.Controls.UITextEdit _kbTarget;
        static bool _kbShown;

        /// <summary>
        /// With touch controls, a game text box that has the focus (a Sim's name, chat...) opens the
        /// phone's own keyboard: the page focuses a hidden text box and sends back what's typed.
        /// </summary>
        void UpdatePhoneKeyboard()
        {
            try
            {
                FSO.Client.UI.Controls.UITextEdit focus = null;
                if (FSO.Common.FSOEnvironment.SoftwareKeyboard)
                    focus = FSO.Client.GameFacade.Screens?.inputManager?.GetFocus() as FSO.Client.UI.Controls.UITextEdit;
                if (focus != null && (!focus.Visible || focus.IsReadOnly)) focus = null;
                bool want = focus != null;
                if (want == _kbShown && focus == _kbTarget) return;
                _kbShown = want;
                _kbTarget = focus;
                ((IJSInProcessRuntime)JsRuntime).InvokeVoid("simsReduxKeyboard", want, want ? (focus.CurrentText ?? "") : "");
            }
            catch { }
        }

        /// <summary>The whole text in the phone keyboard's box (sent on every change).</summary>
        [JSInvokable("SimsReduxKeyboardText")]
        public static void SimsReduxKeyboardText(string text)
        {
            try { _kbTarget?.SetTextFromKeyboard(text); } catch { }
        }

        /// <summary>The phone keyboard was put away (Done / back): the game box lets go of the focus too.</summary>
        [JSInvokable("SimsReduxKeyboardClosed")]
        public static void SimsReduxKeyboardClosed()
        {
            try
            {
                var im = FSO.Client.GameFacade.Screens?.inputManager;
                if (im != null && _kbTarget != null && im.GetFocus() == _kbTarget) im.SetFocus(null);
                _kbShown = false;
                _kbTarget = null;
            }
            catch { }
        }

        /// <summary>A finger on the game (from index.html), already in the game's pixels. phase: 0 down, 1 moved, 2 up, 3 cancelled.</summary>
        [JSInvokable("SimsReduxTouch")]
        public static bool SimsReduxTouch(int id, double x, double y, int phase)
        {
            try { FSO.Common.Rendering.Framework.IO.WebTouchInput.Set(id, (float)x, (float)y, phase); } catch { }
            return FSO.Common.FSOEnvironment.SoftwareKeyboard; // touch controls: the page leaves the touch to the game
        }
        /// <summary>The launcher's version check: the game version this website build actually runs.</summary>
        [JSInvokable("SimsReduxVersion")]
        public static string SimsReduxVersion() { return Simitone.Client.Utils.ReduxBuild.Number; }

        int _frames, _lastW, _lastH;

        /// <summary>Touches and the mouse arrive in page points; the game may be drawn bigger than the page.</summary>
        static void SetInputScale(int[] size)
        {
            if (size == null || size.Length < 4 || size[2] <= 0 || size[3] <= 0) return;
            FSO.Common.FSOEnvironment.InputScaleX = size[0] / (float)size[2];
            FSO.Common.FSOEnvironment.InputScaleY = size[1] / (float)size[3];
        }

        [JSInvokable]
        public void TickDotNet()
        {
            if (_crashed) return;
            try
            {
                if (_game == null)
                {
                    var size = ((IJSInProcessRuntime)JsRuntime).Invoke<int[]>("simsReduxCanvasSize");
                    var gfx = ((IJSInProcessRuntime)JsRuntime).Invoke<string>("simsReduxGfxMode");
                    SetInputScale(size);
                    _game = WebStartup.CreateGame(size[0], size[1], gfx);
                    _game.Run();
                }
                // the browser window may have changed size (or the phone was turned, or the page only
                // finished laying out after the game started): every few frames, make the game match
                // the page. (The canvas is checked every time: the engine sets it back to the page's
                // size whenever the window changes.)
                if (++_frames % 10 == 0 && _game is Simitone.Client.SimitoneGame sg)
                {
                    var now = ((IJSInProcessRuntime)JsRuntime).Invoke<int[]>("simsReduxCanvasSize");
                    if (now[0] > 0 && now[1] > 0)
                    {
                        ((IJSInProcessRuntime)JsRuntime).InvokeVoid("simsReduxFitCanvas");
                        SetInputScale(now);
                        if (now[0] != _lastW || now[1] != _lastH || sg.GraphicsDevice.PresentationParameters.BackBufferWidth != now[0]
                            || sg.GraphicsDevice.PresentationParameters.BackBufferHeight != now[1])
                        {
                            _lastW = now[0]; _lastH = now[1];
                            sg.ResizeTo(now[0], now[1]);
                        }
                    }
                }
                _game.Tick();
                UpdatePhoneKeyboard();
            }
            catch (Exception e)
            {
                // SimsRedux: stop on the first error and show the whole thing in the console
                _crashed = true;
                // leave any lot we're on straight away, so our Sim doesn't carry on without us
                try { Simitone.Client.Utils.ReduxGuest.LeaveBecauseCrashed(); } catch { }
                Console.WriteLine("[SimsRedux] The game stopped: " + e);
                // the first few lines of where it happened go in the on-screen box too (one screenshot is enough)
                var where = string.Join(" | ", (e.StackTrace ?? "").Split('\n').Take(5).Select(l => l.Trim()));
                JsRuntime.InvokeVoidAsync("simsReduxCrashed", e.GetType().Name + ": " + e.Message + "  —  " + where);
            }
        }
    }
}
