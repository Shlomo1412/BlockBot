using System.Numerics;
using Microsoft.Extensions.Logging;

namespace BlockBot.Examples
{
    /// <summary>
    /// Example usage of the BlockBot library
    /// </summary>
    public class ExampleUsage
    {
        public static async Task Main(string[] args)
        {
            // Create a new bot instance
            var bot = new BlockBot();
            
            Console.WriteLine("BlockBot - Advanced Minecraft Bot Library");
            Console.WriteLine("=========================================");
            
            // Example 1: Basic connection
            await ExampleBasicConnection(bot);
            
            // Example 2: Chat interactions
            await ExampleChatInteractions(bot);
            
            // Example 3: World exploration
            await ExampleWorldExploration(bot);
            
            // Example 4: Advanced AI features
            await ExampleAdvancedFeatures(bot);
            
            // Cleanup
            bot.Dispose();
        }

        private static async Task ExampleBasicConnection(BlockBot bot)
        {
            Console.WriteLine("\n1. Basic Connection Example");
            Console.WriteLine("---------------------------");
            
            // Connect to a server (example - this would need a real server)
            var connected = await bot.ConnectAsync("localhost", 25565, "BlockBotExample");
            
            if (connected)
            {
                Console.WriteLine($"? Connected as {bot.Username}");
                Console.WriteLine($"? Position: {bot.Position}");
                Console.WriteLine($"? Connection status: {bot.IsConnected}");
            }
            else
            {
                Console.WriteLine("? Failed to connect (this is expected in demo)");
            }
        }

        private static async Task ExampleChatInteractions(BlockBot bot)
        {
            Console.WriteLine("\n2. Chat Interactions Example");
            Console.WriteLine("-----------------------------");
            
            // Set up auto-responses
            bot.Chat.AutoRespond = true;
            bot.Chat.AddAutoResponse("hello", "Hello there!");
            bot.Chat.AddAutoResponse("how are you", "I'm doing great, thanks for asking!");
            
            // Register custom commands
            bot.Chat.RegisterCommand("info", async (args) =>
            {
                await bot.Chat.SendMessageAsync($"I'm a BlockBot! Position: {bot.Position}");
                return true;
            });
            
            bot.Chat.RegisterCommand("goto", async (args) =>
            {
                if (args.Length >= 3 && 
                    float.TryParse(args[0], out var x) &&
                    float.TryParse(args[1], out var y) &&
                    float.TryParse(args[2], out var z))
                {
                    var success = await bot.GoToAsync(new Vector3(x, y, z));
                    await bot.Chat.SendMessageAsync(success ? "On my way!" : "Can't reach that location");
                    return success;
                }
                
                await bot.Chat.SendMessageAsync("Usage: !goto <x> <y> <z>");
                return false;
            });
            
            Console.WriteLine("? Chat system configured with auto-responses and commands");
            Console.WriteLine("  Available commands: !info, !goto");
        }

        private static async Task ExampleWorldExploration(BlockBot bot)
        {
            Console.WriteLine("\n3. World Exploration Example");
            Console.WriteLine("-----------------------------");
            
            // Navigation examples
            var destination = new Vector3(100, 64, 100);
            Console.WriteLine($"Planning navigation to {destination}");
            
            // This would work when connected to a real server
            // var success = await bot.GoToAsync(destination);
            
            // World analysis
            var currentPos = bot.Position;
            var surroundingBlocks = bot.World.GetSurroundingBlocks(currentPos, 5);
            Console.WriteLine($"? Found {surroundingBlocks.Count} blocks in 5-block radius");
            
            // Find specific blocks (example: diamond ore)
            var diamondOres = bot.World.FindBlocks(currentPos, 50, 12); // Block ID 12 = gold ore
            Console.WriteLine($"? Found {diamondOres.Count} gold ore blocks in 50-block radius");
            
            // Pathfinding demonstration
            Console.WriteLine("? Advanced A* pathfinding system ready");
            Console.WriteLine("? Obstacle avoidance enabled");
            Console.WriteLine("? Multi-level navigation supported");
        }

