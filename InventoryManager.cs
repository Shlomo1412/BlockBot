using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace BlockBot
{
    /// <summary>
    /// Manages inventory operations and item handling
    /// </summary>
    public class InventoryManager : IDisposable
    {
        private readonly ILogger<InventoryManager> _logger;
        private readonly Dictionary<int, ItemStack> _inventory;
        private readonly Dictionary<int, ItemStack> _hotbar;
        private readonly Dictionary<int, ItemStack> _armor;
        private readonly Dictionary<int, ItemStack> _offhand;
        private bool _disposed = false;

        public event Action<int, ItemStack>? ItemChanged;
        public event Action<int>? ItemRemoved;
        public event Action<int, ItemStack>? ItemAdded;

        public int SelectedSlot { get; private set; } = 0;

        public InventoryManager(ILogger<InventoryManager> logger)
        {
            _logger = logger;
            _inventory = new Dictionary<int, ItemStack>();
            _hotbar = new Dictionary<int, ItemStack>();
            _armor = new Dictionary<int, ItemStack>();
            _offhand = new Dictionary<int, ItemStack>();
        }

        public async Task InitializeAsync()
        {
            _logger.LogInformation("Initializing inventory manager");
            await Task.CompletedTask;
        }

        public async Task HandlePacketAsync(Packet packet)
        {
            // Handle inventory-related packets
            await Task.CompletedTask;
        }

        public ItemStack? GetItem(int slot)
        {
            if (slot < 9) // Hotbar
                return _hotbar.TryGetValue(slot, out var hotbarItem) ? hotbarItem : null;
            
            if (slot < 36) // Main inventory
                return _inventory.TryGetValue(slot - 9, out var invItem) ? invItem : null;
            
            if (slot < 40) // Armor
                return _armor.TryGetValue(slot - 36, out var armorItem) ? armorItem : null;
            
            if (slot == 40) // Offhand
                return _offhand.TryGetValue(0, out var offhandItem) ? offhandItem : null;

            return null;
        }

        public void SetItem(int slot, ItemStack item)
        {
            var previousItem = GetItem(slot);
            
            if (slot < 9) // Hotbar
                _hotbar[slot] = item;
            else if (slot < 36) // Main inventory
                _inventory[slot - 9] = item;
            else if (slot < 40) // Armor
                _armor[slot - 36] = item;
            else if (slot == 40) // Offhand
                _offhand[0] = item;

            if (previousItem == null)
                ItemAdded?.Invoke(slot, item);
            else
                ItemChanged?.Invoke(slot, item);
        }

        public void RemoveItem(int slot)
        {
            var item = GetItem(slot);
            if (item == null) return;

            if (slot < 9) // Hotbar
                _hotbar.Remove(slot);
            else if (slot < 36) // Main inventory
                _inventory.Remove(slot - 9);
            else if (slot < 40) // Armor
                _armor.Remove(slot - 36);
            else if (slot == 40) // Offhand
                _offhand.Remove(0);

            ItemRemoved?.Invoke(slot);
        }

        public ItemStack? GetSelectedItem()
        {
            return GetItem(SelectedSlot);
        }

        public void SelectSlot(int slot)
        {
            if (slot >= 0 && slot < 9)
            {
                SelectedSlot = slot;
                _logger.LogDebug($"Selected hotbar slot {slot}");
            }
        }

        public int GetItemCount(string itemType)
        {
            int count = 0;

            // Check hotbar
            foreach (var item in _hotbar.Values)
            {
                if (item.Type == itemType)
                    count += item.Count;
            }

            // Check main inventory
            foreach (var item in _inventory.Values)
            {
                if (item.Type == itemType)
                    count += item.Count;
            }

            return count;
        }

        public List<int> FindItemSlots(string itemType)
        {
            var slots = new List<int>();

            // Check hotbar
            for (int i = 0; i < 9; i++)
            {
                if (_hotbar.TryGetValue(i, out var item) && item.Type == itemType)
                    slots.Add(i);
            }

            // Check main inventory
            for (int i = 0; i < 27; i++)
            {
                if (_inventory.TryGetValue(i, out var item) && item.Type == itemType)
                    slots.Add(i + 9);
            }

            return slots;
        }

        public int? FindEmptySlot()
        {
            // Check hotbar first
            for (int i = 0; i < 9; i++)
            {
                if (!_hotbar.ContainsKey(i))
                    return i;
            }

            // Check main inventory
            for (int i = 0; i < 27; i++)
            {
                if (!_inventory.ContainsKey(i))
                    return i + 9;
            }

            return null;
        }

        public bool HasItem(string itemType, int count = 1)
        {
            return GetItemCount(itemType) >= count;
        }

        public bool HasSpace(int slots = 1)
        {
            int emptySlots = 0;

            // Count empty hotbar slots
            for (int i = 0; i < 9; i++)
            {
                if (!_hotbar.ContainsKey(i))
                    emptySlots++;
            }

            // Count empty inventory slots
            for (int i = 0; i < 27; i++)
            {
                if (!_inventory.ContainsKey(i))
                    emptySlots++;
            }

            return emptySlots >= slots;
        }

        public List<ItemStack> GetAllItems()
        {
            var allItems = new List<ItemStack>();
            allItems.AddRange(_hotbar.Values);
            allItems.AddRange(_inventory.Values);
            allItems.AddRange(_armor.Values);
            allItems.AddRange(_offhand.Values);
            return allItems;
        }

        public Dictionary<string, int> GetItemSummary()
        {
            var summary = new Dictionary<string, int>();

            foreach (var item in GetAllItems())
            {
                if (summary.ContainsKey(item.Type))
                    summary[item.Type] += item.Count;
                else
                    summary[item.Type] = item.Count;
            }

            return summary;
        }

        public bool CanCombineStacks(ItemStack stack1, ItemStack stack2)
        {
            return stack1.Type == stack2.Type && 
                   stack1.Count + stack2.Count <= ItemData.GetMaxStackSize(stack1.Type);
        }

        public void OptimizeInventory()
        {
            var allSlots = new List<int>();
            
            // Add all slot numbers
            for (int i = 0; i < 36; i++)
                allSlots.Add(i);

            // Group items by type
            var itemGroups = new Dictionary<string, List<(int slot, ItemStack item)>>();

            foreach (var slot in allSlots)
            {
                var item = GetItem(slot);
                if (item == null) continue;

                if (!itemGroups.ContainsKey(item.Type))
                    itemGroups[item.Type] = new List<(int, ItemStack)>();

                itemGroups[item.Type].Add((slot, item));
            }

            // Combine stackable items
            foreach (var group in itemGroups)
            {
                var items = group.Value.OrderBy(x => x.slot).ToList();
                var maxStackSize = ItemData.GetMaxStackSize(group.Key);

                for (int i = 0; i < items.Count - 1; i++)
                {
                    var current = items[i];
                    
                    for (int j = i + 1; j < items.Count; j++)
                    {
                        var next = items[j];
                        
                        if (current.item.Count >= maxStackSize) break;

                        var canTransfer = Math.Min(next.item.Count, maxStackSize - current.item.Count);
                        
                        if (canTransfer > 0)
                        {
                            current.item.Count += canTransfer;
                            next.item.Count -= canTransfer;

                            SetItem(current.slot, current.item);

                            if (next.item.Count == 0)
                            {
                                RemoveItem(next.slot);
                                items.RemoveAt(j);
                                j--;
                            }
                            else
                            {
                                SetItem(next.slot, next.item);
                            }
                        }
                    }
                }
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _inventory.Clear();
                _hotbar.Clear();
                _armor.Clear();
                _offhand.Clear();
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Represents a stack of items
    /// </summary>
    public class ItemStack
    {
        public string Type { get; set; }
        public int Count { get; set; }
        public int Durability { get; set; }
        public Dictionary<string, object> Enchantments { get; set; } = new();
        public Dictionary<string, object> Metadata { get; set; } = new();

        public ItemStack(string type, int count = 1, int durability = -1)
        {
            Type = type;
            Count = count;
            Durability = durability;
        }

        public bool IsStackable => ItemData.IsStackable(Type);
        public int MaxStackSize => ItemData.GetMaxStackSize(Type);
        public bool IsTool => ItemData.IsTool(Type);
        public bool IsWeapon => ItemData.IsWeapon(Type);
        public bool IsArmor => ItemData.IsArmor(Type);
        public bool IsFood => ItemData.IsFood(Type);
        public bool IsBlock => ItemData.IsBlock(Type);

        public ItemStack Clone()
        {
            return new ItemStack(Type, Count, Durability)
            {
                Enchantments = new Dictionary<string, object>(Enchantments),
                Metadata = new Dictionary<string, object>(Metadata)
            };
        }

        public override string ToString()
        {
            return Count > 1 ? $"{Type} x{Count}" : Type;
        }
    }

    /// <summary>
    /// Static data about different item types
    /// </summary>
    public static class ItemData
    {
        private static readonly Dictionary<string, ItemInfo> ItemInfos = new()
        {
            // Tools
            { "wooden_pickaxe", new ItemInfo(ItemCategory.Tool, 1, 59, true, false, false, false, false) },
            { "stone_pickaxe", new ItemInfo(ItemCategory.Tool, 1, 131, true, false, false, false, false) },
            { "iron_pickaxe", new ItemInfo(ItemCategory.Tool, 1, 250, true, false, false, false, false) },
            { "diamond_pickaxe", new ItemInfo(ItemCategory.Tool, 1, 1561, true, false, false, false, false) },
            { "netherite_pickaxe", new ItemInfo(ItemCategory.Tool, 1, 2031, true, false, false, false, false) },
            
            { "wooden_sword", new ItemInfo(ItemCategory.Weapon, 1, 59, false, true, false, false, false) },
            { "stone_sword", new ItemInfo(ItemCategory.Weapon, 1, 131, false, true, false, false, false) },
            { "iron_sword", new ItemInfo(ItemCategory.Weapon, 1, 250, false, true, false, false, false) },
            { "diamond_sword", new ItemInfo(ItemCategory.Weapon, 1, 1561, false, true, false, false, false) },
            { "netherite_sword", new ItemInfo(ItemCategory.Weapon, 1, 2031, false, true, false, false, false) },

            // Armor
            { "leather_helmet", new ItemInfo(ItemCategory.Armor, 1, 55, false, false, true, false, false) },
            { "iron_helmet", new ItemInfo(ItemCategory.Armor, 1, 165, false, false, true, false, false) },
            { "diamond_helmet", new ItemInfo(ItemCategory.Armor, 1, 363, false, false, true, false, false) },
            
            // Food
            { "bread", new ItemInfo(ItemCategory.Food, 64, -1, false, false, false, true, false) },
            { "apple", new ItemInfo(ItemCategory.Food, 64, -1, false, false, false, true, false) },
            { "cooked_beef", new ItemInfo(ItemCategory.Food, 64, -1, false, false, false, true, false) },
            
            // Blocks
            { "stone", new ItemInfo(ItemCategory.Block, 64, -1, false, false, false, false, true) },
            { "dirt", new ItemInfo(ItemCategory.Block, 64, -1, false, false, false, false, true) },
            { "cobblestone", new ItemInfo(ItemCategory.Block, 64, -1, false, false, false, false, true) },
            { "oak_planks", new ItemInfo(ItemCategory.Block, 64, -1, false, false, false, false, true) },
            
            // Materials
            { "coal", new ItemInfo(ItemCategory.Material, 64, -1, false, false, false, false, false) },
            { "iron_ingot", new ItemInfo(ItemCategory.Material, 64, -1, false, false, false, false, false) },
            { "gold_ingot", new ItemInfo(ItemCategory.Material, 64, -1, false, false, false, false, false) },
            { "diamond", new ItemInfo(ItemCategory.Material, 64, -1, false, false, false, false, false) },
            { "stick", new ItemInfo(ItemCategory.Material, 64, -1, false, false, false, false, false) }
        };

        public static bool IsStackable(string itemType)
        {
            return GetMaxStackSize(itemType) > 1;
        }

        public static int GetMaxStackSize(string itemType)
        {
            return ItemInfos.TryGetValue(itemType, out var info) ? info.MaxStackSize : 64;
        }

        public static bool IsTool(string itemType)
        {
            return ItemInfos.TryGetValue(itemType, out var info) && info.IsTool;
        }

        public static bool IsWeapon(string itemType)
        {
            return ItemInfos.TryGetValue(itemType, out var info) && info.IsWeapon;
        }

        public static bool IsArmor(string itemType)
        {
            return ItemInfos.TryGetValue(itemType, out var info) && info.IsArmor;
        }

        public static bool IsFood(string itemType)
        {
            return ItemInfos.TryGetValue(itemType, out var info) && info.IsFood;
        }

        public static bool IsBlock(string itemType)
        {
            return ItemInfos.TryGetValue(itemType, out var info) && info.IsBlock;
        }

        public static int GetMaxDurability(string itemType)
        {
            return ItemInfos.TryGetValue(itemType, out var info) ? info.MaxDurability : -1;
        }

        public static ItemCategory GetCategory(string itemType)
        {
            return ItemInfos.TryGetValue(itemType, out var info) ? info.Category : ItemCategory.Misc;
        }

        private record ItemInfo(ItemCategory Category, int MaxStackSize, int MaxDurability, bool IsTool, bool IsWeapon, bool IsArmor, bool IsFood, bool IsBlock);
    }

    /// <summary>
    /// Item categories
    /// </summary>
    public enum ItemCategory
    {
        Tool,
        Weapon,
        Armor,
        Food,
        Block,
        Material,
        Misc
    }
}