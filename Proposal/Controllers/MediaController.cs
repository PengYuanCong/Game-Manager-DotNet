using Microsoft.AspNetCore.Mvc;

namespace Proposal.Controllers
{
    public class MediaController : Controller
    {
        private readonly string _apiKey;

        public MediaController(IConfiguration configuration)
        {
            _apiKey = configuration["YouTubeSettings:ApiKey"] ?? string.Empty;
        }

        public IActionResult Highlights()
        {
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> GetYouTubeVideos(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return BadRequest("請輸入關鍵字");
            }

            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                return StatusCode(503, "尚未設定 YouTube API Key。");
            }

            try
            {
                using var client = new HttpClient();
                var url = $"https://www.googleapis.com/youtube/v3/search?part=snippet&maxResults=5&q={Uri.EscapeDataString(query)}&type=video&key={_apiKey}";

                var response = await client.GetAsync(url);
                var content = await response.Content.ReadAsStringAsync();

                return response.IsSuccessStatusCode
                    ? Content(content, "application/json")
                    : StatusCode((int)response.StatusCode, content);
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }
    }
}
