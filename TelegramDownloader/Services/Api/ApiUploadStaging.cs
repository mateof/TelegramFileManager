namespace TelegramDownloader.Services.Api
{
    /// <summary>
    /// Where multipart uploads received by the API are staged before being
    /// pushed to Telegram.
    ///
    /// The regular server-to-Telegram pipeline reads its sources from the local
    /// root, so an uploaded body is written here first and then handed to that
    /// pipeline. This keeps API uploads identical to web uploads in terms of
    /// progress reporting, task persistence and resume-after-restart.
    /// </summary>
    public static class ApiUploadStaging
    {
        /// <summary>Folder name under the local root used for staged uploads.</summary>
        public const string FolderName = ".api-uploads";
    }
}
