namespace TelegramDownloader.Models.Mobile
{
    /// <summary>
    /// Standard API response wrapper for consistent response format
    /// </summary>
    /// <typeparam name="T">Type of the data payload</typeparam>
    public class ApiResponse<T>
    {
        public bool Success { get; set; }
        public T? Data { get; set; }
        public string? Error { get; set; }
        public string? Message { get; set; }
        public PaginationInfo? Pagination { get; set; }

        public static ApiResponse<T> Ok(T data, string? message = null)
        {
            return new ApiResponse<T>
            {
                Success = true,
                Data = data,
                Message = message
            };
        }

        public static ApiResponse<T> Ok(T data, PaginationInfo pagination)
        {
            return new ApiResponse<T>
            {
                Success = true,
                Data = data,
                Pagination = pagination
            };
        }

        public static ApiResponse<T> Fail(string error)
        {
            return new ApiResponse<T>
            {
                Success = false,
                Error = error
            };
        }
    }

    /// <summary>
    /// Pagination information for list responses
    /// </summary>
    public class PaginationInfo
    {
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalItems { get; set; }
        public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalItems / PageSize) : 0;
        public bool HasNext => Page < TotalPages;
        public bool HasPrevious => Page > 1;

        public static PaginationInfo Create(int page, int pageSize, int totalItems)
        {
            return new PaginationInfo
            {
                Page = page,
                PageSize = pageSize,
                TotalItems = totalItems
            };
        }
    }
}
