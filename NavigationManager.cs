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
        private readonly PathfindingEngine _pathfinding;
        private readonly MovementController _movement;
        private bool _disposed = false;

        private Vector3? _currentTarget;
        private Queue<Vector3> _currentPath = new();
        private bool _isNavigating = false;
        private CancellationTokenSource? _navigationCancellation;

        public event Action<Vector3>? PathStarted;
        public event Action<Vector3>? PathCompleted;
        public event Action? PathFailed;
        public event Action<Vector3>? WaypointReached;

        public bool IsNavigating => _isNavigating;
        public Vector3? CurrentTarget => _currentTarget;
        public int RemainingWaypoints => _currentPath.Count;

        public NavigationManager(WorldManager world, ILogger<NavigationManager> logger)
        {
            _world = world;
            _logger = logger;
            _pathfinding = new PathfindingEngine(world, logger);
            _movement = new MovementController(logger);
        }

        public async Task InitializeAsync()
        {
            _logger.LogInformation("Initializing navigation manager");
            await Task.CompletedTask;
        }

        public async Task<bool> GoToAsync(Vector3 destination, float tolerance = 0.5f)
        {
            try
            {
                _logger.LogInformation($"Navigating to {destination}");
                
                // Cancel any existing navigation
                await StopNavigationAsync();

                var playerPos = GetPlayerPosition();
                if (playerPos == null)
                {
                    _logger.LogWarning("Cannot navigate: player position unknown");
                    return false;
                }

                // Check if already at destination
                if (Vector3.Distance(playerPos.Value, destination) <= tolerance)
                {
                    _logger.LogDebug("Already at destination");
                    return true;
                }

                // Find path
                var path = await _pathfinding.FindPathAsync(playerPos.Value, destination);
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
                    // Get entity position (this would need entity manager reference)
                    // For now, simulate with a basic approach
                    
                    await Task.Delay(500, _navigationCancellation.Token); // Update every 500ms
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
                var playerPos = GetPlayerPosition();
                
                if (playerPos == null)
                {
                    _logger.LogWarning("Lost player position during navigation");
                    return false;
                }

                // Move to waypoint
                var success = await _movement.MoveToAsync(waypoint, tolerance, cancellationToken);
                
                if (!success)
                {
                    _logger.LogWarning($"Failed to reach waypoint {waypoint}");
                    
                    // Try to recalculate path
                    var newPath = await _pathfinding.FindPathAsync(playerPos.Value, _currentTarget!.Value);
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

        private Vector3? GetPlayerPosition()
        {
            // This would normally get position from entity manager
            // For now, return a placeholder
            return Vector3.Zero;
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
        private bool _disposed = false;

        public PathfindingEngine(WorldManager world, ILogger logger)
        {
            _world = world;
            _logger = logger;
            _cacheInvalidation = new ConcurrentDictionary<ChunkCoordinate, DateTime>();
        }

        public async Task<List<Vector3>?> FindPathAsync(Vector3 start, Vector3 end, int maxNodes = 10000)
        {
            return await Task.Run(() => FindPath(start, end, maxNodes));
        }

        public List<Vector3>? FindPath(Vector3 start, Vector3 end, int maxNodes = 10000)
        {
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
            
            // Basic 26-directional movement (including diagonals and vertical)
            for (int x = -1; x <= 1; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    for (int z = -1; z <= 1; z++)
                    {
                        if (x == 0 && y == 0 && z == 0) continue;
                        
                        var neighbor = position + new Vector3(x, y, z);
                        neighbors.Add(neighbor);
                    }
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
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _cacheInvalidation.Clear();
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Controls actual player movement
    /// </summary>
    public class MovementController : IDisposable
    {
        private readonly ILogger _logger;
        private bool _disposed = false;

        public MovementController(ILogger logger)
        {
            _logger = logger;
        }

        public async Task<bool> MoveToAsync(Vector3 target, float tolerance, CancellationToken cancellationToken)
        {
            try
            {
                // Simulate movement - in real implementation this would send movement packets
                var steps = 10;
                for (int i = 0; i < steps && !cancellationToken.IsCancellationRequested; i++)
                {
                    await Task.Delay(100, cancellationToken);
                    
                    // Check if reached target
                    var currentPos = GetCurrentPosition();
                    if (Vector3.Distance(currentPos, target) <= tolerance)
                    {
                        return true;
                    }
                }

                return true; // Simulate success
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        public async Task StopAsync()
        {
            // Stop all movement
            await Task.CompletedTask;
        }

        private Vector3 GetCurrentPosition()
        {
            // Get current player position - placeholder
            return Vector3.Zero;
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