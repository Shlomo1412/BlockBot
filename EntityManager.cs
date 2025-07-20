using System.Collections.Concurrent;
using System.Numerics;
using Microsoft.Extensions.Logging;

namespace BlockBot
{
    /// <summary>
    /// Manages entities in the world including players, mobs, and items
    /// </summary>
    public class EntityManager : IDisposable
    {
        private readonly ILogger<EntityManager> _logger;
        private readonly ConcurrentDictionary<int, Entity> _entities;
        private readonly ConcurrentDictionary<string, Player> _players;
        private bool _disposed = false;

        public event Action<Entity>? EntitySpawned;
        public event Action<Entity>? EntityRemoved;
        public event Action<Entity>? EntityMoved;
        public event Action<Player>? PlayerJoined;
        public event Action<Player>? PlayerLeft;

        public Player? Player { get; private set; }

        public EntityManager(ILogger<EntityManager> logger)
        {
            _logger = logger;
            _entities = new ConcurrentDictionary<int, Entity>();
            _players = new ConcurrentDictionary<string, Player>();
        }

        public async Task InitializeAsync()
        {
            _logger.LogInformation("Initializing entity manager");
            await Task.CompletedTask;
        }

        public async Task HandlePacketAsync(Packet packet)
        {
            // Handle entity-related packets
            await Task.CompletedTask;
        }

        public Entity? GetEntity(int entityId)
        {
            return _entities.TryGetValue(entityId, out var entity) ? entity : null;
        }

        public Player? GetPlayer(string username)
        {
            return _players.TryGetValue(username, out var player) ? player : null;
        }

        public void AddEntity(Entity entity)
        {
            _entities.AddOrUpdate(entity.Id, entity, (_, _) => entity);
            
            if (entity is Player player)
            {
                _players.AddOrUpdate(player.Username, player, (_, _) => player);
                PlayerJoined?.Invoke(player);
            }

            EntitySpawned?.Invoke(entity);
            _logger.LogDebug($"Entity {entity.Id} ({entity.Type}) spawned at {entity.Position}");
        }

        public void RemoveEntity(int entityId)
        {
            if (_entities.TryRemove(entityId, out var entity))
            {
                if (entity is Player player)
                {
                    _players.TryRemove(player.Username, out _);
                    PlayerLeft?.Invoke(player);
                }

                EntityRemoved?.Invoke(entity);
                _logger.LogDebug($"Entity {entityId} removed");
            }
        }

        public void UpdateEntityPosition(int entityId, Vector3 position)
        {
            if (_entities.TryGetValue(entityId, out var entity))
            {
                entity.Position = position;
                entity.LastUpdate = DateTime.UtcNow;
                EntityMoved?.Invoke(entity);
            }
        }

        public List<Entity> GetNearbyEntities(Vector3 position, float radius)
        {
            return _entities.Values
                .Where(e => Vector3.Distance(e.Position, position) <= radius)
                .OrderBy(e => Vector3.Distance(e.Position, position))
                .ToList();
        }

        public List<Entity> GetEntitiesOfType(EntityType type)
        {
            return _entities.Values
                .Where(e => e.Type == type)
                .ToList();
        }

        public List<Entity> GetHostileEntities(Vector3 position, float radius)
        {
            return GetNearbyEntities(position, radius)
                .Where(e => e.IsHostile)
                .ToList();
        }

        public List<Entity> GetPassiveEntities(Vector3 position, float radius)
        {
            return GetNearbyEntities(position, radius)
                .Where(e => !e.IsHostile && e.Type != EntityType.Player)
                .ToList();
        }

        public Entity? GetNearestEntity(Vector3 position, EntityType? type = null)
        {
            var entities = type.HasValue 
                ? GetEntitiesOfType(type.Value)
                : _entities.Values.ToList();

            return entities
                .Where(e => e.Position != position)
                .OrderBy(e => Vector3.Distance(e.Position, position))
                .FirstOrDefault();
        }

