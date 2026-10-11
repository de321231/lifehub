using System.Net;
using System.Security.Cryptography;
using DESD.LifeHub.Core.Abstractions;
using DESD.LifeHub.Core.Entities;
using DESD.LifeHub.Core.Enums;
using DESD.LifeHub.Core.Licensing;
using DESD.LifeHub.Infrastructure.Data;
using DESD.LifeHub.Infrastructure.Licensing;
using DESD.LifeHub.Infrastructure.Options;
using DESD.LifeHub.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MicrosoftOptions = Microsoft.Extensions.Options.Options;

namespace DESD.LifeHub.Tests;

/// <summary>Steuerbare Uhr, damit Kulanz- und Ablaufzeiten deterministisch testbar sind.</summary>
internal sealed class TestClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
}

/// <summary>Simulierter Lizenzserver für die Validierung (kein echter Schlüssel wird verbraucht).</summary>
internal sealed class FakeValidator : ILicenseValidator
{
    public LicenseCheckResult Result { get; set; } =
        LicenseCheckResult.Success("desd-lifehub", "Standard", null, false, 1, 1, 200);

    public Func<LicenseValidationRequest, LicenseCheckResult>? Handler { get; set; }

    public int Calls { get; private set; }

    public Task<LicenseCheckResult> ValidateAsync(
        LicenseValidationRequest request,
        CancellationToken cancellationToken = default)
    {
        Calls++;
        return Task.FromResult(Handler?.Invoke(request) ?? Result);
    }
}

/// <summary>Simulierte OpenLicensd-Admin-API. Erzeugt erkennbare Testsclüssel im Speicher.</summary>
internal sealed class FakeAdminClient : IOpenLicensdAdmin
{
    private int _counter;

    public bool IsConfiguredValue { get; set; } = true;

    public bool IsConfigured => IsConfiguredValue;

    public List<string> IssuedKeys { get; } = new();

    public Func<CreateCompanyLicenseInput, AdminApiResult<OpenLicensdCreatedLicense>>? CreateHandler { get; set; }

    public List<string> RevokedRemoteIds { get; } = new();

    public Task<AdminApiResult<OpenLicensdProduct>> FindProductByCodeAsync(
        string code,
        CancellationToken cancellationToken = default)
        => Task.FromResult(AdminApiResult<OpenLicensdProduct>.Ok(
            new OpenLicensdProduct("prod-1", "LifeHub", code, null)));

    public Task<AdminApiResult<OpenLicensdPolicy>> FindPolicyAsync(
        string productId,
        CancellationToken cancellationToken = default)
        => Task.FromResult(AdminApiResult<OpenLicensdPolicy>.Ok(
            new OpenLicensdPolicy("pol-1", productId, "Standard", 365, "activation", 7, 1)));

    public Task<AdminApiResult<OpenLicensdCreatedLicense>> CreateLicenseAsync(
        CreateCompanyLicenseInput input,
        CancellationToken cancellationToken = default)
    {
        if (CreateHandler is not null)
        {
            return Task.FromResult(CreateHandler(input));
        }

        _counter++;
        var key = $"TEST{_counter:D2}-ABCDE-FGHJK-MNPQR-STVWX";
        IssuedKeys.Add(key);

        return Task.FromResult(AdminApiResult<OpenLicensdCreatedLicense>.Ok(
            new OpenLicensdCreatedLicense(
                Id: $"lic-{_counter}",
                Label: input.Label,
                KeyPrefix: key[..5] + "…",
                ProductId: "prod-1",
                ProductCode: "desd-lifehub",
                ProductName: "LifeHub",
                PolicyId: "pol-1",
                PolicyName: "Standard",
                ExpiresAt: input.ExpiresAt,
                Revoked: false,
                CreatedAt: DateTimeOffset.UtcNow,
                MaxActivations: input.MaxActivations,
                ActivationCount: 0,
                Key: key)));
    }

