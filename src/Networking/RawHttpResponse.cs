namespace pbl4.Networking;

public sealed record RawHttpResponse(
    string HttpVersion,
    int StatusCode,
    string ReasonPhrase,
    IReadOnlyDictionary<string, string> Headers,
    string BodyPreview);

