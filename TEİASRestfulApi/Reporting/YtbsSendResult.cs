using System.Net;

namespace TEİASRestfulApi;

public sealed record YtbsSendResult(bool Ok, HttpStatusCode? Status, string? ErrorBody);
