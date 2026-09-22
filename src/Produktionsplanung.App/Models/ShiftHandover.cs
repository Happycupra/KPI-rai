namespace Produktionsplanung.App.Models;

/// <summary>
/// Persistent handover item between production shifts.
/// Open items remain visible until explicitly acknowledged/resolved.
/// </summary>
public class ShiftHandover
{
    public int Id { get; set; }
    public DateTime HandoverDate { get; set; } = DateTime.Today;
    public int? FromShiftId { get; set; }
    public Shift? FromShift { get; set; }
    public int? ToShiftId { get; set; }
    public Shift? ToShift { get; set; }
    public int? WorkstationId { get; set; }
    public Workstation? Workstation { get; set; }
    public int? ProductionOrderId { get; set; }
    public ProductionOrder? ProductionOrder { get; set; }
    public string Priority { get; set; } = "Normal";
    public string Subject { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public string Status { get; set; } = "Offen";
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string? AcknowledgedBy { get; set; }
    public DateTime? AcknowledgedAtUtc { get; set; }
    public string? Resolution { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
}
