using System.Text;

namespace BlockBot
{
    /// <summary>
    /// Base class for all Minecraft packets
    /// </summary>
    public abstract class Packet
    {
        public abstract int PacketId { get; }
        public abstract byte[] Serialize();
        public abstract void Deserialize(byte[] data);
    }

    /// <summary>
    /// Handshake packet for server connection
    /// </summary>
    public class HandshakePacket : Packet
    {
        public override int PacketId => 0x00;
        public int ProtocolVersion { get; set; }
        public string ServerAddress { get; set; } = string.Empty;
        public ushort ServerPort { get; set; }
        public int NextState { get; set; }

        public override byte[] Serialize()
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            PacketUtils.WriteVarInt(writer, ProtocolVersion);
            PacketUtils.WriteString(writer, ServerAddress);
            writer.Write(ServerPort);
            PacketUtils.WriteVarInt(writer, NextState);

            var data = ms.ToArray();
            using var finalMs = new MemoryStream();
            using var finalWriter = new BinaryWriter(finalMs);
            
            PacketUtils.WriteVarInt(finalWriter, data.Length + PacketUtils.GetVarIntSize(PacketId));
            PacketUtils.WriteVarInt(finalWriter, PacketId);
            finalWriter.Write(data);

            return finalMs.ToArray();
        }

        public override void Deserialize(byte[] data)
        {
            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);

