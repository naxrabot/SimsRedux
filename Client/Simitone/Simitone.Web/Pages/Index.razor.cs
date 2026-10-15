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
        int _frames, _lastW, _lastH;

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
                    _game = WebStartup.CreateGame(size[0], size[1], gfx);
                    _game.Run();
                }
                // the browser window may have changed size (or the page only finished laying out after
                // the game started): every few frames, make the game match the page
                if (++_frames % 15 == 0 && _game is Simitone.Client.SimitoneGame sg)
                {
                    var now = ((IJSInProcessRuntime)JsRuntime).Invoke<int[]>("simsReduxCanvasSize");
                    if (now[0] > 0 && now[1] > 0 && (now[0] != _lastW || now[1] != _lastH))
                    {
                        _lastW = now[0]; _lastH = now[1];
                        ((IJSInProcessRuntime)JsRuntime).InvokeVoid("simsReduxFitCanvas");
                        sg.ResizeTo(now[0], now[1]);
                    }
                }
                _game.Tick();
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
