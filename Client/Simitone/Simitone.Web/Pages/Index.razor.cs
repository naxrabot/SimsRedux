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

        [JSInvokable]
        public void TickDotNet()
        {
            if (_game == null)
            {
                _game = WebStartup.CreateGame();
                _game.Run();
            }
            _game.Tick();
        }
    }
}
