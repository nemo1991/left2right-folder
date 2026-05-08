namespace file_sync.Models
{
    public class ItemInfo
    {
        public required ItemFileInfo Source { set; get; }

        public ItemFileInfo? Target { set; get; }

        public ScanResult? ScanResult { set; get; }

        public string? ScanMsg { set; get; }

        public MigrateResult? MigrateResult { set; get; }

        public string? MigrateMsg { set; get; }
    }
}
