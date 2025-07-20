using System.Collections.Concurrent;
using System.Numerics;
using Microsoft.Extensions.Logging;

namespace BlockBot
{
    /// <summary>
    /// Advanced navigation and pathfinding system
    /// </summary>
    public class NavigationManager : IDisposable
    {
        private readonly ILogger<NavigationManager> _logger;
        private readonly WorldManager _world;
        private readonly EntityManager _entities;
        private readonly PathfindingEngine _pathfinding;
        private readonly MovementController _movement;
        private bool _disposed = false;

        private Vector3? _currentTarget;
        private Queue<Vector3> _currentPath = new();
        private bool _isNavigating = false;
        private CancellationTokenSource? _navigationCancellation;
        private Vector3 _currentPosition = Vector3.Zero;

        public event Action<Vector3>? PathStarted;
        public event Action<Vector3>? PathCompleted;
        public event Action? PathFailed;
        public event Action<Vector3>? WaypointReached;
        public event Action<Vector3>? PositionUpdated;

        public bool IsNavigating => _isNavigating;
        public Vector3? CurrentTarget => _currentTarget;
        public int RemainingWaypoints => _currentPath.Count;
        public Vector3 CurrentPosition => _currentPosition;

        public NavigationManager(WorldManager world, EntityManager entities, ILogger<NavigationManager> logger)
        {
            _world = world;
            _entities = entities;
            _logger = logger;
            _pathfinding = new PathfindingEngine(world, logger);
            _movement = new MovementController(this, logger);
        }

        public async Task InitializeAsync()
        {
            _logger.LogInformation("Initializing navigation manager");
            await Task.CompletedTask;
        }

        public void UpdatePosition(Vector3 position)
        {
            _currentPosition = position;
            PositionUpdated?.Invoke(position);
        }

        public async Task<bool> GoToAsync(Vector3 destination, float tolerance = 0.5f)
        {
            try
            {
                _logger.LogInformation($"Navigating to {destination}");
                
                // Cancel any existing navigation
                await StopNavigationAsync();

                var playerPos = _currentPosition;
                
                // Check if already at destination
                if (Vector3.Distance(playerPos, destination) <= tolerance)
                {
                    _logger.LogDebug("Already at destination");
                    return true;
                }

                // Find path
                var path = await _pathfinding.FindPathAsync(playerPos, destination);
                if (path == null || path.Count == 0)
                {
                    _logger.LogWarning($"No path found to {destination}");
                    PathFailed?.Invoke();
                    return false;
                }

                // Start navigation
                _currentTarget = destination;
                _currentPath = new Queue<Vector3>(path);
                _isNavigating = true;
                _navigationCancellation = new CancellationTokenSource();

                PathStarted?.Invoke(destination);

                // Execute navigation
                var success = await ExecuteNavigationAsync(tolerance, _navigationCancellation.Token);
                
                if (success)
                {
                    PathCompleted?.Invoke(destination);
                }
                else
                {
                    PathFailed?.Invoke();
                }

                return success;
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Navigation cancelled");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Navigation failed");
                PathFailed?.Invoke();
                return false;
            }
            finally
            {
                _isNavigating = false;
                _currentTarget = null;
                _currentPath.Clear();
            }
        }