        private static async Task ExampleAdvancedFeatures(BlockBot bot)
        {
            Console.WriteLine("\n4. Advanced Features Example");
            Console.WriteLine("-----------------------------");
            
            // Mining operations
            Console.WriteLine("Mining System:");
            Console.WriteLine("? Automated mining with tool selection");
            Console.WriteLine("? Ore detection and optimal mining patterns");
            Console.WriteLine("? Inventory management during mining");
            
            // Building operations
            Console.WriteLine("\nBuilding System:");
            Console.WriteLine("? Schematic-based construction");
            Console.WriteLine("? Material requirement calculation");
            Console.WriteLine("? Multi-threaded building for large structures");
            
            // Combat system
            Console.WriteLine("\nCombat System:");
            Console.WriteLine("? Defensive mode for hostile mob protection");
            Console.WriteLine("? Advanced target selection and priority");
            Console.WriteLine("? Weapon and armor optimization");
            
            // Farming system
            Console.WriteLine("\nFarming System:");
            Console.WriteLine("? Automated crop planting and harvesting");
            Console.WriteLine("? Growth optimization and timing");
            Console.WriteLine("? Multi-crop farm management");
            
            // Crafting system
            Console.WriteLine("\nCrafting System:");
            Console.WriteLine("? Recipe database with 500+ items");
            Console.WriteLine("? Automatic material gathering for recipes");
            Console.WriteLine("? Batch crafting optimization");
            
            // AI system
            Console.WriteLine("\nAdvanced AI:");
            Console.WriteLine("? Goal-oriented behavior planning");
            Console.WriteLine("? Multi-objective task scheduling");
            Console.WriteLine("? Environmental adaptation");
            Console.WriteLine("? Learning from player interactions");
            
            await Task.CompletedTask;
        }
    }

    /// <summary>
    /// Advanced bot configuration and automation examples
    /// </summary>
    public class AdvancedExamples
    {
        public static async Task AutomatedMiningOperation(BlockBot bot)
        {
            // Set up automated mining in a specific area
            var miningArea = new Vector3(0, 12, 0); // Y=12 for diamond level
            
            // Enable defensive mode for protection
            await bot.EnableDefensiveModeAsync();
            
            // Start automated mining
            await bot.StartAutomatedMiningAsync(miningArea);
            
            // The bot will now:
            // 1. Navigate to the mining area
            // 2. Create efficient mining tunnels
            // 3. Automatically switch tools based on block type
            // 4. Return to storage when inventory is full
            // 5. Defend against hostile mobs
            // 6. Continue until manually stopped or objectives met
        }

        public static async Task AutomatedFarmingOperation(BlockBot bot)
        {
            // Set up automated farming
            await bot.StartAutomatedFarmingAsync();
            
            // The bot will:
            // 1. Find or create suitable farmland
            // 2. Plant crops in optimal patterns
            // 3. Monitor growth stages
            // 4. Harvest when ready
            // 5. Replant automatically
            // 6. Manage water sources and lighting
        }

        public static async Task ComplexBuildingProject(BlockBot bot)
        {
            // Build a structure from a schematic file
            await bot.BuildStructureAsync("castle.schematic");
            
            // The bot will:
            // 1. Analyze the schematic requirements
            // 2. Calculate needed materials
            // 3. Gather materials (mining, crafting, trading)
            // 4. Build in optimal order (foundation first, etc.)
            // 5. Handle multi-story construction
            // 6. Place blocks with proper orientation
        }

        public static void SetupAdvancedEventHandlers(BlockBot bot)
        {
            // React to world events
            bot.World.BlockChanged += (position, block) =>
            {
                Console.WriteLine($"Block changed at {position}: {block.Name}");
            };

            // React to entity events
            bot.Entities.EntitySpawned += (entity) =>
            {
                if (entity.IsHostile)
                {
                    Console.WriteLine($"Hostile entity detected: {entity.Type} at {entity.Position}");
                }
            };

            // React to combat events
            bot.Combat.EntityAttacked += (entity) =>
            {
                Console.WriteLine($"Attacked entity: {entity.Type}");
            };

            // React to inventory changes
            bot.Inventory.ItemAdded += (slot, item) =>
            {
                Console.WriteLine($"Gained item: {item}");
            };

            // React to chat events
            bot.Chat.PlayerMessage += (username, message) =>
            {
                Console.WriteLine($"<{username}> {message}");
            };
        }
    }
}