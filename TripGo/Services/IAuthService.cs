using TripGo.Models;
using TripGo.ViewModels;

namespace TripGo.Services
{
    public interface IAuthService
    {
        Task<User?> AuthenticateAsync(string identifier, string password);
        Task<bool> RegisterUserAsync(RegisterViewModel model);
    }
}