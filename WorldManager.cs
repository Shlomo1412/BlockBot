using System.Collections.Concurrent;
using System.Numerics;
using Microsoft.Extensions.Logging;

namespace BlockBot
{
    /// <summary>
    /// Manages world state, chunks, and blocks
    /// </summary>
    public class WorldManager : IDisposable
    {
        private readonly ILogger<WorldManager> _logger;
        private readonly ConcurrentDictionary<ChunkCoordinate, Chunk> _chunks;
        private readonly ConcurrentDictionary<Vector3, Block> _blocks;
        private bool _disposed = false;

        public event Action<Chunk>? ChunkLoaded;
        public event Action<Vector3, Block>? BlockChanged;

        public WorldManager(ILogger<WorldManager> logger)
        {
            _logger = logger;
            _chunks = new ConcurrentDictionary<ChunkCoordinate, Chunk>();
            _blocks = new ConcurrentDictionary<Vector3, Block>();
        }

        public async Task InitializeAsync()
        {
            _logger.LogInformation("Initializing world manager");
            await Task.CompletedTask;
        }

        public async Task HandlePacketAsync(Packet packet)
        {
            switch (packet)
            {
                case BlockChangePacket blockChange:
                    await HandleBlockChangeAsync(blockChange);
                    break;
                // Add more packet handlers
            }
        }

        private async Task HandleBlockChangeAsync(BlockChangePacket packet)
        {
            var position = DecodePosition(packet.Position);
            var block = new Block
            {
                Position = position,
                Type = packet.BlockId,
                LastUpdate = DateTime.UtcNow
            };

            _blocks.AddOrUpdate(position, block, (_, _) => block);
            BlockChanged?.Invoke(position, block);

            await Task.CompletedTask;
        }

        public Block? GetBlock(Vector3 position)
        {
            return _blocks.TryGetValue(position, out var block) ? block : null;
        }

        public void SetBlock(Vector3 position, Block block)
        {
            _blocks.AddOrUpdate(position, block, (_, _) => block);
            BlockChanged?.Invoke(position, block);
        }

        public bool IsBlockSolid(Vector3 position)
        {
            var block = GetBlock(position);
            return block != null && BlockData.IsSolid(block.Type);
        }

        public bool IsPathClear(Vector3 from, Vector3 to)
        {
            var direction = Vector3.Normalize(to - from);
            var distance = Vector3.Distance(from, to);
            var steps = (int)Math.Ceiling(distance * 2); // Check every 0.5 blocks

            for (int i = 0; i <= steps; i++)
            {
                var checkPos = from + direction * (distance * i / steps);
                var blockPos = new Vector3(
                    (float)Math.Floor(checkPos.X),
                    (float)Math.Floor(checkPos.Y),
                    (float)Math.Floor(checkPos.Z)
                );

                if (IsBlockSolid(blockPos))
                    return false;
            }

            return true;
        }

        public List<Vector3> GetSurroundingBlocks(Vector3 center, int radius)
        {
            var blocks = new List<Vector3>();

            for (int x = -radius; x <= radius; x++)
            {
                for (int y = -radius; y <= radius; y++)
                {
                    for (int z = -radius; z <= radius; z++)
                    {
                        if (x == 0 && y == 0 && z == 0) continue;
                        
                        var pos = center + new Vector3(x, y, z);
                        blocks.Add(pos);
                    }
                }
            }

            return blocks;
        }

        public List<Vector3> FindBlocks(Vector3 center, int radius, int blockType)
        {
            var foundBlocks = new List<Vector3>();

            for (int x = -radius; x <= radius; x++)
            {
                for (int y = -radius; y <= radius; y++)
                {
                    for (int z = -radius; z <= radius; z++)
                    {
                        var pos = center + new Vector3(x, y, z);
                        var block = GetBlock(pos);
                        
                        if (block?.Type == blockType)
                        {
                            foundBlocks.Add(pos);
                        }
                    }
                }
            }

            return foundBlocks.OrderBy(pos => Vector3.Distance(center, pos)).ToList();
        }

        public float GetGroundHeight(Vector3 position)
        {
            for (int y = (int)position.Y; y >= 0; y--)
            {
                var checkPos = new Vector3(position.X, y, position.Z);
                if (IsBlockSolid(checkPos))
                {
                    return y + 1; // Return position above the solid block
                }
            }

            return 0; // Bedrock level
        }

