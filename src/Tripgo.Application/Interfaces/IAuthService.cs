using System.Threading.Tasks;
using Tripgo.Application.DTOs;
using Tripgo.Domain.Entities;

namespace Tripgo.Application.Interfaces;

public interface IAuthService
{
    Task<User?> AuthenticateAsync(string identifier, string password);
    Task<bool> RegisterUserAsync(RegisterViewModel model);
}
