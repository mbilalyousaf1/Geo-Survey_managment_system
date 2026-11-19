using System.Reflection;
using GeoSurvey.Identity.Infrastructure.SignIn;

namespace GeoSurvey.Identity.Infrastructure.Tests;

public sealed class PlatformSessionStartResultTests
{
    private static readonly DateTimeOffset Created = new(2026, 11, 18, 0, 0, 0, TimeSpan.Zero);
    private static readonly string Token = new('A', 43);

    [Fact]
    public void StartedResult_IsReadOnlyAndDoesNotPrintCredentialOrSubjectData()
    {
        var account = Guid.NewGuid();
        var session = Guid.NewGuid();
        var ticket = new PlatformSessionTicket(account, session, Token, Created, Created.AddMinutes(30), Created.AddHours(8));
        var result = PlatformSessionStartResult.Started(ticket);
        Assert.True(result.SessionCreated);
        Assert.Same(ticket, result.Ticket);
        foreach (var value in new[] { Token, account.ToString(), session.ToString() })
        {
            Assert.DoesNotContain(value, result.ToString()!);
            Assert.DoesNotContain(value, ticket.ToString()!);
        }
        var resultProperties = typeof(PlatformSessionStartResult).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        Assert.Equal(new[] { "SessionCreated", "Ticket" }, resultProperties.Select(property => property.Name).Order(StringComparer.Ordinal));
        var ticketProperties = typeof(PlatformSessionTicket).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        Assert.Equal(new[] { "AccountPublicId", "CreatedAtUtc", "RefreshExpiresAtUtc", "RefreshToken", "SessionExpiresAtUtc", "SessionPublicId" },
            ticketProperties.Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.All(resultProperties.Concat(ticketProperties), property => Assert.False(property.CanWrite));
    }

    [Fact]
    public void RejectedResult_ContainsNoTicket_AndStartedRequiresATicket()
    {
        Assert.False(PlatformSessionStartResult.Rejected.SessionCreated);
        Assert.Null(PlatformSessionStartResult.Rejected.Ticket);
        Assert.Throws<ArgumentNullException>(() => PlatformSessionStartResult.Started(null!));
    }

    [Fact]
    public void StartContract_AcceptsOnlyChallengeCodeAndCancellation()
    {
        var parameters = typeof(IPlatformSessionStarter).GetMethod(nameof(IPlatformSessionStarter.StartAsync))!.GetParameters();
        Assert.Equal(new[] { typeof(string), typeof(string), typeof(CancellationToken) }, parameters.Select(parameter => parameter.ParameterType));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Ticket_RejectsEmptyPublicIdentifiers(bool emptyAccount)
    {
        Assert.Throws<ArgumentException>(() => new PlatformSessionTicket(emptyAccount ? Guid.Empty : Guid.NewGuid(),
            emptyAccount ? Guid.NewGuid() : Guid.Empty, Token, Created, Created.AddMinutes(30), Created.AddHours(8)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sensitive-invalid-refresh-value")]
    [InlineData("!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!")]
    public void InvalidTokenShape_IsRejectedWithoutEchoingInput(string? token)
    {
        var error = Assert.Throws<ArgumentException>(() => new PlatformSessionTicket(Guid.NewGuid(), Guid.NewGuid(), token!, Created, Created.AddMinutes(30), Created.AddHours(8)));
        if (!string.IsNullOrEmpty(token))
            Assert.DoesNotContain(token, error.Message);
    }

    [Theory]
    [InlineData("refresh-at-creation")]
    [InlineData("refresh-before-creation")]
    [InlineData("absolute-at-creation")]
    [InlineData("refresh-after-absolute")]
    [InlineData("creation-offset")]
    [InlineData("refresh-offset")]
    [InlineData("absolute-offset")]
    public void Ticket_RejectsInvalidUtcOrExpiryBounds(string change)
    {
        var created = Created;
        var refresh = Created.AddMinutes(30);
        var absolute = Created.AddHours(8);
        switch (change)
        {
            case "refresh-at-creation": refresh = created; break;
            case "refresh-before-creation": refresh = created.AddTicks(-1); break;
            case "absolute-at-creation": absolute = created; break;
            case "refresh-after-absolute": refresh = absolute.AddTicks(1); break;
            case "creation-offset": created = created.ToOffset(TimeSpan.FromHours(5)); break;
            case "refresh-offset": refresh = refresh.ToOffset(TimeSpan.FromHours(5)); break;
            case "absolute-offset": absolute = absolute.ToOffset(TimeSpan.FromHours(5)); break;
        }
        var error = Assert.Throws<ArgumentException>(() => new PlatformSessionTicket(Guid.NewGuid(), Guid.NewGuid(), Token, created, refresh, absolute));
        Assert.DoesNotContain(Token, error.Message);
    }
}
