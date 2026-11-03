using GreenHedges.HomeServer.Collectors.Libre.LibreLinkUp;

namespace GreenHedges.HomeServer.Collectors.Libre.Worker.Tests;

public sealed class NoConnectionsGraceTests
{
    private static readonly TimeSpan Grace = TimeSpan.FromMinutes(10);

    private static DateTimeOffset At(int minute) => new(2026, 11, 3, 11, minute, 0, TimeSpan.Zero);

    [Fact]
    public void A_single_empty_list_is_transient()
    {
        // Seen in production: one poll returned no connections between normal readings.
        var grace = new NoConnectionsGrace(Grace);

        var error = grace.Empty(At(9));

        Assert.IsNotAssignableFrom<LibreLinkUpUserActionRequiredException>(error);
    }

    [Fact]
    public void Empty_lists_within_the_grace_period_stay_transient()
    {
        var grace = new NoConnectionsGrace(Grace);
        grace.Empty(At(0));

        Assert.IsNotAssignableFrom<LibreLinkUpUserActionRequiredException>(grace.Empty(At(9)));
    }

    [Fact]
    public void Empty_lists_for_the_whole_grace_period_need_the_user()
    {
        var grace = new NoConnectionsGrace(Grace);
        grace.Empty(At(0));

        Assert.IsType<NoConnectionsException>(grace.Empty(At(10)));
    }

    [Fact]
    public void Connections_coming_back_start_a_new_grace_period()
    {
        var grace = new NoConnectionsGrace(Grace);
        grace.Empty(At(0));
        grace.Reset();

        Assert.IsNotAssignableFrom<LibreLinkUpUserActionRequiredException>(grace.Empty(At(15)));
    }

    [Fact]
    public void No_grace_period_needs_the_user_at_once()
    {
        var grace = new NoConnectionsGrace(TimeSpan.Zero);

        Assert.IsType<NoConnectionsException>(grace.Empty(At(0)));
    }
}
