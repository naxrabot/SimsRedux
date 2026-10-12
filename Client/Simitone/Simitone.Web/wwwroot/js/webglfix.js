// SimsRedux web: small fixes to the game's shaders just before the browser compiles them.
//
// The shader converter puts desktop-OpenGL extension checks at the top of some shaders, like
//   #if GL_ARB_shader_texture_lod / #extension ... / #define texture2DGrad texture2DGradARB
// WebGL 2 rejects those (unknown names in #if, #extension after code). WebGL 2 already has
// gradient sampling built in (textureGrad), so the whole check is swapped for that.
(function () {
    'use strict';

    function fixShader(src) {
        if (typeof src !== 'string' || src.indexOf('#version 300 es') < 0) return src;
        let out = src.replace(/#if\s+GL_ARB_shader_texture_lod[\s\S]*?#endif[^\n]*\n?/g,
            '#define texture2DGrad(a,b,c,d) textureGrad(a,b,c,d)\n' +
            '#define texture2DProjGrad(a,b,c,d) textureProjGrad(a,b,c,d)\n');
        // any other "#if GL_SOMETHING" check: only true if the browser really defines it
        out = out.replace(/#(el)?if\s+(GL_[A-Za-z0-9_]+)\s*$/gm, (m, el, name) => '#' + (el ? 'elif' : 'if') + ' defined(' + name + ')');
        return out;
    }

    function patch(proto) {
        if (!proto || proto.__srxPatched) return;
        const original = proto.shaderSource;
        proto.shaderSource = function (shader, source) {
            return original.call(this, shader, fixShader(source));
        };
        proto.__srxPatched = true;
    }

    // ---- diagnostics: report graphics errors in the console (a few times, not every frame) ----
    let reports = 0;
    function report(gl, what) {
        if (reports >= 20) return;
        reports++;
        console.warn('[SimsRedux] graphics: ' + what);
    }
    function watch(proto) {
        const bind = proto.bindTexture;
        proto.bindTexture = function (target, tex) {
            if (tex && !this.isTexture(tex) && tex.__srxDeleted) report(this, 'a picture was drawn after it was thrown away (deleted texture)');
            return bind.call(this, target, tex);
        };
        const del = proto.deleteTexture;
        proto.deleteTexture = function (tex) { if (tex) tex.__srxDeleted = true; return del.call(this, tex); };
        let frames = 0;
        for (const name of ['drawElements', 'drawArrays']) {
            const draw = proto[name];
            proto[name] = function () {
                const r = draw.apply(this, arguments);
                if ((++frames & 1023) === 0) {
                    const err = this.getError();
                    if (err) report(this, name + ' failed with WebGL error ' + err + ' (0x' + err.toString(16) + ')');
                }
                return r;
            };
        }
    }

    if (window.WebGL2RenderingContext) { patch(WebGL2RenderingContext.prototype); watch(WebGL2RenderingContext.prototype); }
    if (window.WebGLRenderingContext) patch(WebGLRenderingContext.prototype);
    window.SimsReduxFixShader = fixShader;
})();
