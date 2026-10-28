using System.ComponentModel.DataAnnotations;

namespace GreenHedges.HomeServer.Common.Web.Data;

/// <summary>
/// Audit columns shared by every table. <see cref="AuditedDbContext"/> sets the Added/Modified values when rows
/// are saved; <see cref="RowVersion"/> is a SQL Server <c>rowversion</c> that the database changes on
/// every insert and update, and EF Core uses it to detect conflicting edits.
/// </summary>
public abstract class AuditedEntity
{
    /// <summary>When the row was added (UTC).</summary>
    public DateTime AddedDate { get; set; }

    /// <summary>The Auth0 user id of whoever added the row.</summary>
    [Required, StringLength(128)]
    public string AddedBy { get; set; } = string.Empty;

    /// <summary>When the row was last changed (UTC); the same as <see cref="AddedDate"/> until then.</summary>
    public DateTime ModifiedDate { get; set; }

    /// <summary>The Auth0 user id of whoever last changed the row.</summary>
    [Required, StringLength(128)]
    public string ModifiedBy { get; set; } = string.Empty;

    [Timestamp]
    public byte[] RowVersion { get; set; } = [];
}