            ProtocolVersion = PacketUtils.ReadVarInt(reader);
            ServerAddress = PacketUtils.ReadString(reader);
            ServerPort = reader.ReadUInt16();
            NextState = PacketUtils.ReadVarInt(reader);
        }
    }

    /// <summary>
    /// Login start packet
    /// </summary>
    public class LoginStartPacket : Packet
    {
        public override int PacketId => 0x00;
        public string Username { get; set; } = string.Empty;

        public override byte[] Serialize()
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            PacketUtils.WriteString(writer, Username);

            var data = ms.ToArray();
            using var finalMs = new MemoryStream();
            using var finalWriter = new BinaryWriter(finalMs);
            
            PacketUtils.WriteVarInt(finalWriter, data.Length + PacketUtils.GetVarIntSize(PacketId));
            PacketUtils.WriteVarInt(finalWriter, PacketId);
            finalWriter.Write(data);

            return finalMs.ToArray();
        }

        public override void Deserialize(byte[] data)
        {
            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);

            Username = PacketUtils.ReadString(reader);
        }
    }

    /// <summary>
    /// Login success packet
    /// </summary>
    public class LoginSuccessPacket : Packet
    {
        public override int PacketId => 0x02;
        public string UUID { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;

        public override byte[] Serialize()
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            PacketUtils.WriteString(writer, UUID);
            PacketUtils.WriteString(writer, Username);

            var data = ms.ToArray();
            using var finalMs = new MemoryStream();
            using var finalWriter = new BinaryWriter(finalMs);
            
            PacketUtils.WriteVarInt(finalWriter, data.Length + PacketUtils.GetVarIntSize(PacketId));
            PacketUtils.WriteVarInt(finalWriter, PacketId);
            finalWriter.Write(data);

            return finalMs.ToArray();
        }

        public override void Deserialize(byte[] data)
        {
            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);

            UUID = PacketUtils.ReadString(reader);
            Username = PacketUtils.ReadString(reader);
        }
    }

    /// <summary>
    /// Chat message packet
    /// </summary>
    public class ChatPacket : Packet
    {
        public override int PacketId => 0x05;
        public string Message { get; set; } = string.Empty;
        public long Timestamp { get; set; }

        public override byte[] Serialize()
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            PacketUtils.WriteString(writer, Message);
            writer.Write(Timestamp);

            var data = ms.ToArray();
            using var finalMs = new MemoryStream();
            using var finalWriter = new BinaryWriter(finalMs);
            
            PacketUtils.WriteVarInt(finalWriter, data.Length + PacketUtils.GetVarIntSize(PacketId));
            PacketUtils.WriteVarInt(finalWriter, PacketId);
            finalWriter.Write(data);

            return finalMs.ToArray();
        }

        public override void Deserialize(byte[] data)
        {
            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);

            Message = PacketUtils.ReadString(reader);
            if (ms.Position < ms.Length)
            {
                Timestamp = reader.ReadInt64();
            }
        }
    }

    /// <summary>
    /// Player position packet
    /// </summary>
    public class PlayerPositionPacket : Packet
    {
        public override int PacketId => 0x14;
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public bool OnGround { get; set; }

        public override byte[] Serialize()
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            writer.Write(X);
            writer.Write(Y);
            writer.Write(Z);
            writer.Write(OnGround);

            var data = ms.ToArray();
            using var finalMs = new MemoryStream();
            using var finalWriter = new BinaryWriter(finalMs);
            
            PacketUtils.WriteVarInt(finalWriter, data.Length + PacketUtils.GetVarIntSize(PacketId));
            PacketUtils.WriteVarInt(finalWriter, PacketId);
            finalWriter.Write(data);

            return finalMs.ToArray();
        }

        public override void Deserialize(byte[] data)
        {
            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);

            X = reader.ReadDouble();
            Y = reader.ReadDouble();
            Z = reader.ReadDouble();
            OnGround = reader.ReadBoolean();
        }
    }

    /// <summary>
    /// Block change packet
    /// </summary>
    public class BlockChangePacket : Packet
    {
        public override int PacketId => 0x0C;
        public long Position { get; set; }
        public int BlockId { get; set; }

        public override byte[] Serialize()
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            writer.Write(Position);
            PacketUtils.WriteVarInt(writer, BlockId);

            var data = ms.ToArray();
            using var finalMs = new MemoryStream();
            using var finalWriter = new BinaryWriter(finalMs);
            
            PacketUtils.WriteVarInt(finalWriter, data.Length + PacketUtils.GetVarIntSize(PacketId));
            PacketUtils.WriteVarInt(finalWriter, PacketId);
            finalWriter.Write(data);

            return finalMs.ToArray();
        }

        public override void Deserialize(byte[] data)
        {
            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);

            Position = reader.ReadInt64();
            BlockId = PacketUtils.ReadVarInt(reader);
        }
    }

    /// <summary>
    /// Spawn entity packet
    /// </summary>
    public class SpawnEntityPacket : Packet
    {
        public override int PacketId => 0x01;
        public int EntityId { get; set; }
        public string EntityUUID { get; set; } = string.Empty;
        public int EntityType { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public byte Pitch { get; set; }
        public byte Yaw { get; set; }

        public override byte[] Serialize()
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            PacketUtils.WriteVarInt(writer, EntityId);
            PacketUtils.WriteString(writer, EntityUUID);
            PacketUtils.WriteVarInt(writer, EntityType);
            writer.Write(X);
            writer.Write(Y);
            writer.Write(Z);
            writer.Write(Pitch);
            writer.Write(Yaw);

            var data = ms.ToArray();
            using var finalMs = new MemoryStream();
            using var finalWriter = new BinaryWriter(finalMs);
            
            PacketUtils.WriteVarInt(finalWriter, data.Length + PacketUtils.GetVarIntSize(PacketId));
            PacketUtils.WriteVarInt(finalWriter, PacketId);
            finalWriter.Write(data);

            return finalMs.ToArray();
        }

        public override void Deserialize(byte[] data)
        {
            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);

            EntityId = PacketUtils.ReadVarInt(reader);
            EntityUUID = PacketUtils.ReadString(reader);
            EntityType = PacketUtils.ReadVarInt(reader);
            X = reader.ReadDouble();
            Y = reader.ReadDouble();
            Z = reader.ReadDouble();
            Pitch = reader.ReadByte();
            Yaw = reader.ReadByte();
        }
    }

    /// <summary>
    /// Window items packet for inventory updates
    /// </summary>
    public class WindowItemsPacket : Packet
    {
        public override int PacketId => 0x13;
        public byte WindowId { get; set; }
        public short Count { get; set; }
        public List<ItemSlot> Items { get; set; } = new();

        public override byte[] Serialize()
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            writer.Write(WindowId);
            writer.Write(Count);
            
            foreach (var item in Items)
            {
                if (item.Present)
                {
                    writer.Write(true);
                    PacketUtils.WriteVarInt(writer, item.ItemId);
                    writer.Write(item.ItemCount);
                    // NBT data would go here in full implementation
                }
                else
                {
                    writer.Write(false);
                }
            }

            var data = ms.ToArray();
            using var finalMs = new MemoryStream();
            using var finalWriter = new BinaryWriter(finalMs);
            
            PacketUtils.WriteVarInt(finalWriter, data.Length + PacketUtils.GetVarIntSize(PacketId));
            PacketUtils.WriteVarInt(finalWriter, PacketId);
            finalWriter.Write(data);

            return finalMs.ToArray();
        }

        public override void Deserialize(byte[] data)
        {
            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);

            WindowId = reader.ReadByte();
            Count = reader.ReadInt16();
            
            Items.Clear();
            for (int i = 0; i < Count; i++)
            {
                var present = reader.ReadBoolean();
                if (present)
                {
                    var itemId = PacketUtils.ReadVarInt(reader);
                    var itemCount = reader.ReadByte();
                    Items.Add(new ItemSlot { Present = true, ItemId = itemId, ItemCount = itemCount });
                }
                else
                {
                    Items.Add(new ItemSlot { Present = false });
                }
            }
        }
    }

    /// <summary>
    /// Represents an item slot in inventory packets
    /// </summary>
    public class ItemSlot
    {
        public bool Present { get; set; }
        public int ItemId { get; set; }
        public byte ItemCount { get; set; }
    }

    /// <summary>
    /// Factory for creating packet instances
    /// </summary>
    public static class PacketFactory
    {
        private static readonly Dictionary<int, Func<Packet>> PacketTypes = new()
        {
            { 0x00, () => new HandshakePacket() },
            { 0x01, () => new SpawnEntityPacket() },
            { 0x02, () => new LoginSuccessPacket() },
            { 0x05, () => new ChatPacket() },
            { 0x0C, () => new BlockChangePacket() },
            { 0x13, () => new WindowItemsPacket() },
            { 0x14, () => new PlayerPositionPacket() }
        };

        public static Packet? CreatePacket(int packetId, byte[] data)
        {
            if (PacketTypes.TryGetValue(packetId, out var factory))
            {
                var packet = factory();
                packet.Deserialize(data);
                return packet;
            }

            return null;
        }

        public static void RegisterPacket(int packetId, Func<Packet> factory)
        {
            PacketTypes[packetId] = factory;
        }
    }

    /// <summary>
    /// Utility methods for packet serialization
    /// </summary>
    public static class PacketUtils
    {
        public static void WriteVarInt(BinaryWriter writer, int value)
        {
            while ((value & -128) != 0)
            {
                writer.Write((byte)(value & 127 | 128));
                value = (int)((uint)value >> 7);
            }
            writer.Write((byte)value);
        }

        public static int ReadVarInt(BinaryReader reader)
        {
            int value = 0;
            int position = 0;
            byte currentByte;

            do
            {
                currentByte = reader.ReadByte();
                value |= (currentByte & 0x7F) << position;

                if ((currentByte & 0x80) == 0)
                    break;

                position += 7;

                if (position >= 32)
                    throw new InvalidOperationException("VarInt is too big");
            } while (true);

            return value;
        }

        public static void WriteString(BinaryWriter writer, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            WriteVarInt(writer, bytes.Length);
            writer.Write(bytes);
        }

        public static string ReadString(BinaryReader reader)
        {
            var length = ReadVarInt(reader);
            var bytes = reader.ReadBytes(length);
            return Encoding.UTF8.GetString(bytes);
        }

        public static int GetVarIntSize(int value)
        {
            int size = 0;
            do
            {
                size++;
                value = (int)((uint)value >> 7);
            } while (value != 0);
            return size;
        }

        public static long EncodePosition(int x, int y, int z)
        {
            return ((long)(x & 0x3FFFFFF) << 38) | ((long)(z & 0x3FFFFFF) << 12) | (long)(y & 0xFFF);
        }

        public static (int x, int y, int z) DecodePosition(long encoded)
        {
            var x = (int)(encoded >> 38);
            var y = (int)(encoded & 0xFFF);
            var z = (int)((encoded >> 12) & 0x3FFFFFF);

            if (x >= 33554432) x -= 67108864;
            if (z >= 33554432) z -= 67108864;

            return (x, y, z);
        }
    }

    /// <summary>
    /// Extension methods for easier packet handling
    /// </summary>
    public static class PacketExtensions
    {
        public static void WriteVarInt(this BinaryWriter writer, int value)
        {
            PacketUtils.WriteVarInt(writer, value);
        }

        public static void WriteString(this BinaryWriter writer, string value)
        {
            PacketUtils.WriteString(writer, value);
        }

        public static int ReadVarInt(this BinaryReader reader)
        {
            return PacketUtils.ReadVarInt(reader);
        }

        public static string ReadString(this BinaryReader reader)
        {
            return PacketUtils.ReadString(reader);
        }

        public static int GetVarIntSize(this Packet packet, int value)
        {
            return PacketUtils.GetVarIntSize(value);
        }
    }
}