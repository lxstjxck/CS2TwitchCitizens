using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace CS2TwitchCitizens.Commands;

[DataContract]
public sealed class ViewerFocusFeedback
{
    [DataMember(Name = "viewerId")] public string ViewerId { get; set; } = string.Empty;
    [DataMember(Name = "result")] public string Result { get; set; } = string.Empty;
    [DataMember(Name = "sequence")] public int Sequence { get; set; }

    public string ToJson()
    {
        using var stream = new MemoryStream();
        new DataContractJsonSerializer(typeof(ViewerFocusFeedback)).WriteObject(stream, this);
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
