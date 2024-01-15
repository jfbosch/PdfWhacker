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

- If these folders do not exist, the application creates them.

## Requirements

- .NET 8.0 or later
- Ghostscript


## License

This project is licensed under the MIT License. See the LICENSE file for details.

