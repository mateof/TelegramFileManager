using Microsoft.AspNetCore.Mvc;
using TelegramDownloader.Models.Api;
using TelegramDownloader.Services.Deletes;

namespace TelegramDownloader.Controllers.Api.V1
{
    /// <summary>
    /// Background deletes with live progress, started by
    /// <c>POST /api/v1/channels/{channelId}/files/delete-operations</c> and
    /// <c>POST /api/v1/local/delete-operations</c>.
    ///
    /// Progress is also pushed as <c>DeleteProgress</c> on <c>/hubs/events</c>
    /// (at most 4 per second per operation, plus one when it ends); without
    /// the hub, poll <c>GET /api/v1/delete-operations/{id}</c> about once a
    /// second. Finished operations stay readable for 10 minutes.
    /// </summary>
    [Route("api/v1/delete-operations")]
    [Tags("Delete operations")]
    public class DeleteOperationsController : ApiV1ControllerBase
    {
        private readonly DeleteOperationRegistry _registry;

        public DeleteOperationsController(DeleteOperationRegistry registry)
        {
            _registry = registry;
        }

        /// <summary>Running delete operations, then the ones finished in the last 10 minutes.</summary>
        [HttpGet]
        [ProducesResponseType(typeof(ApiResult<List<ApiDeleteOperationDto>>), StatusCodes.Status200OK)]
        public IActionResult List() => OkResult(_registry.Snapshots().Select(ApiDeleteOperationDto.From).ToList());

        /// <summary>One delete operation.</summary>
        /// <remarks>
        /// <c>phase</c> goes <c>counting</c> → <c>deleting</c> (<c>waiting</c>
        /// while Telegram imposes a FLOOD_WAIT, with <c>waitUntil</c>) →
        /// <c>done</c>, <c>failed</c> or <c>cancelled</c>. A <c>done</c>
        /// operation can still have <c>failedCount &gt; 0</c> (partial
        /// success); <c>failed</c> lists the first 100 items with the reason.
        /// 404 when unknown or finished more than 10 minutes ago.
        /// </remarks>
        /// <param name="id">Operation id returned by the endpoint that started it.</param>
        [HttpGet("{id}")]
        [ProducesResponseType(typeof(ApiResult<ApiDeleteOperationDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status404NotFound)]
        public IActionResult Get(string id)
        {
            var operation = _registry.Get(id);
            return operation == null
                ? NotFoundResult("Delete operation not found")
                : OkResult(ApiDeleteOperationDto.From(operation.Snapshot()));
        }

        /// <summary>Cancels a running delete operation.</summary>
        /// <remarks>
        /// The operation stops after the step in flight (a batch of up to 100
        /// files, or a FLOOD_WAIT) and ends as <c>cancelled</c>; what was
        /// already deleted stays deleted. 202 with the current state; 404 when
        /// unknown; 409 <c>conflict</c> when it already finished.
        /// </remarks>
        /// <param name="id">Operation id.</param>
        [HttpPost("{id}/cancel")]
        [ProducesResponseType(typeof(ApiResult<ApiDeleteOperationDto>), StatusCodes.Status202Accepted)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status409Conflict)]
        public IActionResult Cancel(string id)
        {
            switch (_registry.TryCancel(id))
            {
                case DeleteCancelResult.NotFound:
                    return NotFoundResult("Delete operation not found");
                case DeleteCancelResult.AlreadyFinished:
                    return ConflictResult("The delete operation already finished");
                default:
                    var operation = _registry.Get(id);
                    return Accepted(ApiResult<ApiDeleteOperationDto?>.Ok(
                        operation == null ? null : ApiDeleteOperationDto.From(operation.Snapshot()), "Cancellation requested"));
            }
        }
    }
}
