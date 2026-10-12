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

        bool _crashed;

        [JSInvokable]
        public void TickDotNet()
        {
            if (_crashed) return;
            try
            {
                if (_game == null)
                {
                    var size = ((IJSInProcessRuntime)JsRuntime).Invoke<int[]>("simsReduxCanvasSize");
                    _game = WebStartup.CreateGame(size[0], size[1]);
                    _game.Run();
                }
                _game.Tick();
            }
            catch (Exception e)
            {
                // SimsRedux: stop on the first error and show the whole thing in the console
                _crashed = true;
                Console.WriteLine("[SimsRedux] The game stopped: " + e);
                JsRuntime.InvokeVoidAsync("simsReduxCrashed", e.Message);
            }
        }
    }
}
