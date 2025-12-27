using System.Text.Json.Serialization;
using YukariConnect.Configuration;

namespace YukariConnect.Configuration;

[JsonSerializable(typeof(YukariOptions))]
public partial class YukariSerializerContext : JsonSerializerContext
{
}
