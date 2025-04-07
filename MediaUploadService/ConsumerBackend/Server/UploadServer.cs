using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaUploadService.ConsumerBackend.Models;
using MediaUploadService.ConsumerBackend.Storage;

namespace MediaUploadService.ConsumerBackend.Server
{
    public class UploadServer
    {
        private readonly UploadQueue _uploadQueue;
        private readonly VideoMetadataManager _metadataManager;
        private readonly int _port;
        private TcpListener? _listener;
        private bool _isRunning;
        private readonly List<Task> _clientTasks = new List<Task>();

        public UploadServer(UploadQueue uploadQueue, VideoMetadataManager metadataManager, int port)
        {
            _uploadQueue = uploadQueue;
            _metadataManager = metadataManager;
            _port = port;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start();
            _isRunning = true;

            Console.WriteLine($"Upload server started on port {_port}");

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync();
                    Console.WriteLine($"Client connected: {client.Client.RemoteEndPoint}");
                    
                    var clientTask = HandleClientAsync(client, cancellationToken);
                    _clientTasks.Add(clientTask);
                    
                    // Clean up completed tasks
                    _clientTasks.RemoveAll(t => t.IsCompleted);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.Interrupted)
            {
                // Socket closed during Accept
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Server error: {ex.Message}");
            }
            finally
            {
                _isRunning = false;
                _listener?.Stop();
            }
        }

        public void Stop()
        {
            if (_isRunning)
            {
                _isRunning = false;
                _listener?.Stop();
            }
            
            // Wait for all client tasks to complete with timeout
            Task.WaitAll(_clientTasks.ToArray(), 5000);
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
        {
            using (client)
            {
                try
                {
                    var stream = client.GetStream();
                    var clientEndpoint = client.Client.RemoteEndPoint?.ToString() ?? "Unknown";
                    
                    // Read filename length (4 bytes)
                    byte[] filenameLengthBuffer = new byte[4];
                    await ReadExactlyAsync(stream, filenameLengthBuffer, 0, 4, cancellationToken);
                    int filenameLength = BitConverter.ToInt32(filenameLengthBuffer, 0);
                    
                    // Read filename
                    byte[] filenameBuffer = new byte[filenameLength];
                    await ReadExactlyAsync(stream, filenameBuffer, 0, filenameLength, cancellationToken);
                    string filename = Encoding.UTF8.GetString(filenameBuffer);
                    
                    // Check if queue is full
                    if (_uploadQueue.IsFull)
                    {
                        Console.WriteLine($"Rejected upload from {clientEndpoint} - queue full");
                        // Send 0 for error
                        await stream.WriteAsync(new byte[] { 0 }, 0, 1, cancellationToken);
                        return;
                    }
                    
                    // Generate ID and prepare paths
                    string videoId = _metadataManager.GenerateId();
                    string fileExtension = Path.GetExtension(filename);
                    string tempPath = Path.Combine(Path.GetTempPath(), $"temp_{videoId}");
                    
                    // Create basic metadata from filename
                    var metadata = new Dictionary<string, object>
                    {
                        { "filename", filename },
                        { "timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds() }
                    };
                    
                    // Receive file data directly to disk - READ A SPECIFIC NUMBER OF BYTES
                    long totalBytesRead = 0;
                    using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
                    {
                        byte[] buffer = new byte[8192];
                        int bytesRead;
                        
                        // Use a timeout to prevent hanging indefinitely
                        client.ReceiveTimeout = 30000; // 30 seconds
                        
                        while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                            totalBytesRead += bytesRead;
                            
                            // Send acknowledgment early if we have the full file
                            // This is just a safeguard for large files - we'll still send it again later
                            if (totalBytesRead > 1024 * 1024 && !stream.DataAvailable)
                            {
                                await stream.WriteAsync(new byte[] { 1 }, 0, 1, cancellationToken);
                                break;
                            }
                        }
                    }
                    
                    // Create upload item
                    var uploadItem = new VideoUploadItem
                    {
                        VideoId = videoId,
                        TempPath = tempPath,
                        FinalPath = Path.Combine(_metadataManager.StoragePath, $"{videoId}{fileExtension}"),
                        Metadata = metadata,
                        ClientAddress = clientEndpoint,
                        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                    };
                    
                    // Add to queue
                    bool added = _uploadQueue.TryAddToQueue(uploadItem);
                    if (!added)
                    {
                        // Queue became full after our check
                        File.Delete(tempPath);
                        await stream.WriteAsync(new byte[] { 0 }, 0, 1, cancellationToken);
                        return;
                    }
                    
                    Console.WriteLine($"Queued video upload from {clientEndpoint}, ID: {videoId}, Size: {totalBytesRead} bytes");
                    
                    // Send acknowledgment
                    await stream.WriteAsync(new byte[] { 1 }, 0, 1, cancellationToken);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error handling client {client.Client.RemoteEndPoint}: {ex.Message}");
                    try
                    {
                        // Send 0 for error
                        await client.GetStream().WriteAsync(new byte[] { 0 }, 0, 1, cancellationToken);
                    }
                    catch
                    {
                        // Ignore send errors during cleanup
                    }
                }
            }
        }

        private async Task SendResponseAsync(NetworkStream stream, object response)
        {
            string json = JsonSerializer.Serialize(response);
            byte[] data = Encoding.UTF8.GetBytes(json);
            await stream.WriteAsync(data, 0, data.Length);
        }

        private async Task ReadExactlyAsync(NetworkStream stream, byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            int totalBytesRead = 0;
            while (totalBytesRead < count)
            {
                int bytesRead = await stream.ReadAsync(buffer, offset + totalBytesRead, count - totalBytesRead, cancellationToken);
                if (bytesRead == 0)
                    throw new EndOfStreamException("Connection closed prematurely");
                totalBytesRead += bytesRead;
            }
        }
    }
}