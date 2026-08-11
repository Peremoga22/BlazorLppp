using System.Runtime.InteropServices;

namespace BlazorLppp.Application.Services;

/// <summary>
/// Конвертує застарілий .doc у .docx через Microsoft Word (COM).
/// </summary>
public static class WordDocConverter
{
    private const int WdFormatXmlDocument = 16;
    private const int WdDoNotSaveChanges = 0;
    private static readonly TimeSpan ConvertTimeout = TimeSpan.FromSeconds(45);

    public static bool IsDocExtension(string filePath)
        => Path.GetExtension(filePath).Equals(".doc", StringComparison.OrdinalIgnoreCase);

    public static string ConvertToDocx(string docPath, string? destinationDocxPath = null)
    {
        if (!File.Exists(docPath))
        {
            throw new FileNotFoundException("Файл .doc не знайдено.", docPath);
        }

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Конвертація .doc потребує Microsoft Word на Windows.");
        }

        return ConvertToDocxWindows(docPath, destinationDocxPath);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static string ConvertToDocxWindows(string docPath, string? destinationDocxPath)
    {
        var outputPath = destinationDocxPath
            ?? Path.Combine(
                Path.GetTempPath(),
                "BlazorLppp",
                $"{Path.GetFileNameWithoutExtension(docPath)}-{Guid.NewGuid():N}.docx");

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        Exception? workerError = null;
        var thread = new Thread(() =>
        {
            try
            {
                ConvertOnStaThread(docPath, outputPath);
            }
            catch (Exception ex)
            {
                workerError = ex;
            }
        })
        {
            IsBackground = true,
            Name = "WordDocConverter"
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        if (!thread.Join(ConvertTimeout))
        {
            try
            {
                foreach (var process in System.Diagnostics.Process.GetProcessesByName("WINWORD"))
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // ignored
            }

            throw new TimeoutException(
                "Конвертація .doc через Word перевищила час очікування. Збережіть файл як .docx і завантажте знову.");
        }

        if (workerError is not null)
        {
            throw new InvalidOperationException(
                "Не вдалося конвертувати .doc через Word. Збережіть файл як .docx і завантажте знову.",
                workerError);
        }

        if (!File.Exists(outputPath))
        {
            throw new InvalidOperationException(
                "Конвертація .doc не створила файл .docx. Збережіть документ як .docx.");
        }

        return outputPath;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void ConvertOnStaThread(string docPath, string outputPath)
    {
        object? wordApp = null;
        object? document = null;

        try
        {
            var wordType = Type.GetTypeFromProgID("Word.Application")
                ?? throw new InvalidOperationException(
                    "Microsoft Word не встановлено. Збережіть файл як .docx і завантажте знову.");

            wordApp = Activator.CreateInstance(wordType)
                ?? throw new InvalidOperationException("Не вдалося запустити Microsoft Word.");

            wordType.InvokeMember(
                "Visible",
                System.Reflection.BindingFlags.SetProperty,
                null,
                wordApp,
                [false]);

            wordType.InvokeMember(
                "DisplayAlerts",
                System.Reflection.BindingFlags.SetProperty,
                null,
                wordApp,
                [0]);

            var documents = wordType.InvokeMember(
                "Documents",
                System.Reflection.BindingFlags.GetProperty,
                null,
                wordApp,
                null);

            document = documents!.GetType().InvokeMember(
                "Open",
                System.Reflection.BindingFlags.InvokeMethod,
                null,
                documents,
                [docPath, false, true, false]);

            document!.GetType().InvokeMember(
                "SaveAs2",
                System.Reflection.BindingFlags.InvokeMethod,
                null,
                document,
                [outputPath, WdFormatXmlDocument]);
        }
        catch (COMException ex)
        {
            throw new InvalidOperationException(
                "Не вдалося конвертувати .doc через Word. Збережіть файл як .docx.",
                ex);
        }
        finally
        {
            TryCloseDocument(document);
            TryQuitWord(wordApp);
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void TryCloseDocument(object? document)
    {
        if (document is null)
        {
            return;
        }

        try
        {
            document.GetType().InvokeMember(
                "Close",
                System.Reflection.BindingFlags.InvokeMethod,
                null,
                document,
                [WdDoNotSaveChanges]);
        }
        catch
        {
            // ignored
        }
        finally
        {
            Marshal.FinalReleaseComObject(document);
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void TryQuitWord(object? wordApp)
    {
        if (wordApp is null)
        {
            return;
        }

        try
        {
            wordApp.GetType().InvokeMember(
                "Quit",
                System.Reflection.BindingFlags.InvokeMethod,
                null,
                wordApp,
                [WdDoNotSaveChanges]);
        }
        catch
        {
            // ignored
        }
        finally
        {
            Marshal.FinalReleaseComObject(wordApp);
        }
    }
}
