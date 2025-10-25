using TelegramDownloader.Models;
using TelegramDownloader.Pages;

namespace TelegramDownloader.Services
{
    public class HelperService
    {
        public static readonly string[] SizeSuffixes =
                  { "bytes", "KB", "MB", "GB", "TB", "PB", "EB", "ZB", "YB" };

        public static string SizeSuffix(Int64 value, int decimalPlaces = 1)
        {
            if (value < 0) { return "-" + SizeSuffix(-value, decimalPlaces); }

            int i = 0;
            decimal dValue = (decimal)value;
            while (Math.Round(dValue, decimalPlaces) >= 1000)
            {
                dValue /= 1024;
                i++;
            }

            return string.Format("{0:n" + decimalPlaces + "} {1}", dValue, SizeSuffixes[i]);
        }

        public static string SizeSuffixPerTime(Int64 value, String time = "s", int decimalPlaces = 1)
        {
            return SizeSuffix(value, decimalPlaces) + "/" + time;
        }

        public static long bytesToMegaBytes(Int64 value)
        {
            return value / 1024 / 1024;
        }

        public static async Task<DirectorySizeModel> GetDirecctorySizeAsync(string path)
        {
            DirectoryInfo dirInfo = new DirectoryInfo(path);
            return await Task.Run(
                () =>
                    {
                        var TotalElements = dirInfo.GetFiles("*.*", SearchOption.AllDirectories);
                        var TotalSize = TotalElements.Sum(file => file.Length);
                        return new DirectorySizeModel()
                        {
                            SizeBytes = TotalSize,
                            SizeWithSuffix = SizeSuffix(TotalSize),
                            TotalElements = TotalElements.Count()
                        };
                    }
                );
        }
    }

    public class WaitingTime
    {
        /// <summary>
        /// waiting time in milliseconds
        /// </summary>
        private int waitingTime = 2000;
        private DateTime initialTime;
        public WaitingTime() 
        {
            initialTime = DateTime.Now;
            waitingTime = GeneralConfigStatic.config.TimeSleepBetweenTransactions;
        }
        public WaitingTime(int wt) 
        {
            waitingTime = wt;
            initialTime = DateTime.Now;
        }

        public async Task Sleep()
        {
            var totalTime = (DateTime.Now - initialTime).TotalMilliseconds;
            if (totalTime <= waitingTime)
            {
                var waitTime = waitingTime - totalTime;
                TimeSpan delay = TimeSpan.FromMilliseconds(waitTime);
                await Task.Delay(delay);
            }
        }

    }

    public class FileExtensionTypeTest
    {
        private static List<String> videoExtensions = new List<String>() {
            ".mp4",
            ".mkv",
            ".avi",
            ".mov",
            ".wmv",
            ".flv",
            ".webm",
            ".mpeg",
            ".mpg",
            ".3gp",
            ".ts",
            ".m4v",
            ".divx",
            ".xvid",
            ".rm",
            ".vob"
        };

        private static List<string> audioExtensions = new List<string>
        {
            ".mp3",
            ".wav",
            ".flac",
            ".aac",
            ".ogg",
            ".wma",
            ".m4a",
            ".aiff",
            ".alac",
            ".pcm",
            ".opus",
            ".mid",   // MIDI
            ".amr",
            ".ra",    // RealAudio
            ".mp2"
        };

        public static bool isVideoExtension(string extension) { return videoExtensions.Contains(extension); }

        public static bool isAudioExtension(string extension) { return audioExtensions.Contains(extension); }
    }
}
