/* ============================================================
   The Sims Redux — server panel, the window
   ============================================================
   Nothing here touches the machine directly: every action goes through
   window.panel (the preload bridge). Text from the servers is always put in
   with textContent, never pasted in as HTML.
   ============================================================ */
"use strict";
(function () {
const $ = id => document.getElementById(id);
const P = window.panel;
const CH = ["live", "test"];
let ACTIVE = "live", NAMES = [], CMDS = [], SOON = [], SET = {}, EXE = "";
const RUN = { live: false, test: false };
const TAIL = { live: [], test: [] };
const LOCAL = { live: {}, test: {} }, PUBLIC = { live: {}, test: {} };
const PROFILE = { live: {}, test: {} };
const HIST = []; let hi = 0;

/* ---------- console ---------- */
const con = $("console");
function atBottom(){ return con.scrollHeight - con.scrollTop - con.clientHeight < 40; }
function clock(at){ const d = new Date(at || Date.now());
  return String(d.getHours()).padStart(2,"0")+":"+String(d.getMinutes()).padStart(2,"0")+":"+String(d.getSeconds()).padStart(2,"0"); }
let lastClock = "";
function line(l){
  const stick = atBottom();
  const empty = con.querySelector(".empty"); if (empty) empty.remove();
  const d = document.createElement("div");
  d.className = "l";
  const c = clock(l.at);
  const t = document.createElement("span"); t.className = "t"; t.textContent = c === lastClock ? "" : c;
  lastClock = c;
  const s = document.createElement("span"); s.className = l.kind; s.textContent = l.text;
  d.appendChild(t); d.appendChild(s); con.appendChild(d);
  while (con.childElementCount > 3000) con.removeChild(con.firstChild);
  if (stick) con.scrollTop = con.scrollHeight;
}
function blank(msg){ con.innerHTML = ""; lastClock = ""; const d = document.createElement("div"); d.className="empty"; d.textContent = msg; con.appendChild(d); }
function redrawConsole(){
  con.innerHTML = ""; lastClock = "";
  const t = TAIL[ACTIVE];
  if (t.length) { t.forEach(line); con.scrollTop = con.scrollHeight; }
  else if (!EXE) blank("The server program isn't built yet. In Visual Studio: Rebuild Solution (it goes to Client\\Server\\bin). Or choose it in Settings.");
  else blank("The " + ACTIVE.toUpperCase() + " server isn't running. Press START.");
}
function note(kind, text){ const l = { channel: ACTIVE, kind, text, at: Date.now() }; TAIL[ACTIVE].push(l); line(l); }

/* ---------- status ---------- */
function setStat(el, cls, text){
  el.className = "stat" + (cls ? " " + cls : "");
  el.querySelector(".val").textContent = text;
}
function paintLocal(){
  const h = LOCAL[ACTIVE] || {};
  const el = $("stGame");
  if (h.foreign)                   setStat(el, "warn", "running outside this panel");
  else if (h.state === "up")       setStat(el, "up",   (h.players|0) + " on a lot · " + (h.online|0) + " active · v" + (h.version||"?"));
  else if (h.state === "strained") setStat(el, "warn", h.detail || "unhappy");
  else if (h.state === "starting") setStat(el, "busy warn", h.detail || "starting…");
  else                             setStat(el, "", "stopped");
  chips();
}
function paintPublic(){
  const p = PUBLIC[ACTIVE] || {};
  const el = $("stEdge");
  $("edgeName").textContent = p.edge === "direct" ? "PUBLIC ADDRESS" : "CLOUDFLARE";
  if (p.state === "up")          setStat(el, "up", "reachable" + (p.players ? " · " + p.players + " on a lot" : ""));
  else if (p.state === "origin") setStat(el, "warn", p.detail || "edge up · this PC unreachable");
  else if (p.state === "bad")    setStat(el, "warn", p.detail);
  else if (p.state === "off")    setStat(el, "", p.detail || "no public address");
  else if (p.state)              setStat(el, "bad",  p.detail || "unreachable");
  else                           setStat(el, "", "checking…");
}
function dur(s){
  s = s|0; if (s < 60) return s + "s";
  const m = Math.floor(s/60); if (m < 60) return m + "m";
  const h = Math.floor(m/60); if (h < 48) return h + "h " + (m%60) + "m";
  return Math.floor(h/24) + "d " + (h%24) + "h";
}
function chips(){
  const c = $("chips"); c.innerHTML = "";
  const add = (k, v) => { const d = document.createElement("span"); d.className = "chip";
    d.textContent = k + " "; const b = document.createElement("b"); b.textContent = v; d.appendChild(b); c.appendChild(d); };
  const L = LOCAL[ACTIVE] || {};
  if (L.state === "up") {
    add("lots open", L.lots|0);
    add("accounts", L.accounts|0);
    if (L.waiting) add("waiting for approval", L.waiting|0);
    add("files", L.files && L.files.ready ? (L.files.mb|0) + " MB" : "not set");
    add("sign-ups", L.registration ? "open" : "closed");
    add("up", dur(L.uptime));
    add("mem", (L.rssMB|0) + " MB");
  }
  add("port", (PROFILE[ACTIVE] && PROFILE[ACTIVE].port) || "?");
}
function paintTabs(){
  document.querySelectorAll(".srv").forEach(b => {
    const ch = b.dataset.ch;
    b.classList.toggle("on", ch === ACTIVE);
    b.classList.toggle("run", !!RUN[ch]);
    b.setAttribute("aria-selected", ch === ACTIVE ? "true" : "false");
  });
}

/* ---------- running state ---------- */
function setRunning(){
  const r = RUN[ACTIVE];
  $("bStart").disabled = r || !EXE;
  $("bStop").disabled = !r;
  $("bRestart").disabled = !r;
  $("cmd").disabled = !r;
  $("bSend").disabled = !r;
  $("cmd").placeholder = r ? "/help  ·  commands for the " + ACTIVE.toUpperCase() + " server" : "start the server to type commands";
  paintTabs();
}
function switchTo(ch){
  if (CH.indexOf(ch) < 0) return;
  ACTIVE = ch;
  P.setActive(ch);
  setRunning(); redrawConsole(); paintLocal(); paintPublic(); say("");
  if (!$("acc").classList.contains("hidden")) loadAccounts();
}

/* ---------- commands panel ---------- */
function drawCommands(filter){
  const body = $("sideBody"); body.innerHTML = "";
  const f = String(filter || "").trim().toLowerCase();
  const hit = (a, b) => !f || (a + " " + b).toLowerCase().indexOf(f) >= 0;
  let any = false;
  const sig2 = (sig, s) => sig.split(/(<[^>]+>|\[[^\]]+\])/).forEach(part => {
    if (!part) return;
    if (/^[<[]/.test(part)) { const e = document.createElement("em"); e.textContent = part; s.appendChild(e); }
    else s.appendChild(document.createTextNode(part));
  });
  for (const [title, rows] of CMDS) {
    const keep = rows.filter(r => hit(r[0], r[1]));
    if (!keep.length) continue;
    any = true;
    const h = document.createElement("div"); h.className = "sect"; h.textContent = title; body.appendChild(h);
    for (const [sig, what] of keep) {
      const b = document.createElement("button"); b.className = "cmd"; b.type = "button";
      const s = document.createElement("span"); s.className = "sig"; sig2(sig, s);
      const w = document.createElement("span"); w.className = "what"; w.textContent = what;
      b.appendChild(s); b.appendChild(w);
      b.addEventListener("click", () => {
        const base = /^\/[a-z]+(?: (?:add|folder|rescan|import|open|close|game))?/.exec(sig);
        const el = $("cmd");
        el.value = base ? base[0] + (/[<[]/.test(sig) ? " " : "") : "";
        if (!RUN[ACTIVE]) say("Start the server first — then this command will go through.");
        el.focus();
      });
      body.appendChild(b);
    }
  }
  const keepSoon = SOON.filter(r => hit(r[0], r[1]));
  if (keepSoon.length) {
    any = true;
    const h = document.createElement("div"); h.className = "sect future"; h.textContent = "Coming in later builds"; body.appendChild(h);
    for (const [sig, what] of keepSoon) {
      const b = document.createElement("div"); b.className = "cmd soon";
      const s = document.createElement("span"); s.className = "sig"; s.textContent = sig;
      const w = document.createElement("span"); w.className = "what"; w.textContent = what;
      b.appendChild(s); b.appendChild(w); body.appendChild(b);
    }
  }
  if (!any) { const n = document.createElement("div"); n.className = "note"; n.textContent = "Nothing matches that."; body.appendChild(n); }
}
function toggleSide(on){
  const s = $("side");
  const show = on === undefined ? s.classList.contains("hidden") : on;
  s.classList.toggle("hidden", !show);
  if (show) { drawCommands($("cmdFind").value); $("cmdFind").focus(); }
}

/* ---------- the input line ---------- */
function say(msg){ $("hint").textContent = msg || ""; }
/* passwords typed into commands aren't kept on screen or in the history */
function scrub(v){
  let m = /^(\/account add .+ )(\S+)( (?:admin|tester|player))$/i.exec(v);
  if (m) return m[1] + "••••••" + m[3];
  m = /^(\/account add .+ )(\S+)$/i.exec(v);
  if (m) return m[1] + "••••••";
  return v.replace(/^(\/resetpw .+ )(\S+)$/i, (x, a) => a + "••••••");
}
async function submit(){
  const el = $("cmd"); const v = el.value.trim();
  if (!v) return;
  note("you", "› " + scrub(v));
  HIST.push(scrub(v) === v ? v : scrub(v)); if (HIST.length > 200) HIST.shift(); hi = HIST.length;
  el.value = ""; say("");
  const r = await P.cmd(ACTIVE, v);
  if (r && r.ok === false) note("err", r.error || "that didn't go through");
}
function complete(){
  const el = $("cmd"), v = el.value;
  if (v[0] !== "/" || /\s/.test(v)) return;
  const want = v.slice(1).toLowerCase();
  const hits = NAMES.filter(n => n.indexOf(want) === 0);
  if (!hits.length) { say("no command starts with " + v); return; }
  if (hits.length === 1) { el.value = "/" + hits[0] + " "; say(""); return; }
  let common = hits[0];
  for (const h of hits) { let i = 0; while (i < common.length && common[i] === h[i]) i++; common = common.slice(0, i); }
  el.value = "/" + common;
  say(hits.map(h => "/" + h).join("  "));
}

/* ---------- accounts ---------- */
function fmtDate(s){ if (!s || /^0001/.test(s)) return "never"; const d = new Date(s); return isNaN(d) ? "?" : d.toLocaleDateString() + " " + d.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" }); }
async function act(action, name, a, b){
  const r = await P.account(ACTIVE, action, name, a, b);
  if (!r || !r.ok) { $("accNote").textContent = (r && r.error) || "that didn't go through"; return false; }
  setTimeout(loadAccounts, 500);
  return true;
}
async function loadAccounts(){
  $("accTitle").textContent = "ACCOUNTS · " + ACTIVE.toUpperCase();
  const d = await P.accounts(ACTIVE);
  const body = $("accBody"); body.innerHTML = "";
  $("accReg").checked = !!d.registration;
  $("accReg").disabled = !d.running;
  $("newGo").disabled = !d.running;
  $("accNote").textContent = d.running ? "" : "start the " + ACTIVE + " server to make changes";
  const list = d.accounts || [];
  if (!list.length) {
    const e = document.createElement("div"); e.className = "empty2";
    e.textContent = "No accounts yet. Make yours above with the Admin role, then log in on the website.";
    body.appendChild(e); return;
  }
  const head = document.createElement("div"); head.className = "arow head";
  ["NAME", "ROLE", "LAST LOGIN", ""].forEach(t => { const s = document.createElement("span"); s.textContent = t; head.appendChild(s); });
  body.appendChild(head);
  for (const a of list) {
    const row = document.createElement("div"); row.className = "arow";
    const n = document.createElement("span"); n.className = "an" + (a.suspended ? " sus" : "") + (a.pending ? " pend" : "");
    n.textContent = a.name + (a.suspended ? " (suspended)" : "") + (a.pending ? " (waiting for approval)" : "");
    const role = document.createElement("select");
    for (const r of ["admin", "tester", "player"]) { const o = document.createElement("option"); o.value = r; o.textContent = r[0].toUpperCase() + r.slice(1); role.appendChild(o); }
    role.value = a.role; role.className = "role-" + a.role; role.disabled = !d.running;
    role.addEventListener("change", () => act("role", a.name, role.value));
    const l = document.createElement("span"); l.className = "al"; l.textContent = fmtDate(a.lastLogin);
    const acts = document.createElement("span"); acts.className = "aa";
    const btn = (text, fn, cls) => { const b = document.createElement("button"); b.textContent = text; if (cls) b.className = cls; b.disabled = !d.running; b.addEventListener("click", fn); acts.appendChild(b); return b; };
    if (a.pending) btn("Approve", () => act("approve", a.name), "go");
    btn("New password", () => {
      /* an input right in the row, rather than a popup */
      acts.innerHTML = "";
      const box = document.createElement("span"); box.className = "pw";
      const i = document.createElement("input"); i.type = "text"; i.placeholder = "new password"; i.spellcheck = false;
      const ok = document.createElement("button"); ok.textContent = "SET"; ok.className = "go";
      const no = document.createElement("button"); no.textContent = "×";
      ok.addEventListener("click", async () => { if (await act("resetpw", a.name, i.value)) $("accNote").textContent = "new password set for " + a.name; });
      no.addEventListener("click", loadAccounts);
      i.addEventListener("keydown", e => { if (e.key === "Enter") ok.click(); if (e.key === "Escape") no.click(); });
      box.appendChild(i); box.appendChild(ok); box.appendChild(no); acts.appendChild(box); i.focus();
    });
    btn(a.suspended ? "Unsuspend" : "Suspend", () => act(a.suspended ? "unsuspend" : "suspend", a.name));
    const del = btn("Delete", () => {
      if (del.dataset.sure) { act("delete", a.name); return; }
      del.dataset.sure = "1"; del.textContent = "Really delete?";
      setTimeout(() => { if (del.isConnected) { delete del.dataset.sure; del.textContent = "Delete"; } }, 4000);
    }, "danger");
    row.appendChild(n); row.appendChild(role); row.appendChild(l); row.appendChild(acts);
    body.appendChild(row);
  }
}
$("bAccounts").addEventListener("click", () => { $("acc").classList.remove("hidden"); loadAccounts(); });
$("accClose").addEventListener("click", () => $("acc").classList.add("hidden"));
$("accReg").addEventListener("change", e => act("registration", "", e.target.checked ? "on" : "off"));
$("newGo").addEventListener("click", async () => {
  const name = $("newName").value.trim(), pw = $("newPw").value;
  if (!name || !pw) { $("accNote").textContent = "give a name and a password"; return; }
  if (await act("add", name, pw, $("newRole").value)) { $("newName").value = ""; $("newPw").value = ""; $("accNote").textContent = "added " + name + " (check the console if it doesn't appear)"; }
});
$("newPw").addEventListener("keydown", e => { if (e.key === "Enter") $("newGo").click(); });

/* ---------- game files ---------- */
function paintFiles(){
  $("gfDir").value = SET.gameFolder || "";
  const st = $("gfState");
  const L = LOCAL[ACTIVE] || {};
  if (L.state !== "up") { st.className = "gfState"; st.textContent = "Start the " + ACTIVE + " server to check the files."; return; }
  const f = L.files || {};
  if (f.ready) { st.className = "gfState ok"; st.textContent = "Ready: " + (f.count|0) + " files, " + (f.mb|0) + " MB. Admins and Testers will see “Get the game files from the server” on the website."; }
  else { st.className = "gfState bad"; st.textContent = "Not available: " + (f.problem || "no folder set"); }
}
$("bFiles").addEventListener("click", () => { $("gf").classList.remove("hidden"); paintFiles(); P.poll(); });
$("gfClose").addEventListener("click", () => $("gf").classList.add("hidden"));
$("gfPick").addEventListener("click", async () => {
  const r = await P.pickGameFolder();
  if (!r || !r.ok) { if (r && r.error) { $("gfState").className = "gfState bad"; $("gfState").textContent = r.error; } return; }
  SET.gameFolder = r.dir; paintFiles();
  setTimeout(() => P.poll(), 800);
});
$("gfOpen").addEventListener("click", async () => { const r = await P.open(ACTIVE, "game"); if (r && !r.ok) $("gfState").textContent = r.error; });

/* ---------- wiring ---------- */
async function boot(){
  const d = await P.init();
  SET = d.settings || {}; CMDS = d.commands || []; SOON = d.planned || []; NAMES = d.names || []; EXE = d.exe || "";
  for (const ch of CH) {
    const s = d.servers[ch] || {};
    RUN[ch] = !!s.running; TAIL[ch] = s.tail || []; PROFILE[ch] = s.profile || {};
  }
  ACTIVE = CH.indexOf(SET.active) >= 0 ? SET.active : "live";
  setRunning(); redrawConsole(); fillCfg(); chips(); drawCommands("");
  if (!EXE) say("Build the solution in Visual Studio first, or choose SimsRedux.Server.exe in Settings.");
}

P.onConsole(l => { TAIL[l.channel].push(l); if (TAIL[l.channel].length > 4000) TAIL[l.channel].shift(); if (l.channel === ACTIVE) line(l); });
P.onRunning(v => { RUN[v.channel] = !!v.running; setRunning(); });
P.onLocal(h => { LOCAL[h.channel] = h; if (h.channel === ACTIVE) { paintLocal(); if (!$("gf").classList.contains("hidden")) paintFiles(); } paintTabs(); });
P.onPublic(p => { PUBLIC[p.channel] = p; if (p.channel === ACTIVE) paintPublic(); });

/* ---- the Cloudflare tunnel switch ---- */
let TUNNEL = { state: "unknown" };
function paintTunnel(){
  const b = $("bTunnel"), t = TUNNEL;
  b.style.display = t.state === "unsupported" ? "none" : "";
  b.classList.remove("on", "off");
  if (t.busy)                       { b.textContent = "switching…"; b.disabled = true; return; }
  b.disabled = false;
  if (t.state === "running")        { b.textContent = "Turn tunnel off"; b.classList.add("on"); }
  else if (t.state === "stopped")   { b.textContent = "Turn tunnel on";  b.classList.add("off"); }
  else if (t.state === "starting" || t.state === "stopping") { b.textContent = t.state + "…"; b.disabled = true; }
  else if (t.state === "missing")   { b.textContent = "no tunnel service"; b.disabled = true;
                                      b.title = "There's no Windows service called " + (t.name || "cloudflared") + " on this PC"; }
  else                              { b.textContent = "tunnel ?"; }
}
P.onTunnel(t => { TUNNEL = t; paintTunnel(); });
$("bTunnel").addEventListener("click", async () => {
  const on = TUNNEL.state !== "running";
  if (!on && !confirm("Turn the Cloudflare tunnel off?\n\nNobody outside this PC will be able to reach the website's account login, the game files or the online town until you turn it back on.\n\nWindows will ask for permission.")) return;
  TUNNEL.busy = true; paintTunnel();
  const r = await P.tunnel(on);
  if (!r.ok) note("err", "Tunnel: " + r.error);
});

document.querySelectorAll(".srv").forEach(b => b.addEventListener("click", () => switchTo(b.dataset.ch)));
$("bStart").addEventListener("click", async () => { say(""); const r = await P.start(ACTIVE); if (r && !r.ok) { note("err", r.error); say(r.error); } });
$("bStop").addEventListener("click", async () => { const r = await P.stop(ACTIVE, false); if (r && !r.ok) say(r.error); });
$("bRestart").addEventListener("click", async () => { const r = await P.restart(ACTIVE); if (r && !r.ok) say(r.error); });
document.querySelectorAll("[data-open]").forEach(b =>
  b.addEventListener("click", async () => { const r = await P.open(ACTIVE, b.dataset.open); if (r && !r.ok) say(r.error); }));
$("bSite").addEventListener("click", () => P.site());
$("bCmds").addEventListener("click", () => toggleSide());
$("sideClose").addEventListener("click", () => toggleSide(false));
$("cmdFind").addEventListener("input", e => drawCommands(e.target.value));
$("bSend").addEventListener("click", submit);
$("bClear").addEventListener("click", async () => { await P.clear(ACTIVE); TAIL[ACTIVE] = []; blank("Cleared."); say(""); });

/* settings */
function fillCfg(){
  $("fExe").value = EXE || "(not found — build the solution, or choose it)";
  $("fLivePort").value = (SET.live && SET.live.port) || 37564;
  $("fLiveUrl").value = (SET.live && SET.live.publicUrl) || "";
  $("fLiveData").value = (SET.live && SET.live.dataDir) || "";
  $("fTestPort").value = (SET.test && SET.test.port) || 37566;
  $("fTestUrl").value = (SET.test && SET.test.publicUrl) || "";
  $("fTestData").value = (SET.test && SET.test.dataDir) || "";
  $("fSite").value = SET.site || "";
}
function openCfg(){ $("cfg").classList.remove("hidden"); $("cfgMsg").textContent = ""; }
$("bCfg").addEventListener("click", () => { fillCfg(); openCfg(); });
$("cfgClose").addEventListener("click", () => $("cfg").classList.add("hidden"));
$("bExe").addEventListener("click", async () => {
  const r = await P.pickExe();
  if (!r || !r.ok) return;
  EXE = r.exe; $("fExe").value = EXE; $("cfgMsg").textContent = "server program set"; setRunning();
});
$("cfgSave").addEventListener("click", async () => {
  const lp = parseInt($("fLivePort").value, 10), tp = parseInt($("fTestPort").value, 10);
  if (lp === tp) { $("cfgMsg").textContent = "Live and Test need different ports"; return; }
  if (lp === 37565 || tp === 37565) { $("cfgMsg").textContent = "37565 is where the game hosts — pick another"; return; }
  const r = await P.settings({
    site: $("fSite").value,
    live: { port: lp, publicUrl: $("fLiveUrl").value, dataDir: $("fLiveData").value },
    test: { port: tp, publicUrl: $("fTestUrl").value, dataDir: $("fTestData").value }
  });
  if (r && r.ok) {
    SET = r.settings;
    for (const ch of CH) PROFILE[ch] = r.servers[ch].profile;
    chips(); $("cfg").classList.add("hidden");
    if (CH.some(c => RUN[c])) say("Saved. Restart a running server for a new port or folder to take effect.");
  }
});

$("cmd").addEventListener("keydown", e => {
  if (e.key === "Enter") { e.preventDefault(); submit(); return; }
  if (e.key === "Tab") { e.preventDefault(); complete(); return; }
  if (e.key === "ArrowUp") { if (hi > 0) { hi--; e.target.value = HIST[hi] || ""; } e.preventDefault(); return; }
  if (e.key === "ArrowDown") { if (hi < HIST.length) { hi++; e.target.value = HIST[hi] || ""; } e.preventDefault(); return; }
});
document.addEventListener("keydown", e => {
  if (e.key !== "Escape") return;
  for (const id of ["acc", "gf", "cfg"]) if (!$(id).classList.contains("hidden")) { $(id).classList.add("hidden"); return; }
  if (!$("side").classList.contains("hidden")) toggleSide(false);
});

boot();
})();
