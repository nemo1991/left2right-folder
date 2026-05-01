using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace file_sync.Services
{
    internal interface ILeft2Right
    {
        string Left { get; }
        string Right { get; }

        List<ItemInfo> ItemInfos { get; }

        event EventHandler<Left2RightEventArgs> Scaning;


        Task<ILeft2Right> ScanAsync(CancellationToken token);

        event EventHandler<Left2RightEventArgs> Migrating;


        Task<ILeft2Right> MigrateAsync(CancellationToken token);
    }

    public class FolderLeftDryRightFill(string left, string right) : ILeft2Right
    {
        public string Left { get; } = left;
        public string Right { get; } = right;

        public List<ItemInfo> ItemInfos { get; } = [];

        public event EventHandler<Left2RightEventArgs>? Scaning;
        public event EventHandler<Left2RightEventArgs>? Migrating;
        Task<ILeft2Right> ILeft2Right.ScanAsync(CancellationToken token)
        {
            throw new NotImplementedException();
        }

        Task<ILeft2Right> ILeft2Right.MigrateAsync(CancellationToken token)
        {
            return Task.Run(async () => { return (ILeft2Right)this; }, token);
        }


    }


    public class Left2RightEventArgs : EventArgs
    {
        public List<ItemInfo>? ItemInfos { set; get; }

        public ItemInfo? Current { set; get; }
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
    }

    public class ItemInfo
    {
        public ItemFileInfo? Source { set; get; }

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
    }

    public enum ProccesReuslt
    {
        Success,
        Fail,
        Skipped
    }
}
