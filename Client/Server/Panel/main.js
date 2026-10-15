/* ============================================================
   The Sims Redux — server panel (Electron main process)
   ============================================================
   One window that starts the Live and Test servers, shows their consoles,
   takes console commands, manages accounts, and says whether the world
   outside can reach them.

   Each server is a normal child process with stdin piped in: console
   commands are written there. The panel's authority comes from owning the
   process — nothing here is reachable over the network.
   ============================================================ */
"use strict";
const { app, BrowserWindow, ipcMain, dialog, shell, Menu } = require("electron");
const path = require("path");
const fs = require("fs");
const { spawn, execFile } = require("child_process");
const C = require("./core.js");

let win = null;
let SETTINGS_FILE = "", S = null;
const TAIL_MAX = 4000;

/* one of these per server (live, test) */
const SV = {};
for (const ch of C.CHANNELS) SV[ch] = { child: null, stopping: false, tail: [], portBusy: false, restartOnExit: false };

/* ---------- talking to the window ---------- */
function send(ch, payload) { try { if (win && !win.isDestroyed()) win.webContents.send(ch, payload); } catch (e) { } }
function out(channel, kind, text) {
  const line = { channel, kind, text: String(text), at: Date.now() };
  const t = SV[channel].tail;
  t.push(line);
  if (t.length > TAIL_MAX) t.splice(0, t.length - TAIL_MAX);
  send("console", line);
}

/* ---------- the server processes ---------- */
function running(ch) { const c = SV[ch].child; return !!(c && c.exitCode === null && !c.killed); }

function startServer(ch) {
  const sv = SV[ch];
  if (running(ch)) return { ok: false, error: "it is already running" };
  const p = C.profile(S, ch, __dirname);
  if (sv.portBusy) return { ok: false, error: "something is already answering on port " + p.port +
    " — a server this panel didn't start (a window left open?). Stop that one first, or change the port in Settings." };
  const exe = S.serverExe && C.isFile(S.serverExe) ? S.serverExe : C.guessServerExe(__dirname);
  const how = C.serverCommand(exe);
  if (!how) return { ok: false, error: "the server program isn't built yet — open the solution in Visual Studio and Rebuild Solution (it goes to Client\\Server\\bin), or choose it in Settings" };
  const other = C.CHANNELS.find(o => o !== ch && running(o) && C.profile(S, o, __dirname).port === p.port);
  if (other) return { ok: false, error: "the " + other + " server is already using port " + p.port + " — give them different ports in Settings" };

  const args = how.pre.concat(C.serverArgs(S, ch, __dirname));
  let child;
  try {
    child = spawn(how.cmd, args, { cwd: path.dirname(exe), stdio: ["pipe", "pipe", "pipe"], windowsHide: true });
  } catch (e) { return { ok: false, error: String(e && e.message || e) }; }
  sv.child = child;
  sv.stopping = false;
  child.stdout.setEncoding("utf8");
  child.stderr.setEncoding("utf8");
  let obuf = "", ebuf = "";
  child.stdout.on("data", d => { obuf += d; let i; while ((i = obuf.indexOf("\n")) >= 0) { out(ch, "out", obuf.slice(0, i).replace(/\r$/, "")); obuf = obuf.slice(i + 1); } });
  child.stderr.on("data", d => { ebuf += d; let i; while ((i = ebuf.indexOf("\n")) >= 0) { out(ch, "err", ebuf.slice(0, i).replace(/\r$/, "")); ebuf = ebuf.slice(i + 1); } });
  child.on("error", e => {
    const msg = /ENOENT/.test(String(e && e.message)) && how.cmd === "dotnet"
      ? "`dotnet` isn't installed — build the server in Visual Studio so there's a SimsRedux.Server.exe"
      : String(e && e.message || e);
    out(ch, "err", msg);
    send("running", { channel: ch, running: false });
  });
  child.on("exit", (code, sig) => {
    const why = sig ? ("signal " + sig) : ("exit code " + code);
    sv.child = null;
    /* 75 is the server's own "restart me" */
    if ((code === 75 || sv.restartOnExit) && !sv.stopping) {
      sv.restartOnExit = false;
      out(ch, "panel", "Restarting the " + ch + " server…");
      setTimeout(() => { const r = startServer(ch); if (!r.ok) out(ch, "err", r.error); }, 700);
      return;
    }
    const hint = code === 3 ? " (it couldn't open its port — see above)" : code === 2 ? " (its accounts file couldn't be read — see above)" : "";
    out(ch, "panel", "The " + ch + " server stopped — " + why + hint + ".");
    sv.stopping = false;
    send("running", { channel: ch, running: false });
  });
  out(ch, "panel", "Starting the " + ch.toUpperCase() + " server on port " + p.port + "…");
  send("running", { channel: ch, running: true });
  setTimeout(() => pollLocal(ch), 1200);
  return { ok: true };
}

