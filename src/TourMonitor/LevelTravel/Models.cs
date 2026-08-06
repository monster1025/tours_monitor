using System.Text.Json.Serialization;

namespace TourMonitor.LevelTravel;

public sealed class MultiEnqueueRequest
{
    [JsonPropertyName("search_params")]
    public List<SearchParam> SearchParams { get; set; } = new();
}

public sealed class SearchParam
{
    [JsonPropertyName("hotel_ids")]
    public int HotelIds { get; set; }

    [JsonPropertyName("from_city")]
    public string FromCity { get; set; } = "";

    [JsonPropertyName("from_country")]
    public string FromCountry { get; set; } = "";

    [JsonPropertyName("to_city")]
    public string ToCity { get; set; } = "";

    [JsonPropertyName("to_country")]
    public string ToCountry { get; set; } = "";

    [JsonPropertyName("start_date")]
    public string StartDate { get; set; } = "";

    [JsonPropertyName("nights")]
    public string Nights { get; set; } = "";

    [JsonPropertyName("adults")]
    public int Adults { get; set; }

    [JsonPropertyName("kids")]
    public int Kids { get; set; }

    [JsonPropertyName("kids_ages")]
    public int[] KidsAges { get; set; } = Array.Empty<int>();

    [JsonPropertyName("flex_dates")]
    public bool FlexDates { get; set; }

    [JsonPropertyName("search_type")]
    public string SearchType { get; set; } = "package";
}

public sealed class MultiEnqueueResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("search_requests")]
    public List<SearchRequest> SearchRequests { get; set; } = new();
}

public sealed class SearchRequest
{
    [JsonPropertyName("request_id")]
    public string RequestId { get; set; } = "";
}

public sealed class SearchStatus
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("status")]
    public Dictionary<string, string>? Status { get; set; }

    [JsonPropertyName("size")]
    public long? Size { get; set; }

    [JsonPropertyName("completeness")]
    public int? Completeness { get; set; }

    [JsonPropertyName("request_id")]
    public string RequestId { get; set; } = "";
}

public sealed class RoomRatesRequest
{
    [JsonPropertyName("hotel_id")]
    public string HotelId { get; set; } = "";

    [JsonPropertyName("request_id")]
    public string RequestId { get; set; } = "";

    [JsonPropertyName("filters")]
    public RoomRatesFilters Filters { get; set; } = new();
}

public sealed class RoomRatesFilters
{
    [JsonPropertyName("meals")]
    public List<string> Meals { get; set; } = new();

    [JsonPropertyName("free_cancel")]
    public bool FreeCancel { get; set; }

    [JsonPropertyName("confirmability")]
    public object? Confirmability { get; set; }

    [JsonPropertyName("payment_benefits")]
    public List<object> PaymentBenefits { get; set; } = new();
}

public sealed class RoomRatesResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("result")]
    public List<RoomRate> Result { get; set; } = new();
}

public sealed class RoomRate
{
    [JsonPropertyName("ID")]
    public string Id { get; set; } = "";

    [JsonPropertyName("min_price")]
    public int MinPrice { get; set; }

    [JsonPropertyName("meal_types")]
    public List<MealType> MealTypes { get; set; } = new();

    [JsonPropertyName("room")]
    public RoomInfo Room { get; set; } = new();

    [JsonPropertyName("offers")]
    public Dictionary<string, List<TourOffer>> Offers { get; set; } = new();
}

public sealed class MealType
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("min_price")]
    public int MinPrice { get; set; }
}

public sealed class RoomInfo
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name_ru")]
    public string NameRu { get; set; } = "";
}

public sealed class TourOffer
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("nights_count")]
    public int NightsCount { get; set; }

    [JsonPropertyName("price")]
    public int Price { get; set; }

    [JsonPropertyName("price_per_night")]
    public int PricePerNight { get; set; }

    [JsonPropertyName("operator_id")]
    public int OperatorId { get; set; }

    [JsonPropertyName("operator_name")]
    public string OperatorName { get; set; } = "";

    [JsonPropertyName("start_date")]
    public string StartDate { get; set; } = "";

    [JsonPropertyName("link")]
    public string Link { get; set; } = "";

    [JsonPropertyName("extras")]
    public OfferExtras? Extras { get; set; }
}

public sealed class OfferExtras
{
    [JsonPropertyName("best_price")]
    public bool BestPrice { get; set; }

    [JsonPropertyName("instant_confirm")]
    public bool InstantConfirm { get; set; }

    [JsonPropertyName("cheap")]
    public bool Cheap { get; set; }
}
