using System;
using System.Collections.Generic;

namespace file_sync.Models
{
    public class Left2RightEventArgs : EventArgs
    {
        public bool IsErr { set; get; }
        public string? Msg { set; get; }
        public ItemInfo? Current { set; get; }
        public Dictionary<ScanResult, int>? ScanResultCount { set; get; }
        public Dictionary<MigrateResult, int>? MigrateResultCount { set; get; }
        public int Total { set; get; }
        public int ProcessedTotal { set; get; }
    }
}
