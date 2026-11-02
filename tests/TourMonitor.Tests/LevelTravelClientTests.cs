using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TourMonitor.LevelTravel;

namespace TourMonitor.Tests;

public class LevelTravelClientTests
{
    private sealed class CapturingTransport : IApiTransport
    {
        public HttpMethod? Method { get; private set; }
        public string? Url { get; private set; }
        public string? Body { get; private set; }

        public Task<ApiTransportResponse> SendAsync(
            HttpMethod method, string url, string? jsonBody, CancellationToken ct = default)
        {
            Method = method;
            Url = url;
            Body = jsonBody;
            return Task.FromResult(new ApiTransportResponse
            {
                Status = 200,
                Body = """{"success":true,"result":[]}""",
            });
        }
    }

    private static LevelTravelClient BuildClient(IApiTransport transport) =>
        new(transport, Options.Create(new LevelTravelOptions()), NullLogger<LevelTravelClient>.Instance);

    [Fact]
    public async Task GetDirectFlightRoomRates_UsesMultiHotelRooms_WithDirectFlightFilter()
    {
        var transport = new CapturingTransport();
        var client = BuildClient(transport);

        var result = await client.GetDirectFlightRoomRatesAsync("9099454", "request-1", 9);

        Assert.True(result.Success);
        Assert.Equal(HttpMethod.Post, transport.Method);
        Assert.Equal("https://api.level.travel/search/multi_hotel_rooms", transport.Url);

        using var doc = JsonDocument.Parse(transport.Body!);
        var root = doc.RootElement;
        Assert.Equal("request-1", root.GetProperty("request_ids")[0].GetString());
        Assert.Equal("9099454", root.GetProperty("hotel_id").GetString());
        Assert.Equal(9, root.GetProperty("centerNightsCount").GetInt32());

        var filters = root.GetProperty("filters");
        Assert.True(filters.GetProperty("direct_flight").GetBoolean());
        Assert.False(filters.GetProperty("free_cancel").GetBoolean());
        Assert.Equal(JsonValueKind.Null, filters.GetProperty("confirmability").ValueKind);
        Assert.Equal(JsonValueKind.Array, filters.GetProperty("operators").ValueKind);
        Assert.Equal(JsonValueKind.Array, filters.GetProperty("meals").ValueKind);
        Assert.Equal(JsonValueKind.Array, filters.GetProperty("payment_benefits").ValueKind);
        Assert.Equal(JsonValueKind.Array, filters.GetProperty("travel_benefits").ValueKind);
    }

    [Fact]
    public async Task GetRoomRates_WithoutFlag_StillUsesLegacyEndpoint()
    {
        var transport = new CapturingTransport();
        var client = BuildClient(transport);

        await client.GetRoomRatesAsync("9099454", "request-1");

        Assert.Equal(HttpMethod.Post, transport.Method);
        Assert.Equal("https://api.level.travel/hotel_search/room_rates", transport.Url);
        using var doc = JsonDocument.Parse(transport.Body!);
        Assert.False(doc.RootElement.GetProperty("filters").TryGetProperty("direct_flight", out _));
    }
}
