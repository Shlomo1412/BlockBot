using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;

namespace BlockBot
{
    /// <summary>
    /// Handles low-level Minecraft protocol communication
    /// </summary>
    public class MinecraftClient : IDisposable
    {
        private readonly ILogger<MinecraftClient> _logger;
        private TcpClient? _tcpClient;
        private NetworkStream? _stream;
        private bool _disposed = false;
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _receiveTask;

        public event Func<Packet, Task>? PacketReceived;
        public event Action? Disconnected;

        public bool IsConnected => _tcpClient?.Connected ?? false;
        public string Username { get; private set; } = string.Empty;

        public MinecraftClient(ILogger<MinecraftClient> logger)
        {
            _logger = logger;
        }

        public async Task<bool> ConnectAsync(string host, int port, string username, string? password = null)
        {
            try
            {
                _tcpClient = new TcpClient();
                await _tcpClient.ConnectAsync(host, port);
                _stream = _tcpClient.GetStream();
                Username = username;

                _cancellationTokenSource = new CancellationTokenSource();
                
                // Perform handshake
                await PerformHandshakeAsync(host, port, username);
                
                // Start receiving packets
                _receiveTask = ReceivePacketsAsync(_cancellationTokenSource.Token);
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to connect to server");
                return false;
            }
        }

        public async Task DisconnectAsync()
        {
            if (_cancellationTokenSource != null)
            {
                _cancellationTokenSource.Cancel();
            }

            if (_receiveTask != null)
            {
                await _receiveTask;
            }

            _stream?.Close();
            _tcpClient?.Close();
        }

        public async Task SendPacketAsync(Packet packet)
        {
            if (_stream == null || !IsConnected)
                return;

            try
            {
                var data = packet.Serialize();
                await _stream.WriteAsync(data);
                await _stream.FlushAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send packet");
            }
        }

        private async Task PerformHandshakeAsync(string host, int port, string username)
        {
            // Implement Minecraft protocol handshake
            // This is a simplified version - real implementation would be more complex
            
            var handshakePacket = new HandshakePacket
            {
                ProtocolVersion = 765, // 1.20.4
                ServerAddress = host,
                ServerPort = (ushort)port,
                NextState = 2 // Login
            };

            await SendPacketAsync(handshakePacket);

            var loginStartPacket = new LoginStartPacket
            {
                Username = username
            };

            await SendPacketAsync(loginStartPacket);
        }

        private async Task ReceivePacketsAsync(CancellationToken cancellationToken)
        {
            var buffer = new byte[4096];
            
            try
            {
                while (!cancellationToken.IsCancellationRequested && IsConnected)
                {
                    var bytesRead = await _stream!.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                    
                    if (bytesRead == 0)
                    {
                        _logger.LogWarning("Connection closed by server");
                        break;
                    }

                    // Parse packets from buffer
                    var packets = ParsePackets(buffer, bytesRead);
                    
                    foreach (var packet in packets)
                    {
                        if (PacketReceived != null)
                        {
                            await PacketReceived.Invoke(packet);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error receiving packets");
            }
            finally
            {
                Disconnected?.Invoke();
            }
        }

        private List<Packet> ParsePackets(byte[] buffer, int length)
        {
            var packets = new List<Packet>();
            var offset = 0;

            while (offset < length)
            {
                try
                {
                    // Read packet length (VarInt)
                    var packetLength = ReadVarInt(buffer, ref offset);
                    
                    if (offset + packetLength > length)
                        break; // Incomplete packet

                    // Read packet ID (VarInt)
                    var startOffset = offset;
                    var packetId = ReadVarInt(buffer, ref offset);
                    
                    // Read packet data
                    var dataLength = packetLength - (offset - startOffset);
                    var packetData = new byte[dataLength];
                    Array.Copy(buffer, offset, packetData, 0, dataLength);
                    offset += dataLength;

                    // Create appropriate packet type
                    var packet = PacketFactory.CreatePacket(packetId, packetData);
                    if (packet != null)
                    {
                        packets.Add(packet);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to parse packet");
                    break;
                }
            }

            return packets;
        }

        private int ReadVarInt(byte[] buffer, ref int offset)
        {
            int value = 0;
            int position = 0;
            byte currentByte;

            do
            {
                if (offset >= buffer.Length)
                    throw new InvalidOperationException("Unexpected end of buffer");

                currentByte = buffer[offset++];
                value |= (currentByte & 0x7F) << position;

                if ((currentByte & 0x80) == 0)
                    break;

                position += 7;

                if (position >= 32)
                    throw new InvalidOperationException("VarInt is too big");
            } while (true);

            return value;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                DisconnectAsync().Wait();
                _cancellationTokenSource?.Dispose();
                _stream?.Dispose();
                _tcpClient?.Dispose();
                _disposed = true;
            }
        }
    }
}