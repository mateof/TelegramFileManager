using System.Diagnostics;
using System.Runtime.InteropServices;
using TelegramDownloader.Data;
using TelegramDownloader.Models;

namespace TelegramDownloader.Services
{
    public interface ISystemMetricsService
    {
        Task<SystemMetrics> GetMetricsAsync();
        void StartMonitoring(int intervalMs = 2000);
        void StopMonitoring();
        event EventHandler<SystemMetrics> OnMetricsUpdated;
    }

    public class SystemMetricsService : ISystemMetricsService, IDisposable
    {
        private readonly Process _currentProcess;
        private Timer _timer;
        private DateTime _lastCpuTime;
        private TimeSpan _lastTotalProcessorTime;
        private bool _isFirstRead = true;

        public event EventHandler<SystemMetrics> OnMetricsUpdated;

        public SystemMetricsService()
        {
            _currentProcess = Process.GetCurrentProcess();
            _lastCpuTime = DateTime.UtcNow;
            _lastTotalProcessorTime = _currentProcess.TotalProcessorTime;
        }

        public void StartMonitoring(int intervalMs = 2000)
        {
            _timer?.Dispose();
            _timer = new Timer(async _ =>
            {
                var metrics = await GetMetricsAsync();
                OnMetricsUpdated?.Invoke(this, metrics);
            }, null, 0, intervalMs);
        }

        public void StopMonitoring()
        {
            _timer?.Dispose();
            _timer = null;
        }

        public async Task<SystemMetrics> GetMetricsAsync()
        {
            var metrics = new SystemMetrics
            {
                ProcessorCount = Environment.ProcessorCount
            };

            // Get CPU usage
            await Task.Run(() =>
            {
                try
                {
                    metrics.AppCpuUsage = GetAppCpuUsage();
                    metrics.SystemCpuUsage = GetSystemCpuUsage();
                }
                catch
                {
                    metrics.AppCpuUsage = 0;
                    metrics.SystemCpuUsage = 0;
                }
            });

            // Get memory info
            GetMemoryInfo(metrics);

            // Get disk info
            await GetDiskInfoAsync(metrics);

            return metrics;
        }

        private double GetAppCpuUsage()
        {
            _currentProcess.Refresh();

            var currentTime = DateTime.UtcNow;
            var currentTotalProcessorTime = _currentProcess.TotalProcessorTime;

            if (_isFirstRead)
            {
                _isFirstRead = false;
                _lastCpuTime = currentTime;
                _lastTotalProcessorTime = currentTotalProcessorTime;
                return 0;
            }

            var cpuUsedMs = (currentTotalProcessorTime - _lastTotalProcessorTime).TotalMilliseconds;
            var elapsedMs = (currentTime - _lastCpuTime).TotalMilliseconds;

            _lastCpuTime = currentTime;
            _lastTotalProcessorTime = currentTotalProcessorTime;

            if (elapsedMs <= 0) return 0;

            var cpuUsagePercent = (cpuUsedMs / (Environment.ProcessorCount * elapsedMs)) * 100;
            return Math.Min(100, Math.Max(0, cpuUsagePercent));
        }

        private double GetSystemCpuUsage()
        {
            // This is a simplified approach - for more accurate system CPU,
            // you'd need platform-specific implementations
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return GetWindowsSystemCpu();
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    return GetLinuxSystemCpu();
                }
            }
            catch { }

