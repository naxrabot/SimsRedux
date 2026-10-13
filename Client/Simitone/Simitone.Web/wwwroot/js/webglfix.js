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
    }

    // ---- feedback loops: drawing into a picture that is also being read from ----
    // Desktop graphics quietly allow this; WebGL refuses the whole draw (things go invisible).
    // Before each draw, any picture that is both the drawing target and bound for reading is
    // unbound for that one draw (reading it there gives nothing useful anyway), then put back.
    function guardFeedback(proto) {
        const gl2 = WebGL2RenderingContext;
        const bindFB = proto.bindFramebuffer;
        proto.bindFramebuffer = function (target, fb) {
            if (target === gl2.FRAMEBUFFER || target === gl2.DRAW_FRAMEBUFFER) this.__srxFB = fb;
            return bindFB.call(this, target, fb);
        };
        const fbTex = proto.framebufferTexture2D;
        proto.framebufferTexture2D = function (target, attachment, textarget, tex, level) {
            const fb = this.__srxFB;
            if (fb && target !== gl2.READ_FRAMEBUFFER) {
                fb.__srxAttached = fb.__srxAttached || {};
                fb.__srxAttached[attachment] = tex;
            }
            return fbTex.call(this, target, attachment, textarget, tex, level);
        };
        const active = proto.activeTexture;
        proto.activeTexture = function (unit) { this.__srxUnit = unit; return active.call(this, unit); };
        const bindTex = proto.bindTexture;
        proto.bindTexture = function (target, tex) {
            if (target === gl2.TEXTURE_2D) {
                this.__srxUnits = this.__srxUnits || {};
                this.__srxUnits[this.__srxUnit || gl2.TEXTURE0] = tex;
            }
            return bindTex.call(this, target, tex);
        };
        let told = false;
        for (const name of ['drawElements', 'drawArrays', 'drawElementsInstanced', 'drawArraysInstanced', 'drawRangeElements']) {
            const draw = proto[name];
            if (!draw) continue;
            proto[name] = function () {
                const fb = this.__srxFB, units = this.__srxUnits;
                let undo = null;
                if (fb && fb.__srxAttached && units) {
                    const attached = Object.values(fb.__srxAttached);
                    for (const u in units) {
                        const tex = units[u];
                        if (tex && attached.indexOf(tex) >= 0) {
                            active.call(this, +u);
                            bindTex.call(this, gl2.TEXTURE_2D, null);
                            (undo = undo || []).push([+u, tex]);
                        }
                    }
                    if (undo) {
                        active.call(this, this.__srxUnit || gl2.TEXTURE0);
                        if (!told) { told = true; console.warn('[SimsRedux] graphics: fixed a picture being drawn into while also being read (feedback loop)'); }
                    }
                }
                const r = draw.apply(this, arguments);
                if (undo) {
                    for (const [u, tex] of undo) { active.call(this, u); bindTex.call(this, gl2.TEXTURE_2D, tex); }
                    active.call(this, this.__srxUnit || gl2.TEXTURE0);
                }
                return r;
            };
        }
    }

    // shows graphics reports in a small box on the page, with a Copy button (no need to open the console)
    let pageBox = null, pageText = [];
    function showOnPage(text) {
        pageText.push(text);
        if (!document.body) return;
        if (!pageBox) {
            pageBox = document.createElement('div');
            pageBox.style.cssText = 'position:fixed;top:8px;right:8px;width:min(560px,90vw);max-height:40vh;overflow:auto;z-index:1002;' +
                'background:#000052;color:#ffd27a;border:2px solid #7878c8;font:11px monospace;padding:8px;white-space:pre-wrap;word-break:break-all';
            const bar = document.createElement('div');
            bar.style.cssText = 'font:bold 12px Segoe UI,sans-serif;color:#fff;margin-bottom:6px;display:flex;gap:8px;align-items:center';
            bar.innerHTML = '<span style="flex:1">Graphics report - please send this to Claude</span>';
            const copy = document.createElement('button');
            copy.textContent = 'Copy';
            copy.onclick = () => {
                const all = pageText.join('\n\n');
                (navigator.clipboard ? navigator.clipboard.writeText(all) : Promise.reject()).then(
                    () => { copy.textContent = 'Copied!'; setTimeout(() => copy.textContent = 'Copy', 1500); },
                    () => { const r = document.createRange(); r.selectNodeContents(body); const sel = getSelection(); sel.removeAllRanges(); sel.addRange(r); });
            };
            const close = document.createElement('button');
            close.textContent = 'Hide';
            close.onclick = () => { pageBox.style.display = 'none'; };
            bar.appendChild(copy); bar.appendChild(close);
            const body = document.createElement('div');
            pageBox.appendChild(bar); pageBox.appendChild(body);
            pageBox.__body = body;
            document.body.appendChild(pageBox);
        }
        pageBox.__body.textContent = pageText.join('\n\n');
    }

    // ---- detailed report of a draw the browser refused (once per shader program) ----
    function inspectDraws(proto) {
        const gl2 = WebGL2RenderingContext;
        const bufSizes = new WeakMap();
        const bindBuf = proto.bindBuffer;
        proto.bindBuffer = function (target, buf) {
            if (target === gl2.ELEMENT_ARRAY_BUFFER) this.__srxEBO = buf;
            return bindBuf.call(this, target, buf);
        };
        const bufData = proto.bufferData;
        proto.bufferData = function (target, data) {
            if (target === gl2.ELEMENT_ARRAY_BUFFER && this.__srxEBO) {
                const size = typeof data === 'number' ? data : (data && data.byteLength) || 0;
                bufSizes.set(this.__srxEBO, size);
            }
            return bufData.apply(this, arguments);
        };
        const reported = new Set();
        let checks = 0;
        function describe(gl, name, args) {
            const prog = gl.getParameter(gl.CURRENT_PROGRAM);
            if (!prog || reported.has(prog) || reported.size >= 6) return;
            reported.add(prog);
            const info = { draw: name, count: args[1], indexType: args[2], offset: args[3] };
            const fb = gl.getParameter(gl.DRAW_FRAMEBUFFER_BINDING);
            info.framebuffer = fb ? ('complete=' + (gl.checkFramebufferStatus(gl.DRAW_FRAMEBUFFER) === gl.FRAMEBUFFER_COMPLETE)) : 'screen';
            if (name === 'drawElements' && gl.__srxEBO) info.indexBufferBytes = bufSizes.get(gl.__srxEBO);
            info.attributes = [];
            const na = gl.getProgramParameter(prog, gl.ACTIVE_ATTRIBUTES);
            for (let i = 0; i < na; i++) {
                const a = gl.getActiveAttrib(prog, i);
                const loc = gl.getAttribLocation(prog, a.name);
                const en = loc >= 0 && gl.getVertexAttrib(loc, gl.VERTEX_ATTRIB_ARRAY_ENABLED);
                info.attributes.push(a.name + '@' + loc + ' shaderType=0x' + a.type.toString(16) + (en ? (' size=' + gl.getVertexAttrib(loc, gl.VERTEX_ATTRIB_ARRAY_SIZE) + ' type=0x' + gl.getVertexAttrib(loc, gl.VERTEX_ATTRIB_ARRAY_TYPE).toString(16) + ' int=' + gl.getVertexAttrib(loc, gl.VERTEX_ATTRIB_ARRAY_INTEGER) + ' buffer=' + !!gl.getVertexAttrib(loc, gl.VERTEX_ATTRIB_ARRAY_BUFFER_BINDING)) : ' (not enabled)'));
            }
            info.samplers = [];
            const nu = gl.getProgramParameter(prog, gl.ACTIVE_UNIFORMS);
            for (let i = 0; i < nu; i++) {
                const u = gl.getActiveUniform(prog, i);
                if (u.type === gl.SAMPLER_2D || u.type === gl.SAMPLER_CUBE || u.type === gl.SAMPLER_3D)
                    info.samplers.push(u.name + ' unit=' + gl.getUniform(prog, gl.getUniformLocation(prog, u.name)));
            }
            const shaders = gl.getAttachedShaders(prog) || [];
            info.shaderStart = shaders.map(s => (gl.getShaderSource(s) || '').split('\n').filter(l => /uniform|in |out |attribute/.test(l)).slice(0, 8).join(' | '));
            const line = '[SimsRedux] graphics: refused draw details ' + JSON.stringify(info);
            console.warn(line);
            showOnPage(line);
        }
        for (const name of ['drawElements', 'drawArrays']) {
            const draw = proto[name];
            proto[name] = function () {
                const r = draw.apply(this, arguments);
                // check every draw for a while after start, then every 64th
                if (checks < 200000 || (checks & 63) === 0) {
                    const err = this.getError();
                    if (err) describe(this, name, arguments);
                }
                checks++;
                return r;
            };
        }
    }

    // ---- test switches (?dbg=nodepth / ?dbg=nostencil) to find what hides moving things ----
    function debugSwitches(proto) {
        let dbg = '';
        try { dbg = (new URLSearchParams(location.search).get('dbg') || '').toLowerCase(); } catch (e) { }
        if (!dbg) return;
        console.warn('[SimsRedux] graphics test switch: ' + dbg);
        const gl2 = WebGL2RenderingContext;
        if (dbg.indexOf('nodepth') >= 0) {
            const depthFunc = proto.depthFunc;
            proto.depthFunc = function () { return depthFunc.call(this, gl2.ALWAYS); };
        }
        if (dbg.indexOf('nostencil') >= 0) {
            const enable = proto.enable;
            proto.enable = function (cap) { if (cap === gl2.STENCIL_TEST) return; return enable.call(this, cap); };
        }
    }

    if (window.WebGL2RenderingContext) { debugSwitches(WebGL2RenderingContext.prototype); }
    if (window.WebGL2RenderingContext) { patch(WebGL2RenderingContext.prototype); watch(WebGL2RenderingContext.prototype); inspectDraws(WebGL2RenderingContext.prototype); guardFeedback(WebGL2RenderingContext.prototype); }
    if (window.WebGLRenderingContext) patch(WebGLRenderingContext.prototype);
    window.SimsReduxFixShader = fixShader;
})();
