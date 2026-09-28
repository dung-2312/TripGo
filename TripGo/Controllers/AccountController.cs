using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TripGo.Models;
using TripGo.ViewModels;

namespace TripGo.Controllers
{
    public class AccountController : Controller
    {
        private readonly TripGoDbContext _context;

        public AccountController(TripGoDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
        {
            if (!ModelState.IsValid) return View(model);

            // Tìm tài khoản theo Email hoặc SĐT
            var user = await _context.Users
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => (u.Email == model.AccountIdentifier || u.PhoneNumber == model.AccountIdentifier)
                                       && u.PasswordHash == model.Password);

            if (user == null)
            {
                ModelState.AddModelError(string.Empty, "Tài khoản hoặc mật khẩu không chính xác!");
                return View(model);
            }

            // Gán thông tin đăng nhập
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                new Claim(ClaimTypes.Name, user.FullName),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Role, user.Role?.RoleName ?? "User")
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

            // Rẽ nhánh: Nếu là Admin -> Vào trang Admin Dashboard
            if (user.Role?.RoleName == "Admin")
            {
                return RedirectToAction("Index", "Dashboard", new { area = "Admin" });
            }

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            return RedirectToAction("Index", "Home");
        }

        [HttpGet]
        public IActionResult Register()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var isExist = await _context.Users.AnyAsync(u => u.Email == model.Email || u.PhoneNumber == model.PhoneNumber);
            if (isExist)
            {
                ModelState.AddModelError(string.Empty, "Email hoặc số điện thoại này đã được sử dụng!");
                return View(model);
            }

            var userRole = await _context.Roles.FirstOrDefaultAsync(r => r.RoleName == "User");

            var newUser = new User
            {
                FullName = model.FullName,
                Email = model.Email,
                PhoneNumber = model.PhoneNumber,
                PasswordHash = model.Password,
                RoleId = userRole != null ? userRole.RoleId : 2
            };

            _context.Users.Add(newUser);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Đăng ký thành công! Hãy đăng nhập.";
            return RedirectToAction("Login");
        }

        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Index", "Home");
        }

        public IActionResult AccessDenied()
        {
            return View();
        }
    }
}