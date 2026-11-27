using TelegramDownloader.Services.Audio;

namespace TelegramDownloader.Models.Api
{
    /// <summary>
    /// What <c>GET /api/v1/audio/transcode/info</c> answers: whether the
    /// <c>.../transcoded</c> endpoints can work on this server and which
    /// parameters they take.
    /// </summary>
    public class ApiAudioTranscodeInfoDto
    {
        /// <summary>FFmpeg can be run. When false every transcoded endpoint answers <c>501</c>.</summary>
        public bool FfmpegAvailable { get; set; }

        /// <summary>Accepted <c>format</c> values (<c>mp3</c>, <c>aac</c>); empty when FFmpeg is missing.</summary>
        public List<string> Formats { get; set; } = new();

        /// <summary>Format used when <c>format</c> is omitted.</summary>
        public string DefaultFormat { get; set; } = AudioTranscodeOptions.DefaultFormat;

        /// <summary>Bitrate (kbps) used when <c>bitrate</c> is omitted.</summary>
        public int DefaultBitrate { get; set; } = AudioTranscodeOptions.DefaultBitrate;

        /// <summary>Lowest bitrate; smaller values are raised to it.</summary>
        public int MinBitrate { get; set; } = AudioTranscodeOptions.MinBitrate;

        /// <summary>Highest bitrate; larger values are lowered to it.</summary>
        public int MaxBitrate { get; set; } = AudioTranscodeOptions.MaxBitrate;

        /// <summary>Suggested values for a picker (any value in range works).</summary>
        public List<int> Bitrates { get; set; } = new();

        public static ApiAudioTranscodeInfoDto Create(bool ffmpegAvailable) => new()
        {
            FfmpegAvailable = ffmpegAvailable,
            Formats = ffmpegAvailable ? AudioTranscodeOptions.Formats.ToList() : new List<string>(),
            Bitrates = AudioTranscodeOptions.SuggestedBitrates.ToList()
        };
    }
}
