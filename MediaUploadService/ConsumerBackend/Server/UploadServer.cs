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
        private TcpListener _listener;
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
                _listener.Stop();
            }
        }

        public void Stop()
        {
            if (_isRunning)
            {
                _isRunning = false;
                _listener.Stop();
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
                    
                    // Read metadata size (4 bytes)
                    byte[] sizeBuffer = new byte[4];
                    await ReadExactlyAsync(stream, sizeBuffer, 0, 4, cancellationToken);
                    Array.Reverse(sizeBuffer); // Reverse in-place to handle big-endian
                    int metadataSize = BitConverter.ToInt32(sizeBuffer, 0); // Convert from big-endian
                    
                    // Read metadata
                    byte[] metadataBuffer = new byte[metadataSize];
                    await ReadExactlyAsync(stream, metadataBuffer, 0, metadataSize, cancellationToken);
                    string metadataJson = Encoding.UTF8.GetString(metadataBuffer);
                    var metadata = JsonSerializer.Deserialize<Dictionary<string, object>>(metadataJson);
                    
                    // Check if queue is full
                    if (_uploadQueue.IsFull)
                    {
                        await SendResponseAsync(stream, new { status = "error", message = "Queue full, try again later" });
                        Console.WriteLine($"Rejected upload from {client.Client.RemoteEndPoint} - queue full");
                        return;
                    }
                    
                    // Check for duplicates
                    if (metadata.TryGetValue("hash", out var hash) && _metadataManager.IsDuplicate(hash.ToString()))
                    {
                        await SendResponseAsync(stream, new { status = "error", message = "Duplicate video detected" });
                        Console.WriteLine($"Rejected duplicate upload from {client.Client.RemoteEndPoint}");
                        return;
                    }
                    
                    // Accept upload - generate ID and send acknowledgment
                    string videoId = _metadataManager.GenerateId();
                    await SendResponseAsync(stream, new { status = "ok", message = "Ready to receive video", video_id = videoId });
                    
                    // Get video size
                    long videoSize = 0;
                    if (metadata.TryGetValue("size", out var size) && size is System.Text.Json.JsonElement sizeElement)
                    {
                        videoSize = sizeElement.GetInt64();
                    }
                    
                    // Prepare temp file path
                    string tempPath = Path.Combine(Path.GetTempPath(), $"temp_{videoId}");
                    
                    // Receive video data
                    using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
                    {
                        byte[] buffer = new byte[8192];
                        long remaining = videoSize;
                        
                        while (remaining > 0)
                        {
                            int bytesToRead = (int)Math.Min(buffer.Length, remaining);
                            int bytesRead = await stream.ReadAsync(buffer, 0, bytesToRead, cancellationToken);
                            
                            if (bytesRead == 0)
                                throw new Exception("Connection closed during video transfer");
                                
                            await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                            remaining -= bytesRead;
                        }
                    }
                    
                    // Create upload item
                    string fileExtension = ".mp4"; // Default
                    if (metadata.TryGetValue("filename", out var filename))
                    {
                        fileExtension = Path.GetExtension(filename.ToString());
                    }
                    
                    var uploadItem = new VideoUploadItem
                    {
                        VideoId = videoId,
                        TempPath = tempPath,
                        FinalPath = Path.Combine(_metadataManager.StoragePath, $"{videoId}{fileExtension}"),
                        Metadata = metadata,
                        ClientAddress = client.Client.RemoteEndPoint.ToString(),
                        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                    };
                    
                    // Add to queue
                    bool added = _uploadQueue.TryAddToQueue(uploadItem);
                    if (!added)
                    {
                        // Queue became full after our check
                        File.Delete(tempPath);
                        await SendResponseAsync(stream, new { status = "error", message = "Queue full, try again later" });
                        return;
                    }
                    
                    Console.WriteLine($"Queued video upload from {client.Client.RemoteEndPoint}, ID: {videoId}");
                    
                    // Send completion acknowledgment
                    await SendResponseAsync(stream, new { status = "ok", message = "Upload complete", video_id = videoId });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error handling client {client.Client.RemoteEndPoint}: {ex.Message}");
                    try
                    {
                        await SendResponseAsync(client.GetStream(), new { status = "error", message = ex.Message });
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