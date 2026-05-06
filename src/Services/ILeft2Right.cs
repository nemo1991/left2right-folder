using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace file_sync.Services
{
    public interface ILeft2Right
    {
        string? Left { set; get; }
        string? Right { set; get; }

        List<ItemInfo> ItemInfos { get; }

        event EventHandler<Left2RightEventArgs> OnItemScaning;

        Task ScanAsync(CancellationToken token);

        event EventHandler<Left2RightEventArgs> OnItemMigrating;


        Task MigrateAsync(CancellationToken token);
    }

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

                int processedTotal = 0;
                int toMoveTotal = 0;
                int toDeleteTotal = 0;
                int confilctTotal = 0;
                int errorTotal = 0;

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

                    var eventArgs = new Left2RightEventArgs
                    {
                        Current = itemInfo
                    };

                    try
                    {

                        var info = new FileInfo(itemInfo.Source.FullPath);

                        // 跳过系统文件和隐藏文件
                        if ((info.Attributes & FileAttributes.System) != 0)
                            continue;

                        itemInfo.Source.FillFileInfo(info);
                        itemInfo.Source.Hash = await HashCalculator.ComputeHashAsync(itemInfo.Source.FullPath, ct: token);


                        var relativePath = Path.GetRelativePath(Left, itemInfo.Source.FullPath);
                        var targetPath = Path.Combine(Right, relativePath);
                        var targetExists = File.Exists(targetPath);

                        if (targetExists)
                        {
                            var targetFileInfo = new FileInfo(targetPath);

                            itemInfo.Target = new ItemFileInfo
                            {
                                FullPath = targetPath
                            };

                            itemInfo.Target.FillFileInfo(targetFileInfo);
                            itemInfo.Target.Hash = await HashCalculator.ComputeHashAsync(targetPath, ct: token);

                            if (itemInfo.Source.Hash == itemInfo.Target.Hash)
                            {
                                itemInfo.Status = ItemStatus.ToDelete;
                                toDeleteTotal += 1;
                            }
                            else
                            {
                                itemInfo.Status = ItemStatus.Confilct;
                                confilctTotal += 1;
                            }
                        }
                        else
                        {
                            itemInfo.Status = ItemStatus.ToMove;
                            toMoveTotal += 1;
                        }

                    }
                    catch (Exception ex)
                    {
                        itemInfo.Status = ItemStatus.Error;
                        eventArgs.Ex = ex;
                        eventArgs.IsErr = true;
                        eventArgs.Msg = ex.Message;
                        errorTotal += 1;
                    }


                    ItemInfos.Add(itemInfo);
                    processedTotal += 1;
                    eventArgs.ProcessedTotal = processedTotal;
                    eventArgs.ErrorTotal = errorTotal;
                    eventArgs.ToDeleteTotal = toDeleteTotal;
                    eventArgs.ConfilctTotal = confilctTotal;
                    eventArgs.ToMoveTotal = toMoveTotal;

                    OnItemScaning?.Invoke(this, eventArgs);
                }
            }, token);
        }

        Task ILeft2Right.MigrateAsync(CancellationToken token)
        {
            return Task.Run(async () => { return (ILeft2Right)this; }, token);
        }

    }

    public class Left2RightEventArgs : EventArgs
    {
        public bool IsErr { set; get; }
        public string? Msg { set; get; }
        public Exception? Ex { set; get; }
        public required ItemInfo Current { set; get; }
        public int Total { set; get; }
        public int ProcessedTotal { set; get; }

        public int ToMoveTotal { set; get; }
        public int ToDeleteTotal { set; get; }

        public int ConfilctTotal { set; get; }
        public int ErrorTotal { set; get; }
    }

    public class ItemFileInfo
    {

        public string? FullPath { set; get; }
        public string? FileName { set; get; }
        public long FileSize { set; get; }
        public DateTime LastModified { set; get; }
        public DateTime CreatedTime { set; get; }
        public DateTime LastAccessTime { set; get; }
        public string? Hash { set; get; }

        public ItemFileInfo FillFileInfo(FileInfo fileInfo)
        {

            FileName = fileInfo.Name;
            FileSize = fileInfo.Length;
            LastAccessTime = fileInfo.LastAccessTime;
            LastModified = fileInfo.LastWriteTime;
            CreatedTime = fileInfo.CreationTime;

            return this;
        }
    }

    public class ItemInfo
    {
        public ItemFileInfo Source { set; get; }

        public ItemFileInfo? Target { set; get; }

        public ItemStatus? Status { set; get; }

        public ProccesReuslt? Result { set; get; }
    }


    public enum ItemStatus
    {
        ToDelete,
        ToMove,
        Confilct,
        Deleted,
        Moved,
        Error,
    }

    public enum ProccesReuslt
    {
        Success,
        Fail,
        Skipped
    }
}
