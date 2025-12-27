namespace TFMAudioApp.Models;

/// <summary>
/// Standard API response wrapper (renamed to avoid conflict with Refit.ApiResponse)
/// </summary>
public class ApiResult<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public string? Error { get; set; }
    public string? Message { get; set; }
    public PaginationInfo? Pagination { get; set; }

    public static ApiResult<T> Ok(T data, string? message = null) => new()
    {
        Success = true,
        Data = data,
        Message = message
    };

    public static ApiResult<T> Fail(string error) => new()
    {
        Success = false,
        Error = error
    };
}

public class PaginationInfo
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalItems { get; set; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalItems / PageSize) : 0;
    public bool HasNext => Page < TotalPages;
    public bool HasPrevious => Page > 1;
}