        private Vector3 DecodePosition(long encoded)
        {
            var (x, y, z) = PacketUtils.DecodePosition(encoded);
            return new Vector3(x, y, z);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _chunks.Clear();
                _blocks.Clear();
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Represents a chunk coordinate
    /// </summary>
    public struct ChunkCoordinate
    {
        public int X { get; set; }
        public int Z { get; set; }

        public ChunkCoordinate(int x, int z)
        {
            X = x;
            Z = z;
        }

        public override bool Equals(object? obj)
        {
            return obj is ChunkCoordinate coordinate && X == coordinate.X && Z == coordinate.Z;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(X, Z);
        }
    }

    /// <summary>
    /// Represents a chunk of blocks
    /// </summary>
    public class Chunk
    {
        public ChunkCoordinate Coordinate { get; set; }
        public Block[,,] Blocks { get; set; }
        public DateTime LoadedAt { get; set; }

        public Chunk(ChunkCoordinate coordinate)
        {
            Coordinate = coordinate;
            Blocks = new Block[16, 384, 16]; // Standard chunk size
            LoadedAt = DateTime.UtcNow;
        }

        public Block? GetBlock(int x, int y, int z)
        {
            if (x < 0 || x >= 16 || y < 0 || y >= 384 || z < 0 || z >= 16)
                return null;

            return Blocks[x, y, z];
        }

        public void SetBlock(int x, int y, int z, Block block)
        {
            if (x < 0 || x >= 16 || y < 0 || y >= 384 || z < 0 || z >= 16)
                return;

            Blocks[x, y, z] = block;
        }
    }

    /// <summary>
    /// Represents a single block
    /// </summary>
    public class Block
    {
        public Vector3 Position { get; set; }
        public int Type { get; set; }
        public Dictionary<string, object> Properties { get; set; } = new();
        public DateTime LastUpdate { get; set; }

        public bool IsSolid => BlockData.IsSolid(Type);
        public bool IsLiquid => BlockData.IsLiquid(Type);
        public bool IsTransparent => BlockData.IsTransparent(Type);
        public float Hardness => BlockData.GetHardness(Type);
        public string Name => BlockData.GetName(Type);
    }

    /// <summary>
    /// Static data about different block types
    /// </summary>
    public static class BlockData
    {
        private static readonly Dictionary<int, BlockInfo> BlockInfos = new()
        {
            { 0, new BlockInfo("air", false, false, true, 0.0f) },
            { 1, new BlockInfo("stone", true, false, false, 1.5f) },
            { 2, new BlockInfo("grass_block", true, false, false, 0.6f) },
            { 3, new BlockInfo("dirt", true, false, false, 0.5f) },
            { 4, new BlockInfo("cobblestone", true, false, false, 2.0f) },
            { 5, new BlockInfo("oak_planks", true, false, false, 2.0f) },
            { 6, new BlockInfo("oak_sapling", false, false, true, 0.0f) },
            { 7, new BlockInfo("bedrock", true, false, false, -1.0f) },
            { 8, new BlockInfo("water", false, true, false, 100.0f) },
            { 9, new BlockInfo("lava", false, true, false, 100.0f) },
            { 10, new BlockInfo("sand", true, false, false, 0.5f) },
            { 11, new BlockInfo("gravel", true, false, false, 0.6f) },
            { 12, new BlockInfo("gold_ore", true, false, false, 3.0f) },
            { 13, new BlockInfo("iron_ore", true, false, false, 3.0f) },
            { 14, new BlockInfo("coal_ore", true, false, false, 3.0f) },
            { 15, new BlockInfo("oak_log", true, false, false, 2.0f) },
            { 16, new BlockInfo("oak_leaves", true, false, true, 0.2f) },
            { 17, new BlockInfo("sponge", true, false, false, 0.6f) },
            { 18, new BlockInfo("glass", true, false, true, 0.3f) },
            { 19, new BlockInfo("lapis_ore", true, false, false, 3.0f) },
            { 20, new BlockInfo("lapis_block", true, false, false, 3.0f) }
        };

        public static bool IsSolid(int blockType)
        {
            return BlockInfos.TryGetValue(blockType, out var info) && info.IsSolid;
        }

        public static bool IsLiquid(int blockType)
        {
            return BlockInfos.TryGetValue(blockType, out var info) && info.IsLiquid;
        }

        public static bool IsTransparent(int blockType)
        {
            return BlockInfos.TryGetValue(blockType, out var info) && info.IsTransparent;
        }

        public static float GetHardness(int blockType)
        {
            return BlockInfos.TryGetValue(blockType, out var info) ? info.Hardness : 1.0f;
        }

        public static string GetName(int blockType)
        {
            return BlockInfos.TryGetValue(blockType, out var info) ? info.Name : "unknown";
        }

        private record BlockInfo(string Name, bool IsSolid, bool IsLiquid, bool IsTransparent, float Hardness);
    }
}