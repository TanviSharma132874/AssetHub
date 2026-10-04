namespace AssetHub.Assets;

// ---------- requests ----------
public record RegisterAssetRequest(string Name, int CategoryId, string? SerialNumber, DateTime? PurchaseDate, double? PurchaseCost, string? Location, string? Notes);
public record UpdateAssetRequest(string Name, string? SerialNumber, DateTime? PurchaseDate, double? PurchaseCost, string? Location, string? Notes);
public record AllocateRequest(int AssetId, int EmployeeId, DateTime? ExpectedReturnDate, string? Notes);
public record ReturnRequest(string? Notes);
public record TransferCreateRequest(int AssetId, int ToEmployeeId, string? Reason);
public record DecisionRequest(string? Note);
public record MaintenanceCreateRequest(int AssetId, string Issue, MaintenancePriority Priority);
public record MaintenanceCompleteRequest(string? Note, bool Retire);

// ---------- responses ----------
public record AssetDto(
    int Id, string AssetTag, string Name, int CategoryId, string CategoryName,
    string? SerialNumber, DateTime? PurchaseDate, double? PurchaseCost, string? Location, string? Notes,
    AssetStatus Status, int? CurrentHolderId, string? CurrentHolderName, DateTime CreatedAt)
{
    public static AssetDto From(Asset a, Allocation? active) => new(
        a.Id, a.AssetTag, a.Name, a.CategoryId, a.CategoryName, a.SerialNumber, a.PurchaseDate,
        a.PurchaseCost, a.Location, a.Notes, a.Status, active?.EmployeeId, active?.EmployeeName, a.CreatedAt);
}

public record AllocationDto(
    int Id, int AssetId, string AssetTag, string AssetName, int EmployeeId, string EmployeeName,
    string? EmployeeDepartment, DateTime AllocatedAt, DateTime? ExpectedReturnDate, DateTime? ReturnedAt,
    string? Notes, string? ReturnNotes, string AllocatedByName)
{
    public static AllocationDto From(Allocation a, Asset asset) => new(
        a.Id, a.AssetId, asset.AssetTag, asset.Name, a.EmployeeId, a.EmployeeName, a.EmployeeDepartment,
        a.AllocatedAt, a.ExpectedReturnDate, a.ReturnedAt, a.Notes, a.ReturnNotes, a.AllocatedByName);
}

public record TransferDto(
    int Id, int AssetId, string AssetTag, string AssetName,
    int FromEmployeeId, string FromEmployeeName, int ToEmployeeId, string ToEmployeeName,
    string? Reason, TransferStatus Status, string RequestedByName, DateTime RequestedAt,
    string? DecidedByName, DateTime? DecidedAt, string? DecisionNote)
{
    public static TransferDto From(TransferRequest t, Asset a) => new(
        t.Id, t.AssetId, a.AssetTag, a.Name, t.FromEmployeeId, t.FromEmployeeName, t.ToEmployeeId, t.ToEmployeeName,
        t.Reason, t.Status, t.RequestedByName, t.RequestedAt, t.DecidedByName, t.DecidedAt, t.DecisionNote);
}

public record MaintenanceDto(
    int Id, int AssetId, string AssetTag, string AssetName, string Issue,
    MaintenancePriority Priority, MaintenanceStatus Status, string RequestedByName, DateTime RequestedAt,
    string? DecidedByName, DateTime? DecidedAt, string? DecisionNote, DateTime? CompletedAt, string? CompletionNote)
{
    public static MaintenanceDto From(MaintenanceRequest m, Asset a) => new(
        m.Id, m.AssetId, a.AssetTag, a.Name, m.Issue, m.Priority, m.Status, m.RequestedByName, m.RequestedAt,
        m.DecidedByName, m.DecidedAt, m.DecisionNote, m.CompletedAt, m.CompletionNote);
}

public record AssetDetailDto(AssetDto Asset, List<AllocationDto> History, List<MaintenanceDto> Maintenance);

public record DashboardDto(
    Dictionary<string, int> AssetsByStatus, int TotalAssets,
    int PendingTransfers, int PendingMaintenance, int MyAllocations);
