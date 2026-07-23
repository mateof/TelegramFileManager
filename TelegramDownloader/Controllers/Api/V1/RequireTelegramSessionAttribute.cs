using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using TelegramDownloader.Data;
using TelegramDownloader.Models.Api;

namespace TelegramDownloader.Controllers.Api.V1
{
    /// <summary>
    /// Rejects the request with <c>401 not_logged_in</c> (or <c>503
    /// setup_required</c>) when no Telegram session is active.
    ///
    /// The API key protects the endpoint; this attribute protects the operation,
    /// which additionally needs a signed-in Telegram account.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class RequireTelegramSessionAttribute : Attribute, IAsyncActionFilter
    {
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var telegram = context.HttpContext.RequestServices.GetService<ITelegramService>();

            if (telegram == null || !telegram.IsConfigured)
            {
                context.Result = new ObjectResult(ApiResult.Fail(
                    ApiErrorCodes.SetupRequired,
                    "The application has not been configured yet. See GET /api/v1/system/setup."))
                {
                    StatusCode = StatusCodes.Status503ServiceUnavailable
                };
                return;
            }

            bool loggedIn;
            try
            {
                loggedIn = telegram.checkUserLogin();
            }
            catch
            {
                loggedIn = false;
            }

            if (!loggedIn)
            {
                context.Result = new ObjectResult(ApiResult.Fail(
                    ApiErrorCodes.NotLoggedIn,
                    "No Telegram session is active. Sign in through /api/v1/auth."))
                {
                    StatusCode = StatusCodes.Status401Unauthorized
                };
                return;
            }

            await next();
        }
    }
}
