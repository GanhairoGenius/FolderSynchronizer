using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FolderSynchronizer
{
    class Program
    {
        private static string _sourceFolder;
        private static string _replicaFolder;
        private static string _logFilePath;
        private static int _syncIntervalInSeconds;
        private static FileSystemWatcher _watcher;
        private static readonly object _syncLock = new object();
        private static readonly List<string> _pendingChanges = new List<string>();
        private static Timer _syncTimer;
        private static StreamWriter _logWriter;

        static void Main(string[] args)
        {
            try
            {
                if (!ParseCommandLineArguments(args))
                {
                    return;
                }

                // Setup logging
                _logWriter = new StreamWriter(_logFilePath, true)
                {
                    AutoFlush = true
                };

                // Initial synchronization
                LogMessage("Initial synchronization started");
                SynchronizeFolders();
                LogMessage("Initial synchronization completed");

                // Setup file system watcher for source folder
                SetupFileSystemWatcher();

                // Setup periodic synchronization
                _syncTimer = new Timer(SyncTimerCallback, null,
                    _syncIntervalInSeconds * 1000,
                    _syncIntervalInSeconds * 1000);

                Console.WriteLine($"Synchronization service started. Press Ctrl+C to exit.");
                Console.WriteLine($"Source folder: {_sourceFolder}");
                Console.WriteLine($"Replica folder: {_replicaFolder}");
                Console.WriteLine($"Log file: {_logFilePath}");
                Console.WriteLine($"Sync interval: {_syncIntervalInSeconds} seconds");

                // Keep the application running until user presses Ctrl+C
                Console.CancelKeyPress += (sender, e) =>
                {
                    LogMessage("Synchronization service stopping");
                    _syncTimer.Dispose();
                    _watcher.Dispose();
                    _logWriter.Close();
                    LogMessage("Synchronization service stopped");
                };

                // Wait indefinitely
                Thread.Sleep(Timeout.Infinite);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                LogMessage($"Error: {ex.Message}");
            }
        }

        private static bool ParseCommandLineArguments(string[] args)
        {
            if (args.Length < 3)
            {
                Console.WriteLine("Usage: FolderSynchronizer <source_folder> <replica_folder> <log_file_path> [sync_interval_seconds]");
                Console.WriteLine("Example: FolderSynchronizer C:\\source D:\\replica C:\\logs\\sync.log 60");
                return false;
            }

            _sourceFolder = args[0];
            _replicaFolder = args[1];
            _logFilePath = args[2];
            _syncIntervalInSeconds = args.Length > 3 && int.TryParse(args[3], out int interval)
                ? interval
                : 60; // Default to 60 seconds if not specified

            // Validate folder paths
            if (!Directory.Exists(_sourceFolder))
            {
                Console.WriteLine($"Error: Source folder '{_sourceFolder}' does not exist.");
                return false;
            }

            // Create replica folder if it doesn't exist
            if (!Directory.Exists(_replicaFolder))
            {
                Directory.CreateDirectory(_replicaFolder);
                Console.WriteLine($"Created replica folder: {_replicaFolder}");
            }

            // Validate log file path
            try
            {
                var logDirectory = Path.GetDirectoryName(_logFilePath);
                if (!string.IsNullOrEmpty(logDirectory) && !Directory.Exists(logDirectory))
                {
                    Directory.CreateDirectory(logDirectory);
                    Console.WriteLine($"Created log directory: {logDirectory}");
                }

                // Test if we can write to the log file
                using (var testWriter = new StreamWriter(_logFilePath, true))
                {
                    testWriter.WriteLine($"Log file initialized at {DateTime.Now}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error setting up log file: {ex.Message}");
                return false;
            }

            return true;
        }

        private static void SetupFileSystemWatcher()
        {
            _watcher = new FileSystemWatcher(_sourceFolder)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                               NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime
            };

            _watcher.Created += OnFileSystemChanged;
            _watcher.Changed += OnFileSystemChanged;
            _watcher.Deleted += OnFileSystemChanged;
            _watcher.Renamed += OnFileSystemRenamed;

            _watcher.EnableRaisingEvents = true;
        }

        private static void OnFileSystemChanged(object sender, FileSystemEventArgs e)
        {
            lock (_syncLock)
            {
                if (!_pendingChanges.Contains(e.FullPath))
                {
                    _pendingChanges.Add(e.FullPath);
                    LogMessage($"Detected {e.ChangeType} event for {e.FullPath}");
                }
            }
        }

        private static void OnFileSystemRenamed(object sender, RenamedEventArgs e)
        {
            lock (_syncLock)
            {
                if (!_pendingChanges.Contains(e.OldFullPath))
                {
                    _pendingChanges.Add(e.OldFullPath);
                    LogMessage($"Detected rename from {e.OldFullPath} to {e.FullPath}");
                }

                if (!_pendingChanges.Contains(e.FullPath))
                {
                    _pendingChanges.Add(e.FullPath);
                }
            }
        }

        private static void SyncTimerCallback(object state)
        {
            lock (_syncLock)
            {
                if (_pendingChanges.Count > 0)
                {
                    LogMessage("Periodic synchronization started due to detected changes");
                    SynchronizeFolders();
                    _pendingChanges.Clear();
                    LogMessage("Periodic synchronization completed");
                }
                else
                {
                    // Still perform a full sync to catch any changes that might have been missed
                    LogMessage("Scheduled periodic synchronization started");
                    SynchronizeFolders();
                    LogMessage("Scheduled periodic synchronization completed");
                }
            }
        }

        private static void SynchronizeFolders()
        {
            try
            {
                // Get all files from source including subdirectories
                var sourceFiles = Directory.GetFiles(_sourceFolder, "*", SearchOption.AllDirectories)
                    .Select(f => new FileInfo(f))
                    .ToList();

                // Get all files from replica including subdirectories
                var replicaFiles = Directory.GetFiles(_replicaFolder, "*", SearchOption.AllDirectories)
                    .Select(f => new FileInfo(f))
                    .ToList();

                // Process each file in source
                foreach (var sourceFile in sourceFiles)
                {
                    string relativePath = sourceFile.FullName.Substring(_sourceFolder.Length).TrimStart(Path.DirectorySeparatorChar);
                    string replicaPath = Path.Combine(_replicaFolder, relativePath);

                    // Ensure directory exists in replica
                    string replicaDirectory = Path.GetDirectoryName(replicaPath);
                    if (!Directory.Exists(replicaDirectory))
                    {
                        Directory.CreateDirectory(replicaDirectory);
                        LogMessage($"Created directory: {replicaDirectory}");
                    }

                    // Check if file exists in replica
                    var replicaFile = replicaFiles.FirstOrDefault(f =>
                        f.FullName.Equals(replicaPath, StringComparison.OrdinalIgnoreCase));

                    if (replicaFile == null)
                    {
                        // File doesn't exist in replica, copy it
                        File.Copy(sourceFile.FullName, replicaPath);
                        LogMessage($"Copied file: {sourceFile.FullName} -> {replicaPath}");
                    }
                    else
                    {
                        // File exists, check if it's different
                        if (AreFilesDifferent(sourceFile.FullName, replicaPath))
                        {
                            File.Copy(sourceFile.FullName, replicaPath, true);
                            LogMessage($"Updated file: {sourceFile.FullName} -> {replicaPath}");
                        }

                        // Remove from replica list as it's been processed
                        replicaFiles.Remove(replicaFile);
                    }
                }

                // Any files left in replicaFiles don't exist in source and should be deleted
                foreach (var replicaFile in replicaFiles)
                {
                    replicaFile.Delete();
                    LogMessage($"Deleted file: {replicaFile.FullName}");
                }

                // Clean up empty directories in replica
                CleanEmptyDirectories(_replicaFolder);
            }
            catch (Exception ex)
            {
                LogMessage($"Error during synchronization: {ex.Message}");
                Console.WriteLine($"Error during synchronization: {ex.Message}");
            }
        }

        private static bool AreFilesDifferent(string file1, string file2)
        {
            // First check file size - if different, no need to compare content
            var fileInfo1 = new FileInfo(file1);
            var fileInfo2 = new FileInfo(file2);

            if (fileInfo1.Length != fileInfo2.Length)
                return true;

            // If same size, compare file hash
            using (var md5 = MD5.Create())
            {
                using (var stream1 = File.OpenRead(file1))
                using (var stream2 = File.OpenRead(file2))
                {
                    var hash1 = md5.ComputeHash(stream1);
                    var hash2 = md5.ComputeHash(stream2);

                    return !hash1.SequenceEqual(hash2);
                }
            }
        }

        private static void CleanEmptyDirectories(string directory)
        {
            foreach (var dir in Directory.GetDirectories(directory))
            {
                CleanEmptyDirectories(dir);

                if (Directory.GetFiles(dir).Length == 0 &&
                    Directory.GetDirectories(dir).Length == 0)
                {
                    Directory.Delete(dir);
                    LogMessage($"Deleted empty directory: {dir}");
                }
            }
        }

        private static void LogMessage(string message)
        {
            string logMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
            Console.WriteLine(logMessage);

            lock (_logWriter)
            {
                _logWriter.WriteLine(logMessage);
            }
        }
    }
}

