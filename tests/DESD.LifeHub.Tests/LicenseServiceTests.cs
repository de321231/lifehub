using DESD.LifeHub.Core.Enums;
using DESD.LifeHub.Core.Licensing;
using Xunit;

namespace DESD.LifeHub.Tests;

/// <summary>
/// Kernfälle der Lizenzprüfung: gültig, unbekannt, abgelaufen, doppelt zugewiesen
/// sowie das Verhalten bei einem Ausfall des Lizenzservers (Kulanzfrist).
/// </summary>
public class LicenseServiceTests
{
    private const string Key = "X4F9K-ABCDE-FGHJK-MNPQR-STVWX";

    [Fact]
    public async Task Assign_WithValidKey_GrantsAccess()
    {
        using var h = new TestHarness();
        var user = h.AddUser("anna@example.de");
        h.Validator.Result = LicenseCheckResult.Success("desd-lifehub", "Standard", null, false, 1, 1, 200);

        var result = await h.Licenses.AssignAsync(user.Id, Key);
        var access = await h.Licenses.EvaluateAccessAsync(user.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(LicenseStatus.Valid, result.Status);
        Assert.True(access.Allowed);
        Assert.False(access.ServingFromLocalGrace);
    }

    [Fact]
    public async Task Assign_WithUnknownKey_DeniesAccess()
    {
        using var h = new TestHarness();
        var user = h.AddUser("anna@example.de");
        h.Validator.Result = LicenseCheckResult.Rejected(LicenseReasons.NotFound);

        var result = await h.Licenses.AssignAsync(user.Id, Key);
        var access = await h.Licenses.EvaluateAccessAsync(user.Id);

        Assert.False(result.Succeeded);
        Assert.Equal(LicenseStatus.NotFound, result.Status);
        Assert.False(access.Allowed);
    }

    [Fact]
    public async Task Assign_WithExpiredKey_DeniesAccess()
    {
        using var h = new TestHarness();
        var user = h.AddUser("anna@example.de");
        h.Validator.Result = LicenseCheckResult.Rejected(
            LicenseReasons.Expired, expiresAt: h.Clock.UtcNow.AddDays(-1));

        var result = await h.Licenses.AssignAsync(user.Id, Key);
        var access = await h.Licenses.EvaluateAccessAsync(user.Id);

        Assert.False(result.Succeeded);
        Assert.Equal(LicenseStatus.Expired, result.Status);
        Assert.False(access.Allowed);
    }

    [Fact]
    public async Task Assign_WithRevokedKey_DeniesAccess()
    {
        using var h = new TestHarness();
        var user = h.AddUser("anna@example.de");
        h.Validator.Result = LicenseCheckResult.Rejected(LicenseReasons.Revoked);

        var result = await h.Licenses.AssignAsync(user.Id, Key);
        var access = await h.Licenses.EvaluateAccessAsync(user.Id);

        Assert.False(result.Succeeded);
        Assert.Equal(LicenseStatus.Revoked, result.Status);
        Assert.False(access.Allowed);
    }

    [Fact]
    public async Task Assign_SameKeyToSecondUser_IsRejected()
    {
        using var h = new TestHarness();
        var first = h.AddUser("anna@example.de");
        var second = h.AddUser("bernd@example.de");
        h.Validator.Result = LicenseCheckResult.Success("desd-lifehub", "Standard", null, false, 1, 1, 200);

        var firstResult = await h.Licenses.AssignAsync(first.Id, Key);
        var secondResult = await h.Licenses.AssignAsync(second.Id, Key);

        Assert.True(firstResult.Succeeded);
        Assert.False(secondResult.Succeeded);
        Assert.Equal("duplicate_key", secondResult.FailureCode);

        // Der zweite Benutzer darf trotz gleichem Schlüssel keinen Zugriff erhalten.
        var secondAccess = await h.Licenses.EvaluateAccessAsync(second.Id);
        Assert.False(secondAccess.Allowed);
    }

    [Fact]
    public async Task Access_WhenServerUnreachableAndNeverValidated_DeniesAccess()
    {
        using var h = new TestHarness();
        var user = h.AddUser("anna@example.de");
        h.Validator.Result = LicenseCheckResult.Transient("Verbindungsfehler");

        var result = await h.Licenses.AssignAsync(user.Id, Key);
        var access = await h.Licenses.EvaluateAccessAsync(user.Id);

        Assert.False(result.Succeeded);
        Assert.Equal(LicenseStatus.Unreachable, result.Status);
        Assert.False(access.Allowed);
        Assert.False(access.ServingFromLocalGrace);
    }

    [Fact]
    public async Task Access_WhenServerUnreachableAfterValid_ServesFromGrace()
    {
        using var h = new TestHarness();
        var user = h.AddUser("anna@example.de");
        h.Validator.Result = LicenseCheckResult.Success("desd-lifehub", "Standard", null, false, 1, 1, 200);

        var assign = await h.Licenses.AssignAsync(user.Id, Key);
        Assert.True(assign.Succeeded);

        // Zwischenspeicherung ablaufen lassen, dann fällt der Server aus.
        h.Clock.Advance(TimeSpan.FromMinutes(h.Options.ValidationCacheMinutes + 1));
        h.Validator.Result = LicenseCheckResult.Transient("Server nicht erreichbar");

        var access = await h.Licenses.EvaluateAccessAsync(user.Id);

        Assert.True(access.Allowed);
        Assert.True(access.ServingFromLocalGrace);
    }

    [Fact]
    public async Task Access_WhenServerUnreachableBeyondGrace_DeniesAccess()
    {
        using var h = new TestHarness();
        var user = h.AddUser("anna@example.de");
        h.Validator.Result = LicenseCheckResult.Success("desd-lifehub", "Standard", null, false, 1, 1, 200);

        var assign = await h.Licenses.AssignAsync(user.Id, Key);
        Assert.True(assign.Succeeded);

        // Deutlich jenseits der lokalen Kulanzfrist fällt der Server aus.
        h.Clock.Advance(TimeSpan.FromHours(h.Options.GracePeriodHours + 1));
        h.Validator.Result = LicenseCheckResult.Transient("Server nicht erreichbar");

        var access = await h.Licenses.EvaluateAccessAsync(user.Id);

        Assert.False(access.Allowed);
        Assert.Equal(LicenseStatus.Unreachable, access.Status);
    }

    [Fact]
    public async Task Revalidate_WhenKeyExpires_UpdatesStatusAndDeniesAccess()
    {
        using var h = new TestHarness();
        var user = h.AddUser("anna@example.de");
        h.Validator.Result = LicenseCheckResult.Success("desd-lifehub", "Standard", null, false, 1, 1, 200);

        var assign = await h.Licenses.AssignAsync(user.Id, Key);
        Assert.True(assign.Succeeded);

        // Ab jetzt antwortet der Server mit "abgelaufen".
        h.Validator.Result = LicenseCheckResult.Rejected(LicenseReasons.Expired);

        var revalidated = await h.Licenses.RevalidateAsync(user.Id, force: true);
        var access = await h.Licenses.EvaluateAccessAsync(user.Id);

        Assert.False(revalidated.Succeeded);
        Assert.Equal(LicenseStatus.Expired, revalidated.Status);
        Assert.False(access.Allowed);
    }

    [Fact]
    public async Task Remove_DeletesAssignmentAndDeniesAccess()
    {
        using var h = new TestHarness();
        var user = h.AddUser("anna@example.de");
        h.Validator.Result = LicenseCheckResult.Success("desd-lifehub", "Standard", null, false, 1, 1, 200);

        await h.Licenses.AssignAsync(user.Id, Key);
        await h.Licenses.RemoveAsync(user.Id, actorUserId: user.Id, asAdmin: true);

        var access = await h.Licenses.EvaluateAccessAsync(user.Id);
        var status = await h.Licenses.GetStatusAsync(user.Id);

        Assert.False(access.Allowed);
        Assert.False(status.HasLicense);
        Assert.Equal(LicenseStatus.None, status.Status);
    }
}