function stopServer(ch, hard) {
  const sv = SV[ch];
  if (!running(ch)) return { ok: false, error: "it isn't running" };
  sv.stopping = true;
  out(ch, "panel", hard ? "Killing the " + ch + " server…" : "Asking the " + ch + " server to save and stop (/quit)…");
  const c = sv.child;
  if (hard) { try { c.kill(); } catch (e) { } return { ok: true }; }
  try { c.stdin.write("/quit\n"); } catch (e) { }
  setTimeout(() => { if (c && c.exitCode === null) { out(ch, "panel", "It didn't stop on its own — killing it."); try { c.kill(); } catch (e) { } } }, 8000);
  return { ok: true };
}

function sendCommand(ch, line) {
  if (!running(ch)) return { ok: false, error: "the " + ch + " server isn't running" };
  try { SV[ch].child.stdin.write(C.oneLine(line) + "\n"); return { ok: true }; }
  catch (e) { return { ok: false, error: String(e && e.message || e) }; }
}

/* ---------- status ---------- */
let pollT = null, slowT = null;
async function pollLocal(ch) {
  const p = C.profile(S, ch, __dirname);
  const r = await C.getJSON("http://127.0.0.1:" + p.port + "/healthz", 2500);
  SV[ch].portBusy = !!(r && r.ok) && !running(ch);
  const h = C.readHealth(r, running(ch));
  if (SV[ch].portBusy) h.foreign = true;
  h.channel = ch;
  // accounts made on the website that are waiting for approval (read from the server's own file)
  try { h.waiting = C.readAccounts(p.dataDir).filter(a => a.pending).length; } catch (e) { h.waiting = 0; }
  send("status:local", h);
}
async function pollPublic(ch) {
  const p = C.profile(S, ch, __dirname);
  const u = String(p.publicUrl || "").replace(/\/+$/, "");
  if (!u) return send("status:public", { channel: ch, state: "off", detail: "no public address set", edge: "" });
  const r = await C.getJSON(u + "/healthz", 8000);
  const h = C.readPublic(r);
  h.channel = ch;
  send("status:public", h);
}
function pollAll() { for (const ch of C.CHANNELS) { pollLocal(ch); pollPublic(ch); } pollTunnel(); }
function startPolling() {
  clearInterval(pollT); clearInterval(slowT);
  pollT = setInterval(() => { C.CHANNELS.forEach(pollLocal); pollTunnel(); }, 3000);
  slowT = setInterval(() => C.CHANNELS.forEach(pollPublic), 15000);
  pollAll();
}

/* ---------- the Cloudflare tunnel ----------
   The tunnel is the Windows service "cloudflared" (installed with `cloudflared service install`).
   Reading its state needs nothing special; starting or stopping it needs administrator rights,
   so Windows shows its usual "allow this app to make changes?" box each time. */