            return 0;
        }

        private double GetWindowsSystemCpu()
        {
            // Use a simple estimation based on all processes
            // For more accurate results, you'd use PerformanceCounter
            try
            {
                var allProcesses = Process.GetProcesses();

                foreach (var proc in allProcesses)
                {
                    try
                    {
                        // This is an approximation
                        if (!proc.HasExited)
                        {
                            proc.Dispose();
                        }
                    }
                    catch { }
                }

                // Return app CPU as approximation when we can't get system CPU
                return GetAppCpuUsage() * 2; // Rough estimate
            }
            catch
            {
                return 0;
            }
        }

        private double GetLinuxSystemCpu()
        {
            try
            {
                var cpuLine = File.ReadAllLines("/proc/stat").FirstOrDefault(l => l.StartsWith("cpu "));
                if (cpuLine != null)
                {
                    var values = cpuLine.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(long.Parse).ToArray();
                    var idle = values[3];
                    var total = values.Sum();
                    var usage = 100.0 * (1.0 - (double)idle / total);
                    return Math.Max(0, Math.Min(100, usage));
                }
            }
            catch { }
            return 0;
        }

        private void GetMemoryInfo(SystemMetrics metrics)
        {
            _currentProcess.Refresh();

            // App memory
            metrics.AppMemoryBytes = _currentProcess.WorkingSet64;
            metrics.AppPrivateMemoryBytes = _currentProcess.PrivateMemorySize64;

            // System memory
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    GetWindowsMemoryInfo(metrics);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    GetLinuxMemoryInfo(metrics);
                }
                else
                {
                    // Fallback estimation
                    metrics.TotalMemoryBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
                    metrics.AvailableMemoryBytes = metrics.TotalMemoryBytes - metrics.AppMemoryBytes;
                    metrics.UsedMemoryBytes = metrics.AppMemoryBytes;
                }

                if (metrics.TotalMemoryBytes > 0)
                {
                    metrics.MemoryUsagePercent = (double)metrics.UsedMemoryBytes / metrics.TotalMemoryBytes * 100;
                }
            }
            catch
            {
                // Fallback
                var gcMemory = GC.GetGCMemoryInfo();
                metrics.TotalMemoryBytes = gcMemory.TotalAvailableMemoryBytes;
                metrics.AvailableMemoryBytes = gcMemory.TotalAvailableMemoryBytes - metrics.AppMemoryBytes;
                metrics.UsedMemoryBytes = metrics.TotalMemoryBytes - metrics.AvailableMemoryBytes;
            }
        }

        private void GetWindowsMemoryInfo(SystemMetrics metrics)
        {
            try
            {
                // Use GC memory info - this provides accurate system memory information
                var gcMemory = GC.GetGCMemoryInfo();
                metrics.TotalMemoryBytes = gcMemory.TotalAvailableMemoryBytes;

                // Estimate used memory based on available memory
                // GC.GetGCMemoryInfo gives us total physical memory available to the process
                // We can use Environment.WorkingSet for a rough estimate of system usage
                var allProcessesMemory = Process.GetProcesses()
                    .Sum(p =>
                    {
                        try { return p.WorkingSet64; }
                        catch { return 0L; }
                    });

                metrics.UsedMemoryBytes = Math.Min(allProcessesMemory, metrics.TotalMemoryBytes);
                metrics.AvailableMemoryBytes = metrics.TotalMemoryBytes - metrics.UsedMemoryBytes;
            }
            catch
            {
                // Fallback to GC info
                var gcMemory = GC.GetGCMemoryInfo();
                metrics.TotalMemoryBytes = gcMemory.TotalAvailableMemoryBytes;
                metrics.AvailableMemoryBytes = metrics.TotalMemoryBytes - metrics.AppMemoryBytes;
                metrics.UsedMemoryBytes = metrics.AppMemoryBytes;
            }
        }

        private void GetLinuxMemoryInfo(SystemMetrics metrics)
        {
            try
            {
                var memInfo = File.ReadAllLines("/proc/meminfo");
                foreach (var line in memInfo)
                {
                    if (line.StartsWith("MemTotal:"))
                    {
                        metrics.TotalMemoryBytes = ParseMemInfoLine(line);
                    }
                    else if (line.StartsWith("MemAvailable:"))
                    {
                        metrics.AvailableMemoryBytes = ParseMemInfoLine(line);
                    }
                }
                metrics.UsedMemoryBytes = metrics.TotalMemoryBytes - metrics.AvailableMemoryBytes;
            }
            catch
            {
                var gcMemory = GC.GetGCMemoryInfo();
                metrics.TotalMemoryBytes = gcMemory.TotalAvailableMemoryBytes;
            }
        }

        private long ParseMemInfoLine(string line)
        {
            var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && long.TryParse(parts[1], out var kb))
            {
                return kb * 1024; // Convert KB to bytes
            }
            return 0;
        }

        private async Task GetDiskInfoAsync(SystemMetrics metrics)
        {
            try
            {
                var tempPath = FileService.TEMPORARYPDIR;
                metrics.TempFolderPath = tempPath;

                // Get temp folder size
                if (Directory.Exists(tempPath))
                {
                    var dirInfo = await HelperService.GetDirecctorySizeAsync(tempPath);
                    metrics.TempFolderSizeBytes = dirInfo.SizeBytes;
                }

                // Get disk info for the drive containing temp folder
                var driveInfo = new DriveInfo(Path.GetPathRoot(tempPath) ?? "C:");
                if (driveInfo.IsReady)
                {
                    metrics.DiskTotalBytes = driveInfo.TotalSize;
                    metrics.DiskFreeBytes = driveInfo.AvailableFreeSpace;
                    metrics.DiskUsedBytes = metrics.DiskTotalBytes - metrics.DiskFreeBytes;
                    metrics.DiskUsagePercent = (double)metrics.DiskUsedBytes / metrics.DiskTotalBytes * 100;
                }
            }
            catch
            {
                // Ignore disk errors
            }
        }

        public void Dispose()
        {
            _timer?.Dispose();
            _currentProcess?.Dispose();
        }
    }
}
