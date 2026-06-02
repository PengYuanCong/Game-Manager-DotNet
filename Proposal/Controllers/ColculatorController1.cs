using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Proposal.Models;
using Proposal.Services;

namespace Proposal.Controllers
{
    [Authorize]
    public class CalculatorController : Controller
    {
        private readonly IUserActivityLogService _activityLogService;
        private readonly ICalculationHistoryRepository _calculationHistoryRepository;
        private readonly ICalculatorDataRepository _calculatorDataRepository;

        public CalculatorController(
            IUserActivityLogService activityLogService,
            ICalculationHistoryRepository calculationHistoryRepository,
            ICalculatorDataRepository calculatorDataRepository)
        {
            _activityLogService = activityLogService;
            _calculationHistoryRepository = calculationHistoryRepository;
            _calculatorDataRepository = calculatorDataRepository;
        }

        [HttpGet]
        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            var username = CurrentUsername();
            var myEquipments = await _calculatorDataRepository.GetEquipmentOptionsAsync(username, cancellationToken);
            var myLoadouts = await _calculatorDataRepository.GetLoadoutOptionsAsync(username, cancellationToken);

            ViewBag.EquipmentList = myEquipments.ToList();
            ViewBag.LoadoutList = myLoadouts.ToList();
            return View();
        }

        // 3. 新增：API Action，讓前端點選組合後，能抓取「總合數值」
        [HttpGet]
        public async Task<IActionResult> GetLoadoutStats(int loadoutId, CancellationToken cancellationToken)
        {
            var stats = await _calculatorDataRepository.GetLoadoutStatsAsync(
                CurrentUsername(),
                loadoutId,
                cancellationToken);

            return stats is null ? NotFound() : Json(stats);
        }

        // --- 你原本的 Calculate 和 SaveRecord 保持不變 ---
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Calculate(string formulaType, double param1, double param2, double param3)
        {
            var message = "請使用頁面上的公式工具進行計算。";
            ViewBag.Result = message;
            ViewBag.FormulaType = formulaType;
            return View("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveRecord(
            string type,
            string inputs,
            string result,
            CancellationToken cancellationToken)
        {
            var username = User.Identity?.Name ?? "anonymous";
            await _calculationHistoryRepository.AddAsync(
                username,
                type ?? string.Empty,
                inputs ?? string.Empty,
                result ?? string.Empty,
                cancellationToken);

            await _activityLogService.AddAsync(
                username,
                "calculator",
                $"使用公式：{type}",
                result ?? string.Empty,
                Url.Action("Index", "Calculator"),
                cancellationToken);

            return Json(new { success = true });
        }

        private string CurrentUsername()
        {
            return User.Identity?.Name ?? "anonymous";
        }
    }
}
