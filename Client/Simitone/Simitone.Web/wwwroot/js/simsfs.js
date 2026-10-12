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
#srx-setup { position: fixed; inset: 0; z-index: 1000; background: #000052; color: #d7d7f5;
  font-family: 'Segoe UI', Tahoma, sans-serif; display: flex; align-items: center; justify-content: center; padding: 16px; }
#srx-setup .box { width: 100%; max-width: 560px; background: #00003a; border: 2px solid #7878c8; border-right-color: #000028;
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
#srx-setup .hidden, #srx-setup.hidden { display: none; }`;
        document.head.appendChild(style);
        const el = document.createElement('div');
        el.id = 'srx-setup';
        el.innerHTML = `<div class="box">
  <h1>The Sims Redux</h1>
  <h2 class="title"></h2>
  <div class="pick hidden">
    <p>To play, choose the folder where <b>The Sims</b> is installed &mdash; the one with the <code>GameData</code> folder inside. It's usually
       <code>C:\\Program Files (x86)\\Maxis\\The Sims</code>.</p>
    <p>Your browser will ask to <i>upload</i> the files. Nothing leaves your computer: they're only copied into this
       browser's storage, so you only have to do this once.</p>
    <button class="choose">Choose your Sims folder</button>
    <input type="file" class="folder hidden" webkitdirectory directory multiple>
  </div>
  <div class="srx-progress hidden"><div class="bar"><div></div></div></div>
  <div class="status"></div>
  <div class="again hidden" style="margin-top:10px"><button class="link change">Use a different Sims folder</button></div>
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
        };
        ui.choose.onclick = () => ui.input.click();
        return ui;
    }
    function show(title, { pick = false, progress = false, again = false } = {}) {
        const s = screen();
        s.root.classList.remove('hidden');
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
        let n = 0;
        await Promise.all(list.map(async rel => {
            const name = 'Content/' + rel;
            try {
                const r = await fetch(name);
                if (!r.ok) return;
                const data = new Uint8Array(await r.arrayBuffer());
                addFile(name, { size: data.length, data });
            } catch (e) { console.warn('[SimsFS] could not load ' + name, e); }
            n++;
        }));
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

    // Shows the setup screen as needed; resolves once the game can start.
    async function prepare() {
        await openDb();
        screen().change.onclick = async () => {
            if (!confirm('Choose a different Sims folder? Your current Sims files will be removed from this browser (homes saved in the game go with them).')) return;
            await clearSims();
            location.reload();
        };

        const contentReady = loadSiteContent();
        let loaded = false;
        try { loaded = await loadStored(); }
        catch (e) { console.warn('[SimsFS] stored files could not be read', e); }

        let problem = '';
        while (!loaded) {
            show('Welcome!', { pick: true });
            if (problem) status(problem, true);
            const list = await waitForFolder();
            try {
                if (!await copyFolder(list)) {
                    problem = "That folder doesn't have The Sims in it (no GameData\\UIText.iff was found). Pick the folder that has GameData inside.";
                    continue;
                }
                loaded = await loadStored();
                if (!loaded) problem = 'Your Sims files could not be read back. Please try again.';
            } catch (e) {
                console.error(e);
                problem = 'Copying failed: ' + (e && e.message ? e.message : e) + '. Make sure there is enough free disk space and try again.';
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

    window.SimsFS = { prepare, fileExists, dirExists, fileSize, read, write, deleteFile, createDir, deleteDir, moveFile, list, normalize };
})();
