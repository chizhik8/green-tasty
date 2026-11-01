using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.Model;
using System.Collections.Generic;

namespace APIHandler.Helpers;

public static class DynamoDbContextExtensions
{
    /// <summary>
    /// Converts an entity to a low-level attribute map for <c>IAmazonDynamoDB</c> requests (conditional puts, transactions).
    /// The conversion is done by the context, so the item has the same attribute names and value representation
    /// as <c>SaveAsync</c> writes. Null properties are left out: a NULL value is not allowed for index key attributes.
    /// </summary>
    public static Dictionary<string, AttributeValue> ToAttributeMap<T>(this IDynamoDBContext context, T item, string tableName) => context
        .ToDocument(item, new ToDocumentConfig
        {
            OverrideTableName = tableName,
            IgnoreNullValues = true,
        })
        .ToAttributeMap();
}
