namespace GreenHedges.HomeServer.VehicleManager.Monitoring;

/// <summary>The scheduled jobs whose runs are watched (scheduled in <c>Program.cs</c>).</summary>
public enum ScheduledJob
{
    /// <summary><see cref="Services.ScheduledReminders"/>, each morning at 07:00 UK time.</summary>
    MorningReminders,
}
