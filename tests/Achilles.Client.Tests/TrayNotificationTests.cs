using FluentAssertions;
using Achilles.Client.Agent;
using Xunit;

namespace Achilles.Client.Tests;

public sealed class TrayNotificationTests
{
    [Fact]
    public void TrayNotificationService_Notify_AppendsHistoryAndFiresEvent()
    {
        var svc = new TrayNotificationService();
        TrayNotification? received = null;
        svc.NotificationRaised += n => received = n;

        svc.Notify("Test Title", "Test Message", TrayNotificationLevel.Info);

        received.Should().NotBeNull();
        received!.Title.Should().Be("Test Title");
        received.Message.Should().Be("Test Message");
        received.Level.Should().Be(TrayNotificationLevel.Info);

        svc.History.Should().HaveCount(1);
        svc.History[0].Title.Should().Be("Test Title");
    }

    [Fact]
    public void TrayNotificationService_Helpers_EmitExpectedEvents()
    {
        var svc = new TrayNotificationService();

        svc.NotifyLeaseAcquired("lease-123", 4, DateTimeOffset.UtcNow.AddMinutes(30));
        svc.NotifyExpiringSoon("lease-123", TimeSpan.FromMinutes(5));
        svc.NotifyNetworkFailureGrace(TimeSpan.FromHours(12));
        svc.NotifyOfflineBorrowActive("lease-123", 7);
        svc.NotifyLeaseReleased("lease-123");

        svc.History.Should().HaveCount(5);
        svc.History[0].Level.Should().Be(TrayNotificationLevel.Success);
        svc.History[0].Title.Should().Contain("Licencia Aktívna");
        svc.History[1].Level.Should().Be(TrayNotificationLevel.Warning);
        svc.History[2].Level.Should().Be(TrayNotificationLevel.Warning);
        svc.History[3].Level.Should().Be(TrayNotificationLevel.Info);
        svc.History[4].Level.Should().Be(TrayNotificationLevel.Info);
        svc.History[4].Title.Should().Contain("Uvoľnené");
    }

    [Fact]
    public void TrayNotificationService_History_CapsAt100()
    {
        var svc = new TrayNotificationService();
        for (int i = 0; i < 110; i++)
        {
            svc.Notify($"Title {i}", $"Message {i}", TrayNotificationLevel.Info);
        }

        svc.History.Should().HaveCount(100);
        svc.History[^1].Title.Should().Be("Title 109");
    }
}