    public Task<AdminApiResult<OpenLicensdLicense>> RevokeLicenseAsync(
        string licenseId,
        CancellationToken cancellationToken = default)
    {
        RevokedRemoteIds.Add(licenseId);
        return Task.FromResult(AdminApiResult<OpenLicensdLicense>.Ok(
            new OpenLicensdLicense(
                licenseId, "L", "k", "prod-1", "desd-lifehub", "LifeHub", "pol-1", "Standard",
                null, null, true, DateTimeOffset.UtcNow, 1, 0)));
    }
}

/// <summary>
/// Testumgebung: echte Fachservices gegen eine In-Memory-SQLite-Datenbank, mit
/// simuliertem Lizenzserver. Deckt Validierung und Firmenpool-Erzeugung ab.
/// </summary>
internal sealed class TestHarness : IDisposable
{
    private readonly SqliteConnection _connection;

    public TestHarness()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        Db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options);

        Db.Database.EnsureCreated();

        Protector = new AesLicenseKeyProtector(MicrosoftOptions.Create(new LicenseKeyProtectionOptions
        {
            EncryptionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            HashPepper = "test-pepper",
        }));

        Options = new OpenLicensdOptions
        {
            BaseUrl = "https://license.test",
            ProductCode = "desd-lifehub",
            DecisionCacheSeconds = 0,
            ValidationCacheMinutes = 60,
            NegativeCacheMinutes = 0,
            GracePeriodHours = 48,
            ProductId = "prod-1",
            PolicyId = "pol-1",
            ApiToken = "test-token",
        };

        var options = MicrosoftOptions.Create(Options);
        var audit = new AuditService(Db, MicrosoftOptions.Create(new AuditOptions { Enabled = false }), NullLogger<AuditService>.Instance);
        var cache = new MemoryCache(new MemoryCacheOptions());

        Licenses = new LicenseService(
            Db, Validator, Protector, Clock, options, audit, cache, NullLogger<LicenseService>.Instance);

        Company = new CompanyLicenseService(
            Db, Licenses, Protector, Admin, Clock, options, audit, NullLogger<CompanyLicenseService>.Instance);
    }

    public AppDbContext Db { get; }

    public TestClock Clock { get; } = new();

    public FakeValidator Validator { get; } = new();

    public FakeAdminClient Admin { get; } = new();

    public AesLicenseKeyProtector Protector { get; }

    public OpenLicensdOptions Options { get; }

    public LicenseService Licenses { get; }

    public CompanyLicenseService Company { get; }

    public ApplicationUser AddUser(string email, bool active = true)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            NormalizedUserName = email.ToUpperInvariant(),
            NormalizedEmail = email.ToUpperInvariant(),
            DisplayName = email.Split('@')[0],
            IsActive = active,
            SecurityStamp = Guid.NewGuid().ToString(),
        };

        Db.Users.Add(user);
        Db.SaveChanges();
        return user;
    }

    public Organization AddOrganization(string name, string slug, Guid ownerUserId)
    {
        var organization = new Organization
        {
            Id = Guid.NewGuid(),
            Name = name,
            Slug = slug,
            IsPersonal = false,
            OwnerUserId = ownerUserId,
        };

        Db.Organizations.Add(organization);
        Db.SaveChanges();
        return organization;
    }

    public void AddMember(Guid organizationId, Guid userId, OrganizationRole role = OrganizationRole.Member)
    {
        Db.OrganizationMembers.Add(new OrganizationMember
        {
            OrganizationId = organizationId,
            UserId = userId,
            Role = role,
        });

        Db.SaveChanges();
    }

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
    }
}

/// <summary>HTTP-Stub, mit dem sich ein simulierter OpenLicensd-Server darstellen lässt.</summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        => _handler = handler;

    public List<HttpRequestMessage> Requests { get; } = new();

    /// <summary>Anfragetexte, die beim Senden erfasst werden (der Client gibt die Anfrage danach frei).</summary>
    public List<string> CapturedBodies { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);
        CapturedBodies.Add(request.Content is null
            ? string.Empty
            : request.Content.ReadAsStringAsync().GetAwaiter().GetResult());

        return Task.FromResult(_handler(request));
    }
}

internal static class StubResponses
{
    public static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
}
