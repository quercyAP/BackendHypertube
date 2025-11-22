namespace Hypertube.BitTorrent.Models;

/// <summary>
/// Types de messages du protocole BitTorrent Peer Wire
/// </summary>
public enum MessageType : byte
{
    Choke = 0,
    Unchoke = 1,
    Interested = 2,
    NotInterested = 3,
    Have = 4,
    Bitfield = 5,
    Request = 6,
    Piece = 7,
    Cancel = 8,
    KeepAlive = 255  // Pas de ID, juste length=0
}

/// <summary>
/// Représente un message du protocole Peer Wire
/// </summary>
public class PeerMessage
{
    /// <summary>
    /// Type de message
    /// </summary>
    public MessageType Type { get; set; }

    /// <summary>
    /// Payload du message (optionnel, dépend du type)
    /// </summary>
    public byte[]? Payload { get; set; }

    /// <summary>
    /// Longueur du message (length prefix)
    /// </summary>
    public int Length => Payload == null ? 1 : 1 + Payload.Length;

    public PeerMessage(MessageType type, byte[]? payload = null)
    {
        Type = type;
        Payload = payload;
    }

    /// <summary>
    /// Encode le message en bytes pour envoi
    /// Format: <length prefix (4 bytes)><id (1 byte)><payload>
    /// </summary>
    public byte[] ToBytes()
    {
        // Keep-alive message (length = 0)
        if (Type == MessageType.KeepAlive)
        {
            return new byte[] { 0, 0, 0, 0 };
        }

        var messageLength = Length;
        var result = new byte[4 + messageLength];

        // Length prefix (4 bytes, big-endian)
        result[0] = (byte)(messageLength >> 24);
        result[1] = (byte)(messageLength >> 16);
        result[2] = (byte)(messageLength >> 8);
        result[3] = (byte)messageLength;

        // Message ID
        result[4] = (byte)Type;

        // Payload
        if (Payload != null && Payload.Length > 0)
        {
            Array.Copy(Payload, 0, result, 5, Payload.Length);
        }

        return result;
    }

    /// <summary>
    /// Parse un message depuis bytes
    /// </summary>
    public static PeerMessage FromBytes(byte[] data)
    {
        if (data.Length < 5)
        {
            throw new ArgumentException("Message too short");
        }

        var messageType = (MessageType)data[0];
        var payload = data.Length > 1 ? data[1..] : null;

        return new PeerMessage(messageType, payload);
    }

    public override string ToString()
    {
        return $"{Type} (payload: {Payload?.Length ?? 0} bytes)";
    }
}
