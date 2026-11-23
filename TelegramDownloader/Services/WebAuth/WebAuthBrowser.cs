using System.Text.Json;
using Microsoft.JSInterop;

namespace TelegramDownloader.Services.WebAuth
{
    /// <summary>
    /// Browser-side calls the Blazor UI needs: the session cookie can only be
    /// set or cleared by an HTTP response to the browser, never by the circuit,
    /// so these run <c>fetch</c> against the web-auth API.
    /// </summary>
    public static class WebAuthBrowser
    {
        /// <summary>Clears the session cookie and reloads the UI (the gate then shows the login page).</summary>
        public static ValueTask SignOutAsync(IJSRuntime js) =>
            js.InvokeVoidAsync("eval",
                "fetch('/api/v1/web-auth/logout',{method:'POST',credentials:'same-origin',headers:{'X-Requested-With':'tfm'}})" +
                ".finally(function(){location.href='/';});");

        /// <summary>
        /// Signs this browser in (after the password was set or changed from the
        /// Blazor UI, which invalidates the previous cookie). Returns true on success.
        /// </summary>
        public static async Task<bool> SignInAsync(IJSRuntime js, string password, bool remember = false)
        {
            var body = JsonSerializer.Serialize(new { password, remember });
            var script =
                "fetch('/api/v1/web-auth/login',{method:'POST',credentials:'same-origin'," +
                "headers:{'Content-Type':'application/json','X-Requested-With':'tfm'},body:" + JsonSerializer.Serialize(body) + "})" +
                ".then(function(r){return r.ok;}).catch(function(){return false;})";
            try
            {
                return await js.InvokeAsync<bool>("eval", script);
            }
            catch
            {
                return false;
            }
        }
    }
}
