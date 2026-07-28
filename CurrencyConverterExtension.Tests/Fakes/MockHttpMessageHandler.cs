using System.Net;
using System.Text;

namespace CurrencyConverterExtension.Tests.Fakes;

internal sealed class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
    public int RequestCount { get; private set; }
    public List<Uri> Requests { get; } = [];

    public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestCount++;
        if (request.RequestUri is not null)
        {
            Requests.Add(request.RequestUri);
        }

        return Task.FromResult(_responder(request));
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
}