// SimsRedux web: the game's files in the browser.
//
// The player picks their own Sims 1 folder once. Its files are copied into this browser's
// storage (IndexedDB) - nothing is uploaded anywhere - and on every visit they're loaded into
// memory so the game can read them instantly (the game reads files synchronously).
//
// Paths the game uses:
//   /sims/...      the player's Sims 1 folder (saves made in-game are kept in the browser too)
//   /userdata/...  SimsRedux's own settings and data (kept in the browser)
//   Content/...    the game's own content that ships with the website (read-only)
//
// The C# side (BrowserFileSystem.cs) calls the functions on window.SimsFS.
(function () {
    'use strict';

    const DB_NAME = 'SimsRedux';
    const STORE = 'files';
    const SIMS_ROOT = '/sims/';
    // big files that aren't needed to start: loaded in the background after the game starts
    const LAZY = [/^\/sims\/music\//, /\.(mp4|bik|avi|webm|mov|wmv|mpg|mpeg)$/];
    // never copied from the player's folder (installers, programs, manuals)
    const SKIP_EXT = /\.(exe|dll|ico|msi|cab|hdr|inx|chm|pdf|lnk|url|tmp)$/i;
    // written by the game but not worth keeping between visits
    const NO_PERSIST = /\.log$/i;

    const files = new Map();   // key (lower-case path) -> { name, size, data: Uint8Array|null, blob }
    const dirs = new Map();    // key of every folder that exists -> its real name
    let db = null;

    // ---------- paths ----------

    function normalize(path) {
        let p = String(path).replace(/\\/g, '/');
        const abs = p.startsWith('/');
        const out = [];
        for (const part of p.split('/')) {
            if (part === '' || part === '.') continue;
            if (part === '..') { out.pop(); continue; }
            out.push(part);
        }
        return (abs ? '/' : '') + out.join('/');
    }
    function key(path) { return normalize(path).toLowerCase(); }
    function parentKey(k) {
        const i = k.lastIndexOf('/');
        return i <= 0 ? (k.startsWith('/') ? '/' : '') : k.substring(0, i);
    }
    // registers every folder above a path (given with its real capitalisation)
    function addDirsFor(realPath) {
        let d = normalize(realPath);
        while (true) {
            const i = d.lastIndexOf('/');
            if (i <= 0) break;
            d = d.substring(0, i);
            const k = d.toLowerCase();
            if (dirs.has(k)) break;
            dirs.set(k, d);
        }
    }
    function addFile(name, entry) {
        const k = key(name);
        entry.name = normalize(name);
        files.set(k, entry);
        addDirsFor(entry.name);
        return k;
    }

    // ---------- IndexedDB ----------

    function req(r) {
        return new Promise((ok, fail) => { r.onsuccess = () => ok(r.result); r.onerror = () => fail(r.error); });
    }
    function txDone(tx) {
        return new Promise((ok, fail) => { tx.oncomplete = () => ok(); tx.onerror = () => fail(tx.error); tx.onabort = () => fail(tx.error); });
    }
    async function openDb() {
        if (db) return db;
        const r = indexedDB.open(DB_NAME, 1);
        r.onupgradeneeded = () => r.result.createObjectStore(STORE);
        db = await req(r);
        return db;
    }
    async function allRecords() {
        const tx = (await openDb()).transaction(STORE, 'readonly');
        const store = tx.objectStore(STORE);
        const [keys, values] = await Promise.all([req(store.getAllKeys()), req(store.getAll())]);
        return keys.map((k, i) => ({ k, v: values[i] }));
    }
    // writes from the game are saved one after another so they land in order
    let saveChain = Promise.resolve();
    function persist(k, entry) {
        if (NO_PERSIST.test(k) || !(k.startsWith(SIMS_ROOT) || k.startsWith('/userdata/'))) return;
        const blob = new Blob([entry.data]);
        saveChain = saveChain.then(async () => {
            const tx = (await openDb()).transaction(STORE, 'readwrite');
            tx.objectStore(STORE).put({ name: entry.name, blob }, k);
            await txDone(tx);
        }).catch(e => console.warn('[SimsFS] could not save ' + k, e));
    }
    function unpersist(k) {
        saveChain = saveChain.then(async () => {
            const tx = (await openDb()).transaction(STORE, 'readwrite');
            tx.objectStore(STORE).delete(k);
            await txDone(tx);
        }).catch(e => console.warn('[SimsFS] could not delete ' + k, e));
    }
    async function clearSims() {
        const tx = (await openDb()).transaction(STORE, 'readwrite');
        tx.objectStore(STORE).delete(IDBKeyRange.bound(SIMS_ROOT, SIMS_ROOT + '\uffff'));
        await txDone(tx);
    }

    // ---------- the setup / loading screen ----------

    let ui = null;
    function screen() {
        if (ui) return ui;
        const style = document.createElement('style');
        style.textContent = `
#srx-setup { position: fixed; inset: 0; z-index: 1000; color: #d7d7f5;
  /* the Sims Redux wallpaper (the same one as the online loading screen), blue around it */
  background: #002b87 url(img/redux-wallpaper.jpg) center / contain no-repeat; overflow: auto;
  font-family: 'Segoe UI', Tahoma, sans-serif; display: flex; align-items: center; justify-content: center; padding: 16px; }
#srx-setup .box { width: 100%; max-width: 560px; background: rgba(0, 0, 58, .92); max-height: calc(100vh - 32px); overflow: auto; border: 2px solid #7878c8; border-right-color: #000028;
  border-bottom-color: #000028; box-shadow: 6px 6px 0 rgba(0,0,0,.35); padding: 24px 26px; }
#srx-setup h1 { font-size: 26px; margin: 0 0 4px; color: #fff; font-weight: 600; }
#srx-setup h2 { font-size: 15px; margin: 0 0 18px; color: #78ff8c; font-weight: 600; }
#srx-setup p { font-size: 14px; line-height: 1.5; margin: 0 0 12px; }
#srx-setup code { background: #000052; padding: 1px 5px; color: #fff; font-size: 13px; overflow-wrap: anywhere; }
#srx-setup button { font: inherit; font-size: 15px; font-weight: 600; color: #fff; background: #2d6ae0; border: 2px solid #8fb4ff;
  border-right-color: #12307a; border-bottom-color: #12307a; padding: 9px 18px; cursor: pointer; margin-top: 6px; }
#srx-setup button:hover { background: #3b7bf5; }
#srx-setup button.link { background: none; border: none; color: #9fb4ff; font-size: 13px; font-weight: 400; padding: 0; text-decoration: underline; }
#srx-setup .bar { height: 14px; background: #000020; border: 1px solid #5a5aa8; margin: 14px 0 6px; }
#srx-setup .bar div { height: 100%; width: 0; background: linear-gradient(#7cf08a, #2fae45); transition: width .15s; }
#srx-setup .status { font-size: 13px; min-height: 18px; }
#srx-setup .error { color: #ff9a8a; }
#srx-setup .hidden, #srx-setup.hidden { display: none; }
#srx-setup .acct { border: 1px solid #3a3a8a; background: #00002c; padding: 12px 14px; margin: 0 0 16px; }
#srx-setup .acct h3 { margin: 0 0 8px; font-size: 13px; color: #9fb4ff; font-weight: 600; letter-spacing: .04em; }
#srx-setup .acct .row { display: flex; gap: 8px; flex-wrap: wrap; }
#srx-setup .acct input { font: inherit; font-size: 14px; color: #fff; background: #000052; border: 1px solid #5a5aa8; padding: 7px 9px; flex: 1 1 140px; min-width: 0; }
#srx-setup .acct input:focus { outline: none; border-color: #8fb4ff; }
#srx-setup .acct button { margin-top: 8px; }
#srx-setup .acct button.second { background: #1d2a6e; border-color: #5a6ab8; border-right-color: #0a1240; border-bottom-color: #0a1240; }
#srx-setup .acct .who { font-size: 14px; }
#srx-setup .acct .who b { color: #fff; }
#srx-setup .acct .msg { font-size: 13px; min-height: 0; margin-top: 6px; }
#srx-setup .acct .msg.error { color: #ff9a8a; }
#srx-setup .acct .small { font-size: 12px; color: #8a8ac8; margin-top: 8px; }
#srx-setup .srvfiles { margin: 0 0 14px; }
#srx-setup .or { font-size: 13px; color: #8a8ac8; margin: 2px 0 6px; }

/* ---------- the launcher ---------- */
#srx-setup .box.wide { max-width: 680px; padding: 0; background: rgba(4, 8, 38, .88); border: 1px solid rgba(143, 180, 255, .35);
  box-shadow: 0 24px 70px rgba(0,0,0,.6), inset 0 1px 0 rgba(255,255,255,.06); backdrop-filter: blur(6px);
  display: flex; flex-direction: column; overflow: hidden; }
#srx-setup .launcher { display: flex; flex-direction: column; min-height: 0; flex: 1 1 auto; }
#srx-setup .launcher.hidden { display: none; }
#srx-setup .l-top { padding: 28px 30px 18px; text-align: center; background: linear-gradient(rgba(120,255,140,.07), transparent); }
#srx-setup .logo { font-size: 38px; font-weight: 800; letter-spacing: .14em; color: #fff; line-height: 1; text-shadow: 0 2px 18px rgba(80,140,255,.45); }
#srx-setup .logo span { color: #78ff8c; }
#srx-setup .logo img { max-width: 100%; max-height: 120px; }
#srx-setup .ver { margin-top: 10px; font-size: 12px; letter-spacing: .12em; text-transform: uppercase; color: #8a96d0; }
#srx-setup .ver b { color: #fff; font-weight: 600; }
#srx-setup .ver .stage { color: #04102a; background: #78ff8c; font-weight: 700; padding: 1px 6px; margin-right: 8px; letter-spacing: .1em; }
#srx-setup .l-acct { margin: 0 30px; padding: 14px 16px; border: 1px solid rgba(143,180,255,.2); background: rgba(0,0,0,.25);
  display: grid; grid-template-columns: auto 1fr auto; gap: 4px 14px; align-items: center; }
#srx-setup .avatar { grid-row: span 2; width: 44px; height: 44px; border-radius: 50%; display: grid; place-items: center; font-weight: 700; font-size: 19px;
  color: #04102a; background: linear-gradient(135deg, #9fffb0, #3bc25a); }
#srx-setup .avatar.off { background: #2a335e; color: #9fb4ff; }
#srx-setup .lname { font-size: 16px; font-weight: 600; color: #fff; overflow-wrap: anywhere; }
#srx-setup .badge { font-size: 10px; letter-spacing: .1em; font-weight: 700; color: #9fb4ff; border: 1px solid #5a6ab8; padding: 1px 5px; margin-left: 6px; vertical-align: 2px; }
#srx-setup .lstatus { font-size: 12px; color: #8a96d0; display: flex; align-items: center; gap: 6px; }
#srx-setup .dot { width: 8px; height: 8px; border-radius: 50%; background: #6a6a8a; flex: none; }
#srx-setup .dot.on { background: #78ff8c; box-shadow: 0 0 8px #78ff8c; }
#srx-setup .dot.warn { background: #ffb547; }
#srx-setup .lbtns { grid-row: span 2; display: flex; gap: 8px; flex-wrap: wrap; justify-content: flex-end; }
#srx-setup button.ghost { font-size: 12px; font-weight: 600; letter-spacing: .04em; padding: 6px 11px; background: transparent; color: #c8d4ff;
  border: 1px solid rgba(143,180,255,.4); margin: 0; }
#srx-setup button.ghost:hover { background: rgba(143,180,255,.12); color: #fff; }
#srx-setup .lfolder { grid-column: 1 / -1; margin-top: 10px; padding-top: 10px; border-top: 1px solid rgba(143,180,255,.12); font-size: 12px; color: #8a96d0;
  display: flex; gap: 8px; align-items: baseline; flex-wrap: wrap; }
#srx-setup .lfolder b { color: #d7d7f5; font-weight: 600; }
#srx-setup .llevel { grid-column: 1 / -1; display: flex; align-items: center; gap: 10px; margin-top: 10px; font-size: 11px; letter-spacing: .08em; color: #8a96d0; }
#srx-setup .llevel .lvtext b { color: #78ff8c; font-size: 13px; }
#srx-setup .lvbar { flex: 1; height: 6px; background: rgba(143,180,255,.15); }
#srx-setup .lvbar div { height: 100%; width: 0; background: linear-gradient(90deg, #3bc25a, #9fffb0); }
#srx-setup .lreport { grid-column: 1 / -1; margin-top: 10px; padding-top: 10px; border-top: 1px solid rgba(143,180,255,.12); display: grid; gap: 8px; }
#srx-setup .lreport input, #srx-setup .lreport textarea { font: inherit; font-size: 13px; color: #fff; background: #000052; border: 1px solid #5a5aa8; padding: 7px 9px; resize: vertical; }
#srx-setup .lreport .rrow { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; }
#srx-setup .lreport .rmsg { font-size: 12px; color: #8a96d0; }
#srx-setup .lreport .rmsg.error { color: #ff9a8a; }
#srx-setup .lreport.hidden, #srx-setup .llevel.hidden { display: none; }
#srx-setup .lfolder button.link { font-size: 12px; }
#srx-setup .l-srv { margin: 10px 30px 0; display: grid; grid-template-columns: 1fr 1fr; gap: 10px; }
#srx-setup .sitem { padding: 10px 14px; border: 1px solid rgba(143,180,255,.2); background: rgba(0,0,0,.25); }
#srx-setup button.srv { font: inherit; text-align: left; cursor: pointer; color: inherit; margin: 0; border-radius: 0; width: 100%; }
#srx-setup button.srv:hover { background: rgba(143,180,255,.1); }
#srx-setup button.srv.sel { border-color: #78ff8c; background: rgba(120,255,140,.08); box-shadow: inset 0 0 0 1px #78ff8c; }
#srx-setup button.srv.sel .k::after { content: '  \\2713 SELECTED'; color: #78ff8c; }
#srx-setup .dot.down { background: #ff5a5a; box-shadow: 0 0 8px #ff5a5a; }
#srx-setup .vcheck { font-size: 12px; text-align: center; margin: 0 0 10px; color: #8a96d0; min-height: 16px; }
#srx-setup .vcheck.ok { color: #78ff8c; } #srx-setup .vcheck.warn { color: #ffb547; }
#srx-setup .sitem .k { font-size: 10px; letter-spacing: .16em; font-weight: 700; color: #8a96d0; margin-bottom: 4px; }
#srx-setup .sitem .v { font-size: 15px; font-weight: 600; color: #fff; display: flex; align-items: center; gap: 8px; }
#srx-setup .sitem .v small { font-size: 12px; font-weight: 400; color: #8a96d0; margin-left: auto; }
#srx-setup .smotd { margin: 12px 30px 0; font-size: 13.5px; color: #e4e9ff; padding: 10px 14px; border-left: 3px solid #78ff8c; background: rgba(120,255,140,.06); }
#srx-setup .smotd .mevent { margin-top: 6px; font-weight: 700; color: #ffd36b; letter-spacing: .04em; }
#srx-setup .smotd .k { font-size: 10px; letter-spacing: .16em; font-weight: 700; color: #78ff8c; margin-bottom: 4px; }
#srx-setup .notes { margin: 16px 30px 0; flex: 1 1 auto; min-height: 120px; overflow: auto; border-top: 1px solid rgba(143,180,255,.2); padding-top: 14px; }
#srx-setup .nhead { display: flex; justify-content: space-between; align-items: baseline; font-size: 12px; letter-spacing: .16em; font-weight: 700; color: #fff; margin-bottom: 10px; }
#srx-setup .nhead span { color: #8a96d0; font-weight: 400; letter-spacing: .06em; }
#srx-setup .patch + .patch { margin-top: 16px; padding-top: 14px; border-top: 1px dashed rgba(143,180,255,.15); }
#srx-setup .patch .pv { font-size: 12px; color: #9fb4ff; letter-spacing: .1em; margin-bottom: 6px; }
#srx-setup .cat { font-size: 11px; font-weight: 700; letter-spacing: .14em; margin: 10px 0 5px; }
#srx-setup .cat.feat { color: #78ff8c; }
#srx-setup .cat.upd { color: #7fc4ff; }
#srx-setup .notes ul { margin: 0; padding: 0; list-style: none; }
#srx-setup .notes li { font-size: 13.5px; line-height: 1.45; padding: 2px 0 2px 16px; position: relative; color: #d7d7f5; }
#srx-setup .notes li::before { content: ''; position: absolute; left: 3px; top: 9px; width: 5px; height: 5px; background: currentColor; opacity: .5; }
#srx-setup .cat.feat + ul li::before { background: #78ff8c; opacity: 1; }
#srx-setup .cat.upd + ul li::before { background: #7fc4ff; opacity: 1; }
#srx-setup .nolder { margin-top: 12px; font-size: 12px; }
#srx-setup .l-bottom { padding: 18px 30px 22px; }
#srx-setup button.play { display: block; width: 100%; margin: 0; padding: 15px; font-size: 20px; font-weight: 800; letter-spacing: .3em;
  color: #04220d; background: linear-gradient(#9fffb0, #3bc25a); border: none; box-shadow: 0 6px 24px rgba(80,230,110,.25), inset 0 1px 0 rgba(255,255,255,.5); }
#srx-setup button.play:hover { background: linear-gradient(#b8ffc5, #4fd56d); }
#srx-setup button.play:disabled { opacity: .6; cursor: default; }
#srx-setup .l-foot { margin-top: 10px; text-align: center; font-size: 11px; color: #6a74a8; }
@media (max-width: 560px) {
  #srx-setup .l-top { padding: 22px 18px 14px; } #srx-setup .logo { font-size: 28px; }
  #srx-setup .l-acct { margin: 0 16px; grid-template-columns: auto 1fr; }
  #srx-setup .avatar { grid-column: 1; grid-row: 1 / span 2; } #srx-setup .lname { grid-column: 2; grid-row: 1; } #srx-setup .lstatus { grid-column: 2; grid-row: 2; }
  #srx-setup .lbtns { grid-row: 3; grid-column: 1 / -1; justify-content: flex-start; margin-top: 10px; } #srx-setup .llevel { grid-row: 4; } #srx-setup .lreport { grid-row: 5; } #srx-setup .lfolder { grid-row: 6; }
  #srx-setup .l-srv { margin: 10px 16px 0; } #srx-setup .smotd { margin: 12px 16px 0; } #srx-setup .notes { margin: 14px 16px 0; } #srx-setup .l-bottom { padding: 14px 16px 18px; }
}`;
        document.head.appendChild(style);
        const el = document.createElement('div');
        el.id = 'srx-setup';
        el.innerHTML = `<div class="box">
<div class="launcher hidden">
  <div class="l-top">
    <div class="logo">THE SIMS <span>REDUX</span></div>
    <div class="ver"><span class="stage">BETA</span>Version <b class="vnum">&mdash;</b></div>
  </div>
  <div class="l-acct">
    <div class="avatar">?</div>
    <div class="lname"></div>
    <div class="lbtns"></div>
    <div class="lstatus"><i class="dot"></i><span class="ltext"></span></div>
    <div class="llevel hidden"><span class="lvtext"></span><div class="lvbar"><div></div></div></div>
    <div class="lreport hidden">
      <input class="rabout" type="text" maxlength="40" placeholder="Player's name" spellcheck="false">
      <textarea class="rreason" maxlength="400" rows="2" placeholder="What happened?"></textarea>
      <div class="rrow"><button class="ghost rsend">Send report</button><button class="link rcancel">Cancel</button><span class="rmsg"></span></div>
    </div>
    <div class="lfolder">Game files &amp; saves: <b class="lfstate">Checking...</b> <button class="link lchange">Change folder</button></div>
  </div>
  <div class="l-srv">
    <button class="sitem srv" data-ch="live"><div class="k">LIVE SERVER</div><div class="v"><i class="dot"></i><span class="sstate">Checking...</span><small class="scount"></small></div></button>
    <button class="sitem srv" data-ch="test"><div class="k">TEST SERVER</div><div class="v"><i class="dot"></i><span class="sstate">Checking...</span><small class="scount"></small></div></button>
  </div>
  <div class="smotd hidden"><div class="k">MESSAGE OF THE DAY</div><div class="mtext"></div></div>
  <div class="notes">
    <div class="nhead">PATCH NOTES <span class="ndate"></span></div>
    <div class="nbody"></div>
    <button class="link nolder hidden">Show earlier patches</button>
  </div>
  <div class="l-bottom">
    <div class="vcheck"></div>
    <button class="play">PLAY</button>
    <div class="l-foot">build ${(document.querySelector('meta[name="srx-build"]') || {}).content || 'dev'}</div>
  </div>
</div>
<div class="setup">
  <h1>The Sims Redux</h1>
  <h2 class="title"></h2>
  <div class="acct">
    <h3>YOUR ACCOUNT</h3>
    <div class="out">
      <div class="row"><input class="aname" type="text" autocomplete="username" placeholder="Name" maxlength="20" spellcheck="false">
        <input class="apw" type="password" autocomplete="current-password" placeholder="Password"></div>
      <button class="login">Log in</button> <button class="second register">Create account</button>
      <button class="link offline" style="margin-left:10px">Play offline</button>
      <div class="small">An account keeps your homes and families yours online. Offline, you play on your own.</div>
    </div>
    <div class="in hidden">
      <div class="who">Logged in as <b class="aname2"></b> <span class="arole"></span></div>
      <button class="link logout">Log out</button>
    </div>
    <div class="msg"></div>
  </div>
  <div class="pick hidden">
    <div class="srvfiles hidden">
      <p>Your account can load the game files straight from the server &mdash; nothing to install.</p>
      <button class="fromserver">Get the game files from the server</button>
      <p class="or">or use your own copy of The Sims:</p>
    </div>
    <p>To play, choose the folder where <b>The Sims</b> is installed &mdash; the one with the <code>GameData</code> folder inside. It's usually
       <code>C:\\Program Files (x86)\\Maxis\\The Sims</code>.</p>
    <p>Your browser will ask to <i>upload</i> the files. Nothing leaves your computer: they're only copied into this
       browser's storage, so you only have to do this once.</p>
    <button class="choose">Choose your Sims folder</button>
    <input type="file" class="folder hidden" webkitdirectory directory multiple>
  </div>
  <div class="srx-progress hidden"><div class="bar"><div></div></div></div>
  <div class="status"></div>
  <div class="again hidden" style="margin-top:10px"><button class="link change">Use a different Sims folder</button>
    <button class="link signout" style="margin-left:16px">Log out / switch account</button></div>
  <div style="margin-top:14px;font-size:11px;color:#7878b8">build ${(document.querySelector('meta[name="srx-build"]') || {}).content || 'dev'}</div>
</div>
</div>`;
        document.body.appendChild(el);
        ui = {
            root: el,
            title: el.querySelector('.title'),
            pick: el.querySelector('.pick'),
            choose: el.querySelector('.choose'),
            input: el.querySelector('.folder'),
            progress: el.querySelector('.srx-progress'),
            bar: el.querySelector('.bar div'),
            status: el.querySelector('.status'),
            again: el.querySelector('.again'),
            change: el.querySelector('.change'),
            acct: el.querySelector('.acct'),
            srvfiles: el.querySelector('.srvfiles'),
            fromServer: el.querySelector('.fromserver'),
        };
        Account.attach(el);
        ui.choose.onclick = () => ui.input.click();
        return ui;
    }
    function show(title, { pick = false, progress = false, again = false } = {}) {
        const s = screen();
        s.root.classList.remove('hidden');
        // the setup steps (log in, game folder, loading), not the launcher
        s.root.querySelector('.box').classList.remove('wide');
        s.root.querySelector('.launcher').classList.add('hidden');
        s.root.querySelector('.setup').classList.remove('hidden');
        s.title.textContent = title;
        s.pick.classList.toggle('hidden', !pick);
        s.progress.classList.toggle('hidden', !progress);
        s.again.classList.toggle('hidden', !again);
        status('');
    }
    function status(text, error) {
        const s = screen();
        s.status.textContent = text;
        s.status.classList.toggle('error', !!error);
    }
    function setProgress(done, total) {
        screen().bar.style.width = (total > 0 ? Math.min(100, done / total * 100) : 0).toFixed(1) + '%';
    }
    function mb(bytes) { return (bytes / 1048576).toFixed(0) + ' MB'; }

    // ---------- the player's account (on the dedicated server) ----------
    //
    // The website and the server are at different addresses: tsr.mbrew.studio serves the game,
    // tsr-lot.mbrew.studio is the server on Joey's PC (through Cloudflare). ?server=... picks
    // another server, e.g. the Test one.
    // The launcher picks Live (the default) or Test; each server has its own accounts, so each keeps
    // its own login in this browser.
    const LIVE_URL = 'https://tsr-lot.mbrew.studio';
    function lsGet(k, d) { try { const v = localStorage.getItem(k); return v == null ? d : v; } catch (e) { return d; } }
    function lsSet(k, v) { try { if (v == null) localStorage.removeItem(k); else localStorage.setItem(k, v); } catch (e) { } }
    const Servers = {
        get channel() { return lsGet('srx-channel', 'live') === 'test' ? 'test' : 'live'; },
        set channel(c) { lsSet('srx-channel', c === 'test' ? 'test' : null); },
        // (?server=... in the address replaces the Live server, for trying a server on this PC)
        url(c) { return c === 'test' ? lsGet('srx-test-url', 'https://tsr-test.mbrew.studio') : ((new URLSearchParams(location.search).get('server') || LIVE_URL).replace(/\/+$/, '')); },
        learn(servers) { if (servers && typeof servers.test === 'string' && /^https:\/\//.test(servers.test)) lsSet('srx-test-url', servers.test.replace(/\/+$/, '')); },
    };
    const Account = (function () {
        const forced = new URLSearchParams(location.search).get('server');
        const SERVER = (forced || Servers.url(Servers.channel)).replace(/\/+$/, '');
        const KEY = (!forced && Servers.channel === 'test') ? 'srx-account-test' : 'srx-account';
        let session = null;      // { token, name, role, files }
        let root = null, onChange = null;
        let reachable = null;    // did the server answer last time? (null = not asked yet)

        function load() {
            try { session = JSON.parse(localStorage.getItem(KEY) || 'null'); } catch (e) { session = null; }
            return session;
        }
        function store(s) {
            session = s;
            try { if (s) localStorage.setItem(KEY, JSON.stringify(s)); else localStorage.removeItem(KEY); } catch (e) { }
            paint();
            if (onChange) onChange(session);
        }
        async function call(path, body, method) {
            const headers = {};
            if (body) headers['Content-Type'] = 'application/json';
            if (session && session.token) headers['Authorization'] = 'Bearer ' + session.token;
            let r;
            try {
                r = await fetch(SERVER + path, { method: method || (body ? 'POST' : 'GET'), headers, body: body ? JSON.stringify(body) : undefined });
            } catch (e) {
                reachable = false;
                return { ok: false, status: 0, error: "Couldn't reach the server. It may be offline right now - you can still play without an account." };
            }
            reachable = true;
            let j = null;
            try { j = await r.json(); } catch (e) { }
            if (!r.ok) return { ok: false, status: r.status, error: (j && j.error) || ('The server answered ' + r.status) };
            return Object.assign({ ok: true, status: r.status }, j || {});
        }
        function msg(text, error) {
            if (!root) return;
            const m = root.querySelector('.msg');
            m.textContent = text || '';
            m.classList.toggle('error', !!error);
        }
        function paint() {
            if (!root) return;
            const on = !!(session && session.token);
            root.querySelector('.out').classList.toggle('hidden', on);
            root.querySelector('.in').classList.toggle('hidden', !on);
            if (on) {
                root.querySelector('.aname2').textContent = session.name;
                root.querySelector('.arole').textContent = session.role === 'admin' ? '(Admin)' : session.role === 'tester' ? '(Tester)' : '';
            }
            const s = screen();
            s.srvfiles.classList.toggle('hidden', !(on && session.files));
        }
        async function login(register) {
            const name = root.querySelector('.aname').value.trim();
            const password = root.querySelector('.apw').value;
            if (!name || !password) { msg('Type a name and a password.', true); return; }
            msg(register ? 'Making your account...' : 'Logging in...');
            const r = await call(register ? '/api/register' : '/api/login', { name, password });
            if (!r.ok) { msg(r.error, true); return; }
            root.querySelector('.apw').value = '';
            // new accounts wait for the server owner to approve them
            if (r.pending) {
                msg('Account made. Please wait for admin approval.');
                alert(r.message || 'Please wait for admin approval before connecting.');
                return;
            }
            store({ token: r.token, name: r.name, role: r.role, files: !!r.files });
            msg(register ? 'Account made. Welcome, ' + r.name + '!' : (r.motd || ''));
            if (choose) { const c = choose; choose = null; c('account'); }
        }
        async function logout() {
            if (session) call('/api/logout', {}).catch(() => { });
            store(null);
            msg('Logged out.');
        }
        // checks a remembered login is still good (and picks up role changes)
        async function refresh() {
            if (!load()) { paint(); return; }
            const r = await call('/api/me');
            if (r.ok) store({ token: session.token, name: r.name, role: r.role, files: !!r.files, level: r.level | 0, xp: r.xp | 0, next: r.next | 0 });
            else if (r.status === 401) { store(null); msg('Please log in again.'); }
            else paint(); // server offline: keep the remembered login
        }
        // The first step on every visit: resolves once the player is logged in or chose to play offline.
        let choose = null;
        async function step() {
            await refresh();
            if (session && session.token) return 'account';
            return new Promise(ok => { choose = ok; });
        }
        function attach(el) {
            root = el.querySelector('.acct');
            root.querySelector('.offline').onclick = () => { msg(''); if (choose) { const c = choose; choose = null; c('offline'); } };
            root.querySelector('.login').onclick = () => login(false);
            root.querySelector('.register').onclick = () => login(true);
            root.querySelector('.logout').onclick = logout;
            root.querySelector('.apw').addEventListener('keydown', e => { if (e.key === 'Enter') login(false); });
            load(); paint();
        }
        return {
            SERVER, attach, refresh, call, step, logout,
            get reachable() { return reachable; },
            get session() { return session; },
            set onChange(fn) { onChange = fn; },
            show(on) { if (root) root.classList.toggle('hidden', !on); },
        };
    })();

    // Admin and Tester accounts: the Sims 1 files come from the server instead of a folder.
    async function downloadFromServer() {
        const s = Account.session;
        const list = await Account.call('/api/files');
        if (!list.ok) { if (list.status === 401) Account.refresh(); throw new Error(list.error); }
        const files = (list.files || []).filter(f => f && f.p && !SKIP_EXT.test(f.p));
        if (!files.some(f => f.p.toLowerCase() === 'gamedata/uitext.iff')) throw new Error("the server's game folder doesn't have GameData\\UIText.iff in it");
        const total = files.reduce((n, f) => n + (f.s || 0), 0);
        show('Getting the game files from the server', { progress: true });
        Account.show(false);
        await clearSims();
        try { if (navigator.storage && navigator.storage.persist) await navigator.storage.persist(); } catch (e) { }
        // phones (iPhones especially) only let a website keep so much: say so up front
        try {
            const est = navigator.storage && navigator.storage.estimate ? await navigator.storage.estimate() : null;
            if (est && est.quota && est.quota - (est.usage || 0) < total * 1.05)
                throw new Error('this browser only lets the website keep about ' + mb(est.quota - (est.usage || 0)) +
                    ', and the game files are ' + mb(total) + '. (Private/incognito tabs get much less - try a normal tab.)');
        } catch (e) { if (e && /lets the website keep/.test(e.message)) throw e; }
        // if the page dies part-way (the browser ran out of memory and reloaded it), say so next time
        try { sessionStorage.setItem('srx-downloading', String(Date.now())); } catch (e) { }

        let done = 0, count = 0, failed = null;
        const queue = files.slice();
        async function worker() {
            while (queue.length && !failed) {
                const f = queue.shift();
                let blob = null;
                for (let attempt = 0; attempt < 4 && !blob; attempt++) {
                    try {
                        const r = await fetch(Account.SERVER + '/api/file?p=' + encodeURIComponent(f.p), { headers: { 'Authorization': 'Bearer ' + s.token } });
                        if (r.status === 401 || r.status === 403) { failed = 'your login stopped working - log in again'; return; }
                        if (!r.ok) throw new Error(r.status);
                        blob = await r.blob();
                    } catch (e) {
                        if (attempt === 3) { failed = 'couldn\'t download ' + f.p + ' (' + (e && e.message || e) + ')'; return; }
                        await new Promise(ok => setTimeout(ok, 800 * (attempt + 1)));
                    }
                }
                const name = SIMS_ROOT + f.p;
                try {
                    const tx = db.transaction(STORE, 'readwrite');
                    tx.objectStore(STORE).put({ name: normalize(name), blob }, key(name));
                    await txDone(tx);
                } catch (e) {
                    failed = (e && e.name === 'QuotaExceededError')
                        ? 'this browser ran out of storage space after ' + mb(done) + ' (it needs ' + mb(total) + ')'
                        : "couldn't save " + f.p + ' (' + ((e && (e.name + ': ' + e.message)) || e) + ')';
                    return;
                }
                done += blob.size; count++;
                setProgress(done, total);
                status(`${count} of ${files.length} files (${mb(done)} of ${mb(total)})`);
            }
        }
        await Promise.all(Array.from({ length: 6 }, worker));
        try { sessionStorage.removeItem('srx-downloading'); } catch (e) { }
        if (failed) { await clearSims(); throw new Error(failed); }
        return true;
    }

    // ---------- choosing the folder ----------

    function waitForFolder() {
        return new Promise(resolve => {
            const s = screen();
            s.input.value = '';
            s.input.onchange = () => {
                const list = Array.from(s.input.files || []);
                if (list.length) resolve(list);
            };
        });
    }

    // Works out which part of the chosen folder is the Sims folder (the player may pick its parent).
    function findSimsRoot(list) {
        for (const f of list) {
            const rel = (f.webkitRelativePath || f.name).replace(/\\/g, '/');
            const lower = rel.toLowerCase();
            if (lower === 'gamedata/uitext.iff' || lower.endsWith('/gamedata/uitext.iff'))
                return rel.substring(0, rel.length - 'gamedata/uitext.iff'.length);
        }
        return null;
    }

    async function copyFolder(list) {
        const root = findSimsRoot(list);
        if (root === null) return false;
        const chosen = list.filter(f => {
            const rel = (f.webkitRelativePath || f.name).replace(/\\/g, '/');
            return rel.startsWith(root) && !SKIP_EXT.test(rel);
        });
        const total = chosen.reduce((n, f) => n + f.size, 0);
        show('Copying your Sims files', { progress: true });
        await clearSims();
        try { if (navigator.storage && navigator.storage.persist) await navigator.storage.persist(); } catch (e) { }

        let done = 0, i = 0;
        const BATCH = 100;
        while (i < chosen.length) {
            const batch = chosen.slice(i, i + BATCH);
            const tx = db.transaction(STORE, 'readwrite');
            const store = tx.objectStore(STORE);
            for (const f of batch) {
                const rel = (f.webkitRelativePath || f.name).replace(/\\/g, '/').substring(root.length);
                const name = SIMS_ROOT + rel;
                store.put({ name: normalize(name), blob: f }, key(name));
            }
            await txDone(tx);
            i += batch.length;
            done += batch.reduce((n, f) => n + f.size, 0);
            setProgress(done, total);
            status(`${i} of ${chosen.length} files (${mb(done)} of ${mb(total)})`);
        }
        return true;
    }

    // ---------- loading ----------

    async function loadSiteContent() {
        let list;
        try {
            const r = await fetch('Content/files.txt', { cache: 'no-cache' });
            if (!r.ok) throw new Error(r.status);
            list = (await r.text()).split(/\r?\n/).map(l => l.trim().replace(/\\/g, '/')).filter(l => l);
        } catch (e) {
            console.warn('[SimsFS] no Content/files.txt', e);
            return;
        }
        // a handful at a time: browsers refuse thousands of downloads at once
        const queue = list.slice();
        async function worker() {
            while (queue.length) {
                const name = 'Content/' + queue.shift();
                for (let attempt = 0; attempt < 3; attempt++) {
                    try {
                        const r = await fetch(name);
                        if (!r.ok) break;
                        const data = new Uint8Array(await r.arrayBuffer());
                        addFile(name, { size: data.length, data });
                        break;
                    } catch (e) {
                        if (attempt === 2) console.warn('[SimsFS] could not load ' + name, e);
                    }
                }
            }
        }
        await Promise.all(Array.from({ length: 8 }, worker));
    }

    function isLazy(k) { return LAZY.some(r => r.test(k)); }

    async function loadStored() {
        const records = await allRecords();
        const sims = records.filter(r => r.k.startsWith(SIMS_ROOT));
        if (!sims.some(r => r.k === '/sims/gamedata/uitext.iff')) return false;

        // what's big in this Sims folder (shown in the console, F12)
        const bySize = {};
        for (const r of sims) {
            const top = r.k.substring(SIMS_ROOT.length).split('/')[0];
            bySize[top] = (bySize[top] || 0) + (r.v.blob ? r.v.blob.size : 0);
        }
        console.log('[SimsFS] folder sizes: ' + Object.entries(bySize).sort((a, b) => b[1] - a[1]).map(([n, b]) => n + ' ' + mb(b)).join(', '));

        const now = records.filter(r => !isLazy(r.k));
        const total = now.reduce((n, r) => n + (r.v.blob ? r.v.blob.size : 0), 0);
        show('Loading your Sims files', { progress: true, again: true });
        Account.show(false);
        try {
            if (sessionStorage.getItem('srx-loading')) status('Last time, loading stopped suddenly - this device may not have enough memory for the game.', true);
            sessionStorage.setItem('srx-loading', '1');
        } catch (e) { }
        let done = 0, count = 0;
        // a few at a time keeps the browser responsive
        const queue = now.slice();
        async function worker() {
            while (queue.length) {
                const r = queue.shift();
                const data = new Uint8Array(await r.v.blob.arrayBuffer());
                addFile(r.v.name, { size: data.length, data });
                done += data.length; count++;
                if (count % 25 === 0 || !queue.length) {
                    setProgress(done, total);
                    status(`${mb(done)} of ${mb(total)}`);
                }
            }
        }
        await Promise.all([worker(), worker(), worker(), worker()]);
        try { sessionStorage.removeItem('srx-loading'); } catch (e) { }
        // music etc: known now, loaded in the background once the game is running
        for (const r of records.filter(r => isLazy(r.k)))
            addFile(r.v.name, { size: r.v.blob.size, data: null, blob: r.v.blob });
        return true;
    }

    function loadLazyInBackground() {
        const waiting = Array.from(files.values()).filter(f => !f.data && f.blob);
        (async () => {
            for (const f of waiting) {
                if (f.data) continue;
                try { f.data = new Uint8Array(await f.blob.arrayBuffer()); f.blob = null; }
                catch (e) { console.warn('[SimsFS] could not load ' + f.name, e); }
            }
        })();
    }

    // ---------- the launcher ----------
    //
    // After logging in (or choosing to play offline): the game's version, patch notes, the account
    // and the game folder. Nothing big loads until Play is pressed.
    let patchNotes = null;
    async function getPatchNotes() {
        if (patchNotes) return patchNotes;
        try {
            const r = await fetch('patchnotes.json', { cache: 'no-cache' });
            patchNotes = r.ok ? await r.json() : null;
        } catch (e) { patchNotes = null; }
        return patchNotes;
    }
    async function hasGameFiles() {
        try {
            const tx = (await openDb()).transaction(STORE, 'readonly');
            return (await req(tx.objectStore(STORE).count(IDBKeyRange.only('/sims/gamedata/uitext.iff')))) > 0;
        } catch (e) { return false; }
    }
    function esc(t) { const d = document.createElement('div'); d.textContent = t == null ? '' : String(t); return d.innerHTML; }
    function patchHtml(p, withVersion) {
        let h = '<div class="patch">';
        if (withVersion) h += '<div class="pv">VERSION ' + esc(p.version) + (p.date ? ' &middot; ' + esc(p.date) : '') + '</div>';
        if (p.features && p.features.length) h += '<div class="cat feat">NEW FEATURES</div><ul>' + p.features.map(x => '<li>' + esc(x) + '</li>').join('') + '</ul>';
        if (p.updates && p.updates.length) h += '<div class="cat upd">UPDATES</div><ul>' + p.updates.map(x => '<li>' + esc(x) + '</li>').join('') + '</ul>';
        return h + '</div>';
    }

    async function launcher(how) {
        const s = screen();
        const L = s.root.querySelector('.launcher');
        s.root.classList.remove('hidden');
        s.root.querySelector('.setup').classList.add('hidden');
        s.root.querySelector('.box').classList.add('wide');
        L.classList.remove('hidden');

        // version and patch notes
        const notes = await getPatchNotes();
        const patches = (notes && notes.patches) || [];
        const latest = patches[0];
        L.querySelector('.stage').textContent = ((notes && notes.stage) || 'Beta').toUpperCase();
        L.querySelector('.vnum').textContent = latest ? latest.version : '?';
        L.querySelector('.ndate').textContent = latest ? 'v' + latest.version + (latest.date ? ' \u00b7 ' + latest.date : '') : '';
        const body = L.querySelector('.nbody');
        body.innerHTML = latest ? patchHtml(latest, false) : '<p>Patch notes are unavailable right now.</p>';
        const older = L.querySelector('.nolder');
        older.classList.toggle('hidden', patches.length < 2);
        older.textContent = 'Show earlier patches';
        older.onclick = () => {
            if (older.dataset.open) { body.innerHTML = patchHtml(latest, false); older.textContent = 'Show earlier patches'; delete older.dataset.open; return; }
            body.innerHTML = patchHtml(latest, false) + patches.slice(1).map(p => patchHtml(p, true)).join('');
            older.textContent = 'Hide earlier patches'; older.dataset.open = '1';
        };

        // the account
        const acc = Account.session;
        const on = how !== 'offline' && !!(acc && acc.token);
        const av = L.querySelector('.avatar');
        av.textContent = on ? (acc.name || '?').charAt(0).toUpperCase() : '?';
        av.classList.toggle('off', !on);
        const role = on ? (acc.role === 'admin' ? 'ADMIN' : acc.role === 'tester' ? 'TESTER' : 'PLAYER') : '';
        L.querySelector('.lname').innerHTML = on ? esc(acc.name) + '<span class="badge">' + role + '</span>' : 'Playing offline';
        const dot = L.querySelector('.dot'), text = L.querySelector('.ltext');
        dot.className = 'dot';
        if (!on) text.textContent = 'Not logged in \u2014 the online town is unavailable';
        else if (Account.reachable === false) { dot.classList.add('warn'); text.textContent = "Logged in \u2014 couldn't reach the server"; }
        else { dot.classList.add('on'); text.textContent = 'Logged in'; }

        // account level and XP (earned playing online)
        const lvBox = L.querySelector('.llevel');
        lvBox.classList.toggle('hidden', !(on && acc.level > 0));
        if (on && acc.level > 0) {
            L.querySelector('.lvtext').innerHTML = 'LEVEL <b>' + (acc.level | 0) + '</b>';
            L.querySelector('.lvbar div').style.width = (acc.next > 0 ? Math.min(100, (acc.xp | 0) / acc.next * 100) : 0).toFixed(1) + '%';
            lvBox.title = (acc.xp | 0) + ' / ' + (acc.next | 0) + ' XP to level ' + ((acc.level | 0) + 1);
        }

        // the game folder (the Sims files and the homes saved in them live in this browser)
        const fstate = L.querySelector('.lfstate');
        const paintFolder = async () => {
            fstate.textContent = (await hasGameFiles()) ? 'Saved in this browser' : "Not set up yet \u2014 you'll choose it after pressing Play";
        };
        await paintFolder();

        // Live and Test: online (green), maintenance (amber) or offline (red), with how many people are
        // on; the chosen one's message of the day sits above the patch notes (every 15 seconds while showing)
        const smotd = L.querySelector('.smotd');
        const statusOf = async (url) => {
            try {
                const ctl = new AbortController(); const t = setTimeout(() => ctl.abort(), 6000);
                const r = await fetch(url + '/api/status', { cache: 'no-store', signal: ctl.signal });
                clearTimeout(t);
                return r.ok ? await r.json() : null;
            } catch (e) { return null; }
        };
        const status = { live: null, test: null };
        const chosen = Servers.channel;
        const paintServers = async () => {
            status.live = await statusOf(Servers.url('live'));
            if (status.live) Servers.learn(status.live.servers);
            status.test = await statusOf(Servers.url('test'));
            for (const ch of ['live', 'test']) {
                const card = L.querySelector('.srv[data-ch="' + ch + '"]'), st = status[ch];
                card.classList.toggle('sel', ch === chosen);
                const dot = card.querySelector('.dot'), state = card.querySelector('.sstate'), count = card.querySelector('.scount');
                dot.className = 'dot';
                if (!st) { dot.classList.add('down'); state.textContent = 'Offline'; count.textContent = ''; continue; }
                if (st.town) { dot.classList.add('on'); state.textContent = 'Online'; }
                else { dot.classList.add('warn'); state.textContent = 'Maintenance'; }
                count.textContent = (st.online | 0) + ' online' + ((st.playing | 0) > 0 ? ' \u00b7 ' + (st.playing | 0) + ' in a lot' : '');
            }
            const mine = status[chosen];
            const evs = (mine && Array.isArray(mine.events)) ? mine.events : [];
            const mt = L.querySelector('.mtext');
            mt.textContent = (mine && mine.motd) || '';
            for (const ev of evs) {
                const d = document.createElement('div');
                d.className = 'mevent';
                let until = '';
                try { until = ' \u2014 until ' + new Date(ev.ends).toLocaleString([], { weekday: 'short', hour: 'numeric', minute: '2-digit' }); } catch (e) { }
                d.textContent = '\u2605 EVENT: ' + ev.title + until;
                mt.appendChild(d);
            }
            smotd.classList.toggle('hidden', !(mine && (mine.motd || evs.length)));
            checkVersion();
        };

        // the version check: this website's game, the newest patch notes, and the chosen server's game
        const play = L.querySelector('.play'), vcheck = L.querySelector('.vcheck');
        let webVersion = null;
        try { webVersion = DotNet.invokeMethod('SimsRedux.Web', 'SimsReduxVersion'); } catch (e) { webVersion = null; }
        const newer = (a, b) => { // is version a newer than b?
            const x = String(a).split('.').map(n => parseInt(n, 10) || 0), y = String(b).split('.').map(n => parseInt(n, 10) || 0);
            for (let i = 0; i < Math.max(x.length, y.length); i++) { if ((x[i] || 0) !== (y[i] || 0)) return (x[i] || 0) > (y[i] || 0); }
            return false;
        };
        if (webVersion) L.querySelector('.vnum').textContent = webVersion;
        let needsUpdate = false;
        const checkVersion = () => {
            const srv = status[chosen] && status[chosen].game;
            const site = latest && latest.version;
            needsUpdate = !!webVersion && ((site && newer(site, webVersion)) || (srv && newer(srv, webVersion)));
            vcheck.className = 'vcheck';
            if (!webVersion) { vcheck.textContent = ''; }
            else if (needsUpdate) { vcheck.classList.add('warn'); vcheck.textContent = 'A new version is out (v' + (srv && newer(srv, site || '0') ? srv : site) + ') \u2014 you have v' + webVersion + '.'; }
            else if (srv && newer(webVersion, srv)) { vcheck.classList.add('warn'); vcheck.textContent = 'Up to date (v' + webVersion + '). The server is still on v' + srv + ' \u2014 online play waits for it to update; single player is fine.'; }
            else { vcheck.classList.add('ok'); vcheck.textContent = '\u2713 Up to date \u00b7 v' + webVersion; }
            play.textContent = needsUpdate ? 'UPDATE' : 'PLAY';
        };
        paintServers();
        const serverTimer = setInterval(paintServers, 15000);

        return new Promise(done => {
            const btns = L.querySelector('.lbtns');
            btns.innerHTML = on ? '<button class="ghost lrep">Report a player</button><button class="ghost lswitch">Switch account</button><button class="ghost llogout">Log out</button>'
                : '<button class="ghost llogin">Log in</button>';
            const rep = L.querySelector('.lreport'), rmsg = L.querySelector('.rmsg');
            rep.classList.add('hidden');
            if (on) {
                btns.querySelector('.lrep').onclick = () => { rep.classList.toggle('hidden'); rmsg.textContent = ''; L.querySelector('.rabout').focus(); };
                L.querySelector('.rcancel').onclick = () => rep.classList.add('hidden');
                L.querySelector('.rsend').onclick = async () => {
                    const about = L.querySelector('.rabout').value.trim(), reason = L.querySelector('.rreason').value.trim();
                    rmsg.className = 'rmsg';
                    if (!about || reason.length < 5) { rmsg.classList.add('error'); rmsg.textContent = 'Say who it is and what happened.'; return; }
                    rmsg.textContent = 'Sending...';
                    const r = await Account.call('/api/report', { about, reason });
                    if (!r.ok) { rmsg.classList.add('error'); rmsg.textContent = r.error || 'It could not be sent.'; return; }
                    L.querySelector('.rabout').value = ''; L.querySelector('.rreason').value = '';
                    rmsg.textContent = 'Thanks - your report went to the admin.';
                };
            }
            const finish = (r) => { clearInterval(serverTimer); document.removeEventListener('keydown', onKey); done(r); };
            const leave = async (focus) => {
                await Account.logout();
                finish('account');
                if (focus) setTimeout(() => { const n = s.root.querySelector('.aname'); if (n) n.focus(); }, 0);
            };
            if (on) {
                btns.querySelector('.lswitch').onclick = () => leave(true);
                btns.querySelector('.llogout').onclick = () => leave(false);
            } else btns.querySelector('.llogin').onclick = () => finish('account');
            L.querySelector('.lchange').onclick = async () => {
                if (!(await hasGameFiles())) { alert("There's no game folder yet - you'll choose one after pressing Play."); return; }
                if (!confirm('Change the game folder? The Sims files in this browser are removed (homes saved in the game go with them), and you choose a folder again after pressing Play.')) return;
                try { await saveChain; await clearSims(); } catch (e) { console.warn('[SimsFS] could not clear', e); }
                await paintFolder();
            };
            for (const card of L.querySelectorAll('.srv'))
                card.onclick = () => {
                    const ch = card.dataset.ch;
                    if (ch === chosen) return;
                    if (ch === 'test' && !confirm('Switch to the Test server? It has its own accounts and town, and gets new things first (they may be unfinished).')) return;
                    Servers.channel = ch;
                    finish('switch');
                };
            play.disabled = false;
            play.onclick = async () => {
                play.disabled = true;
                if (!needsUpdate) { finish('play'); return; }
                // fetch the newest website: clear what the browser kept of the old one, then reload
                vcheck.className = 'vcheck'; vcheck.textContent = 'Updating...';
                try { if (window.caches) for (const k of await caches.keys()) await caches.delete(k); } catch (e) { }
                try { if (navigator.serviceWorker) for (const r of await navigator.serviceWorker.getRegistrations()) await r.unregister(); } catch (e) { }
                finish('update');
            };
            const onKey = (e) => {
                if (e.key !== 'Enter' || L.classList.contains('hidden')) return;
                const t = e.target && e.target.tagName;
                if (t === 'INPUT' || t === 'TEXTAREA' || t === 'BUTTON') return;
                play.click();
            };
            document.addEventListener('keydown', onKey);
        });
    }

    // Shows the setup screen as needed; resolves once the game can start.
    async function prepare() {
        const build = (document.querySelector('meta[name="srx-build"]') || {}).content || 'dev';
        console.log('[SimsRedux] web build ' + build);
        await openDb();
        screen().root.querySelector('.signout').onclick = async () => {
            try { await Account.call('/api/logout', {}); } catch (e) { }
            try { await Account.logout(); } catch (e) { }
            location.reload();
        };
        screen().change.onclick = async () => {
            if (!confirm('Choose a different Sims folder? Your current Sims files will be removed from this browser (homes saved in the game go with them).')) return;
            await clearSims();
            location.reload();
        };

        const contentReady = loadSiteContent();

        // 1. log in, make an account, or play offline (skipped when a login is remembered),
        //    then the launcher - nothing loads until Play
        let how;
        for (;;) {
            show(Servers.channel === 'test' ? 'Welcome! (Test server)' : 'Welcome!', {});
            Account.show(true);
            how = await Account.step();
            const r = await launcher(how);
            if (r === 'play') break;
            if (r === 'switch' || r === 'update') { location.reload(); return new Promise(() => { }); }
        }
        console.log('[SimsRedux] ' + (how === 'offline' ? 'playing offline' : 'logged in as ' + Account.session.name));

        // 2. the game files: already in this browser, or picked / downloaded now
        let loaded = false;
        try { loaded = await loadStored(); }
        catch (e) { console.warn('[SimsFS] stored files could not be read', e); }

        let problem = '';
        try {
            if (sessionStorage.getItem('srx-downloading')) {
                sessionStorage.removeItem('srx-downloading');
                problem = 'The last download stopped suddenly - the browser most likely ran out of memory and reloaded the page. ' +
                    'Phones (iPhones especially) may not have enough room for The Sims in a browser.';
                await clearSims();
            }
        } catch (e) { }
        while (!loaded) {
            show('Welcome!', { pick: true });
            Account.show(true);
            Account.refresh();
            if (problem) status(problem, true);
            const choice = await Promise.race([
                waitForFolder().then(list => ({ list })),
                new Promise(ok => { screen().fromServer.onclick = () => ok({ server: true }); }),
            ]);
            try {
                if (choice.server) {
                    await downloadFromServer();
                    loaded = await loadStored();
                    if (!loaded) problem = 'The game files could not be read back. Please try again.';
                    continue;
                }
                const list = choice.list;
                if (!await copyFolder(list)) {
                    problem = "That folder doesn't have The Sims in it (no GameData\\UIText.iff was found). Pick the folder that has GameData inside.";
                    continue;
                }
                loaded = await loadStored();
                if (!loaded) problem = 'Your Sims files could not be read back. Please try again.';
            } catch (e) {
                console.error(e);
                problem = (choice.server ? 'Getting the files from the server failed: ' : 'Copying failed: ') + (e && e.message ? e.message : e) +
                    (choice.server ? '. Try again, or pick your own Sims folder.' : '. Make sure there is enough free disk space and try again.');
            }
        }
        await contentReady;
        screen().root.classList.add('hidden');
        loadLazyInBackground();
    }

    // ---------- what the game calls (synchronously) ----------

    function fileExists(path) { return files.has(key(path)); }
    function dirExists(path) {
        const k = key(path);
        return k === '' || k === '/' || dirs.has(k);
    }
    // size, or -1 if missing
    function fileSize(path) {
        const f = files.get(key(path));
        return f ? f.size : -1;
    }
    // copies part of a file into the game's memory; returns bytes read, -1 missing, -2 still loading
    function read(path, offset, view) {
        const f = files.get(key(path));
        if (!f) return -1;
        if (!f.data) {
            if (f.blob && !f.loading) {
                f.loading = true;
                f.blob.arrayBuffer().then(b => { f.data = new Uint8Array(b); f.blob = null; });
            }
            return -2;
        }
        const n = Math.max(0, Math.min(view.length, f.data.length - offset));
        if (n > 0) view.set(f.data.subarray(offset, offset + n), 0);
        return n;
    }
    function write(path, view) {
        const data = view.slice();
        const k = addFile(path, { size: data.length, data });
        persist(k, files.get(k));
    }
    function deleteFile(path) {
        const k = key(path);
        if (files.delete(k)) unpersist(k);
    }
    function createDir(path) {
        const k = key(path);
        if (k && k !== '/') { dirs.set(k, normalize(path)); addDirsFor(normalize(path)); }
    }
    function deleteDir(path, recursive) {
        const k = key(path);
        const prefix = k + '/';
        for (const fk of Array.from(files.keys()))
            if (fk.startsWith(prefix)) { if (!recursive) return; deleteFile(fk); }
        for (const d of Array.from(dirs.keys())) if (d === k || d.startsWith(prefix)) dirs.delete(d);
    }
    function moveFile(from, to, overwrite) {
        const fk = key(from), tk = key(to);
        const f = files.get(fk);
        if (!f) return false;
        if (files.has(tk) && !overwrite) return false;
        if (!f.data && f.blob) return false;
        files.delete(fk); unpersist(fk);
        const k = addFile(to, { size: f.size, data: f.data });
        persist(k, files.get(k));
        return true;
    }
    function patternRegex(pattern) {
        if (!pattern || pattern === '*' || pattern === '*.*') return null;
        const esc = pattern.replace(/[.+^${}()|[\]\\]/g, '\\$&').replace(/\*/g, '.*').replace(/\?/g, '.');
        return new RegExp('^' + esc + '$', 'i');
    }
    // lists files (or folders) in a folder; results start with the folder exactly as the game wrote it
    function list(path, pattern, recursive, folders) {
        const k = key(path);
        const prefix = (k === '/' ? '/' : k + '/');
        const shown = String(path).replace(/[\\/]+$/, '');
        const re = patternRegex(pattern);
        const out = [];
        const source = folders ? dirs : files;
        for (const [ik, v] of source) {
            if (!ik.startsWith(prefix)) continue;
            const rest = ik.substring(prefix.length);
            if (!rest || (!recursive && rest.includes('/'))) continue;
            const full = folders ? v : v.name;
            // keep the real capitalisation of the part below the folder
            const realRest = full.substring(full.length - rest.length);
            const base = realRest.substring(realRest.lastIndexOf('/') + 1);
            if (re && !re.test(base)) continue;
            out.push(shown + '/' + realRest);
        }
        return out;
    }

    // ---- called from the game's Options (Play > Sims Folder, Graphics > Screen) ----
    async function changeFolder() {
        try { await saveChain; await clearSims(); } catch (e) { console.warn('[SimsFS] could not clear', e); }
        location.reload();
    }
    function setFullScreen(on) {
        try {
            if (on && !document.fullscreenElement) document.documentElement.requestFullscreen().catch(e => console.warn('[SimsRedux] full screen refused', e));
            else if (!on && document.fullscreenElement) document.exitFullscreen();
        } catch (e) { console.warn('[SimsRedux] full screen', e); }
    }
    function isFullScreen() { return !!document.fullscreenElement; }

    window.SimsAccount = { get server() { return Account.SERVER; }, get session() { return Account.session; }, refresh: () => Account.refresh() };
    // the game's online town reads who's logged in here (WebPlatform.cs)
    const accountServer = () => Account.SERVER;
    const accountToken = () => (Account.session && Account.session.token) || '';
    const accountName = () => (Account.session && Account.session.name) || '';
    window.SimsFS = { accountServer, accountToken, accountName, changeFolder, setFullScreen, isFullScreen, prepare, fileExists, dirExists, fileSize, read, write, deleteFile, createDir, deleteDir, moveFile, list, normalize };
})();
