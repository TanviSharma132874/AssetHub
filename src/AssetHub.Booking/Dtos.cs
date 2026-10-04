namespace AssetHub.Booking;

// ---------- requests ----------
public record ResourceRequest(string Name, string Type, string? Location, int? Capacity, string? Description, bool IsActive);
public record BookingRequest(int ResourceId, string Title, DateTime StartTime, DateTime EndTime);
public record AuditCreateRequest(string Name, string? Description, DateTime? DueDate, int? CategoryId);
public record AuditVerifyRequest(string Status, string? Remarks);
public record AuditCloseRequest(bool Force);

// ---------- responses ----------
public record ResourceDto(int Id, string Name, string Type, string? Location, int? Capacity, string? Description, bool IsActive)
{
    public static ResourceDto From(Resource r) => new(r.Id, r.Name, r.Type, r.Location, r.Capacity, r.Description, r.IsActive);
}

public record BookingDto(
    int Id, int ResourceId, string ResourceName, int BookedById, string BookedByName, string Title,
    DateTime StartTime, DateTime EndTime, BookingStatus Status, DateTime CreatedAt)
{
    public static BookingDto From(Booking b, Resource r) => new(
        b.Id, b.ResourceId, r.Name, b.BookedById, b.BookedByName, b.Title, b.StartTime, b.EndTime, b.Status, b.CreatedAt);
}

public record AuditCycleDto(
    int Id, string Name, string? Description, string? CategoryFilterName, DateTime? DueDate, AuditStatus Status,
    string CreatedByName, DateTime CreatedAt, DateTime? ClosedAt,
    int Total, int Pending, int Verified, int Missing, int Damaged);

public record AuditItemDto(
    int Id, int AssetId, string AssetTag, string AssetName, string? CategoryName, string? Location,
    AuditItemStatus Status, string? Remarks, string? VerifiedByName, DateTime? VerifiedAt)
{
    public static AuditItemDto From(AuditItem i) => new(
        i.Id, i.AssetId, i.AssetTag, i.AssetName, i.CategoryName, i.Location,
        i.Status, i.Remarks, i.VerifiedByName, i.VerifiedAt);
}

public record AuditDetailDto(AuditCycleDto Cycle, List<AuditItemDto> Items);

public record NotificationDto(int Id, string Title, string Message, string Type, bool IsRead, DateTime CreatedAt)
{
    public static NotificationDto From(Notification n) => new(n.Id, n.Title, n.Message, n.Type, n.IsRead, n.CreatedAt);
}

// Shape of Module B's asset list, as consumed here.
public record AssetInfo(int Id, string AssetTag, string Name, int CategoryId, string CategoryName, string? Location, string Status);
