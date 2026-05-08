using file_sync.Models;
using System;
using System.Collections.Generic;
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
}
