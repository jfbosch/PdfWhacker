# PdfWhacker

PdfWhacker is a console application written in C# that watches a specified directory for new PDF files to compress, decrypt, or merge them using Ghostscript.

As a supplementary note, it is important to clarify that Ghostscript does not 'merge' PDF files in the conventional sense. Instead, it processes multiple PDF files as inputs to generate a completely new PDF file. While the visual appearance of the resulting PDF file is intended to be identical to that of the input files, the new file is an entirely distinct entity. The input files are fully interpreted, and the resultant file shares no commonalities with the original files beyond their visual representation.


## Features

Compress PDFs:

- Watches the CompressionInput directory for new PDF files and compresses them automatically into the Output directory.
- Compresses existing PDF files in the CompressionInput directory on startup, if there are any.
- One-shot recursive form: `PdfWhacker compress <directoryPath> <ghostscriptExecutablePath>` compresses every PDF under `<directoryPath>` in place. Originals are only replaced when compression succeeds and meets the minimum size-reduction threshold; timestamps are preserved.


Decrypt PDFs:

- Watches the DecryptInput directory for new PDF files and removes their password protection automatically, writing the decrypted output to the Output directory.
- Decrypts existing PDF files in the DecryptInput directory on startup, if there are any.
- Candidate passwords are read from `appsettings.json` next to the executable (see [Password configuration](#password-configuration)).
- One-shot recursive form: `PdfWhacker decrypt <directoryPath> <ghostscriptExecutablePath>` decrypts every password-protected PDF under `<directoryPath>` in place.


Merge PDFs

- Watches the MergeInput directory for new PDF files to be merged and prints the found files to the console.
- Once all files are found, press `m` then Enter to merge them all. The merged file is written to the Output directory as `merged-<timestamp>.pdf` (millisecond-precision timestamp with a numeric collision suffix, so prior merges are never overwritten).
- Merges existing PDF files in the MergeInput directory on startup, if there are any.

General.
- Handles errors gracefully, including files that are password protected or not found.
- Provides detailed console output, including compression statistics and error messages.

## Usage

PdfWhacker is invoked with a subcommand. There are three:

```
PdfWhacker watch    <workingFolderPath> <ghostscriptExecutablePath>
PdfWhacker compress <directoryPath>     <ghostscriptExecutablePath>
PdfWhacker decrypt  <directoryPath>     <ghostscriptExecutablePath>
```

- **`watch`** — Long-running mode. Watches `<workingFolderPath>/CompressionInput`, `<workingFolderPath>/DecryptInput`, and `<workingFolderPath>/MergeInput` for PDFs and produces output in `<workingFolderPath>/Output`. Press `m` then Enter to trigger a merge, `q` then Enter (or Ctrl-C) to quit.
- **`compress`** — One-shot mode. Recursively compresses every PDF under `<directoryPath>` in place.
- **`decrypt`** — One-shot mode. Recursively decrypts every password-protected PDF under `<directoryPath>` in place using passwords from `appsettings.json` next to the executable.

The watch-mode working directory will contain the following subdirectories:

- CompressionInput: The application watches this directory for new PDF files to compress.
- DecryptInput: The application watches this directory for new password-protected PDF files to decrypt.
- MergeInput: The application watches this directory for new PDF files to be merged.
- Original/Compression: The application moves the original PDF files to this directory after compressing them.
- Original/Decrypt: The application moves the original PDF files to this directory after attempting to decrypt them.
- Original/Merge: The application moves the original PDF files to this directory after merging them.
- Output: The application saves compressed, decrypted (using their original filenames), and merged PDF files (named `merged-<timestamp>.pdf`) in this directory.

If these directories do not exist, the application creates them.

`Output` is shared by both the compression and decryption pipelines. If the same filename appears in both `CompressionInput` and `DecryptInput`, the later run will overwrite the earlier output. Rename one ahead of time if you need to keep both.

The `Original/*` folders are not pruned by PdfWhacker — they accumulate everything you've processed. Periodically clear them out (or rotate them to cold storage) if disk usage matters.

If you ran an older version of PdfWhacker, the legacy `CompressionOriginal`, `CompressionOutput`, `MergeOriginal`, and `MergeOutput` folders are migrated automatically into the new structure on startup. Files whose names already exist at the destination are left in place so nothing is silently overwritten.


During compression

- If the compressed output is more than 95% of the original size (i.e. less than a 5% reduction), the compression is considered ineffective and the original file is output to the compressed folder. 
- Similarly, if a PDF is password protected, the original file is output to the compressed folder, as compression is not possible on encrypted PDFs. 

- 
During decryption:

- If the PDF is not encrypted, the original is passed through to the Output folder unchanged.
- If the PDF is encrypted, each configured password is tried in turn (preceded by an implicit no-password attempt that handles owner-password-only PDFs). The first password that succeeds produces the decrypted output.
- If no configured password matches, the still-encrypted original is passed through to the Output folder (watch mode) or left in place (recursive `decrypt` mode, classified as "Skipped (locked)").
- Decryption does not recompress the PDF — quality is preserved. Run compression separately afterward if you also want a smaller file.

During merging:

- If any of the input files are password protected, or if another error occurs, no merging will be completed, and the input files will be left in the MergeInput directory.

## Password configuration

Candidate passwords live in `appsettings.json` next to the `PdfWhacker` executable:

```json
{
  "Passwords": [
    "password1",
    "password2",
    "password3"
  ]
}
```

Every locked PDF is attempted against each password in this list, in order. If the file is missing, contains an empty list, or fails to parse, a warning is printed at startup and the system continues — only owner-password-only PDFs will then be decryptable.

### Password handling — security caveat

Passwords are not echoed to stdout. However, each decryption attempt invokes Ghostscript with `-sPDFPassword=<plaintext>` as a command-line argument, which means **the plaintext password is briefly visible to any process on the same machine that can enumerate child process command lines** (Task Manager's "Command line" column, `Get-CimInstance Win32_Process | select CommandLine`, Process Explorer, etc.). Anyone with local execution rights on the box can read passwords while a decrypt is in flight.

The on-disk copy in `appsettings.json` is protected only by file ACLs. PdfWhacker does not encrypt it.

If your threat model includes other local users, do not store sensitive passwords in `appsettings.json` and prefer compressing/merging only — leave decryption for systems where you control all local accounts.

## Requirements

- .NET 10.0 or later
- Ghostscript


## License

This project is licensed under the MIT License. See the LICENSE file for details.


## Back story

In my business and private life I deal with very many PDFs. I have found that the vast majority of them are very poorly optimized and the files are much bigger than what is necessary for my archival purposes.
I have long been a fan of the many tools at ILovePDF, such as their compression tool.
https://www.ilovepdf.com/compress_pdf
However, it got tedious to keep uploading my documents to their site and then downloading the compressed versions. Also, while I have no reason to doubt their security and practices, there is always some level of concern when uploading confidential documents to a third-party public site.
Thus, I decided to write a little app that runs locally. It allows me to drop PDFs into one folder and they show up compressed in another folder within a second or two. This application is really just an automation around GhostScript, which you can use to achieve the same manually.
