using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Proposal.Models;
using Proposal.Services;

namespace Proposal.Controllers
{
    [Authorize]
    public class UserController : Controller
    {
        private const long MaxAvatarBytes = 2 * 1024 * 1024;
        private readonly IUserActivityLogService _activityLogService;
        private readonly IAiRecommendationFavoriteService _favoriteService;
        private readonly ICalculationHistoryRepository _calculationHistoryRepository;

        public UserController(
            IUserActivityLogService activityLogService,
            IAiRecommendationFavoriteService favoriteService,
            ICalculationHistoryRepository calculationHistoryRepository)
        {
            _activityLogService = activityLogService;
            _favoriteService = favoriteService;
            _calculationHistoryRepository = calculationHistoryRepository;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadAvatar(
            IFormFile avatarFile,
            CancellationToken cancellationToken)
        {
            var username = User.Identity?.Name ?? "anonymous";

            if (avatarFile == null || avatarFile.Length <= 0)
            {
                TempData["Error"] = "請選擇要上傳的頭像圖片。";
                return RedirectToAction("Profile");
            }

            if (avatarFile.Length > MaxAvatarBytes)
            {
                TempData["Error"] = "頭像圖片不可超過 2 MB。";
                return RedirectToAction("Profile");
            }

            if (!await IsJpegAsync(avatarFile, cancellationToken))
            {
                TempData["Error"] = "頭像目前只允許上傳 JPEG 圖片。";
                return RedirectToAction("Profile");
            }

            var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "avatars");
            Directory.CreateDirectory(uploadsFolder);

            var filePath = Path.Combine(uploadsFolder, BuildSafeAvatarFileName(username));
            await using (var source = avatarFile.OpenReadStream())
            await using (var destination = new FileStream(filePath, FileMode.Create))
            {
                await source.CopyToAsync(destination, cancellationToken);
            }

            await _activityLogService.AddAsync(
                username,
                "profile",
                "更新個人頭像",
                "上傳了一張新的個人資料圖片。",
                Url.Action("Profile", "User"),
                cancellationToken);

            return RedirectToAction("Profile");
        }

        public async Task<IActionResult> Profile(CancellationToken cancellationToken)
        {
            var username = User.Identity?.Name ?? string.Empty;
            var viewModel = new UserProfileViewModel
            {
                Username = username
            };

            try
            {
                viewModel.RecentCalculations = await _calculationHistoryRepository.GetRecentAsync(
                    username,
                    5,
                    cancellationToken);
                viewModel.RecentActivities = await _activityLogService.GetRecentAsync(
                    username,
                    5,
                    cancellationToken);
                viewModel.RecommendationFavorites = await _favoriteService.GetRecentAsync(
                    username,
                    6,
                    cancellationToken);
                viewModel.DbInfo = "資料服務正常";
            }
            catch (Exception ex)
            {
                viewModel.DbInfo = $"資料服務暫時異常：{ex.Message}";
            }

            return View(viewModel);
        }

        private static string BuildSafeAvatarFileName(string username)
        {
            var safeName = new string(username
                .Where(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.')
                .ToArray());

            return $"{(string.IsNullOrWhiteSpace(safeName) ? "anonymous" : safeName)}.jpg";
        }

        private static async Task<bool> IsJpegAsync(IFormFile file, CancellationToken cancellationToken)
        {
            if (!string.Equals(file.ContentType, "image/jpeg", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            await using var stream = file.OpenReadStream();
            var header = new byte[3];
            var read = await stream.ReadAsync(header, cancellationToken);

            return read == header.Length
                && header[0] == 0xFF
                && header[1] == 0xD8
                && header[2] == 0xFF;
        }
    }
}
