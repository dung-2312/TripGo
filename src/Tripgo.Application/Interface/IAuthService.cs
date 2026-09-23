using Tripgo.Application.DTO;
using Tripgo.Domain.Entities;

namespace Tripgo.Application.Interfaces;

public interface IAuthService
{
    Task<User?> AuthenticateAsync(string identifier, string password);
    Task<bool> RegisterUserAsync(RegisterViewModel model);
}