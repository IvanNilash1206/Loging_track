using System.Text.Json;
using System.Text.Json.Serialization;
using Google.Cloud.Firestore;

namespace LogSystem.Dashboard.Converters;

/// <summary>
/// Converts Google.Cloud.Firestore.Timestamp to/from ISO 8601 strings in JSON.
/// Without this, Timestamp serializes as {"seconds":N,"nanos":N} which JS can't parse.
/// </summary>
public class FirestoreTimestampJsonConverter : JsonConverter<Timestamp>
{
    public override Timestamp Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var dt = reader.GetDateTime();
        return Timestamp.FromDateTime(DateTime.SpecifyKind(dt, DateTimeKind.Utc));
    }

    public override void Write(Utf8JsonWriter writer, Timestamp value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToDateTime().ToString("O"));
    }
}
