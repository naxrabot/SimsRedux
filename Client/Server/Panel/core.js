/* ============================================================
   The Sims Redux — server panel: the parts that are not Electron
   ============================================================
   Plain Node, so it can be tested without a window: where the server
   program and its data live, the Live and Test settings, what the health
   check means, whether Cloudflare can reach this PC, and the console
   commands.
   ============================================================ */
"use strict";
const fs = require("fs");
const path = require("path");
const http = require("http");
const https = require("https");

const CHANNELS = ["live", "test"];

/* ---------- where things are ---------- */

/** The panel sits in Client/Server/Panel; Visual Studio builds the server into Client/Server/bin. */
function guessServerExe(from) {
  const here = from || __dirname;
  const tries = [
    path.join(here, "..", "bin", "SimsRedux.Server.exe"),
    path.join(here, "..", "bin", "SimsRedux.Server.dll"),
    path.join(here, "..", "SimsRedux.Server", "bin", "SimsRedux.Server.exe"),
  ];
  for (const f of tries) if (isFile(f)) return path.resolve(f);
  return "";
}
function isFile(f) { try { return fs.statSync(f).isFile(); } catch (e) { return false; } }

/** How to run the server: the .exe directly, or a .dll through `dotnet`. */
function serverCommand(exe) {
  if (!exe) return null;
  if (/\.dll$/i.test(exe)) return { cmd: "dotnet", pre: [exe] };
  return { cmd: exe, pre: [] };
}

/** Each server keeps its own data (accounts, settings, log) in its own folder. */
function defaultDataDir(channel, from) {
  return path.resolve(path.join(from || __dirname, "..", "ServerData", channel));
}
function dataPaths(dir) {
  return {
    dir,
    config: path.join(dir, "server.json"),
    accounts: path.join(dir, "accounts.json"),
    backups: path.join(dir, "backups"),
    patchnotes: path.join(dir, "patchnotes.json"),
    log: path.join(dir, "server.log")
  };
}

/* ---------- settings ---------- */
const DEFAULTS = {
  serverExe: "",
  active: "live",
  gameFolder: "",
  live: { port: 37564, publicUrl: "https://tsr-lot.mbrew.studio", dataDir: "" },
  test: { port: 37566, publicUrl: "", dataDir: "" },
  site: "https://tsr.mbrew.studio",
  tunnelService: "cloudflared"
};
function loadSettings(file) {
  let s = {};
  try { s = JSON.parse(fs.readFileSync(file, "utf8")) || {}; } catch (e) { }
  const out = Object.assign({}, DEFAULTS, s);
  for (const c of CHANNELS) out[c] = Object.assign({}, DEFAULTS[c], s[c] || {});
  if (CHANNELS.indexOf(out.active) < 0) out.active = "live";
  return out;
}
function saveSettings(file, s) {
  try {
    fs.mkdirSync(path.dirname(file), { recursive: true });
    fs.writeFileSync(file, JSON.stringify(s, null, 2));
    return true;
  } catch (e) { return false; }
}
/** The settings for one server, with its data folder filled in. */
function profile(S, channel, from) {
  const p = Object.assign({}, S[channel] || DEFAULTS[channel]);
  p.channel = channel;
  p.dataDir = p.dataDir || defaultDataDir(channel, from);
  p.port = (p.port | 0) || DEFAULTS[channel].port;
  return p;
}
/** What the server is started with. Only things that are set are passed, so server.json keeps the rest. */
function serverArgs(S, channel, from) {
  const p = profile(S, channel, from);
  const a = ["--data", p.dataDir, "--port", String(p.port), "--channel", channel];
  if (S.gameFolder) a.push("--game-folder", S.gameFolder);
  return a;
}

/* ---------- reading the server's own files (never the password hashes) ---------- */
function readJSON(f) { try { return JSON.parse(fs.readFileSync(f, "utf8")); } catch (e) { return null; } }
function readAccounts(dataDir) {
  const list = readJSON(dataPaths(dataDir).accounts);
  if (!Array.isArray(list)) return [];
  return list.map(a => ({
    name: String(a.name || ""),
    role: String(a.role || "player"),
    suspended: !!a.suspended,
    pending: !!a.pending,
    created: a.created || "",
    lastLogin: a.lastLogin || ""
  })).sort((x, y) => (y.pending - x.pending) || x.name.localeCompare(y.name, undefined, { sensitivity: "base" }));
}
function readServerConfig(dataDir) { return readJSON(dataPaths(dataDir).config) || {}; }

