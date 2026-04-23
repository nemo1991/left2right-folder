using System;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using file_sync.Models;

namespace file_sync.Services;

/// <summary>
/// 应用状态持久化接口（支持断点续传）
/// </summary>
public interface IAppState
{
    Task InitializeAsync();
    Task SaveScanSessionAsync(string sessionId, string sourceDir, string targetDir);
    Task SaveScannedFilesAsync(string sessionId, System.Collections.Generic.List<FileEntry> files);
    Task<ScanSession?> GetLastIncompleteSessionAsync();
    Task MarkSessionCompletedAsync(string sessionId);
    void Dispose();
}

public record ScanSession(
    string Id,
    string SourceDir,
    string TargetDir,
    DateTime CreatedAt,
    string Status
);

/// <summary>
/// 应用状态持久化 - 使用 SQLite 存储
/// </summary>
public class AppState : IAppState
{
    private SqliteConnection? _connection;
    private readonly string _dbPath;

    public AppState()
    {
        _dbPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appdata.db");
    }

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection($"Data Source={_dbPath}");
        await _connection.OpenAsync();

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS ScanSessions (
                Id TEXT PRIMARY KEY,
                SourceDir TEXT NOT NULL,
                TargetDir TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                Status TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS ScannedFiles (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SessionId TEXT NOT NULL,
                FullPath TEXT NOT NULL,
                FileName TEXT NOT NULL,
                FileSize INTEGER NOT NULL,
                Hash TEXT,
                Status TEXT NOT NULL,
                FOREIGN KEY (SessionId) REFERENCES ScanSessions(Id)
            );

            CREATE INDEX IF NOT EXISTS IX_ScannedFiles_SessionId ON ScannedFiles(SessionId);
        ";
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task SaveScanSessionAsync(string sessionId, string sourceDir, string targetDir)
    {
        if (_connection == null) throw new InvalidOperationException("Database not initialized");

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            INSERT OR REPLACE INTO ScanSessions (Id, SourceDir, TargetDir, CreatedAt, Status)
            VALUES ($sessionId, $sourceDir, $targetDir, datetime('now'), 'Active')
        ";
        cmd.Parameters.AddWithValue("$sessionId", sessionId);
        cmd.Parameters.AddWithValue("$sourceDir", sourceDir);
        cmd.Parameters.AddWithValue("$targetDir", targetDir);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task SaveScannedFilesAsync(string sessionId, System.Collections.Generic.List<FileEntry> files)
    {
        if (_connection == null) throw new InvalidOperationException("Database not initialized");

        using var transaction = _connection.BeginTransaction();
        try
        {
            foreach (var file in files)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO ScannedFiles (SessionId, FullPath, FileName, FileSize, Hash, Status)
                    VALUES ($sessionId, $fullPath, $fileName, $fileSize, $hash, $status)
                ";
                cmd.Parameters.AddWithValue("$sessionId", sessionId);
                cmd.Parameters.AddWithValue("$fullPath", file.FullPath);
                cmd.Parameters.AddWithValue("$fileName", file.FileName);
                cmd.Parameters.AddWithValue("$fileSize", file.FileSize);
                cmd.Parameters.AddWithValue("$hash", (object?)file.Hash ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$status", file.Status.ToString());
                await cmd.ExecuteNonQueryAsync();
            }
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<ScanSession?> GetLastIncompleteSessionAsync()
    {
        if (_connection == null) throw new InvalidOperationException("Database not initialized");

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, SourceDir, TargetDir, CreatedAt, Status
            FROM ScanSessions
            WHERE Status = 'Active'
            ORDER BY CreatedAt DESC
            LIMIT 1
        ";

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new ScanSession(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetDateTime(3),
                reader.GetString(4)
            );
        }

        return null;
    }

    public async Task MarkSessionCompletedAsync(string sessionId)
    {
        if (_connection == null) throw new InvalidOperationException("Database not initialized");

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            UPDATE ScanSessions SET Status = 'Completed' WHERE Id = $sessionId
        ";
        cmd.Parameters.AddWithValue("$sessionId", sessionId);
        await cmd.ExecuteNonQueryAsync();
    }

    public void Dispose()
    {
        _connection?.Close();
        _connection?.Dispose();
    }
}
