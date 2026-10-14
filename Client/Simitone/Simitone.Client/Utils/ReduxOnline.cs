using FSO.Common;
using FSO.Content;
using FSO.Files.Formats.IFF.Chunks;
using FSO.SimAntics;
using FSO.SimAntics.Model;
using FSO.SimAntics.NetPlay.Drivers;
using FSO.SimAntics.NetPlay.Model;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using FSO.Common.IO;
namespace Simitone.Client.Utils
{
    /// <summary>
    /// SimsRedux online play: one player hosts the lot they're playing, friends join it with a
    /// Sim from their own game.
    ///
    /// Everything travels over WebSockets, which pass through Cloudflare (tsr-lot.mbrew.studio) and
    /// also work from a web browser later on. The game itself already keeps every player's copy of
    /// the lot in step (FreeSO's lockstep "server driver" / "client driver"); this file is the
    /// connection between them, plus the TS1 parts: while a friend is on your lot their game uses
    /// a copy of your neighbourhood (families, neighbours, Sims), because the Sims 1 scripts read
    /// from it all the time and every player's game has to read the same thing.
    ///
    /// Every packet starts with one byte saying what it is (ReduxPacket).
    /// </summary>
    public enum ReduxPacket : byte
    {
        VM = 0,          // [type byte][VM data]   lot ticks / commands
        Join = 10,       // guest -> host: who I am and the Sim I'm bringing
        Welcome = 11,    // host -> guest: your player id, the lot's name
        Hood = 12,       // host -> guest: the host's neighbourhood files
        AddVisitor = 13, // host -> guests: another friend's Sim (character file + neighbour data)
        Bye = 14,        // either way: leaving / refused, with a reason
    }

    public static class ReduxOnlineInfo
    {
        public const int Version = 1;
        public const int DefaultPort = 37564;
        /// <summary>Where the game hosts when the dedicated server is running on DefaultPort (it passes players along).</summary>
        public const int BehindServerPort = 37565;

        /// <summary>
        /// The port to host on: the usual one, unless something on this PC (the dedicated server,
        /// started from the server panel) already answers there - then the one behind it.
        /// </summary>
        public static int HostPort()
        {
            try
            {
                using (var probe = new TcpClient())
                {
                    var connect = probe.ConnectAsync(IPAddress.Loopback, DefaultPort);
                    if (connect.Wait(400) && probe.Connected)
                    {
                        Log("[host] The dedicated server is running on port " + DefaultPort + " - hosting behind it on " + BehindServerPort);
                        return BehindServerPort;
                    }
                }
            }
            catch { }
            return DefaultPort;
        }
        public const uint HostID = 65537;
        public const uint FirstGuestID = 100001;
        public const int MaxMessage = 64 * 1024 * 1024;
        public const string DefaultAddress = "tsr-lot.mbrew.studio";
        /// <summary>The lot server's old address (tsr.mbrew.studio is the game's website now).</summary>
        public const string OldAddress = "tsr.mbrew.studio";

        /// <summary>A saved or typed address that still says the old one gets the new one.</summary>
        public static string Upgrade(string typed)
        {
            var t = (typed ?? "").Trim();
            if (t == OldAddress || t == OldAddress + "/lot" || t == "wss://" + OldAddress + "/lot") return DefaultAddress;
            return t;
        }

        /// <summary>
        /// Turns what the player typed into a WebSocket address:
        /// "tsr-lot.mbrew.studio" -> wss://tsr-lot.mbrew.studio/lot, "localhost" -> ws://localhost:37564/,
        /// "192.168.1.5" -> ws://192.168.1.5:37564/. Full ws:// or wss:// addresses are kept.
        /// </summary>
        public static Uri MakeAddress(string typed)
        {
            typed = Upgrade(typed);
            if (typed.Length == 0) typed = DefaultAddress;
            if (typed.StartsWith("ws://") || typed.StartsWith("wss://")) return new Uri(typed);
            if (typed.StartsWith("http://")) return new Uri("ws://" + typed.Substring(7));
            if (typed.StartsWith("https://")) return new Uri("wss://" + typed.Substring(8));

            var hostPart = typed.Split('/')[0];
            var hostOnly = hostPart.Split(':')[0];
            IPAddress ip;
            bool local = hostOnly == "localhost" || IPAddress.TryParse(hostOnly, out ip);
            if (local)
            {
                if (!hostPart.Contains(":")) typed = hostPart + ":" + DefaultPort + typed.Substring(hostPart.Length);
                return new Uri("ws://" + typed);
            }
            if (!typed.Contains("/")) typed += "/lot";
            return new Uri("wss://" + typed);
        }

