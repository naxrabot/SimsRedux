namespace SimsRedux.Creator;

/// <summary>
/// Minimal OBJD (object definition) accessor. The chunk is a uint32 version followed by uint16 fields
/// (field order from FreeSO's OBJD.cs VERSION_142_Fields). SimsRedux: we only edit a handful of fields
/// in place and keep every other byte untouched, so unknown/expansion fields survive.
/// </summary>
public sealed class Objd
{
    // Field indices (uint16 units after the 4-byte version).
    public const int FieldStackSize = 0, FieldBaseGraphicId = 1, FieldNumGraphics = 2, FieldObjectType = 7,
        FieldMasterId = 8, FieldSubIndex = 9, FieldGuid = 12 /* and 13 */, FieldPrice = 16,
        FieldSalePrice = 32, FieldCatalogStringsId = 39, FieldDynamicSpriteBaseId = 62, FieldNumDynamicSprites = 63;

    private readonly byte[] _data;

    public Objd(byte[] data)
    {
        if (data == null || data.Length < 4 + 2 * 40) throw new InvalidDataException("OBJD chunk is too short.");
        _data = (byte[])data.Clone();
    }

    public uint Version => BitConverter.ToUInt32(_data, 0);

    public ushort GetField(int index) => BitConverter.ToUInt16(_data, 4 + index * 2);
    public void SetField(int index, ushort value) => BitConverter.GetBytes(value).CopyTo(_data, 4 + index * 2);

    public uint Guid
    {
        get => BitConverter.ToUInt32(_data, 4 + FieldGuid * 2);
        set => BitConverter.GetBytes(value).CopyTo(_data, 4 + FieldGuid * 2);
    }
    public ushort Price { get => GetField(FieldPrice); set => SetField(FieldPrice, value); }
    public ushort BaseGraphicId => GetField(FieldBaseGraphicId);
    public ushort NumGraphics => GetField(FieldNumGraphics);
    public ushort CatalogStringsId => GetField(FieldCatalogStringsId);
    public ushort MasterId => GetField(FieldMasterId);
    public short SubIndex => (short)GetField(FieldSubIndex);
    public ushort ObjectType => GetField(FieldObjectType);
    public int FieldCount => (_data.Length - 4) / 2;
    public ushort DynamicSpriteBaseId => FieldCount > FieldNumDynamicSprites ? GetField(FieldDynamicSpriteBaseId) : (ushort)0;
    public ushort NumDynamicSprites => FieldCount > FieldNumDynamicSprites ? GetField(FieldNumDynamicSprites) : (ushort)0;

    /// <summary>True if DGRP <paramref name="id"/> belongs to this object (base graphics or dynamic sprites).</summary>
    public bool OwnsDgrp(ushort id) =>
        (id >= BaseGraphicId && id < BaseGraphicId + Math.Max((int)NumGraphics, 1)) ||
        (NumDynamicSprites > 0 && id >= DynamicSpriteBaseId && id < DynamicSpriteBaseId + NumDynamicSprites);

    public byte[] ToBytes() => (byte[])_data.Clone();
}

/// <summary>One sprite reference inside a DGRP image.</summary>
public sealed class DgrpSprite
{
    public ushort Type;          // only stored by versions &lt; 20003
    public uint SpriteId;
    public uint FrameIndex;
    public uint Flags;           // bit 0 = horizontal flip
    public int OffsetX, OffsetY;
    public float ObjectZ, ObjectX, ObjectY;
    public bool Flip => (Flags & 1) != 0;
}

/// <summary>One of the 12 images (4 directions x 3 zooms) of a DGRP.</summary>
public sealed class DgrpImage
{
    /// <summary>Direction flag: 0x01 RightBack, 0x04 RightFront, 0x10 LeftFront, 0x40 LeftBack.</summary>
    public uint Direction;
    /// <summary>1 = far, 2 = medium, 3 = near.</summary>
    public uint Zoom;
    public List<DgrpSprite> Sprites = new();
    public bool IsFront => Direction == 0x04 || Direction == 0x10;
}

/// <summary>
/// DGRP (draw group) reader/writer. SimsRedux: mirrors FreeSO's DGRP.cs; written back in the same
/// version it was read in so we don't change anything we don't have to.
/// </summary>
public sealed class Dgrp
{
    public ushort Version = 20004;
    public List<DgrpImage> Images = new();

    public static Dgrp Read(byte[] d)
    {
        var g = new Dgrp();
        var r = new BinaryReader(new MemoryStream(d));
        g.Version = r.ReadUInt16();
        uint count = g.Version < 20003 ? r.ReadUInt16() : r.ReadUInt32();
        if (count > 1000) throw new InvalidDataException("DGRP image count is invalid.");
        for (int i = 0; i < count; i++)
        {
            var img = new DgrpImage();
            uint sc;
            if (g.Version < 20003) { sc = r.ReadUInt16(); img.Direction = r.ReadByte(); img.Zoom = r.ReadByte(); }
            else { img.Direction = r.ReadUInt32(); img.Zoom = r.ReadUInt32(); sc = r.ReadUInt32(); }
            if (sc > 1000) throw new InvalidDataException("DGRP sprite count is invalid.");
            for (int j = 0; j < sc; j++)
            {
                var s = new DgrpSprite();
                if (g.Version < 20003)
                {
                    s.Type = r.ReadUInt16(); s.SpriteId = r.ReadUInt16(); s.FrameIndex = r.ReadUInt16(); s.Flags = r.ReadUInt16();
                    s.OffsetX = r.ReadInt16(); s.OffsetY = r.ReadInt16();
                    if (g.Version == 20001) s.ObjectZ = r.ReadSingle();
                }
                else
                {
                    s.SpriteId = r.ReadUInt32(); s.FrameIndex = r.ReadUInt32(); s.OffsetX = r.ReadInt32(); s.OffsetY = r.ReadInt32();
                    s.ObjectZ = r.ReadSingle(); s.Flags = r.ReadUInt32();
                    if (g.Version == 20004) { s.ObjectX = r.ReadSingle(); s.ObjectY = r.ReadSingle(); }
                }
                img.Sprites.Add(s);
            }
            g.Images.Add(img);
        }
        return g;
    }

    public byte[] Write()
    {
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(Version);
        if (Version < 20003) w.Write((ushort)Images.Count); else w.Write((uint)Images.Count);
        foreach (var img in Images)
        {
            if (Version < 20003) { w.Write((ushort)img.Sprites.Count); w.Write((byte)img.Direction); w.Write((byte)img.Zoom); }
            else { w.Write(img.Direction); w.Write(img.Zoom); w.Write((uint)img.Sprites.Count); }
            foreach (var s in img.Sprites)
            {
                if (Version < 20003)
                {
                    w.Write(s.Type); w.Write((ushort)s.SpriteId); w.Write((ushort)s.FrameIndex); w.Write((ushort)s.Flags);
                    w.Write((short)s.OffsetX); w.Write((short)s.OffsetY);
                    if (Version == 20001) w.Write(s.ObjectZ);
                }
                else
                {
                    w.Write(s.SpriteId); w.Write(s.FrameIndex); w.Write(s.OffsetX); w.Write(s.OffsetY); w.Write(s.ObjectZ); w.Write(s.Flags);
                    if (Version == 20004) { w.Write(s.ObjectX); w.Write(s.ObjectY); }
                }
            }
        }
        w.Flush();
        return ms.ToArray();
    }
}