let tunnelState = "unknown", tunnelBusy = false;
function tunnelName() { return C.safeServiceName(S && S.tunnelService); }
function pollTunnel() {
  if (process.platform !== "win32") { tunnelState = "unsupported"; return send("status:tunnel", { state: tunnelState }); }
  execFile("sc.exe", ["query", tunnelName()], { windowsHide: true, timeout: 5000 }, (err, stdout, stderr) => {
    tunnelState = C.readServiceState(String(stdout || "") + String(stderr || ""), err && err.code);
    send("status:tunnel", { state: tunnelState, busy: tunnelBusy, name: tunnelName() });
  });
}
function setTunnel(on) {
  if (process.platform !== "win32") return Promise.resolve({ ok: false, error: "the tunnel switch only works on Windows" });
  if (tunnelBusy) return Promise.resolve({ ok: false, error: "already switching the tunnel" });
  const name = tunnelName();
  tunnelBusy = true;
  send("status:tunnel", { state: tunnelState, busy: true, name });
  const verb = on ? "start" : "stop";
  // sc.exe run as administrator (Windows asks first); -Wait so we know when it's done
  const ps = "Start-Process -FilePath 'sc.exe' -ArgumentList '" + verb + "','" + name + "' -Verb RunAs -WindowStyle Hidden -Wait";
  return new Promise(resolve => {
    execFile("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", ps], { windowsHide: true, timeout: 120000 }, (err, stdout, stderr) => {
      tunnelBusy = false;
      const text = String(stderr || "") + String(stdout || "");
      setTimeout(pollTunnel, 1500);
      setTimeout(() => C.CHANNELS.forEach(pollPublic), 4000);
      if (err && /canceled|cancelled|declined/i.test(text)) return resolve({ ok: false, error: "Windows didn't get permission (the box was closed or No was picked)" });
      if (err) return resolve({ ok: false, error: (text.trim() || String(err.message || err)).slice(0, 300) });
      resolve({ ok: true });
    });
  });
}

/* ---------- the window ---------- */
function create() {
  win = new BrowserWindow({
    width: 1220, height: 840, minWidth: 920, minHeight: 620,
    backgroundColor: "#0b0e1a",
    title: "The Sims Redux — server",
    autoHideMenuBar: true,
    webPreferences: { preload: path.join(__dirname, "preload.js"), contextIsolation: true, nodeIntegration: false }
  });
  Menu.setApplicationMenu(null);
  win.loadFile(path.join(__dirname, "index.html"));
  win.on("closed", () => { win = null; });
}

app.whenReady().then(() => {
  SETTINGS_FILE = path.join(app.getPath("userData"), "panel.json");
  S = C.loadSettings(SETTINGS_FILE);
  create();
  startPolling();
  app.on("activate", () => { if (!win) create(); });
});
/* closing the panel stops the servers, saving first */
app.on("window-all-closed", () => {
  let waiting = 0;
  for (const ch of C.CHANNELS) if (running(ch)) { waiting++; stopServer(ch, false); }
  if (!waiting) return app.quit();
  const t = setInterval(() => {
    if (!C.CHANNELS.some(running)) { clearInterval(t); app.quit(); }
  }, 200);
  setTimeout(() => { for (const ch of C.CHANNELS) if (running(ch)) try { SV[ch].child.kill(); } catch (e) { } app.quit(); }, 9000);
});

/* ---------- what the window may ask for ---------- */
function ch(v) { return C.CHANNELS.indexOf(v) >= 0 ? v : "live"; }

