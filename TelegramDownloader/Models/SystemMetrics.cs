using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TelegramDownloader.Models
{
    public class SystemMetrics
    {
        // CPU
        public double SystemCpuUsage { get; set; }
        public double AppCpuUsage { get; set; }
        public int ProcessorCount { get; set; }

        // Memory
        public long TotalMemoryBytes { get; set; }
        public long UsedMemoryBytes { get; set; }
        public long AvailableMemoryBytes { get; set; }
        public double MemoryUsagePercent { get; set; }

        // App Memory
        public long AppMemoryBytes { get; set; }
        public long AppPrivateMemoryBytes { get; set; }

        // Disk
        public string TempFolderPath { get; set; }
        public long TempFolderSizeBytes { get; set; }
        public long DiskTotalBytes { get; set; }
        public long DiskUsedBytes { get; set; }
        public long DiskFreeBytes { get; set; }
        public double DiskUsagePercent { get; set; }

        // Formatted strings
        public string TotalMemory => FormatBytes(TotalMemoryBytes);
        public string UsedMemory => FormatBytes(UsedMemoryBytes);
        public string AvailableMemory => FormatBytes(AvailableMemoryBytes);
        public string AppMemory => FormatBytes(AppMemoryBytes);
        public string AppPrivateMemory => FormatBytes(AppPrivateMemoryBytes);
        public string TempFolderSize => FormatBytes(TempFolderSizeBytes);
        public string DiskTotal => FormatBytes(DiskTotalBytes);
        public string DiskUsed => FormatBytes(DiskUsedBytes);
        public string DiskFree => FormatBytes(DiskFreeBytes);

        private static string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            double value = bytes;
            while (value >= 1024 && i < suffixes.Length - 1)
            {
                value /= 1024;
                i++;
            }
            return $"{value:F1} {suffixes[i]}";
        }
    }
}
