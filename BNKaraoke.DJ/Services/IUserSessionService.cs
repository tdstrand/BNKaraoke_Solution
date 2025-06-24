using BNKaraoke.DJ.Models;
using System;
using System.Collections.Generic;

namespace BNKaraoke.DJ.Services
{
    public interface IUserSessionService
    {
        bool IsAuthenticated { get; }
        string? Token { get; }
        string? FirstName { get; }
        string? UserName { get; }
        List<string>? Roles { get; }
        event EventHandler SessionChanged;
        void SetSession(LoginResult loginResult, string userName);
        void ClearSession();
    }
}