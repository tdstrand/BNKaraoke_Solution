using System.Threading.Tasks;
using BNKaraoke.DJ.Models;

namespace BNKaraoke.DJ.Services;

public interface IAuthService
{
    Task<LoginResult> LoginAsync(string phoneNumber, string password);
}