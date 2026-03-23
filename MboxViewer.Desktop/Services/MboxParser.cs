using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using MboxViewer.Desktop.Models;

namespace MboxViewer.Desktop.Services;

public sealed partial class MboxParser
{
    private static readonly Regex EncodedWordRegex = EncodedWordPattern();
    private static readonly Regex AddressRegex = AddressPattern();
    private static readonly Regex HtmlRegex = HtmlPattern();
    private static readonly Regex WhitespaceRegex = WhitespacePattern();
    private static readonly Regex BoundaryRegex = BoundaryPattern();

    public async Task<IReadOnlyList<EmailMessage>> ParseAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var messages = new List<EmailMessage>();
        using var stream = File.OpenRead(filePath);
        using var reader = new StreamReader(stream, Encoding.UTF8, true);

        var currentMessage = new StringBuilder();
        var isFirstSeparator = true;

        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) ?? string.Empty;

            if (line.StartsWith("From ", StringComparison.Ordinal) && !isFirstSeparator)
            {
                var parsed = ParseMessage(currentMessage.ToString());
                if (parsed is not null)
                {
                    messages.Add(parsed);
                }

                currentMessage.Clear();
            }

            currentMessage.AppendLine(line);
            isFirstSeparator = false;
        }

        if (currentMessage.Length > 0)
        {
            var parsed = ParseMessage(currentMessage.ToString());
            if (parsed is not null)
            {
                messages.Add(parsed);
            }
        }

        return messages;
    }

    private EmailMessage? ParseMessage(string rawMessage)
    {
        if (string.IsNullOrWhiteSpace(rawMessage))
        {
            return null;
        }

        var normalized = rawMessage.Replace("\r\n", "\n");
        var separatorIndex = normalized.IndexOf('\n');
        if (separatorIndex >= 0 && normalized.StartsWith("From ", StringComparison.Ordinal))
        {
            normalized = normalized[(separatorIndex + 1)..];
        }

        var headerBodySplit = normalized.IndexOf("\n\n", StringComparison.Ordinal);
        var rawHeaders = headerBodySplit >= 0 ? normalized[..headerBodySplit] : normalized;
        var rawBody = headerBodySplit >= 0 ? normalized[(headerBodySplit + 2)..] : string.Empty;

        var headers = ParseHeaders(rawHeaders);
        var subject = DecodeHeader(headers.GetValueOrDefault("Subject"));
        var from = DecodeHeader(headers.GetValueOrDefault("From"));
        var to = DecodeHeader(headers.GetValueOrDefault("To"));
        var cc = DecodeHeader(headers.GetValueOrDefault("Cc"));

        var (fromName, fromAddress) = ParseAddress(from);
        var contentType = headers.GetValueOrDefault("Content-Type") ?? "text/plain; charset=utf-8";
        var transferEncoding = headers.GetValueOrDefault("Content-Transfer-Encoding");
        var (plainTextBody, htmlBody) = ExtractBody(rawBody, contentType, transferEncoding);

        var previewSource = string.IsNullOrWhiteSpace(plainTextBody) ? StripHtml(htmlBody) : plainTextBody;
        var preview = BuildPreview(previewSource);

        var message = new EmailMessage
        {
            Subject = string.IsNullOrWhiteSpace(subject) ? "(Sin asunto)" : subject,
            FromName = fromName,
            FromAddress = fromAddress,
            To = to,
            Cc = cc,
            Date = ParseDate(headers.GetValueOrDefault("Date")),
            HeadersText = rawHeaders.Trim(),
            PlainTextBody = previewSource.Trim(),
            HtmlBody = htmlBody.Trim(),
            Preview = preview,
            IsRead = false,
            IsStarred = false,
            IsArchived = false,
            IsDeleted = false,
            IsImportant = IsMarkedImportant(headers)
        };

        if (message.IsImportant)
        {
            message.Labels.Add("Importante");
        }

        if (!string.IsNullOrWhiteSpace(headers.GetValueOrDefault("List-Post")))
        {
            message.Labels.Add("Lista");
        }

        if (string.IsNullOrWhiteSpace(message.PlainTextBody))
        {
            message.PlainTextBody = "(Este mensaje no tiene una parte de texto legible)";
        }

        return message;
    }

    private static Dictionary<string, string> ParseHeaders(string rawHeaders)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? currentHeader = null;

        foreach (var line in rawHeaders.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if ((line.StartsWith(' ') || line.StartsWith('\t')) && currentHeader is not null)
            {
                result[currentHeader] = $"{result[currentHeader]} {line.Trim()}";
                continue;
            }

            var separatorIndex = line.IndexOf(':');
            if (separatorIndex <= 0)
            {
                continue;
            }

            currentHeader = line[..separatorIndex].Trim();
            result[currentHeader] = line[(separatorIndex + 1)..].Trim();
        }

        return result;
    }

    private static (string PlainText, string Html) ExtractBody(string body, string? contentType, string? transferEncoding)
    {
        if (!string.IsNullOrWhiteSpace(contentType) && contentType.Contains("multipart/", StringComparison.OrdinalIgnoreCase))
        {
            var boundary = ExtractBoundary(contentType);
            if (!string.IsNullOrWhiteSpace(boundary))
            {
                var parts = SplitMultipartBody(body, boundary);
                foreach (var part in parts)
                {
                    var splitIndex = part.IndexOf("\n\n", StringComparison.Ordinal);
                    var partHeaders = splitIndex >= 0 ? part[..splitIndex] : part;
                    var partBody = splitIndex >= 0 ? part[(splitIndex + 2)..] : string.Empty;
                    var parsedHeaders = ParseHeaders(partHeaders);
                    var nested = ExtractBody(
                        partBody,
                        parsedHeaders.GetValueOrDefault("Content-Type"),
                        parsedHeaders.GetValueOrDefault("Content-Transfer-Encoding"));

                    if (!string.IsNullOrWhiteSpace(nested.PlainText))
                    {
                        return nested;
                    }

                    if (!string.IsNullOrWhiteSpace(nested.Html))
                    {
                        return nested;
                    }
                }
            }
        }

        var decoded = DecodeTransfer(body, transferEncoding, contentType);
        if (!string.IsNullOrWhiteSpace(contentType) && contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase))
        {
            return (StripHtml(decoded), decoded);
        }

        return (decoded, string.Empty);
    }

    private static IEnumerable<string> SplitMultipartBody(string body, string boundary)
    {
        var marker = $"--{boundary}";
        return body
            .Split(marker, StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Trim())
            .Where(part => !part.Equals("--", StringComparison.Ordinal) && !part.StartsWith("--", StringComparison.Ordinal));
    }

    private static string ExtractBoundary(string contentType)
    {
        var match = BoundaryRegex.Match(contentType);
        return match.Success ? match.Groups["boundary"].Value.Trim('"') : string.Empty;
    }

    private static string DecodeTransfer(string body, string? transferEncoding, string? contentType)
    {
        var charset = ExtractCharset(contentType);

        if (string.Equals(transferEncoding, "base64", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var cleaned = string.Concat(body.Where(ch => !char.IsWhiteSpace(ch)));
                var data = Convert.FromBase64String(cleaned);
                return GetEncoding(charset).GetString(data);
            }
            catch
            {
                return body;
            }
        }

        if (string.Equals(transferEncoding, "quoted-printable", StringComparison.OrdinalIgnoreCase))
        {
            return DecodeQuotedPrintable(body, charset);
        }

        return body.Replace("\0", string.Empty).Trim();
    }

    private static string DecodeQuotedPrintable(string input, string charset)
    {
        using var memory = new MemoryStream();

        for (var index = 0; index < input.Length; index++)
        {
            if (input[index] == '=')
            {
                if (index + 2 < input.Length && input[index + 1] == '\r' && input[index + 2] == '\n')
                {
                    index += 2;
                    continue;
                }

                if (index + 1 < input.Length && input[index + 1] == '\n')
                {
                    index += 1;
                    continue;
                }

                if (index + 2 < input.Length)
                {
                    var hex = input.Substring(index + 1, 2);
                    if (byte.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var value))
                    {
                        memory.WriteByte(value);
                        index += 2;
                        continue;
                    }
                }
            }

            memory.WriteByte((byte)input[index]);
        }

        return GetEncoding(charset).GetString(memory.ToArray());
    }

    private static string ExtractCharset(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return "utf-8";
        }

        var segments = contentType.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var charsetSegment = segments.FirstOrDefault(segment => segment.StartsWith("charset=", StringComparison.OrdinalIgnoreCase));
        return charsetSegment?.Split('=')[1].Trim('"', '\'') ?? "utf-8";
    }

    private static Encoding GetEncoding(string charset)
    {
        try
        {
            return Encoding.GetEncoding(charset);
        }
        catch
        {
            return Encoding.UTF8;
        }
    }

    private static string DecodeHeader(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        return EncodedWordRegex.Replace(input, match =>
        {
            var charset = match.Groups["charset"].Value;
            var encoding = match.Groups["encoding"].Value;
            var value = match.Groups["value"].Value;

            try
            {
                var bytes = encoding.Equals("B", StringComparison.OrdinalIgnoreCase)
                    ? Convert.FromBase64String(value)
                    : DecodeEncodedWordQuotedPrintable(value);
                return GetEncoding(charset).GetString(bytes);
            }
            catch
            {
                return match.Value;
            }
        });
    }

    private static byte[] DecodeEncodedWordQuotedPrintable(string value)
    {
        value = value.Replace('_', ' ');
        using var memory = new MemoryStream();

        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '=' && index + 2 < value.Length)
            {
                var hex = value.Substring(index + 1, 2);
                if (byte.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var parsed))
                {
                    memory.WriteByte(parsed);
                    index += 2;
                    continue;
                }
            }

            memory.WriteByte((byte)value[index]);
        }

        return memory.ToArray();
    }

    private static (string Name, string Address) ParseAddress(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return ("Desconocido", string.Empty);
        }

        var match = AddressRegex.Match(input);
        if (match.Success)
        {
            var name = match.Groups["name"].Value.Trim().Trim('"');
            var address = match.Groups["address"].Value.Trim();
            return (string.IsNullOrWhiteSpace(name) ? address : name, address);
        }

        return (input.Trim(), input.Trim());
    }

    private static DateTimeOffset? ParseDate(string? rawDate)
    {
        return DateTimeOffset.TryParse(rawDate, out var parsed) ? parsed : null;
    }

    private static bool IsMarkedImportant(IReadOnlyDictionary<string, string> headers)
    {
        var importance = headers.GetValueOrDefault("Importance");
        var priority = headers.GetValueOrDefault("X-Priority");
        return importance?.Contains("high", StringComparison.OrdinalIgnoreCase) == true
            || priority?.StartsWith("1", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static string StripHtml(string input)
    {
        var withoutTags = HtmlRegex.Replace(input, " ");
        return WebUtility.HtmlDecode(WhitespaceRegex.Replace(withoutTags, " ")).Trim();
    }

    private static string BuildPreview(string input)
    {
        var normalized = WhitespaceRegex.Replace(input, " ").Trim();
        return normalized.Length <= 180 ? normalized : $"{normalized[..177]}...";
    }

    [GeneratedRegex(@"=\?(?<charset>[^?]+)\?(?<encoding>[bBqQ])\?(?<value>[^?]+)\?=")]
    private static partial Regex EncodedWordPattern();

    [GeneratedRegex(@"^(?:(?<name>.*?))?\s*<(?<address>[^>]+)>$")]
    private static partial Regex AddressPattern();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex HtmlPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();

    [GeneratedRegex(@"boundary=(?<boundary>[^;]+)", RegexOptions.IgnoreCase)]
    private static partial Regex BoundaryPattern();
}
