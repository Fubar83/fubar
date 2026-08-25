using System.Text;
using Fubar.Studio.Core.Import;
using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Infrastructure.Import;

/// <summary>
/// Parses a pasted <c>curl</c> command into a <see cref="RequestModel"/>. Handles the flags people
/// actually paste: <c>-X/--request</c>, <c>-H/--header</c>, <c>-d/--data*</c>, <c>-F/--form</c>,
/// <c>-u/--user</c>, and the URL (quoted or bare), plus line-continuation backslashes. Cosmetic flags
/// (<c>-L</c>, <c>--compressed</c>, <c>-k</c>, <c>-s</c>, ...) are ignored.
/// </summary>
public sealed class CurlImporter : ICurlImportService
{
    public RequestModel Parse(string curlCommand)
    {
        if (string.IsNullOrWhiteSpace(curlCommand))
        {
            throw new FormatException("Nothing to import - paste a curl command.");
        }

        var tokens = Tokenize(curlCommand);
        var i = 0;
        if (i < tokens.Count && tokens[i].Equals("curl", StringComparison.OrdinalIgnoreCase))
        {
            i++;
        }

        string? url = null;
        string? method = null;
        string? user = null;
        var headers = new List<KeyValueItem>();
        var dataParts = new List<string>();
        var formParts = new List<string>();

        while (i < tokens.Count)
        {
            var tok = tokens[i];

            // Split "--key=value" so the value doesn't need its own token.
            string? inlineValue = null;
            if (tok.StartsWith("--", StringComparison.Ordinal) && tok.Contains('='))
            {
                var eq = tok.IndexOf('=');
                inlineValue = tok[(eq + 1)..];
                tok = tok[..eq];
            }

            string? Value(string shortOpt)
            {
                // Supports both "-H value" and the attached "-Hvalue" form.
                if (inlineValue is not null)
                {
                    return inlineValue;
                }

                if (shortOpt.Length == 2 && tok.Length > 2 && tok.StartsWith(shortOpt, StringComparison.Ordinal))
                {
                    return tok[2..];
                }

                return ++i < tokens.Count ? tokens[i] : null;
            }

            switch (tok)
            {
                case "-X" or "--request":
                case var _ when tok.StartsWith("-X", StringComparison.Ordinal) && tok.Length > 2:
                    method = Value("-X");
                    break;

                case "-H" or "--header":
                case var _ when tok.StartsWith("-H", StringComparison.Ordinal) && tok.Length > 2:
                    if (Value("-H") is { } h)
                    {
                        AddHeader(headers, h);
                    }
                    break;

                case "-d" or "--data" or "--data-raw" or "--data-ascii" or "--data-binary" or "--data-urlencode":
                case var _ when tok.StartsWith("-d", StringComparison.Ordinal) && tok.Length > 2:
                    if (Value("-d") is { } d)
                    {
                        dataParts.Add(d);
                    }
                    break;

                case "-F" or "--form":
                case var _ when tok.StartsWith("-F", StringComparison.Ordinal) && tok.Length > 2:
                    if (Value("-F") is { } f)
                    {
                        formParts.Add(f);
                    }
                    break;

                case "-u" or "--user":
                    user = Value("-u");
                    break;

                case "-A" or "--user-agent":
                    if (Value("-A") is { } ua)
                    {
                        AddHeader(headers, "User-Agent: " + ua);
                    }
                    break;

                case "-e" or "--referer":
                    if (Value("-e") is { } referer)
                    {
                        AddHeader(headers, "Referer: " + referer);
                    }
                    break;

                case "-b" or "--cookie":
                    if (Value("-b") is { } cookie)
                    {
                        AddHeader(headers, "Cookie: " + cookie);
                    }
                    break;

                case "--url":
                    url = Value("--url");
                    break;

                // Flags that take an argument we don't use - consume it.
                case "-o" or "--output" or "--max-time" or "--connect-timeout" or "--retry":
                    _ = Value(tok);
                    break;

                // Cosmetic no-argument flags - ignore.
                case "--compressed" or "-L" or "--location" or "-k" or "--insecure" or "-s" or "--silent"
                    or "-i" or "--include" or "-v" or "--verbose" or "-g" or "--globoff" or "-f" or "--fail"
                    or "-#" or "--progress-bar":
                    break;

                default:
                    // First bare (non-dash) token is the URL.
                    if (!tok.StartsWith('-') && url is null)
                    {
                        url = tok;
                    }
                    break;
            }

            i++;
        }

        if (string.IsNullOrWhiteSpace(url))
        {
            throw new FormatException("Could not find a URL in the curl command.");
        }

        var (queryParams, cleanUrl) = SplitQuery(url);

        var model = new RequestModel
        {
            Name = DeriveName(method, url),
            Url = cleanUrl,
            QueryParams = queryParams,
            Headers = headers,
        };

        ApplyBody(model, dataParts, formParts, headers);
        model.Method = (method ?? (model.Body.Type != BodyType.None ? "POST" : "GET")).ToUpperInvariant();

        if (user is { Length: > 0 })
        {
            var colon = user.IndexOf(':');
            model.Auth = new AuthConfig
            {
                Type = AuthType.Basic,
                Username = colon < 0 ? user : user[..colon],
                Password = colon < 0 ? "" : user[(colon + 1)..],
            };
        }

        return model;
    }

