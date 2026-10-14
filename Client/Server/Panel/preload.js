/* The only road between the window and the machine. Every call is a named job the main
   process already knows how to do; nothing here runs a path or command from the page. */
"use strict";
const { contextBridge, ipcRenderer } = require("electron");

contextBridge.exposeInMainWorld("panel", {
  init:       ()            => ipcRenderer.invoke("panel:init"),
  start:      c             => ipcRenderer.invoke("panel:start", String(c)),
  stop:       (c, hard)     => ipcRenderer.invoke("panel:stop", String(c), !!hard),
  restart:    c             => ipcRenderer.invoke("panel:restart", String(c)),
  cmd:        (c, line)     => ipcRenderer.invoke("panel:cmd", String(c), String(line || "")),
  clear:      c             => ipcRenderer.invoke("panel:clear", String(c)),
  poll:       ()            => ipcRenderer.invoke("panel:poll"),
  setActive:  c             => ipcRenderer.invoke("panel:setActive", String(c)),
  open:       (c, what)     => ipcRenderer.invoke("panel:open", String(c), String(what || "data")),
  site:       ()            => ipcRenderer.invoke("panel:site"),
  pickGameFolder: ()        => ipcRenderer.invoke("panel:pickGameFolder"),
  pickExe:    ()            => ipcRenderer.invoke("panel:pickExe"),
  settings:   patch         => ipcRenderer.invoke("panel:settings", patch),
  accounts:   c             => ipcRenderer.invoke("panel:accounts", String(c)),
  account:    (c, action, name, a, b) => ipcRenderer.invoke("panel:account", String(c), String(action), String(name || ""), String(a || ""), String(b || "")),

  onConsole: fn => ipcRenderer.on("console", (e, line) => fn(line)),
  onRunning: fn => ipcRenderer.on("running", (e, v) => fn(v)),
  onLocal:   fn => ipcRenderer.on("status:local", (e, v) => fn(v)),
  onPublic:  fn => ipcRenderer.on("status:public", (e, v) => fn(v))
});
