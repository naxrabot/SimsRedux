// Tests for core.js (no window needed):  node test-core.mjs
import { createRequire } from "module";
import fs from "fs";
import os from "os";
import path from "path";
const require = createRequire(import.meta.url);
const C = require("./core.js");

let failed = 0;
const ok = (cond, what) => { if (cond) console.log("ok   " + what); else { failed++; console.log("FAIL " + what); } };

// settings: defaults, and both servers always present
const tmp = fs.mkdtempSync(path.join(os.tmpdir(), "srxpanel-"));
const f = path.join(tmp, "panel.json");
let S = C.loadSettings(f);
ok(S.live.port === 37564 && S.test.port === 37566, "default ports: live 37564, test 37566");
ok(S.live.publicUrl === "https://tsr-lot.mbrew.studio", "live public address is the tunnel");
fs.writeFileSync(f, JSON.stringify({ active: "nope", live: { port: 40000 } }));
S = C.loadSettings(f);
ok(S.active === "live" && S.live.port === 40000 && S.test.port === 37566 && S.live.publicUrl, "partial settings are filled in");
ok(C.saveSettings(f, S) && C.loadSettings(f).live.port === 40000, "settings save and load");

// how the server is started
S.gameFolder = "E:\\Steam\\The Sims";
const a = C.serverArgs(S, "test", "/x/Client/Server/Panel");
ok(a.join(" ") === ["--data", path.resolve("/x/Client/Server/ServerData/test"), "--port", "37566", "--channel", "test", "--game-folder", "E:\\Steam\\The Sims"].join(" "), "server arguments");
S.gameFolder = "";
ok(C.serverArgs(S, "live", "/x").indexOf("--game-folder") < 0, "no game folder argument when none is set");
ok(C.serverCommand("C:\\a\\SimsRedux.Server.exe").cmd.endsWith(".exe"), "an .exe runs directly");
ok(C.serverCommand("/a/SimsRedux.Server.dll").cmd === "dotnet", "a .dll runs through dotnet");

// health
ok(C.readHealth(null, false).state === "off", "nothing answering, not started = off");
ok(C.readHealth({ ok: false }, true).state === "starting", "nothing answering, started = starting");
ok(C.readHealth({ ok: true, status: 200, json: { ok: true, players: 2, accounts: 5, files: { ready: true } } }, true).state === "up", "healthy");
ok(C.readHealth({ ok: true, status: 200, json: { hello: 1 } }, true).state === "strained", "something else on the port");

// public address
ok(C.readPublic({ ok: true, status: 530, headers: { "cf-ray": "x" }, body: "error code: 1033" }).state === "origin", "Cloudflare 1033 = tunnel down");
ok(C.readPublic({ ok: true, status: 502, headers: { server: "cloudflare" } }).state === "origin", "502 behind Cloudflare = server not answering");
ok(C.readPublic({ ok: true, status: 200, headers: { "cf-ray": "x" }, json: { ok: true, players: 1 } }).state === "up", "reachable through Cloudflare");
ok(C.readPublic({ ok: false, error: "ENOTFOUND" }).state === "down", "no answer = down");

// accounts are read without their password hashes
const dd = path.join(tmp, "data");
fs.mkdirSync(dd);
fs.writeFileSync(path.join(dd, "accounts.json"), JSON.stringify([
  { name: "zed", role: "player", hash: "SECRET", salt: "SALT" },
  { name: "Joey", role: "admin", hash: "SECRET", salt: "SALT", suspended: false }
]));
const acc = C.readAccounts(dd);
ok(acc.length === 2 && acc[0].name === "Joey", "accounts read and sorted");
ok(!JSON.stringify(acc).includes("SECRET") && !JSON.stringify(acc).includes("SALT"), "password hashes never leave the file");
ok(C.readAccounts(path.join(tmp, "nothing")).length === 0, "no accounts file = no accounts");
fs.writeFileSync(path.join(dd, "accounts.json"), JSON.stringify([
  { name: "Ann", role: "player" }, { name: "zoe", role: "player", pending: true }
]));
const acc2 = C.readAccounts(dd);
ok(acc2[0].name === "zoe" && acc2[0].pending && !acc2[1].pending, "accounts waiting for approval come first");

// commands
const names = C.commandNames();
ok(names.includes("role") && names.includes("files") && names.includes("quit"), "command names for tab completion");
ok(C.oneLine("a\nb\r\nc") === "a b c", "commands from the panel stay on one line");

// the Cloudflare tunnel service
ok(C.readServiceState("SERVICE_NAME: cloudflared\n        STATE              : 4  RUNNING", 0) === "running", "tunnel running");
ok(C.readServiceState("        STATE              : 1  STOPPED ", 0) === "stopped", "tunnel stopped");
ok(C.readServiceState("[SC] EnumQueryServicesStatus:OpenService FAILED 1060:", 1060) === "missing", "tunnel not installed");
ok(C.safeServiceName("cloudflared") === "cloudflared" && C.safeServiceName("x'; rm -rf /") === "cloudflared", "only plain service names reach Windows");

fs.rmSync(tmp, { recursive: true, force: true });
console.log(failed ? "\n" + failed + " FAILED" : "\nall passed");
process.exit(failed ? 1 : 0);
