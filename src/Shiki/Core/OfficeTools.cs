using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace Shiki.Core;

/// <summary>
/// Word, Excel and PowerPoint files to PDF (and PDF to Word) using the Office already on the PC,
/// driven through its automation interface, or LibreOffice when Office isn't there. Nothing is
/// uploaded; the apps run hidden and are closed afterwards.
/// </summary>
public static class OfficeTools
{
    static readonly string[] WordTypes = [".doc", ".docx", ".docm", ".odt", ".rtf"];
    static readonly string[] ExcelTypes = [".xls", ".xlsx", ".xlsm", ".ods", ".csv"];
    static readonly string[] PowerPointTypes = [".ppt", ".pptx", ".pptm", ".odp"];
    public static IEnumerable<string> Extensions => WordTypes.Concat(ExcelTypes).Concat(PowerPointTypes);

    static bool Has(string progId) => Type.GetTypeFromProgID(progId) != null;
    public static bool HasWord => Has("Word.Application");
    static bool HasExcel => Has("Excel.Application");
    static bool HasPowerPoint => Has("PowerPoint.Application");

    public static string? LibreOffice =>
        new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 }
            .Select(f => Path.Combine(Environment.GetFolderPath(f), "LibreOffice", "program", "soffice.exe"))
            .FirstOrDefault(File.Exists);

    /// <summary>True when something on this PC can turn documents into PDFs.</summary>
    public static bool Available => HasWord || HasExcel || HasPowerPoint || LibreOffice != null;

    // Office automation constants (wdExportFormatPDF, xlTypePDF, ppSaveAsPDF, wdFormatDocumentDefault).
    const int WordPdf = 17, ExcelPdf = 0, PowerPointPdf = 32, WordDocx = 16;

    public static IReadOnlyList<string> ToPdf(IReadOnlyList<string> inputs, IProgress<double> progress, CancellationToken ct)
    {
        var outputs = new List<string>();
        using var office = new Session();
        for (int i = 0; i < inputs.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var input = inputs[i];
            var output = OutputPaths.Next(input, ".pdf");
            var ext = Path.GetExtension(input).ToLowerInvariant();
            if (WordTypes.Contains(ext) && HasWord) office.Word(input, output);
            else if (ExcelTypes.Contains(ext) && HasExcel) office.Excel(input, output);
            else if (PowerPointTypes.Contains(ext) && HasPowerPoint) office.PowerPoint(input, output);
            else ViaLibreOffice(input, output, "pdf");
            outputs.Add(output);
            progress.Report((i + 1) / (double)inputs.Count);
        }
        JobNotes.Say(outputs.Count == 1 ? $"Saved as {Path.GetFileName(outputs[0])}" : $"Made {outputs.Count} PDFs");
        return outputs;
    }

    /// <summary>Word opens PDFs and rebuilds them as editable documents (layout may shift on complex pages).</summary>
    public static IReadOnlyList<string> PdfToWord(IReadOnlyList<string> inputs, IProgress<double> progress, CancellationToken ct)
    {
        if (!HasWord) throw new InvalidOperationException("PDF to Word needs Microsoft Word on this PC.");
        var outputs = new List<string>();
        using var office = new Session();
        for (int i = 0; i < inputs.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var output = OutputPaths.Next(inputs[i], ".docx");
            office.PdfToDocx(inputs[i], output);
            outputs.Add(output);
            progress.Report((i + 1) / (double)inputs.Count);
        }
        JobNotes.Say("Done. Complex layouts may need a little tidying in Word.");
        return outputs;
    }

    /// <summary>Office prefers to be driven from a single-threaded apartment; give it one.</summary>
    public static T OnSta<T>(Func<T> work)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { result = work(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();
        if (error != null) throw error is COMException com
            ? new InvalidOperationException("Office couldn't open or save this file. Is it open with unsaved changes, or password-protected?", com)
            : error;
        return result;
    }

    static void ViaLibreOffice(string input, string output, string format)
    {
        var soffice = LibreOffice ?? throw new InvalidOperationException(
            "Turning documents into PDFs needs Microsoft Office or the free LibreOffice (libreoffice.org) on this PC.");
        var temp = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "Shiki", "office-" + Guid.NewGuid().ToString("N")[..8])).FullName;
        try
        {
            var start = new ProcessStartInfo(soffice) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var arg in new[] { "--headless", "--convert-to", format, "--outdir", temp, input }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!;
            if (!process.WaitForExit(TimeSpan.FromMinutes(3))) { process.Kill(true); throw new TimeoutException("LibreOffice took too long."); }
            var made = Directory.GetFiles(temp).FirstOrDefault() ?? throw new InvalidOperationException("LibreOffice couldn't convert this file.");
            File.Move(made, output);
        }
        finally
        {
            try { Directory.Delete(temp, true); } catch (IOException) { }
        }
    }

    /// <summary>Sets one of Word's own per-user options for a moment and restores what was there.</summary>
    sealed class WordOption : IDisposable
    {
        readonly string _key;
        readonly string _name;
        readonly object? _previous;

        public WordOption(string version, string name, int value)
        {
            _key = $@"Software\Microsoft\Office\{version}\Word\Options";
            _name = name;
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(_key);
            _previous = key.GetValue(name);
            key.SetValue(name, value, Microsoft.Win32.RegistryValueKind.DWord);
        }

        public void Dispose()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(_key);
                if (_previous == null) key.DeleteValue(_name, throwOnMissingValue: false);
                else key.SetValue(_name, _previous);
            }
            catch (Exception ex)
            {
                Log.Error("Could not restore a Word option", ex);
            }
        }
    }

    /// <summary>
    /// The Office apps for one job, started on demand and closed at the end. If one was already
    /// running with your own documents, it's left open (only what we opened is closed).
    /// </summary>
    sealed class Session : IDisposable
    {
        dynamic? _word, _excel, _powerPoint;
        bool _quitWord, _quitExcel, _quitPowerPoint;
        Process? _wordProcess;

        static dynamic Start(string progId) => Activator.CreateInstance(Type.GetTypeFromProgID(progId)!)!;

        public void Word(string input, string output)
        {
            var word = WordApp();
            dynamic doc = word.Documents.Open(FileName: input, ConfirmConversions: false, ReadOnly: true, AddToRecentFiles: false, Visible: false);
            try
            {
                doc.ExportAsFixedFormat(OutputFileName: output, ExportFormat: WordPdf);
            }
            finally
            {
                doc.Close(SaveChanges: 0);
                Marshal.FinalReleaseComObject(doc);
            }
        }

        public void PdfToDocx(string input, string output)
        {
            var word = WordApp(openPdfs: true);
            if (UnlicensedWordShowing())
                throw new InvalidOperationException("Word on this PC says \"Unlicensed Product\", so it can't convert PDFs. Open Word once and sign in to Office, then try again.");
            // Word asks "this will convert your PDF, OK?" in a dialog nobody can see while hidden.
            // Its own option turns that off; switch it on just for this and put it back after.
            using var quiet = new WordOption((string)word.Version, "DisableConvertPdfWarning", 1);
            // If Word sits on some other hidden prompt, don't wait forever: stop the copy we started.
            using var watchdog = new Timer(_ => { try { _wordProcess?.Kill(); } catch (InvalidOperationException) { } }, null, TimeSpan.FromSeconds(90), Timeout.InfiniteTimeSpan);
            dynamic doc;
            try
            {
                doc = word.Documents.Open(FileName: input, ConfirmConversions: false, ReadOnly: true, AddToRecentFiles: false, Visible: false);
            }
            catch (Exception ex) when (ex is COMException or InvalidComObjectException)
            {
                throw new InvalidOperationException("Word didn't open the PDF. If Word shows a sign-in or licence message when you open it, sort that out first.", ex);
            }
            watchdog.Change(Timeout.Infinite, Timeout.Infinite);
            try
            {
                doc.SaveAs2(FileName: output, FileFormat: WordDocx);
            }
            finally
            {
                doc.Close(SaveChanges: 0);
                Marshal.FinalReleaseComObject(doc);
            }
        }

        dynamic WordApp(bool openPdfs = false)
        {
            if (_word == null)
            {
                _word = Start("Word.Application");
                _quitWord = _word.Documents.Count == 0;
                _word.Visible = false;
                _word.DisplayAlerts = 0; // wdAlertsNone
            }
            // PCs with an old Office next to a new one can hand out the old Word, and Word 2010
            // can't open PDFs. Only then switch to the newest Word on the PC.
            if (openPdfs && Version((object)_word) < 15)
            {
                if (NewestWordExe() is not { } exe) throw new InvalidOperationException("PDF to Word needs Word 2013 or newer.");
                // Let go of the old one first: while it runs, it's what Windows hands back.
                if (_quitWord) { try { _word.Quit(SaveChanges: 0); } catch (COMException) { } }
                Marshal.FinalReleaseComObject(_word);
                _word = null;
                if (ModernWord(exe) is not { } modern) throw new InvalidOperationException("The newer Word on this PC didn't start for conversion.");
                (_word, _wordProcess) = modern;
                _quitWord = _word!.Documents.Count == 0;
                _word.Visible = false;
                _word.DisplayAlerts = 0;
            }
            return _word;
        }

        /// <summary>Word without a licence shows it in its window title ("Word (Unlicensed Product)").</summary>
        static bool UnlicensedWordShowing()
        {
            bool found = false;
            var words = Process.GetProcessesByName("WINWORD").Select(p => (uint)p.Id).ToHashSet();
            EnumWindows((hwnd, _) =>
            {
                GetWindowThreadProcessId(hwnd, out var pid);
                if (!words.Contains(pid)) return true;
                var title = new System.Text.StringBuilder(256);
                GetWindowText(hwnd, title, title.Capacity);
                if (title.ToString().Contains("Unlicensed", StringComparison.OrdinalIgnoreCase)) found = true;
                return !found;
            }, IntPtr.Zero);
            return found;
        }

        delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, System.Text.StringBuilder text, int count);

        static double Version(object app) =>
            double.TryParse((string)((dynamic)app).Version, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;

        static string? NewestWordExe() =>
            new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 }
                .SelectMany(f => new[] { @"Microsoft Office\root\Office16", @"Microsoft Office\Office16", @"Microsoft Office\root\Office15", @"Microsoft Office\Office15" }
                    .Select(d => Path.Combine(Environment.GetFolderPath(f), d, "WINWORD.EXE")))
                .FirstOrDefault(File.Exists);

        /// <summary>Starts the newest installed Word for automation and attaches to it.</summary>
        static (dynamic App, Process Process)? ModernWord(string exe)
        {
            Process? process = null;
            try
            {
                process = Process.Start(new ProcessStartInfo(exe, "/Automation -Embedding") { UseShellExecute = false });
                CLSIDFromProgID("Word.Application", out var clsid);
                for (int i = 0; i < 60; i++)
                {
                    Thread.Sleep(250);
                    try
                    {
                        GetActiveObject(ref clsid, IntPtr.Zero, out var running);
                        dynamic app = running;
                        if (Version(running) >= 15) return (app, process!);
                        // An older Word of yours: leave its handle alone (it may be shared) and keep waiting.
                    }
                    catch (COMException)
                    {
                        // Not registered yet.
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Could not start the newer Word", ex);
            }
            try { process?.Kill(); } catch (InvalidOperationException) { }
            return null;
        }

        [DllImport("ole32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        static extern void CLSIDFromProgID(string progId, out Guid clsid);

        [DllImport("oleaut32.dll", PreserveSig = false)]
        static extern void GetActiveObject(ref Guid clsid, IntPtr reserved, [MarshalAs(UnmanagedType.IUnknown)] out object running);

        public void Excel(string input, string output)
        {
            if (_excel == null)
            {
                _excel = Start("Excel.Application");
                _quitExcel = _excel.Workbooks.Count == 0;
                _excel.DisplayAlerts = false;
            }
            dynamic book = _excel.Workbooks.Open(Filename: input, UpdateLinks: 0, ReadOnly: true, AddToMru: false);
            try
            {
                book.ExportAsFixedFormat(Type: ExcelPdf, Filename: output);
            }
            finally
            {
                book.Close(SaveChanges: false);
                Marshal.FinalReleaseComObject(book);
            }
        }

        public void PowerPoint(string input, string output)
        {
            if (_powerPoint == null)
            {
                // PowerPoint is single-instance: if you have slides open, it's your copy, so leave it running.
                _powerPoint = Start("PowerPoint.Application");
                _quitPowerPoint = _powerPoint.Presentations.Count == 0;
            }
            dynamic deck = _powerPoint.Presentations.Open(FileName: input, ReadOnly: -1, Untitled: 0, WithWindow: 0);
            try
            {
                deck.SaveAs(FileName: output, FileFormat: PowerPointPdf);
            }
            finally
            {
                deck.Close();
                Marshal.FinalReleaseComObject(deck);
            }
        }

        public void Dispose()
        {
            Quit((object?)_word, _quitWord, w => w.Quit(SaveChanges: 0));
            // The Word we started ourselves: make sure it's gone once it has nothing of yours open.
            if (_wordProcess is { } started && _quitWord)
            {
                try { if (!started.WaitForExit(5000)) started.Kill(); } catch (InvalidOperationException) { }
            }
            Quit((object?)_excel, _quitExcel, e => e.Quit());
            Quit((object?)_powerPoint, _quitPowerPoint, p => p.Quit());
        }

        static void Quit(object? app, bool quit, Action<dynamic> close)
        {
            if (app == null) return;
            try
            {
                if (quit) close(app);
            }
            catch (Exception ex)
            {
                Log.Error("Could not close an Office app", ex);
            }
            finally
            {
                Marshal.FinalReleaseComObject(app);
            }
        }
    }
}
