using Microsoft.AspNetCore.Mvc;
using TelegramDownloader.Models.Api;
using TelegramDownloader.Services.Audio;

namespace TelegramDownloader.Controllers.Api.V1
{
    /// <summary>
    /// Audio transcoding to MP3/AAC with FFmpeg, for offline downloads of the
    /// audio app. The files themselves are served by
    /// <c>GET /api/v1/channels/{channelId}/files/{fileId}/transcoded</c> and
    /// <c>GET /api/v1/local/transcoded</c>.
    /// </summary>
    [Route("api/v1/audio")]
    [Tags("Audio")]
    public class AudioController : ApiV1ControllerBase
    {
        private readonly AudioTranscodeService _transcoder;

        public AudioController(AudioTranscodeService transcoder)
        {
            _transcoder = transcoder;
        }

        /// <summary>Whether audio transcoding is available, and its formats and bitrates.</summary>
        /// <remarks>
        /// Check it before offering transcoded downloads: without FFmpeg
        /// <c>formats</c> is empty and the transcoded endpoints answer <c>501</c>,
        /// so the client should download the original instead.
        /// </remarks>
        [HttpGet("transcode/info")]
        [ProducesResponseType(typeof(ApiResult<ApiAudioTranscodeInfoDto>), StatusCodes.Status200OK)]
        public IActionResult TranscodeInfo() => OkResult(ApiAudioTranscodeInfoDto.Create(_transcoder.IsAvailable));

        /// <summary>The <c>501</c> answer of the transcoded endpoints when FFmpeg is missing.</summary>
        [NonAction]
        public static IActionResult FfmpegUnavailable() =>
            new ObjectResult(ApiResult.Fail(ApiErrorCodes.NotSupported, "FFmpeg is not available on the server"))
            {
                StatusCode = StatusCodes.Status501NotImplemented
            };
    }
}
