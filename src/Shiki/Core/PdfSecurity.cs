using System;
using System.Collections.Generic;
using System.IO;
using Docnet.Core;
using Docnet.Core.Models;
using ImageMagick;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Shiki.Core;

/// <summary>Passwords and page numbers for PDFs.</summary>
public static class PdfSecurity
{
    public enum Check { NotProtected, Opens, WrongPassword, Unsupported }

    /// <summary>Tries a password without changing anything, so the dialog can say right away if it's wrong.</summary>
    public static Check TryPassword(string input, string password)
    {
        try
        {
            using var plain = PdfReader.Open(input, PdfDocumentOpenMode.Import);
            if (!plain.SecuritySettings.IsEncrypted) return Check.NotProtected;
        }
        catch (PdfReaderException)
        {
            // Needs a password: carry on and try the one we were given.
        }
        catch (Exception)
        {
            return Check.Unsupported;
        }

        try
        {
            using var doc = PdfReader.Open(input, password, PdfDocumentOpenMode.Import);
            return Check.Opens;
        }
        catch (PdfReaderException ex) when (ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase))
        {
            return Check.WrongPassword;
        }
        catch (Exception)
        {
            // PDFsharp can't read this kind of encryption; PDFium may still open it.
            return OpensWithPdfium(input, password) ? Check.Unsupported : Check.WrongPassword;
        }
    }

    /// <summary>Saves a copy without the password. Falls back to re-rendering pages when the encryption type isn't supported.</summary>
    public static string Unlock(string input, string password, IProgress<double> progress)
    {
        var output = OutputPaths.Next(input, ".pdf", "unlocked");
        try
        {
            using var doc = PdfReader.Open(input, password, PdfDocumentOpenMode.Modify);
            doc.SecurityHandler.SetEncryptionToNoneAndResetPasswords();
            doc.Save(output);
            JobNotes.Say($"Saved {Path.GetFileName(output)} without the password");
            return output;
        }
        catch (PdfReaderException ex) when (ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("That password didn't open the PDF.");
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            Log.Error("Clean unlock failed, re-rendering pages instead", ex);
            RenderUnlocked(input, password, output, progress);
            JobNotes.Say("Unlocked; the pages are now pictures (this kind of lock can't be removed cleanly)");
            return output;
        }
    }

    /// <summary>Saves a copy that needs <paramref name="password"/> to open (AES-256).</summary>
    public static string Lock(string input, string password)
    {
        var output = OutputPaths.Next(input, ".pdf", "locked");
        using var doc = PdfReader.Open(input, PdfDocumentOpenMode.Modify);
        doc.SecuritySettings.UserPassword = password;
        doc.SecuritySettings.OwnerPassword = password;
        doc.SecurityHandler.SetEncryptionToV5();
        doc.Save(output);
        JobNotes.Say($"Saved {Path.GetFileName(output)}; it asks for the password when opened");
        return output;
    }

    /// <summary>Adds "3 / 10" centred at the bottom of every page.</summary>
    public static string AddPageNumbers(string input, IProgress<double> progress)
    {
        var output = OutputPaths.Next(input, ".pdf", "numbered");
        using var doc = PdfReader.Open(input, PdfDocumentOpenMode.Modify);
        var font = new XFont("Segoe UI", 9.5);
        var brush = new XSolidBrush(XColor.FromArgb(255, 90, 90, 96));
        int count = doc.PageCount;
        for (int i = 0; i < count; i++)
        {
            var page = doc.Pages[i];
            using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
            var text = $"{i + 1} / {count}";
            var size = gfx.MeasureString(text, font);
            gfx.DrawString(text, font, brush, new XPoint((gfx.PageSize.Width - size.Width) / 2, gfx.PageSize.Height - 13));
            progress.Report((i + 1) / (double)count);
        }
        doc.Save(output);
        return output;
    }

    static bool OpensWithPdfium(string input, string password)
    {
        try
        {
            lock (PdfTools.PdfiumGate)
            {
                using var reader = DocLib.Instance.GetDocReader(input, password, new PageDimensions(0.2));
                return reader.GetPageCount() > 0;
            }
        }
        catch
        {
            return false;
        }
    }

    static void RenderUnlocked(string input, string password, string output, IProgress<double> progress)
    {
        var images = new List<MagickImage>();
        try
        {
            lock (PdfTools.PdfiumGate)
            {
                using var reader = DocLib.Instance.GetDocReader(input, password, new PageDimensions(2.5));
                int count = reader.GetPageCount();
                for (int i = 0; i < count; i++)
                {
                    using var page = reader.GetPageReader(i);
                    var image = new MagickImage();
                    image.ReadPixels(page.GetImage(), new PixelReadSettings((uint)page.GetPageWidth(), (uint)page.GetPageHeight(), StorageType.Char, PixelMapping.BGRA));
                    ImageTools.FlattenOnWhite(image);
                    images.Add(image);
                    progress.Report((i + 1) / (double)count);
                }
            }
            PdfTools.ImagesToPdf(images, output);
        }
        finally
        {
            foreach (var image in images) image.Dispose();
        }
    }
}
