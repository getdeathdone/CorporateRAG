using UglyToad.PdfPig;

namespace CorporateRag.Pdf;

public sealed class PdfPigTextExtractor : IPdfTextExtractor
{
    public string ExtractText(string pdfPath)
    {
        using var document = PdfDocument.Open(pdfPath);

        return string.Join(
            Environment.NewLine + Environment.NewLine,
            document.GetPages().Select(page => page.Text));
    }
}
