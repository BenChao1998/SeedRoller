using System.Globalization;
using System.Text.Json.Serialization;

namespace SeedRollerCli;

[JsonConverter(typeof(JsonStringEnumConverter))]
internal enum RewardDetailType
{
    Text,
    Card,
    Potion,
    Gold
}

internal sealed record RewardDetail(
    RewardDetailType Type,
    string Label,
    string Value,
    string? ModelId = null,
    int? Amount = null)
{
    public string FormatForDisplay()
    {
        var text = Value;
        if (!string.IsNullOrWhiteSpace(ModelId))
        {
            text = string.IsNullOrWhiteSpace(Value)
                ? ModelId
                : $"{Value} ({ModelId})";
        }
        else if (Amount is { } amount && string.IsNullOrWhiteSpace(Value))
        {
            text = amount.ToString(CultureInfo.InvariantCulture);
        }

        if (string.IsNullOrWhiteSpace(Label))
        {
            return text;
        }

        return $"{Label}：{text}";
    }

    [JsonIgnore]
    public string SearchText
    {
        get
        {
            var text = string.IsNullOrWhiteSpace(Label)
                ? Value
                : $"{Label} {Value}";
            if (!string.IsNullOrWhiteSpace(ModelId))
            {
                text = $"{text} {ModelId}";
            }

            if (Amount is { } amount && string.IsNullOrWhiteSpace(Value))
            {
                text = $"{text} {amount}";
            }

            return text;
        }
    }
}
