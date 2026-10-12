using FSO.Common.IO;
using System.Runtime.InteropServices.JavaScript;

namespace Simitone.Web
{
    /// <summary>
    /// SimsRedux web: the game's files in the browser. Every FSOFile / FSODirectory call ends up
    /// here and is answered by wwwroot/js/simsfs.js, which holds the player's Sims 1 folder
    /// (copied into the browser once), SimsRedux's own saved data, and the site's Content folder.
    /// </summary>
    public partial class BrowserFileSystem : IFSOFileSystem
    {
        // files up to this size are read in one go; bigger ones (FAR archives) are read in pieces
        private const int WholeFileLimit = 8 * 1024 * 1024;

        [JSImport("globalThis.SimsFS.fileExists")] internal static partial bool JsFileExists(string path);
        [JSImport("globalThis.SimsFS.dirExists")] internal static partial bool JsDirExists(string path);
        [JSImport("globalThis.SimsFS.fileSize")] internal static partial double JsFileSize(string path);
        [JSImport("globalThis.SimsFS.read")] internal static partial int JsRead(string path, double offset, [JSMarshalAs<JSType.MemoryView>] Span<byte> dest);
        [JSImport("globalThis.SimsFS.write")] internal static partial void JsWrite(string path, [JSMarshalAs<JSType.MemoryView>] Span<byte> data);
        [JSImport("globalThis.SimsFS.deleteFile")] internal static partial void JsDeleteFile(string path);
        [JSImport("globalThis.SimsFS.createDir")] internal static partial void JsCreateDir(string path);
        [JSImport("globalThis.SimsFS.deleteDir")] internal static partial void JsDeleteDir(string path, bool recursive);
        [JSImport("globalThis.SimsFS.moveFile")] internal static partial bool JsMoveFile(string from, string to, bool overwrite);
        [JSImport("globalThis.SimsFS.list")]
        [return: JSMarshalAs<JSType.Array<JSType.String>>]
        internal static partial string[] JsList(string path, string pattern, bool recursive, bool folders);

        public bool FileExists(string path) { return JsFileExists(path); }
        public bool DirectoryExists(string path) { return JsDirExists(path); }

        public Stream Open(string path, FileMode mode, FileAccess access, FileShare share)
        {
            long size = (long)JsFileSize(path);
            bool exists = size >= 0;
            switch (mode)
            {
                case FileMode.Open:
                case FileMode.Truncate:
                    if (!exists) throw new FileNotFoundException("Could not find file '" + path + "'.", path);
                    break;
                case FileMode.CreateNew:
                    if (exists) throw new IOException("The file '" + path + "' already exists.");
                    break;
            }
            bool keep = exists && mode != FileMode.Create && mode != FileMode.CreateNew && mode != FileMode.Truncate;

            // reading a big file: read it in pieces as the game asks for them
            if (access == FileAccess.Read && keep && size > WholeFileLimit)
                return new ChunkedReadStream(path, size);

            var stream = new BrowserFileStream(path, access != FileAccess.Read);
            if (keep && size > 0)
            {
                var data = new byte[size];
                ReadAll(path, data);
                stream.Fill(data);
                stream.Position = (mode == FileMode.Append) ? stream.Length : 0;
            }
            stream.Dirty = !exists || !keep; // a new or emptied file is saved even if nothing is written
            return stream;
        }

        internal static void ReadAll(string path, byte[] data)
        {
            int got = JsRead(path, 0, data);
            if (got == -2) throw new IOException("The file '" + path + "' is still loading.");
            if (got < 0) throw new FileNotFoundException("Could not find file '" + path + "'.", path);
        }

        public void DeleteFile(string path) { JsDeleteFile(path); }
        public void CreateDirectory(string path) { JsCreateDir(path); }

        public void DeleteDirectory(string path, bool recursive)
        {
            if (!JsDirExists(path)) throw new DirectoryNotFoundException("Could not find a part of the path '" + path + "'.");
            if (!recursive && (JsList(path, "*", false, false).Length > 0 || JsList(path, "*", false, true).Length > 0))
                throw new IOException("The directory is not empty: '" + path + "'.");
            JsDeleteDir(path, recursive);
        }

        public string[] GetFiles(string path, string searchPattern, SearchOption option)
        {
            if (!JsDirExists(path)) throw new DirectoryNotFoundException("Could not find a part of the path '" + path + "'.");
            return JsList(path, searchPattern ?? "*", option == SearchOption.AllDirectories, false);
        }

        public string[] GetDirectories(string path, string searchPattern, SearchOption option)
        {
            if (!JsDirExists(path)) throw new DirectoryNotFoundException("Could not find a part of the path '" + path + "'.");
            return JsList(path, searchPattern ?? "*", option == SearchOption.AllDirectories, true);
        }

