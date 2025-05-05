# Folder Synchronization Program

A C# console application that synchronizes two folders: source and replica. The program maintains a full, identical copy of the source folder at the replica folder location.

## Features

- One-way synchronization: After synchronization, the content of the replica folder exactly matches the content of the source folder
- Periodic synchronization at configurable intervals
- File system monitoring for immediate detection of changes
- Detailed logging of all file operations (creation, copying, removal) to both a log file and console output
- Command-line configuration of folder paths, synchronization interval, and log file path
- No third-party folder synchronization libraries used
- Uses built-in MD5 hashing for file comparison

## Usage

```
FolderSynchronizer <source_folder> <replica_folder> <log_file_path> [sync_interval_seconds]
```

### Parameters

- `source_folder`: Path to the source folder to be synchronized
- `replica_folder`: Path to the replica folder where content will be copied
- `log_file_path`: Path to the log file
- `sync_interval_seconds`: (Optional) Interval between periodic synchronizations in seconds. Default: 60 seconds

### Example

```
FolderSynchronizer C:\source D:\replica C:\logs\sync.log 300
```

This will synchronize the contents of `C:\source` to `D:\replica` every 5 minutes (300 seconds) and log all operations to `C:\logs\sync.log`.

## How It Works

1. When started, the program performs an initial synchronization
2. It sets up a FileSystemWatcher to monitor changes in the source folder
3. It sets up a timer to perform periodic synchronization
4. For each synchronization:
   - It copies new and modified files from source to replica
   - It removes files from replica that don't exist in source
   - It creates directories in replica that exist in source
   - It removes empty directories from replica
5. All operations are logged to both console and the specified log file

## Requirements

- .NET Framework 4.5 or above
- Windows operating system

## Building the Project

1. Clone the repository
2. Open the solution in Visual Studio
3. Build the solution
4. Run the executable with appropriate command-line arguments