/* ---------- patch notes ----------
   Each update ships its patch notes with the website (Simitone.Web/wwwroot/patchnotes.json).
   What's changed here in the panel is kept apart, in the server's data folder
   (patchnotes.json there): a version edited here replaces the shipped one, a version
   added here is new, a version hidden here isn't shown. The server hands those
   changes to the website's launcher, which puts the two together the same way. */
function shippedNotesFile(from) {
  return path.resolve(from || __dirname, "..", "..", "Simitone", "Simitone.Web", "wwwroot", "patchnotes.json");
}
/** -1 / 0 / 1, newest last ("3.10" is after "3.9"). */
function verCmp(a, b) {
  const x = String(a).split(".").map(n => parseInt(n, 10) || 0), y = String(b).split(".").map(n => parseInt(n, 10) || 0);
  for (let i = 0; i < Math.max(x.length, y.length); i++) if ((x[i] || 0) !== (y[i] || 0)) return (x[i] || 0) < (y[i] || 0) ? -1 : 1;
  return 0;
}
/** A patch entry made safe: short plain lines only. null if it isn't usable. */
function cleanPatch(p) {
  if (!p || typeof p !== "object") return null;
  const version = String(p.version || "").trim();
  if (!/^\d{1,3}(\.\d{1,3}){0,3}$/.test(version)) return null;
  const lines = v => (Array.isArray(v) ? v : String(v || "").split(/\r?\n/))
    .map(t => oneLine(String(t)).replace(/^[-*\u2022]\s*/, "").trim()).filter(t => t.length > 0).slice(0, 30).map(t => t.slice(0, 200));
  const out = { version, date: oneLine(String(p.date || "")).slice(0, 40), features: lines(p.features), updates: lines(p.updates) };
  if (p.hidden) out.hidden = true;
  return out;
}
/** The shipped notes with the panel's changes on top, newest first. Each entry says where it came from. */
function mergePatchNotes(shipped, changes) {
  const by = new Map();
  for (const p of ((shipped && shipped.patches) || [])) { const c = cleanPatch(p); if (c) by.set(c.version, Object.assign(c, { shipped: true })); }
  for (const p of ((changes && changes.patches) || [])) {
    const c = cleanPatch(p); if (!c) continue;
    const had = by.get(c.version);
    by.set(c.version, Object.assign(c, { shipped: !!had, edited: true }));
  }
  return Array.from(by.values()).sort((a, b) => verCmp(b.version, a.version));
}
function readPatchNotes(dataDir, from) {
  const shipped = readJSON(shippedNotesFile(from));
  const changes = readJSON(dataPaths(dataDir).patchnotes);
  return { ok: true, shippedFound: !!shipped, stage: (shipped && shipped.stage) || "Beta", patches: mergePatchNotes(shipped, changes) };
}
function writeChanges(dataDir, fn) {
  const file = dataPaths(dataDir).patchnotes;
  const cur = readJSON(file) || {};
  const list = (Array.isArray(cur.patches) ? cur.patches : []).map(cleanPatch).filter(Boolean);
  const next = fn(list);
  fs.mkdirSync(dataDir, { recursive: true });
  fs.writeFileSync(file + ".tmp", JSON.stringify({ patches: next }, null, 2));
  fs.renameSync(file + ".tmp", file);
}
/** Save one version as typed in the panel (replaces the shipped one for that version). */
function savePatch(dataDir, entry) {
  const c = cleanPatch(entry);
  if (!c) return { ok: false, error: "the version must look like 3.5" };
  delete c.hidden;
  if (!c.features.length && !c.updates.length) return { ok: false, error: "add at least one feature or update" };
  writeChanges(dataDir, list => list.filter(p => p.version !== c.version).concat([c]));
  return { ok: true };
}
/** Hide a version from the launcher (a shipped one stays hidden until shown again). */
function hidePatch(dataDir, version) {
  version = String(version || "").trim();
  writeChanges(dataDir, list => list.filter(p => p.version !== version).concat([{ version, date: "", features: [], updates: [], hidden: true }]));
  return { ok: true };
}
/** Forget the panel's change to a version: back to what shipped (or gone, if it was added here). */
function resetPatch(dataDir, version) {
  version = String(version || "").trim();
  writeChanges(dataDir, list => list.filter(p => p.version !== version));
  return { ok: true };
}

/* ---------- asking a server how it is ---------- */

