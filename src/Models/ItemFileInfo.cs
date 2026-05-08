using System;
using System.IO;

namespace file_sync.Models
{
    public class ItemFileInfo
    {
        public string? FullPath { set; get; }
        public string? FileName { set; get; }
        public long FileSize { set; get; }
        public DateTime? LastModified { set; get; }
        public DateTime? CreatedTime { set; get; }
        public DateTime? LastAccessTime { set; get; }
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
}
