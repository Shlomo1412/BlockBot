using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using System.Numerics;

namespace BlockBot
{
    /// <summary>
    /// Main BlockBot class - A powerful Minecraft bot library similar to Mineflayer with advanced features
    /// </summary>
    public class BlockBot : IDisposable
    {
        private readonly ILogger<BlockBot> _logger;
        private readonly MinecraftClient _client;
        private readonly WorldManager _world;
        private readonly EntityManager _entities;
        private readonly InventoryManager _inventory;
        private readonly NavigationManager _navigation;
        private readonly ChatManager _chat;
        private readonly CombatManager _combat;
        private readonly CraftingManager _crafting;
        private readonly BuildingManager _building;
        private readonly FarmingManager _farming;
        private readonly MiningManager _mining;
        private readonly RedstoneManager _redstone;
        private readonly AdvancedAI _ai;
        
        private bool _disposed = false;

        public BlockBot(ILogger<BlockBot>? logger = null)
        {
            _logger = logger ?? new ConsoleLogger<BlockBot>();
            _client = new MinecraftClient(new ConsoleLogger<MinecraftClient>());
            _world = new WorldManager(new ConsoleLogger<WorldManager>());
            _entities = new EntityManager(new ConsoleLogger<EntityManager>());
            _inventory = new InventoryManager(new ConsoleLogger<InventoryManager>());
            _navigation = new NavigationManager(_world, _entities, new ConsoleLogger<NavigationManager>());
            _chat = new ChatManager(_client, new ConsoleLogger<ChatManager>());
            _combat = new CombatManager(_entities, _client, new ConsoleLogger<CombatManager>());
            _crafting = new CraftingManager(_inventory, _client, new ConsoleLogger<CraftingManager>());
            _building = new BuildingManager(_world, _inventory, _client, new ConsoleLogger<BuildingManager>());
            _farming = new FarmingManager(_world, _inventory, _client, new ConsoleLogger<FarmingManager>());
            _mining = new MiningManager(_world, _inventory, _navigation, _client, new ConsoleLogger<MiningManager>());
            _redstone = new RedstoneManager(_world, new ConsoleLogger<RedstoneManager>());
            _ai = new AdvancedAI(_world, _entities, _navigation, new ConsoleLogger<AdvancedAI>());
            
            SetupEventHandlers();
        }

        // Properties for accessing managers
        public MinecraftClient Client => _client;
        public WorldManager World => _world;
        public EntityManager Entities => _entities;
        public InventoryManager Inventory => _inventory;
        public NavigationManager Navigation => _navigation;
        public ChatManager Chat => _chat;
        public CombatManager Combat => _combat;
        public CraftingManager Crafting => _crafting;
        public BuildingManager Building => _building;
        public FarmingManager Farming => _farming;
        public MiningManager Mining => _mining;
        public RedstoneManager Redstone => _redstone;
        public AdvancedAI AI => _ai;

        // Bot state properties
        public bool IsConnected => _client.IsConnected;
        public Player? Player => _entities.Player;
        public Vector3 Position => Player?.Position ?? Vector3.Zero;
        public string Username => _client.Username;

        /// <summary>
        /// Connect to a Minecraft server
        /// </summary>
        public async Task<bool> ConnectAsync(string host, int port, string username, string? password = null)
        {
            try
            {
                _logger.LogInformation($"Connecting to {host}:{port} as {username}");
                var success = await _client.ConnectAsync(host, port, username, password);
                
                if (success)
                {
                    _logger.LogInformation("Successfully connected to server");
                    await InitializeAsync();
                }
                
                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to connect to server");
                return false;
            }
        }

        /// <summary>
        /// Disconnect from the server
        /// </summary>
        public async Task DisconnectAsync()
        {
            try
            {
                _logger.LogInformation("Disconnecting from server");
                await _client.DisconnectAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during disconnect");
            }
        }

        /// <summary>
        /// Initialize bot systems after connection
        /// </summary>
        private async Task InitializeAsync()
        {
            await _world.InitializeAsync();
            await _entities.InitializeAsync();
            await _inventory.InitializeAsync();
            await _navigation.InitializeAsync();
            
            _logger.LogInformation("Bot initialization complete");
        }

        /// <summary>
        /// Setup event handlers between different managers
        /// </summary>
        private void SetupEventHandlers()
        {
            _client.PacketReceived += OnPacketReceived;
            _client.Disconnected += OnDisconnected;
            _world.ChunkLoaded += _navigation.OnChunkLoaded;
            _entities.EntitySpawned += _combat.OnEntitySpawned;
            _entities.EntityRemoved += _combat.OnEntityRemoved;
            _entities.EntityMoved += OnEntityMoved;
            _inventory.ItemChanged += _crafting.OnItemChanged;
        }

        private async Task OnPacketReceived(Packet packet)
        {
            // Distribute packets to appropriate managers
            await _world.HandlePacketAsync(packet);
            await _entities.HandlePacketAsync(packet);
            await _inventory.HandlePacketAsync(packet);
            await _chat.HandlePacketAsync(packet);
        }

        private void OnEntityMoved(Entity entity)
        {
            // Update navigation system when player moves
            if (entity == _entities.Player)
            {
                _navigation.UpdatePosition(entity.Position);
            }
        }

        private void OnDisconnected()
        {
            _logger.LogWarning("Disconnected from server");
        }

        // Advanced bot actions
        public async Task<bool> GoToAsync(Vector3 destination, float tolerance = 0.5f)
        {
            return await _navigation.GoToAsync(destination, tolerance);
        }

        public async Task<bool> FollowEntityAsync(int entityId, float distance = 3.0f)
        {
            return await _navigation.FollowEntityAsync(entityId, distance);
        }

        public async Task<bool> AttackEntityAsync(int entityId)
        {
            return await _combat.AttackEntityAsync(entityId);
        }

        public async Task<bool> MineBlockAsync(Vector3 position)
        {
            return await _mining.MineBlockAsync(position);
        }

        public async Task<bool> PlaceBlockAsync(Vector3 position, string blockType)
        {
            return await _building.PlaceBlockAsync(position, blockType);
        }

        public async Task<bool> CraftItemAsync(string itemName, int quantity = 1)
        {
            return await _crafting.CraftItemAsync(itemName, quantity);
        }

        public async Task SendChatAsync(string message)
        {
            await _chat.SendMessageAsync(message);
        }

        // Advanced AI features
        public async Task StartAutomatedFarmingAsync()
        {
            await _farming.StartAutomatedFarmingAsync();
        }

        public async Task StartAutomatedMiningAsync(Vector3 area)
        {
            await _mining.StartAutomatedMiningAsync(area);
        }

        public async Task BuildStructureAsync(string schematicPath)
        {
            await _building.BuildFromSchematicAsync(schematicPath);
        }

        public async Task EnableDefensiveModeAsync()
        {
            await _combat.EnableDefensiveModeAsync();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _client?.Dispose();
                _world?.Dispose();
                _entities?.Dispose();
                _inventory?.Dispose();
                _navigation?.Dispose();
                _chat?.Dispose();
                _combat?.Dispose();
                _crafting?.Dispose();
                _building?.Dispose();
                _farming?.Dispose();
                _mining?.Dispose();
                _redstone?.Dispose();
                _ai?.Dispose();
                
                _disposed = true;
            }
        }
    }
}