        public void SetPlayer(Player player)
        {
            Player = player;
            AddEntity(player);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _entities.Clear();
                _players.Clear();
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Base class for all entities
    /// </summary>
    public class Entity
    {
        public int Id { get; set; }
        public EntityType Type { get; set; }
        public Vector3 Position { get; set; }
        public Vector3 Velocity { get; set; }
        public float Yaw { get; set; }
        public float Pitch { get; set; }
        public float Health { get; set; }
        public float MaxHealth { get; set; }
        public bool IsAlive => Health > 0;
        public bool IsHostile { get; set; }
        public DateTime LastUpdate { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();

        public Entity(int id, EntityType type)
        {
            Id = id;
            Type = type;
            Health = 20.0f;
            MaxHealth = 20.0f;
            LastUpdate = DateTime.UtcNow;
        }

        public float DistanceTo(Vector3 position)
        {
            return Vector3.Distance(Position, position);
        }

        public float DistanceTo(Entity other)
        {
            return DistanceTo(other.Position);
        }

        public Vector3 DirectionTo(Vector3 position)
        {
            return Vector3.Normalize(position - Position);
        }

        public Vector3 DirectionTo(Entity other)
        {
            return DirectionTo(other.Position);
        }

        public bool IsInRange(Vector3 position, float range)
        {
            return DistanceTo(position) <= range;
        }

        public bool IsInRange(Entity other, float range)
        {
            return DistanceTo(other) <= range;
        }
    }

    /// <summary>
    /// Represents a player entity
    /// </summary>
    public class Player : Entity
    {
        public string Username { get; set; }
        public string DisplayName { get; set; }
        public GameMode GameMode { get; set; }
        public float Experience { get; set; }
        public int Level { get; set; }
        public float Food { get; set; }
        public float Saturation { get; set; }
        public bool IsSneaking { get; set; }
        public bool IsSprinting { get; set; }
        public bool IsFlying { get; set; }

        public Player(int id, string username) : base(id, EntityType.Player)
        {
            Username = username;
            DisplayName = username;
            GameMode = GameMode.Survival;
            Food = 20.0f;
            Saturation = 5.0f;
        }
    }

    /// <summary>
    /// Represents a mob entity
    /// </summary>
    public class Mob : Entity
    {
        public MobType MobType { get; set; }
        public bool IsAngry { get; set; }
        public Entity? Target { get; set; }
        public float AttackDamage { get; set; }
        public float MovementSpeed { get; set; }

        public Mob(int id, MobType mobType) : base(id, EntityType.Mob)
        {
            MobType = mobType;
            IsHostile = MobData.IsHostile(mobType);
            AttackDamage = MobData.GetAttackDamage(mobType);
            MovementSpeed = MobData.GetMovementSpeed(mobType);
            MaxHealth = MobData.GetMaxHealth(mobType);
            Health = MaxHealth;
        }
    }

    /// <summary>
    /// Represents an item entity (dropped item)
    /// </summary>
    public class ItemEntity : Entity
    {
        public string ItemType { get; set; }
        public int Count { get; set; }
        public DateTime DroppedAt { get; set; }
        public int PickupDelay { get; set; }

        public ItemEntity(int id, string itemType, int count) : base(id, EntityType.Item)
        {
            ItemType = itemType;
            Count = count;
            DroppedAt = DateTime.UtcNow;
            PickupDelay = 40; // 2 seconds in ticks
        }

        public bool CanPickup => DateTime.UtcNow.Subtract(DroppedAt).TotalMilliseconds > PickupDelay * 50;
    }

    /// <summary>
    /// Entity types
    /// </summary>
    public enum EntityType
    {
        Player,
        Mob,
        Item,
        Projectile,
        Vehicle,
        Other
    }

    /// <summary>
    /// Game modes
    /// </summary>
    public enum GameMode
    {
        Survival,
        Creative,
        Adventure,
        Spectator
    }

    /// <summary>
    /// Mob types
    /// </summary>
    public enum MobType
    {
        // Passive
        Pig, Cow, Sheep, Chicken, Horse, Villager,
        
        // Neutral
        Wolf, IronGolem, Bee, Dolphin,
        
        // Hostile
        Zombie, Skeleton, Spider, Creeper, Enderman, Witch
    }

    /// <summary>
    /// Static data about different mob types
    /// </summary>
    public static class MobData
    {
        private static readonly Dictionary<MobType, MobInfo> MobInfos = new()
        {
            // Passive mobs
            { MobType.Pig, new MobInfo(10.0f, 0.0f, 0.25f, false) },
            { MobType.Cow, new MobInfo(10.0f, 0.0f, 0.25f, false) },
            { MobType.Sheep, new MobInfo(8.0f, 0.0f, 0.23f, false) },
            { MobType.Chicken, new MobInfo(4.0f, 0.0f, 0.25f, false) },
            { MobType.Horse, new MobInfo(15.0f, 0.0f, 0.3f, false) },
            { MobType.Villager, new MobInfo(20.0f, 0.0f, 0.5f, false) },
            
            // Neutral mobs
            { MobType.Wolf, new MobInfo(8.0f, 4.0f, 0.3f, false) },
            { MobType.IronGolem, new MobInfo(100.0f, 15.0f, 0.25f, false) },
            { MobType.Bee, new MobInfo(10.0f, 2.0f, 0.3f, false) },
            { MobType.Dolphin, new MobInfo(10.0f, 3.0f, 0.6f, false) },
            
            // Hostile mobs
            { MobType.Zombie, new MobInfo(20.0f, 3.0f, 0.23f, true) },
            { MobType.Skeleton, new MobInfo(20.0f, 2.0f, 0.25f, true) },
            { MobType.Spider, new MobInfo(16.0f, 2.0f, 0.3f, true) },
            { MobType.Creeper, new MobInfo(20.0f, 0.0f, 0.25f, true) },
            { MobType.Enderman, new MobInfo(40.0f, 7.0f, 0.3f, true) },
            { MobType.Witch, new MobInfo(26.0f, 0.0f, 0.25f, true) }
        };

        public static bool IsHostile(MobType mobType)
        {
            return MobInfos.TryGetValue(mobType, out var info) && info.IsHostile;
        }

        public static float GetMaxHealth(MobType mobType)
        {
            return MobInfos.TryGetValue(mobType, out var info) ? info.MaxHealth : 20.0f;
        }

        public static float GetAttackDamage(MobType mobType)
        {
            return MobInfos.TryGetValue(mobType, out var info) ? info.AttackDamage : 0.0f;
        }

        public static float GetMovementSpeed(MobType mobType)
        {
            return MobInfos.TryGetValue(mobType, out var info) ? info.MovementSpeed : 0.25f;
        }

        private record MobInfo(float MaxHealth, float AttackDamage, float MovementSpeed, bool IsHostile);
    }
}