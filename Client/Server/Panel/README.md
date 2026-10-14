# The Sims Redux — server panel

One window to run The Sims Redux dedicated servers: start and stop them, watch
their consoles, type commands, manage accounts, and see whether players can
reach them through Cloudflare.

## Running it

You need [Node.js](https://nodejs.org) (the same one the Red is Dead panel uses).

1. Build the solution in Visual Studio (**Rebuild Solution**). That puts the
   server program in `Client\Server\bin\SimsRedux.Server.exe`.
2. Double-click `Client\Server\Panel\start.bat`. The first run downloads
   Electron once; after that it opens in a couple of seconds.

## Live and Test

The **LIVE / TEST** switch at the top picks which server the window is showing.
Each has its own port, data folder (accounts, settings, log, backups) and
console, and both can run at once.

| | Port | Data folder | Public address |
|---|---|---|---|
| Live | 37564 | `Client\Server\ServerData\live` | https://tsr-lot.mbrew.studio |
| Test | 37566 | `Client\Server\ServerData\test` | none yet (needs its own tunnel route) |

Port 37565 is kept for the game: while drop 2 isn't done, a lot you host from
the game runs on 37565 behind the Live server, which passes players through.

## The two status lights

**GAME SERVER** is this PC: is the server running and answering.
**CLOUDFLARE** is the public address checked from outside. "Edge up · this PC
unreachable" means Cloudflare is fine but the tunnel (cloudflared) or the
server isn't — see the Tailscale note below.

## Accounts

**Accounts…** lists everyone with their role:

- **Admin** — you. Can download the Sims 1 files from the server.
- **Tester** — people you choose. Can also download the Sims 1 files.
- **Player** — everyone else. Must use their own Sims 1 folder.

Change a role from the drop-down, set a new password, suspend or delete. The
checkbox at the top opens or closes making new accounts on the website. The
first time, make your own account here with the **Admin** role.

## Game files

**Game files…** → **Choose…** and pick the folder The Sims is installed in (the
one with `GameData` inside). Admins and Testers then see **Get the game files
from the server** on the website. Only files from that folder can ever be sent,
and only to Admin and Tester accounts.

## Console commands

**Commands** lists them all; click one to put it in the box. Tab completes.

## If the Cloudflare light says the tunnel is down

That's cloudflared on this PC, not the server. If Tailscale has an **exit node**
or **Use Tailscale subnets** turned on, cloudflared can't reach Cloudflare. Turn
both off on this PC (`tailscale set --exit-node=` and
`tailscale set --accept-routes=false` in a normal PowerShell window).

## Running the panel's tests

`node test-core.mjs`
