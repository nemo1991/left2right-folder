using file_sync.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace file_sync.Services
{
    public class FolderLeftDryRightFill(IHashCalculator hashCalculator) : ILeft2Right
    {
        public string? Left { set; get; }
        public string? Right { set; get; }

        public IHashCalculator HashCalculator { get; } = hashCalculator;

        public List<ItemInfo> ItemInfos { get; } = [];

        public event EventHandler<Left2RightEventArgs>? OnItemScaning;
        public event EventHandler<Left2RightEventArgs>? OnItemMigrating;
        Task ILeft2Right.ScanAsync(CancellationToken token)
        {

            return Task.Run(async () =>
            {
                if (!Directory.Exists(Left))
                {
                    throw new DirectoryNotFoundException($"源目录不存在：{Left}");
                }

                if (!Directory.Exists(Right))
                {
                    throw new DirectoryNotFoundException($"目标目录不存在：{Right}");
                }

                // 使用 EnumerateFiles 流式遍历，避免 GetFiles 一次性加载所有文件
                var fileEnumerator = Directory.EnumerateFiles(Left, "*.*", SearchOption.AllDirectories)
                    .GetEnumerator();

                var scanResultCount = new Dictionary<ScanResult, int>() {
                    { ScanResult.ToMove,0},
                    { ScanResult.ToDelete,0 },
                    { ScanResult.Confilct,0},
                    { ScanResult.Error,0}
                };

                var eventArgs = new Left2RightEventArgs
                {
                    ScanResultCount = scanResultCount
                };

                while (fileEnumerator.MoveNext())
                {
                    token.ThrowIfCancellationRequested();


                    ItemInfo itemInfo = new()
                    {
                        Source = new ItemFileInfo
                        {
                            FullPath = fileEnumerator.Current,
                        }
                    };

                    eventArgs.Current = itemInfo;

                    try
                    {

                        var info = new FileInfo(itemInfo.Source.FullPath);

                        // 跳过系统文件和隐藏文件
                        if ((info.Attributes & FileAttributes.System) != 0)
                            continue;

                        itemInfo.Source.FillFileInfo(info);
                        itemInfo.Source.Hash = await HashCalculator.ComputeHashAsync(itemInfo.Source.FullPath, ct: token);


                        var relativePath = Path.GetRelativePath(Left, itemInfo.Source.FullPath);
                        itemInfo.Target = new ItemFileInfo
                        {
                            FullPath = Path.Combine(Right, relativePath)
                        };

                        if (File.Exists(itemInfo.Target.FullPath))
                        {
                            var targetFileInfo = new FileInfo(itemInfo.Target.FullPath);

                            itemInfo.Target.FillFileInfo(targetFileInfo);
                            itemInfo.Target.Hash = await HashCalculator.ComputeHashAsync(itemInfo.Target.FullPath, ct: token);

                            if (itemInfo.Source.Hash == itemInfo.Target.Hash)
                            {
                                itemInfo.ScanResult = ScanResult.ToDelete;
                                scanResultCount[ScanResult.ToDelete] += 1;
                            }
                            else
                            {
                                itemInfo.ScanResult = ScanResult.Confilct;
                                scanResultCount[ScanResult.Confilct] += 1;
                            }
                        }
                        else
                        {
                            itemInfo.ScanResult = ScanResult.ToMove;
                            scanResultCount[ScanResult.ToMove] += 1;
                        }

                    }
                    catch (Exception ex)
                    {
                        itemInfo.ScanResult = ScanResult.Error;
                        eventArgs.IsErr = true;
                        eventArgs.Msg = ex.Message;
                        scanResultCount[ScanResult.Error] += 1;
                    }


                    ItemInfos.Add(itemInfo);
                    eventArgs.ProcessedTotal += 1;

                    OnItemScaning?.Invoke(this, eventArgs);
                }
            }, token);
        }

        Task ILeft2Right.MigrateAsync(CancellationToken token)
        {
            return Task.Run(async () =>
            {
                var args = new Left2RightEventArgs
                {
                    Total = ItemInfos.Count,
                    MigrateResultCount = new Dictionary<MigrateResult, int>() {
                            { MigrateResult.Deleted,0},
                            { MigrateResult.Moved,0 },
                            { MigrateResult.Skipped,0},
                            { MigrateResult.Fail,0}
                        }
                };

                if (ItemInfos == null || ItemInfos.Count < 1)
                {
                    args.IsErr = true;
                    args.Msg = "没有可迁移的文件，请先执行扫描";
                    OnItemMigrating?.Invoke(this, args);
                    return;
                }

                foreach (var itemInfo in ItemInfos)
                {
                    try
                    {
                        args.Current = itemInfo;

                        token.ThrowIfCancellationRequested();

                        if (itemInfo.ScanResult == ScanResult.Error || itemInfo.ScanResult == ScanResult.Confilct)
                        {
                            itemInfo.MigrateResult = MigrateResult.Skipped;
                            args.MigrateResultCount[MigrateResult.Skipped] += 1;
                        }
                        else if (itemInfo.ScanResult == ScanResult.ToDelete)
                        {
                            File.Delete(itemInfo.Source.FullPath!);
                            itemInfo.MigrateResult = MigrateResult.Deleted;
                            args.MigrateResultCount[MigrateResult.Deleted] += 1;
                        }
                        else if (itemInfo.ScanResult == ScanResult.ToMove)
                        {
                            var targetDir = Path.GetDirectoryName(itemInfo.Target!.FullPath);
                            if (!Directory.Exists(targetDir))
                            {
                                Directory.CreateDirectory(targetDir!);
                            }
                            File.Move(itemInfo.Source.FullPath!, itemInfo.Target.FullPath!);
                            itemInfo.MigrateResult = MigrateResult.Moved;
                            args.MigrateResultCount[MigrateResult.Moved] += 1;
                        }
                        else
                        {
                            args.IsErr = true;
                            args.Msg = $"未知的扫描结果：{itemInfo.ScanResult}.";
                            itemInfo.MigrateResult = MigrateResult.Fail;
                            args.MigrateResultCount[MigrateResult.Fail] += 1;
                            itemInfo.MigrateMsg = args.Msg;
                        }
                    }
                    catch (Exception ex)
                    {
                        itemInfo.MigrateResult = MigrateResult.Fail;
                        itemInfo.MigrateMsg = ex.Message;
                        args.IsErr = true;
                        args.Msg = ex.Message;
                    }

                    args.ProcessedTotal += 1;

                    OnItemMigrating?.Invoke(this, args);
                }

            }, token);
        }

    }
}
