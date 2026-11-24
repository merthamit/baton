#nullable disable warnings
using System.Net;
using System.Text;
using Baton.Api.Live;
using Baton.Tests.Shared;
using Hl7.Fhir.Model;
using Task = System.Threading.Tasks.Task;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Baton.Api.Tests;

// HAPI's rest-hook calls POST /api/fhir/notify when a subscribed resource changes. The endpoint only publishes "this
// patient changed" to the live feed; it never fails on what it is sent, and it checks the shared secret when one is set.
[Collection("Firely validator")]
public class FhirNotifyTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private WebApplicationFactory<Program> App(string secret = "local-secret") => factory.WithWebHostBuilder(b =>
    {
        b.UseSetting("Baton:Source", "InMemory");
        b.UseSetting("Baton:HookSecret", secret);
    });

    private static async Task<WardChange> Notify(WebApplicationFactory<Program> app, string body, string secret = "local-secret")
    {
        var feed = (WardChangeFeed)app.Services.GetService(typeof(WardChangeFeed));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var changes = feed.Subscribe(cts.Token).GetAsyncEnumerator(cts.Token);
        var next = changes.MoveNextAsync();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/fhir/notify") { Content = new StringContent(body, Encoding.UTF8, "application/fhir+json") };
        request.Headers.Add("X-Baton-Hook", secret);
        var response = await app.CreateClient().SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(await next);
        var change = changes.Current;
        await changes.DisposeAsync();
        return change;
    }

    [Fact]
    public async Task NotifyWithResourcePublishesItsPatient()
    {
        var change = await Notify(App(), """{"resourceType":"Observation","id":"x","status":"final","subject":{"reference":"Patient/synthetic-03"}}""");

        Assert.Equal("Fhir", change.Kind);
        Assert.Equal("synthetic-03", change.PatientId);
        Assert.Null(change.TrySession);
    }

    [Fact]
    public async Task TaskUsesItsForPatient()
    {
        var change = await Notify(App(), """{"resourceType":"Task","for":{"reference":"Patient/synthetic-02"}}""");

        Assert.Equal("synthetic-02", change.PatientId);
    }

    [Fact]
    public async Task NotifyWithEmptyBodyStillPublishes()
    {
        var change = await Notify(App(), "");

        Assert.Equal("Fhir", change.Kind);
        Assert.Null(change.PatientId);
    }

    [Fact]
    public async Task NotifyWithGarbageIs200()
    {
        var change = await Notify(App(), "this is not json {");

        Assert.Null(change.PatientId);
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("POST")]
    [InlineData("DELETE")]
    public async Task HapiStyleCallUnderTheEndpointIsAccepted(string method)
    {
        var app = App();
        var feed = (WardChangeFeed)app.Services.GetService(typeof(WardChangeFeed));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var changes = feed.Subscribe(cts.Token).GetAsyncEnumerator(cts.Token);
        var next = changes.MoveNextAsync();

        var body = method == "DELETE" ? "" : """{"resourceType":"Observation","subject":{"reference":"Patient/synthetic-03"}}""";
        using var request = new HttpRequestMessage(new HttpMethod(method), "/api/fhir/notify/Observation/synthetic-03-live-202611231510-rr")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/fhir+json"),
        };
        request.Headers.Add("X-Baton-Hook", "local-secret");
        var response = await app.CreateClient().SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(await next);
        Assert.Equal("Fhir", changes.Current.Kind);
        await changes.DisposeAsync();
    }

    [Fact]
    public async Task RegistrarNeverStopsTheApi()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new BatonOptions
        {
            Source = BatonOptions.Hapi,
            FhirBaseUrl = "::not a url::",
            LiveCallbackUrl = "http://localhost:5188/api/fhir/notify",
        });
        using var registrar = new LiveSubscriptionRegistrar(options, Microsoft.Extensions.Logging.Abstractions.NullLogger<LiveSubscriptionRegistrar>.Instance);

        await registrar.StartAsync(CancellationToken.None);
        await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(1));
        Assert.False(registrar.ExecuteTask!.IsFaulted); // a bad URL is logged and retried, never thrown

        await registrar.StopAsync(CancellationToken.None);
        Assert.False(registrar.ExecuteTask.IsFaulted);
    }

    [Fact]
    public async Task NotifyWithWrongSecretIs401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/fhir/notify") { Content = new StringContent("", Encoding.UTF8, "application/fhir+json") };
        request.Headers.Add("X-Baton-Hook", "wrong");

        var response = await App().CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void SubscriptionsCoverTheFourKindsAndValidate()
    {
        var subscriptions = LiveSubscriptions.Build("http://host.docker.internal:5188/api/fhir/notify", "local-secret");

        Assert.Equal(
            ["baton-live-observation", "baton-live-task", "baton-live-flag", "baton-live-communication"],
            subscriptions.Select(s => s.Id));
        Assert.Equal("Observation?category=vital-signs", subscriptions[0].Criteria);
        Assert.Equal("Task?code=urn:baton:escalation|clinical-review", subscriptions[1].Criteria);
        Assert.All(subscriptions, s =>
        {
            Assert.Equal(Subscription.SubscriptionChannelType.RestHook, s.Channel.Type);
            Assert.Equal("http://host.docker.internal:5188/api/fhir/notify", s.Channel.Endpoint);
            Assert.Equal("application/fhir+json", s.Channel.Payload);
            var errors = FhirValidation.Validator.Validate(s).Issue
                .Where(i => i.Severity is OperationOutcome.IssueSeverity.Error or OperationOutcome.IssueSeverity.Fatal)
                .Select(i => i.Details?.Text ?? i.Diagnostics)
                .ToList();
            Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
        });
    }

    [Fact]
    public void SecretGoesInTheChannelHeader()
    {
        Assert.Equal(["X-Baton-Hook: s3"], LiveSubscriptions.Build("http://x/notify", "s3")[0].Channel.Header);
        Assert.Empty(LiveSubscriptions.Build("http://x/notify", null)[0].Channel.Header);
    }
}
