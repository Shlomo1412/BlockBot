using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace BlockBot
{
    /// <summary>
    /// Manages chat functionality and command processing
    /// </summary>
    public class ChatManager : IDisposable
    {
        private readonly MinecraftClient _client;
        private readonly ILogger<ChatManager> _logger;
        private readonly Dictionary<string, Func<string[], Task<bool>>> _commands;
        private readonly List<ChatFilter> _filters;
        private bool _disposed = false;

        public event Action<ChatMessage>? MessageReceived;
        public event Action<string, string>? PlayerMessage;
        public event Action<string>? SystemMessage;
        public event Action<string, string>? CommandExecuted;

        public bool AutoRespond { get; set; } = false;
        public Dictionary<string, string> AutoResponses { get; set; } = new();
        public bool LogChat { get; set; } = true;

        public ChatManager(MinecraftClient client, ILogger<ChatManager> logger)
        {
            _client = client;
            _logger = logger;
            _commands = new Dictionary<string, Func<string[], Task<bool>>>();
            _filters = new List<ChatFilter>();
            
            InitializeDefaultCommands();
            InitializeDefaultFilters();
        }

        public async Task HandlePacketAsync(Packet packet)
        {
            if (packet is ChatPacket chatPacket)
            {
                await ProcessChatMessageAsync(chatPacket);
            }
        }

        public async Task SendMessageAsync(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;

            try
            {
                // Truncate message if too long
                if (message.Length > 256)
                {
                    message = message.Substring(0, 256);
                    _logger.LogWarning("Message truncated to 256 characters");
                }

                var chatPacket = new ChatPacket
                {
                    Message = message,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };

                await _client.SendPacketAsync(chatPacket);
                
                if (LogChat)
                {
                    _logger.LogInformation($"[SENT] {message}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send chat message");
            }
        }

        public async Task SendPrivateMessageAsync(string recipient, string message)
        {
            await SendMessageAsync($"/msg {recipient} {message}");
        }

        public async Task SendCommandAsync(string command, params string[] args)
        {
            var fullCommand = args.Length > 0 ? $"/{command} {string.Join(" ", args)}" : $"/{command}";
            await SendMessageAsync(fullCommand);
        }

        private async Task ProcessChatMessageAsync(ChatPacket packet)
        {
            try
            {
                var chatMessage = ParseChatMessage(packet.Message);
                
                if (LogChat)
                {
                    _logger.LogInformation($"[CHAT] {chatMessage}");
                }

                // Apply filters
                foreach (var filter in _filters)
                {
                    if (filter.ShouldFilter(chatMessage))
                    {
                        _logger.LogDebug($"Message filtered by {filter.Name}");
                        return;
                    }
                }

                MessageReceived?.Invoke(chatMessage);

                // Handle different message types
                if (chatMessage.Type == ChatMessageType.PlayerMessage)
                {
                    PlayerMessage?.Invoke(chatMessage.Username!, chatMessage.Content);
                    await HandlePlayerMessageAsync(chatMessage);
                }
                else if (chatMessage.Type == ChatMessageType.System)
                {
                    SystemMessage?.Invoke(chatMessage.Content);
                }

                // Process commands if directed at bot
                if (chatMessage.Type == ChatMessageType.PlayerMessage && 
                    chatMessage.Content.StartsWith("!"))
                {
                    await ProcessCommandAsync(chatMessage);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing chat message");
            }
        }

        private ChatMessage ParseChatMessage(string rawMessage)
        {
            // Parse different chat message formats
            
            // Player message: <username> message
            var playerMessageMatch = Regex.Match(rawMessage, @"^<([^>]+)>\s*(.*)$");
            if (playerMessageMatch.Success)
            {
                return new ChatMessage
                {
                    Type = ChatMessageType.PlayerMessage,
                    Username = playerMessageMatch.Groups[1].Value,
                    Content = playerMessageMatch.Groups[2].Value,
                    Timestamp = DateTime.UtcNow,
                    Raw = rawMessage
                };
            }

            // Private message: username whispers to you: message
            var whisperMatch = Regex.Match(rawMessage, @"^([^\s]+)\s+whispers\s+to\s+you:\s*(.*)$");
            if (whisperMatch.Success)
            {
                return new ChatMessage
                {
                    Type = ChatMessageType.PrivateMessage,
                    Username = whisperMatch.Groups[1].Value,
                    Content = whisperMatch.Groups[2].Value,
                    Timestamp = DateTime.UtcNow,
                    Raw = rawMessage
                };
            }

            // Death message
            if (rawMessage.Contains("was slain") || rawMessage.Contains("died") || 
                rawMessage.Contains("fell") || rawMessage.Contains("drowned"))
            {
                return new ChatMessage
                {
                    Type = ChatMessageType.Death,
                    Content = rawMessage,
                    Timestamp = DateTime.UtcNow,
                    Raw = rawMessage
                };
            }

            // Join/leave messages
            if (rawMessage.Contains("joined the game") || rawMessage.Contains("left the game"))
            {
                return new ChatMessage
                {
                    Type = ChatMessageType.JoinLeave,
                    Content = rawMessage,
                    Timestamp = DateTime.UtcNow,
                    Raw = rawMessage
                };
            }

            // Default to system message
            return new ChatMessage
            {
                Type = ChatMessageType.System,
                Content = rawMessage,
                Timestamp = DateTime.UtcNow,
                Raw = rawMessage
            };
        }

        private async Task HandlePlayerMessageAsync(ChatMessage message)
        {
            if (AutoRespond && AutoResponses.ContainsKey(message.Content.ToLower()))
            {
                var response = AutoResponses[message.Content.ToLower()];
                await Task.Delay(1000); // Slight delay to seem more natural
                await SendMessageAsync(response);
            }

            // Respond to mentions
            if (message.Content.Contains(_client.Username))
            {
                await HandleMentionAsync(message);
            }
        }

        private async Task HandleMentionAsync(ChatMessage message)
        {
            // Basic responses to mentions
            var responses = new[]
            {
                "Hello!",
                "Yes?",
                "How can I help?",
                "I'm here!"
            };

            var random = new Random();
            var response = responses[random.Next(responses.Length)];
            
            await Task.Delay(500);
            await SendMessageAsync(response);
        }

        private async Task ProcessCommandAsync(ChatMessage message)
        {
            try
            {
                var content = message.Content.Substring(1); // Remove !
                var parts = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                
                if (parts.Length == 0) return;

                var command = parts[0].ToLower();
                var args = parts.Skip(1).ToArray();

                if (_commands.TryGetValue(command, out var commandHandler))
                {
                    var success = await commandHandler(args);
                    CommandExecuted?.Invoke(command, success ? "Success" : "Failed");
                    
                    _logger.LogInformation($"Command '{command}' executed by {message.Username} - {(success ? "Success" : "Failed")}");
                }
                else
                {
                    await SendMessageAsync($"Unknown command: {command}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing command");
                await SendMessageAsync("Error processing command");
            }
        }

        public void RegisterCommand(string command, Func<string[], Task<bool>> handler)
        {
            _commands[command.ToLower()] = handler;
            _logger.LogDebug($"Registered command: {command}");
        }

        public void AddChatFilter(ChatFilter filter)
        {
            _filters.Add(filter);
        }

        public void AddAutoResponse(string trigger, string response)
        {
            AutoResponses[trigger.ToLower()] = response;
        }

        private void InitializeDefaultCommands()
        {
            RegisterCommand("help", async (args) =>
            {
                await SendMessageAsync("Available commands: " + string.Join(", ", _commands.Keys));
                return true;
            });

            RegisterCommand("status", async (args) =>
            {
                await SendMessageAsync("Bot is online and operational!");
                return true;
            });

            RegisterCommand("ping", async (args) =>
            {
                await SendMessageAsync("Pong!");
                return true;
            });

            RegisterCommand("time", async (args) =>
            {
                await SendMessageAsync($"Current time: {DateTime.Now:HH:mm:ss}");
                return true;
            });
        }

        private void InitializeDefaultFilters()
        {
            // Filter spam messages
            AddChatFilter(new ChatFilter("spam", message => 
                message.Content.Length > 100 && 
                message.Content.Count(c => c == '!') > 5));

            // Filter repeated messages (simple)
            var lastMessages = new Queue<string>();
            AddChatFilter(new ChatFilter("repeat", message =>
            {
                if (lastMessages.Contains(message.Content))
                    return true;

                lastMessages.Enqueue(message.Content);
                if (lastMessages.Count > 10)
                    lastMessages.Dequeue();

                return false;
            }));
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _commands.Clear();
                _filters.Clear();
                AutoResponses.Clear();
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Represents a chat message
    /// </summary>
    public class ChatMessage
    {
        public ChatMessageType Type { get; set; }
        public string? Username { get; set; }
        public string Content { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string Raw { get; set; } = string.Empty;

        public override string ToString()
        {
            return Type switch
            {
                ChatMessageType.PlayerMessage => $"<{Username}> {Content}",
                ChatMessageType.PrivateMessage => $"{Username} -> {Content}",
                _ => Content
            };
        }
    }

    /// <summary>
    /// Chat message types
    /// </summary>
    public enum ChatMessageType
    {
        PlayerMessage,
        PrivateMessage,
        System,
        Death,
        JoinLeave,
        Server,
        Unknown
    }

    /// <summary>
    /// Chat filter for filtering unwanted messages
    /// </summary>
    public class ChatFilter
    {
        public string Name { get; set; }
        public Func<ChatMessage, bool> FilterFunction { get; set; }

        public ChatFilter(string name, Func<ChatMessage, bool> filterFunction)
        {
            Name = name;
            FilterFunction = filterFunction;
        }

        public bool ShouldFilter(ChatMessage message)
        {
            return FilterFunction(message);
        }
    }
}