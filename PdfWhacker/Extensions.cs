namespace PdfWhacker
{
	public static class Extensions
	{
		public static void WaitForFileToBeReady(
			this string filePath)
		{
			while (!(IsFileReady(filePath)))
			{
				Thread.Sleep(250);
			}
		}


		public static bool IsFileReady(
			this string filePath)
		{
			if (!File.Exists(filePath))
				throw new FileNotFoundException("The file to process cannot be found.", filePath);

			// If the file can be opened for exclusive access it means that the file
			// is no longer locked by another process.
			try
			{
				using (FileStream inputStream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None))
				{
					return inputStream.Length > 0;
				}
			}
			catch (Exception)
			{
				// The file is unavailable because it is:
				// still being written to
				// or being processed by another thread.
				return false;
			}
		}


		public static void MoveAndReplaceFile(
			this string filePath,
			string targetFilePath)
		{
			if (File.Exists(targetFilePath))
				File.Delete(targetFilePath);
			File.Move(filePath, targetFilePath);
		}


	}
}
