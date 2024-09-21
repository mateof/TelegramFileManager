using Microsoft.AspNetCore.Mvc;

namespace TelegramDownloader.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class LogController : ControllerBase
    {
        [Route("Download")]
        public IActionResult download(string fileName, int? show = 0)
        {
            FileStream fs = new FileStream(Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "logs", fileName), FileMode.Open, FileAccess.Read);
            return show == 1 ? new FileStreamResult(fs, "text/plain") : File(fs, "text/plain", fileName);
        }
    }
}
