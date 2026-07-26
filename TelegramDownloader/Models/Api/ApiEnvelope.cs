namespace TelegramDownloader.Models.Api
{
    /// <summary>
    /// Envelope returned by every endpoint of the modular v1 API.
    /// Clients can always rely on <see cref="Success"/> to branch, and on
    /// <see cref="Error"/> carrying a machine-readable <see cref="ApiError.Code"/>.
    /// </summary>
    /// <typeparam name="T">Type of the payload.</typeparam>
    public class ApiResult<T>
    {
        /// <summary>True when the operation completed successfully.</summary>
        public bool Success { get; set; }

        /// <summary>Payload. Null when <see cref="Success"/> is false.</summary>
        public T? Data { get; set; }

        /// <summary>Error detail. Null when <see cref="Success"/> is true.</summary>
        public ApiError? Error { get; set; }

        /// <summary>Optional human readable note about the operation.</summary>
        public string? Message { get; set; }

        /// <summary>Pagination block, present only on paged list endpoints.</summary>
        public PageInfo? Page { get; set; }

        public static ApiResult<T> Ok(T data, string? message = null) =>
            new() { Success = true, Data = data, Message = message };

        public static ApiResult<T> Ok(T data, PageInfo page) =>
            new() { Success = true, Data = data, Page = page };

        public static ApiResult<T> Fail(string code, string message, string? detail = null) =>
            new() { Success = false, Error = new ApiError { Code = code, Message = message, Detail = detail } };
    }

    /// <summary>
    /// Non-generic helper used by endpoints that return no payload.
    /// </summary>
    public class ApiResult : ApiResult<object>
    {
        public static ApiResult Done(string? message = null) =>
            new() { Success = true, Message = message };

        public new static ApiResult Fail(string code, string message, string? detail = null) =>
            new() { Success = false, Error = new ApiError { Code = code, Message = message, Detail = detail } };
    }

    /// <summary>
    /// Machine readable error description. See <see cref="ApiErrorCodes"/>.
    /// </summary>
    public class ApiError
    {
        /// <summary>Stable, machine readable code (e.g. <c>channel_not_found</c>).</summary>
        public string Code { get; set; } = ApiErrorCodes.InternalError;

        /// <summary>Short human readable explanation.</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>Optional extra context (exception message, offending value...).</summary>
        public string? Detail { get; set; }
    }

    /// <summary>
    /// Canonical set of error codes returned by the v1 API.
    /// </summary>
    public static class ApiErrorCodes
    {
        public const string Unauthorized = "unauthorized";
        public const string NotLoggedIn = "not_logged_in";
        public const string SetupRequired = "setup_required";
        public const string InvalidRequest = "invalid_request";
        public const string NotFound = "not_found";
        public const string ChannelNotFound = "channel_not_found";
        public const string FileNotFound = "file_not_found";
        public const string TaskNotFound = "task_not_found";
        public const string PlaylistNotFound = "playlist_not_found";
        public const string Conflict = "conflict";
        public const string AlreadyRunning = "already_running";
        public const string Forbidden = "forbidden";
        public const string NotSupported = "not_supported";
        public const string ServiceUnavailable = "service_unavailable";
        public const string InternalError = "internal_error";
    }

    /// <summary>
    /// Pagination metadata attached to list responses.
    /// </summary>
    public class PageInfo
    {
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalItems { get; set; }
        public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalItems / PageSize) : 0;
        public bool HasNext => Page < TotalPages;
        public bool HasPrevious => Page > 1;

        public static PageInfo Create(int page, int pageSize, int totalItems) =>
            new() { Page = page, PageSize = pageSize, TotalItems = totalItems };
    }

    /// <summary>
    /// Common paging/sorting query parameters.
    /// </summary>
    public class PagedQuery
    {
        private int _page = 1;
        private int _pageSize = 50;

        /// <summary>1-based page number.</summary>
        public int Page
        {
            get => _page;
            set => _page = value < 1 ? 1 : value;
        }

        /// <summary>Items per page (1-500).</summary>
        public int PageSize
        {
            get => _pageSize;
            set => _pageSize = value < 1 ? 1 : (value > 500 ? 500 : value);
        }

        /// <summary>Field to sort by. Supported values depend on the endpoint.</summary>
        public string? SortBy { get; set; }

        /// <summary>Sort direction.</summary>
        public bool SortDescending { get; set; }
    }
}