        public static void Log(string text)
        {
            Console.WriteLine("[SimsRedux Online] " + text);
            if (OperatingSystem.IsBrowser()) return; // the browser console already has it
            try { FSOFile.AppendAllText(Path.Combine(FSOEnvironment.UserDir, "online_debug.log"), DateTime.Now.ToString("HH:mm:ss") + " " + text + "\r\n"); }
            catch { }
        }

        public static byte[] Pack(ReduxPacket kind, Action<BinaryWriter> body)
        {
            using (var ms = new MemoryStream())
            {
                using (var w = new BinaryWriter(ms))
                {
                    w.Write((byte)kind);
                    body?.Invoke(w);
                }
                return ms.ToArray();
            }
        }

        public static byte[] PackVM(VMNetMessage msg)
        {
            var data = new byte[msg.Data.Length + 2];
            data[0] = (byte)ReduxPacket.VM;
            data[1] = (byte)msg.Type;
            Buffer.BlockCopy(msg.Data, 0, data, 2, msg.Data.Length);
            return data;
        }

        public static VMNetMessage UnpackVM(byte[] data)
        {
            var payload = new byte[data.Length - 2];
            Buffer.BlockCopy(data, 2, payload, 0, payload.Length);
            return new VMNetMessage((VMNetMessageType)data[1], payload);
        }

        public static byte[] PackBye(string reason)
        {
            return Pack(ReduxPacket.Bye, w => w.Write(reason ?? ""));
        }

        /// <summary>File names sent between players must be plain names inside the neighbourhood folder.</summary>
        public static bool SafeRelativePath(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Length > 200) return false;
            if (path.Contains("..") || path.Contains(":") || path.StartsWith("/") || path.StartsWith("\\")) return false;
            foreach (var c in path)
                if (!(char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.' || c == '/' || c == ' ')) return false;
            return true;
        }
    }

    /// <summary>One WebSocket connection: queues whole messages coming in, sends messages in order.</summary>
    public class ReduxSocket
    {
        public WebSocket Socket;
        public ConcurrentQueue<byte[]> Incoming = new ConcurrentQueue<byte[]>();
        public volatile bool Closed;
        public string CloseReason = "";
        public string RemoteIP = "";

        // host side
        public VMNetClient Client;
        public string PlayerName = "";
        public DateTime Opened = DateTime.UtcNow;

        private Task SendChain = Task.CompletedTask;
        private readonly object SendLock = new object();

        public ReduxSocket(WebSocket socket)
        {
            Socket = socket;
        }

        public void Start()
        {
            Task.Run(ReceiveLoop);
        }

        private async Task ReceiveLoop()
        {
            var buffer = new byte[64 * 1024];
            var message = new MemoryStream();
            try
            {
                while (Socket.State == WebSocketState.Open)
                {
                    var result = await Socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        if (CloseReason.Length == 0) CloseReason = "The other player closed the connection.";
                        break;
                    }
                    message.Write(buffer, 0, result.Count);
                    if (message.Length > ReduxOnlineInfo.MaxMessage) throw new Exception("Message too big.");
                    if (result.EndOfMessage)
                    {
                        Incoming.Enqueue(message.ToArray());
                        message = new MemoryStream();
                    }
                }
            }
            catch (Exception e)
            {
                if (CloseReason.Length == 0) CloseReason = e.Message;
            }
            Closed = true;
        }

        public void Send(byte[] data)
        {
            if (Closed) return;
            lock (SendLock)
            {
                SendChain = SendChain.ContinueWith(async (t) =>
                {
                    if (Closed || Socket.State != WebSocketState.Open) return;
                    try
                    {
                        await Socket.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Binary, true, CancellationToken.None);
                    }
                    catch (Exception e)
                    {
                        if (CloseReason.Length == 0) CloseReason = e.Message;
                        Closed = true;
                    }
                }).Unwrap();
            }
        }

