namespace Fubar.Studio.Core.Models;

/// <summary>
/// A slim, executor-shaped description of the HTTP request an OAuth2/login auth flow sends to acquire a
/// token. Reuses the same value types as a normal request (<see cref="KeyValueItem"/>/<see cref="RequestBody"/>)
/// so the HTTP executor can run it and the Body editor can edit it unchanged, but deliberately omits the
/// full <see cref="RequestModel"/> surface (Name/Auth/Captures/Kind…) to keep <c>auth-profiles.json</c>
/// clean and avoid auth-inside-auth recursion. Any string field may contain <c>{{variables}}</c>, resolved
/// at send/test time. When an <see cref="AuthConfig.TokenRequest"/> is present the auth provider runs this
/// (seeded from an <c>AuthTemplate</c>) instead of the legacy fixed-form OAuth2 path.
/// </summary>
public sealed class AuthTokenRequest
{
    public string Method { get; set; } = "POST";

    public string Url { get; set; } = "";

    public List<KeyValueItem> Headers { get; set; } = [];

    public RequestBody Body { get; set; } = new();
}
