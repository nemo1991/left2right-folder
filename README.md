# 文件迁移工具

一个基于 WPF 的桌面应用程序，用于智能对比和迁移两个大目录的文件。

![软件截图](screenshot.png)

## 功能特性

- 📁 **智能对比** - 通过文件名 + MD5 Hash 精确判断文件是否重复
- 🗑️ **自动清理** - 目标目录已存在则删除原目录文件
- 📦 **自动迁移** - 目标目录不存在则移动到目标目录
- 📊 **迁移报告** - 生成详细的 CSV 格式报告
- 💾 **断点续传** - 支持中途关闭后恢复扫描进度
- ⚡ **高性能** - 并行扫描、流式 Hash 计算，支持百万级文件

## 系统要求

- Windows 10/11
- .NET 10.0 运行时（使用独立发布版本则无需安装）

## 下载安装

### 方式一：独立发布版（推荐）

无需安装 .NET 运行时，解压即用：

1. 从 [Releases](https://github.com/your-username/file-sync/releases) 下载最新版本
2. 解压到任意目录
3. 双击 `file-sync.exe` 运行

### 方式二：源码编译

```bash
# 克隆仓库
git clone https://github.com/your-username/file-sync.git
cd file-sync

# 构建
dotnet build

# 运行
dotnet run

# 发布独立可执行文件
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ../publish
```

## 使用说明

1. **选择目录** - 点击"浏览"按钮分别选择原目录和目标目录
2. **扫描对比** - 点击"扫描目录"按钮，程序会自动对比两个目录
3. **确认操作** - 查看扫描结果（待删除/待移动文件列表）
4. **执行迁移** - 点击"开始迁移"按钮执行操作
5. **查看报告** - 迁移完成后自动生成 CSV 报告到桌面

## 核心流程

```
┌─────────────┐     ┌─────────────┐     ┌─────────────┐     ┌─────────────┐
│   扫描源    │ →   │  扫描目标    │ →   │  对比文件    │ →   │  执行迁移    │
│   目录      │     │   目录      │     │ (文件名+Hash) │     │  (删除/移动) │
└─────────────┘     └─────────────┘     └─────────────┘     └─────────────┘
                                                                    ↓
┌─────────────┐                                             ┌─────────────┐
│  生成报告    │ ← ← ← ← ← ← ← ← ← ← ← ← ← ← ← ← ← ← ← ← ← │   CSV 报告   │
└─────────────┘                                             └─────────────┘
```

## 技术栈

| 组件 | 技术 |
|------|------|
| 框架 | WPF + .NET 10 |
| MVVM | CommunityToolkit.Mvvm 8.x |
| 数据库 | Microsoft.Data.Sqlite |
| Hash 算法 | MD5 (流式计算) |

## 项目结构

```
file-sync/
├── file-sync.sln                 # 解决方案文件
├── src/                          # 源代码
│   ├── file-sync.csproj
│   ├── App.xaml / App.xaml.cs    # 应用程序入口
│   ├── MainWindow.xaml / .cs     # 主界面
│   ├── ViewModels/
│   │   └── MainViewModel.cs      # MVVM 视图模型
│   ├── Models/
│   │   └── FileEntry.cs          # 数据模型
│   └── Services/
│       ├── FileScanner.cs        # 目录扫描（并行）
│       ├── HashCalculator.cs     # MD5 Hash 计算（流式）
│       ├── FileComparator.cs     # 文件对比
│       ├── FileMigrator.cs       # 删除/移动操作
│       ├── CsvReportGenerator.cs # CSV 报告生成
│       └── AppState.cs           # SQLite 状态持久化
├── publish/                      # 发布输出
├── CLAUDE.md                     # AI 开发指南
├── .gitignore
└── README.md
```

## 性能优化

- ✅ `Directory.EnumerateFiles` 流式遍历（非 `GetFiles`）
- ✅ `ConcurrentBag` 并发收集扫描结果
- ✅ MD5 流式计算（1MB buffer，避免大文件内存溢出）
- ✅ SQLite 持久化支持断点续传
- ✅ 先比较文件大小再计算 Hash（减少不必要的计算）

## 迁移报告格式

CSV 报告包含以下字段：

| 字段 | 说明 |
|------|------|
| 操作类型 | Delete / Move / Skip / Error |
| 源路径 | 文件原始路径 |
| 目标路径 | 迁移后路径 |
| 文件大小 | 字节 |
| Hash 值 | MD5 校验值 |
| 状态 | Success / Failed / Skipped |
| 错误信息 | 失败原因 |

## 常见问题

**Q: 迁移过程中可以取消吗？**  
A: 可以，点击"取消"按钮即可停止当前操作。

**Q: 断点续传如何使用？**  
A: 程序会自动保存扫描进度，下次打开时会自动恢复上次的会话。

**Q: 文件移动后原名冲突怎么办？**  
A: 程序会自动添加后缀（如 `file_1.txt`, `file_2.txt`）避免冲突。

## License

MIT License

## 贡献

欢迎提交 Issue 和 Pull Request！

## 致谢

- 🤖 **AI 驱动开发** — 本项目由 Claude Code 辅助构建，代码生成、架构设计、性能优化均由 AI 完成
- 💪 **技术支持** — 基于 Qwen3.5 模型提供核心开发支持
