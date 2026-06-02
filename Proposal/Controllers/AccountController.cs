using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Proposal.Services;

namespace Proposal.Controllers
{
    public class AccountController : Controller
    {
        private readonly IConfiguration _config;
        private readonly IWebHostEnvironment _environment;
        private readonly IUserAccountRepository _accountRepository;
        private readonly IPasswordHashService _passwordHashService;

        public AccountController(
            IConfiguration config,
            IWebHostEnvironment environment,
            IUserAccountRepository accountRepository,
            IPasswordHashService passwordHashService)
        {
            _config = config;
            _environment = environment;
            _accountRepository = accountRepository;
            _passwordHashService = passwordHashService;
        }

        [HttpGet]
        public IActionResult Login()
        {
            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                return RedirectToAction("Index", "Home");
            }

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(
            string username,
            string password,
            CancellationToken cancellationToken)
        {
            username = username?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                ViewBag.Error = "請輸入帳號與密碼。";
                return View();
            }

            var storedPassword = await _accountRepository.GetPasswordHashAsync(username, cancellationToken);
            if (!_passwordHashService.VerifyPassword(password, storedPassword, out var needsRehash))
            {
                ViewBag.Error = "帳號或密碼錯誤，請重新輸入！";
                return View();
            }

            if (needsRehash)
            {
                var upgradedHash = _passwordHashService.HashPassword(password);
                await _accountRepository.UpdatePasswordHashAsync(username, upgradedHash, cancellationToken);
            }

            var role = IsAdminUser(username) ? "Admin" : "User";
            var claims = new List<Claim>
            {
                new(ClaimTypes.Name, username),
                new(ClaimTypes.Role, role),
                new("Role", role)
            };

            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(claimsIdentity));

            return RedirectToAction("Index", "Home");
        }

        [HttpGet]
        public IActionResult Register()
        {
            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                return RedirectToAction("Index", "Home");
            }

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(
            string username,
            string password,
            string confirmPassword,
            CancellationToken cancellationToken)
        {
            username = username?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(username))
            {
                ViewBag.Error = "請輸入帳號名稱。";
                return View();
            }

            if (!IsValidUsername(username))
            {
                ViewBag.Error = "帳號只能使用 3 到 40 個英文字母、數字、底線、連字號或小數點。";
                return View();
            }

            if (password != confirmPassword)
            {
                ViewBag.Error = "兩次輸入的密碼不一致，請重新確認！";
                return View();
            }

            if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
            {
                ViewBag.Error = "密碼至少需要 8 碼，請提高帳號安全性。";
                return View();
            }

            if (await _accountRepository.UsernameExistsAsync(username, cancellationToken))
            {
                ViewBag.Error = "這個帳號已經被註冊過了，請換一個名稱！";
                return View();
            }

            var passwordHash = _passwordHashService.HashPassword(password);
            await _accountRepository.CreateUserAsync(username, passwordHash, cancellationToken);

            TempData["SuccessMsg"] = "帳號註冊成功！請使用新帳號登入。";
            return RedirectToAction("Login");
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login");
        }

        [HttpGet]
        public IActionResult AccessDenied()
        {
            return View();
        }

        private bool IsAdminUser(string username)
        {
            var configuredAdminUsers = _config.GetSection("AdminUsers").Get<string[]>()
                ?? SplitAdminUsers(_config["AdminUsers"])
                ?? SplitAdminUsers(Environment.GetEnvironmentVariable("APP_ADMIN_USERS"));

            if (configuredAdminUsers is null)
            {
                configuredAdminUsers = _environment.IsProduction()
                    ? Array.Empty<string>()
                    : new[] { "Peng", "admin" };
            }

            return configuredAdminUsers.Any(adminUser =>
                string.Equals(adminUser.Trim(), username.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsValidUsername(string username)
        {
            if (username.Length is < 3 or > 40)
            {
                return false;
            }

            return username.All(character =>
                char.IsAsciiLetterOrDigit(character)
                || character is '_' or '-' or '.');
        }

        private static string[]? SplitAdminUsers(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? null
                : value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
    }
}