        public async Task<bool> FollowEntityAsync(int entityId, float distance = 3.0f)
        {
            try
            {
                _logger.LogInformation($"Following entity {entityId} at distance {distance}");
                
                _navigationCancellation = new CancellationTokenSource();
                _isNavigating = true;

                while (!_navigationCancellation.Token.IsCancellationRequested)
                {
                    // Get entity position from EntityManager
                    var entity = _entities.GetEntity(entityId);
                    if (entity == null)
                    {
                        _logger.LogWarning($"Entity {entityId} not found, stopping following");
                        break;
                    }
                    
                    var entityPosition = entity.Position;
                    var distanceToEntity = Vector3.Distance(_currentPosition, entityPosition);
                    
                    // If too far, move closer
                    if (distanceToEntity > distance + 2.0f)
                    {
                        var direction = Vector3.Normalize(entityPosition - _currentPosition);
                        var targetPosition = entityPosition - direction * distance;
                        
                        await GoToAsync(targetPosition, 1.0f);
                    }
                    
                    // Update every second - operational timing for entity following
                    await Task.Delay(1000, _navigationCancellation.Token);
                }

                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            finally
            {
                _isNavigating = false;
            }
        }

        public async Task StopNavigationAsync()
        {
            if (_isNavigating)
            {
                _navigationCancellation?.Cancel();
                await _movement.StopAsync();
                _isNavigating = false;
                _currentTarget = null;
                _currentPath.Clear();
                _logger.LogDebug("Navigation stopped");
            }
        }

        public void OnChunkLoaded(Chunk chunk)
        {
            // Update pathfinding when new chunks are loaded
            _pathfinding.InvalidateCache(chunk.Coordinate);
        }

        private async Task<bool> ExecuteNavigationAsync(float tolerance, CancellationToken cancellationToken)
        {
            while (_currentPath.Count > 0 && !cancellationToken.IsCancellationRequested)
            {
                var waypoint = _currentPath.Dequeue();
                
                // Move to waypoint
                var success = await _movement.MoveToAsync(waypoint, tolerance, cancellationToken);
                
                if (!success)
                {
                    _logger.LogWarning($"Failed to reach waypoint {waypoint}");
                    
                    // Try to recalculate path
                    var newPath = await _pathfinding.FindPathAsync(_currentPosition, _currentTarget!.Value);
                    if (newPath != null && newPath.Count > 0)
                    {
                        _currentPath = new Queue<Vector3>(newPath);
                        continue;
                    }
                    
                    return false;
                }

                WaypointReached?.Invoke(waypoint);
                _logger.LogDebug($"Reached waypoint {waypoint}");
            }

            return true;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _navigationCancellation?.Cancel();
                _navigationCancellation?.Dispose();
                _pathfinding?.Dispose();
                _movement?.Dispose();
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Advanced A* pathfinding engine
    /// </summary>
    public class PathfindingEngine : IDisposable
    {
        private readonly WorldManager _world;
        private readonly ILogger _logger;
        private readonly ConcurrentDictionary<ChunkCoordinate, DateTime> _cacheInvalidation;
        private readonly Dictionary<string, List<Vector3>> _pathCache;
        private bool _disposed = false;

        public PathfindingEngine(WorldManager world, ILogger logger)
        {
            _world = world;
            _logger = logger;
            _cacheInvalidation = new ConcurrentDictionary<ChunkCoordinate, DateTime>();
            _pathCache = new Dictionary<string, List<Vector3>>();
        }

        public async Task<List<Vector3>?> FindPathAsync(Vector3 start, Vector3 end, int maxNodes = 10000)
        {
            return await Task.Run(() => FindPath(start, end, maxNodes));
        }

        public List<Vector3>? FindPath(Vector3 start, Vector3 end, int maxNodes = 10000)
        {
            // Check cache first
            var cacheKey = $"{start}-{end}";
            if (_pathCache.TryGetValue(cacheKey, out var cachedPath))
            {
                _logger.LogDebug("Using cached path");
                return cachedPath;
            }

            var startNode = new PathNode(start, 0, GetHeuristic(start, end));
            var endPos = end;

            var openSet = new PriorityQueue<PathNode, float>();
            var closedSet = new HashSet<Vector3>();
            var nodeMap = new Dictionary<Vector3, PathNode>();

            openSet.Enqueue(startNode, startNode.F);
            nodeMap[start] = startNode;

            int nodesEvaluated = 0;

            while (openSet.Count > 0 && nodesEvaluated < maxNodes)
            {
                var current = openSet.Dequeue();
                nodesEvaluated++;

                if (Vector3.Distance(current.Position, endPos) < 1.0f)
                {
                    // Found path, reconstruct it
                    var path = ReconstructPath(current);
                    _logger.LogDebug($"Path found with {path.Count} waypoints (evaluated {nodesEvaluated} nodes)");
                    
                    // Cache the path
                    _pathCache[cacheKey] = path;
                    
                    return path;
                }

                closedSet.Add(current.Position);

                // Check all possible moves
                var neighbors = GetNeighbors(current.Position);
                
                foreach (var neighbor in neighbors)
                {
                    if (closedSet.Contains(neighbor))
                        continue;

                    if (!IsPassable(neighbor))
                        continue;

                    var gScore = current.G + GetMovementCost(current.Position, neighbor);
                    var hScore = GetHeuristic(neighbor, endPos);
                    
                    if (nodeMap.TryGetValue(neighbor, out var existingNode))
                    {
                        if (gScore < existingNode.G)
                        {
                            existingNode.G = gScore;
                            existingNode.Parent = current;
                            // Re-add to priority queue with updated priority
                            openSet.Enqueue(existingNode, existingNode.F);
                        }
                    }
                    else
                    {
                        var newNode = new PathNode(neighbor, gScore, hScore)
                        {
                            Parent = current
                        };
                        nodeMap[neighbor] = newNode;
                        openSet.Enqueue(newNode, newNode.F);
                    }
                }
            }

            _logger.LogWarning($"No path found (evaluated {nodesEvaluated} nodes)");
            return null;
        }

        private List<Vector3> GetNeighbors(Vector3 position)
        {
            var neighbors = new List<Vector3>();
            
            // 8-directional movement (horizontal only for basic implementation)
            for (int x = -1; x <= 1; x++)
            {
                for (int z = -1; z <= 1; z++)
                {
                    if (x == 0 && z == 0) continue;
                    
                    var neighbor = position + new Vector3(x, 0, z);
                    neighbors.Add(neighbor);
                    
                    // Also check one block up and one block down
                    neighbors.Add(neighbor + Vector3.UnitY);
                    neighbors.Add(neighbor - Vector3.UnitY);
                }
            }

            return neighbors;
        }

        private bool IsPassable(Vector3 position)
        {
            // Check if the position is passable (not solid blocks)
            var blockAtPos = _world.GetBlock(position);
            var blockAbove = _world.GetBlock(position + Vector3.UnitY);
            
            // Need space for player (2 blocks high)
            if (blockAtPos != null && blockAtPos.IsSolid) return false;
            if (blockAbove != null && blockAbove.IsSolid) return false;
            
            // Need solid ground beneath (unless flying/swimming)
            var blockBelow = _world.GetBlock(position - Vector3.UnitY);
            if (blockBelow == null || !blockBelow.IsSolid) return false;

            return true;
        }

        private float GetMovementCost(Vector3 from, Vector3 to)
        {
            var diff = to - from;
            
            // Base cost
            float cost = Vector3.Distance(from, to);
            
            // Penalize vertical movement
            if (Math.Abs(diff.Y) > 0.1f)
                cost *= 1.5f;
            
            // Penalize diagonal movement slightly
            if (Math.Abs(diff.X) > 0.1f && Math.Abs(diff.Z) > 0.1f)
                cost *= 1.1f;

            // Consider block types for more advanced pathfinding
            var blockBelow = _world.GetBlock(to - Vector3.UnitY);
            if (blockBelow != null)
            {
                // Prefer paths on solid ground
                if (blockBelow.IsSolid)
                    cost *= 1.0f;
                else if (blockBelow.IsLiquid)
                    cost *= 2.0f; // Swimming is slower
                else
                    cost *= 5.0f; // Avoid air gaps
            }

            return cost;
        }

        private float GetHeuristic(Vector3 from, Vector3 to)
        {
            // Manhattan distance with some diagonal consideration
            var diff = to - from;
            return Math.Abs(diff.X) + Math.Abs(diff.Y) + Math.Abs(diff.Z);
        }

        private List<Vector3> ReconstructPath(PathNode endNode)
        {
            var path = new List<Vector3>();
            var current = endNode;

            while (current != null)
            {
                path.Add(current.Position);
                current = current.Parent;
            }

            path.Reverse();
            return OptimizePath(path);
        }

        private List<Vector3> OptimizePath(List<Vector3> path)
        {
            if (path.Count <= 2) return path;

            var optimized = new List<Vector3> { path[0] };

            for (int i = 1; i < path.Count - 1; i++)
            {
                var current = path[i];
                var next = path[i + 1];
                var last = optimized.Last();

                // If we can go directly from last to next, skip current
                if (!_world.IsPathClear(last, next))
                {
                    optimized.Add(current);
                }
            }

            optimized.Add(path.Last());
            return optimized;
        }

        public void InvalidateCache(ChunkCoordinate chunk)
        {
            _cacheInvalidation[chunk] = DateTime.UtcNow;
            
            // Clear relevant cached paths
            var keysToRemove = _pathCache.Keys.Where(key => 
                key.Contains($"{chunk.X * 16}") || 
                key.Contains($"{chunk.Z * 16}")).ToList();
                
            foreach (var key in keysToRemove)
            {
                _pathCache.Remove(key);
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _cacheInvalidation.Clear();
                _pathCache.Clear();
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Controls actual player movement
    /// </summary>
    public class MovementController : IDisposable
    {
        private readonly NavigationManager _navigation;
        private readonly ILogger _logger;
        private bool _disposed = false;
        private CancellationTokenSource? _movementCancellation;

        public MovementController(NavigationManager navigation, ILogger logger)
        {
            _navigation = navigation;
            _logger = logger;
        }

        public async Task<bool> MoveToAsync(Vector3 target, float tolerance, CancellationToken cancellationToken)
        {
            try
            {
                _movementCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                
                var startPosition = _navigation.CurrentPosition;
                var distance = Vector3.Distance(startPosition, target);
                var moveTime = Math.Max(100, (int)(distance * 1000)); // 1 second per block minimum 100ms
                var steps = Math.Max(10, (int)(distance * 10)); // 10 steps per block
                
                for (int i = 0; i <= steps && !_movementCancellation.Token.IsCancellationRequested; i++)
                {
                    var progress = (float)i / steps;
                    var currentPos = Vector3.Lerp(startPosition, target, progress);
                    
                    // Update position
                    _navigation.UpdatePosition(currentPos);
                    
                    // Check if reached target
                    if (Vector3.Distance(currentPos, target) <= tolerance)
                    {
                        _navigation.UpdatePosition(target);
                        return true;
                    }
                    
                    await Task.Delay(moveTime / steps, _movementCancellation.Token);
                }

                return Vector3.Distance(_navigation.CurrentPosition, target) <= tolerance;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        public async Task StopAsync()
        {
            _movementCancellation?.Cancel();
            await Task.CompletedTask;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _movementCancellation?.Cancel();
                _movementCancellation?.Dispose();
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Represents a node in the pathfinding algorithm
    /// </summary>
    public class PathNode
    {
        public Vector3 Position { get; set; }
        public float G { get; set; } // Cost from start
        public float H { get; set; } // Heuristic cost to end
        public float F => G + H; // Total cost
        public PathNode? Parent { get; set; }

        public PathNode(Vector3 position, float g, float h)
        {
            Position = position;
            G = g;
            H = h;
        }
    }
}