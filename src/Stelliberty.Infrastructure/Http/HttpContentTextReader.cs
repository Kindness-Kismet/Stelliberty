using System.Text;

namespace Stelliberty.Infrastructure.Http;

internal static class HttpContentTextReader
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    static HttpContentTextReader()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static async Task<string> ReadAsStringAsync(HttpContent content, CancellationToken cancellationToken = default)
    {
        var bytes = await content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        var charset = content.Headers.ContentType?.CharSet;
        var encoding = ResolveEncoding(charset);
        using var stream = new MemoryStream(bytes);
        using var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: string.IsNullOrWhiteSpace(charset));
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    private static Encoding ResolveEncoding(string? charset)
    {
        if (string.IsNullOrWhiteSpace(charset))
        {
            return Utf8;
        }

        var normalized = charset.Trim().Trim('"', '\'');
        if (string.Equals(normalized, "utf8", StringComparison.OrdinalIgnoreCase))
        {
            normalized = "utf-8";
        }

        return Encoding.GetEncoding(normalized);
    }
}
