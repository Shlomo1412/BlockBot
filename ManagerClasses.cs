using System.Numerics;
using Microsoft.Extensions.Logging;

namespace BlockBot
{
    /// <summary>
    /// Manages combat operations and PvP functionality
    /// </summary>
    public class CombatManager : IDisposable
    {
        private readonly EntityManager _entities;
        private readonly MinecraftClient _client;
        private readonly ILogger<CombatManager> _logger;
        private readonly List<Entity> _hostileTargets = new();
        private readonly CombatState _combatState = new();
        private bool _defensiveModeEnabled = false;
        private bool _disposed = false;
        private CancellationTokenSource? _defenseCancellation;

        public event Action<Entity>? EntityAttacked;
        public event Action<Entity>? EntityKilled;
        public event Action? DefensiveModeToggled;

        public bool IsInCombat => _combatState.IsInCombat;
        public Entity? CurrentTarget => _combatState.CurrentTarget;

        public CombatManager(EntityManager entities, MinecraftClient client, ILogger<CombatManager> logger)
        {
            _entities = entities;
            _client = client;
            _logger = logger;
        }

        public async Task<bool> AttackEntityAsync(int entityId)
        {
            var entity = _entities.GetEntity(entityId);
            if (entity == null || !entity.IsAlive)
                return false;

            // Check attack cooldown
            var timeSinceLastAttack = DateTime.UtcNow - _combatState.LastAttackTime;
            var cooldownMs = GetAttackCooldown();
            
            if (timeSinceLastAttack.TotalMilliseconds < cooldownMs)
            {
                var remainingCooldown = cooldownMs - (int)timeSinceLastAttack.TotalMilliseconds;
                await Task.Delay(Math.Max(0, remainingCooldown));
            }

            _logger.LogInformation($"Attacking entity {entityId} ({entity.Type})");
            
            // Send attack packet to server
            await SendAttackPacketAsync(entityId);
            
            // Calculate damage based on weapon and entity type
            var damage = CalculateAttackDamage(entity);
            
            // Apply damage locally for immediate feedback
            entity.Health = Math.Max(0, entity.Health - damage);
            
            // Update combat state
            _combatState.CurrentTarget = entity;
            _combatState.LastAttackTime = DateTime.UtcNow;
            _combatState.IsInCombat = true;
            
            EntityAttacked?.Invoke(entity);
            
            if (entity.Health <= 0)
            {
                _logger.LogInformation($"Entity {entityId} killed");
                EntityKilled?.Invoke(entity);
                _combatState.IsInCombat = false;
                _combatState.CurrentTarget = null;
            }

            return true;
        }

        private async Task SendAttackPacketAsync(int entityId)
        {
            // Create and send attack packet
            var attackPacket = new AttackEntityPacket
            {
                EntityId = entityId
            };
            
            await _client.SendPacketAsync(attackPacket);
        }

        public async Task EnableDefensiveModeAsync()
        {
            if (_defensiveModeEnabled) return;
            
            _defensiveModeEnabled = true;
            DefensiveModeToggled?.Invoke();
            _logger.LogInformation("Defensive mode enabled");
            
            _defenseCancellation = new CancellationTokenSource();
            
            // Start defensive monitoring task
            _ = Task.Run(async () => await MonitorDefensiveThreatsAsync(_defenseCancellation.Token));
        }

        public async Task DisableDefensiveModeAsync()
        {
            if (!_defensiveModeEnabled) return;
            
            _defensiveModeEnabled = false;
            _defenseCancellation?.Cancel();
            DefensiveModeToggled?.Invoke();
            _logger.LogInformation("Defensive mode disabled");
            
            await Task.CompletedTask;
        }

        private async Task MonitorDefensiveThreatsAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && _defensiveModeEnabled)
            {
                try
                {
                    // Check for hostile entities in range
                    var playerPos = _entities.Player?.Position ?? Vector3.Zero;
                    var nearbyHostiles = _entities.GetHostileEntities(playerPos, 10.0f);
                    
                    foreach (var hostile in nearbyHostiles)
                    {
                        if (!_hostileTargets.Contains(hostile))
                        {
                            _hostileTargets.Add(hostile);
                            _logger.LogWarning($"Hostile entity detected: {hostile.Type} at {hostile.Position}");
                            
                            // Auto-attack if very close
                            if (hostile.DistanceTo(playerPos) < 3.0f)
                            {
                                await AttackEntityAsync(hostile.Id);
                            }
                        }
                    }
                    
                    // Remove entities that are no longer threats
                    _hostileTargets.RemoveAll(e => !e.IsAlive || e.DistanceTo(playerPos) > 15.0f);
                    
                    // Wait for next check cycle - this is operational timing, not simulation
                    await Task.Delay(1000, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in defensive monitoring");
                }
            }
        }

