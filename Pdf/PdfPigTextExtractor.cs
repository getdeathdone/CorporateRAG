using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace CorporateRag.Pdf;

public sealed class PdfPigTextExtractor : IPdfTextExtractor
{
    public string ExtractText(string pdfPath)
    {
        using var document = PdfDocument.Open(pdfPath);

        return string.Join(
            Environment.NewLine + Environment.NewLine,
            document.GetPages().Select(ExtractPageText));
    }

    private static string ExtractPageText(Page page)
    {
        var words = page.GetWords().ToArray();
        if (words.Length == 0)
        {
            return page.Text;
        }

        return string.Join(
            ' ',
            words
                .OrderByDescending(word => Math.Round(word.BoundingBox.Bottom / 4) * 4)
                .ThenBy(word => word.BoundingBox.Left)
                .Select(word => word.Text));
    }
}
