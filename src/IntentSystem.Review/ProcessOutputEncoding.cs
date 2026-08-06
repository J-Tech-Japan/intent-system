using System.Text;

namespace IntentSystem.Review;

internal static class ProcessOutputEncoding
{
    public static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
}