/** A small JSON GET that never throws and never hangs. */
function getJSON(url, ms, maxBytes) {
  return new Promise(resolve => {
    let done = false;
    const finish = v => { if (!done) { done = true; resolve(v); } };
    let req;
    try {
      const lib = url.startsWith("https:") ? https : http;
      req = lib.get(url, { timeout: ms || 4000, headers: { "User-Agent": "SimsRedux-Panel" } }, res => {
        let body = "";
        res.setEncoding("utf8");
        const cap = maxBytes || 200000;
        res.on("data", d => { body += d; if (body.length > cap) req.destroy(); });
        res.on("end", () => {
          let json = null;
          try { json = JSON.parse(body); } catch (e) { }
          finish({ ok: true, status: res.statusCode, headers: res.headers, json, body: json ? "" : body.slice(0, 400) });
        });
      });
    } catch (e) { return finish({ ok: false, error: String(e && e.message || e) }); }
    req.on("timeout", () => { req.destroy(); finish({ ok: false, error: "timed out" }); });
    req.on("error", e => finish({ ok: false, error: String(e && e.message || e) }));
  });
}

/**
 * What the local server says about itself.
 *   off      nothing is listening
 *   starting the process is up but not answering yet
 *   up       fine
 */
function readHealth(r, running) {
  if (!r || !r.ok) return { state: running ? "starting" : "off", detail: running ? "process up, not answering yet" : "not running" };
  const h = r.json;
  if (!h || h.ok !== true) return { state: "strained", detail: "answered " + r.status + ", but it isn't a Sims Redux server" };
  return {
    state: "up", detail: "",
    channel: h.channel || "", version: h.version || "",
    players: h.players | 0, online: h.online | 0, accounts: h.accounts | 0, lots: h.lots | 0, town: !!h.town,
    registration: !!h.registration,
    uptime: h.uptime | 0, rssMB: h.rssMB | 0,
    files: h.files || { ready: false }
  };
}

/**
 * What the public address says. The useful part is WHERE it's broken: Cloudflare can be fine
 * while the tunnel to this PC isn't, which looks the same from a browser.
 */
function readPublic(r) {
  if (!r || !r.ok) return { state: "down", detail: r && r.error ? r.error : "no answer", edge: "" };
  const cf = !!(r.headers && (r.headers["cf-ray"] || /cloudflare/i.test(r.headers.server || "")));
  const edge = cf ? "cloudflare" : "direct";
  if (r.status === 530 || (r.body && /1033/.test(r.body)))
    return { state: "origin", detail: "Cloudflare is up; the tunnel to this PC isn't (cloudflared not connected)", edge };
  if (r.status >= 520 && r.status <= 527)
    return { state: "origin", detail: "Cloudflare is up; it can't reach this PC (" + r.status + ")", edge };
  if (r.status === 502 || r.status === 503 || r.status === 504)
    return { state: "origin", detail: "the tunnel answered " + r.status + " — the server isn't answering behind it", edge };
  if (r.status !== 200) return { state: "bad", detail: "answered " + r.status, edge };
  if (!r.json || r.json.ok !== true) return { state: "bad", detail: "answered, but not as a Sims Redux server", edge };
  return { state: "up", detail: cf ? "through Cloudflare" : "answering directly", edge, players: r.json.players | 0, channel: r.json.channel || "" };
}

/* ---------- the console commands ----------
   Kept here so the panel can show them while the server is stopped. Must match
   Commands.cs in SimsRedux.Server. */