        /// <summary>Sends a goodbye (if a reason is given), then closes once everything queued has gone.</summary>
        public void Close(string reason = null)
        {
            if (reason != null) Send(ReduxOnlineInfo.PackBye(reason));
            lock (SendLock)
            {
                SendChain = SendChain.ContinueWith(async (t) =>
                {
                    try
                    {
                        if (Socket.State == WebSocketState.Open)
                            await Socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                    }
                    catch { }
                    Closed = true;
                }).Unwrap();
            }
        }
    }

    /// <summary>
    /// The host's side. Listens for friends (plain TCP with the WebSocket handshake done here, so
    /// Windows needs no special permissions and Cloudflare's tunnel can forward to it), lets them
    /// in with their Sim, and passes the lot's ticks between the game and everyone connected.
    /// </summary>
    public class ReduxHost
    {
        public int Port;
        public int GuestCount { get { lock (Sockets) return Sockets.Count(x => x.Client != null); } }
        /// <summary>Every open connection, including friends still joining.</summary>
        public int ConnectionCount { get { lock (Sockets) return Sockets.Count(x => !x.Closed) + Pending.Count; } }
        public List<string> GuestNames { get { lock (Sockets) return Sockets.Where(x => x.Client != null).Select(x => x.PlayerName).ToList(); } }
        public event Action<string> OnNotice;

        private VM vm;
        private VMServerDriver Driver;
        private TcpListener Listener;
        private volatile bool Running;
        private List<ReduxSocket> Sockets = new List<ReduxSocket>();
        private ConcurrentQueue<ReduxSocket> Pending = new ConcurrentQueue<ReduxSocket>();
        private uint NextID = ReduxOnlineInfo.FirstGuestID;

        // friends' Sims that were added to the host's neighbourhood for this session
        private List<Neighbour> VisitorNeighbours = new List<Neighbour>();
        private List<string> VisitorFiles = new List<string>();

        // commands a friend's game is allowed to send (no buying, building, money or time changes)
        private static readonly HashSet<VMCommandType> GuestCommands = new HashSet<VMCommandType>()
        {
            VMCommandType.Interaction,
            VMCommandType.Chat,
            VMCommandType.InteractionCancel,
            VMCommandType.Goto,
            VMCommandType.DialogResponse,
            VMCommandType.RequestResync,
            VMCommandType.InteractionResult,
            VMCommandType.Ping,
            VMCommandType.ResetSim,
            VMCommandType.DebugSim,
        };

        public ReduxHost(VM vm, VMServerDriver driver)
        {
            this.vm = vm;
            Driver = driver;
        }

        public bool Start(int port, out string error)
        {
            error = null;
            Port = port;
            try
            {
                Listener = new TcpListener(IPAddress.Any, port);
                if (OperatingSystem.IsWindows()) Listener.ExclusiveAddressUse = true; // never quietly share a port with the dedicated server
                Listener.Start();
            }
            catch (Exception e)
            {
                error = e.Message;
                ReduxOnlineInfo.Log("[host] Couldn't start hosting on port " + port + ": " + e.Message);
                return false;
            }
            Running = true;
            Driver.OnTickBroadcast += Broadcast;
            Driver.OnDirectMessage += Direct;
            Driver.OnDropClient += Drop;
            Task.Run(AcceptLoop);
            ReduxOnlineInfo.Log("[host] Hosting on port " + port);
            return true;
        }

        public void Stop(string reason)
        {
            if (!Running) return;
            Running = false;
            try { Listener?.Stop(); } catch { }
            Driver.OnTickBroadcast -= Broadcast;
            Driver.OnDirectMessage -= Direct;
            Driver.OnDropClient -= Drop;
            lock (Sockets)
            {
                foreach (var s in Sockets) s.Close(reason);
                Sockets.Clear();
            }
            RemoveVisitorsFromHood();
            ReduxOnlineInfo.Log("[host] Stopped hosting: " + reason);
        }

        private async Task AcceptLoop()
        {
            while (Running)
            {
                TcpClient tcp;
                try { tcp = await Listener.AcceptTcpClientAsync(); }
                catch { break; }
                _ = Task.Run(() => Handshake(tcp));
            }
        }

        /// <summary>The WebSocket opening handshake (RFC 6455), then hand the connection to the game.</summary>
        private async Task Handshake(TcpClient tcp)
        {
            try
            {
                tcp.NoDelay = true;
                var stream = tcp.GetStream();
                var header = new List<byte>();
                var one = new byte[1];
                while (true)
                {
                    int n = await stream.ReadAsync(one, 0, 1);
                    if (n == 0) throw new Exception("closed during handshake");
                    header.Add(one[0]);
                    int c = header.Count;
                    if (c >= 4 && header[c - 4] == '\r' && header[c - 3] == '\n' && header[c - 2] == '\r' && header[c - 1] == '\n') break;
                    if (c > 16384) throw new Exception("handshake too long");
                }
                var text = Encoding.ASCII.GetString(header.ToArray());
                string key = null, realIP = null;
                foreach (var line in text.Split(new[] { "\r\n" }, StringSplitOptions.None))
                {
                    var colon = line.IndexOf(':');
                    if (colon <= 0) continue;
                    var name = line.Substring(0, colon).Trim().ToLowerInvariant();
                    var value = line.Substring(colon + 1).Trim();
                    if (name == "sec-websocket-key") key = value;
                    else if (name == "cf-connecting-ip" || (name == "x-forwarded-for" && realIP == null)) realIP = value.Split(',')[0].Trim();
                }
                if (key == null)
                {
                    // someone opened the address in a normal browser
                    var body = "The Sims Redux lot server is running. Join from the game's Online button.";
                    var resp = "HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n" + body;
                    var rb = Encoding.ASCII.GetBytes(resp);
                    await stream.WriteAsync(rb, 0, rb.Length);
                    tcp.Close();
                    return;
                }
                var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
                var reply = "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: " + accept + "\r\n\r\n";
                var bytes = Encoding.ASCII.GetBytes(reply);
                await stream.WriteAsync(bytes, 0, bytes.Length);

                var ws = WebSocket.CreateFromStream(stream, new WebSocketCreationOptions()
                {
                    IsServer = true,
                    KeepAliveInterval = TimeSpan.FromSeconds(20)
                });
                var sock = new ReduxSocket(ws);
                sock.RemoteIP = realIP ?? ((tcp.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "?");
                sock.Start();
                Pending.Enqueue(sock);
            }
            catch (Exception e)
            {
                ReduxOnlineInfo.Log("[host] A connection failed during the handshake: " + e.Message);
                try { tcp.Close(); } catch { }
            }
        }

        // ---- called by the lot (VMServerDriver), possibly from another thread ----

        private void Broadcast(VMNetMessage msg, HashSet<VMNetClient> ignore)
        {
            var data = ReduxOnlineInfo.PackVM(msg);
            lock (Sockets)
            {
                foreach (var s in Sockets)
                    if (s.Client != null && !ignore.Contains(s.Client)) s.Send(data);
            }
        }

        private void Direct(VMNetClient target, VMNetMessage msg)
        {
            var sock = target?.NetHandle as ReduxSocket;
            sock?.Send(ReduxOnlineInfo.PackVM(msg));
        }

        private void Drop(VMNetClient target)
        {
            var sock = target?.NetHandle as ReduxSocket;
            sock?.Close(target.FatalDCMessage ?? "You were disconnected by the host.");
        }

        // ---- called every frame by the game, before the lot ticks ----

        private int SpeedSyncTimer;

        public void Process()
        {
            if (!Running) return;
            // every couple of seconds, copy the host's game speed (pause, 1, 2, 3, or paused for a
            // question) to everyone, so friends who just arrived match it
            if (++SpeedSyncTimer >= 120)
            {
                SpeedSyncTimer = 0;
                if (GuestCount > 0) Driver.SendCommand(new FSO.SimAntics.NetPlay.Model.Commands.VMNetSetSpeedCmd() { Full = true, ActorUID = ReduxOnlineInfo.HostID });
            }
            ReduxSocket fresh;
            while (Pending.TryDequeue(out fresh))
            {
                lock (Sockets) Sockets.Add(fresh);
            }

            List<ReduxSocket> list;
            lock (Sockets) list = new List<ReduxSocket>(Sockets);
            foreach (var sock in list)
            {
                byte[] data;
                while (sock.Incoming.TryDequeue(out data))
                {
                    try { Handle(sock, data); }
                    catch (Exception e)
                    {
                        ReduxOnlineInfo.Log("[host] Bad message from " + sock.PlayerName + ": " + e.Message);
                        sock.Close("Your game sent something the host couldn't read.");
                    }
                }
                if (sock.Client == null && !sock.Closed && (DateTime.UtcNow - sock.Opened).TotalSeconds > 60)
                    sock.Close("Took too long to join.");
                if (sock.Closed)
                {
                    lock (Sockets) Sockets.Remove(sock);
                    if (sock.Client != null)
                    {
                        Driver.DisconnectClient(sock.Client);
                        OnNotice?.Invoke(sock.PlayerName + " left.");
                        ReduxOnlineInfo.Log(sock.PlayerName + " left (" + sock.CloseReason + ")");
                    }
                }
            }
        }

        private void Handle(ReduxSocket sock, byte[] data)
        {
            if (data.Length == 0) return;
            var kind = (ReduxPacket)data[0];
            if (sock.Client == null)
            {
                if (kind == ReduxPacket.Join) HandleJoin(sock, data);
                return;
            }
            switch (kind)
            {
                case ReduxPacket.VM:
                    if (data.Length < 3) return;
                    var msg = ReduxOnlineInfo.UnpackVM(data);
                    if (msg.Type != VMNetMessageType.Command || msg.Data.Length == 0) return;
                    var cmd = (VMCommandType)msg.Data[0];
                    if (!GuestCommands.Contains(cmd)) return; // friends can't buy, build or change the time
                    if (cmd == VMCommandType.RequestResync)
                    {
                        // their game went out of step: send a fresh neighbourhood before the fresh lot
                        sock.Send(BuildHoodPacket());
                    }
                    Driver.HandleMessage(sock.Client, msg);
                    break;
                case ReduxPacket.Bye:
                    sock.Close();
                    break;
            }
        }

        private void HandleJoin(ReduxSocket sock, byte[] data)
        {
            int version;
            string name;
            uint guid;
            byte[] iff;
            short[] personData;
            using (var r = new BinaryReader(new MemoryStream(data, 1, data.Length - 1)))
            {
                version = r.ReadInt32();
                name = r.ReadString();
                guid = r.ReadUInt32();
                var len = r.ReadInt32();
                if (len < 0 || len > 16 * 1024 * 1024) throw new Exception("bad character size");
                iff = r.ReadBytes(len);
                var pd = r.ReadInt32();
                if (pd < 0 || pd > 1024) throw new Exception("bad person data");
                personData = new short[pd];
                for (int i = 0; i < pd; i++) personData[i] = r.ReadInt16();
            }
            if (name.Length > 40) name = name.Substring(0, 40);
            sock.PlayerName = name;

            if (version != ReduxOnlineInfo.Version)
            {
                sock.Close("Your game is a different version from the host's. Both of you need the latest update.");
                return;
            }

            var neigh = Content.Get().Neighborhood;
            var folder = Path.Combine(FSOEnvironment.UserDir, "OnlineVisitors/");
            FSODirectory.CreateDirectory(folder);

            // The host may already have a Sim with this id (the same starting neighbourhood, or
            // testing with two copies of the game on one PC). Then the visitor gets a new id, so
            // they're a separate Sim on the lot that just looks and acts the same.
            if (GuidTaken(guid))
            {
                var newGuid = FreeGuid();
                var tempPath = Path.Combine(folder, "Rename" + guid.ToString("X8") + ".iff");
                FSOFile.WriteAllBytes(tempPath, iff);
                iff = Reguid(tempPath, guid, newGuid);
                try { FSOFile.Delete(tempPath); } catch { }
                if (iff == null)
                {
                    sock.Close("The Sim you picked couldn't be added to the host's game.");
                    return;
                }
                ReduxOnlineInfo.Log("[host] Sim " + guid.ToString("X8") + " is already in the host's game, visiting as " + newGuid.ToString("X8"));
                guid = newGuid;
            }

            // 1. the Sim's character file, for the host's game and everyone else's
            var fileName = "Visitor" + guid.ToString("X8") + ".iff";
            var path = Path.Combine(folder, fileName);
            FSOFile.WriteAllBytes(path, iff);
            var found = neigh.RegisterCharacterFile(path);
            if (!found.Contains(guid))
            {
                sock.Close("The Sim you picked couldn't be read by the host's game.");
                return;
            }
            VisitorFiles.Add(path);

            // 2. the Sim's personality, skills and so on, as a neighbour in the host's neighbourhood
            //    (not part of the host's family, so the host can't control them)
            var nid = neigh.Neighbors.GetFreeID();
            var pdata = (short[])personData.Clone();
            if (pdata.Length > (int)VMPersonDataVariable.TS1FamilyNumber) pdata[(int)VMPersonDataVariable.TS1FamilyNumber] = 0;
            if (pdata.Length > (int)VMPersonDataVariable.NeighborId) pdata[(int)VMPersonDataVariable.NeighborId] = nid;
            var nb = new Neighbour()
            {
                NeighbourID = nid,
                GUID = guid,
                Name = "visitor_" + guid.ToString("x8"),
                PersonData = pdata,
                PersonMode = 9,
                Relationships = new Dictionary<int, List<short>>()
            };
            neigh.Neighbors.AddNeighbor(nb);
            VisitorNeighbours.Add(nb);

            // 3. friends already here get the new Sim before it appears on the lot
            var addPacket = PackAddVisitor(fileName, iff, nb);
            lock (Sockets)
            {
                foreach (var other in Sockets)
                    if (other != sock && other.Client != null) other.Send(addPacket);
            }

            // 4. the new friend gets their id and the neighbourhood (the lot itself comes next)
            var id = NextID++;
            sock.Send(ReduxOnlineInfo.Pack(ReduxPacket.Welcome, w =>
            {
                w.Write(id);
                w.Write(vm.TS1State?.CurrentFamily != null ? "the host's lot" : "the lot");
            }));
            sock.Send(BuildHoodPacket());

            // 5. into the lot: the game sends the lot to them and creates their Sim
            var client = new VMNetClient()
            {
                PersistID = id,
                RemoteIP = sock.RemoteIP,
                AvatarState = new VMNetAvatarPersistState()
                {
                    Name = name,
                    PersistID = id,
                    CustomGUID = guid,
                    DefaultSuits = new VMAvatarDefaultSuits(false),
                    Permissions = FSO.SimAntics.Model.TSOPlatform.VMTSOAvatarPermissions.Visitor,
                },
                NetHandle = sock,
            };
            sock.Client = client;
            Driver.ConnectClient(client);
            OnNotice?.Invoke(name + " joined your lot!");
            ReduxOnlineInfo.Log(name + " joined from " + sock.RemoteIP + " with Sim " + guid.ToString("X8") + " as player " + id);
        }

        private bool GuidTaken(uint guid)
        {
            var neigh = Content.Get().Neighborhood;
            if (neigh.GetNeighborIDForGUID(guid) != null) return true;
            if (vm.Entities.Any(x => x.Object?.OBJ?.GUID == guid)) return true;
            var objs = Content.Get().WorldObjects as FSO.Content.TS1.TS1ObjectProvider;
            return objs != null && objs.Entries.ContainsKey(guid);
        }

        private uint FreeGuid()
        {
            var rand = new Random();
            while (true)
            {
                var g = (uint)rand.Next(0x10000000, int.MaxValue);
                if (!GuidTaken(g)) return g;
            }
        }

        /// <summary>Gives the Sim in a character file a new id, returning the new file.</summary>
        private static byte[] Reguid(string path, uint oldGuid, uint newGuid)
        {
            try
            {
                var file = new FSO.Files.Formats.IFF.IffFile(path, true);
                bool changed = false;
                foreach (var objd in file.List<OBJD>() ?? new List<OBJD>())
                {
                    if (objd.GUID == oldGuid) { objd.GUID = newGuid; changed = true; }
                }
                if (!changed) return null;
                using (var ms = new MemoryStream())
                {
                    file.Write(ms);
                    return ms.ToArray();
                }
            }
            catch (Exception e)
            {
                ReduxOnlineInfo.Log("[host] Couldn't give the Sim a new id: " + e.Message);
                return null;
            }
        }

        private static byte[] PackAddVisitor(string fileName, byte[] iff, Neighbour nb)
        {
            return ReduxOnlineInfo.Pack(ReduxPacket.AddVisitor, w =>
            {
                w.Write(fileName);
                w.Write(iff.Length);
                w.Write(iff);
                w.Write(nb.NeighbourID);
                w.Write(nb.GUID);
                w.Write(nb.Name ?? "");
                w.Write(nb.PersonData.Length);
                foreach (var v in nb.PersonData) w.Write(v);
            });
        }

        /// <summary>The host's neighbourhood as it is right now: the live neighbourhood data plus the files around it.</summary>
        private byte[] BuildHoodPacket()
        {
            var neigh = Content.Get().Neighborhood;
            var files = new List<Tuple<string, byte[]>>();

            byte[] main;
            using (var ms = new MemoryStream())
            {
                neigh.MainResource.Write(ms);
                main = ms.ToArray();
            }
            files.Add(Tuple.Create("Neighborhood.iff", main));

            foreach (var name in new[] { "LotLocations.iff", "LotZoning.iff", "StreetNames.iff",
                "Houses/NeighborhoodDesc.iff", "Houses/STDesc.iff", "Houses/MTDesc.iff" })
            {
                var p = Path.Combine(neigh.UserPath, name);
                if (FSOFile.Exists(p)) files.Add(Tuple.Create(name, FSOFile.ReadAllBytes(p)));
            }
            var chars = Path.Combine(neigh.UserPath, "Characters/");
            if (FSODirectory.Exists(chars))
            {
                foreach (var f in FSODirectory.GetFiles(chars, "*.iff"))
                    files.Add(Tuple.Create("Characters/" + Path.GetFileName(f), FSOFile.ReadAllBytes(f)));
            }
            foreach (var f in VisitorFiles)
                if (FSOFile.Exists(f)) files.Add(Tuple.Create("Characters/" + Path.GetFileName(f), FSOFile.ReadAllBytes(f)));

            return ReduxOnlineInfo.Pack(ReduxPacket.Hood, w =>
            {
                w.Write(files.Count);
                foreach (var f in files)
                {
                    w.Write(f.Item1);
                    w.Write(f.Item2.Length);
                    w.Write(f.Item2);
                }
            });
        }

        /// <summary>
        /// Saves the host's game without the visiting Sims in it (they belong to the friends' own
        /// games), then puts them back for the rest of the session.
        /// </summary>
        public void SaveWithoutVisitors(Action save)
        {
            var neigh = Content.Get().Neighborhood;
            var nbrs = neigh.Neighbors;
            var ids = new HashSet<int>(VisitorNeighbours.Select(x => (int)x.NeighbourID));
            var removedRels = new List<Tuple<Neighbour, int, List<short>>>();
            foreach (var v in VisitorNeighbours) Unlink(nbrs, v);
            foreach (var n in nbrs.Entries)
            {
                if (n.Relationships == null) continue;
                foreach (var key in n.Relationships.Keys.Where(k => ids.Contains(k)).ToList())
                {
                    removedRels.Add(Tuple.Create(n, key, n.Relationships[key]));
                    n.Relationships.Remove(key);
                }
            }
            try { save(); }
            finally
            {
                foreach (var v in VisitorNeighbours) nbrs.AddNeighbor(v);
                foreach (var r in removedRels) r.Item1.Relationships[r.Item2] = r.Item3;
            }
        }

        private static void Unlink(NBRS nbrs, Neighbour nb)
        {
            nbrs.Entries.Remove(nb);
            nbrs.NeighbourByID.Remove(nb.NeighbourID);
            short id;
            if (nbrs.DefaultNeighbourByGUID.TryGetValue(nb.GUID, out id) && id == nb.NeighbourID) nbrs.DefaultNeighbourByGUID.Remove(nb.GUID);
            for (int i = 0; i < nbrs.Entries.Count; i++) nbrs.Entries[i].RuntimeIndex = i;
        }

        private void RemoveVisitorsFromHood()
        {
            var neigh = Content.Get().Neighborhood;
            var ids = new HashSet<int>(VisitorNeighbours.Select(x => (int)x.NeighbourID));
            foreach (var v in VisitorNeighbours) Unlink(neigh.Neighbors, v);
            foreach (var n in neigh.Neighbors.Entries)
            {
                if (n.Relationships == null) continue;
                foreach (var key in n.Relationships.Keys.Where(k => ids.Contains(k)).ToList()) n.Relationships.Remove(key);
            }
            VisitorNeighbours.Clear();
        }
    }

    /// <summary>
    /// A friend's side: connects to the host, swaps in the host's neighbourhood while visiting,
    /// and passes the lot's ticks to the game.
    /// </summary>
    public class ReduxGuest
    {
        public string Address;
        public uint MyID;
        public bool Welcomed;
        public bool HoodLoaded;
        public string Failure;
        public string LotName = "";
        public bool Finished;

        private ReduxSocket Sock;
        private VMClientDriver Driver;
        private VM vm;
        private byte[] JoinPacket;
        private volatile bool _connecting;
        public bool Connecting { get => _connecting; private set => _connecting = value; }

        public static string HoodFolder { get { return Path.Combine(FSOEnvironment.UserDir, "OnlineHood/"); } }

        /// <summary>Builds the "here's me and my Sim" packet. Must be made before the host's neighbourhood replaces ours.</summary>
        public static byte[] MakeJoinPacket(string playerName, uint simGUID, out string error)
        {
            error = null;
            var neigh = Content.Get().Neighborhood;
            var objs = Content.Get().WorldObjects as FSO.Content.TS1.TS1ObjectProvider;
            FSO.Content.GameObjectReference entry = null;
            if (objs == null || !objs.Entries.TryGetValue(simGUID, out entry) || entry.FileName == null || !FSOFile.Exists(entry.FileName))
            {
                error = "Couldn't find that Sim's character file.";
                return null;
            }
            var iff = FSOFile.ReadAllBytes(entry.FileName);
            var nid = neigh.GetNeighborIDForGUID(simGUID);
            var nb = (nid == null) ? null : neigh.GetNeighborByID(nid.Value);
            var personData = nb?.PersonData ?? new short[0];
            return ReduxOnlineInfo.Pack(ReduxPacket.Join, w =>
            {
                w.Write(ReduxOnlineInfo.Version);
                w.Write(playerName ?? "Friend");
                w.Write(simGUID);
                w.Write(iff.Length);
                w.Write(iff);
                w.Write(personData.Length);
                foreach (var v in personData) w.Write(v);
            });
        }

        public ReduxGuest(VM vm, VMClientDriver driver, string address, byte[] joinPacket)
        {
            this.vm = vm;
            Driver = driver;
            Address = address;
            JoinPacket = joinPacket;
            Driver.OnClientCommand += (data) => Sock?.Send(ReduxOnlineInfo.PackVM(new VMNetMessage(VMNetMessageType.Command, data)));
        }

        public void Connect()
        {
            Uri uri;
            try { uri = ReduxOnlineInfo.MakeAddress(Address); }
            catch (Exception e)
            {
                Failure = "That address doesn't look right: " + e.Message;
                return;
            }
            Connecting = true;
            ReduxOnlineInfo.Log("[guest] Connecting to " + uri);
            Task.Run(async () =>
            {
                try
                {
                    var ws = new ClientWebSocket();
                    if (!OperatingSystem.IsBrowser()) ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20); // browsers manage this themselves
                    // up to 90 seconds: a lot on the dedicated server may be starting up for us
                    using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90)))
                        await ws.ConnectAsync(uri, timeout.Token);
                    var sock = new ReduxSocket(ws);
                    sock.Start();
                    sock.Send(JoinPacket);
                    Sock = sock;
                }
                catch (Exception e)
                {
                    Failure = "Couldn't reach the host at " + uri + ".\n\n" + e.Message;
                    ReduxOnlineInfo.Log("[guest] Connect failed: " + e);
                }
                Connecting = false;
            });
        }

        /// <summary>Called every frame before the lot ticks. Returns false once the connection is over (see Failure).</summary>
        public bool Process()
        {
            if (Finished) return false;
            if (Failure != null) { Finished = true; return false; }
            var sock = Sock;
            if (sock == null) return true; // still connecting

            byte[] data;
            while (sock.Incoming.TryDequeue(out data))
            {
                try { Handle(data); }
                catch (Exception e)
                {
                    Failure = "Something from the host couldn't be read: " + e.Message;
                    ReduxOnlineInfo.Log("[guest] Guest error: " + e);
                    sock.Close();
                    Finished = true;
                    return false;
                }
                if (Failure != null) { Finished = true; return false; }
            }
            if (sock.Closed)
            {
                if (Failure == null) Failure = "The connection to the host was lost." + (string.IsNullOrEmpty(sock.CloseReason) ? "" : "\n\n" + sock.CloseReason);
                Finished = true;
                return false;
            }
            return true;
        }

        private void Handle(byte[] data)
        {
            if (data.Length == 0) return;
            switch ((ReduxPacket)data[0])
            {
                case ReduxPacket.VM:
                    if (data.Length >= 2) Driver.ServerMessage(ReduxOnlineInfo.UnpackVM(data));
                    break;
                case ReduxPacket.Welcome:
                    using (var r = new BinaryReader(new MemoryStream(data, 1, data.Length - 1)))
                    {
                        MyID = r.ReadUInt32();
                        LotName = r.ReadString();
                    }
                    vm.MyUID = MyID;
                    Welcomed = true;
                    ReduxOnlineInfo.Log("[guest] Welcomed as player " + MyID);
                    break;
                case ReduxPacket.Hood:
                    LoadHood(data);
                    break;
                case ReduxPacket.AddVisitor:
                    AddVisitor(data);
                    break;
                case ReduxPacket.Bye:
                    using (var r = new BinaryReader(new MemoryStream(data, 1, data.Length - 1)))
                        Failure = r.ReadString();
                    if (string.IsNullOrEmpty(Failure)) Failure = "The host closed the lot.";
                    break;
            }
        }

        private void LoadHood(byte[] data)
        {
            var folder = HoodFolder;
            try { if (FSODirectory.Exists(folder)) FSODirectory.Delete(folder, true); } catch { }
            FSODirectory.CreateDirectory(folder);
            FSODirectory.CreateDirectory(Path.Combine(folder, "Characters/"));
            FSODirectory.CreateDirectory(Path.Combine(folder, "Houses/"));
            using (var r = new BinaryReader(new MemoryStream(data, 1, data.Length - 1)))
            {
                var count = r.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    var name = r.ReadString();
                    var len = r.ReadInt32();
                    var bytes = r.ReadBytes(len);
                    if (!ReduxOnlineInfo.SafeRelativePath(name)) continue;
                    var path = Path.Combine(folder, name);
                    FSODirectory.CreateDirectory(Path.GetDirectoryName(path));
                    FSOFile.WriteAllBytes(path, bytes);
                }
            }
            Content.Get().Neighborhood.LoadFolder(folder);
            HoodLoaded = true;
            ReduxOnlineInfo.Log("[guest] Loaded the host's neighbourhood");
        }

        private void AddVisitor(byte[] data)
        {
            var neigh = Content.Get().Neighborhood;
            using (var r = new BinaryReader(new MemoryStream(data, 1, data.Length - 1)))
            {
                var fileName = r.ReadString();
                var len = r.ReadInt32();
                var iff = r.ReadBytes(len);
                var nid = r.ReadInt16();
                var guid = r.ReadUInt32();
                var name = r.ReadString();
                var pdLen = r.ReadInt32();
                var pd = new short[pdLen];
                for (int i = 0; i < pdLen; i++) pd[i] = r.ReadInt16();
                if (!ReduxOnlineInfo.SafeRelativePath(fileName)) return;

                var path = Path.Combine(HoodFolder, "Characters/", fileName);
                FSODirectory.CreateDirectory(Path.GetDirectoryName(path));
                FSOFile.WriteAllBytes(path, iff);
                neigh.RegisterCharacterFile(path);
                if (!neigh.Neighbors.NeighbourByID.ContainsKey(nid))
                {
                    neigh.Neighbors.AddNeighbor(new Neighbour()
                    {
                        NeighbourID = nid,
                        GUID = guid,
                        Name = name,
                        PersonData = pd,
                        PersonMode = 9,
                        Relationships = new Dictionary<int, List<short>>()
                    });
                }
            }
        }

        public void Leave()
        {
            Finished = true;
            Sock?.Close("left");
        }
    }
}
