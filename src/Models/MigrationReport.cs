using file_sync.Services;
using System;

namespace file_sync.Models;

/// <summary>
/// 迁移报告
/// </summary>
public record MigrationReport(
    DateTime StartTime,
    DateTime EndTime,
    string SourceDirectory,
    string TargetDirectory,
    ILeft2Right Left2Right
);


