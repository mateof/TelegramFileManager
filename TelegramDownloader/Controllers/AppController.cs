using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TelegramDownloader.Data.db;
using TelegramDownloader.Data;
using TelegramDownloader.Models;
using TelegramDownloader.Services;
using TL;

namespace TelegramDownloader.Controllers
{
    [Route("api/app")]
    [ApiController]
    public class AppController : ControllerBase
    {
        IDbService _db { get; set; }
        ITelegramService _ts { get; set; }
        IFileService _fs { get; set; }

        public AppController(IDbService db, ITelegramService ts, IFileService fs)
        {
            _fs = fs; ;
            _ts = ts;
            _db = db;

        }

        [HttpGet("ping")]
        public ActionResult Ping()
        {
            var result = new Dictionary<string, string>
            {
                { "status", "connected" },
                { "app", "TFM App" },
                { "time", DateTimeOffset.Now.ToString("o") }
            };
            return Ok(result);
        }

        [HttpGet("mychats")]
        public async Task<ActionResult> getMyChats()
        {
            List<ChatViewBase> mineChats = new List<ChatViewBase>();
            foreach (var chat in await _ts.getAllChats())
            {
                if (chat.chat is Channel chat1)
                {
                    if (chat1.flags.HasFlag(Channel.Flags.creator))
                    {
                        mineChats.Add(chat);
                    }
                }
            }
            return Ok(mineChats);
        }
    }
}
