# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 项目概述

文件迁移工具 - WPF 桌面应用程序，用于对比和迁移两个大目录的文件。

**功能**：
- 以原目录文件为基准
- 对比目标目录是否有同名同 Hash 的文件
- 如果目标目录已存在该文件 → 删除原目录文件
- 如果目标目录不存在 → 移动原目录文件到目标目录
- 生成 CSV 迁移报告
- 支持断点续传（SQLite 持久化扫描结果）

## 技术栈

- **框架**: WPF + .NET 6.0
- **MVVM**: CommunityToolkit.Mvvm 8.x
- **数据库**: Microsoft.Data.Sqlite (断点续传)

## 项目结构

```
file-sync/
├── file-sync.csproj
├── App.xaml / App.xaml.cs
├── MainWindow.xaml / MainWindow.xaml.cs          # 主界面
├── ViewModels/
│   └── MainViewModel.cs                          # MVVM 视图模型
├── Models/
│   └── FileEntry.cs                              # 数据模型
├── Services/
│   ├── FileScanner.cs                            # 目录扫描（并行）
│   ├── HashCalculator.cs                         # Hash 计算（流式 MD5）
│   ├── FileComparator.cs                         # 文件对比
│   ├── FileMigrator.cs                           # 删除/移动操作
│   ├── CsvReportGenerator.cs                     # CSV 报告生成
│   └── AppState.cs                               # SQLite 状态持久化
```

## 常用命令

```bash
# 构建
dotnet build

# 运行
dotnet run

# 发布
dotnet publish -c Release -r win-x64 -o ../publish
```

## 核心流程

1. **扫描**: `FileScanner.ScanAsync()` 并行遍历目录，返回 `List<FileEntry>`
2. **对比**: `FileComparator.CompareAsync()` 先按文件名分组，再计算 Hash 精确比对
3. **迁移**: `FileMigrator.MigrateAsync()` 执行删除/移动操作
4. **报告**: `CsvReportGenerator.GenerateCsvAsync()` 生成 CSV 报告

## 性能优化

- `Directory.EnumerateFiles` 流式遍历（非 `GetFiles`）
- `ConcurrentBag` 并发收集扫描结果
- MD5 流式计算（1MB buffer）
- SQLite 持久化支持断点续传
