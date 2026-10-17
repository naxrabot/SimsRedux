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
        try { if (typeof Saves !== 'undefined' && Saves.isSave(k)) Saves.changed(); } catch (e) { }
        const blob = new Blob([entry.data]);
        saveChain = saveChain.then(async () => {
            const tx = (await openDb()).transaction(STORE, 'readwrite');
            tx.objectStore(STORE).put({ name: entry.name, blob }, k);
            await txDone(tx);
        }).catch(e => console.warn('[SimsFS] could not save ' + k, e));
    }
    function unpersist(k) {
        try { if (typeof Saves !== 'undefined' && Saves.isSave(k)) Saves.changed(); } catch (e) { }
        saveChain = saveChain.then(async () => {
            const tx = (await openDb()).transaction(STORE, 'readwrite');
            tx.objectStore(STORE).delete(k);
            await txDone(tx);
        }).catch(e => console.warn('[SimsFS] could not delete ' + k, e));
    }
    async function clearSims() {
        const tx = (await openDb()).transaction(STORE, 'readwrite');
        tx.objectStore(STORE).delete(IDBKeyRange.bound(SIMS_ROOT, SIMS_ROOT + '\uffff'));
        tx.objectStore(STORE).delete(PENDING_KEY);
        await txDone(tx);
    }
    // while a download from the server is unfinished (the files so far are kept, so it can carry on)
    const PENDING_KEY = '/meta/download-pending';
    async function setPending(on) {
        const tx = (await openDb()).transaction(STORE, 'readwrite');
        if (on) tx.objectStore(STORE).put({ name: PENDING_KEY, since: Date.now() }, PENDING_KEY);
        else tx.objectStore(STORE).delete(PENDING_KEY);
        await txDone(tx);
    }

    // ---------- the setup / loading screen ----------

    let ui = null;
    function screen() {
        if (ui) return ui;
        const style = document.createElement('style');
        style.textContent = `
#srx-setup { position: fixed; inset: 0; z-index: 1000; color: #d7d7f5;
  /* the landing page picture (scattered Sims photos), filling the screen; dark brown behind it while it loads */
  background: #3a2a22 url(img/redux-landing.jpg) center / cover no-repeat; overflow: auto;
  font-family: 'Segoe UI', Tahoma, sans-serif; display: flex; align-items: center; justify-content: center; padding: 16px; }
#srx-setup .box { width: 100%; max-width: 560px; background: rgba(0, 0, 58, .92); max-height: calc(100vh - 32px); max-height: calc(100dvh - 32px); overflow: auto; border: 2px solid #7878c8; border-right-color: #000028;
  border-bottom-color: #000028; box-shadow: 6px 6px 0 rgba(0,0,0,.35); padding: 24px 26px; }
#srx-setup h1 { font-size: 26px; margin: 0 0 4px; color: #fff; font-weight: 600; }
#srx-setup h1 .logoimg { display: block; width: 220px; max-width: 70%; margin: 0 auto 6px; }
#srx-setup .logo .logoimg { display: block; width: 300px; max-width: 80%; height: auto; margin: 0 auto 4px; filter: drop-shadow(0 4px 10px rgba(0,0,0,.45)); }
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
#srx-setup .acct .row.signup { margin-top: 8px; }
#srx-setup .acct .row.signup.hidden { display: none; }
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
  display: flex; flex-direction: column; overflow-x: hidden; overflow-y: auto; } /* (scrolls when it can't fit, e.g. on a phone: Play stays reachable) */
/* the launcher always fills the height of the window (the patch notes take the spare room) */
@media (min-width: 561px) { #srx-setup .box.wide { height: calc(100vh - 32px); height: calc(100dvh - 32px); } }
#srx-setup .launcher { display: flex; flex-direction: column; min-height: 0; flex: 1 1 auto; }
#srx-setup .launcher.hidden { display: none; }
#srx-setup .l-top { padding: 28px 30px 18px; text-align: center; background: linear-gradient(rgba(120,255,140,.07), transparent); }
#srx-setup .logo { font-size: 38px; font-weight: 800; letter-spacing: .14em; color: #fff; line-height: 1; text-shadow: 0 2px 18px rgba(80,140,255,.45); }
#srx-setup .logo span { color: #78ff8c; }
#srx-setup .logo img { max-width: 100%; max-height: 190px; }
#srx-setup .ver { margin-top: 10px; font-size: 12px; letter-spacing: .12em; text-transform: uppercase; color: #8a96d0; }
#srx-setup .ver b { color: #fff; font-weight: 600; }
#srx-setup .ver .stage { color: #04102a; background: #78ff8c; font-weight: 700; padding: 1px 6px; margin-right: 8px; letter-spacing: .1em; }
#srx-setup .l-acct { margin: 0 30px; padding: 14px 16px; border: 1px solid rgba(143,180,255,.2); background: rgba(0,0,0,.25);
  display: grid; grid-template-columns: auto minmax(150px, 1fr) auto; gap: 4px 14px; align-items: center; }
#srx-setup .avatar { grid-row: span 2; width: 44px; height: 44px; border-radius: 50%; display: grid; place-items: center; font-weight: 700; font-size: 19px;
  color: #04102a; background: linear-gradient(135deg, #9fffb0, #3bc25a); }
#srx-setup .avatar.off { background: #2a335e; color: #9fb4ff; }
/* the account menu: the round initial opens it */
#srx-setup .l-acct { position: relative; }
#srx-setup button.avatar { margin: 0; padding: 0; border: none; position: relative; cursor: pointer; font: inherit; font-weight: 700; font-size: 19px; color: #04102a;
  background: linear-gradient(135deg, #9fffb0, #3bc25a); box-shadow: 0 0 0 0 rgba(120,255,140,0); transition: box-shadow .15s; }
#srx-setup button.avatar:not(:disabled):hover, #srx-setup button.avatar[aria-expanded="true"] { background: linear-gradient(135deg, #b8ffc5, #4fd56d); box-shadow: 0 0 0 3px rgba(120,255,140,.35); }
#srx-setup button.avatar::after { content: ''; position: absolute; right: -2px; bottom: -2px; width: 16px; height: 16px; border-radius: 50%;
  background: #0b1238 url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 16 16'%3E%3Cpath d='M4.5 6.5l3.5 3.5 3.5-3.5' fill='none' stroke='%23c8d4ff' stroke-width='1.8' stroke-linecap='round' stroke-linejoin='round'/%3E%3C/svg%3E") center/12px no-repeat;
  border: 1px solid rgba(143,180,255,.5); }
#srx-setup button.avatar[aria-expanded="true"]::after { transform: rotate(180deg); }
#srx-setup button.avatar:disabled { cursor: default; } #srx-setup button.avatar:disabled::after { display: none; }
#srx-setup button.avatar.off, #srx-setup button.avatar.off:hover { background: #2a335e; color: #9fb4ff; box-shadow: none; }
#srx-setup .lbtns.menu { position: absolute; z-index: 5; top: 66px; left: 12px; grid-row: auto; grid-column: auto; margin: 0; max-width: none; min-width: 210px;
  flex-direction: column; flex-wrap: nowrap; gap: 2px; justify-content: flex-start; padding: 6px; background: #0b1238; border: 1px solid rgba(143,180,255,.35);
  box-shadow: 0 14px 34px rgba(0,0,0,.55); }
#srx-setup .lbtns.menu:not(.open) { display: none; }
#srx-setup .l-acct .lname { grid-column: 2; grid-row: 1; } #srx-setup .l-acct .lstatus { grid-column: 2; grid-row: 2; }
#srx-setup .lbtns.menu button.ghost { border: none; text-align: left; padding: 9px 12px; font-size: 13px; width: 100%; }
#srx-setup .lbtns.menu button.ghost.danger { color: #ffb0a0; }
#srx-setup .lbtns.menu .lfolder.inmenu { grid-column: auto; margin: 0; padding: 8px 12px 6px; border-top: none; display: block; font-size: 12px; line-height: 1.5; max-width: 260px; }
#srx-setup .lbtns.menu .lfolder.inmenu b { display: block; }
#srx-setup .lbtns.menu .lfolder.inmenu button.link { padding: 0; margin-top: 4px; }
#srx-setup .lbtns.menu hr { border: none; border-top: 1px solid rgba(143,180,255,.15); margin: 4px 2px; }
#srx-setup .lname { font-size: 16px; font-weight: 600; color: #fff; overflow-wrap: anywhere; }
#srx-setup .badge { font-size: 10px; letter-spacing: .1em; font-weight: 700; color: #9fb4ff; border: 1px solid #5a6ab8; padding: 1px 5px; margin-left: 6px; vertical-align: 2px; }
#srx-setup .lstatus { font-size: 12px; color: #8a96d0; display: flex; align-items: center; gap: 6px; white-space: nowrap; }
#srx-setup .dot { width: 8px; height: 8px; border-radius: 50%; background: #6a6a8a; flex: none; }
#srx-setup .dot.on { background: #78ff8c; box-shadow: 0 0 8px #78ff8c; }
#srx-setup .dot.warn { background: #ffb547; }
#srx-setup .lbtns { grid-row: span 2; max-width: 400px; display: flex; gap: 8px; flex-wrap: wrap; justify-content: flex-end; }
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
#srx-setup .vcheck .vsoon { color: #8f93ad; font-style: italic; margin-top: 3px; }
#srx-setup .vcheck .vrefresh { font-size: 12px; margin-top: 4px; }
#srx-setup .sitem .k { font-size: 10px; letter-spacing: .16em; font-weight: 700; color: #8a96d0; margin-bottom: 4px; }
#srx-setup .sitem .v { font-size: 15px; font-weight: 600; color: #fff; display: flex; align-items: center; gap: 8px; }
#srx-setup .sitem .v small { font-size: 12px; font-weight: 400; color: #8a96d0; margin-left: auto; }
#srx-setup .smotd { margin: 12px 30px 0; font-size: 13.5px; color: #e4e9ff; padding: 10px 14px; border-left: 3px solid #78ff8c; background: rgba(120,255,140,.06); }
#srx-setup .smotd .mevent { margin-top: 6px; font-weight: 700; color: #ffd36b; letter-spacing: .04em; }
#srx-setup .smotd .k { font-size: 10px; letter-spacing: .16em; font-weight: 700; color: #78ff8c; margin-bottom: 4px; }
#srx-setup .modbox { margin: 16px 30px 0; flex: 1 1 auto; min-height: 120px; overflow: auto; border-top: 1px solid rgba(143,180,255,.2); padding-top: 14px; }
#srx-setup .modbox.hidden { display: none; }
#srx-setup .modbox .nhead button.link { font-size: 11px; letter-spacing: 0; font-weight: 400; }
#srx-setup .mtabs { display: flex; gap: 6px; flex-wrap: wrap; margin-bottom: 12px; }
#srx-setup .mtabs button { font-size: 12px; padding: 5px 11px; background: transparent; border: 1px solid rgba(143,180,255,.35); color: #c8d4ff; font-weight: 600; }
#srx-setup .mtabs button.sel { border-color: #78ff8c; color: #78ff8c; background: rgba(120,255,140,.08); }
#srx-setup .mbody textarea, #srx-setup .mbody input, #srx-setup .mbody select { font: inherit; font-size: 13px; color: #fff; background: #000052; border: 1px solid #5a5aa8; padding: 7px 9px; }
#srx-setup .mbody textarea { width: 100%; box-sizing: border-box; resize: vertical; }
#srx-setup .mbody .mrow { display: flex; gap: 8px; flex-wrap: wrap; align-items: center; margin-top: 8px; }
#srx-setup .mbody .mrow input { flex: 1 1 140px; min-width: 0; }
#srx-setup .mbody .mrow input.short { flex: 0 0 80px; }
#srx-setup .mbody button { font-size: 13px; padding: 7px 14px; margin: 0; }
#srx-setup .mitem { border: 1px solid rgba(143,180,255,.15); background: rgba(0,0,0,.2); padding: 9px 12px; margin-bottom: 8px; display: flex; gap: 12px; align-items: flex-start; }
#srx-setup .mitem .t { flex: 1; font-size: 13px; line-height: 1.45; overflow-wrap: anywhere; }
#srx-setup .mitem .t small { display: block; color: #8a96d0; font-size: 11.5px; }
#srx-setup .mitem button { font-size: 11px; padding: 4px 9px; }
#srx-setup .mempty { color: #8a96d0; font-size: 13px; padding: 6px 0; }
#srx-setup .mmsg { font-size: 12px; color: #78ff8c; min-height: 16px; margin-top: 8px; }
#srx-setup .mmsg.error { color: #ff9a8a; }
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
#srx-setup .l-bottom { padding: 18px 30px 22px; position: sticky; bottom: 0; z-index: 2; margin-top: auto; background: rgb(5, 9, 38); }
#srx-setup .l-bottom::before { content: ''; position: absolute; left: 0; right: 0; bottom: 100%; height: 22px; pointer-events: none; background: linear-gradient(rgba(5, 9, 38, 0), rgb(5, 9, 38)); } /* (Play stays on screen; the page scrolls behind it) */
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
  #srx-setup .l-srv { margin: 10px 16px 0; } #srx-setup .smotd { margin: 12px 16px 0; } #srx-setup .notes, #srx-setup .modbox { margin: 14px 16px 0; flex: none; } /* (the whole launcher scrolls on a phone, not a sliver of it) */ #srx-setup .l-bottom { padding: 10px 16px max(14px, env(safe-area-inset-bottom)); } #srx-setup .vcheck { margin-bottom: 8px; } #srx-setup button.play { padding: 13px; } #srx-setup .notes, #srx-setup .modbox { padding-bottom: 8px; }
}`;
        document.head.appendChild(style);
        const el = document.createElement('div');
        el.id = 'srx-setup';
        el.innerHTML = `<div class="box">
<div class="launcher hidden">
  <div class="l-top">
    <div class="logo"><img class="logoimg" src="img/redux-logo.png" alt="The Sims Redux"></div>
    <div class="ver"><span class="stage">BETA</span>Version <b class="vnum">&mdash;</b></div>
  </div>
  <div class="l-acct">
    <button class="avatar" type="button" aria-haspopup="menu" aria-expanded="false" title="Account">?</button>
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
  <div class="modbox hidden">
    <div class="nhead">MODERATOR TOOLS <button class="link mback">Back to patch notes</button></div>
    <div class="mtabs"></div>
    <div class="mbody"></div>
    <div class="mmsg"></div>
  </div>
  <div class="notes">
    <div class="nhead">PATCH NOTES <span class="ndate"></span></div>
    <div class="nbody"></div>
    <button class="link nolder hidden">Show earlier patches</button>
  </div>
  <div class="l-bottom">
    <div class="vcheck"></div>
    <button class="play">PLAY</button>
    <div class="l-foot">build ${esc(buildName())}</div>
  </div>
</div>
<div class="setup">
  <h1><img class="logoimg" src="img/redux-logo.png" alt="The Sims Redux"></h1>
  <h2 class="title"></h2>
  <div class="acct">
    <div class="out">
      <div class="row"><input class="aname" type="text" autocomplete="username" placeholder="Account name" maxlength="20" spellcheck="false">
        <input class="apw" type="password" autocomplete="current-password" placeholder="Password"></div>
      <div class="row signup hidden">
        <input class="areal" type="text" autocomplete="name" placeholder="Your real name" maxlength="60" spellcheck="false">
        <input class="aemail" type="email" autocomplete="email" placeholder="Email (for password resets)" maxlength="120" spellcheck="false"></div>
      <button class="login">Log in</button> <button class="second register">Create account</button>
      <div class="small">You need an approved account in order to play. New accounts must wait for approval.</div>
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
  <div style="margin-top:14px;font-size:11px;color:#7878b8">build ${esc(buildName())}</div>
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
    // the website build (index.html's srx-build, filled in when the site is published)
    function buildName() { return (document.querySelector('meta[name="srx-build"]') || {}).content || 'dev'; }

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
    // ?server=... in the address replaces the Live server, for trying a server on this PC. Only this PC,
    // the home network or an mbrew.studio address is accepted - a link pointing anywhere else would
    // otherwise get the player's password typed into (and their login sent to) someone else's server.
    const FORCED = (function () {
        const v = new URLSearchParams(location.search).get('server');
        if (!v) return null;
        try {
            const u = new URL(v);
            const h = u.hostname.toLowerCase();
            const local = h === 'localhost' || h === '[::1]' || /^127\./.test(h) || /^10\./.test(h) || /^192\.168\./.test(h) || /^172\.(1[6-9]|2\d|3[01])\./.test(h);
            const ours = u.protocol === 'https:' && (h === 'mbrew.studio' || h.endsWith('.mbrew.studio'));
            if ((u.protocol === 'https:' || u.protocol === 'http:') && (local || ours)) return u.origin;
        } catch (e) { }
        console.warn('[SimsRedux] ?server= ignored: only this PC, the home network or mbrew.studio servers can be used');
        return null;
    })();
    const Servers = {
        get channel() { return lsGet('srx-channel', 'live') === 'test' ? 'test' : 'live'; },
        set channel(c) { lsSet('srx-channel', c === 'test' ? 'test' : null); },
        url(c) { return c === 'test' ? lsGet('srx-test-url', 'https://tsr-test.mbrew.studio') : (FORCED || LIVE_URL); },
        // (a server picked with ?server= doesn't get to change where the Test server is)
        learn(servers) { if (!FORCED && servers && typeof servers.test === 'string' && /^https:\/\//.test(servers.test)) lsSet('srx-test-url', servers.test.replace(/\/+$/, '')); },
    };
    const Account = (function () {
        const SERVER = (FORCED || Servers.url(Servers.channel)).replace(/\/+$/, '');
        // each server keeps its own login in this browser (a ?server= one too, so a login is only ever sent where it was made)
        const KEY = FORCED ? (FORCED === LIVE_URL ? 'srx-account' : 'srx-account@' + FORCED)
            : Servers.channel === 'test' ? 'srx-account-test' : 'srx-account';
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
                return { ok: false, status: 0, error: "Couldn't reach the server. It may be offline right now - try again in a moment." };
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
            const signup = root.querySelector('.signup');
            // a new account also asks for the person's real name (so the admin knows who to approve)
            // and their email (for password resets): the first press of Create account shows those
            if (register && signup.classList.contains('hidden')) {
                signup.classList.remove('hidden');
                msg('Fill in your real name and email too, then press Create account again.');
                root.querySelector(name ? '.areal' : '.aname').focus();
                return;
            }
            if (!register) signup.classList.add('hidden');
            if (!name || !password) { msg('Type a name and a password.', true); return; }
            const realName = root.querySelector('.areal').value.trim(), email = root.querySelector('.aemail').value.trim();
            if (register && realName.length < 2) { msg('Please enter your real name, so the admin knows who you are.', true); return; }
            if (register && !/^[^@\s]+@[^@\s]+\.[^@\s]{2,}$/.test(email)) { msg('Please enter a valid email address (used if you forget your password).', true); return; }
            msg(register ? 'Making your account...' : 'Logging in...');
            const r = await call(register ? '/api/register' : '/api/login', register ? { name, password, realName, email } : { name, password });
            if (!r.ok) { msg(r.error, true); return; }
            root.querySelector('.apw').value = '';
            // new accounts wait for the server owner to approve them
            if (r.pending) {
                signup.classList.add('hidden');
                root.querySelector('.areal').value = ''; root.querySelector('.aemail').value = '';
                msg('Account made. Please wait for admin approval.');
                alert(r.message || 'Please wait for admin approval before connecting.');
                return;
            }
            store({ token: r.token, name: r.name, role: r.role, files: !!r.files, powers: Array.isArray(r.powers) ? r.powers : [] });
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
            const token = session.token;
            const r = await call('/api/me');
            // logged out, or in as someone else, while the server was answering: that answer is stale
            if (!session || session.token !== token) return;
            if (r.ok) store({ token: session.token, name: r.name, role: r.role, files: !!r.files, level: r.level | 0, xp: r.xp | 0, next: r.next | 0, powers: Array.isArray(r.powers) ? r.powers : [] });
            else if (r.status === 401) { store(null); msg('Please log in again.'); }
            else paint(); // server offline: keep the remembered login
        }
        // The first step on every visit: resolves once the player is logged in with an approved account
        // (there's no playing without one - accounts are approved by the admin).
        let choose = null;
        async function step() {
            await refresh();
            if (session && session.token) return 'account';
            return new Promise(ok => { choose = ok; });
        }
        function attach(el) {
            root = el.querySelector('.acct');
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


    // ---------- problems ----------
    //
    // When the game stops on someone's device (or the page is reloaded part way through loading,
    // which is what a phone does when it runs out of memory), what happened is sent to the server's
    // log - so a problem on a phone can be looked into without plugging the phone into a computer.
    // Only the problem and the kind of device are sent (never the login).
    const Problems = (function () {
        let sent = 0;
        function device() {
            const n = navigator;
            let d = (n.userAgent || '').replace(/\s+/g, ' ');
            try { d += ' | screen ' + screen.width + 'x' + screen.height + ' @' + (window.devicePixelRatio || 1); } catch (e) { }
            if (n.deviceMemory) d += ' | ' + n.deviceMemory + ' GB';
            return d;
        }
        function report(stage, message) {
            if (sent >= 6 || !Account.session || !Account.session.token) return;
            sent++;
            Account.call('/api/problem', { stage: String(stage || ''), message: String(message || '').slice(0, 1500), device: device() }).catch(() => { });
        }
        window.addEventListener('error', e => report('page error', (e.message || '') + (e.filename ? ' at ' + e.filename.split('/').pop() + ':' + e.lineno : '')));
        window.addEventListener('unhandledrejection', e => report('page error', (e.reason && (e.reason.stack || e.reason.message)) || String(e.reason)));
        // .NET's own "unhandled error" goes to the console
        const consoleError = console.error.bind(console);
        console.error = function () {
            consoleError.apply(console, arguments);
            try {
                const text = Array.from(arguments).map(a => (a && a.stack) || String(a)).join(' ');
                if (/unhandled|exception|crit:|abort|out of memory|RuntimeError/i.test(text)) report('game error', text);
            } catch (e) { }
        };
        return { report, device };
    })();
    window.simsReduxReportProblem = (stage, message) => Problems.report(stage, message);


    // ---------- cross saves ----------
    //
    // The player's single-player game (neighbourhood, houses, Sims - the UserData folders - and most
    // settings) is kept on their account on the server too, so every device they log in on has the
    // same game. Before playing, this device's saves and the account's are compared:
    //   only the account's changed  -> they come down to this device
    //   only this device's changed  -> they go up to the account
    //   both changed                -> the player chooses which to keep (the other is kept as a backup)
    // While playing, the saves go up a few seconds after the game saves. Playing offline just
    // waits: the changes go up next time.
    const Saves = (function () {
        const SAVE_RE = /^\/userdata\/userdata\d*\//;            // (keys are lower-case)
        const SETTINGS_KEY = '/userdata/redux_settings.ini';
        // settings that belong to the device, not the player (they never travel)
        const DEVICE_SETTINGS = ['controls', 'touchlayout', 'uiscale', 'lighting', 'antialias', 'smoothzoom', 'fullscreen'];
        const OWNER = 'srx-saves-owner';
        let enabled = false, timer = null, busy = false, again = false, told = false;

        function isSave(k) { return SAVE_RE.test(k) || k === SETTINGS_KEY; }
        function me() { return ((Account.session && Account.session.name) || '').toLowerCase(); }
        function stateKey() { return 'srx-saves@' + Account.SERVER + '|' + me(); }
        function getState() { try { return JSON.parse(lsGet(stateKey(), 'null')); } catch (e) { return null; } }
        function setState(st) { lsSet(stateKey(), st ? JSON.stringify(st) : null); }
        function device() {
            const ua = navigator.userAgent || '';
            if (/iPad/.test(ua) || (/Macintosh/.test(ua) && (navigator.maxTouchPoints || 0) > 1)) return 'iPad';
            if (/iPhone/.test(ua)) return 'iPhone';
            if (/Android/.test(ua)) return /Mobile/.test(ua) ? 'Android phone' : 'Android tablet';
            if (/Windows/.test(ua)) return 'Windows PC';
            if (/Macintosh/.test(ua)) return 'Mac';
            if (/CrOS/.test(ua)) return 'Chromebook';
            if (/Linux/.test(ua)) return 'Linux PC';
            return 'a device';
        }
        const te = new TextEncoder(), td = new TextDecoder();
        function settingLines(text) { return String(text || '').split(/\r?\n/).filter(l => l.indexOf('=') > 0); }
        function isDeviceLine(l) { return DEVICE_SETTINGS.includes(l.substring(0, l.indexOf('=')).trim().toLowerCase()); }
        function shared(data) { return te.encode(settingLines(td.decode(data)).filter(l => !isDeviceLine(l)).join('\r\n')); }

        // the saves as one bundle: "SRXS", the list of files (JSON), then the files one after another
        function pack(list) {
            const items = list.map(f => ({ name: f.name, data: key(f.name) === SETTINGS_KEY ? shared(f.data) : f.data }))
                .sort((a, b) => (a.name.toLowerCase() < b.name.toLowerCase() ? -1 : a.name.toLowerCase() > b.name.toLowerCase() ? 1 : 0));
            const head = te.encode(JSON.stringify({ v: 1, files: items.map(f => [f.name, f.data.length]) }));
            const total = 8 + head.length + items.reduce((n, f) => n + f.data.length, 0);
            const out = new Uint8Array(total);
            out.set(te.encode('SRXS'), 0);
            new DataView(out.buffer).setUint32(4, head.length, true);
            out.set(head, 8);
            let at = 8 + head.length;
            for (const f of items) { out.set(f.data, at); at += f.data.length; }
            return out;
        }
        function unpack(u8) {
            if (u8.length < 8 || td.decode(u8.subarray(0, 4)) !== 'SRXS') throw new Error('not a saves bundle');
            const len = new DataView(u8.buffer, u8.byteOffset).getUint32(4, true);
            const head = JSON.parse(td.decode(u8.subarray(8, 8 + len)));
            let at = 8 + len;
            const out = [];
            for (const [name, size] of head.files) {
                const n = normalize(String(name));
                // only ever a save's own place (never somewhere else in this browser's storage)
                if (!isSave(key(n))) throw new Error('unexpected file in the saves: ' + n);
                out.push({ name: n, data: u8.slice(at, at + size) });
                at += size;
            }
            return out;
        }
        async function through(u8, stream) {
            const piped = new Blob([u8]).stream().pipeThrough(stream);
            return new Uint8Array(await new Response(piped).arrayBuffer());
        }
        const gzip = u8 => through(u8, new CompressionStream('gzip'));
        const gunzip = u8 => through(u8, new DecompressionStream('gzip'));
        async function hash(u8) {
            const d = new Uint8Array(await crypto.subtle.digest('SHA-256', u8));
            return Array.from(d, b => b.toString(16).padStart(2, '0')).join('');
        }

        // this device's saves: from storage (before the game starts) or from memory (while playing)
        async function fromStorage() {
            const out = [];
            for (const r of await allRecords()) {
                if (!isSave(r.k) || !r.v || !r.v.blob) continue;
                out.push({ name: r.v.name, data: new Uint8Array(await r.v.blob.arrayBuffer()) });
            }
            return out;
        }
        function fromMemory() {
            const out = [];
            for (const [k, f] of files) if (isSave(k) && f.data) out.push({ name: f.name, data: f.data });
            return out;
        }
        // the account's saves replace this device's (in storage, before the game reads them)
        async function toStorage(list) {
            const records = await allRecords();
            const localSettings = records.find(r => r.k === SETTINGS_KEY);
            let localText = '';
            if (localSettings && localSettings.v && localSettings.v.blob) localText = td.decode(new Uint8Array(await localSettings.v.blob.arrayBuffer()));
            const tx = (await openDb()).transaction(STORE, 'readwrite');
            const st = tx.objectStore(STORE);
            for (const r of records) if (SAVE_RE.test(r.k)) st.delete(r.k);
            for (const f of list) {
                let data = f.data;
                if (key(f.name) === SETTINGS_KEY) {
                    // their settings, with this device's own (controls, layout, size, graphics) kept
                    const mine = settingLines(localText).filter(isDeviceLine);
                    data = te.encode(settingLines(td.decode(data)).filter(l => !isDeviceLine(l)).concat(mine).join('\r\n'));
                }
                st.put({ name: f.name, blob: new Blob([data]) }, key(f.name));
            }
            await txDone(tx);
        }

        async function binary(path, body) {
            const headers = { 'Authorization': 'Bearer ' + ((Account.session && Account.session.token) || '') };
            if (body) headers['Content-Type'] = 'application/octet-stream';
            let r;
            try { r = await fetch(Account.SERVER + path, { method: body ? 'POST' : 'GET', headers, body }); }
            catch (e) { return { ok: false, status: 0, error: "Couldn't reach the server." }; }
            if (!body && r.ok) return { ok: true, status: r.status, data: new Uint8Array(await r.arrayBuffer()) };
            let j = null;
            try { j = await r.json(); } catch (e) { }
            if (!r.ok) return Object.assign({ ok: false, status: r.status, error: (j && j.error) || ('The server answered ' + r.status) }, j || {});
            return Object.assign({ ok: true, status: r.status }, j || {});
        }

        async function download(version) {
            const r = await binary('/api/saves?v=' + version);
            if (!r.ok) throw new Error(r.error);
            const list = unpack(await gunzip(r.data));
            await toStorage(list);
            setState({ version, hash: await hash(pack(await fromStorage())) });
            return list.length;
        }
        async function upload(raw, h, base, force) {
            const r = await binary('/api/saves?base=' + base + (force ? '&force=1' : '') + '&device=' + encodeURIComponent(device()), await gzip(raw));
            if (r.ok) setState({ version: r.version, hash: h });
            return r;
        }
        function when(iso) {
            if (!iso) return '';
            const mins = Math.round((Date.now() - Date.parse(iso)) / 60000);
            if (!(mins >= 0)) return '';
            if (mins < 2) return 'just now';
            if (mins < 90) return mins + ' minutes ago';
            const hrs = Math.round(mins / 60);
            if (hrs < 36) return hrs + ' hours ago';
            return Math.round(hrs / 24) + ' days ago';
        }

        // a question with two answers, over the setup screen
        function ask(title, text, a, b) {
            return new Promise(done => {
                const o = document.createElement('div');
                o.style.cssText = 'position:fixed;inset:0;z-index:1004;display:flex;align-items:center;justify-content:center;background:rgba(0,0,20,.7);padding:16px;font-family:Segoe UI,Roboto,sans-serif';
                o.innerHTML = '<div style="max-width:440px;width:100%;background:#0b1238;border:1px solid rgba(143,180,255,.45);box-shadow:0 20px 60px rgba(0,0,0,.6);padding:20px 22px;color:#d7d7f5">'
                    + '<div style="font-size:17px;font-weight:700;color:#fff;margin-bottom:10px"></div><div style="font-size:14px;line-height:1.5;margin-bottom:16px;white-space:pre-line"></div>'
                    + '<div style="display:flex;gap:10px;flex-wrap:wrap"><button style="flex:1 1 160px;font:inherit;font-weight:700;padding:11px;background:#2d6ae0;color:#fff;border:1px solid #8fb4ff;cursor:pointer"></button>'
                    + '<button style="flex:1 1 160px;font:inherit;font-weight:700;padding:11px;background:#1d2a6e;color:#fff;border:1px solid #5a6ab8;cursor:pointer"></button></div></div>';
                o.firstChild.children[0].textContent = title;
                o.firstChild.children[1].textContent = text;
                const btns = o.querySelectorAll('button');
                btns[0].textContent = a; btns[1].textContent = b;
                btns[0].onclick = () => { o.remove(); done(0); };
                btns[1].onclick = () => { o.remove(); done(1); };
                document.body.appendChild(o);
            });
        }

        // ---- before playing ----
        async function syncBeforePlay(offline) {
            enabled = !offline && !!(Account.session && Account.session.token);
            if (!enabled) return;
            try {
                if (typeof CompressionStream === 'undefined' || !crypto.subtle) { enabled = false; console.warn('[Saves] this browser can\'t pack saves - cross saves are off here'); return; }
                status('Checking your saves...');
                const info = await Account.call('/api/saves/info');
                if (!info.ok) { console.warn('[Saves] ' + info.error); status(''); return; } // (offline: they go up next time)
                const local = await fromStorage();
                const raw = pack(local);
                const h = local.length ? await hash(raw) : '';
                let st = getState();
                const owner = lsGet(OWNER, null);
                if (owner && owner !== me() && local.length) {
                    // the saves in this browser were another account's
                    if (info.version > 0) {
                        const c = await ask('Whose saves?', 'The saves in this browser belong to ' + owner + '. Your account has its own saves (from ' + (info.device || 'another device') + ').', 'Use my saves', 'Keep the ones here');
                        lsSet(OWNER, me());
                        if (c === 0) { status('Getting your saves...'); await download(info.version); status(''); return; }
                    }
                    st = null;
                }
                lsSet(OWNER, me());
                const localChanged = st ? h !== st.hash : local.length > 0;
                const serverChanged = info.version > 0 && (!st || info.version !== st.version);
                if (!localChanged && !serverChanged) { status(''); return; }
                if (serverChanged && !localChanged) {
                    status('Getting your saves from ' + (info.device || 'your account') + '...');
                    const n = await download(info.version);
                    console.log('[Saves] got ' + n + ' files from the account (number ' + info.version + ')');
                    status(''); return;
                }
                if (localChanged && !serverChanged) {
                    status('Backing up your saves...');
                    const r = await upload(raw, h, st ? st.version : 0, false);
                    if (!r.ok && r.status !== 409) console.warn('[Saves] ' + r.error);
                    if (r.status !== 409) { status(''); return; }
                }
                // both changed (or it's the first time on this device and both have saves)
                if (!st && info.version > 0) {
                    // maybe they're the same saves after all
                    const r = await binary('/api/saves?v=' + info.version);
                    if (r.ok && h && await hash(pack(unpack(await gunzip(r.data)))) === h) { setState({ version: info.version, hash: h }); status(''); return; }
                }
                const c = await ask('Which saves do you want?',
                    'Your game was saved on this device and on ' + (info.device || 'another device') + (info.when ? ' (' + when(info.when) + ')' : '') + '.\n\nPick the one to play. The other is kept as a backup on your account.',
                    'Keep this device\'s', 'Use ' + (info.device || 'the other device') + '\'s');
                if (c === 0) {
                    status('Backing up your saves...');
                    const r = await upload(raw, h, info.version, true);
                    if (!r.ok) console.warn('[Saves] ' + r.error);
                } else {
                    status('Getting your saves...');
                    if (local.length) await binary('/api/saves/backup?device=' + encodeURIComponent(device()), await gzip(raw));
                    await download(info.version);
                }
                status('');
            } catch (e) {
                console.warn('[Saves] sync failed', e);
                status('');
            }
        }

        // ---- while playing: the game saved ----
        function changed() {
            if (!enabled) return;
            clearTimeout(timer);
            timer = setTimeout(push, 8000); // (a save writes several files: wait until it's done)
        }
        function notice(title, text) {
            try { DotNet.invokeMethod('SimsRedux.Web', 'SimsReduxNotice', title, text); } catch (e) { }
        }
        async function push() {
            if (busy) { again = true; return; }
            busy = true;
            try {
                await saveChain;
                const raw = pack(fromMemory());
                const h = await hash(raw);
                const st = getState();
                if (st && st.hash === h) return;
                const r = await upload(raw, h, st ? st.version : 0, false);
                if (r.ok) {
                    console.log('[Saves] backed up to the account (number ' + r.version + ')');
                    if (!told) { told = true; notice('Saves backed up', 'Your game is saved to your account, ready on any device you log in on.'); }
                } else if (r.status === 409) {
                    notice('Saves changed elsewhere', 'Your game was also saved on ' + (r.device || 'another device') + '. Next time you start, you can pick which saves to keep.');
                } else console.warn('[Saves] ' + r.error);
            } catch (e) { console.warn('[Saves] backup failed', e); }
            finally {
                busy = false;
                if (again) { again = false; changed(); }
            }
        }

        return {
            isSave, changed, syncBeforePlay,
            /** For the launcher: a line about the saves on this account, or ''. */
            line() {
                const st = Account.session && Account.session.token ? getState() : null;
                return st ? 'backed up to your account' : '';
            },
        };
    })();

    // Admin and Tester accounts: the Sims 1 files come from the server instead of a folder.
    async function downloadFromServer() {
        const s = Account.session;
        if (!s || !s.token) throw new Error('you need to be logged in');
        const list = await Account.call('/api/files');
        if (!list.ok) { if (list.status === 401) Account.refresh(); throw new Error(list.error); }
        // only plain paths inside the Sims folder (a "..", or a path from the root, could land on /userdata/)
        const files = (Array.isArray(list.files) ? list.files : []).filter(f => f && typeof f.p === 'string' && f.p &&
            !SKIP_EXT.test(f.p) && !/^[\\/]/.test(f.p) && !/(^|[\\/])\.\.([\\/]|$)/.test(f.p));
        if (!files.some(f => f.p.toLowerCase() === 'gamedata/uitext.iff')) throw new Error("the server's game folder doesn't have GameData\\UIText.iff in it");
        show('Getting the game files from the server', { progress: true });
        Account.show(false);
        try { if (navigator.storage && navigator.storage.persist) await navigator.storage.persist(); } catch (e) { }

        // only what's missing or changed: files kept from before (an earlier or interrupted download)
        // are reused, and ones the server no longer has are removed
        status('Checking which files you already have...');
        const have = new Map();
        for (const r of await allRecords()) {
            if (!r.k.startsWith(SIMS_ROOT) || !r.v) continue;
            have.set(r.k, { size: r.v.blob ? r.v.blob.size : -1, m: r.v.m });
        }
        const wanted = new Set(files.map(f => key(SIMS_ROOT + f.p)));
        const stale = Array.from(have.keys()).filter(k => !wanted.has(k));
        const need = files.filter(f => {
            const h = have.get(key(SIMS_ROOT + f.p));
            return !h || h.size !== (f.s || 0) || (f.m && h.m && h.m !== f.m);
        });
        const total = need.reduce((n, f) => n + (f.s || 0), 0);
        const kept = files.length - need.length;

        // phones (iPhones especially) only let a website keep so much: say so up front
        try {
            const est = navigator.storage && navigator.storage.estimate ? await navigator.storage.estimate() : null;
            if (est && est.quota && est.quota - (est.usage || 0) < total * 1.05)
                throw new Error('this browser only lets the website keep about ' + mb(est.quota - (est.usage || 0)) +
                    ', and the game files are ' + mb(total) + '. (Private/incognito tabs get much less - try a normal tab.)');
        } catch (e) { if (e && /lets the website keep/.test(e.message)) throw e; }
        // if the page dies part-way (the browser ran out of memory and reloaded it), say so next time
        try { sessionStorage.setItem('srx-downloading', String(Date.now())); } catch (e) { }
        await setPending(true);
        if (stale.length) {
            const tx = db.transaction(STORE, 'readwrite');
            for (const k of stale) tx.objectStore(STORE).delete(k);
            await txDone(tx);
        }

        // the work: many small files per request, compressed (a new enough server), big ones on their own
        const BATCH_BYTES = 12 * 1048576, BATCH_FILES = 300, ALONE = 24 * 1048576;
        const jobs = [];
        let batch = [], batchBytes = 0;
        for (const f of need) {
            if (!list.pack || (f.s || 0) > ALONE) { jobs.push([f]); continue; }
            if (batch.length && (batchBytes + (f.s || 0) > BATCH_BYTES || batch.length >= BATCH_FILES)) { jobs.push(batch); batch = []; batchBytes = 0; }
            batch.push(f); batchBytes += f.s || 0;
        }
        if (batch.length) jobs.push(batch);

        const auth = { 'Authorization': 'Bearer ' + s.token };
        async function one(f) {
            const r = await fetch(Account.SERVER + '/api/file?p=' + encodeURIComponent(f.p), { headers: auth });
            if (r.status === 401 || r.status === 403) throw Object.assign(new Error('login'), { login: true });
            if (!r.ok) throw new Error(r.status);
            return [{ f, blob: await r.blob() }];
        }
        async function packed(group) {
            const r = await fetch(Account.SERVER + '/api/pack', {
                method: 'POST', headers: Object.assign({ 'Content-Type': 'application/json' }, auth),
                body: JSON.stringify({ p: group.map(f => f.p) })
            });
            if (r.status === 401 || r.status === 403) throw Object.assign(new Error('login'), { login: true });
            if (!r.ok) throw new Error(r.status);
            const u8 = new Uint8Array(await r.arrayBuffer());
            const dv = new DataView(u8.buffer, u8.byteOffset, u8.byteLength);
            const byPath = new Map(group.map(f => [f.p, f]));
            const out = [];
            let o = 0;
            while (o < u8.length) {
                const pl = dv.getUint16(o, true); o += 2;
                const path = new TextDecoder().decode(u8.subarray(o, o + pl)); o += pl;
                const size = dv.getUint32(o, true); o += 4;
                if (o + size > u8.length) throw new Error('cut short');
                const f = byPath.get(path);
                if (f) out.push({ f, blob: new Blob([u8.subarray(o, o + size)]) });
                o += size;
            }
            if (out.length !== group.length) throw new Error('missing files');
            return out;
        }

        let done = 0, count = 0, failed = null;
        if (!need.length) status(`You already have all ${files.length} game files - nothing to download.`);
        else if (kept) status(`You already have ${kept} of ${files.length} files - getting the other ${need.length} (${mb(total)})`);
        const queue = jobs.slice();
        async function worker() {
            while (queue.length && !failed) {
                const job = queue.shift();
                let got = null;
                for (let attempt = 0; attempt < 4 && !got; attempt++) {
                    try { got = (job.length === 1 && !list.pack) || (job.length === 1 && (job[0].s || 0) > ALONE) ? await one(job[0]) : await packed(job); }
                    catch (e) {
                        if (e && e.login) { failed = 'your login stopped working - log in again'; return; }
                        if (attempt === 3) { failed = 'couldn\'t download ' + job[0].p + (job.length > 1 ? ' and ' + (job.length - 1) + ' more' : '') + ' (' + (e && e.message || e) + ')'; return; }
                        await new Promise(ok => setTimeout(ok, 800 * (attempt + 1)));
                    }
                }
                try {
                    const tx = db.transaction(STORE, 'readwrite');
                    for (const g of got) {
                        const name = SIMS_ROOT + g.f.p;
                        tx.objectStore(STORE).put({ name: normalize(name), blob: g.blob, m: g.f.m }, key(name));
                    }
                    await txDone(tx);
                } catch (e) {
                    failed = (e && e.name === 'QuotaExceededError')
                        ? 'this browser ran out of storage space after ' + mb(done) + ' (it needs ' + mb(total) + ')'
                        : "couldn't save " + job[0].p + ' (' + ((e && (e.name + ': ' + e.message)) || e) + ')';
                    return;
                }
                for (const g of got) { done += g.blob.size; count++; }
                setProgress(done, total);
                status(`${count} of ${need.length} files (${mb(done)} of ${mb(total)})` + (kept ? ` - ${kept} already here` : ''));
            }
        }
        await Promise.all(Array.from({ length: list.pack ? 4 : 6 }, worker));
        try { sessionStorage.removeItem('srx-downloading'); } catch (e) { }
        // (on a problem the files so far are kept: trying again carries on from there)
        if (failed) throw new Error(failed + ' - press it again to carry on where it stopped');
        await setPending(false);
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
        try {
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
        } catch (e) {
            // (ran out of space part-way: don't leave half a Sims folder that looks complete next visit)
            try { await clearSims(); } catch (x) { }
            throw e;
        }
        return true;
    }

    // ---------- loading ----------

    // Where the website's game content is: the backup copy of the game at /classic/ uses the main
    // site's (it's the same), so the website only holds it once.
    const SITE_CONTENT = /\/classic\//.test(location.pathname) ? '../Content/' : 'Content/';

    async function loadSiteContent() {
        let list;
        try {
            const r = await fetch(SITE_CONTENT + 'files.txt', { cache: 'no-cache' });
            if (!r.ok) throw new Error(r.status);
            list = (await r.text()).split(/\r?\n/).map(l => l.trim().replace(/\\/g, '/')).filter(l => l);
        } catch (e) {
            console.warn('[SimsFS] no Content/files.txt', e);
            return;
        }
        // the custom content comes in a few compressed bundles (tools/bundle_content.py)
        const bundles = list.filter(l => l.endsWith('.srxpack.gz'));
        list = list.filter(l => !l.endsWith('.srxpack.gz'));
        const bundleJob = loadBundles(bundles);
        // a handful at a time: browsers refuse thousands of downloads at once
        const queue = list.slice();
        async function worker() {
            while (queue.length) {
                const rel = queue.shift();
                const name = 'Content/' + rel;
                for (let attempt = 0; attempt < 3; attempt++) {
                    try {
                        const r = await fetch(SITE_CONTENT + rel);
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
        await bundleJob;
    }

    // The custom content bundles: kept in the browser's cache by name (the name changes whenever
    // what's inside does), so they're only downloaded again after an update changes them.
    async function loadBundles(names) {
        if (!names.length) return;
        let cache = null;
        try { if (window.caches) cache = await caches.open('srx-bundles'); } catch (e) { cache = null; }
        // bundles from older versions aren't needed any more
        if (cache) {
            try {
                const want = new Set(names.map(n => new URL(SITE_CONTENT + n, location.href).href));
                for (const req of await cache.keys()) if (!want.has(req.url)) await cache.delete(req);
            } catch (e) { }
        }
        for (const name of names) {
            const url = SITE_CONTENT + name;
            let u8 = null;
            for (let attempt = 0; attempt < 3 && !u8; attempt++) {
                try {
                    let r = cache ? await cache.match(url) : null;
                    if (!r) {
                        r = await fetch(url);
                        if (!r.ok) throw new Error(r.status);
                        if (cache) { try { await cache.put(url, r.clone()); } catch (e) { } }
                    }
                    const gz = await r.arrayBuffer();
                    u8 = new Uint8Array(await new Response(new Blob([gz]).stream().pipeThrough(new DecompressionStream('gzip'))).arrayBuffer());
                } catch (e) {
                    if (cache) { try { await cache.delete(url); } catch (e2) { } }
                    if (attempt === 2) console.warn('[SimsFS] could not load ' + url, e);
                }
            }
            if (!u8) continue;
            const dv = new DataView(u8.buffer, u8.byteOffset, u8.byteLength);
            let o = 0, n = 0;
            while (o + 6 <= u8.length) {
                const pl = dv.getUint16(o, true); o += 2;
                const path = new TextDecoder().decode(u8.subarray(o, o + pl)); o += pl;
                const size = dv.getUint32(o, true); o += 4;
                if (o + size > u8.length) break;
                addFile('Content/' + path, { size, data: u8.subarray(o, o + size) });
                o += size; n++;
            }
            console.log('[SimsFS] ' + name + ': ' + n + ' files');
        }
    }

    function isLazy(k) { return LAZY.some(r => r.test(k)); }

    async function loadStored() {
        const all = await allRecords();
        // a download from the server that hasn't finished: not ready to play yet (it carries on next time)
        if (all.some(r => r.k === PENDING_KEY)) return false;
        const records = all.filter(r => r.v && r.v.blob);
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
            if (sessionStorage.getItem('srx-loading')) {
                status('Last time, loading stopped suddenly - this device may not have enough memory for the game.', true);
                Problems.report('reloaded while loading', 'The page was reloaded part way through loading the game files (' + mb(total) + ' to load) - most likely the browser ran out of memory.');
            }
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
        // (8 at a time: reading them back is mostly waiting on storage, so more at once is quicker)
        await Promise.all(Array.from({ length: 8 }, worker));
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
    // The notes shipped with the website, with the changes made in the server panel on top
    // (a version edited there replaces this one, one added there is new, one hidden there is left out).
    // a small JSON GET that gives up after a while (null if it failed); the timeout covers the body too
    async function fetchJson(url, ms, cache) {
        const ctl = new AbortController(); const t = setTimeout(() => ctl.abort(), ms);
        try {
            const r = await fetch(url, { cache, signal: ctl.signal });
            return r.ok ? await r.json() : null;
        } catch (e) { return null; }
        finally { clearTimeout(t); }
    }
    // versions compare as numbers ("3.10" is after "3.9"): < 0, 0 or > 0
    function cmpVer(a, b) {
        const x = String(a).split('.').map(n => parseInt(n, 10) || 0), y = String(b).split('.').map(n => parseInt(n, 10) || 0);
        for (let i = 0; i < Math.max(x.length, y.length); i++) if ((x[i] || 0) !== (y[i] || 0)) return (x[i] || 0) - (y[i] || 0);
        return 0;
    }
    let patchNotes = null;
    let onlineBlocked = ''; // why online mode is shut for this visit ('' = open) - asked by the game
    async function getPatchNotes() {
        if (patchNotes) return patchNotes;
        const [shipped, changes] = await Promise.all([fetchJson('patchnotes.json', 5000, 'no-cache'), fetchJson(Account.SERVER + '/api/patchnotes', 5000, 'no-cache')]);
        const cmp = cmpVer;
        const lines = v => (Array.isArray(v) ? v : []).map(t => String(t).trim()).filter(Boolean).slice(0, 30);
        const by = new Map();
        for (const src of [shipped, changes]) {
            for (const p of ((src && Array.isArray(src.patches)) ? src.patches : [])) {
                if (!p || !/^\d{1,3}(\.\d{1,3}){0,3}$/.test(String(p.version || ''))) continue;
                if (p.hidden) { by.delete(String(p.version)); continue; }
                by.set(String(p.version), { version: String(p.version), date: String(p.date || ''), features: lines(p.features), updates: lines(p.updates) });
            }
        }
        if (!shipped && !changes) return null;
        // (the version check goes by the website's own notes: a version written up early in the panel isn't out yet)
        const shippedLatest = ((shipped && Array.isArray(shipped.patches)) ? shipped.patches : []).map(p => String((p && p.version) || '')).filter(v => /^\d/.test(v)).sort((a, b) => cmp(b, a))[0] || null;
        patchNotes = { stage: (shipped && shipped.stage) || 'Beta', shippedLatest, patches: Array.from(by.values()).sort((a, b) => cmp(b.version, a.version)) };
        return patchNotes;
    }
    async function hasGameFiles() {
        try {
            const tx = (await openDb()).transaction(STORE, 'readonly');
            const st = tx.objectStore(STORE);
            if ((await req(st.count(IDBKeyRange.only(PENDING_KEY)))) > 0) return false; // (a download that hasn't finished)
            return (await req(st.count(IDBKeyRange.only('/sims/gamedata/uitext.iff')))) > 0;
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
        const older = L.querySelector('.nolder');
        older.classList.toggle('hidden', patches.length < 2);
        // all the patches show by default (the notes scroll); the link folds them away
        const showPatches = (all) => {
            body.innerHTML = !latest ? '<p>Patch notes are unavailable right now.</p>'
                : patchHtml(latest, false) + (all ? patches.slice(1).map(p => patchHtml(p, true)).join('') : '');
            older.textContent = all ? 'Hide earlier patches' : 'Show earlier patches';
            if (all) older.dataset.open = '1'; else delete older.dataset.open;
        };
        showPatches(true);
        older.onclick = () => showPatches(!older.dataset.open);

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
            const backed = Saves.line();
            fstate.textContent = (await hasGameFiles()) ? 'Saved in this browser' + (backed ? ' \u00b7 ' + backed : '') : "Not set up yet \u2014 you'll choose it after pressing Play";
        };
        await paintFolder();

        // Live and Test: online (green), maintenance (amber) or offline (red), with how many people are
        // on; the chosen one's message of the day sits above the patch notes (every 15 seconds while showing)
        const smotd = L.querySelector('.smotd');
        const statusOf = url => fetchJson(url + '/api/status', 6000, 'no-store');
        const srvStatus = { live: null, test: null };
        const chosen = Servers.channel;
        let closed = false; // the launcher was left: a check still on its way mustn't paint over the next one
        const paintServers = async () => {
            const live = await statusOf(Servers.url('live'));
            if (closed) return;
            if (live) Servers.learn(live.servers);
            const test = await statusOf(Servers.url('test'));
            if (closed) return;
            srvStatus.live = live; srvStatus.test = test;
            for (const ch of ['live', 'test']) {
                const card = L.querySelector('.srv[data-ch="' + ch + '"]'), st = srvStatus[ch];
                card.classList.toggle('sel', ch === chosen);
                const dot = card.querySelector('.dot'), state = card.querySelector('.sstate'), count = card.querySelector('.scount');
                dot.className = 'dot';
                if (!st) { dot.classList.add('down'); state.textContent = 'Offline'; count.textContent = ''; continue; }
                if (st.maintenance) { dot.classList.add('warn'); state.textContent = 'Maintenance'; }
                else if (st.town) { dot.classList.add('on'); state.textContent = 'Online'; }
                else { dot.classList.add('warn'); state.textContent = 'Maintenance'; }
                count.textContent = (st.online | 0) + ' online' + ((st.playing | 0) > 0 ? ' \u00b7 ' + (st.playing | 0) + ' in a lot' : '');
            }
            const mine = srvStatus[chosen];
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
        const newer = (a, b) => cmpVer(a, b) > 0; // is version a newer than b?
        if (webVersion) L.querySelector('.vnum').textContent = webVersion;
        let needsUpdate = false;
        let refreshSite = () => { }; // (set once the launcher's buttons are wired up)
        const checkVersion = () => {
            const srv = srvStatus[chosen] && srvStatus[chosen].game;
            const site = (notes && notes.shippedLatest) || (latest && latest.version);
            needsUpdate = !!webVersion && ((site && newer(site, webVersion)) || (srv && newer(srv, webVersion)));
            vcheck.className = 'vcheck';
            if (!webVersion) { vcheck.textContent = ''; }
            else if (needsUpdate) {
                vcheck.classList.add('warn');
                vcheck.textContent = 'A new version is out (v' + (srv && newer(srv, site || '0') ? srv : site) + ') \u2014 you have v' + webVersion + '.';
                const soon = document.createElement('div'); soon.className = 'vsoon';
                soon.textContent = 'Your game will update shortly. It can take up to 20 minutes. Please refresh occasionally. Online mode is unavailable until the game has been updated.';
                vcheck.appendChild(soon);
                const now = document.createElement('button'); now.className = 'link vrefresh'; now.textContent = 'Refresh now';
                now.onclick = () => refreshSite();
                vcheck.appendChild(now);
            }
            else if (srv && newer(webVersion, srv)) { vcheck.classList.add('warn'); vcheck.textContent = 'Up to date (v' + webVersion + '). The server is still on v' + srv + ' \u2014 online play waits for it to update; single player is fine.'; }
            else { vcheck.classList.add('ok'); vcheck.textContent = '\u2713 Up to date \u00b7 v' + webVersion; }
            play.textContent = needsUpdate ? 'PLAY OFFLINE' : 'PLAY'; // out of date: single player only until the new version arrives
        };
        paintServers();
        const serverTimer = setInterval(paintServers, 15000);

        return new Promise(done => {
            const btns = L.querySelector('.lbtns');
            // (the game files & saves line lives in the menu when logged in: back to its place first,
            // since the menu is rebuilt below)
            const lfolder = L.querySelector('.lfolder');
            if (lfolder.parentNode !== L.querySelector('.l-acct')) L.querySelector('.l-acct').appendChild(lfolder);
            lfolder.classList.remove('inmenu');
            const isMod = on && Array.isArray(acc.powers) && acc.powers.length > 0;
            btns.innerHTML = on ? (isMod ? '<button class="ghost lmod" role="menuitem">Moderator tools</button>' : '') + '<button class="ghost lrep" role="menuitem">Report a player</button><hr><button class="ghost lswitch" role="menuitem">Switch account</button><button class="ghost llogout danger" role="menuitem">Log out</button>'
                : '<button class="ghost llogin">Log in</button>';
            // logged in: the buttons live in a menu that the round initial opens (logged out: just Log in, shown)
            btns.classList.toggle('menu', on);
            if (on) { lfolder.classList.add('inmenu'); btns.insertBefore(lfolder, btns.firstChild); btns.insertBefore(document.createElement('hr'), lfolder.nextSibling); }
            btns.classList.remove('open');
            btns.setAttribute('role', on ? 'menu' : '');
            av.setAttribute('aria-expanded', 'false');
            av.disabled = !on;
            av.style.cursor = on ? '' : 'default';
            const setMenu = (open) => { btns.classList.toggle('open', open); av.setAttribute('aria-expanded', open ? 'true' : 'false'); };
            av.onclick = (e) => { if (!on) return; e.stopPropagation(); setMenu(!btns.classList.contains('open')); };
            btns.onclick = (e) => { if (e.target && e.target.tagName === 'BUTTON' && !e.target.classList.contains('lchange')) setMenu(false); }; // a choice closes the menu
            if (L._srxMenuClose) document.removeEventListener('pointerdown', L._srxMenuClose, true);
            L._srxMenuClose = (e) => { if (btns.classList.contains('open') && !btns.contains(e.target) && e.target !== av) setMenu(false); };
            document.addEventListener('pointerdown', L._srxMenuClose, true);
            const modbox = L.querySelector('.modbox'), notesBox = L.querySelector('.notes');
            modbox.classList.add('hidden'); notesBox.classList.remove('hidden');
            if (isMod) btns.querySelector('.lmod').onclick = () => openMod(L);
            const rep = L.querySelector('.lreport'), rmsg = L.querySelector('.rmsg');
            rep.classList.add('hidden');
            if (on) {
                btns.querySelector('.lrep').onclick = () => { rep.classList.toggle('hidden'); rmsg.textContent = ''; L.querySelector('.rabout').focus(); };
                L.querySelector('.rcancel').onclick = () => rep.classList.add('hidden');
                const rsend = L.querySelector('.rsend');
                rsend.onclick = async () => {
                    if (rsend.disabled) return;
                    const about = L.querySelector('.rabout').value.trim(), reason = L.querySelector('.rreason').value.trim();
                    rmsg.className = 'rmsg';
                    if (!about || reason.length < 5) { rmsg.classList.add('error'); rmsg.textContent = 'Say who it is and what happened.'; return; }
                    rmsg.textContent = 'Sending...';
                    rsend.disabled = true; // (one report per press, however often it's clicked)
                    let r;
                    try { r = await Account.call('/api/report', { about, reason }); } finally { rsend.disabled = false; }
                    if (!r.ok) { rmsg.classList.add('error'); rmsg.textContent = r.error || 'It could not be sent.'; return; }
                    L.querySelector('.rabout').value = ''; L.querySelector('.rreason').value = '';
                    rmsg.textContent = 'Thanks - your report went to the admin.';
                };
            }
            const finish = (r) => {
                closed = true; clearInterval(serverTimer); document.removeEventListener('keydown', onKey);
                if (L._srxMenuClose) { document.removeEventListener('pointerdown', L._srxMenuClose, true); L._srxMenuClose = null; }
                done(r);
            };
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
                // out of date: the game still starts, but online mode stays shut until it's updated
                onlineBlocked = needsUpdate
                    ? 'Online mode is unavailable until the game has been updated.\n\nYour game will update shortly - it can take up to 20 minutes. Refresh the page now and then to get it.'
                    : '';
                finish('play');
            };
            // fetch the newest website: clear what the browser kept of the old one, then reload
            refreshSite = async () => {
                vcheck.className = 'vcheck'; vcheck.textContent = 'Refreshing...';
                try { if (window.caches) for (const k of await caches.keys()) await caches.delete(k); } catch (e) { }
                try { if (navigator.serviceWorker) for (const r of await navigator.serviceWorker.getRegistrations()) await r.unregister(); } catch (e) { }
                finish('update');
            };
            const onKey = (e) => {
                if (e.key === 'Escape' && btns.classList.contains('open')) { btns.classList.remove('open'); av.setAttribute('aria-expanded', 'false'); av.focus(); e.preventDefault(); return; }
                if (e.key !== 'Enter' || L.classList.contains('hidden')) return;
                const t = e.target && e.target.tagName;
                if (t === 'INPUT' || t === 'TEXTAREA' || t === 'BUTTON' || t === 'SELECT') return;
                play.click();
            };
            document.addEventListener('keydown', onKey);
        });
    }

    // ---------- moderator tools (accounts given powers in the server panel) ----------
    // Everything here is checked again by the server against the account's powers.
    let MOD_TAB = null;
    async function openMod(L) {
        const box = L.querySelector('.modbox'), notes = L.querySelector('.notes');
        notes.classList.add('hidden'); box.classList.remove('hidden');
        L.querySelector('.mback').onclick = () => { box.classList.add('hidden'); notes.classList.remove('hidden'); };
        await drawMod(L);
    }
    async function drawMod(L) {
        const tabs = L.querySelector('.mtabs'), body = L.querySelector('.mbody'), msg = L.querySelector('.mmsg');
        const d = await Account.call('/api/mod');
        if (!d.ok) { body.innerHTML = ''; msg.className = 'mmsg error'; msg.textContent = d.error || "Couldn't load the moderator tools."; return; }
        const names = { say: 'Message', kick: 'Kick', reports: 'Reports', flags: 'Cheat flags', events: 'Events' };
        const powers = d.powers || [];
        if (!MOD_TAB || powers.indexOf(MOD_TAB) < 0) MOD_TAB = powers[0];
        tabs.innerHTML = '';
        for (const p of powers) {
            const b = document.createElement('button'); b.textContent = names[p] || p; b.classList.toggle('sel', p === MOD_TAB);
            b.onclick = () => { MOD_TAB = p; msg.textContent = ''; drawMod(L); };
            tabs.appendChild(b);
        }
        body.innerHTML = '';
        const mk = (tag, cls, text) => { const e = document.createElement(tag); if (cls) e.className = cls; if (text != null) e.textContent = text; return e; };
        const act = async (payload, then) => {
            msg.className = 'mmsg'; msg.textContent = 'Working...';
            const r = await Account.call('/api/mod/do', payload);
            if (!r.ok) { msg.className = 'mmsg error'; msg.textContent = r.error || 'That did not go through.'; return; }
            msg.textContent = r.message || 'Done.';
            if (then) then();
            setTimeout(() => drawMod(L).then(() => { msg.textContent = r.message || 'Done.'; }), 300);
        };
        const item = (text, sub, btnText, fn) => {
            const it = mk('div', 'mitem'); const t = mk('div', 't', text); if (sub) t.appendChild(mk('small', null, sub)); it.appendChild(t);
            if (btnText) { const b = mk('button', 'ghost', btnText); b.onclick = fn; it.appendChild(b); }
            body.appendChild(it);
        };
        const when = s => { try { return new Date(s).toLocaleString([], { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' }); } catch (e) { return ''; } };
        if (MOD_TAB === 'say') {
            body.appendChild(mk('div', 'mempty', 'Pops up for everyone in the online town, signed with your name.'));
            const ta = mk('textarea'); ta.rows = 3; ta.maxLength = 300; ta.placeholder = 'Your message'; body.appendChild(ta);
            const row = mk('div', 'mrow'); const b = mk('button', null, 'Send to everyone'); row.appendChild(b); body.appendChild(row);
            b.onclick = () => act({ action: 'say', text: ta.value }, () => { ta.value = ''; });
        } else if (MOD_TAB === 'kick') {
            body.appendChild(mk('div', 'mempty', 'Sends a player off the online town. Admins can\'t be kicked.'));
            const row = mk('div', 'mrow');
            const nm = mk('input'); nm.placeholder = "Player's account name"; nm.setAttribute('list', 'srx-online');
            const dl = mk('datalist'); dl.id = 'srx-online'; for (const n of d.online || []) { const o = mk('option'); o.value = n; dl.appendChild(o); }
            const mins = mk('input', 'short'); mins.type = 'number'; mins.min = 0; mins.max = 60; mins.value = 10; mins.title = 'Minutes before they can come back (0-60)';
            const b = mk('button', null, 'Kick');
            row.appendChild(nm); row.appendChild(dl); row.appendChild(mins); row.appendChild(mk('span', 'mempty', 'min')); row.appendChild(b);
            body.appendChild(row);
            if ((d.online || []).length) body.appendChild(mk('div', 'mempty', 'Online now: ' + d.online.join(', ')));
            b.onclick = () => act({ action: 'kick', name: nm.value.trim(), minutes: Number(mins.value) || 0 }, () => { nm.value = ''; });
        } else if (MOD_TAB === 'reports') {
            const list = d.reports || [];
            if (!list.length) body.appendChild(mk('div', 'mempty', 'No reports waiting.'));
            for (const r of list) item(r.from + ' reported ' + r.about, r.reason + ' \u00b7 ' + when(r.time), 'Dealt with', () => act({ action: 'report', id: r.id }));
        } else if (MOD_TAB === 'flags') {
            const list = d.flags || [];
            if (!list.length) body.appendChild(mk('div', 'mempty', 'Nothing flagged.'));
            for (const f of list) item(f.account + ' \u2014 ' + f.what, f.detail + ' \u00b7 ' + when(f.time), 'Looked at', () => act({ action: 'flag', id: f.id }));
        } else if (MOD_TAB === 'events') {
            const row = mk('div', 'mrow');
            const sel = mk('select'); for (const k of d.kinds || []) { const o = mk('option', null, k.title); o.value = k.kind; o.title = k.about; sel.appendChild(o); }
            const hrs = mk('input', 'short'); hrs.type = 'number'; hrs.min = 1; hrs.max = 24; hrs.value = 2; hrs.title = 'Hours (1-24)';
            const ttl = mk('input'); ttl.placeholder = 'Title (optional)'; ttl.maxLength = 60;
            const b = mk('button', null, 'Start');
            row.appendChild(sel); row.appendChild(hrs); row.appendChild(mk('span', 'mempty', 'hours')); row.appendChild(ttl); row.appendChild(b);
            body.appendChild(row);
            b.onclick = () => act({ action: 'event', kind: sel.value, hours: Number(hrs.value) || 2, title: ttl.value });
            const evs = d.events || [];
            body.appendChild(mk('div', 'mempty', evs.length ? 'On now and coming up:' : 'No events on or coming up.'));
            for (const e of evs) item(e.title, when(e.start) + ' \u2013 ' + when(e.end), 'End', () => act({ action: 'endevent', kind: e.kind }));
        }
    }

    // Shows the setup screen as needed; resolves once the game can start.
    async function prepare() {
        console.log('[SimsRedux] web build ' + buildName());
        await openDb();
        screen().root.querySelector('.signout').onclick = async () => {
            try { await Account.call('/api/logout', {}); } catch (e) { }
            try { await Account.logout(); } catch (e) { }
            location.reload();
        };
        screen().change.onclick = () => {
            if (!confirm('Choose a different Sims folder? Your current Sims files will be removed from this browser (homes saved in the game go with them).')) return;
            changeFolder(); // (waits for saves still on their way, so none lands after the clear)
        };

        const contentReady = loadSiteContent();

        // 1. log in or make an account (skipped when a login is remembered),
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
        console.log('[SimsRedux] ' + (how === 'offline' ? 'playing offline' : 'logged in as ' + ((Account.session && Account.session.name) || '?')));

        // 2. cross saves: this device's and the account's are brought in line (before the game reads them)
        show('Getting ready', { progress: false });
        Account.show(false);
        await Saves.syncBeforePlay(how === 'offline');

        // 3. the game files: already in this browser, or picked / downloaded now
        let loaded = false;
        try { loaded = await loadStored(); }
        catch (e) { console.warn('[SimsFS] stored files could not be read', e); }

        let problem = '';
        try {
            if (sessionStorage.getItem('srx-downloading')) {
                sessionStorage.removeItem('srx-downloading');
                problem = 'The last download stopped suddenly - the browser most likely ran out of memory and reloaded the page. ' +
                    'The files it got are kept: get them from the server again to carry on where it stopped. ' +
                    'Phones (iPhones especially) may not have enough room for The Sims in a browser.';
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
                f.blob.arrayBuffer().then(b => { f.data = new Uint8Array(b); f.blob = null; },
                    e => { f.loading = false; console.warn('[SimsFS] could not load ' + f.name, e); }); // (asked again next read)
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
    // set when the game started out of date: the reason online mode is shut ('' = it's open)
    const onlineBlockedReason = () => onlineBlocked || '';
    // a phone or tablet. Phones and tablets count as touch even when the browser says it has a fine
    // pointer (many Android phones do); the game's Options > Interface > Controls can switch to PC
    // (a tablet with a mouse and keyboard). ?controls=touch or ?controls=pc in the address forces it.
    // ?controls=touch / ?controls=pc in the address (wins over the game's Controls setting)
    function forcedControls()
    {
        try { return (new URLSearchParams(location.search).get('controls') || '').toLowerCase(); } catch (e) { return ''; }
    }
    function touchDevice()
    {
        try
        {
            const forced = (new URLSearchParams(location.search).get('controls') || '').toLowerCase();
            if (forced === 'touch') return true;
            if (forced === 'pc' || forced === 'mouse') return false;
            const ua = navigator.userAgent || '';
            const touchPoints = navigator.maxTouchPoints || 0;
            if (/Android|iPhone|iPad|iPod|Mobile|Silk|Kindle/i.test(ua)) return true;
            if (/Macintosh/i.test(ua) && touchPoints > 1) return true; // an iPad asking for the desktop site
            const mm = (q) => window.matchMedia && window.matchMedia(q).matches;
            // anything else (a computer): touch only when a touch screen is the only pointer
            return touchPoints > 0 && mm('(pointer: coarse)') && !mm('(any-pointer: fine)');
        }
        catch (e) { return false; }
    }
    const accountName = () => (Account.session && Account.session.name) || '';
    window.SimsFS = { touchDevice, forcedControls, accountServer, accountToken, accountName, onlineBlockedReason, changeFolder, setFullScreen, isFullScreen, prepare, fileExists, dirExists, fileSize, read, write, deleteFile, createDir, deleteDir, moveFile, list, normalize };
})();
