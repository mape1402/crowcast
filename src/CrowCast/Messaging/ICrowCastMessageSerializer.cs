namespace CrowCast.Messaging;

public interface ICrowCastMessageSerializer
{
    byte[] Serialize<TMessage>(TMessage message)
        where TMessage : notnull;

    TMessage Deserialize<TMessage>(ReadOnlyMemory<byte> content)
        where TMessage : notnull;
}
