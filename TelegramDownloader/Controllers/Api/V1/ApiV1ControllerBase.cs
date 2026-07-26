using Microsoft.AspNetCore.Mvc;
using TelegramDownloader.Models.Api;

namespace TelegramDownloader.Controllers.Api.V1
{
    /// <summary>
    /// Shared plumbing for every v1 controller: consistent envelopes, consistent
    /// status codes and a helper to build absolute URLs behind a reverse proxy.
    /// </summary>
    [ApiController]
    [Produces("application/json")]
    [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status500InternalServerError)]
    public abstract class ApiV1ControllerBase : ControllerBase
    {
        /// <summary>
        /// Absolute base URL of this server as seen by the client, honouring
        /// <c>X-Forwarded-Proto</c>/<c>X-Forwarded-Host</c> (the app enables
        /// forwarded headers at startup).
        /// </summary>
        protected string BaseUrl => $"{Request.Scheme}://{Request.Host}";

        protected IActionResult OkResult<T>(T data, string? message = null) =>
            Ok(ApiResult<T>.Ok(data, message));

        protected IActionResult OkPaged<T>(T data, PageInfo page) =>
            Ok(ApiResult<T>.Ok(data, page));

        protected IActionResult OkEmpty(string? message = null) =>
            Ok(ApiResult.Done(message));

        protected IActionResult BadRequestResult(string message, string code = ApiErrorCodes.InvalidRequest, string? detail = null) =>
            BadRequest(ApiResult.Fail(code, message, detail));

        protected IActionResult NotFoundResult(string message, string code = ApiErrorCodes.NotFound) =>
            NotFound(ApiResult.Fail(code, message));

        protected IActionResult ConflictResult(string message, string code = ApiErrorCodes.Conflict) =>
            Conflict(ApiResult.Fail(code, message));

        protected IActionResult ForbiddenResult(string message) =>
            StatusCode(StatusCodes.Status403Forbidden, ApiResult.Fail(ApiErrorCodes.Forbidden, message));

        protected IActionResult ErrorResult(string message, Exception? ex = null, string code = ApiErrorCodes.InternalError) =>
            StatusCode(StatusCodes.Status500InternalServerError, ApiResult.Fail(code, message, ex?.Message));

        protected IActionResult UnavailableResult(string message, string code = ApiErrorCodes.ServiceUnavailable) =>
            StatusCode(StatusCodes.Status503ServiceUnavailable, ApiResult.Fail(code, message));

        /// <summary>
        /// Applies in-memory paging to an already materialised list and returns
        /// both the page and its metadata.
        /// </summary>
        protected static (List<T> Items, PageInfo Page) Paginate<T>(IReadOnlyList<T> source, PagedQuery query)
        {
            var page = PageInfo.Create(query.Page, query.PageSize, source.Count);
            var items = source.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToList();
            return (items, page);
        }
    }
}