ipcMain.handle("panel:init", () => {
  setTimeout(pollAll, 150);
  const exe = S.serverExe && C.isFile(S.serverExe) ? S.serverExe : C.guessServerExe(__dirname);
  const servers = {};
  for (const c of C.CHANNELS) servers[c] = { running: running(c), tail: SV[c].tail, profile: C.profile(S, c, __dirname) };
  return { settings: S, exe, servers, commands: C.COMMANDS, planned: C.PLANNED, names: C.commandNames() };
});
ipcMain.handle("panel:start", (e, c) => startServer(ch(c)));
ipcMain.handle("panel:stop", (e, c, hard) => stopServer(ch(c), !!hard));
ipcMain.handle("panel:restart", (e, c) => {
  c = ch(c);
  if (!running(c)) return startServer(c);
  SV[c].restartOnExit = true;
  return sendCommand(c, "/restart");
});
ipcMain.handle("panel:cmd", (e, c, line) => sendCommand(ch(c), line));
ipcMain.handle("panel:clear", (e, c) => { SV[ch(c)].tail = []; return { ok: true }; });
ipcMain.handle("panel:poll", () => { pollAll(); return { ok: true }; });
ipcMain.handle("panel:setActive", (e, c) => { S.active = ch(c); C.saveSettings(SETTINGS_FILE, S); return { ok: true }; });

ipcMain.handle("panel:open", (e, c, what) => {
  const P = C.dataPaths(C.profile(S, ch(c), __dirname).dataDir);
  try {
    if (what === "log") {
      if (!fs.existsSync(P.log)) return { ok: false, error: "no server.log yet — start the server once" };
      shell.showItemInFolder(P.log); return { ok: true };
    }
    const target = what === "backups" ? P.backups : what === "game" ? S.gameFolder : P.dir;
    if (!target || !fs.existsSync(target)) return { ok: false, error: "there's no " + (target || "folder") + " yet" + (what === "game" ? "" : " — start the server once") };
    shell.openPath(target);
    return { ok: true };
  } catch (err) { return { ok: false, error: String(err && err.message || err) }; }
});
ipcMain.handle("panel:tunnel", async (e, on) => {
  const r = await setTunnel(!!on);
  for (const c of C.CHANNELS) out(c, r.ok ? "panel" : "err", r.ok
    ? "Cloudflare tunnel " + (on ? "started - the game can be reached from the internet again." : "stopped - nobody outside this PC can reach the game now.")
    : "Couldn't " + (on ? "start" : "stop") + " the Cloudflare tunnel: " + r.error);
  return r;
});
ipcMain.handle("panel:site", () => { shell.openExternal(S.site || C.DEFAULTS.site); return { ok: true }; });

/** Pick the Sims 1 folder that Admins and Testers can download. */
ipcMain.handle("panel:pickGameFolder", async () => {
  const r = await dialog.showOpenDialog(win, { title: "The Sims 1 folder (the one with GameData inside)", properties: ["openDirectory"] });
  if (r.canceled || !r.filePaths[0]) return { ok: false };
  const d = r.filePaths[0];
  const has = dir => { try { return fs.readdirSync(dir).some(n => n.toLowerCase() === "gamedata"); } catch (e) { return false; } };
  let ok = has(d);
  if (!ok) try { ok = fs.readdirSync(d).some(n => has(path.join(d, n))); } catch (e) { }
  if (!ok) return { ok: false, error: "there's no GameData folder in there — pick the folder The Sims is installed in" };
  S.gameFolder = d; C.saveSettings(SETTINGS_FILE, S);
  /* running servers pick it up straight away */
  for (const c of C.CHANNELS) if (running(c)) sendCommand(c, "/files folder " + d);
  return { ok: true, dir: d };
});

/** Pick the server program by hand (if it isn't where Visual Studio puts it). */
ipcMain.handle("panel:pickExe", async () => {
  const r = await dialog.showOpenDialog(win, { title: "SimsRedux.Server.exe", properties: ["openFile"],
    filters: [{ name: "Server", extensions: ["exe", "dll"] }] });
  if (r.canceled || !r.filePaths[0]) return { ok: false };
  S.serverExe = r.filePaths[0]; C.saveSettings(SETTINGS_FILE, S);
  return { ok: true, exe: S.serverExe };
});

