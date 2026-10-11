using System.Net;
using DESD.LifeHub.Infrastructure.Licensing;
using DESD.LifeHub.Infrastructure.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DESD.LifeHub.Tests;

/// <summary>
/// Vertragstests gegen einen <em>simulierten</em> OpenLicensd-Server (HTTP-Stub).
/// Sie stellen sicher, dass Snake-Case-Antworten korrekt gelesen werden und dass
/// ein fachlicher Fehler nicht mit einem Netzwerkfehler verwechselt wird.
/// </summary>
public class OpenLicensdAdminClientTests
{
    private static OpenLicensdAdminClient CreateClient(StubHttpMessageHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://license.test") };
        var options = Options.Create(new OpenLicensdOptions
        {
            BaseUrl = "https://license.test",
            ApiToken = "test-token",
            ProductCode = "desd-lifehub",
            LicensesPath = "/api/v1/licenses",
            ProductsPath = "/api/v1/products",
            PoliciesPath = "/api/v1/policies",
        });

        return new OpenLicensdAdminClient(http, options, NullLogger<OpenLicensdAdminClient>.Instance);
    }

    [Fact]
    public async Task CreateLicense_ParsesSnakeCaseResponse_AndSendsBearerToken()
    {
        const string body = """
        {
          "id": "abc-123",
          "label": "LifeHub – Acme #001",
          "key": "X4F9K-ABCDE-FGHJK-MNPQR-STVWX",
          "key_prefix": "X4F9K",
          "product_id": "prod-1",
          "product_code": "desd-lifehub",
          "product_name": "LifeHub",
          "policy_id": "pol-1",
          "policy_name": "Standard",
          "expires_at": "2027-01-01T00:00:00Z",
          "revoked": false,
          "created_at": "2026-01-01T00:00:00Z",
          "max_activations": 1,
          "activation_count": 0
        }
        """;

        var handler = new StubHttpMessageHandler(_ => StubResponses.Json(HttpStatusCode.Created, body));
        var client = CreateClient(handler);

        var result = await client.CreateLicenseAsync(
            new CreateCompanyLicenseInput("LifeHub – Acme #001", "prod-1", "pol-1", null, 1));

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Value);
        Assert.Equal("abc-123", result.Value!.Id);
        Assert.Equal("X4F9K-ABCDE-FGHJK-MNPQR-STVWX", result.Value.Key);
        Assert.Equal(1, result.Value.MaxActivations);

        var request = handler.Requests.Single();
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/api/v1/licenses", request.RequestUri!.AbsolutePath);
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("test-token", request.Headers.Authorization.Parameter);

        var sentBody = handler.CapturedBodies.Single();
        Assert.Contains("\"product_id\"", sentBody);
        Assert.Contains("\"policy_id\"", sentBody);
    }

    [Fact]
    public async Task CreateLicense_Unauthorized_IsBusinessError_NotNetworkError()
    {
        var handler = new StubHttpMessageHandler(_ =>
            StubResponses.Json(HttpStatusCode.Unauthorized, """{"error":"unauthorized"}"""));
        var client = CreateClient(handler);

        var result = await client.CreateLicenseAsync(
            new CreateCompanyLicenseInput("L", "prod-1", "pol-1", null, 1));

        Assert.False(result.Succeeded);
        Assert.False(result.Unreachable);
        Assert.Equal(401, result.StatusCode);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task CreateLicense_RejectedPayload_IsBusinessError()
    {
        var handler = new StubHttpMessageHandler(_ =>
            StubResponses.Json(HttpStatusCode.BadRequest, """{"error":"policy not found"}"""));
        var client = CreateClient(handler);

        var result = await client.CreateLicenseAsync(
            new CreateCompanyLicenseInput("L", "prod-1", "pol-x", null, 1));

        Assert.False(result.Succeeded);
        Assert.False(result.Unreachable);
        Assert.Equal(400, result.StatusCode);
        Assert.Contains("policy not found", result.Error!);
    }

    [Fact]
    public async Task CreateLicense_ServerUnreachable_IsMarkedUnreachable()
    {
        var handler = new StubHttpMessageHandler(_ => throw new HttpRequestException("connection refused"));
        var client = CreateClient(handler);

        var result = await client.CreateLicenseAsync(
            new CreateCompanyLicenseInput("L", "prod-1", "pol-1", null, 1));

        Assert.False(result.Succeeded);
        Assert.True(result.Unreachable);
    }

    [Fact]
    public async Task FindProductByCode_MatchesProductFromPagedResponse()
    {
        const string body = """
        {
          "items": [
            { "id": "prod-1", "name": "LifeHub", "code": "desd-lifehub", "description": null },
            { "id": "prod-2", "name": "Other", "code": "other", "description": null }
          ],
          "page": 1,
          "page_size": 100,
          "total": 2,
          "total_pages": 1
        }
        """;

        var handler = new StubHttpMessageHandler(_ => StubResponses.Json(HttpStatusCode.OK, body));
        var client = CreateClient(handler);

        var result = await client.FindProductByCodeAsync("desd-lifehub");

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Value);
        Assert.Equal("prod-1", result.Value!.Id);
    }

    [Fact]
    public async Task FindProductByCode_UnknownCode_IsBusinessError()
    {
        const string body = """{ "items": [], "page": 1, "page_size": 100, "total": 0, "total_pages": 0 }""";
        var handler = new StubHttpMessageHandler(_ => StubResponses.Json(HttpStatusCode.OK, body));
        var client = CreateClient(handler);

        var result = await client.FindProductByCodeAsync("does-not-exist");

        Assert.False(result.Succeeded);
        Assert.False(result.Unreachable);
    }

    [Fact]
    public async Task RevokeLicense_UsesPatchToRevokeEndpoint()
    {
        const string body = """
        {
          "id": "abc-123", "label": "L", "revoked": true,
          "product_code": "desd-lifehub", "policy_name": "Standard",
          "created_at": "2026-01-01T00:00:00Z", "max_activations": 1, "activation_count": 1
        }
        """;

        var handler = new StubHttpMessageHandler(_ => StubResponses.Json(HttpStatusCode.OK, body));
        var client = CreateClient(handler);

        var result = await client.RevokeLicenseAsync("abc-123");

        Assert.True(result.Succeeded);
        var request = handler.Requests.Single();
        Assert.Equal(HttpMethod.Patch, request.Method);
        Assert.Equal("/api/v1/licenses/abc-123/revoke", request.RequestUri!.AbsolutePath);
    }
}
