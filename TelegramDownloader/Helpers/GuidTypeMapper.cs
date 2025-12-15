using MongoDB.Bson;

namespace TelegramDownloader.Helpers
{
    /// <summary>
    /// Custom type mapper for Guid to BsonValue conversion.
    /// Used by Serilog.Sinks.MongoDB to properly serialize Guid properties in logs.
    /// </summary>
    public class GuidTypeMapper : ICustomBsonTypeMapper
    {
        public bool TryMapToBsonValue(object value, out BsonValue bsonValue)
        {
            if (value is Guid guid)
            {
                bsonValue = new BsonString(guid.ToString());
                return true;
            }
            bsonValue = null!;
            return false;
        }
    }
}
