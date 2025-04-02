using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace MediaUploadService.ConsumerBackend.Storage
{
    public class VideoMetadataManager
    {
        private readonly string _metadataFile;
        private readonly Dictionary<string, Dictionary<string, object>> _videos;
        private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
        
        public string StoragePath => Path.GetDirectoryName(_metadataFile);

        public VideoMetadataManager(string metadataFile)
        {
            _metadataFile = metadataFile;
            _videos = new Dictionary<string, Dictionary<string, object>>();
            
            // Load existing metadata if file exists
            LoadMetadata();
            Console.WriteLine($"Metadata manager initialized with file: {metadataFile}");
        }

        private void LoadMetadata()
        {
            _lock.EnterWriteLock();
            try
            {
                if (File.Exists(_metadataFile))
                {
                    string json = File.ReadAllText(_metadataFile);
                    var videos = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, object>>>(json);
                    foreach (var video in videos)
                    {
                        _videos[video.Key] = video.Value;
                    }
                    Console.WriteLine($"Loaded metadata for {_videos.Count} videos");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading metadata: {ex.Message}");
                // Continue with empty dictionary
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        private bool SaveMetadata(bool alreadyHasLock = false)
        {
            if (!alreadyHasLock)
            {
                _lock.EnterWriteLock(); // Changed from ReadLock to WriteLog for consistency
            }
            
            try
            {
                // Ensure directory exists
                Directory.CreateDirectory(Path.GetDirectoryName(_metadataFile));
                
                string json = JsonSerializer.Serialize(_videos, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_metadataFile, json);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving metadata: {ex.Message}");
                return false;
            }
            finally
            {
                if (!alreadyHasLock)
                {
                    _lock.ExitWriteLock(); // Changed from ExitReadLock
                }
            }
        }

        public string GenerateId()
        {
            return Guid.NewGuid().ToString();
        }

        public long GetTimestamp()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        public void AddVideoMetadata(string videoId, Dictionary<string, object> metadata)
        {
            _lock.EnterWriteLock();
            try
            {
                if (metadata.ContainsKey("timestamp") == false)
                {
                    metadata["timestamp"] = GetTimestamp();
                }
                
                _videos[videoId] = metadata;
                SaveMetadata(true); // Pass true to indicate we already have a lock
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public Dictionary<string, object> GetVideoMetadata(string videoId)
        {
            _lock.EnterReadLock();
            try
            {
                if (_videos.TryGetValue(videoId, out var metadata))
                {
                    return new Dictionary<string, object>(metadata);
                }
                return null;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public Dictionary<string, Dictionary<string, object>> GetAllVideos()
        {
            _lock.EnterReadLock();
            try
            {
                var result = new Dictionary<string, Dictionary<string, object>>();
                foreach (var video in _videos)
                {
                    result[video.Key] = new Dictionary<string, object>(video.Value);
                }
                return result;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public bool DeleteVideoMetadata(string videoId)
        {
            _lock.EnterWriteLock();
            try
            {
                bool result = _videos.Remove(videoId);
                if (result)
                {
                    SaveMetadata();
                }
                return result;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public bool IsDuplicate(string videoHash)
        {
            _lock.EnterReadLock();
            try
            {
                foreach (var video in _videos.Values)
                {
                    if (video.TryGetValue("hash", out var hash) && hash.ToString() == videoHash)
                    {
                        return true;
                    }
                }
                return false;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }
    }
}