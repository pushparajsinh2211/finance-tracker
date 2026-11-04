using System.Diagnostics;
using System.Text;

namespace FamilyLedger.Api.Services.FinanceHub;

public interface IStatementOcrService
{
    Task<string> RecognizePdfAsync(byte[] pdfBytes, string? password, CancellationToken cancellationToken);
}

public sealed class TesseractStatementOcrService(
    ILogger<TesseractStatementOcrService> logger) : IStatementOcrService
{
    private const int MaximumOcrPages = 20;
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(2);

    public async Task<string> RecognizePdfAsync(
        byte[] pdfBytes,
        string? password,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                "OCR for password-protected scanned PDFs is disabled to keep the password out of operating-system process arguments. Export an unlocked copy or use CSV.");
        }

        var workingDirectory = Path.Combine(
            Path.GetTempPath(),
            "financehub-ocr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDirectory);

        try
        {
            var inputPath = Path.Combine(workingDirectory, "statement.pdf");
            await File.WriteAllBytesAsync(inputPath, pdfBytes, cancellationToken);

            await RunAsync(
                "pdftoppm",
                ["-f", "1", "-l", MaximumOcrPages.ToString(), "-r", "200", "-png", inputPath,
                    Path.Combine(workingDirectory, "page")],
                workingDirectory,
                cancellationToken);

            var pageFiles = Directory
                .EnumerateFiles(workingDirectory, "page-*.png")
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            if (pageFiles.Length == 0)
                throw new InvalidOperationException("The scanned PDF could not be rendered for OCR.");

            var recognized = new StringBuilder();
            foreach (var pageFile in pageFiles)
            {
                var text = await RunAsync(
                    "tesseract",
                    [pageFile, "stdout", "-l", "eng", "--psm", "6"],
                    workingDirectory,
                    cancellationToken);
                recognized.AppendLine(text);
            }

            return recognized.ToString();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException(
                "OCR is unavailable on this server. Install Poppler and Tesseract or import a CSV/text PDF.");
        }
        finally
        {
            try
            {
                Directory.Delete(workingDirectory, recursive: true);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not remove temporary OCR directory.");
            }
        }
    }

    private static async Task<string> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CommandTimeout);

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        var output = await outputTask;
        var error = await errorTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                executable == "pdftoppm"
                    ? "The scanned PDF could not be rendered for OCR."
                    : "OCR could not recognize this statement. Try a clearer scan or CSV export.");
        }

        return output;
    }
}
