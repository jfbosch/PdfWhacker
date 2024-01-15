# PdfWacker

PdfWacker is a console application written in C# that watches a specified directory for new PDF files and automatically compresses them using Ghostscript. The application also compresses existing PDF files in the directory when it starts.

## Features

- Watches a specified directory for new PDF files and compresses them automatically.
- Compresses existing PDF files in the same directory on startup, if there are any.
- Handles errors gracefully, including files that are password protected or not found.
- Provides detailed console output, including compression statistics and error messages.


## Usage

The application requires two command-line arguments:

- The path to the working folder.
- The path to the Ghostscript executable.

Here is an example of how to run the application:

PdfWacker.exe <working folder path> <ghostscript executable path>


The working folder should contain three subfolders:

- CompressionInput: The application watches this folder for new PDF files to compress.
- CompressionOriginal: The application moves the original PDF files to this folder after compressing them.
- CompressionOutput: The application saves the compressed PDF files in this folder.

If these folders do not exist, the application creates them.

If the compression achieved is less than 5% smaller than the original, then it is considered ineffective and the original file is output to the compressed folder. 
Similarly, if a PDF is password protected, the original file is output to the compressed folder, as compression is not possible on encrypted PDFs. 


## Requirements

- .NET 8.0 or later
- Ghostscript


## License

This project is licensed under the MIT License. See the LICENSE file for details.


## Back story

In my business and private life I deal with very many PDFs. I have found that the vast majority of them are very poorly optimized and the files are much bigger than what is necessary for my archival purposes.
I have long been a fan of the many tools at ILovePDF, such as their compression tool.
https://www.ilovepdf.com/compress_pdf
However, it got tedious to keep uploading my documents to their site and then downloading the compressed versions. Also, while I have no reason to doubt their security and practices, there is always some level of concern when uploading confidential documents to a third-party public site.
Thus, I decided to write a little app that runs locally. It allows me to drop PDFs into one folder and they show up compressed in another folder within a second or two. This application is really just an automation around GhostScript, which you can use to achieve the same manually.
