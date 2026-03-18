using prototype1.Data;
using prototype1.Models;
using Microsoft.EntityFrameworkCore;
using BCrypt.Net;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace prototype1.Services;

public class AuthenticationService
{
    private readonly ApplicationDbContext _context;
    private readonly ProtectedSessionStorage _sessionStorage;
    private User? _currentUser;
    private bool _initialized = false;

    public AuthenticationService(ApplicationDbContext context, ProtectedSessionStorage sessionStorage)
    {
        _context = context;
        _sessionStorage = sessionStorage;
    }

    public User? CurrentUser => _currentUser;
    public bool IsAuthenticated => _currentUser != null;

    public event Action? OnAuthenticationStateChanged;

    public async Task InitializeAsync()
    {
        if (_initialized)
            return;

        _initialized = true;

        try
        {
            var result = await _sessionStorage.GetAsync<int>("userId");
            if (result.Success)
            {
                _currentUser = await _context.Users.FindAsync(result.Value);
                OnAuthenticationStateChanged?.Invoke();
            }
        }
        catch
        {
            // Session storage not available yet
        }
    }

    public async Task<(bool Success, string Message)> RegisterAsync(string username, string email, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || username.Length < 3)
            return (false, "Username must be at least 3 characters long.");

        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            return (false, "Please enter a valid email address.");

        if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
            return (false, "Password must be at least 6 characters long.");

        var existingUser = await _context.Users
            .FirstOrDefaultAsync(u => u.Username == username || u.Email == email);

        if (existingUser != null)
            return (false, "Username or email already exists.");

        var user = new User
        {
            Username = username,
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            CreatedAt = DateTime.UtcNow
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        return (true, "Registration successful!");
    }

    public async Task<(bool Success, string Message)> LoginAsync(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return (false, "Please enter both username and password.");

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Username == username);

        if (user == null || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            return (false, "Invalid username or password.");

        _currentUser = user;
        await _sessionStorage.SetAsync("userId", user.Id);
        OnAuthenticationStateChanged?.Invoke();

        return (true, "Login successful!");
    }

    public async Task LogoutAsync()
    {
        _currentUser = null;
        await _sessionStorage.DeleteAsync("userId");
        OnAuthenticationStateChanged?.Invoke();
    }
}