        public void MoveFile(string source, string dest, bool overwrite)
        {
            if (!JsFileExists(source)) throw new FileNotFoundException("Could not find file '" + source + "'.", source);
            if (!overwrite && JsFileExists(dest)) throw new IOException("The file '" + dest + "' already exists.");
            if (!JsMoveFile(source, dest, overwrite)) throw new IOException("Could not move '" + source + "' to '" + dest + "'.");
        }

        /// <summary>A file held in memory while it's open; saved back to the browser when closed.</summary>
        private class BrowserFileStream : MemoryStream
        {
            private readonly string Path;
            private readonly bool Writable;
            public bool Dirty;
            private bool Closed;

            public BrowserFileStream(string path, bool writable)
            {
                Path = path;
                Writable = writable;
            }

            private bool Filling;
            public override bool CanWrite => !Closed && (Writable || Filling);

            /// <summary>Puts the file's current contents in (even for a read-only stream).</summary>
            public void Fill(byte[] data)
            {
                Filling = true;
                base.Write(data, 0, data.Length);
                Filling = false;
            }
            public override void Write(byte[] buffer, int offset, int count) { base.Write(buffer, offset, count); Dirty = true; }
            public override void Write(ReadOnlySpan<byte> buffer) { base.Write(buffer); Dirty = true; }
            public override void WriteByte(byte value) { base.WriteByte(value); Dirty = true; }
            public override void SetLength(long value) { base.SetLength(value); Dirty = true; }

            public override void Flush()
            {
                base.Flush();
                Save();
            }

            private void Save()
            {
                if (!Writable || !Dirty || Closed) return;
                JsWrite(Path, new Span<byte>(GetBuffer(), 0, (int)Length));
                Dirty = false;
            }

            protected override void Dispose(bool disposing)
            {
                if (!Closed)
                {
                    Save();
                    Closed = true;
                }
                base.Dispose(disposing);
            }
        }

        /// <summary>Reads a big file (like a FAR archive) a piece at a time.</summary>
        private class ChunkedReadStream : Stream
        {
            private const int ChunkSize = 256 * 1024;
            private readonly string Path;
            private readonly long Size;
            private long Pos;
            private readonly byte[] Chunk = new byte[ChunkSize];
            private long ChunkStart = -1;
            private int ChunkLength;

            public ChunkedReadStream(string path, long size)
            {
                Path = path;
                Size = size;
            }

            public override bool CanRead => true;
            public override bool CanSeek => true;
            public override bool CanWrite => false;
            public override long Length => Size;
            public override long Position { get => Pos; set => Pos = value; }

            public override int Read(byte[] buffer, int offset, int count)
            {
                return Read(new Span<byte>(buffer, offset, count));
            }

            public override int Read(Span<byte> buffer)
            {
                if (Pos >= Size || buffer.Length == 0) return 0;
                int total = 0;
                // large reads go straight into the caller's buffer
                if (buffer.Length >= ChunkSize)
                {
                    int got = JsRead(Path, Pos, buffer.Slice(0, (int)Math.Min(buffer.Length, Size - Pos)));
                    if (got < 0) throw new IOException("Could not read '" + Path + "'.");
                    Pos += got;
                    return got;
                }
                while (total < buffer.Length && Pos < Size)
                {
                    if (ChunkStart < 0 || Pos < ChunkStart || Pos >= ChunkStart + ChunkLength)
                    {
                        ChunkStart = Pos;
                        ChunkLength = JsRead(Path, Pos, Chunk);
                        if (ChunkLength < 0) { ChunkStart = -1; throw new IOException("Could not read '" + Path + "'."); }
                        if (ChunkLength == 0) { ChunkStart = -1; break; }
                    }
                    int from = (int)(Pos - ChunkStart);
                    int n = Math.Min(buffer.Length - total, ChunkLength - from);
                    new ReadOnlySpan<byte>(Chunk, from, n).CopyTo(buffer.Slice(total));
                    total += n;
                    Pos += n;
                }
                return total;
            }

            public override int ReadByte()
            {
                Span<byte> one = stackalloc byte[1];
                return Read(one) == 1 ? one[0] : -1;
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                switch (origin)
                {
                    case SeekOrigin.Begin: Pos = offset; break;
                    case SeekOrigin.Current: Pos += offset; break;
                    case SeekOrigin.End: Pos = Size + offset; break;
                }
                return Pos;
            }

            public override void Flush() { }
            public override void SetLength(long value) { throw new NotSupportedException(); }
            public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
        }
    }
}
