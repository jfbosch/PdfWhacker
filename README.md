# PdfWhacker

PdfWhacker is a console application written in C# that watches a specified directory for new PDF files to compress or merge them using Ghostscript.

As a supplementary note, it is important to clarify that Ghostscript does not 'merge' PDF files in the conventional sense. Instead, it processes multiple PDF files as inputs to generate a completely new PDF file. While the visual appearance of the resulting PDF file is intended to be identical to that of the input files, the new file is an entirely distinct entity. The input files are fully interpreted, and the resultant file shares no commonalities with the original files beyond their visual representation.


## Features

Compress PDFs:

- Watches the CompressionInput directory for new PDF files and compresses them automatically into the CompressionOutput directory.
- Compresses existing PDF files in the CompressionInput directory on startup, if there are any.


Merge PDFs

- Watches the MergeInput directory for new PDF files to be merged and prints the found files to the console.
- Once all files are found, press (m) to merge them all and have the merged.pdf placed in the MergeOutput directory.
- Merges existing PDF files in the MergeInput directory on startup, if there are any.

General.
- Handles errors gracefully, including files that are password protected or not found.
- Provides detailed console output, including compression statistics and error messages.

## Usage

The application requires two command-line arguments:

- The path to the working folder.
- The path to the Ghostscript executable.

Here is an example of how to run the application:

```
PdfWhacker.exe <working folder path> <ghostscript executable path>
```


The working directory should contain six subdirectories:

- CompressionInput: The application watches this directory for new PDF files to compress.
- CompressionOriginal: The application moves the original PDF files to this directory after compressing them.
- CompressionOutput: The application saves the compressed PDF files in this directory .
- MergeInput: The application watches this directory for new PDF files to be merged.
- MergeOriginal: The application moves the original PDF files to this directory after merging them.
- MergeOutput: The application saves the merged PDF file in this directory with the name merged.pdf..

- 
If these directories do not exist, the application creates them.


During compression

- If the compression achieved is less than 5% smaller than the original, then it is considered ineffective and the original file is output to the compressed folder. 
- Similarly, if a PDF is password protected, the original file is output to the compressed folder, as compression is not possible on encrypted PDFs. 

- 
During merging:

- If any of the input files are password protected, or if another error occurs, no merging will be completed, and the input files will be left in the MergeInput directory.

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
