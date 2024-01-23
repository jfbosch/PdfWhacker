using System.Diagnostics;

namespace PdfWacker
{
	public class PdfCompressor
	{
		public void CompressFile(
			string inputFilePath,
			string outputFolderPath,
			string processedOriginalFolderPath,
			string ghostscriptPath)
		{
			try
			{
				if (!File.Exists(inputFilePath))
				{
					Console.WriteLine($"Input file not found: {inputFilePath}");
					return;
				}

				string inputFileName = Path.GetFileName(inputFilePath);
				string outputFilePath = Path.Combine(outputFolderPath, inputFileName);
				string processedOriginalFilePath = Path.Combine(processedOriginalFolderPath, inputFileName);

				Console.WriteLine("");
				Console.WriteLine("-------------------------");
				Console.WriteLine($"Compressing file: {inputFileName}");

				inputFilePath.WaitForFileToBeReady();


				//double originalPdfVersion = 0;
				//try
				//{
				//	originalPdfVersion = GetPDFCompatibilityVersion(filePath);
				//}
				//catch (NotImplementedException ex) when (ex.Message.Contains("Encrypted files are currently not supported", StringComparison.InvariantCultureIgnoreCase))
				//{
				//	Console.WriteLine("Unable to compress because PDF is password protected; copying original file to output.");
				//MoveAndReplaceFile(filePath, outputFilePath);
				//}
				//Console.WriteLine($"PDF compatibility version: {originalPdfVersion}");

				var originalSize = new FileInfo(inputFilePath).Length;

				File.Copy(inputFilePath, processedOriginalFilePath, true);

				bool pdfIsPasswordProtected = false;

				// Set up and start the Ghostscript process
				ProcessStartInfo psi = new ProcessStartInfo
				{
					FileName = ghostscriptPath,
					Arguments = $"-sDEVICE=pdfwrite -dCompatibilityLevel=1.7 -dPDFSETTINGS=/ebook -dNOPAUSE -dQUIET -dBATCH -sOutputFile=\"{outputFilePath}\" \"{inputFilePath}\"",
					UseShellExecute = false,
					CreateNoWindow = true,
					RedirectStandardError = true,
				};
				using (Process compressionProcess = Process.Start(psi))
				{
					string errorOutput = compressionProcess.StandardError.ReadToEnd();

					if (!string.IsNullOrEmpty(errorOutput))
					{
						Console.WriteLine($"Error: {errorOutput}");
						if (errorOutput.Contains("This file requires a password for access", StringComparison.InvariantCultureIgnoreCase))
							pdfIsPasswordProtected = true;
					}
					compressionProcess.WaitForExit();
				}


				if (pdfIsPasswordProtected)
				{
					Console.WriteLine("Unable to compress because PDF is password protected; copying original file to output.");
					File.Copy(processedOriginalFilePath, outputFilePath, true);
					File.Delete(inputFilePath);
					return;
				}
				else if (!File.Exists(outputFilePath))
				{
					Console.WriteLine("Unable to compress due to unexpected error; copying original file to output.");
					File.Copy(processedOriginalFilePath, outputFilePath, true);
					File.Delete(inputFilePath);
					return;
				}

				var compressedSize = new FileInfo(outputFilePath).Length;
				double compressionRatio = (double)compressedSize / originalSize * 100;

				// Check if effective compression was possible
				if (compressionRatio > 95.0)
				{
					Console.WriteLine("Effective compression not possible, copying original file to output.");
					File.Copy(processedOriginalFilePath, outputFilePath, true);
				}
				else
				{
					Console.WriteLine($"{originalSize} bytes - Original Size");
					Console.WriteLine($"{compressedSize} bytes - Compressed Size");
					Console.WriteLine($"{compressionRatio:F2} % of original size.");
				}

				if (File.Exists(inputFilePath))
					File.Delete(inputFilePath);
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error processing file {Path.GetFileName(inputFilePath)}");
				Console.WriteLine($"Stack Trace: {ex.ToString()}");
			}
		}
	}
}