const COMMANDS = [
  ["Accounts", [
    ["/accounts", "every account with its role"],
    ["/account add <name> <password> [role]", "make an account — role is admin, tester or player"],
    ["/role <name> <admin|tester|player>", "change a role — Admins and Testers can download the game files"],
    ["/resetpw <name> <password>", "set a new password for someone who forgot theirs"],
    ["/approve <name>", "let in an account made on the website"],
    ["/unapprove <name>", "put an account back to waiting"],
    ["/pending", "accounts waiting for approval"],
    ["/suspend <name>", "lock an account out"],
    ["/unsuspend <name>", "let them back in"],
    ["/delete <name>", "remove an account for good"],
    ["/registration on|off", "let people make accounts on the website, or not"]
  ]],
  ["The online town", [
    ["/town", "the town's houses and which are open"],
    ["/town fresh", "brand-new town: the game's Neighbourhood 1 with its starting families"],
    ["/town fresh empty", "brand-new town with houses but no families"],
    ["/town sources", "every neighbourhood found, with its families"],
    ["/town owners", "which account's family lives where"],
    ["/town disown <account>", "take a town family away from an account"],
    ["/town time [<day> <time>]", "the online town's clock - e.g. /town time 2 7:30pm"],
    ["/money <account> <amount>", "give a player's town family money (minus takes it away)"],
    ["/town import [folder]", "copy a neighbourhood in as the town — empty = your Neighborhood 1"],
    ["/lots", "lots open right now, with who's on them"],
    ["/lots open <house>", "open a lot (players can also just travel there)"],
    ["/lots close <house|all>", "save and close a lot"],
    ["/lots game <path>", "the game program that runs lots (Simitone.exe, or its folder)"]
  ]],
  ["Players", [
    ["/who", "accounts active in the last 5 minutes"],
    ["/motd <text>", "the message of the day (on the website's launcher)"],
    ["/say <text>", "a message on every online player's screen"],
    ["/kick <name> [minutes]", "send a player off the town's lots (10 minutes before they can return)"],
    ["/unkick <name>", "let a kicked player back in now"],
    ["/reports [all]", "players' reports about other players"],
    ["/reports done <id|all>", "mark reports as dealt with"],
    ["/flags [all]", "cheat-check flags (online only) waiting for a look"],
    ["/flags ok <id|all>", "mark flags as looked at"]
  ]],
  ["Events and XP", [
    ["/event", "event types, and events on now or coming up"],
    ["/event start <type> [hours] [title]", "start one now - e.g. /event start doublexp 3"],
    ["/event at <yyyy-mm-dd> <time> <type> [hours] [title]", "schedule one - e.g. /event at 2026-12-24 6pm holiday 30"],
    ["/event stop <type|all>", "end events early"],
    ["/xp [name]", "a player's level and XP (no name: the top 10)"],
    ["/xp give <name> <amount>", "give (or take, with minus) XP"],
    ["/xp settings", "what everything is worth in XP"],
    ["/xp set <setting> <value>", "change an amount - e.g. /xp set PerMinute 3"]
  ]],
  ["Game files", [
    ["/files", "the Sims 1 files Admins and Testers can download"],
    ["/files folder <path>", "where the Sims 1 folder is on this PC"],
    ["/files rescan", "look at the game folder again"]
  ]],
  ["Running the server", [
    ["/status", "accounts, who's on, game files, memory"],
    ["/name <text>", "the server's name"],
    ["/save", "save everything now"],
    ["/backup", "save and keep a dated copy in backups/"],
    ["/backups", "the backups (one is made automatically every day)"],
    ["/restore <backup> confirm", "put a backup back (restarts the server)"],
    ["/restart", "restart the server"],
    ["/quit", "save and stop the server"],
    ["/help", "the server's own list"]
  ]]
];

/* On the roadmap, not built yet: listed so there's one place to look. */
const PLANNED = [
  ["/promote", "copy the Test server's content to Live"],
  ["/traveler", "become The Traveler in-game"]
];

/** Every real command name, for tab completion. */
function commandNames() {
  const out = [];
  for (const [, rows] of COMMANDS)
    for (const [sig] of rows) {
      const m = /^\/([a-z]+)/.exec(sig);
      if (m && out.indexOf(m[1]) < 0) out.push(m[1]);
    }
  return out.sort();
}

/** A name or password typed into the panel, made safe for a single console line. */
function oneLine(s) { return String(s == null ? "" : s).replace(/[\r\n]+/g, " ").trim(); }

/* ---------- the Cloudflare tunnel (a Windows service called cloudflared) ---------- */
/** Reads `sc query <service>`: "running", "stopped", "starting", "stopping", "missing" (not installed) or "unknown". */
function readServiceState(text, code) {
  const t = String(text || "");
  if (code === 1060 || /1060|does not exist|not exist as an installed service/i.test(t)) return "missing";
  const m = t.match(/STATE\s*:\s*(\d+)/i);
  if (!m) return "unknown";
  switch (parseInt(m[1], 10)) {
    case 1: return "stopped";
    case 2: return "starting";
    case 3: return "stopping";
    case 4: return "running";
    default: return "unknown";
  }
}
/** Only plain service names (letters, digits, - _ .) are ever passed to Windows. */
function safeServiceName(n) { n = String(n || "").trim(); return /^[A-Za-z0-9_.-]{1,64}$/.test(n) ? n : "cloudflared"; }

module.exports = { readServiceState, safeServiceName,
  CHANNELS, DEFAULTS,
  guessServerExe, serverCommand, isFile,
  defaultDataDir, dataPaths, readAccounts, readServerConfig,
  shippedNotesFile, verCmp, cleanPatch, mergePatchNotes, readPatchNotes, savePatch, hidePatch, resetPatch,
  loadSettings, saveSettings, profile, serverArgs,
  getJSON, readHealth, readPublic,
  COMMANDS, PLANNED, commandNames, oneLine
};
