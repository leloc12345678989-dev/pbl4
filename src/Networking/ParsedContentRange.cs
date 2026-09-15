namespace pbl4.Networking;

public sealed record ParsedContentRange(long Start, long End, long? TotalLength)
{
    public long Length => End - Start + 1;
}

