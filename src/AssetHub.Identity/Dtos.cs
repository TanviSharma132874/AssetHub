namespace AssetHub.Identity;

public record SignupRequest(string FullName, string Email, string Password, int? DepartmentId, string? Designation, string? Phone);
public record LoginRequest(string Email, string Password);
public record RoleChangeRequest(string Role);
public record UpdateUserRequest(string FullName, int? DepartmentId, string? Designation, string? Phone, bool IsActive);

public record UserDto(
    int Id, string FullName, string Email, string Role,
    int? DepartmentId, string? DepartmentName, string? Designation, string? Phone,
    bool IsActive, DateTime CreatedAt)
{
    // Department must be loaded (Include) for DepartmentName to be filled.
    public static UserDto From(User u) => new(
        u.Id, u.FullName, u.Email, u.Role, u.DepartmentId, u.Department?.Name,
        u.Designation, u.Phone, u.IsActive, u.CreatedAt);
}

public record AuthResponse(string Token, DateTime ExpiresAt, UserDto User);

public record DepartmentRequest(string Name, string Code, string? Description);
public record DepartmentDto(int Id, string Name, string Code, string? Description, int EmployeeCount);

public record CategoryRequest(string Name, string Prefix, string? Description);
public record CategoryDto(int Id, string Name, string Prefix, string? Description)
{
    public static CategoryDto From(AssetCategory c) => new(c.Id, c.Name, c.Prefix, c.Description);
}
