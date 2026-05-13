using System.Text;
using System.Text.RegularExpressions;

namespace PdfWhacker;

public static class PdfEncryptionDetector
{
	private const int TailScanBytes = 4096;
	private static readonly Regex EncryptEntryPattern = new(
		@"/Encrypt\s+\d+\s+\d+\s+R",
		RegexOptions.Compiled);

	public static bool IsEncrypted(string path)
	{
		try
		{
			using var stream = File.OpenRead(path);

			Span<byte> headerBuffer = stackalloc byte[5];
			int headerRead = stream.Read(headerBuffer);
			if (headerRead < 5)
				return false;
			if (headerBuffer[0] != (byte)'%' ||
				headerBuffer[1] != (byte)'P' ||
				headerBuffer[2] != (byte)'D' ||
				headerBuffer[3] != (byte)'F' ||
				headerBuffer[4] != (byte)'-')
				return false;

			long length = stream.Length;
			int tailSize = (int)Math.Min(TailScanBytes, length);
			stream.Seek(-tailSize, SeekOrigin.End);
			byte[] tailBuffer = new byte[tailSize];
			int tailRead = stream.Read(tailBuffer, 0, tailSize);
			string tail = Encoding.ASCII.GetString(tailBuffer, 0, tailRead);

			return EncryptEntryPattern.IsMatch(tail);
		}
		catch
		{
			return false;
		}
	}
}