ipcMain.handle("panel:settings", (e, patch) => {
  patch = patch || {};
  for (const k of ["gameFolder", "site", "serverExe", "tunnelService"]) if (typeof patch[k] === "string") S[k] = patch[k].trim();
  for (const c of C.CHANNELS) if (patch[c]) {
    const p = patch[c];
    S[c] = Object.assign({}, S[c], {
      port: Math.max(1, Math.min(65535, parseInt(p.port, 10) || C.DEFAULTS[c].port)),
      publicUrl: String(p.publicUrl || "").trim(),
      dataDir: String(p.dataDir || "").trim()
    });
  }
  C.saveSettings(SETTINGS_FILE, S);
  startPolling();
  const servers = {};
  for (const c of C.CHANNELS) servers[c] = { profile: C.profile(S, c, __dirname) };
  return { ok: true, settings: S, servers };
});

/* ---------- server tools: read from the server's files, changed through its console ---------- */
ipcMain.handle("panel:tools", (e, c) => {
  c = ch(c);
  return Object.assign({ ok: true, running: running(c), powers: C.POWERS }, C.readTools(C.profile(S, c, __dirname).dataDir));
});
ipcMain.handle("panel:tool", (e, c, action, args) => {
  c = ch(c);
  const r = C.toolCommand(String(action), args && typeof args === "object" ? args : {});
  if (r.error) return { ok: false, error: r.error };
  // maintenance can be switched while stopped: the server starts in it
  if (action === "maintenance" && !running(c)) {
    try { return C.setMaintenanceFile(C.profile(S, c, __dirname).dataDir, !!(args && args.on)); }
    catch (err) { return { ok: false, error: String(err && err.message || err) }; }
  }
  return sendCommand(c, r.cmd);
});

/* ---------- patch notes: the panel's changes, kept in the server's data folder ---------- */
ipcMain.handle("panel:notes", (e, c) => C.readPatchNotes(C.profile(S, ch(c), __dirname).dataDir, __dirname));
ipcMain.handle("panel:note", (e, c, action, entry) => {
  const dir = C.profile(S, ch(c), __dirname).dataDir;
  try {
    if (action === "save") return C.savePatch(dir, entry);
    if (action === "hide") return C.hidePatch(dir, entry && entry.version);
    if (action === "reset") return C.resetPatch(dir, entry && entry.version);
  } catch (err) { return { ok: false, error: String(err && err.message || err) }; }
  return { ok: false, error: "unknown action" };
});

/* ---------- accounts: read from the server's own file, changed through its console ---------- */
ipcMain.handle("panel:accounts", (e, c) => {
  c = ch(c);
  const p = C.profile(S, c, __dirname);
  const cfg = C.readServerConfig(p.dataDir);
  return { ok: true, running: running(c), accounts: C.readAccounts(p.dataDir), registration: cfg.registration !== false, motd: cfg.motd || "" };
});
ipcMain.handle("panel:account", (e, c, action, name, a, b) => {
  c = ch(c);
  name = C.oneLine(name); a = C.oneLine(a); b = C.oneLine(b);
  if (!name && action !== "registration" && action !== "motd") return { ok: false, error: "no account given" };
  if (/\s/.test(a) && (action === "add" || action === "resetpw")) return { ok: false, error: "passwords can't have spaces" };
  switch (action) {
    case "add": return sendCommand(c, "/account add " + name + " " + a + " " + (b || "player"));
    case "role": return sendCommand(c, "/role " + name + " " + a);
    case "resetpw": return sendCommand(c, "/resetpw " + name + " " + a);
    case "approve": return sendCommand(c, "/approve " + name);
    case "unapprove": return sendCommand(c, "/unapprove " + name);
    case "suspend": return sendCommand(c, "/suspend " + name);
    case "unsuspend": return sendCommand(c, "/unsuspend " + name);
    case "delete": return sendCommand(c, "/delete " + name);
    case "registration": return sendCommand(c, "/registration " + (a === "on" ? "on" : "off"));
    case "motd": return sendCommand(c, "/motd " + a);
  }
  return { ok: false, error: "unknown action" };
});
