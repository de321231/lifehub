using DESD.LifeHub.Core.Enums;
using DESD.LifeHub.Core.Licensing;
using Xunit;

namespace DESD.LifeHub.Tests;

/// <summary>
/// Firmen-Kontingente: Erzeugung über die (simulierte) OpenLicensd-Admin-API,
/// Zuweisung an Mitarbeiter und Freigabe zurück in den Pool.
/// </summary>
public class CompanyLicenseServiceTests
{
    private static (TestHarness Harness, Guid OrgId, Guid AdminId, Guid EmployeeId) Setup()
    {
        var h = new TestHarness();
        var admin = h.AddUser("chef@acme.de");
        var employee = h.AddUser("mitarbeiter@acme.de");
        var org = h.AddOrganization("Acme GmbH", "acme", admin.Id);
        h.AddMember(org.Id, admin.Id, OrganizationRole.Owner);
        h.AddMember(org.Id, employee.Id, OrganizationRole.Member);

        h.Validator.Result = LicenseCheckResult.Success("desd-lifehub", "Standard", null, false, 1, 1, 200);

        return (h, org.Id, admin.Id, employee.Id);
    }

    [Fact]
    public async Task Generate_CreatesKeysInPool_AndNeverStoresPlaintext()
    {
        var (h, orgId, adminId, _) = Setup();
        using var _h = h;

        var result = await h.Company.GenerateAsync(orgId, 3, adminId);

        Assert.True(result.Succeeded);
        Assert.Equal(3, result.Created);

        var summary = await h.Company.GetPoolSummaryAsync(orgId);
        Assert.Equal(3, summary.Total);
        Assert.Equal(0, summary.Assigned);
        Assert.Equal(3, summary.Free);

        // Der Rohschlüssel darf niemals im Klartext in der Datenbank stehen.
        var stored = await h.Company.ListAsync(orgId);
        Assert.All(stored, row => Assert.False(string.IsNullOrWhiteSpace(row.MaskedKey)));

        foreach (var issued in h.Admin.IssuedKeys)
        {
            var plaintextMatch = h.Db.CompanyLicenses.Any(l => l.LicenseKeyCiphertext.Contains(issued));
            Assert.False(plaintextMatch);
        }
    }

    [Fact]
    public async Task Generate_WithoutAdminApi_Fails()
    {
        var (h, orgId, adminId, _) = Setup();
        using var _h = h;
        h.Admin.IsConfiguredValue = false;

        var result = await h.Company.GenerateAsync(orgId, 2, adminId);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.FatalError);
        Assert.Equal(0, result.Created);
    }

    [Fact]
    public async Task Assign_ToEmployee_ActivatesAndMarksPoolEntry()
    {
        var (h, orgId, adminId, employeeId) = Setup();
        using var _h = h;

        await h.Company.GenerateAsync(orgId, 1, adminId);
        var license = (await h.Company.ListAsync(orgId)).Single();

        var assign = await h.Company.AssignAsync(orgId, license.Id, employeeId, adminId);
        Assert.True(assign.Succeeded);

        var summary = await h.Company.GetPoolSummaryAsync(orgId);
        Assert.Equal(1, summary.Assigned);
        Assert.Equal(0, summary.Free);

        var employees = await h.Company.GetEmployeesAsync(orgId);
        var employee = employees.Single(e => e.UserId == employeeId);
        Assert.True(employee.HasValidLicense);
        Assert.True(employee.FromCompanyPool);

        var access = await h.Licenses.EvaluateAccessAsync(employeeId);
        Assert.True(access.Allowed);

        // Erneute Zuweisung derselben Lizenz ist nicht möglich.
        var again = await h.Company.AssignAsync(orgId, license.Id, employeeId, adminId);
        Assert.False(again.Succeeded);
    }

    [Fact]
    public async Task Assign_ToNonMember_IsRejected()
    {
        var (h, orgId, adminId, _) = Setup();
        using var _h = h;

        var stranger = h.AddUser("fremd@other.de");
        await h.Company.GenerateAsync(orgId, 1, adminId);
        var license = (await h.Company.ListAsync(orgId)).Single();

        var result = await h.Company.AssignAsync(orgId, license.Id, stranger.Id, adminId);

        Assert.False(result.Succeeded);
        var summary = await h.Company.GetPoolSummaryAsync(orgId);
        Assert.Equal(0, summary.Assigned);
        Assert.Equal(1, summary.Free);
    }

    [Fact]
    public async Task Release_ReturnsLicenseToPoolAndRemovesUserLicense()
    {
        var (h, orgId, adminId, employeeId) = Setup();
        using var _h = h;

        await h.Company.GenerateAsync(orgId, 1, adminId);
        var license = (await h.Company.ListAsync(orgId)).Single();
        await h.Company.AssignAsync(orgId, license.Id, employeeId, adminId);

        var release = await h.Company.ReleaseAsync(orgId, license.Id, adminId);
        Assert.True(release.Succeeded);

        var summary = await h.Company.GetPoolSummaryAsync(orgId);
        Assert.Equal(0, summary.Assigned);
        Assert.Equal(1, summary.Free);

        var access = await h.Licenses.EvaluateAccessAsync(employeeId);
        Assert.False(access.Allowed);
    }

    [Fact]
    public async Task Revoke_RemovesAssignmentMarksRevokedAndCallsRemoteRevoke()
    {
        var (h, orgId, adminId, employeeId) = Setup();
        using var _h = h;

        await h.Company.GenerateAsync(orgId, 1, adminId);
        var license = (await h.Company.ListAsync(orgId)).Single();
        await h.Company.AssignAsync(orgId, license.Id, employeeId, adminId);

        var revoke = await h.Company.RevokeAsync(orgId, license.Id, adminId);
        Assert.True(revoke.Succeeded);

        var summary = await h.Company.GetPoolSummaryAsync(orgId);
        Assert.Equal(1, summary.Revoked);
        Assert.Equal(0, summary.Assigned);
        Assert.Equal(0, summary.Free);

        Assert.Contains("lic-1", h.Admin.RevokedRemoteIds);

        var access = await h.Licenses.EvaluateAccessAsync(employeeId);
        Assert.False(access.Allowed);
    }

    [Fact]
    public async Task OperatorOverview_ReportsMembersAndPoolCounts()
    {
        var (h, orgId, adminId, employeeId) = Setup();
        using var _h = h;

        await h.Company.GenerateAsync(orgId, 2, adminId);
        var license = (await h.Company.ListAsync(orgId)).First();
        await h.Company.AssignAsync(orgId, license.Id, employeeId, adminId);

        var overview = await h.Company.GetOperatorOverviewAsync();
        var row = overview.Single(o => o.OrganizationId == orgId);

        Assert.Equal(2, row.MemberCount);
        Assert.Equal(1, row.LicensedUsers);
        Assert.Equal(2, row.PoolTotal);
        Assert.Equal(1, row.PoolAssigned);
        Assert.Equal(1, row.PoolFree);
    }
}