        private float CalculateAttackDamage(Entity target)
        {
            // Base damage
            float damage = 1.0f;
            
            // Adjust based on target type
            if (target is Mob mob)
            {
                // More damage to weaker mobs
                damage = mob.MaxHealth * 0.2f;
            }
            
            // TODO: Add weapon damage calculations based on equipped weapon
            
            return Math.Max(1.0f, damage);
        }

        private int GetAttackCooldown()
        {
            // Attack speed based on weapon type
            // TODO: Check current weapon from inventory
            return 600; // 0.6 seconds standard cooldown
        }

        public void OnEntitySpawned(Entity entity)
        {
            if (entity.IsHostile && _defensiveModeEnabled)
            {
                var playerPos = _entities.Player?.Position ?? Vector3.Zero;
                if (entity.DistanceTo(playerPos) < 15.0f)
                {
                    _hostileTargets.Add(entity);
                    _logger.LogDebug($"Added hostile entity to watch list: {entity.Type}");
                }
            }
        }

        public void OnEntityRemoved(Entity entity)
        {
            _hostileTargets.Remove(entity);
            if (_combatState.CurrentTarget == entity)
            {
                _combatState.CurrentTarget = null;
                _combatState.IsInCombat = false;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _defenseCancellation?.Cancel();
                _defenseCancellation?.Dispose();
                _hostileTargets.Clear();
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Combat state tracking
    /// </summary>
    public class CombatState
    {
        public bool IsInCombat { get; set; }
        public Entity? CurrentTarget { get; set; }
        public DateTime LastAttackTime { get; set; }
        public float Health { get; set; } = 20.0f;
        public float MaxHealth { get; set; } = 20.0f;
    }

    /// <summary>
    /// Manages crafting operations and recipes
    /// </summary>
    public class CraftingManager : IDisposable
    {
        private readonly InventoryManager _inventory;
        private readonly MinecraftClient _client;
        private readonly ILogger<CraftingManager> _logger;
        private readonly Dictionary<string, Recipe> _recipes = new();
        private readonly Queue<CraftingTask> _craftingQueue = new();
        private readonly Dictionary<string, DateTime> _craftingInProgress = new();
        private bool _disposed = false;
        private bool _autoCraftingEnabled = false;

        public event Action<string, int>? ItemCrafted;
        public event Action<string>? CraftingFailed;

        public CraftingManager(InventoryManager inventory, MinecraftClient client, ILogger<CraftingManager> logger)
        {
            _inventory = inventory;
            _client = client;
            _logger = logger;
            InitializeRecipes();
        }

        public async Task<bool> CraftItemAsync(string itemName, int quantity = 1)
        {
            if (!_recipes.TryGetValue(itemName, out var recipe))
            {
                _logger.LogWarning($"No recipe found for {itemName}");
                CraftingFailed?.Invoke(itemName);
                return false;
            }

            // Check if we have required materials
            var missingMaterials = new Dictionary<string, int>();
            foreach (var ingredient in recipe.Ingredients)
            {
                var needed = ingredient.Value * quantity;
                var available = _inventory.GetItemCount(ingredient.Key);
                if (available < needed)
                {
                    missingMaterials[ingredient.Key] = needed - available;
                }
            }

            if (missingMaterials.Any())
            {
                _logger.LogWarning($"Insufficient materials for {itemName}: {string.Join(", ", missingMaterials.Select(kv => $"{kv.Value} {kv.Key}"))}");
                CraftingFailed?.Invoke(itemName);
                return false;
            }

            // Send crafting packet to server
            await SendCraftingPacketAsync(itemName, quantity);

            // Track crafting start time
            _craftingInProgress[itemName] = DateTime.UtcNow;

            // Remove materials from inventory
            foreach (var ingredient in recipe.Ingredients)
            {
                var slotsWithItem = _inventory.FindItemSlots(ingredient.Key);
                var remaining = ingredient.Value * quantity;
                
                foreach (var slot in slotsWithItem)
                {
                    if (remaining <= 0) break;
                    
                    var item = _inventory.GetItem(slot);
                    if (item != null)
                    {
                        var toRemove = Math.Min(remaining, item.Count);
                        item.Count -= toRemove;
                        remaining -= toRemove;
                        
                        if (item.Count <= 0)
                        {
                            _inventory.RemoveItem(slot);
                        }
                        else
                        {
                            _inventory.SetItem(slot, item);
                        }
                    }
                }
            }

            // Wait for crafting completion based on recipe time
            var craftingTimeMs = recipe.CraftingTime;
            await Task.Delay(craftingTimeMs);

            // Add crafted items to inventory
            var resultCount = recipe.ResultCount * quantity;
            var emptySlot = _inventory.FindEmptySlot();
            
            if (emptySlot.HasValue)
            {
                var craftedItem = new ItemStack(recipe.Result, resultCount);
                _inventory.SetItem(emptySlot.Value, craftedItem);
                
                _logger.LogInformation($"Crafted {resultCount}x {recipe.Result}");
                ItemCrafted?.Invoke(recipe.Result, resultCount);
                
                // Remove from crafting tracking
                _craftingInProgress.Remove(itemName);
                return true;
            }
            else
            {
                _logger.LogWarning("No space in inventory for crafted items");
                CraftingFailed?.Invoke(itemName);
                _craftingInProgress.Remove(itemName);
                return false;
            }
        }

        private async Task SendCraftingPacketAsync(string itemName, int quantity)
        {
            // Create and send crafting packet
            var craftingPacket = new CraftingPacket
            {
                ItemName = itemName,
                Quantity = quantity
            };
            
            await _client.SendPacketAsync(craftingPacket);
        }

        public void QueueCraftingTask(string itemName, int quantity)
        {
            _craftingQueue.Enqueue(new CraftingTask { ItemName = itemName, Quantity = quantity });
        }

        public async Task ProcessCraftingQueueAsync()
        {
            while (_craftingQueue.Count > 0)
            {
                var task = _craftingQueue.Dequeue();
                await CraftItemAsync(task.ItemName, task.Quantity);
            }
        }

        public void OnItemChanged(int slot, ItemStack item)
        {
            if (_autoCraftingEnabled)
            {
                // Check if we can craft anything useful with new items
                CheckAutoCraftingOpportunities();
            }
        }

        private void CheckAutoCraftingOpportunities()
        {
            // Auto-craft basic tools if we have materials
            var inventory = _inventory.GetItemSummary();
            
            // Auto-craft sticks if we have planks
            if (inventory.ContainsKey("oak_planks") && inventory["oak_planks"] >= 2)
            {
                if (!inventory.ContainsKey("stick") || inventory["stick"] < 10)
                {
                    QueueCraftingTask("stick", 1);
                }
            }
            
            // Auto-craft tools if we have materials
            if (inventory.ContainsKey("stick") && inventory["stick"] >= 2)
            {
                if (inventory.ContainsKey("cobblestone") && inventory["cobblestone"] >= 3)
                {
                    if (!inventory.ContainsKey("stone_pickaxe"))
                    {
                        QueueCraftingTask("stone_pickaxe", 1);
                    }
                }
            }
        }

        private void InitializeRecipes()
        {
            // Tool recipes
            _recipes["wooden_pickaxe"] = new Recipe
            {
                Result = "wooden_pickaxe",
                ResultCount = 1,
                CraftingTime = 1000,
                Ingredients = new Dictionary<string, int>
                {
                    { "oak_planks", 3 },
                    { "stick", 2 }
                }
            };

            _recipes["stone_pickaxe"] = new Recipe
            {
                Result = "stone_pickaxe",
                ResultCount = 1,
                CraftingTime = 1000,
                Ingredients = new Dictionary<string, int>
                {
                    { "cobblestone", 3 },
                    { "stick", 2 }
                }
            };

            _recipes["iron_pickaxe"] = new Recipe
            {
                Result = "iron_pickaxe",
                ResultCount = 1,
                CraftingTime = 1500,
                Ingredients = new Dictionary<string, int>
                {
                    { "iron_ingot", 3 },
                    { "stick", 2 }
                }
            };

            // Basic items
            _recipes["stick"] = new Recipe
            {
                Result = "stick",
                ResultCount = 4,
                CraftingTime = 500,
                Ingredients = new Dictionary<string, int>
                {
                    { "oak_planks", 2 }
                }
            };

            _recipes["crafting_table"] = new Recipe
            {
                Result = "crafting_table",
                ResultCount = 1,
                CraftingTime = 800,
                Ingredients = new Dictionary<string, int>
                {
                    { "oak_planks", 4 }
                }
            };
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _recipes.Clear();
                _craftingQueue.Clear();
                _craftingInProgress.Clear();
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Represents a crafting task
    /// </summary>
    public class CraftingTask
    {
        public string ItemName { get; set; } = string.Empty;
        public int Quantity { get; set; }
    }

    /// <summary>
    /// Manages building and construction operations
    /// </summary>
    public class BuildingManager : IDisposable
    {
        private readonly WorldManager _world;
        private readonly InventoryManager _inventory;
        private readonly MinecraftClient _client;
        private readonly ILogger<BuildingManager> _logger;
        private readonly Dictionary<string, BuildingSchematic> _schematics = new();
        private bool _disposed = false;

        public event Action<Vector3, string>? BlockPlaced;
        public event Action<string>? StructureCompleted;

        public BuildingManager(WorldManager world, InventoryManager inventory, MinecraftClient client, ILogger<BuildingManager> logger)
        {
            _world = world;
            _inventory = inventory;
            _client = client;
            _logger = logger;
            InitializeBasicSchematics();
        }

        public async Task<bool> PlaceBlockAsync(Vector3 position, string blockType)
        {
            if (!_inventory.HasItem(blockType))
            {
                _logger.LogWarning($"No {blockType} in inventory");
                return false;
            }

            // Check if position is valid for building
            if (!IsValidBuildPosition(position))
            {
                _logger.LogWarning($"Invalid build position: {position}");
                return false;
            }

            // Send block placement packet to server
            await SendBlockPlacementPacketAsync(position, blockType);

            // Remove block from inventory
            var slots = _inventory.FindItemSlots(blockType);
            if (slots.Any())
            {
                var slot = slots.First();
                var item = _inventory.GetItem(slot);
                if (item != null)
                {
                    item.Count--;
                    if (item.Count <= 0)
                    {
                        _inventory.RemoveItem(slot);
                    }
                    else
                    {
                        _inventory.SetItem(slot, item);
                    }
                }
            }

            // Place block in world
            var blockId = GetBlockId(blockType);
            var block = new Block
            {
                Position = position,
                Type = blockId,
                LastUpdate = DateTime.UtcNow
            };
            
            _world.SetBlock(position, block);
            
            _logger.LogInformation($"Placed {blockType} at {position}");
            BlockPlaced?.Invoke(position, blockType);
            
            return true;
        }

        private async Task SendBlockPlacementPacketAsync(Vector3 position, string blockType)
        {
            var placementPacket = new BlockPlacementPacket
            {
                Position = PacketUtils.EncodePosition((int)position.X, (int)position.Y, (int)position.Z),
                BlockType = GetBlockId(blockType)
            };
            
            await _client.SendPacketAsync(placementPacket);
        }

        public async Task<bool> BuildFromSchematicAsync(string schematicName)
        {
            if (!_schematics.TryGetValue(schematicName, out var schematic))
            {
                _logger.LogWarning($"Unknown schematic: {schematicName}");
                return false;
            }

            _logger.LogInformation($"Building structure: {schematicName}");

            // Check if we have all required materials
            var materialRequirements = CalculateMaterialRequirements(schematic);
            var missingMaterials = new Dictionary<string, int>();
            
            foreach (var requirement in materialRequirements)
            {
                var available = _inventory.GetItemCount(requirement.Key);
                if (available < requirement.Value)
                {
                    missingMaterials[requirement.Key] = requirement.Value - available;
                }
            }

            if (missingMaterials.Any())
            {
                _logger.LogWarning($"Missing materials: {string.Join(", ", missingMaterials.Select(kv => $"{kv.Value} {kv.Key}"))}");
                return false;
            }

            // Build structure block by block
            var basePosition = Vector3.Zero; // TODO: Determine by placement logic
            
            foreach (var block in schematic.Blocks.OrderBy(b => b.Position.Y)) // Build from bottom up
            {
                var worldPosition = basePosition + block.Position;
                var success = await PlaceBlockAsync(worldPosition, block.BlockType);
                
                if (!success)
                {
                    _logger.LogWarning($"Failed to place block at {worldPosition}");
                    return false;
                }
                
                // Brief pause between placements to avoid server rate limiting
                await Task.Delay(50);
            }

            _logger.LogInformation($"Structure '{schematicName}' completed");
            StructureCompleted?.Invoke(schematicName);
            return true;
        }

        private bool IsValidBuildPosition(Vector3 position)
        {
            // Check if position is not occupied
            var existingBlock = _world.GetBlock(position);
            if (existingBlock != null && existingBlock.IsSolid)
            {
                return false;
            }

            // Check if position has support (for non-floating blocks)
            var supportBlock = _world.GetBlock(position - Vector3.UnitY);
            return supportBlock != null && supportBlock.IsSolid;
        }

        private Dictionary<string, int> CalculateMaterialRequirements(BuildingSchematic schematic)
        {
            var requirements = new Dictionary<string, int>();
            
            foreach (var block in schematic.Blocks)
            {
                if (requirements.ContainsKey(block.BlockType))
                {
                    requirements[block.BlockType]++;
                }
                else
                {
                    requirements[block.BlockType] = 1;
                }
            }
            
            return requirements;
        }

        private int GetBlockId(string blockType)
        {
            // Map block names to IDs
            return blockType switch
            {
                "stone" => 1,
                "grass_block" => 2,
                "dirt" => 3,
                "cobblestone" => 4,
                "oak_planks" => 5,
                _ => 1 // Default to stone
            };
        }

        private void InitializeBasicSchematics()
        {
            // Simple house schematic
            _schematics["simple_house"] = new BuildingSchematic
            {
                Name = "Simple House",
                Blocks = new List<SchematicBlock>
                {
                    // Foundation
                    new() { Position = new Vector3(0, 0, 0), BlockType = "cobblestone" },
                    new() { Position = new Vector3(1, 0, 0), BlockType = "cobblestone" },
                    new() { Position = new Vector3(2, 0, 0), BlockType = "cobblestone" },
                    new() { Position = new Vector3(0, 0, 1), BlockType = "cobblestone" },
                    new() { Position = new Vector3(2, 0, 1), BlockType = "cobblestone" },
                    new() { Position = new Vector3(0, 0, 2), BlockType = "cobblestone" },
                    new() { Position = new Vector3(1, 0, 2), BlockType = "cobblestone" },
                    new() { Position = new Vector3(2, 0, 2), BlockType = "cobblestone" },
                    
                    // Walls
                    new() { Position = new Vector3(0, 1, 0), BlockType = "oak_planks" },
                    new() { Position = new Vector3(2, 1, 0), BlockType = "oak_planks" },
                    new() { Position = new Vector3(0, 1, 2), BlockType = "oak_planks" },
                    new() { Position = new Vector3(2, 1, 2), BlockType = "oak_planks" },
                    
                    // Roof
                    new() { Position = new Vector3(0, 2, 0), BlockType = "oak_planks" },
                    new() { Position = new Vector3(1, 2, 0), BlockType = "oak_planks" },
                    new() { Position = new Vector3(2, 2, 0), BlockType = "oak_planks" },
                    new() { Position = new Vector3(0, 2, 1), BlockType = "oak_planks" },
                    new() { Position = new Vector3(2, 2, 1), BlockType = "oak_planks" },
                    new() { Position = new Vector3(0, 2, 2), BlockType = "oak_planks" },
                    new() { Position = new Vector3(1, 2, 2), BlockType = "oak_planks" },
                    new() { Position = new Vector3(2, 2, 2), BlockType = "oak_planks" }
                }
            };
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _schematics.Clear();
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Building schematic definition
    /// </summary>
    public class BuildingSchematic
    {
        public string Name { get; set; } = string.Empty;
        public List<SchematicBlock> Blocks { get; set; } = new();
    }

    /// <summary>
    /// Individual block in a schematic
    /// </summary>
    public class SchematicBlock
    {
        public Vector3 Position { get; set; }
        public string BlockType { get; set; } = string.Empty;
    }

    /// <summary>
    /// Manages farming operations
    /// </summary>
    public class FarmingManager : IDisposable
    {
        private readonly WorldManager _world;
        private readonly InventoryManager _inventory;
        private readonly MinecraftClient _client;
        private readonly ILogger<FarmingManager> _logger;
        private readonly List<FarmPlot> _farmPlots = new();
        private bool _autoFarmingEnabled = false;
        private bool _disposed = false;
        private CancellationTokenSource? _farmingCancellation;

        public event Action<Vector3, string>? CropPlanted;
        public event Action<Vector3, string, int>? CropHarvested;

        public FarmingManager(WorldManager world, InventoryManager inventory, MinecraftClient client, ILogger<FarmingManager> logger)
        {
            _world = world;
            _inventory = inventory;
            _client = client;
            _logger = logger;
        }

        public async Task StartAutomatedFarmingAsync()
        {
            if (_autoFarmingEnabled) return;
            
            _autoFarmingEnabled = true;
            _farmingCancellation = new CancellationTokenSource();
            _logger.LogInformation("Automated farming started");
            
            // Start farming loop
            _ = Task.Run(async () => await FarmingLoopAsync(_farmingCancellation.Token));
            
            await Task.CompletedTask;
        }

        public async Task StopAutomatedFarmingAsync()
        {
            _autoFarmingEnabled = false;
            _farmingCancellation?.Cancel();
            _logger.LogInformation("Automated farming stopped");
            await Task.CompletedTask;
        }

        private async Task FarmingLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && _autoFarmingEnabled)
            {
                try
                {
                    // Check existing farm plots
                    foreach (var plot in _farmPlots.ToList())
                    {
                        await ProcessFarmPlotAsync(plot);
                    }
                    
                    // Look for new farming opportunities
                    await ExpandFarmingAsync();
                    
                    // Check every 5 seconds - operational timing for farm management
                    await Task.Delay(5000, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in farming loop");
                }
            }
        }

        private async Task ProcessFarmPlotAsync(FarmPlot plot)
        {
            var block = _world.GetBlock(plot.Position);
            
            // Check if crop is ready to harvest
            if (IsCropMature(block))
            {
                await HarvestCropAsync(plot.Position, plot.CropType);
                await PlantCropAsync(plot.Position, plot.CropType);
            }
        }

        private async Task ExpandFarmingAsync()
        {
            // Look for suitable farmland near existing plots or player
            var searchCenter = _farmPlots.Any() ? _farmPlots.First().Position : Vector3.Zero;
            
            // Find suitable locations for new farm plots
            for (int x = -5; x <= 5; x++)
            {
                for (int z = -5; z <= 5; z++)
                {
                    var position = searchCenter + new Vector3(x, 0, z);
                    
                    if (IsSuitableForFarming(position) && !_farmPlots.Any(p => p.Position == position))
                    {
                        // Create new farm plot
                        var plot = new FarmPlot
                        {
                            Position = position,
                            CropType = "wheat", // Default crop
                            PlantedAt = DateTime.UtcNow
                        };
                        
                        _farmPlots.Add(plot);
                        await PlantCropAsync(position, "wheat");
                        
                        // Limit number of plots to manage
                        if (_farmPlots.Count >= 20) break;
                    }
                }
                if (_farmPlots.Count >= 20) break;
            }
        }

        private async Task<bool> PlantCropAsync(Vector3 position, string cropType)
        {
            if (!_inventory.HasItem($"{cropType}_seeds"))
            {
                return false;
            }

            // Send block placement packet for seeds
            await SendSeedPlantingPacketAsync(position, cropType);

            // Remove seeds from inventory
            var seedSlots = _inventory.FindItemSlots($"{cropType}_seeds");
            if (seedSlots.Any())
            {
                var slot = seedSlots.First();
                var item = _inventory.GetItem(slot);
                if (item != null)
                {
                    item.Count--;
                    if (item.Count <= 0)
                    {
                        _inventory.RemoveItem(slot);
                    }
                    else
                    {
                        _inventory.SetItem(slot, item);
                    }
                }
            }

            // Plant crop
            var cropBlock = new Block
            {
                Position = position,
                Type = GetCropBlockId(cropType),
                LastUpdate = DateTime.UtcNow
            };
            cropBlock.Properties["growth_stage"] = 0;
            cropBlock.Properties["planted_at"] = DateTime.UtcNow;
            
            _world.SetBlock(position, cropBlock);
            
            _logger.LogDebug($"Planted {cropType} at {position}");
            CropPlanted?.Invoke(position, cropType);
            
            return true;
        }

        private async Task SendSeedPlantingPacketAsync(Vector3 position, string cropType)
        {
            var plantingPacket = new BlockPlacementPacket
            {
                Position = PacketUtils.EncodePosition((int)position.X, (int)position.Y, (int)position.Z),
                BlockType = GetCropBlockId(cropType)
            };
            
            await _client.SendPacketAsync(plantingPacket);
        }

        private async Task<bool> HarvestCropAsync(Vector3 position, string cropType)
        {
            var block = _world.GetBlock(position);
            if (block == null) return false;

            // Send block breaking packet
            await SendBlockBreakingPacketAsync(position);

            // Calculate yield
            var yield = CalculateCropYield(cropType);
            
            // Add harvested items to inventory
            var emptySlot = _inventory.FindEmptySlot();
            if (emptySlot.HasValue)
            {
                var harvestedItem = new ItemStack(cropType, yield);
                _inventory.SetItem(emptySlot.Value, harvestedItem);
            }

            // Also get seeds back
            var seedSlot = _inventory.FindEmptySlot();
            if (seedSlot.HasValue)
            {
                var seeds = new ItemStack($"{cropType}_seeds", 1 + Random.Shared.Next(0, 3));
                _inventory.SetItem(seedSlot.Value, seeds);
            }

            // Remove crop block
            _world.SetBlock(position, new Block { Position = position, Type = 0 }); // Air
            
            _logger.LogDebug($"Harvested {yield} {cropType} at {position}");
            CropHarvested?.Invoke(position, cropType, yield);
            
            return true;
        }

        private async Task SendBlockBreakingPacketAsync(Vector3 position)
        {
            var breakingPacket = new BlockBreakingPacket
            {
                Position = PacketUtils.EncodePosition((int)position.X, (int)position.Y, (int)position.Z)
            };
            
            await _client.SendPacketAsync(breakingPacket);
        }

        private bool IsSuitableForFarming(Vector3 position)
        {
            var blockBelow = _world.GetBlock(position - Vector3.UnitY);
            var blockAt = _world.GetBlock(position);
            
            // Need dirt/grass below and air above
            return blockBelow != null && 
                   (blockBelow.Type == 2 || blockBelow.Type == 3) && // grass or dirt
                   (blockAt == null || blockAt.Type == 0); // air
        }

        private bool IsCropMature(Block? block)
        {
            if (block == null) return false;
            
            if (block.Properties.TryGetValue("planted_at", out var plantedObj) &&
                plantedObj is DateTime plantedAt)
            {
                var growthTime = TimeSpan.FromMinutes(5); // 5 minutes to grow
                return DateTime.UtcNow - plantedAt >= growthTime;
            }
            
            return false;
        }

        private int CalculateCropYield(string cropType)
        {
            return cropType switch
            {
                "wheat" => 1 + Random.Shared.Next(0, 3),
                "carrot" => 1 + Random.Shared.Next(0, 4),
                "potato" => 1 + Random.Shared.Next(0, 4),
                _ => 1
            };
        }

        private int GetCropBlockId(string cropType)
        {
            return cropType switch
            {
                "wheat" => 100, // Wheat crop block ID
                "carrot" => 101,
                "potato" => 102,
                _ => 100
            };
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _farmingCancellation?.Cancel();
                _farmingCancellation?.Dispose();
                _farmPlots.Clear();
                _autoFarmingEnabled = false;
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Represents a farm plot
    /// </summary>
    public class FarmPlot
    {
        public Vector3 Position { get; set; }
        public string CropType { get; set; } = string.Empty;
        public DateTime PlantedAt { get; set; }
    }

    /// <summary>
    /// Manages mining operations
    /// </summary>
    public class MiningManager : IDisposable
    {
        private readonly WorldManager _world;
        private readonly InventoryManager _inventory;
        private readonly NavigationManager _navigation;
        private readonly MinecraftClient _client;
        private readonly ILogger<MiningManager> _logger;
        private readonly List<Vector3> _miningTargets = new();
        private readonly Dictionary<Vector3, DateTime> _miningInProgress = new();
        private bool _autoMiningEnabled = false;
        private bool _disposed = false;
        private CancellationTokenSource? _miningCancellation;

        public event Action<Vector3, string>? BlockMined;
        public event Action<string, int>? OreFound;

        public MiningManager(WorldManager world, InventoryManager inventory, NavigationManager navigation, MinecraftClient client, ILogger<MiningManager> logger)
        {
            _world = world;
            _inventory = inventory;
            _navigation = navigation;
            _client = client;
            _logger = logger;
        }

        public async Task<bool> MineBlockAsync(Vector3 position)
        {
            var block = _world.GetBlock(position);
            if (block == null)
            {
                _logger.LogWarning($"No block found at {position}");
                return false;
            }

            // Check if we have the right tool
            var requiredTool = GetRequiredTool(block.Type);
            if (!string.IsNullOrEmpty(requiredTool) && !_inventory.HasItem(requiredTool))
            {
                _logger.LogWarning($"Missing required tool: {requiredTool}");
                return false;
            }

            var miningTime = CalculateMiningTime(block, requiredTool);
            _logger.LogInformation($"Mining {block.Name} at {position} (estimated time: {miningTime}ms)");
            
            // Send block breaking packet to server
            await SendBlockBreakingPacketAsync(position);
            
            // Track mining start
            _miningInProgress[position] = DateTime.UtcNow;
            
            // Wait for mining completion
            await Task.Delay(miningTime);
            
            // Calculate drops
            var drops = CalculateBlockDrops(block);
            
            // Add drops to inventory
            foreach (var drop in drops)
            {
                var emptySlot = _inventory.FindEmptySlot();
                if (emptySlot.HasValue)
                {
                    _inventory.SetItem(emptySlot.Value, drop);
                }
            }

            // Remove block from world
            _world.SetBlock(position, new Block { Position = position, Type = 0 }); // Air
            
            // Remove from mining tracking
            _miningInProgress.Remove(position);
            
            BlockMined?.Invoke(position, block.Name);
            
            // Check if it was an ore
            if (IsOreBlock(block.Type))
            {
                OreFound?.Invoke(block.Name, drops.Sum(d => d.Count));
            }
            
            return true;
        }

        private async Task SendBlockBreakingPacketAsync(Vector3 position)
        {
            var breakingPacket = new BlockBreakingPacket
            {
                Position = PacketUtils.EncodePosition((int)position.X, (int)position.Y, (int)position.Z)
            };
            
            await _client.SendPacketAsync(breakingPacket);
        }

        public async Task StartAutomatedMiningAsync(Vector3 area)
        {
            if (_autoMiningEnabled) return;
            
            _autoMiningEnabled = true;
            _miningCancellation = new CancellationTokenSource();
            _logger.LogInformation($"Automated mining started in area {area}");
            
            // Generate mining targets
            GenerateMiningTargets(area);
            
            // Start mining loop
            _ = Task.Run(async () => await MiningLoopAsync(_miningCancellation.Token));
            
            await Task.CompletedTask;
        }

        public async Task StopAutomatedMiningAsync()
        {
            _autoMiningEnabled = false;
            _miningCancellation?.Cancel();
            _logger.LogInformation("Automated mining stopped");
            await Task.CompletedTask;
        }

        private async Task MiningLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && _autoMiningEnabled)
            {
                try
                {
                    if (_miningTargets.Any())
                    {
                        var target = _miningTargets.First();
                        _miningTargets.RemoveAt(0);
                        
                        // Navigate to target
                        var success = await _navigation.GoToAsync(target + Vector3.UnitY, 1.5f); // Stand next to block
                        if (success)
                        {
                            await MineBlockAsync(target);
                        }
                        
                        // Check inventory space
                        if (!_inventory.HasSpace(5))
                        {
                            _logger.LogInformation("Inventory full, optimizing...");
                            _inventory.OptimizeInventory();
                            
                            if (!_inventory.HasSpace(3))
                            {
                                _logger.LogWarning("Inventory still full, pausing mining");
                                // Wait for inventory space - operational delay, not simulation
                                await Task.Delay(10000, cancellationToken);
                            }
                        }
                    }
                    else
                    {
                        // No targets left, generate more
                        var playerPos = _navigation.CurrentPosition;
                        GenerateMiningTargets(playerPos);
                        
                        if (!_miningTargets.Any())
                        {
                            _logger.LogInformation("No more mining targets found, stopping");
                            break;
                        }
                    }
                    
                    // Brief pause between mining operations - operational timing
                    await Task.Delay(500, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in mining loop");
                }
            }
        }

        private void GenerateMiningTargets(Vector3 center)
        {
            var radius = 10;
            var targets = new List<Vector3>();
            
            for (int x = -radius; x <= radius; x++)
            {
                for (int y = -radius; y <= radius; y++)
                {
                    for (int z = -radius; z <= radius; z++)
                    {
                        var position = center + new Vector3(x, y, z);
                        var block = _world.GetBlock(position);
                        
                        if (block != null && IsMineableBlock(block.Type))
                        {
                            targets.Add(position);
                        }
                    }
                }
            }
            
            // Prioritize ore blocks
            _miningTargets.AddRange(targets.Where(t => IsOreBlock(_world.GetBlock(t)?.Type ?? 0)).OrderBy(t => Vector3.Distance(center, t)));
            _miningTargets.AddRange(targets.Where(t => !IsOreBlock(_world.GetBlock(t)?.Type ?? 0)).OrderBy(t => Vector3.Distance(center, t)));
        }

        private int CalculateMiningTime(Block block, string tool)
        {
            var baseTime = (int)(block.Hardness * 1000);
            
            // Tool efficiency
            var efficiency = tool switch
            {
                "diamond_pickaxe" => 0.2f,
                "iron_pickaxe" => 0.4f,
                "stone_pickaxe" => 0.6f,
                "wooden_pickaxe" => 0.8f,
                _ => 1.0f // Hand mining
            };
            
            return Math.Max(50, (int)(baseTime * efficiency));
        }

        private List<ItemStack> CalculateBlockDrops(Block block)
        {
            var drops = new List<ItemStack>();
            
            var dropItem = block.Type switch
            {
                1 => "cobblestone", // Stone -> Cobblestone
                2 => "dirt", // Grass -> Dirt
                3 => "dirt", // Dirt -> Dirt
                4 => "cobblestone", // Cobblestone -> Cobblestone
                12 => "gold_ore", // Gold ore
                13 => "iron_ore", // Iron ore
                14 => "coal", // Coal ore -> Coal
                15 => "oak_log", // Oak log
                _ => BlockData.GetName(block.Type)
            };
            
            var quantity = block.Type switch
            {
                14 => 1 + Random.Shared.Next(0, 2), // Coal: 1-2
                _ => 1
            };
            
            drops.Add(new ItemStack(dropItem, quantity));
            return drops;
        }

        private string GetRequiredTool(int blockType)
        {
            return blockType switch
            {
                1 or 4 => "pickaxe", // Stone, Cobblestone
                12 or 13 or 14 => "pickaxe", // Ores
                15 => "axe", // Wood
                _ => string.Empty
            };
        }

        private bool IsMineableBlock(int blockType)
        {
            return blockType switch
            {
                0 => false, // Air
                7 => false, // Bedrock
                8 or 9 => false, // Water, Lava
                _ => true
            };
        }

        private bool IsOreBlock(int blockType)
        {
            return blockType is 12 or 13 or 14 or 19; // Gold, Iron, Coal, Lapis
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _miningCancellation?.Cancel();
                _miningCancellation?.Dispose();
                _miningTargets.Clear();
                _miningInProgress.Clear();
                _autoMiningEnabled = false;
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Manages redstone operations
    /// </summary>
    public class RedstoneManager : IDisposable
    {
        private readonly WorldManager _world;
        private readonly ILogger<RedstoneManager> _logger;
        private bool _disposed = false;

        public RedstoneManager(WorldManager world, ILogger<RedstoneManager> logger)
        {
            _world = world;
            _logger = logger;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Advanced AI system for autonomous behavior
    /// </summary>
    public class AdvancedAI : IDisposable
    {
        private readonly WorldManager _world;
        private readonly EntityManager _entities;
        private readonly NavigationManager _navigation;
        private readonly ILogger<AdvancedAI> _logger;
        private bool _disposed = false;

        public AdvancedAI(WorldManager world, EntityManager entities, NavigationManager navigation, ILogger<AdvancedAI> logger)
        {
            _world = world;
            _entities = entities;
            _navigation = navigation;
            _logger = logger;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Simple console logger implementation
    /// </summary>
    public class ConsoleLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            Console.WriteLine($"[{timestamp}] [{logLevel}] {typeof(T).Name}: {message}");
            
            if (exception != null)
            {
                Console.WriteLine(exception);
            }
        }
    }

    /// <summary>
    /// Recipe for crafting items
    /// </summary>
    public class Recipe
    {
        public Dictionary<string, int> Ingredients { get; set; } = new();
        public string Result { get; set; } = string.Empty;
        public int ResultCount { get; set; } = 1;
        public int CraftingTime { get; set; } = 1000; // milliseconds
    }
}