    private static void ApplyBody(RequestModel model, List<string> dataParts, List<string> formParts, List<KeyValueItem> headers)
    {
        if (formParts.Count > 0)
        {
            model.Body = new RequestBody
            {
                Type = BodyType.FormData,
                FormData = formParts.Select(ToPair).ToList(),
            };
            return;
        }

        if (dataParts.Count == 0)
        {
            return;
        }

        var raw = string.Join("&", dataParts);
        var contentType = headers.FirstOrDefault(h => h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))?.Value ?? "";
        var trimmed = raw.TrimStart();

        if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            model.Body = new RequestBody { Type = BodyType.Json, Raw = raw };
        }
        else if (contentType.Contains("x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase) || (raw.Contains('=') && !raw.Contains(' ')))
        {
            model.Body = new RequestBody
            {
                Type = BodyType.UrlEncoded,
                UrlEncoded = raw.Split('&', StringSplitOptions.RemoveEmptyEntries).Select(ToPair).ToList(),
            };
        }
        else
        {
            model.Body = new RequestBody { Type = BodyType.RawText, Raw = raw };
        }
    }

    private static KeyValueItem ToPair(string kv)
    {
        var eq = kv.IndexOf('=');
        return eq < 0
            ? new KeyValueItem { Key = kv, Value = "", Enabled = true }
            : new KeyValueItem { Key = kv[..eq], Value = kv[(eq + 1)..], Enabled = true };
    }

    private static void AddHeader(List<KeyValueItem> headers, string raw)
    {
        var colon = raw.IndexOf(':');
        if (colon < 0)
        {
            headers.Add(new KeyValueItem { Key = raw.Trim(), Value = "", Enabled = true });
            return;
        }

        headers.Add(new KeyValueItem
        {
            Key = raw[..colon].Trim(),
            Value = raw[(colon + 1)..].Trim(),
            Enabled = true,
        });
    }

    private static (List<KeyValueItem> Query, string Url) SplitQuery(string url)
    {
        var q = url.IndexOf('?');
        if (q < 0)
        {
            return ([], url);
        }

        var query = url[(q + 1)..]
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair =>
            {
                var eq = pair.IndexOf('=');
                return new KeyValueItem
                {
                    Key = Uri.UnescapeDataString(eq < 0 ? pair : pair[..eq]),
                    Value = eq < 0 ? "" : Uri.UnescapeDataString(pair[(eq + 1)..]),
                    Enabled = true,
                };
            })
            .ToList();

        return (query, url[..q]);
    }

    private static string DeriveName(string? method, string url)
    {
        var verb = (method ?? "GET").ToUpperInvariant();
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var path = uri.AbsolutePath.TrimEnd('/');
            return $"{verb} {(string.IsNullOrEmpty(path) ? uri.Host : path)}";
        }

        return $"{verb} request";
    }

    /// <summary>Splits a command line into tokens, honouring single/double quotes and treating a
    /// backslash before a newline as a line continuation.</summary>
    private static List<string> Tokenize(string input)
    {
        var tokens = new List<string>();
        var sb = new StringBuilder();
        var inSingle = false;
        var inDouble = false;
        var has = false;

        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];

            if (inSingle)
            {
                if (c == '\'') { inSingle = false; }
                else { sb.Append(c); }
                continue;
            }

            if (inDouble)
            {
                if (c == '"') { inDouble = false; }
                else if (c == '\\' && i + 1 < input.Length && (input[i + 1] is '"' or '\\' or '$' or '`')) { sb.Append(input[++i]); }
                else { sb.Append(c); }
                continue;
            }

            switch (c)
            {
                case '\'':
                    inSingle = true;
                    has = true;
                    break;
                case '"':
                    inDouble = true;
                    has = true;
                    break;
                case '\\' when i + 1 < input.Length && (input[i + 1] == '\n' || input[i + 1] == '\r'):
                    // Line continuation - skip the backslash and the newline(s).
                    while (i + 1 < input.Length && (input[i + 1] == '\n' || input[i + 1] == '\r')) { i++; }
                    break;
                case '\\' when i + 1 < input.Length:
                    sb.Append(input[++i]);
                    has = true;
                    break;
                case ' ' or '\t' or '\n' or '\r':
                    if (has) { tokens.Add(sb.ToString()); sb.Clear(); has = false; }
                    break;
                default:
                    sb.Append(c);
                    has = true;
                    break;
            }
        }

        if (has)
        {
            tokens.Add(sb.ToString());
        }

        return tokens;
    }
}
